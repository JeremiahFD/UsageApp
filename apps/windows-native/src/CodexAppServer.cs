using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

namespace UsageApp.Native
{
    internal sealed class CodexAppServer : IDisposable
    {
        private const string InstalledSource = "installed Codex CLI";
        private const string PinnedFallbackSource = "official pinned Codex npm fallback";
        private const string AppServerArguments =
            "app-server --listen stdio://";
        private const string NativeBetaVersion = "0.2.0-beta.1";
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const int JobObjectExtendedLimitInformationClass = 9;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(
            IntPtr jobAttributes,
            string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int informationClass,
            ref JobObjectExtendedLimitInformation information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(
            IntPtr job,
            IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private readonly object gate = new object();
        private readonly object stopGate = new object();
        private readonly object writeGate = new object();
        private readonly object pendingGate = new object();
        private readonly object jsonGate = new object();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly Dictionary<int, PendingRequest> pendingRequests =
            new Dictionary<int, PendingRequest>();
        private Process process;
        private StreamWriter input;
        private StreamReader output;
        private IntPtr processJob;
        private int nextId = 1;
        private int disposed;
        private int connectionSequence;
        private int activeConnectionId;
        private int rateLimitsUpdatePending;
        private int rateLimitsUpdateDispatching;
        private string connectedSource;

        internal event EventHandler RateLimitsUpdated;

        public UsageSnapshot ReadRateLimits()
        {
            lock (gate)
            {
                ThrowIfDisposed();
                EnsureConnected();
                try
                {
                    object result = Request("account/rateLimits/read", new Dictionary<string, object>(), 25000);
                    return Normalize(result, ReadOptionalUsage());
                }
                catch (Exception installedError)
                {
                    bool retryWithPinnedFallback =
                        string.Equals(
                            connectedSource,
                            InstalledSource,
                            StringComparison.Ordinal)
                        && IsCompatibilityError(installedError);
                    Stop();
                    if (!retryWithPinnedFallback)
                    {
                        throw;
                    }

                    try
                    {
                        StartPinnedFallback();
                        object result = Request(
                            "account/rateLimits/read",
                            new Dictionary<string, object>(),
                            25000);
                        return Normalize(result, ReadOptionalUsage());
                    }
                    catch (Exception fallbackError)
                    {
                        Stop();
                        throw new InvalidOperationException(
                            "The installed Codex CLI does not support the required "
                            + "account/rateLimits/read method. Installed CLI: "
                            + installedError.Message
                            + " Pinned fallback: "
                            + fallbackError.Message,
                            fallbackError);
                    }
                }
            }
        }

        private object ReadOptionalUsage()
        {
            try
            {
                return Request(
                    "account/usage/read",
                    new Dictionary<string, object>(),
                    1500);
            }
            catch
            {
                // Activity history is optional. A rate-limit refresh must
                // still succeed and retain the previous normalized history.
                return null;
            }
        }

        private void EnsureConnected()
        {
            ThrowIfDisposed();
            if (process != null
                && !process.HasExited
                && input != null
                && output != null
                && Volatile.Read(ref activeConnectionId) != 0)
            {
                return;
            }

            Stop();
            Exception installedExecutableFailure = null;
            try
            {
                StartCandidate(
                    "codex.exe",
                    AppServerArguments,
                    15000,
                    InstalledSource);
                return;
            }
            catch (Exception error)
            {
                installedExecutableFailure = error;
                Stop();
            }

            Exception installedCommandFailure = null;
            try
            {
                StartCandidate(
                    CommandProcessor(),
                    "/d /s /c codex.cmd " + AppServerArguments,
                    15000,
                    InstalledSource);
                return;
            }
            catch (Exception error)
            {
                installedCommandFailure = error;
                Stop();
            }

            try
            {
                StartPinnedFallback();
            }
            catch (Exception fallbackFailure)
            {
                Stop();
                throw new InvalidOperationException(
                    "Unable to start Codex. Installed executable: "
                    + installedExecutableFailure.Message
                    + " Installed command shim: "
                    + installedCommandFailure.Message
                    + " Pinned fallback: "
                    + fallbackFailure.Message,
                    fallbackFailure);
            }
        }

        private void StartPinnedFallback()
        {
            StartCandidate(
                CommandProcessor(),
                "/d /s /c npx.cmd -y @openai/codex@0.145.0 "
                    + AppServerArguments,
                90000,
                PinnedFallbackSource);
        }

        private static string CommandProcessor()
        {
            string commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
            return string.IsNullOrEmpty(commandProcessor)
                ? "cmd.exe"
                : commandProcessor;
        }

        private void StartCandidate(
            string fileName,
            string arguments,
            int initializeTimeoutMilliseconds,
            string source)
        {
            ThrowIfDisposed();
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = fileName;
            start.Arguments = arguments;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;

            process = new Process();
            process.StartInfo = start;
            process.EnableRaisingEvents = true;
            process.ErrorDataReceived += delegate { };

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Windows could not start the installed Codex CLI.");
                }
                ThrowIfDisposed();
                processJob = TryCreateKillOnCloseJob(process);
                process.BeginErrorReadLine();
                input = process.StandardInput;
                input.AutoFlush = true;
                output = process.StandardOutput;
                int connectionId = Interlocked.Increment(ref connectionSequence);
                Volatile.Write(ref activeConnectionId, connectionId);
                StartReader(output, input, connectionId);

                Dictionary<string, object> clientInfo = new Dictionary<string, object>();
                clientInfo["name"] = "usageapp_windows_native";
                clientInfo["title"] = "UsageApp Native for Windows";
                clientInfo["version"] = NativeBetaVersion;

                Dictionary<string, object> capabilities = new Dictionary<string, object>();
                capabilities["experimentalApi"] = true;
                capabilities["optOutNotificationMethods"] = new object[0];

                Dictionary<string, object> parameters = new Dictionary<string, object>();
                parameters["clientInfo"] = clientInfo;
                parameters["capabilities"] = capabilities;

                Request("initialize", parameters, initializeTimeoutMilliseconds);
                Notify("initialized", new Dictionary<string, object>());
                connectedSource = source;
            }
            catch
            {
                Stop();
                throw;
            }
        }

