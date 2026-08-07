using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace UsageApp.Native
{
    /// <summary>
    /// A small, opt-in receiver for Claude Code's documented status-line JSON.
    /// It binds only to 127.0.0.1 and requires an unguessable path token. It
    /// deliberately normalizes the quota object before notifying the UI and
    /// never logs or persists the original status-line payload.
    /// </summary>
    internal sealed class ClaudeStatusLineReceiver : IDisposable
    {
        internal const int MaximumBodyBytes = 64 * 1024;

        private readonly string pathToken;
        private readonly int requestedPort;
        private readonly object gate = new object();
        private HttpListener listener;
        private bool disposed;

        internal ClaudeStatusLineReceiver()
            : this(CreatePathToken(), 0)
        {
        }

        internal ClaudeStatusLineReceiver(string token)
            : this(token, 0)
        {
        }

        internal ClaudeStatusLineReceiver(string token, int port)
        {
            if (!IsValidPathToken(token))
            {
                throw new ArgumentException("A receiver token is required.", "token");
            }
            pathToken = token;
            if (port < 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException("port");
            }
            requestedPort = port;
        }

        internal event EventHandler<ClaudeQuotaSnapshot> SnapshotReceived;
        internal event EventHandler<string> ReceiverFaulted;

        internal bool IsListening
        {
            get
            {
                lock (gate)
                {
                    return listener != null && listener.IsListening;
                }
            }
        }

        internal int Port { get; private set; }
        internal string PathToken { get { return pathToken; } }

        internal string StatusLineEndpoint
        {
            get
            {
                if (Port <= 0)
                {
                    return null;
                }
                return string.Format(
                    "http://127.0.0.1:{0}/v1/statusline/{1}",
                    Port,
                    pathToken);
            }
        }

        internal void Start()
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (listener != null && listener.IsListening)
                {
                    return;
                }

                HttpListener next = new HttpListener();
                int port = requestedPort == 0 ? ReserveLoopbackPort() : requestedPort;
                next.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", port));
                try
                {
                    next.Start();
                }
                catch
                {
                    next.Close();
                    throw;
                }
                listener = next;
                Port = port;
                BeginAccept(next);
            }
        }

        internal void Stop()
        {
            HttpListener active;
            lock (gate)
            {
                active = listener;
                listener = null;
                Port = 0;
            }
            if (active != null)
            {
                try { active.Close(); }
                catch { }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stop();
        }

        internal static bool IsValidPathToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 32 || token.Length > 128)
            {
                return false;
            }
            foreach (char value in token)
            {
                bool accepted = (value >= 'a' && value <= 'z')
                    || (value >= 'A' && value <= 'Z')
                    || (value >= '0' && value <= '9')
                    || value == '-' || value == '_';
                if (!accepted) return false;
            }
            return true;
        }

        internal static string CreatePathToken()
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static int ReserveLoopbackPort()
        {
            System.Net.Sockets.TcpListener reservation =
                new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            int port = ((System.Net.IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            return port;
        }

        private void BeginAccept(HttpListener active)
        {
            try
            {
                active.BeginGetContext(Accepted, active);
            }
            catch (ObjectDisposedException) { }
            catch (HttpListenerException) { }
        }

        private void Accepted(IAsyncResult result)
        {
            HttpListener active = result.AsyncState as HttpListener;
            HttpListenerContext context = null;
            try
            {
                context = active.EndGetContext(result);
            }
            catch (ObjectDisposedException) { return; }
            catch (HttpListenerException) { return; }
            finally
            {
                if (active != null && active.IsListening) BeginAccept(active);
            }

            if (context != null)
            {
                ThreadPool.QueueUserWorkItem(delegate { Handle(context); });
            }
        }

        private void Handle(HttpListenerContext context)
        {
            try
            {
                if (!IsLoopback(context.Request.RemoteEndPoint)
                    || context.Request.HttpMethod != "POST"
                    || !string.Equals(context.Request.Url.AbsolutePath,
                        "/v1/statusline/" + pathToken,
                        StringComparison.Ordinal)
                    || !IsJson(context.Request.ContentType))
                {
                    Reply(context.Response, 404);
                    return;
                }
                if (context.Request.ContentLength64 > MaximumBodyBytes)
                {
                    Reply(context.Response, 413);
                    return;
                }

                string body = ReadBody(context.Request);
                object raw = new JavaScriptSerializer().DeserializeObject(body);
                ClaudeQuotaSnapshot snapshot = ClaudeStatusLine.Normalize(
                    raw, DateTime.UtcNow, DateTime.UtcNow);
                EventHandler<ClaudeQuotaSnapshot> received = SnapshotReceived;
                if (received != null) received(this, snapshot);
                Reply(context.Response, 200);
            }
            catch (Exception)
            {
                EventHandler<string> faulted = ReceiverFaulted;
                if (faulted != null) faulted(this, "Claude status-line update was not usable.");
                try { Reply(context.Response, 400); }
                catch { }
            }
        }

        private static bool IsLoopback(IPEndPoint endpoint)
        {
            return endpoint != null && IPAddress.IsLoopback(endpoint.Address);
        }

        private static bool IsJson(string contentType)
        {
            return !string.IsNullOrEmpty(contentType)
                && contentType.Split(';')[0].Trim().Equals(
                    "application/json", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            using (StreamReader reader = new StreamReader(request.InputStream,
                new UTF8Encoding(false, true), false, MaximumBodyBytes, true))
            {
                char[] buffer = new char[4096];
                StringBuilder value = new StringBuilder();
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    value.Append(buffer, 0, count);
                    if (Encoding.UTF8.GetByteCount(value.ToString()) > MaximumBodyBytes)
                    {
                        throw new InvalidDataException("Request body is too large.");
                    }
                }
                return value.ToString();
            }
        }

        private static void Reply(HttpListenerResponse response, int statusCode)
        {
            response.StatusCode = statusCode;
            response.ContentLength64 = 0;
            response.Close();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException("ClaudeStatusLineReceiver");
        }
    }
}
