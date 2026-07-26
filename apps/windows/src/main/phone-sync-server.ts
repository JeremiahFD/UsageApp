import type {
  AppSettings,
  PairRequest,
  PairResponse,
  UsageSnapshot,
} from "@usageapp/core";
import {
  createHash,
  randomBytes,
  randomInt,
  randomUUID,
  timingSafeEqual,
} from "node:crypto";
import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import { networkInterfaces } from "node:os";
import { dirname, join } from "node:path";
import type {
  PairingCodeInfo,
  PhoneSyncStatus,
} from "../shared/desktop";

interface TokenRecord {
  id: string;
  deviceName: string;
  tokenSha256: string;
  createdAt: string;
}

interface PairingChallenge {
  codeSha256: string;
  expiresAtMs: number;
  attemptsRemaining: number;
}

interface StoredPhoneSync {
  version: 1;
  tokens: TokenRecord[];
}

const MAX_BODY_BYTES = 4_096;
const PAIRING_LIFETIME_MS = 10 * 60_000;

function sha256(value: string): string {
  return createHash("sha256").update(value, "utf8").digest("hex");
}

function secureHashEqual(leftHex: string, rightHex: string): boolean {
  const left = Buffer.from(leftHex, "hex");
  const right = Buffer.from(rightHex, "hex");
  return left.length === right.length && timingSafeEqual(left, right);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function validTokenRecord(value: unknown): value is TokenRecord {
  return (
    isRecord(value) &&
    typeof value.id === "string" &&
    typeof value.deviceName === "string" &&
    typeof value.tokenSha256 === "string" &&
    /^[a-f0-9]{64}$/i.test(value.tokenSha256) &&
    typeof value.createdAt === "string"
  );
}

function pairingRequest(value: unknown): PairRequest | null {
  if (
    !isRecord(value) ||
    typeof value.code !== "string" ||
    !/^\d{6}$/.test(value.code)
  ) {
    return null;
  }
  const deviceName =
    typeof value.deviceName === "string"
      ? value.deviceName.trim().slice(0, 80)
      : "";
  return {
    code: value.code,
    deviceName: deviceName || "Android device",
  };
}

async function readJsonBody(request: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  let length = 0;
  for await (const chunk of request) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    length += buffer.length;
    if (length > MAX_BODY_BYTES) {
      throw new Error("Request body is too large.");
    }
    chunks.push(buffer);
  }
  if (chunks.length === 0) {
    return null;
  }
  return JSON.parse(Buffer.concat(chunks).toString("utf8")) as unknown;
}

function json(
  response: ServerResponse,
  status: number,
  body: unknown,
): void {
  const payload = JSON.stringify(body);
  response.writeHead(status, {
    "Cache-Control": "no-store",
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(payload),
    "X-Content-Type-Options": "nosniff",
  });
  response.end(payload);
}

interface LanCandidate {
  address: string;
  score: number;
}

function isRfc1918Ipv4(address: string): boolean {
  const parts = address.split(".").map(Number);
  if (
    parts.length !== 4 ||
    parts.some((part) => !Number.isInteger(part) || part < 0 || part > 255)
  ) {
    return false;
  }
  const [first, second] = parts;
  return (
    first === 10 ||
    (first === 172 && second !== undefined && second >= 16 && second <= 31) ||
    (first === 192 && second === 168)
  );
}

function preferredLanAddress(): string | null {
  const candidates: LanCandidate[] = [];
  for (const [interfaceName, entries] of Object.entries(networkInterfaces())) {
    // Do not expose the sync service on VPNs, tunnels, WSL, Hyper-V, or other
    // virtual adapters. The first MVP is intentionally a physical private-LAN
    // feature.
    if (
      /vpn|nord|tailscale|zerotier|wireguard|tunnel|virtual|vethernet|wsl|hyper-v|bluetooth/i.test(
        interfaceName,
      )
    ) {
      continue;
    }
    for (const entry of entries ?? []) {
      if (
        entry.family !== "IPv4" ||
        entry.internal ||
        !isRfc1918Ipv4(entry.address)
      ) {
        continue;
      }
      let score = /wi-?fi|wireless|wlan/i.test(interfaceName)
        ? 300
        : /ethernet/i.test(interfaceName)
          ? 200
          : 100;
      if (entry.address.startsWith("192.168.")) score += 30;
      else if (entry.address.startsWith("10.")) score += 20;
      else score += 10;
      candidates.push({ address: entry.address, score });
    }
  }
  candidates.sort(
    (left, right) =>
      right.score - left.score || left.address.localeCompare(right.address),
  );
  return candidates.at(0)?.address ?? null;
}

export class PhoneSyncServer {
  private readonly storagePath: string;
  private server: Server | null = null;
  private port: number;
  private enabled = false;
  private listening = false;
  private host: string | null = null;
  private lastError: string | null = null;
  private challenge: PairingChallenge | null = null;
  private tokens: TokenRecord[] = [];
  private loaded = false;
  private tokenMutationQueue: Promise<void> = Promise.resolve();

