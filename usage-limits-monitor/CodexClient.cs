using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UsageLimitsMonitor
{
    internal sealed class CodexClient : IDisposable
    {
        private readonly Process process;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> pending = new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly SemaphoreSlim writer = new SemaphoreSlim(1, 1);
        private TaskCompletionSource<bool> login;
        private int sequence;
        private volatile bool disposed;
        public bool Running { get { try { return !disposed && !process.HasExited; } catch (InvalidOperationException) { return false; } } }

        private CodexClient(string executable, string profile)
        {
            if (!File.Exists(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select the native codex.exe in Settings.");
            Directory.CreateDirectory(profile);
            File.WriteAllText(Path.Combine(profile, "config.toml"), "cli_auth_credentials_store = \"keyring\"\n[analytics]\nenabled = false\n", new UTF8Encoding(false));
            // Some launchers supply both PATH and Path; Framework's child environment rejects that block.
            var inherited = Environment.GetEnvironmentVariables();
            var pathKeys = inherited.Keys.Cast<string>().Where(k => string.Equals(k, "PATH", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (pathKeys.Length > 1)
            {
                string searchPath = Convert.ToString(inherited[pathKeys[0]]);
                foreach (string key in pathKeys) Environment.SetEnvironmentVariable(key, null);
                Environment.SetEnvironmentVariable("PATH", searchPath);
            }
            var start = new ProcessStartInfo {
                FileName = Path.GetFullPath(executable), Arguments = "app-server --listen stdio://",
                WorkingDirectory = profile, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // Scope the documented home override to this child; never change the user's global home.
            start.EnvironmentVariables["CODEX_HOME"] = Path.GetFullPath(profile);
            foreach (string key in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "OPENAI_BASE_URL", "OPENAI_API_BASE", "OPENAI_ORG_ID", "OPENAI_ORGANIZATION", "OPENAI_PROJECT_ID" }) start.EnvironmentVariables.Remove(key);
            process = new Process { StartInfo = start };
            process.Start();
            process.StandardInput.AutoFlush = true;
            Task.Run((Func<Task>)ReadLoop);
            // Drain diagnostics without persisting potentially sensitive upstream messages.
            Task.Run(async delegate { try { while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) != null) { } } catch (Exception) { } });
        }
        public static async Task<CodexClient> Start(string executable, string profile)
        {
            var client = new CodexClient(executable, profile);
            try
            {
                await client.Request("initialize", new { clientInfo = new { name = "usage_limits_monitor", title = "Usage Limits Monitor", version = "1.0.0" } }).ConfigureAwait(false);
                await client.Send(new { method = "initialized", @params = new { } }).ConfigureAwait(false);
                return client;
            }
            catch { client.Dispose(); throw; }
        }
        private async Task Send(object message)
        {
            await writer.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Running) throw new IOException("Codex exited. Refresh or reconnect the account.");
                await process.StandardInput.WriteLineAsync(Json.Write(message)).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            finally { writer.Release(); }
        }
        public async Task<Dictionary<string, object>> Request(string method, object parameters)
        {
            int id = Interlocked.Increment(ref sequence);
            var completion = new TaskCompletionSource<Dictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = completion;
            try
            {
                await Send(new { id = id, method = method, @params = parameters ?? new { } }).ConfigureAwait(false);
                if (await Task.WhenAny(completion.Task, Task.Delay(30000)).ConfigureAwait(false) != completion.Task)
                    throw new TimeoutException("Codex did not respond within 30 seconds.");
                return await completion.Task.ConfigureAwait(false);
            }
            finally { TaskCompletionSource<Dictionary<string, object>> ignored; pending.TryRemove(id, out ignored); }
        }
        private async Task ReadLoop()
        {
            try
            {
                string line;
                while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (line.Length > 2 * 1024 * 1024) throw new IOException("Codex response exceeds the monitor's limit.");
                    Dictionary<string, object> message;
                    try { message = Json.Read(line); } catch (ArgumentException) { continue; }
                    if (message == null) continue;
                    string method = Json.Text(message, "method");
                    if (method == "account/login/completed" && login != null)
                        login.TrySetResult(object.Equals(Json.Get(Json.Map(Json.Get(message, "params")), "success"), true));
                    if (Json.Get(message, "id") == null) continue;
                    if (!string.IsNullOrEmpty(method))
                    {
                        await Send(new { id = Json.Get(message, "id"), error = new { code = -32601, message = "This monitor does not execute tools or agent tasks." } }).ConfigureAwait(false);
                        continue;
                    }
                    var number = Json.Number(Json.Get(message, "id"));
                    TaskCompletionSource<Dictionary<string, object>> completion;
                    if (!number.HasValue || !pending.TryGetValue((int)number.Value, out completion)) continue;
                    if (Json.Get(message, "error") != null)
                    {
                        string code = Json.Text(Json.Map(Json.Get(message, "error")), "code");
                        completion.TrySetException(new InvalidOperationException("Codex request failed (" + code + "). Check your connection or reconnect the account."));
                    }
                    else completion.TrySetResult(Json.Map(Json.Get(message, "result")));
                }
            }
            catch (Exception) { /* Pending requests receive a safe, credential-free error below. */ }
            finally
            {
                foreach (var item in pending.Values) item.TrySetException(new IOException("Codex connection closed. Refresh or reconnect the account."));
                if (login != null) login.TrySetResult(false);
            }
        }
        public async Task Login(Action<string> openBrowser, CancellationToken cancellation)
        {
            login = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            string loginId = null;
            Exception failure = null;
            try
            {
                var result = await Request("account/login/start", new { type = "chatgpt" }).ConfigureAwait(false);
                loginId = Json.Text(result, "loginId");
                string address = Json.Text(result, "authUrl");
                Uri uri;
                if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) ||
                    !(uri.Host == "chatgpt.com" || uri.Host == "auth.openai.com" || uri.Host == "openai.com"))
                    throw new InvalidOperationException("Codex returned an unexpected sign-in address. Update Codex before retrying.");
                openBrowser(address);
                var cancelled = Task.Delay(Timeout.Infinite, cancellation);
                var completed = await Task.WhenAny(login.Task, Task.Delay(TimeSpan.FromMinutes(5)), cancelled).ConfigureAwait(false);
                if (completed == cancelled) throw new OperationCanceledException();
                if (completed != login.Task) throw new TimeoutException("Sign-in timed out. Please reconnect and try again.");
                if (!await login.Task.ConfigureAwait(false)) throw new InvalidOperationException("Sign-in was not completed. Please reconnect and try again.");
            }
            catch (Exception error) { failure = error; }
            finally { login = null; }
            if (failure != null)
            {
                if (!string.IsNullOrEmpty(loginId) && Running)
                {
                    try { await Request("account/login/cancel", new { loginId = loginId }).ConfigureAwait(false); } catch (Exception) { }
                }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        public async Task<Dictionary<string, object>> ReadAccount()
        {
            var response = await Request("account/read", new { refreshToken = false }).ConfigureAwait(false);
            var account = Json.Map(Json.Get(response, "account"));
            if (Json.Text(account, "type") != "chatgpt") throw new InvalidOperationException("Sign in with a ChatGPT account to read its included limits.");
            return account;
        }
        public static string Identity(Dictionary<string, object> account)
        {
            string identity = Json.Get(account, "chatgptAccountId") as string ?? Json.Get(account, "id") as string ?? Json.Get(account, "email") as string;
            if (string.IsNullOrWhiteSpace(identity)) throw new InvalidOperationException("Codex did not report an account identity. Update Codex or reconnect.");
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(identity.Trim().ToLowerInvariant())));
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { process.StandardInput.Close(); } catch (Exception) { }
            try { if (!process.WaitForExit(500)) process.Kill(); } catch (Exception) { }
            process.Dispose();
        }
        public static string FindExecutable()
        {
            foreach (string part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { var path = Path.Combine(part.Trim('"'), "codex.exe"); if (File.Exists(path)) return Path.GetFullPath(path); } catch (ArgumentException) { }
            }
            var bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "plugins", ".plugin-appserver", "codex.exe");
            return File.Exists(bundled) ? bundled : "";
        }
    }
}