        private object Request(string method, object parameters, int timeoutMilliseconds)
        {
            StreamWriter writer = input;
            int connectionId = Volatile.Read(ref activeConnectionId);
            if (writer == null || connectionId == 0)
            {
                throw new EndOfStreamException("The Codex app-server is not connected.");
            }

            int id = nextId++;
            PendingRequest pending = new PendingRequest(connectionId);
            Dictionary<string, object> message = new Dictionary<string, object>();
            message["id"] = id;
            message["method"] = method;
            message["params"] = parameters;
            lock (pendingGate)
            {
                pendingRequests[id] = pending;
            }

            try
            {
                WriteMessage(writer, message);
                if (!pending.Signal.Wait(timeoutMilliseconds))
                {
                    lock (pendingGate)
                    {
                        PendingRequest registered;
                        if (pendingRequests.TryGetValue(id, out registered)
                            && object.ReferenceEquals(registered, pending))
                        {
                            pendingRequests.Remove(id);
                        }
                    }
                    if (!pending.Signal.IsSet)
                    {
                        throw new TimeoutException(
                            string.Format(
                                CultureInfo.CurrentCulture,
                                "The Codex app-server timed out while calling {0}.",
                                method));
                    }
                }

                if (pending.Failure != null)
                {
                    throw pending.Failure;
                }

                IDictionary<string, object> response = pending.Response;
                object error;
                if (response != null
                    && response.TryGetValue("error", out error)
                    && error != null)
                {
                    IDictionary<string, object> errorObject = AsObject(error);
                    string errorMessage = StringValue(Get(errorObject, "message"));
                    int parsedCode = Integer(Get(errorObject, "code"));
                    int? errorCode = parsedCode == int.MinValue
                        ? (int?)null
                        : parsedCode;
                    throw new CodexRpcException(
                        method,
                        errorCode,
                        errorMessage,
                        Get(errorObject, "data"));
                }

                return ExtractResult(response, method);
            }
            finally
            {
                lock (pendingGate)
                {
                    PendingRequest registered;
                    if (pendingRequests.TryGetValue(id, out registered)
                        && object.ReferenceEquals(registered, pending))
                    {
                        pendingRequests.Remove(id);
                    }
                }
                pending.Dispose();
            }
        }