  constructor(
    userDataPath: string,
    private readonly getSnapshot: () => UsageSnapshot | null,
    private readonly onStatusChanged: () => void,
    initialPort: number,
  ) {
    this.storagePath = join(userDataPath, "phone-sync.json");
    this.port = initialPort;
  }

  async configure(settings: AppSettings): Promise<PhoneSyncStatus> {
    await this.load();
    const nextHost = settings.phoneSyncEnabled ? preferredLanAddress() : null;
    const mustRestart =
      this.server !== null &&
      (this.port !== settings.phoneSyncPort || this.host !== nextHost);
    this.enabled = settings.phoneSyncEnabled;
    this.port = settings.phoneSyncPort;
    this.host = nextHost;

    if (!this.enabled) {
      this.challenge = null;
      await this.stopServer();
      this.lastError = null;
      return this.status();
    }

    if (mustRestart) {
      this.challenge = null;
      await this.stopServer();
    }
    if (this.server === null) {
      await this.startServer();
    }
    return this.status();
  }

  status(): PhoneSyncStatus {
    return {
      enabled: this.enabled,
      listening: this.listening,
      port: this.port,
      addresses:
        this.listening && this.host
          ? [`http://${this.host}:${this.port}`]
          : [],
      pairedDeviceCount: this.tokens.length,
      pairingCodeActive:
        this.challenge !== null &&
        Date.now() < this.challenge.expiresAtMs &&
        this.challenge.attemptsRemaining > 0,
      error: this.lastError,
    };
  }

  createPairingCode(): PairingCodeInfo {
    if (!this.enabled || !this.listening) {
      throw new Error(
        "Phone sync must be enabled and listening before pairing.",
      );
    }
    const code = randomInt(0, 1_000_000).toString().padStart(6, "0");
    const expiresAtMs = Date.now() + PAIRING_LIFETIME_MS;
    this.challenge = {
      codeSha256: sha256(code),
      expiresAtMs,
      attemptsRemaining: 5,
    };
    this.onStatusChanged();
    return {
      code,
      expiresAt: new Date(expiresAtMs).toISOString(),
      addresses: this.host ? [`http://${this.host}:${this.port}`] : [],
      port: this.port,
    };
  }

  async revokeAllTokens(): Promise<void> {
    await this.load();
    this.challenge = null;
    await this.mutateTokens(() => []);
    this.onStatusChanged();
  }

  async stop(): Promise<void> {
    this.enabled = false;
    this.challenge = null;
    await this.stopServer();
  }

