import {
  decodeUsageSnapshot,
  type PairResponse,
  type UsageSnapshot,
} from "@usageapp/core";

const REQUEST_TIMEOUT_MS = 12_000;

type JsonObject = Record<string, unknown>;

export class ApiError extends Error {
  readonly status: number | null;

  constructor(message: string, status: number | null = null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

function isObject(value: unknown): value is JsonObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isPrivateIpv4(hostname: string): boolean {
  if (!/^\d{1,3}(?:\.\d{1,3}){3}$/.test(hostname)) {
    return false;
  }

  const segments = hostname.split(".");
  const numbers = segments.map((segment) => Number(segment));
  if (
    numbers.some(
      (segment) =>
        !Number.isInteger(segment) || segment < 0 || segment > 255,
    )
  ) {
    return false;
  }

  const first = numbers[0];
  const second = numbers[1];
  if (first === undefined || second === undefined) {
    return false;
  }

  return (
    first === 10 ||
    (first === 172 && second >= 16 && second <= 31) ||
    (first === 192 && second === 168)
  );
}

export function normalizePrivateServerUrl(rawUrl: string): string {
  const candidate = rawUrl.trim();
  if (!candidate) {
    throw new ApiError("Enter the address shown by the Windows app.");
  }

  const withProtocol = candidate.includes("://")
    ? candidate
    : `http://${candidate}`;

  let url: URL;
  try {
    url = new URL(withProtocol);
  } catch {
    throw new ApiError(
      "Enter a valid LAN address, such as http://192.168.1.42:43120.",
    );
  }

  if (url.protocol !== "http:") {
    throw new ApiError(
      "The local viewer currently supports HTTP addresses on a trusted private network only.",
    );
  }

  if (url.username || url.password || url.search || url.hash) {
    throw new ApiError(
      "Use only the PC address and port; remove credentials, query text, or fragments.",
    );
  }

  if (url.pathname !== "/" && url.pathname !== "") {
    throw new ApiError(
      "Use the PC's base address without an API path at the end.",
    );
  }

  if (!isPrivateIpv4(url.hostname)) {
    throw new ApiError(
      "For safety, use the numeric private LAN IP shown by the Windows app, such as 192.168.1.42.",
    );
  }

  return url.origin;
}

async function readResponseBody(response: Response): Promise<unknown> {
  const text = await response.text();
  if (!text) {
    return null;
  }

  try {
    return JSON.parse(text) as unknown;
  } catch {
    return text;
  }
}

function responseErrorMessage(body: unknown, fallback: string): string {
  if (typeof body === "string" && body.trim()) {
    return body.trim();
  }

  if (isObject(body)) {
    const message = body.message ?? body.error;
    if (typeof message === "string" && message.trim()) {
      return message.trim();
    }
  }

  return fallback;
}

async function fetchWithTimeout(
  url: string,
  init: RequestInit,
): Promise<Response> {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

  try {
    return await fetch(url, { ...init, signal: controller.signal });
  } catch (error) {
    if (error instanceof Error && error.name === "AbortError") {
      throw new ApiError(
        "The PC did not respond in time. Check that both devices are on the same Wi-Fi.",
      );
    }

    throw new ApiError(
      "Could not reach the PC. Check the address, Windows app, firewall, and Wi-Fi connection.",
    );
  } finally {
    clearTimeout(timeoutId);
  }
}

export async function pairWithServer(input: {
  serverUrl: string;
  code: string;
  deviceName: string;
}): Promise<PairResponse> {
  const serverUrl = normalizePrivateServerUrl(input.serverUrl);
  const response = await fetchWithTimeout(`${serverUrl}/v1/pair`, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      code: input.code,
      deviceName: input.deviceName,
    }),
  });
  const body = await readResponseBody(response);

  if (!response.ok) {
    const fallback =
      response.status === 401 || response.status === 403
        ? "That pairing code was rejected or has expired."
        : `Pairing failed (HTTP ${response.status}).`;
    throw new ApiError(responseErrorMessage(body, fallback), response.status);
  }

  if (!isObject(body)) {
    throw new ApiError("The PC returned an invalid pairing response.");
  }

  const token = body.token ?? body.accessToken ?? body.bearerToken;
  if (typeof token !== "string" || token.length < 16) {
    throw new ApiError("The PC did not return a valid device token.");
  }
  if (typeof body.deviceId !== "string" || body.deviceId.length < 8) {
    throw new ApiError("The PC did not return a valid device identifier.");
  }

  return { token, deviceId: body.deviceId };
}

export async function fetchSnapshot(input: {
  serverUrl: string;
  token: string;
}): Promise<UsageSnapshot> {
  const serverUrl = normalizePrivateServerUrl(input.serverUrl);
  const response = await fetchWithTimeout(`${serverUrl}/v1/snapshot`, {
    method: "GET",
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${input.token}`,
    },
  });
  const body = await readResponseBody(response);

  if (!response.ok) {
    const fallback =
      response.status === 401 || response.status === 403
        ? "This phone is no longer authorized. Pair it again in Settings."
        : `Usage refresh failed (HTTP ${response.status}).`;
    throw new ApiError(responseErrorMessage(body, fallback), response.status);
  }

  const snapshot = decodeUsageSnapshot(body);
  if (!snapshot) {
    throw new ApiError("The PC returned usage data in an unsupported format.");
  }

  return snapshot;
}

export async function revokeDevice(input: {
  serverUrl: string;
  token: string;
}): Promise<void> {
  const serverUrl = normalizePrivateServerUrl(input.serverUrl);
  const response = await fetchWithTimeout(`${serverUrl}/v1/device`, {
    method: "DELETE",
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${input.token}`,
    },
  });
  if (!response.ok && response.status !== 401 && response.status !== 403) {
    const body = await readResponseBody(response);
    throw new ApiError(
      responseErrorMessage(
        body,
        `Device revocation failed (HTTP ${response.status}).`,
      ),
      response.status,
    );
  }
}
