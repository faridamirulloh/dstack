using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace MiniCodex.Codex;

public sealed class CodexClient : IAsyncDisposable
{
    private const int MaxIncomingBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly Process _process;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = [];
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly SemaphoreSlim _requests = new(56, 56);
    private readonly SemaphoreSlim _controls = new(8, 8);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<WireMessage> _events = Channel.CreateBounded<WireMessage>(new BoundedChannelOptions(512) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Func<WireMessage, Task> _receive;
    private readonly Action<string> _disconnected;
    private long _nextId;
    private int _closed;
    private long _queuedBytes;
    private readonly Task _reader;
    private readonly Task _dispatcher;
    private readonly Task _stderr;
    public bool IsAlive => Volatile.Read(ref _closed) == 0 && !_process.HasExited;
    public int PendingCount => _pending.Count;

    public CodexClient(ProcessStartInfo start, Func<WireMessage, Task> receive, Action<string> disconnected)
    {
        _receive = receive; _disconnected = disconnected;
        _process = Process.Start(start) ?? throw new IOException("Codex could not be started.");
        _process.StandardInput.AutoFlush = true;
        _reader = ReadAsync(); _dispatcher = DispatchAsync(); _stderr = DrainErrorsAsync();
    }

    public async Task<JsonElement> CallAsync(string method, object parameters, bool control = false)
    {
        var token = _lifetime.Token;
        var slots = control ? _controls : _requests;
        await slots.WaitAsync(token).ConfigureAwait(false);
        var id = "mini-" + Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (!IsAlive) throw new IOException("Codex is disconnected. Reconnect before continuing.");
            _pending[id] = completion;
            await SendAsync(new { id, method, @params = parameters }).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(120), token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        { throw new IOException($"Codex did not acknowledge {method}. Its outcome may be unknown; the request was not retried."); }
        finally { _pending.TryRemove(id, out _); slots.Release(); }
    }

    public Task NotifyAsync(string method, object parameters) => SendAsync(new { method, @params = parameters });
    public Task RespondAsync(JsonElement id, object result) => SendAsync(new { id, result });
    public Task RejectAsync(JsonElement id, int code = -32601, string message = "This client does not support this request.") => SendAsync(new { id, error = new { code, message } });

    private async Task SendAsync(object envelope)
    {
        var line = JsonSerializer.Serialize(envelope, Json);
        if (Encoding.UTF8.GetByteCount(line) > 8 * 1024 * 1024) throw new IOException("The outgoing Codex message is too large.");
        await _write.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        try { await _process.StandardInput.WriteLineAsync(line.AsMemory(), _lifetime.Token).ConfigureAwait(false); }
        finally { _write.Release(); }
    }

    private async Task ReadAsync()
    {
        var line = new MemoryStream();
        byte[]? oversizedPrefix = null;
        try
        {
            var buffer = new byte[8192];
            while (true)
            {
                var count = await _process.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(), _lifetime.Token).ConfigureAwait(false);
                if (count == 0) break;
                var offset = 0;
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] != '\n') continue;
                    Append(buffer.AsSpan(offset, i - offset));
                    if (oversizedPrefix is not null) await HandleOversizedPrefixAsync(oversizedPrefix).ConfigureAwait(false);
                    else if (line.Length > 0) await HandleLineAsync(line.GetBuffer().AsMemory(0, checked((int)line.Length))).ConfigureAwait(false);
                    // Do not retain a large image response buffer for the lifetime of this account.
                    if (line.Capacity > 256 * 1024) { line.Dispose(); line = new MemoryStream(); }
                    else { line.SetLength(0); line.Position = 0; }
                    oversizedPrefix = null; offset = i + 1;
                }
                Append(buffer.AsSpan(offset, count - offset));
            }
            if (!_lifetime.IsCancellationRequested) Fail("Codex exited. Reconnect and reload history before retrying uncertain work.");
        }
        catch (Exception error) when (error is IOException or JsonException or OperationCanceledException or InvalidOperationException)
        { if (!_lifetime.IsCancellationRequested) Fail("The Codex connection was lost. " + Wire.Short(error.Message, 300)); }
        finally { line.Dispose(); _events.Writer.TryComplete(); }

        void Append(ReadOnlySpan<byte> bytes)
        {
            if (oversizedPrefix is not null) return;
            if (line.Length + bytes.Length > MaxIncomingBytes)
            {
                oversizedPrefix = line.GetBuffer().AsSpan(0, Math.Min(checked((int)line.Length), 8192)).ToArray();
                line.Dispose(); line = new MemoryStream();
            }
            else line.Write(bytes);
        }
    }

    private async Task HandleLineAsync(ReadOnlyMemory<byte> line)
    {
        if (IsWhitespace(line.Span)) return;
        using var document = JsonDocument.Parse(line);
        JsonElement root; int size;
        try { (root, size) = ImagePayloads.Normalize(document.RootElement); }
        catch (CodexPayloadTooLargeException)
        {
            var envelope = document.RootElement;
            await HandleOversizedAsync(envelope.Get("id"), envelope.Text("method"), envelope.Get("params").Text("threadId")).ConfigureAwait(false);
            return;
        }
        var id = root.Get("id");
        if (root.Text("method") is { } method)
        {
            if (Interlocked.Add(ref _queuedBytes, size) > 16 * 1024 * 1024) throw new IOException("Codex events exceeded the 16 MiB queue limit. Reconnect and refresh saved history.");
            await _events.Writer.WriteAsync(new WireMessage(method, root.Get("params").Clone(), id.ValueKind == JsonValueKind.Undefined ? default : id.Clone(), size), _lifetime.Token).ConfigureAwait(false);
            return;
        }
        if (id.Text() is not { } key || !_pending.TryGetValue(key, out var waiting)) return;
        if (root.Get("error") is { ValueKind: JsonValueKind.Object } error)
            waiting.TrySetException(new CodexRpcException((int)(error.Get("code").Number() ?? -1), Wire.Short(error.Text("message"))));
        else waiting.TrySetResult(root.Get("result").Clone());
    }

    private static bool IsWhitespace(ReadOnlySpan<byte> line)
    {
        foreach (var value in line) if (value is not (9 or 10 or 13 or 32)) return false;
        return true;
    }

    private Task HandleOversizedPrefixAsync(byte[] prefix)
    {
        var envelope = ReadEnvelopePrefix(prefix);
        return HandleOversizedAsync(envelope.Id, envelope.Method, envelope.ThreadId);
    }

    private static (JsonElement Id, string? Method, string? ThreadId) ReadEnvelopePrefix(ReadOnlySpan<byte> prefix)
    {
        JsonElement id = default; string? method = null; string? threadId = null;
        try
        {
            var reader = new Utf8JsonReader(prefix, isFinalBlock: false, state: default);
            string? property = null; var depth = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName) { property = reader.GetString(); depth = reader.CurrentDepth; continue; }
                if (depth == 1 && property == "id" && reader.TokenType is JsonTokenType.String or JsonTokenType.Number)
                { using var value = JsonDocument.ParseValue(ref reader); id = value.RootElement.Clone(); }
                else if (depth == 1 && property == "method" && reader.TokenType == JsonTokenType.String) method = reader.GetString();
                else if (depth == 2 && property == "threadId" && reader.TokenType == JsonTokenType.String) threadId = reader.GetString();
                property = null;
            }
        }
        catch (JsonException) { }
        return (id, method, threadId);
    }

    private async Task HandleOversizedAsync(JsonElement id, string? method, string? threadId)
    {
        var error = new CodexPayloadTooLargeException();
        if (method is null)
        {
            if (id.Text() is { } key)
            { if (_pending.TryGetValue(key, out var waiting)) waiting.TrySetException(error); }
            else
                foreach (var request in _pending.Values) request.TrySetException(error);
            return;
        }
        if (id.ValueKind != JsonValueKind.Undefined)
            await RejectAsync(id, -32000, "This Codex request is too large to display.").ConfigureAwait(false);
        var parameters = JsonSerializer.SerializeToElement(new { threadId, method, message = "An oversized Codex output was skipped. The account is still connected." });
        const int size = 512;
        Interlocked.Add(ref _queuedBytes, size);
        await _events.Writer.WriteAsync(new WireMessage("mini/outputTooLarge", parameters, default, size), _lifetime.Token).ConfigureAwait(false);
    }

    private async Task DispatchAsync()
    {
        try
        {
            await foreach (var message in _events.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            { Interlocked.Add(ref _queuedBytes, -message.ByteSize); await _receive(message).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Fail("A Codex event could not be handled. Reconnect before continuing."); }
    }

    private async Task DrainErrorsAsync()
    {
        // Drain stderr without recording raw payloads, credentials, or login URLs.
        try { var buffer = new char[4096]; while (await _process.StandardError.ReadAsync(buffer.AsMemory(), _lifetime.Token).ConfigureAwait(false) != 0) { } }
        catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException) { }
    }

    private void Fail(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        foreach (var request in _pending.Values) request.TrySetException(new IOException(reason));
        _disconnected(reason);
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _closed, 1);
        _lifetime.Cancel();
        foreach (var request in _pending.Values) request.TrySetException(new IOException("Codex was stopped."));
        try
        {
            _process.StandardInput.Close();
            if (!_process.HasExited)
            {
                try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (TimeoutException) { _process.Kill(entireProcessTree: true); await _process.WaitForExitAsync().ConfigureAwait(false); }
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { }
        await Task.WhenAll(_reader, _dispatcher, _stderr).ConfigureAwait(false);
        _process.Dispose(); _lifetime.Dispose();
    }
}
