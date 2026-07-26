import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { createInterface, type Interface as ReadLineInterface } from "node:readline";

interface RpcErrorPayload {
  code?: number;
  message?: string;
  data?: unknown;
}

interface RpcMessage {
  id?: number | string;
  method?: string;
  params?: unknown;
  result?: unknown;
  error?: RpcErrorPayload;
}

interface PendingRequest {
  resolve(value: unknown): void;
  reject(error: Error): void;
  timer: NodeJS.Timeout;
}

interface LaunchCandidate {
  label: string;
  command: string;
  args: string[];
  initializeTimeoutMs: number;
  source: "installed" | "configured" | "pinned-fallback";
}

export interface CodexUsageResponses {
  rateLimitsResult: unknown;
  usageResult: unknown;
  warnings: string[];
}

export class CodexRpcError extends Error {
  readonly code: number | null;

  constructor(message: string, code: number | null = null) {
    super(message);
    this.name = "CodexRpcError";
    this.code = code;
  }
}

class StdioSession {
  private nextId = 1;
  private readonly pending = new Map<number, PendingRequest>();
  private readonly lines: ReadLineInterface;
  private closed = false;

  constructor(
    private readonly child: ChildProcessWithoutNullStreams,
    readonly candidate: LaunchCandidate,
    private readonly onClosed: (session: StdioSession) => void,
    private readonly onNotification: (method: string, params: unknown) => void,
  ) {
    this.lines = createInterface({
      input: child.stdout,
      crlfDelay: Number.POSITIVE_INFINITY,
    });
    this.lines.on("line", (line) => this.handleLine(line));
    child.once("spawn", () => {
      if (this.closed) {
        this.terminateChild();
      }
    });
    child.once("exit", (code, signal) => {
      const reason =
        signal !== null
          ? `Codex app-server stopped (${signal}).`
          : `Codex app-server exited with code ${String(code)}.`;
      this.close(new CodexRpcError(reason));
    });
    child.once("error", (error) => {
      this.close(
        new CodexRpcError(`Could not run Codex app-server: ${error.message}`),
      );
    });
    child.stdin.on("error", (error) => {
      this.close(
        new CodexRpcError(
          `Could not write to Codex app-server: ${error.message}`,
        ),
      );
    });

    // Drain stderr without copying it to logs. The app-server owns authentication
    // and its diagnostic stream may contain machine-specific details.
    child.stderr.resume();
  }

  async initialize(): Promise<void> {
    await this.request(
      "initialize",
      {
        clientInfo: {
          name: "usageapp_windows",
          title: "UsageApp for Windows",
          version: "0.1.0",
        },
        capabilities: {
          experimentalApi: true,
          optOutNotificationMethods: [],
        },
      },
      this.candidate.initializeTimeoutMs,
    );
    this.notify("initialized", {});
  }

  async readUsage(): Promise<CodexUsageResponses> {
    const rateLimitsPromise = this.request(
      "account/rateLimits/read",
      {},
      25_000,
    );
    const usagePromise = this.request("account/usage/read", {}, 25_000);
    let rateLimitsResult: unknown;
    try {
      rateLimitsResult = await rateLimitsPromise;
    } catch (error) {
      // The activity-history request is optional, but it was already sent in
      // parallel. Observe any later rejection before the client replaces this
      // incompatible session.
      void usagePromise.catch(() => undefined);
      throw error;
    }

    const warnings: string[] = [];
    let usageResult: unknown = null;
    try {
      usageResult = await usagePromise;
    } catch (error) {
      warnings.push(errorMessage(error));
    }

    return {
      rateLimitsResult,
      usageResult,
      warnings,
    };
  }