        private void Notify(string method, object parameters)
        {
            Dictionary<string, object> message = new Dictionary<string, object>();
            message["method"] = method;
            message["params"] = parameters;
            WriteMessage(input, message);
        }

        private void ReplyMethodNotSupported(
            StreamWriter writer,
            object id,
            string method)
        {
            Dictionary<string, object> error = new Dictionary<string, object>();
            error["code"] = -32601;
            error["message"] = "Method not supported by UsageApp Native: " + (method ?? "unknown");

            Dictionary<string, object> response = new Dictionary<string, object>();
            response["id"] = id;
            response["error"] = error;
            WriteMessage(writer, response);
        }

        private void WriteMessage(StreamWriter writer, object message)
        {
            if (writer == null)
            {
                throw new EndOfStreamException("The Codex app-server is not connected.");
            }
            lock (writeGate)
            {
                string serialized;
                lock (jsonGate)
                {
                    serialized = json.Serialize(message);
                }
                writer.WriteLine(serialized);
            }
        }

        private void StartReader(
            StreamReader reader,
            StreamWriter writer,
            int connectionId)
        {
            Thread readerThread = new Thread(delegate()
            {
                Exception failure = null;
                try
                {
                    while (Volatile.Read(ref disposed) == 0
                        && Volatile.Read(ref activeConnectionId) == connectionId)
                    {
                        string line = reader.ReadLine();
                        if (line == null)
                        {
                            failure = new EndOfStreamException(
                                "The Codex app-server stopped before replying.");
                            break;
                        }
                        HandleIncomingLine(line, writer, connectionId);
                    }
                }
                catch (Exception error)
                {
                    failure = error;
                }
                finally
                {
                    Interlocked.CompareExchange(
                        ref activeConnectionId,
                        0,
                        connectionId);
                    FailPendingRequests(
                        connectionId,
                        failure ?? new EndOfStreamException(
                            "The Codex app-server connection ended."));
                }
            });
            readerThread.IsBackground = true;
            readerThread.Name = "UsageApp Codex app-server reader";
            readerThread.Start();
        }

        private void HandleIncomingLine(
            string line,
            StreamWriter writer,
            int connectionId)
        {
            object deserialized;
            try
            {
                lock (jsonGate)
                {
                    deserialized = json.DeserializeObject(line);
                }
            }
            catch
            {
                // Ignore a malformed diagnostic line without tearing down an
                // otherwise healthy JSONL session.
                return;
            }
            IDictionary<string, object> response = AsObject(deserialized);
            if (response == null)
            {
                return;
            }

            object responseId;
            if (response.TryGetValue("id", out responseId))
            {
                object requestedMethod;
                if (response.TryGetValue("method", out requestedMethod)
                    && Volatile.Read(ref activeConnectionId) == connectionId)
                {
                    ReplyMethodNotSupported(
                        writer,
                        responseId,
                        StringValue(requestedMethod));
                    return;
                }

                int id = Integer(responseId);
                lock (pendingGate)
                {
                    PendingRequest registered;
                    if (pendingRequests.TryGetValue(id, out registered)
                        && registered.ConnectionId == connectionId)
                    {
                        registered.Response = response;
                        registered.Signal.Set();
                        pendingRequests.Remove(id);
                    }
                }
                return;
            }

            if (IsRateLimitsUpdatedNotification(response))
            {
                QueueRateLimitsUpdated();
            }
        }

