using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Tweakr.App.Services;

/// <summary>
/// Discord Rich Presence over the local IPC named pipe (discord-ipc-0..9). No SDK needed.
/// Runs on a background loop: reconnects if Discord starts later or restarts, and never throws into the UI.
/// For the big image, upload Assets/tweakr-1024.png in the Discord Developer Portal
/// (Rich Presence -> Art Assets) with the key "tweakr".
/// </summary>
public sealed class DiscordPresenceService : IDisposable
{
    const string ClientId = "1561484591292879069";
    const string LargeImageKey = "tweakr";

    readonly CancellationTokenSource _cts = new();
    readonly long _startUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    readonly object _lock = new();
    readonly Task _loop;
    NamedPipeClientStream? _pipe;
    volatile bool _enabled, _dirty;
    string _details = "Starting up", _state = "";

    public DiscordPresenceService() => _loop = Task.Run(() => LoopAsync(_cts.Token));

    public void SetEnabled(bool on) { _enabled = on; _dirty = true; }

    public void Update(string details, string? state)
    {
        lock (_lock) { _details = details; _state = state ?? ""; }
        _dirty = true;
    }

    async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_enabled)
                {
                    if (_pipe is not null) { await SendActivityAsync(null, ct); Disconnect(); }   // clear, then hang up
                    await Task.Delay(500, ct);
                    continue;
                }

                if (_pipe is not { IsConnected: true })
                {
                    Disconnect();
                    if (!await ConnectAsync(ct)) { await Task.Delay(TimeSpan.FromSeconds(15), ct); continue; }   // Discord not running
                    _dirty = true;
                }

                if (_dirty)
                {
                    _dirty = false;
                    await SendActivityAsync(BuildActivity(), ct);
                }
                await Task.Delay(400, ct);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                Disconnect();    // pipe broke (Discord closed); try again shortly
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { break; }
            }
        }
        Disconnect();
    }

    object BuildActivity()
    {
        string details, state;
        lock (_lock) { details = _details; state = _state; }
        var act = new Dictionary<string, object>
        {
            ["details"] = details,
            ["timestamps"] = new { start = _startUnix },
            ["assets"] = new { large_image = LargeImageKey, large_text = "Tweakr" }
        };
        if (state.Length >= 2) act["state"] = state;     // Discord rejects 1-character strings
        return act;
    }

    async Task<bool> ConnectAsync(CancellationToken ct)
    {
        for (int i = 0; i < 10; i++)
        {
            var p = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await p.ConnectAsync(400, ct);
                await WriteFrameAsync(p, 0, JsonSerializer.Serialize(new { v = 1, client_id = ClientId }), ct);   // handshake
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(3000);
                var (op, _) = await ReadFrameAsync(p, timeout.Token);
                if (op == 2) { p.Dispose(); continue; }
                _pipe = p;
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { p.Dispose(); throw; }
            catch { p.Dispose(); }
        }
        return false;
    }

    async Task SendActivityAsync(object? activity, CancellationToken ct)
    {
        var pipe = _pipe ?? throw new IOException("not connected");
        var msg = new
        {
            cmd = "SET_ACTIVITY",
            args = new { pid = Environment.ProcessId, activity },     // activity = null clears the presence
            nonce = Guid.NewGuid().ToString()
        };
        await WriteFrameAsync(pipe, 1, JsonSerializer.Serialize(msg), ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(3000);
        while (true)    // skip pings until the response arrives
        {
            var (op, json) = await ReadFrameAsync(pipe, timeout.Token);
            if (op == 3) { await WriteFrameAsync(pipe, 4, json, ct); continue; }
            if (op == 2) throw new IOException("Discord closed the connection");
            break;
        }
    }

    void Disconnect() { try { _pipe?.Dispose(); } catch { } _pipe = null; }

    // frame = [int32 opcode][int32 length][utf8 json], little-endian
    static async Task WriteFrameAsync(Stream s, int op, string json, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var buf = new byte[8 + payload.Length];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), op);
        BitConverter.TryWriteBytes(buf.AsSpan(4, 4), payload.Length);
        payload.CopyTo(buf, 8);
        await s.WriteAsync(buf, ct);
        await s.FlushAsync(ct);
    }

    static async Task<(int op, string json)> ReadFrameAsync(Stream s, CancellationToken ct)
    {
        var header = new byte[8];
        await ReadExactAsync(s, header, ct);
        int op = BitConverter.ToInt32(header, 0), len = BitConverter.ToInt32(header, 4);
        if (len < 0 || len > (1 << 20)) throw new IOException("bad frame");
        var body = new byte[len];
        await ReadExactAsync(s, body, ct);
        return (op, Encoding.UTF8.GetString(body));
    }

    static async Task ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(read), ct);
            if (n == 0) throw new IOException("pipe closed");
            read += n;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop.Wait(1000); } catch { }
        Disconnect();
    }
}