  request(
    method: string,
    params: unknown,
    timeoutMs: number,
  ): Promise<unknown> {
    if (this.closed || !this.child.stdin.writable) {
      return Promise.reject(
        new CodexRpcError("Codex app-server is not connected."),
      );
    }

    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(
          new CodexRpcError(
            `Codex app-server timed out while calling ${method}.`,
          ),
        );
      }, timeoutMs);
      timer.unref();
      this.pending.set(id, { resolve, reject, timer });

      this.write({ id, method, params });
    });
  }

  stop(): void {
    this.terminateChild();
    if (!this.closed) {
      this.close(new CodexRpcError("Codex app-server was stopped."));
    }
  }

  private notify(method: string, params: unknown): void {
    this.write({ method, params });
  }

  private write(message: RpcMessage): void {
    try {
      this.child.stdin.write(
        `${JSON.stringify(message)}\n`,
        (error?: Error | null) => {
          if (error) {
            this.close(
              new CodexRpcError(
                `Could not write to Codex app-server: ${error.message}`,
              ),
            );
          }
        },
      );
    } catch (error) {
      this.close(
        new CodexRpcError(
          `Could not write to Codex app-server: ${errorMessage(error)}`,
        ),
      );
    }
  }

  private handleLine(line: string): void {
    let message: RpcMessage;
    try {
      message = JSON.parse(line) as RpcMessage;
    } catch {
      return;
    }

    if (typeof message.id === "number") {
      const request = this.pending.get(message.id);
      if (request) {
        clearTimeout(request.timer);
        this.pending.delete(message.id);
        if (message.error) {
          request.reject(
            new CodexRpcError(
              message.error.message ?? "Codex app-server returned an error.",
              typeof message.error.code === "number"
                ? message.error.code
                : null,
            ),
          );
        } else {
          request.resolve(message.result);
        }
        return;
      }
    }

    if (message.id === undefined && typeof message.method === "string") {
      try {
        this.onNotification(message.method, message.params);
      } catch {
        // A local notification observer must never break the JSONL transport.
      }
      return;
    }

    // This monitor never starts turns and opts out of attestation, so it does
    // not support server-initiated requests. Return a protocol error rather
    // than leaving a request hanging.
    if (message.id !== undefined && typeof message.method === "string") {
      this.write({
        id: message.id,
        error: {
          code: -32_601,
          message: `Method not supported by UsageApp: ${message.method}`,
        },
      });
    }
  }

  private close(error: Error): void {
    if (this.closed) {
      return;
    }
    this.closed = true;
    this.terminateChild();
    this.lines.close();
    for (const request of this.pending.values()) {
      clearTimeout(request.timer);
      request.reject(error);
    }
    this.pending.clear();
    this.onClosed(this);
  }

  private terminateChild(): void {
    if (!this.child.killed) {
      try {
        this.child.kill();
      } catch {
        // The child may have failed before Windows assigned it a process ID.
      }
    }
  }
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function isAutoCommand(codexCommand: string): boolean {
  const normalized = codexCommand.trim();
  return normalized.length === 0 || normalized.toLowerCase() === "auto";
}

function normalizeCommand(codexCommand: string): string {
  return isAutoCommand(codexCommand) ? "auto" : codexCommand.trim();
}

function installedCandidate(): LaunchCandidate {
  return {
    label: "installed Codex CLI",
    command: "codex",
    args: ["app-server", "--listen", "stdio://"],
    initializeTimeoutMs: 15_000,
    source: "installed",
  };
}

function pinnedFallbackCandidate(): LaunchCandidate {
  const commandProcessor = process.env.ComSpec || "cmd.exe";
  return {
    label: "official pinned Codex npm fallback",
    command: commandProcessor,
    args: [
      "/d",
      "/s",
      "/c",
      "npx.cmd -y @openai/codex@0.145.0 app-server --listen stdio://",
    ],
    initializeTimeoutMs: 90_000,
    source: "pinned-fallback",
  };
}

function candidates(codexCommand: string): LaunchCandidate[] {
  if (!isAutoCommand(codexCommand)) {
    return [
      {
        label: "configured Codex CLI",
        command: codexCommand.trim(),
        args: ["app-server", "--listen", "stdio://"],
        initializeTimeoutMs: 15_000,
        source: "configured",
      },
    ];
  }

  return [installedCandidate(), pinnedFallbackCandidate()];
}

function isMandatoryRateLimitsCompatibilityError(error: unknown): boolean {
  if (error instanceof CodexRpcError && error.code === -32_601) {
    return true;
  }

  const message = errorMessage(error).toLowerCase();
  return (
    message.includes("method not found") ||
    message.includes("unknown method") ||
    message.includes("unimplemented") ||
    message.includes("not implemented") ||
    (message.includes("account/ratelimits/read") &&
      (message.includes("unsupported") || message.includes("not supported")))
  );
}

function connectionCancelledError(): CodexRpcError {
  return new CodexRpcError("Codex app-server connection was cancelled.");
}

async function spawnCandidate(
  candidate: LaunchCandidate,
  onClosed: (session: StdioSession) => void,
  onNotification: (method: string, params: unknown) => void,
  onCreated: (session: StdioSession) => void,
): Promise<StdioSession> {
  const child = spawn(candidate.command, candidate.args, {
    windowsHide: true,
    shell: false,
    stdio: ["pipe", "pipe", "pipe"],
    env: process.env,
  });
  const session = new StdioSession(
    child,
    candidate,
    onClosed,
    onNotification,
  );
  onCreated(session);

  try {
    await new Promise<void>((resolve, reject) => {
      child.once("spawn", resolve);
      child.once("error", reject);
    });
    await session.initialize();
    return session;
  } catch (error) {
    session.stop();
    throw error;
  }
}