        private void FailPendingRequests(int connectionId, Exception failure)
        {
            lock (pendingGate)
            {
                List<int> ids = new List<int>();
                foreach (KeyValuePair<int, PendingRequest> entry in pendingRequests)
                {
                    if (entry.Value.ConnectionId == connectionId)
                    {
                        ids.Add(entry.Key);
                        entry.Value.Failure = failure;
                        entry.Value.Signal.Set();
                    }
                }
                foreach (int id in ids)
                {
                    pendingRequests.Remove(id);
                }
            }
        }

        private void QueueRateLimitsUpdated()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }
            Interlocked.Exchange(ref rateLimitsUpdatePending, 1);
            if (Interlocked.CompareExchange(ref rateLimitsUpdateDispatching, 1, 0) != 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    while (Interlocked.Exchange(ref rateLimitsUpdatePending, 0) != 0)
                    {
                        if (Volatile.Read(ref disposed) != 0)
                        {
                            return;
                        }
                        EventHandler handler = RateLimitsUpdated;
                        if (handler != null)
                        {
                            try
                            {
                                handler(this, EventArgs.Empty);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref rateLimitsUpdateDispatching, 0);
                    if (Volatile.Read(ref rateLimitsUpdatePending) != 0
                        && Volatile.Read(ref disposed) == 0)
                    {
                        QueueRateLimitsUpdated();
                    }
                }
            });
        }

        private static bool IsCompatibilityError(Exception error)
        {
            CodexRpcException rpc = error as CodexRpcException;
            return IsCompatibilityRpcError(
                rpc == null ? (int?)null : rpc.ErrorCode,
                error == null ? null : error.Message);
        }

        internal static bool IsCompatibilityRpcErrorForTest(
            int? errorCode,
            string errorMessage)
        {
            return IsCompatibilityRpcError(errorCode, errorMessage);
        }

        private static bool IsCompatibilityRpcError(
            int? errorCode,
            string errorMessage)
        {
            return errorCode == -32601
                || ContainsIgnoreCase(errorMessage, "method not found")
                || ContainsIgnoreCase(errorMessage, "unknown method")
                || ContainsIgnoreCase(errorMessage, "unimplemented")
                || ContainsIgnoreCase(errorMessage, "not implemented")
                || ContainsIgnoreCase(errorMessage, "unsupported method")
                || ContainsIgnoreCase(errorMessage, "method is not supported")
                || ContainsIgnoreCase(errorMessage, "unrecognized method")
                || (ContainsIgnoreCase(
                        errorMessage,
                        "account/ratelimits/read")
                    && (ContainsIgnoreCase(errorMessage, "unsupported")
                        || ContainsIgnoreCase(errorMessage, "not supported")));
        }

        internal static bool IsRateLimitsUpdatedNotificationForTest(object rawMessage)
        {
            return IsRateLimitsUpdatedNotification(AsObject(rawMessage));
        }

        private static bool IsRateLimitsUpdatedNotification(
            IDictionary<string, object> message)
        {
            object incomingMethod;
            return message != null
                && !message.ContainsKey("id")
                && message.TryGetValue("method", out incomingMethod)
                && string.Equals(
                    StringValue(incomingMethod),
                    "account/rateLimits/updated",
                    StringComparison.Ordinal);
        }

        private static bool ContainsIgnoreCase(string value, string fragment)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static UsageSnapshot NormalizeForTest(object rawResult)
        {
            return Normalize(rawResult, null);
        }

        internal static UsageSnapshot NormalizeForTest(
            object rawResult,
            object rawUsage)
        {
            return Normalize(rawResult, rawUsage);
        }

        internal static object ExtractResultForTest(
            object rawResponse,
            string method)
        {
            return ExtractResult(AsObject(rawResponse), method);
        }

        private static object ExtractResult(
            IDictionary<string, object> response,
            string method)
        {
            object result;
            if (response == null
                || !response.TryGetValue("result", out result)
                || result == null)
            {
                throw new InvalidDataException(
                    "The Codex app-server returned no usable result for "
                        + (string.IsNullOrEmpty(method) ? "a request" : method)
                        + ".");
            }
            return result;
        }

        private static UsageSnapshot Normalize(
            object rawResult,
            object rawUsage)
        {
            IDictionary<string, object> response = AsObject(rawResult);
            if (response == null)
            {
                throw new InvalidDataException(
                    "The Codex app-server returned an unusable rate-limit result.");
            }

            UsageSnapshot snapshot = new UsageSnapshot();
            snapshot.ObservedAtUtc = DateTime.UtcNow;

            List<KeyValuePair<string, IDictionary<string, object>>> limits =
                new List<KeyValuePair<string, IDictionary<string, object>>>();
            IDictionary<string, object> byId = AsObject(Get(response, "rateLimitsByLimitId"));
            if (byId != null)
            {
                foreach (KeyValuePair<string, object> entry in byId)
                {
                    IDictionary<string, object> limit = AsObject(entry.Value);
                    if (limit != null)
                    {
                        limits.Add(
                            new KeyValuePair<string, IDictionary<string, object>>(
                                entry.Key,
                                limit));
                    }
                }
            }

            if (limits.Count == 0)
            {
                IDictionary<string, object> single = AsObject(Get(response, "rateLimits"));
                if (single != null)
                {
                    string id = StringValue(Get(single, "limitId"));
                    limits.Add(
                        new KeyValuePair<string, IDictionary<string, object>>(
                            string.IsNullOrEmpty(id) ? "codex" : id,
                            single));
                }
            }

            foreach (KeyValuePair<string, IDictionary<string, object>> entry in limits)
            {
                AddWindow(snapshot, entry.Value, "primary", entry.Key);
                AddWindow(snapshot, entry.Value, "secondary", entry.Key);
            }

            NormalizeBankedResets(snapshot, response);

            IDictionary<string, object> preferred = null;
            foreach (KeyValuePair<string, IDictionary<string, object>> entry in limits)
            {
                if (string.Equals(entry.Key, "codex", StringComparison.OrdinalIgnoreCase))
                {
                    preferred = entry.Value;
                    break;
                }
            }
            if (preferred == null && limits.Count > 0)
            {
                preferred = limits[0].Value;
            }
            snapshot.PlanType = StringValue(Get(preferred, "planType"));
            if (string.IsNullOrEmpty(snapshot.PlanType))
            {
                foreach (KeyValuePair<string, IDictionary<string, object>> entry in limits)
                {
                    string candidate = StringValue(Get(entry.Value, "planType"));
                    if (!string.IsNullOrEmpty(candidate))
                    {
                        snapshot.PlanType = candidate;
                        break;
                    }
                }
            }
            NormalizeTokenUsage(snapshot, rawUsage);
            return snapshot;
        }

        private static void NormalizeTokenUsage(
            UsageSnapshot snapshot,
            object rawUsage)
        {
            IDictionary<string, object> usage = AsObject(rawUsage);
            IDictionary<string, object> summary =
                AsObject(Get(usage, "summary"));
            if (summary == null)
            {
                return;
            }

            object rawBuckets = Get(usage, "dailyUsageBuckets");
            IEnumerable buckets = rawBuckets as IEnumerable;
            if (rawBuckets is string
                || rawBuckets is IDictionary<string, object>)
            {
                buckets = null;
            }
            bool recognizedSummary = summary.ContainsKey("lifetimeTokens")
                || summary.ContainsKey("peakDailyTokens")
                || summary.ContainsKey("longestRunningTurnSec")
                || summary.ContainsKey("currentStreakDays")
                || summary.ContainsKey("longestStreakDays");
            if (!recognizedSummary && buckets == null)
            {
                return;
            }

            TokenUsageSummary normalized = new TokenUsageSummary();
            normalized.LifetimeTokens =
                UsageInteger(Get(summary, "lifetimeTokens"));
            normalized.PeakDailyTokens =
                UsageInteger(Get(summary, "peakDailyTokens"));
            normalized.LongestRunningTurnSeconds =
                UsageInteger(Get(summary, "longestRunningTurnSec"));
            normalized.CurrentStreakDays =
                UsageInteger(Get(summary, "currentStreakDays"));
            normalized.LongestStreakDays =
                UsageInteger(Get(summary, "longestStreakDays"));

            if (buckets != null)
            {
                Dictionary<string, long> byDate =
                    new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (object rawBucket in buckets)
                {
                    IDictionary<string, object> bucket = AsObject(rawBucket);
                    string startDate = StringValue(Get(bucket, "startDate"));
                    long? tokens = UsageInteger(Get(bucket, "tokens"));
                    if (bucket == null
                        || !IsDateKey(startDate)
                        || !tokens.HasValue)
                    {
                        continue;
                    }
                    byDate[startDate] = tokens.Value;
                }
                foreach (KeyValuePair<string, long> entry in byDate)
                {
                    normalized.DailyBuckets.Add(
                        new TokenUsageDailyBucket
                        {
                            StartDate = entry.Key,
                            Tokens = entry.Value
                        });
                }
                normalized.DailyBuckets.Sort(
                    delegate(
                        TokenUsageDailyBucket left,
                        TokenUsageDailyBucket right)
                    {
                        return string.CompareOrdinal(
                            left.StartDate,
                            right.StartDate);
                    });
            }

            snapshot.TokenUsage = normalized;
            snapshot.TokenUsageObservedAtUtc = snapshot.ObservedAtUtc;
        }

        private static void NormalizeBankedResets(
            UsageSnapshot snapshot,
            IDictionary<string, object> response)
        {
            IDictionary<string, object> summary =
                AsObject(Get(response, "rateLimitResetCredits"));
            if (summary == null)
            {
                return;
            }

            double? rawCount = Number(Get(summary, "availableCount"));
            if (rawCount.HasValue && rawCount.Value >= 0)
            {
                snapshot.BankedResets.AvailableCount = (int)Math.Truncate(rawCount.Value);
                snapshot.BankedResets.CountObservedAtUtc = snapshot.ObservedAtUtc;
            }

            object rawCredits = Get(summary, "credits");
            IEnumerable credits = rawCredits as IEnumerable;
            if (rawCredits is string || rawCredits is IDictionary<string, object>)
            {
                credits = null;
            }
            snapshot.BankedResets.DetailsAvailable = credits != null;
            if (credits == null)
            {
                return;
            }
            snapshot.BankedResets.DetailsObservedAtUtc = snapshot.ObservedAtUtc;

            foreach (object rawCredit in credits)
            {
                IDictionary<string, object> credit = AsObject(rawCredit);
                string id = StringValue(Get(credit, "id"));
                DateTime? grantedAt = EpochSeconds(Get(credit, "grantedAt"));
                if (credit == null || string.IsNullOrEmpty(id) || !grantedAt.HasValue)
                {
                    continue;
                }

                BankedReset item = new BankedReset();
                item.Id = id;
                item.Title = StringValue(Get(credit, "title"));
                item.Description = StringValue(Get(credit, "description"));
                item.Status = StringValue(Get(credit, "status")) ?? "unknown";
                item.GrantedAtUtc = grantedAt.Value;
                item.ExpiresAtUtc = EpochSeconds(Get(credit, "expiresAt"));
                snapshot.BankedResets.Items.Add(item);
            }

            snapshot.BankedResets.Items.Sort(delegate(BankedReset left, BankedReset right)
            {
                if (!left.ExpiresAtUtc.HasValue && !right.ExpiresAtUtc.HasValue)
                {
                    return 0;
                }
                if (!left.ExpiresAtUtc.HasValue)
                {
                    return 1;
                }
                if (!right.ExpiresAtUtc.HasValue)
                {
                    return -1;
                }
                return left.ExpiresAtUtc.Value.CompareTo(right.ExpiresAtUtc.Value);
            });
        }

        private static void AddWindow(
            UsageSnapshot snapshot,
            IDictionary<string, object> limit,
            string kind,
            string fallbackId)
        {
            IDictionary<string, object> rawWindow = AsObject(Get(limit, kind));
            double? used = Number(Get(rawWindow, "usedPercent"));
            if (rawWindow == null || !used.HasValue)
            {
                return;
            }

            string limitId = StringValue(Get(limit, "limitId"));
            if (string.IsNullOrEmpty(limitId))
            {
                limitId = fallbackId;
            }
            string limitName = StringValue(Get(limit, "limitName"));
            double? rawDuration = Number(Get(rawWindow, "windowDurationMins"));
            int? duration = rawDuration.HasValue && rawDuration.Value >= 0
                ? (int?)Math.Truncate(rawDuration.Value)
                : null;
            double clampedUsed = Math.Max(0, Math.Min(100, used.Value));
            string durationLabel = UsageFormatting.DurationLabel(duration);

            UsageWindow window = new UsageWindow();
            window.LimitId = limitId;
            window.LimitName = limitName;
            window.Kind = kind;
            window.Label = !string.IsNullOrEmpty(limitName)
                && !string.Equals(limitId, "codex", StringComparison.OrdinalIgnoreCase)
                ? limitName + " - " + durationLabel
                : durationLabel;
            window.UsedPercent = clampedUsed;
            window.DurationMinutes = duration;
            window.ResetsAtUtc = EpochSeconds(Get(rawWindow, "resetsAt"));
            snapshot.Windows.Add(window);
        }

        private static DateTime? EpochSeconds(object value)
        {
            double? seconds = Number(value);
            if (!seconds.HasValue || seconds.Value <= 0)
            {
                return null;
            }

            try
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(seconds.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private static IDictionary<string, object> AsObject(object value)
        {
            return value as IDictionary<string, object>;
        }

        private static object Get(IDictionary<string, object> value, string key)
        {
            if (value == null)
            {
                return null;
            }
            object result;
            return value.TryGetValue(key, out result) ? result : null;
        }

        private static string StringValue(object value)
        {
            return value as string;
        }

        private static bool IsDateKey(string value)
        {
            DateTime parsed;
            return !string.IsNullOrEmpty(value)
                && value.Length == 10
                && DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out parsed);
        }

        private static long? UsageInteger(object value)
        {
            if (value == null || value is bool)
            {
                return null;
            }
            try
            {
                decimal number = Convert.ToDecimal(
                    value,
                    CultureInfo.InvariantCulture);
                if (number < 0)
                {
                    return null;
                }
                return Convert.ToInt64(decimal.Truncate(number));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static double? Number(object value)
        {
            if (value == null || value is bool)
            {
                return null;
            }
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int Integer(object value)
        {
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return int.MinValue;
            }
        }

        public void Stop()
        {
            lock (stopGate)
            {
                int stoppingConnectionId = Interlocked.Exchange(
                    ref activeConnectionId,
                    0);
                StreamWriter writer = input;
                StreamReader reader = output;
                Process active = process;
                IntPtr activeJob = processJob;
                input = null;
                output = null;
                process = null;
                processJob = IntPtr.Zero;
                connectedSource = null;
                if (stoppingConnectionId != 0)
                {
                    FailPendingRequests(
                        stoppingConnectionId,
                        new EndOfStreamException(
                            "The Codex app-server connection was stopped."));
                }

                bool jobClosed = false;
                if (activeJob != IntPtr.Zero)
                {
                    try
                    {
                        jobClosed = CloseHandle(activeJob);
                    }
                    catch
                    {
                        jobClosed = false;
                    }
                }
                try
                {
                    if (!jobClosed && active != null && !active.HasExited)
                    {
                        active.Kill();
                    }
                }
                catch
                {
                }
                try
                {
                    if (writer != null)
                    {
                        writer.Dispose();
                    }
                }
                catch
                {
                }
                try
                {
                    if (reader != null)
                    {
                        reader.Dispose();
                    }
                }
                catch
                {
                }
                if (active != null)
                {
                    active.Dispose();
                }
            }
        }

        private static IntPtr TryCreateKillOnCloseJob(Process active)
        {
            if (active == null)
            {
                return IntPtr.Zero;
            }

            IntPtr job = IntPtr.Zero;
            try
            {
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                JobObjectExtendedLimitInformation information =
                    new JobObjectExtendedLimitInformation();
                information.BasicLimitInformation.LimitFlags =
                    JobObjectLimitKillOnJobClose;
                uint informationLength = (uint)Marshal.SizeOf(
                    typeof(JobObjectExtendedLimitInformation));
                if (!SetInformationJobObject(
                        job,
                        JobObjectExtendedLimitInformationClass,
                        ref information,
                        informationLength)
                    || !AssignProcessToJobObject(job, active.Handle))
                {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }
                return job;
            }
            catch
            {
                if (job != IntPtr.Zero)
                {
                    try
                    {
                        CloseHandle(job);
                    }
                    catch
                    {
                    }
                }
                return IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            internal long PerProcessUserTimeLimit;
            internal long PerJobUserTimeLimit;
            internal uint LimitFlags;
            internal UIntPtr MinimumWorkingSetSize;
            internal UIntPtr MaximumWorkingSetSize;
            internal uint ActiveProcessLimit;
            internal UIntPtr Affinity;
            internal uint PriorityClass;
            internal uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            internal ulong ReadOperationCount;
            internal ulong WriteOperationCount;
            internal ulong OtherOperationCount;
            internal ulong ReadTransferCount;
            internal ulong WriteTransferCount;
            internal ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            internal JobObjectBasicLimitInformation BasicLimitInformation;
            internal IoCounters IoInfo;
            internal UIntPtr ProcessMemoryLimit;
            internal UIntPtr JobMemoryLimit;
            internal UIntPtr PeakProcessMemoryUsed;
            internal UIntPtr PeakJobMemoryUsed;
        }

        private sealed class PendingRequest : IDisposable
        {
            internal PendingRequest(int connectionId)
            {
                ConnectionId = connectionId;
                Signal = new ManualResetEventSlim(false);
            }

            internal int ConnectionId { get; private set; }

            internal ManualResetEventSlim Signal { get; private set; }

            internal IDictionary<string, object> Response { get; set; }

            internal Exception Failure { get; set; }

            public void Dispose()
            {
                Signal.Dispose();
            }
        }

        private sealed class CodexRpcException : InvalidOperationException
        {
            internal CodexRpcException(
                string method,
                int? errorCode,
                string errorMessage,
                object rpcData)
                : base(FormatMessage(method, errorCode, errorMessage))
            {
                Method = method;
                ErrorCode = errorCode;
                RpcData = rpcData;
            }

            internal string Method { get; private set; }

            internal int? ErrorCode { get; private set; }

            internal object RpcData { get; private set; }

            private static string FormatMessage(
                string method,
                int? errorCode,
                string errorMessage)
            {
                string detail = string.IsNullOrEmpty(errorMessage)
                    ? "The Codex app-server returned an error."
                    : errorMessage;
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} ({1}{2}): {3}",
                    "The Codex app-server rejected " + (method ?? "a request"),
                    "RPC code ",
                    errorCode.HasValue
                        ? errorCode.Value.ToString(CultureInfo.InvariantCulture)
                        : "unavailable",
                    detail);
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref disposed, 1);
            Stop();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException("CodexAppServer");
            }
        }
    }
}
