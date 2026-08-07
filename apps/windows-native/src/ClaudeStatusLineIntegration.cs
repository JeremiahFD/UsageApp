using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace UsageApp.Native
{
    internal sealed class ClaudeIntegrationResult
    {
        public bool Succeeded { get; set; }
        public bool Connected { get; set; }
        public bool Conflict { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Explicit, reversible status-line installation for Claude Code. It owns
    /// exactly the `statusLine` setting, preserves a pre-existing command, and
    /// restores it only when that setting still matches the value it installed.
    /// It never reads Claude credentials, conversations, or telemetry content.
    /// </summary>
    internal sealed class ClaudeStatusLineIntegration
    {
        private const int StateVersion = 1;
        internal const int DefaultPort = 38181;
        private readonly string statePath;
        private readonly string wrapperDirectory;
        private readonly string wrapperPath;
        private readonly string priorCommandPath;
        private readonly string settingsPath;
        private readonly string settingsPathError;
        private readonly ClaudeStatusLineReceiver receiver;

        internal ClaudeStatusLineIntegration(ClaudeStatusLineReceiver receiver)
            : this(receiver, DefaultStateDirectory(), ResolveCurrentSettingsPath())
        {
        }

        internal ClaudeStatusLineIntegration(
            ClaudeStatusLineReceiver receiver,
            string stateDirectory,
            string explicitSettingsPath)
            : this(receiver, stateDirectory, explicitSettingsPath, null)
        {
        }

        private ClaudeStatusLineIntegration(
            ClaudeStatusLineReceiver receiver,
            string stateDirectory,
            SettingsPathResolution settingsResolution)
            : this(
                receiver,
                stateDirectory,
                settingsResolution == null ? null : settingsResolution.Path,
                settingsResolution == null ? null : settingsResolution.Error)
        {
        }

        private ClaudeStatusLineIntegration(
            ClaudeStatusLineReceiver receiver,
            string stateDirectory,
            string explicitSettingsPath,
            string explicitSettingsPathError)
        {
            if (receiver == null) throw new ArgumentNullException("receiver");
            if (string.IsNullOrWhiteSpace(stateDirectory))
            {
                throw new ArgumentException(
                    "An integration state directory is required.",
                    "stateDirectory");
            }
            this.receiver = receiver;
            statePath = Path.Combine(stateDirectory, "claude-statusline-state.json");
            wrapperDirectory = Path.Combine(stateDirectory, "claude-statusline");
            wrapperPath = Path.Combine(wrapperDirectory, "statusline-wrapper.ps1");
            priorCommandPath = Path.Combine(wrapperDirectory, "prior-statusline.cmd");
            settingsPath = explicitSettingsPath;
            settingsPathError = explicitSettingsPathError;
        }

        internal static string ResolveSettingsPathForTest(
            string claudeConfigDirectory,
            string userProfile)
        {
            SettingsPathResolution resolution = ResolveSettingsPath(
                claudeConfigDirectory,
                userProfile);
            return resolution.Path;
        }

        internal static string LoadSavedToken()
        {
            try
            {
                string path = Path.Combine(DefaultStateDirectory(),
                    "claude-statusline-state.json");
                Journal journal = new JavaScriptSerializer().Deserialize<Journal>(
                    File.ReadAllText(path, Encoding.UTF8));
                return journal != null && journal.Version == StateVersion
                    && ClaudeStatusLineReceiver.IsValidPathToken(journal.PathToken)
                    ? journal.PathToken : null;
            }
            catch { return null; }
        }

        private static string DefaultStateDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "UsageAppNative");
        }

        private static SettingsPathResolution ResolveCurrentSettingsPath()
        {
            return ResolveSettingsPath(
                Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        private static SettingsPathResolution ResolveSettingsPath(
            string claudeConfigDirectory,
            string userProfile)
        {
            try
            {
                string root;
                if (string.IsNullOrWhiteSpace(claudeConfigDirectory))
                {
                    if (string.IsNullOrWhiteSpace(userProfile))
                    {
                        throw new InvalidOperationException(
                            "The Windows user-profile directory is unavailable.");
                    }
                    root = Path.Combine(userProfile, ".claude");
                }
                else
                {
                    root = claudeConfigDirectory.Trim();
                    if (root.Length >= 2
                        && root[0] == '"'
                        && root[root.Length - 1] == '"')
                    {
                        root = root.Substring(1, root.Length - 2);
                    }
                    root = Environment.ExpandEnvironmentVariables(root);
                    if (root == "~"
                        || root.StartsWith("~\\", StringComparison.Ordinal)
                        || root.StartsWith("~/", StringComparison.Ordinal))
                    {
                        if (string.IsNullOrWhiteSpace(userProfile))
                        {
                            throw new InvalidOperationException(
                                "The Windows user-profile directory is unavailable.");
                        }
                        root = root.Length == 1
                            ? userProfile
                            : Path.Combine(userProfile, root.Substring(2));
                    }
                }

                root = Path.GetFullPath(root);
                return new SettingsPathResolution
                {
                    Path = Path.Combine(root, "settings.json")
                };
            }
            catch
            {
                return new SettingsPathResolution
                {
                    Error = "CLAUDE_CONFIG_DIR is not a valid Windows folder, so UsageApp left Claude settings unchanged."
                };
            }
        }

        internal ClaudeIntegrationResult Connect()
        {
            ClaudeIntegrationResult settingsPathFailure = SettingsPathFailure();
            if (settingsPathFailure != null)
            {
                receiver.Stop();
                return settingsPathFailure;
            }

            Journal existing = LoadJournal();
            if (existing != null && existing.InstalledStatusLine != null)
            {
                return Inspect(existing);
            }
            try
            {
                receiver.Start();
            }
            catch (Exception receiverError)
            {
                receiver.Stop();
                return Result(false, false, false,
                    ReceiverStartFailure(receiverError, false));
            }
            if (string.IsNullOrEmpty(receiver.StatusLineEndpoint))
            {
                receiver.Stop();
                return Result(false, false, false,
                    "The local Claude receiver did not start.");
            }

            Dictionary<string, object> settings;
            bool existed;
            string error;
            if (!TryReadSettings(out settings, out existed, out error))
            {
                receiver.Stop();
                return Result(false, false, false, error);
            }

            object previous;
            bool hadStatusLine = settings.TryGetValue("statusLine", out previous);
            Dictionary<string, object> previousObject = previous as Dictionary<string, object>;
            if (hadStatusLine && previousObject == null)
            {
                receiver.Stop();
                return Result(false, false, true,
                    "Claude's existing statusLine setting is not an object, so UsageApp left it unchanged.");
            }

            string priorCommand = null;
            if (previousObject != null)
            {
                object command;
                if (previousObject.TryGetValue("command", out command))
                {
                    priorCommand = command as string;
                }
            }
            if (!string.IsNullOrEmpty(priorCommand)
                && priorCommand.IndexOf(wrapperPath, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                receiver.Stop();
                return Result(false, false, true,
                    "Claude already points to a UsageApp wrapper without a matching restoration record, so it was left unchanged.");
            }

            Journal preparedJournal = null;
            try
            {
                Directory.CreateDirectory(wrapperDirectory);
                if (!string.IsNullOrWhiteSpace(priorCommand))
                {
                    File.WriteAllText(priorCommandPath,
                        priorCommand,
                        new UTF8Encoding(false));
                }
                else if (File.Exists(priorCommandPath))
                {
                    File.Delete(priorCommandPath);
                }
                File.WriteAllText(wrapperPath,
                    WrapperScript(receiver.StatusLineEndpoint,
                        string.IsNullOrWhiteSpace(priorCommand) ? null : priorCommandPath),
                    new UTF8Encoding(false));

                Dictionary<string, object> installed = previousObject == null
                    ? new Dictionary<string, object>()
                    : CloneObject(previousObject);
                installed["type"] = "command";
                installed["command"] = "powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""
                    + wrapperPath.Replace("\"", "\"\"") + "\"";
                if (!installed.ContainsKey("refreshInterval")) installed["refreshInterval"] = 5;

                preparedJournal = new Journal();
                preparedJournal.Version = StateVersion;
                preparedJournal.SettingsPath = settingsPath;
                preparedJournal.SettingsFileExisted = existed;
                preparedJournal.HadStatusLine = hadStatusLine;
                preparedJournal.PriorStatusLine = previous;
                preparedJournal.InstalledStatusLine = installed;
                preparedJournal.PathToken = receiver.PathToken;
                SaveJournal(preparedJournal); // save restoration details before mutating Claude settings
                settings["statusLine"] = installed;
                WriteSettings(settingsPath, settings);
                return Result(true, true, false,
                    "Claude monitoring is ready. Close and start a new Claude Code session to send its first quota update.");
            }
            catch (Exception)
            {
                return RecoverFailedConnect(preparedJournal);
            }
        }

        /// <summary>
        /// Starts only a previously journaled integration. Unlike Connect it
        /// never creates or edits Claude settings on application startup.
        /// </summary>
        internal ClaudeIntegrationResult Resume()
        {
            ClaudeIntegrationResult settingsPathFailure = SettingsPathFailure();
            if (settingsPathFailure != null)
            {
                receiver.Stop();
                return settingsPathFailure;
            }
            Journal existing = LoadJournal();
            return existing == null || existing.InstalledStatusLine == null
                ? Result(true, false, false,
                    "Claude monitoring is not connected.")
                : Inspect(existing);
        }

        internal ClaudeIntegrationResult Disconnect()
        {
            Journal journal = LoadJournal();
            if (journal == null || journal.InstalledStatusLine == null)
            {
                receiver.Stop();
                return Result(true, false, false,
                    "Claude monitoring is not connected.");
            }
            ClaudeIntegrationResult settingsPathFailure = SettingsPathFailure();
            if (settingsPathFailure != null)
            {
                receiver.Stop();
                return Result(false, false, true,
                    settingsPathFailure.Message
                    + " UsageApp stopped its receiver but retained the restoration record.");
            }
            if (!SamePath(journal.SettingsPath, settingsPath))
            {
                receiver.Stop();
                return Result(false, false, true,
                    "CLAUDE_CONFIG_DIR now points to a different Claude profile. UsageApp stopped its receiver and retained the original restoration record; restore the earlier profile location before disconnecting again.");
            }
            Dictionary<string, object> settings;
            bool existed;
            string error;
            if (!TryReadSettings(out settings, out existed, out error))
            {
                receiver.Stop();
                return Result(false, false, true,
                    error + " UsageApp stopped its receiver and retained the restoration record.");
            }
            object current = null;
            if (!settings.TryGetValue("statusLine", out current)
                || !SemanticJsonEquals(current, journal.InstalledStatusLine))
            {
                receiver.Stop();
                return Result(false, false, true,
                    "Claude's statusLine setting changed after connection. UsageApp stopped its receiver, left that setting untouched, and retained its restoration record.");
            }

            bool restored = false;
            try
            {
                if (journal.HadStatusLine) settings["statusLine"] = journal.PriorStatusLine;
                else settings.Remove("statusLine");
                if (!journal.SettingsFileExisted && settings.Count == 0)
                {
                    if (File.Exists(settingsPath)) File.Delete(settingsPath);
                }
                else WriteSettings(settingsPath, settings);
                restored = true;
                receiver.Stop();
            }
            catch (Exception)
            {
                return ResolveFailedDisconnect(journal);
            }

            if (restored && CleanupPreparedArtifacts())
            {
                return Result(true, false, false,
                    "Claude monitoring was disconnected and the previous status line was restored.");
            }
            return Result(false, false, true,
                "Claude monitoring was disconnected and the previous status line was restored, but UsageApp could not remove every local integration file. The restoration record was retained for recovery.");
        }

        private ClaudeIntegrationResult Inspect(Journal journal)
        {
            if (!SamePath(journal.SettingsPath, settingsPath))
            {
                receiver.Stop();
                return Result(false, false, true,
                    "CLAUDE_CONFIG_DIR points to a different Claude profile than UsageApp's restoration record, so the receiver was not started.");
            }
            if (!ClaudeStatusLineReceiver.IsValidPathToken(journal.PathToken)
                || !string.Equals(journal.PathToken, receiver.PathToken,
                    StringComparison.Ordinal))
            {
                receiver.Stop();
                return Result(false, false, true,
                    "UsageApp's Claude receiver identity changed, so the existing Claude setting was left untouched.");
            }
            Dictionary<string, object> settings;
            bool existed;
            string error;
            if (!TryReadSettings(out settings, out existed, out error))
            {
                receiver.Stop();
                return Result(false, false, false, error);
            }
            object current;
            if (!settings.TryGetValue("statusLine", out current)
                || !SemanticJsonEquals(current, journal.InstalledStatusLine))
            {
                receiver.Stop();
                return Result(false, false, true,
                    "Claude's statusLine setting changed after connection, so UsageApp left it untouched.");
            }
            try { receiver.Start(); }
            catch (Exception errorStartingReceiver)
            {
                receiver.Stop();
                return Result(false, false, false,
                    ReceiverStartFailure(errorStartingReceiver, true));
            }
            if (string.IsNullOrEmpty(receiver.StatusLineEndpoint))
            {
                receiver.Stop();
                return Result(false, false, false,
                    "Claude is configured, but the local receiver did not expose an endpoint.");
            }
            return Result(true, true, false,
                "Claude monitoring is already connected. Start a new Claude Code session to refresh quota.");
        }

        private ClaudeIntegrationResult RecoverFailedConnect(Journal preparedJournal)
        {
            Journal recovery = LoadJournal() ?? preparedJournal;
            Dictionary<string, object> currentSettings;
            bool currentExisted;
            string currentError;
            if (recovery != null
                && recovery.InstalledStatusLine != null
                && TryReadSettings(
                    out currentSettings,
                    out currentExisted,
                    out currentError))
            {
                object currentStatusLine;
                if (currentSettings.TryGetValue(
                        "statusLine",
                        out currentStatusLine)
                    && SemanticJsonEquals(
                        currentStatusLine,
                        recovery.InstalledStatusLine))
                {
                    if (LoadJournal() == null)
                    {
                        try
                        {
                            SaveJournal(recovery);
                        }
                        catch
                        {
                            if (TryRestorePriorSettings(
                                    recovery,
                                    currentSettings))
                            {
                                receiver.Stop();
                                bool cleanedAfterRollback =
                                    CleanupPreparedArtifacts();
                                return Result(false, false,
                                    !cleanedAfterRollback,
                                    cleanedAfterRollback
                                        ? "Claude settings were restored after UsageApp could not save a durable restoration record."
                                        : "Claude settings were restored, but UsageApp could not remove every incomplete integration file.");
                            }
                            return Result(false, receiver.IsListening, true,
                                "Claude's UsageApp status line is installed, but its restoration record could not be saved or rolled back. Keep UsageApp open and resolve the Claude settings conflict before trying again.");
                        }
                    }
                    return Result(
                        receiver.IsListening,
                        receiver.IsListening,
                        false,
                        receiver.IsListening
                            ? "Claude monitoring was connected, and UsageApp retained the restoration record. Start a new Claude Code session to test it."
                            : "Claude's UsageApp status line is installed, but its local receiver is not running. Restart UsageApp before using Claude monitoring.");
                }

                if (StatusLineMatchesPrior(currentSettings, recovery))
                {
                    receiver.Stop();
                    bool cleaned = CleanupPreparedArtifacts();
                    return Result(false, false, !cleaned,
                        cleaned
                            ? "Claude settings were not changed because UsageApp could not prepare its local status-line bridge."
                            : "Claude settings were not changed, but UsageApp could not remove every incomplete integration file.");
                }

                receiver.Stop();
                TryDeleteOwnedFile(settingsPath + ".usageapp.tmp");
                return Result(false, false, true,
                    "Claude settings changed while UsageApp was connecting. The receiver was stopped, and the restoration record was retained instead of overwriting the newer setting.");
            }

            receiver.Stop();
            if (recovery == null)
            {
                bool cleaned = CleanupPreparedArtifacts();
                return Result(false, false, !cleaned,
                    cleaned
                        ? "Claude settings were not changed because UsageApp could not prepare its local status-line bridge."
                        : "Claude settings were not changed, but UsageApp could not remove every incomplete integration file.");
            }
            TryDeleteOwnedFile(settingsPath + ".usageapp.tmp");
            return Result(false, false, true,
                "UsageApp could not verify Claude settings after a connection error. The receiver was stopped, and the restoration record was retained for recovery.");
        }

        private ClaudeIntegrationResult ResolveFailedDisconnect(Journal journal)
        {
            Dictionary<string, object> currentSettings;
            bool currentExisted;
            string currentError;
            if (TryReadSettings(
                    out currentSettings,
                    out currentExisted,
                    out currentError))
            {
                object currentStatusLine;
                if (currentSettings.TryGetValue(
                        "statusLine",
                        out currentStatusLine)
                    && SemanticJsonEquals(
                        currentStatusLine,
                        journal.InstalledStatusLine))
                {
                    return Result(false, receiver.IsListening, false,
                        receiver.IsListening
                            ? "UsageApp could not restore Claude's previous status line, so monitoring remains connected and the restoration record was retained."
                            : "UsageApp could not restore Claude's previous status line. The restoration record was retained, but the local receiver is not running.");
                }
                if (StatusLineMatchesPrior(currentSettings, journal))
                {
                    receiver.Stop();
                    bool cleaned = CleanupPreparedArtifacts();
                    return Result(cleaned, false, !cleaned,
                        cleaned
                            ? "Claude monitoring was disconnected and the previous status line was restored."
                            : "Claude monitoring was disconnected and the previous status line was restored, but local cleanup is incomplete.");
                }
            }

            receiver.Stop();
            return Result(false, false, true,
                "UsageApp could not safely verify Claude's status-line setting after the restore failed. The receiver was stopped and the restoration record was retained.");
        }

        private bool TryRestorePriorSettings(
            Journal journal,
            Dictionary<string, object> settings)
        {
            try
            {
                if (journal.HadStatusLine)
                {
                    settings["statusLine"] = journal.PriorStatusLine;
                }
                else
                {
                    settings.Remove("statusLine");
                }
                if (!journal.SettingsFileExisted && settings.Count == 0)
                {
                    if (File.Exists(settingsPath)) File.Delete(settingsPath);
                }
                else
                {
                    WriteSettings(settingsPath, settings);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool StatusLineMatchesPrior(
            Dictionary<string, object> settings,
            Journal journal)
        {
            object current = null;
            bool hasCurrent = settings != null
                && settings.TryGetValue("statusLine", out current);
            return journal.HadStatusLine
                ? hasCurrent && SemanticJsonEquals(current, journal.PriorStatusLine)
                : !hasCurrent;
        }

        private ClaudeIntegrationResult SettingsPathFailure()
        {
            if (!string.IsNullOrEmpty(settingsPath)
                && string.IsNullOrEmpty(settingsPathError))
            {
                return null;
            }
            return Result(false, false, false,
                string.IsNullOrEmpty(settingsPathError)
                    ? "UsageApp could not locate Claude's settings directory."
                    : settingsPathError);
        }

        private static string ReceiverStartFailure(
            Exception error,
            bool alreadyConfigured)
        {
            string prefix = alreadyConfigured
                ? "Claude remains configured, but UsageApp could not start its loopback-only receiver. "
                : "UsageApp could not start its loopback-only Claude receiver. ";
            if (error is HttpListenerException)
            {
                return prefix
                    + "Local port 38181 may already be in use or blocked by Windows policy. Close another UsageApp instance or the conflicting app, then try again.";
            }
            if (error is ObjectDisposedException)
            {
                return prefix + "Restart UsageApp, then try again.";
            }
            return prefix
                + "Another app or Windows policy may be blocking the local receiver.";
        }

        private static bool SamePath(string left, string right)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(left)
                    && !string.IsNullOrWhiteSpace(right)
                    && string.Equals(
                        Path.GetFullPath(left),
                        Path.GetFullPath(right),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadSettings(out Dictionary<string, object> settings, out bool existed, out string error)
        {
            return TryReadSettings(
                settingsPath,
                out settings,
                out existed,
                out error);
        }

        private static bool TryReadSettings(
            string path,
            out Dictionary<string, object> settings,
            out bool existed,
            out string error)
        {
            settings = null;
            existed = !string.IsNullOrEmpty(path) && File.Exists(path);
            error = null;
            if (!existed)
            {
                settings = new Dictionary<string, object>();
                return true;
            }
            try
            {
                settings = new JavaScriptSerializer().DeserializeObject(
                    File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                if (settings == null)
                {
                    error = "Claude settings are not a JSON object, so UsageApp left them unchanged.";
                    return false;
                }
                return true;
            }
            catch
            {
                error = "Claude settings are not valid JSON, so UsageApp left them unchanged.";
                return false;
            }
        }

        private static void WriteSettings(
            string path,
            Dictionary<string, object> settings)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + ".usageapp.tmp";
            File.WriteAllText(temporary,
                new JavaScriptSerializer().Serialize(settings), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private Journal LoadJournal()
        {
            try
            {
                if (!File.Exists(statePath)) return null;
                Journal journal = new JavaScriptSerializer().Deserialize<Journal>(
                    File.ReadAllText(statePath, Encoding.UTF8));
                return journal != null && journal.Version == StateVersion
                    && !string.IsNullOrEmpty(journal.SettingsPath)
                    ? journal : null;
            }
            catch { return null; }
        }

        private void SaveJournal(Journal journal)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath));
            string temporary = statePath + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(journal),
                new UTF8Encoding(false));
            if (File.Exists(statePath)) File.Replace(temporary, statePath, null);
            else File.Move(temporary, statePath);
        }

        private static Dictionary<string, object> CloneObject(Dictionary<string, object> source)
        {
            return new JavaScriptSerializer().DeserializeObject(
                new JavaScriptSerializer().Serialize(source)) as Dictionary<string, object>;
        }

        internal static bool SemanticJsonEqualsForTest(
            object left,
            object right)
        {
            return SemanticJsonEquals(left, right);
        }

        private static bool SemanticJsonEquals(object left, object right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;

            IDictionary leftObject = left as IDictionary;
            IDictionary rightObject = right as IDictionary;
            if (leftObject != null || rightObject != null)
            {
                if (leftObject == null
                    || rightObject == null
                    || leftObject.Count != rightObject.Count)
                {
                    return false;
                }
                foreach (DictionaryEntry entry in leftObject)
                {
                    if (!rightObject.Contains(entry.Key)
                        || !SemanticJsonEquals(
                            entry.Value,
                            rightObject[entry.Key]))
                    {
                        return false;
                    }
                }
                return true;
            }

            IList leftArray = left as IList;
            IList rightArray = right as IList;
            if (leftArray != null || rightArray != null)
            {
                if (leftArray == null
                    || rightArray == null
                    || leftArray.Count != rightArray.Count)
                {
                    return false;
                }
                for (int index = 0; index < leftArray.Count; index++)
                {
                    if (!SemanticJsonEquals(
                        leftArray[index],
                        rightArray[index]))
                    {
                        return false;
                    }
                }
                return true;
            }

            if (IsJsonNumber(left) || IsJsonNumber(right))
            {
                if (!IsJsonNumber(left) || !IsJsonNumber(right)) return false;
                try
                {
                    return Convert.ToDecimal(left)
                        == Convert.ToDecimal(right);
                }
                catch
                {
                    double leftNumber = Convert.ToDouble(left);
                    double rightNumber = Convert.ToDouble(right);
                    return !double.IsNaN(leftNumber)
                        && !double.IsNaN(rightNumber)
                        && leftNumber.Equals(rightNumber);
                }
            }

            return left.Equals(right);
        }

        private static bool IsJsonNumber(object value)
        {
            if (value == null || value is bool || value is char) return false;
            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }

        private bool CleanupPreparedArtifacts()
        {
            bool auxiliaryCleaned =
                TryDeleteOwnedFile(settingsPath + ".usageapp.tmp");
            auxiliaryCleaned = TryDeleteOwnedFile(statePath + ".tmp")
                && auxiliaryCleaned;
            bool wrappersCleaned = TryDeleteOwnedFile(wrapperPath);
            wrappersCleaned = TryDeleteOwnedFile(priorCommandPath)
                && wrappersCleaned;
            bool cleaned = auxiliaryCleaned && wrappersCleaned;
            if (cleaned)
            {
                cleaned = TryDeleteOwnedFile(statePath);
                try
                {
                    if (Directory.Exists(wrapperDirectory)
                        && Directory.GetFileSystemEntries(
                            wrapperDirectory).Length == 0)
                    {
                        Directory.Delete(wrapperDirectory, false);
                    }
                }
                catch
                {
                    // The owned files are gone. A nonempty or concurrently
                    // recreated directory is harmless and is not a failed
                    // disconnect transaction.
                }
            }
            return cleaned;
        }

        private static bool TryDeleteOwnedFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return !File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static ClaudeIntegrationResult Result(
            bool succeeded,
            bool connected,
            bool conflict,
            string message)
        {
            return new ClaudeIntegrationResult
            {
                Succeeded = succeeded,
                Connected = connected,
                Conflict = conflict,
                Message = message
            };
        }

        private static string WrapperScript(string endpoint, string priorCommandFile)
        {
            string prior = string.IsNullOrEmpty(priorCommandFile)
                ? "$null"
                : "'" + priorCommandFile.Replace("'", "''") + "'";
            return "$ErrorActionPreference = 'Stop'\r\n"
                + "$rawJson = [Console]::In.ReadToEnd()\r\n"
                + "try { Invoke-WebRequest -UseBasicParsing -DisableKeepAlive -Method Post -Uri '"
                + endpoint + "' -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($rawJson)) -TimeoutSec 1 | Out-Null } catch { }\r\n"
                + "$prior = " + prior + "\r\n"
                + "if ($null -ne $prior) { try {\r\n"
                + "  $priorCommand = [IO.File]::ReadAllText($prior, [Text.Encoding]::UTF8)\r\n"
                + "  $bash = $env:CLAUDE_CODE_GIT_BASH_PATH\r\n"
                + "  if ([string]::IsNullOrWhiteSpace($bash) -or -not (Test-Path -LiteralPath $bash)) {\r\n"
                + "    $found = Get-Command bash.exe -ErrorAction SilentlyContinue | Select-Object -First 1\r\n"
                + "    $bash = if ($null -ne $found) { $found.Source } else { $null }\r\n"
                + "  }\r\n"
                + "  if ($null -ne $bash) { $rawJson | & $bash -lc $priorCommand }\r\n"
                + "  else { $rawJson | & ([ScriptBlock]::Create($priorCommand)) }\r\n"
                + "} catch { } }\r\n";
        }

        private sealed class Journal
        {
            public int Version { get; set; }
            public string SettingsPath { get; set; }
            public bool SettingsFileExisted { get; set; }
            public bool HadStatusLine { get; set; }
            public object PriorStatusLine { get; set; }
            public object InstalledStatusLine { get; set; }
            public string PathToken { get; set; }
        }

        private sealed class SettingsPathResolution
        {
            public string Path { get; set; }
            public string Error { get; set; }
        }
    }
}
