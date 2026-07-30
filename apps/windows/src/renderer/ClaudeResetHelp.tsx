import type { UsageSnapshot } from "@usageapp/core";
import { useState, type ReactNode } from "react";

/**
 * True when quota is coming from the Claude desktop app's own record, which
 * reports utilization but no reset times. A Claude Code terminal session is
 * the only source of those, so this is the one state worth guiding the user
 * through.
 */
export function needsResetTimes(snapshot: UsageSnapshot | null): boolean {
  return (
    snapshot !== null &&
    snapshot.windows.length > 0 &&
    snapshot.windows.every((window) => window.resetsAt === null)
  );
}

function CopyCommandButton(): ReactNode {
  const [copied, setCopied] = useState(false);

  return (
    <span className="help-command">
      <code>claude</code>
      <button
        type="button"
        onClick={() => {
          void navigator.clipboard.writeText("claude").then(
            () => {
              setCopied(true);
              window.setTimeout(() => setCopied(false), 1_600);
            },
            () => {
              // Clipboard access can be denied. The command stays on screen
              // to type by hand, so this needs no error state.
            },
          );
        }}
      >
        {copied ? "Copied" : "Copy"}
      </button>
    </span>
  );
}

/**
 * Collapsed by default: the app still works without this, so it should read
 * as an optional improvement rather than a problem to fix.
 */
export function ClaudeResetHelp({
  variant = "flyout",
}: {
  variant?: "flyout" | "dashboard";
}): ReactNode {
  return (
    <details className={`help-callout ${variant}`}>
      <summary>
        <strong>Reset times need a terminal session</strong>
        <span>One time · about 30 seconds</span>
      </summary>
      <div className="help-body">
        <p>
          Your usage percentages update on their own. Reset times come only
          from Claude Code&apos;s status line, which runs in a terminal rather
          than inside the desktop app.
        </p>
        <ol className="help-steps">
          <li>Open Windows Terminal, PowerShell, or Command Prompt.</li>
          <li>
            Run <CopyCommandButton />
          </li>
          <li>Send any prompt, then wait about five seconds.</li>
          <li>Return here. Reset times appear and stay saved.</li>
        </ol>
        <p className="help-note">
          Worth repeating about once per reset window. Percentages keep
          updating whether or not you do.
        </p>
      </div>
    </details>
  );
}