export class CodexAppServerClient {
  private session: StdioSession | null = null;
  private connectingSession: StdioSession | null = null;
  private command = "";
  private connectPromise: Promise<StdioSession> | null = null;
  private connectionGeneration = 0;

  constructor(
    private readonly onRateLimitsUpdated: () => void = () => undefined,
  ) {}

  async readUsage(codexCommand: string): Promise<CodexUsageResponses> {
    const normalizedCommand = normalizeCommand(codexCommand);
    let session = await this.connect(normalizedCommand);
    try {
      return await session.readUsage();
    } catch (error) {
      if (
        isAutoCommand(normalizedCommand) &&
        session.candidate.source === "installed" &&
        isMandatoryRateLimitsCompatibilityError(error)
      ) {
        this.discardSession(session);
        session = await this.connectWithCandidates(normalizedCommand, [
          pinnedFallbackCandidate(),
        ]);
        try {
          return await session.readUsage();
        } catch (fallbackError) {
          this.discardSession(session);
          throw fallbackError;
        }
      }

      this.discardSession(session);
      throw error;
    }
  }

  stop(): void {
    this.connectionGeneration += 1;
    const activeSession = this.session;
    const initializingSession = this.connectingSession;
    this.session = null;
    this.connectingSession = null;
    this.connectPromise = null;
    activeSession?.stop();
    if (initializingSession && initializingSession !== activeSession) {
      initializingSession.stop();
    }
  }

  private async connect(codexCommand: string): Promise<StdioSession> {
    if (this.session && this.command === codexCommand) {
      return this.session;
    }
    if (this.connectPromise && this.command === codexCommand) {
      return this.connectPromise;
    }

    return this.connectWithCandidates(codexCommand, candidates(codexCommand));
  }

  private async connectWithCandidates(
    codexCommand: string,
    launchCandidates: LaunchCandidate[],
  ): Promise<StdioSession> {
    this.stop();
    this.command = codexCommand;
    const generation = this.connectionGeneration;
    const connection = this.tryCandidates(launchCandidates, generation);
    this.connectPromise = connection;
    try {
      const connected = await connection;
      if (generation !== this.connectionGeneration) {
        connected.stop();
        throw connectionCancelledError();
      }
      this.session = connected;
      return connected;
    } finally {
      if (this.connectPromise === connection) {
        this.connectPromise = null;
      }
    }
  }

  private async tryCandidates(
    launchCandidates: LaunchCandidate[],
    generation: number,
  ): Promise<StdioSession> {
    const failures: string[] = [];
    for (const candidate of launchCandidates) {
      if (generation !== this.connectionGeneration) {
        throw connectionCancelledError();
      }

      try {
        const connected = await spawnCandidate(
          candidate,
          (closed) => {
            if (this.session === closed) {
              this.session = null;
            }
            if (this.connectingSession === closed) {
              this.connectingSession = null;
            }
          },
          (method) => {
            if (method === "account/rateLimits/updated") {
              this.onRateLimitsUpdated();
            }
          },
          (created) => {
            if (generation === this.connectionGeneration) {
              this.connectingSession = created;
            } else {
              created.stop();
            }
          },
        );
        if (this.connectingSession === connected) {
          this.connectingSession = null;
        }
        if (generation !== this.connectionGeneration) {
          connected.stop();
          throw connectionCancelledError();
        }
        return connected;
      } catch (error) {
        if (generation !== this.connectionGeneration) {
          throw connectionCancelledError();
        }
        failures.push(`${candidate.label}: ${errorMessage(error)}`);
      }
    }

    throw new CodexRpcError(
      `Unable to start Codex. ${failures.join(" ")}`,
    );
  }

  private discardSession(session: StdioSession): void {
    if (this.session === session) {
      this.session = null;
    }
    if (this.connectingSession === session) {
      this.connectingSession = null;
    }
    session.stop();
  }
}

export function looksLikeAuthenticationError(error: unknown): boolean {
  const message = errorMessage(error).toLowerCase();
  return (
    message.includes("not logged in") ||
    message.includes("login required") ||
    message.includes("authentication") ||
    message.includes("unauthorized") ||
    message.includes("sign in") ||
    message.includes("401")
  );
}

export { errorMessage };