  private async load(): Promise<void> {
    if (this.loaded) {
      return;
    }
    this.loaded = true;
    try {
      const parsed = JSON.parse(
        await readFile(this.storagePath, "utf8"),
      ) as unknown;
      if (
        isRecord(parsed) &&
        parsed.version === 1 &&
        Array.isArray(parsed.tokens)
      ) {
        this.tokens = parsed.tokens.filter(validTokenRecord);
      }
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "ENOENT") {
        console.warn("Could not load phone pairing records:", error);
      }
      this.tokens = [];
    }
  }

  private async persist(tokens: readonly TokenRecord[]): Promise<void> {
    await mkdir(dirname(this.storagePath), { recursive: true });
    const temporaryPath = `${this.storagePath}.tmp`;
    const stored: StoredPhoneSync = {
      version: 1,
      tokens: [...tokens],
    };
    await writeFile(
      temporaryPath,
      `${JSON.stringify(stored, null, 2)}\n`,
      "utf8",
    );
    await rename(temporaryPath, this.storagePath);
  }

  private async mutateTokens(
    mutate: (current: readonly TokenRecord[]) => TokenRecord[],
  ): Promise<void> {
    const operation = this.tokenMutationQueue.then(async () => {
      const nextTokens = mutate(this.tokens);
      await this.persist(nextTokens);
      this.tokens = nextTokens;
    });
    this.tokenMutationQueue = operation.catch(() => {
      // A failed durable write must not poison later revocation attempts.
    });
    await operation;
  }

  private async startServer(): Promise<void> {
    if (!this.host) {
      this.listening = false;
      this.lastError =
        "No physical private Wi-Fi or Ethernet address is available.";
      this.onStatusChanged();
      return;
    }
    const server = createServer((request, response) => {
      void this.handleRequest(request, response).catch((error) => {
        json(response, 500, {
          error: "The local sync server could not complete the request.",
        });
        console.warn("Phone sync request failed:", error);
      });
    });
    this.server = server;

    server.on("error", (error) => {
      this.listening = false;
      this.lastError = error.message;
      this.onStatusChanged();
    });

    try {
      await new Promise<void>((resolve, reject) => {
        const onInitialError = (error: Error) => {
          server.off("listening", onListening);
          reject(error);
        };
        const onListening = () => {
          server.off("error", onInitialError);
          resolve();
        };
        server.once("error", onInitialError);
        server.once("listening", onListening);
        server.listen({
          host: this.host,
          port: this.port,
          exclusive: true,
        });
      });
      this.listening = true;
      this.lastError = null;
    } catch (error) {
      this.listening = false;
      this.lastError =
        error instanceof Error ? error.message : "Could not start phone sync.";
      this.server = null;
      server.close();
    }
  }

  private async stopServer(): Promise<void> {
    const server = this.server;
    this.server = null;
    this.listening = false;
    if (!server) {
      return;
    }
    await new Promise<void>((resolve) => {
      server.close(() => resolve());
      server.closeAllConnections();
    });
  }

  private async handleRequest(
    request: IncomingMessage,
    response: ServerResponse,
  ): Promise<void> {
    const url = new URL(request.url ?? "/", `http://127.0.0.1:${this.port}`);

    if (url.pathname === "/v1/pair") {
      if (request.method !== "POST") {
        response.setHeader("Allow", "POST");
        json(response, 405, { error: "Method not allowed." });
        return;
      }
      await this.handlePair(request, response);
      return;
    }

    if (url.pathname === "/v1/snapshot") {
      if (request.method !== "GET") {
        response.setHeader("Allow", "GET");
        json(response, 405, { error: "Method not allowed." });
        return;
      }
      if (!this.authorized(request)) {
        response.setHeader("WWW-Authenticate", "Bearer");
        json(response, 401, { error: "A valid pairing token is required." });
        return;
      }
      const snapshot = this.getSnapshot();
      if (!snapshot) {
        json(response, 503, { error: "Usage data is not available yet." });
        return;
      }
      json(response, 200, snapshot);
      return;
    }

    if (url.pathname === "/v1/device") {
      if (request.method !== "DELETE") {
        response.setHeader("Allow", "DELETE");
        json(response, 405, { error: "Method not allowed." });
        return;
      }
      const record = this.authorizedRecord(request);
      if (!record) {
        response.setHeader("WWW-Authenticate", "Bearer");
        json(response, 401, { error: "A valid pairing token is required." });
        return;
      }
      await this.mutateTokens((current) =>
        current.filter((candidate) => candidate.id !== record.id),
      );
      this.onStatusChanged();
      response.writeHead(204, {
        "Cache-Control": "no-store",
        "X-Content-Type-Options": "nosniff",
      });
      response.end();
      return;
    }

    json(response, 404, { error: "Not found." });
  }

  private async handlePair(
    request: IncomingMessage,
    response: ServerResponse,
  ): Promise<void> {
    let body: unknown;
    try {
      body = await readJsonBody(request);
    } catch {
      json(response, 400, { error: "Expected a small JSON request body." });
      return;
    }
    const pair = pairingRequest(body);
    const challenge = this.challenge;
    if (
      !pair ||
      !challenge ||
      Date.now() >= challenge.expiresAtMs ||
      challenge.attemptsRemaining <= 0
    ) {
      this.challenge = null;
      this.onStatusChanged();
      json(response, 401, {
        error: "The pairing code is invalid or has expired.",
      });
      return;
    }

    const matches = secureHashEqual(
      challenge.codeSha256,
      sha256(pair.code),
    );
    challenge.attemptsRemaining -= 1;
    if (!matches) {
      if (challenge.attemptsRemaining <= 0) {
        this.challenge = null;
        this.onStatusChanged();
      }
      json(response, 401, { error: "The pairing code is invalid." });
      return;
    }

    // Pairing codes are one-time. Only a SHA-256 verifier for the high-entropy
    // bearer token is persisted; the raw token is returned exactly once.
    this.challenge = null;
    const token = randomBytes(32).toString("base64url");
    const deviceId = randomUUID();
    const tokenRecord: TokenRecord = {
      id: deviceId,
      deviceName: pair.deviceName,
      tokenSha256: sha256(token),
      createdAt: new Date().toISOString(),
    };
    await this.mutateTokens((current) => [...current, tokenRecord]);
    this.onStatusChanged();

    const result: PairResponse = { token, deviceId };
    json(response, 201, result);
  }

  private authorized(request: IncomingMessage): boolean {
    return this.authorizedRecord(request) !== null;
  }

  private authorizedRecord(request: IncomingMessage): TokenRecord | null {
    const authorization = request.headers.authorization;
    if (!authorization?.startsWith("Bearer ")) {
      return null;
    }
    const token = authorization.slice("Bearer ".length).trim();
    if (token.length < 32 || token.length > 256) {
      return null;
    }
    const presented = sha256(token);
    return this.tokens.find((record) =>
      secureHashEqual(record.tokenSha256, presented),
    ) ?? null;
  }
}
