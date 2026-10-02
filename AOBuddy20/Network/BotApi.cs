// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: BotApi.cs
//
// Last modified: 2026-10-02
// Created:       2026-10-02 (ported from AOBuddy10 BotApi.cs)
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;

namespace AOBuddy20.Network;

/// <summary>
///     LOCAL CONTROL API (ported from AOBuddy10, owner 2026-09-24): lets the monitor, the
///     aobuddy MCP server (tools/aobuddy-mcp) and run-bot.ps1 steer and read the bot the way
///     the owner does by tell. Listens on 127.0.0.1 ONLY - nothing off this PC can reach it -
///     on AccountInfo.BotApiPort (0 = off). A plain TcpListener with a minimal HTTP reader, so
///     no URL reservation or admin rights are needed.
///       GET  /status          -> the cached status JSON (BotApiService rebuilds it each second)
///       GET  /nav             -> the cached nav JSON
///       GET  /inventory       -> the cached inventory JSON
///       GET  /log?after=N     -> { seq, lines } past N (ApiLogRing)
///       POST /command  (body) -> runs the text exactly as an owner tell; replies collected ~2.5 s
///     STATELESS PLUMBING: this class never touches SDK state. The JSON it serves are cached
///     strings rebuilt on the update thread (BotApiService), and a command only ENQUEUES here -
///     the drain runs on the update thread, never on the listener thread.
/// </summary>
public sealed class BotApi
{
    private readonly int _port;
    private readonly Action<string> _log;
    private readonly Func<string> _status;
    private readonly Func<string> _nav;
    private readonly Func<string> _inventory;
    private readonly Func<int, string> _logAfter;
    private TcpListener? _listener;
    private Thread? _thread;
    private volatile bool _running;

    /// <summary>Commands waiting to run on the update thread (Drain).</summary>
    private readonly ConcurrentQueue<(string Text, Action<string> Reply)> _commands = new();

    public BotApi(int port, Action<string> log, Func<string> status, Func<string> nav, Func<string> inventory,
        Func<int, string> logAfter)
    {
        _port = port;
        _log = log;
        _status = status;
        _nav = nav;
        _inventory = inventory;
        _logAfter = logAfter;
    }

    public void Start()
    {
        if (_port <= 0)
        {
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "AOBuddy BotApi" };
            _thread.Start();
            _log($"API: listening on 127.0.0.1:{_port} (status, command) for the monitor and the MCP server.");
        }
        catch (Exception ex)
        {
            _log($"API: couldn't listen on 127.0.0.1:{_port}: {ex.Message}");
        }
    }

    public void Stop()
    {
        _running = false;
        try
        {
            _listener?.Stop();
        }
        catch
        {
        }
    }

    /// <summary>
    ///     Runs the queued commands through <paramref name="runner" /> - call from the update
    ///     thread only (the same thread the owner's tells are handled on).
    /// </summary>
    public void Drain(Action<string, Action<string>> runner)
    {
        while (_commands.TryDequeue(out var cmd))
        {
            try
            {
                runner(cmd.Text, cmd.Reply);
            }
            catch (Exception ex)
            {
                try
                {
                    cmd.Reply("error: " + ex.Message);
                }
                catch
                {
                }
            }
        }
    }

    private void Loop()
    {
        while (_running)
        {
            TcpClient c;
            try
            {
                c = _listener!.AcceptTcpClient();
            }
            catch
            {
                if (!_running)
                {
                    return;
                }

                continue;
            }

            ThreadPool.QueueUserWorkItem(_ => Serve(c));
        }
    }

    private void Serve(TcpClient c)
    {
        using (c)
        {
            try
            {
                c.ReceiveTimeout = 5000;
                var s = c.GetStream();
                // Request line + headers up to the blank line, then Content-Length bytes of body.
                var head = new StringBuilder();
                int b, crlf = 0;
                while (crlf < 4 && (b = s.ReadByte()) >= 0)
                {
                    head.Append((char)b);
                    crlf = (b == '\r' || b == '\n') ? crlf + 1 : 0;
                }

                var lines = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
                var req = lines[0].Split(' ');
                var method = req.Length > 0 ? req[0] : "";
                var path = req.Length > 1 ? req[1] : "/";
                // "?after=N" on /log: everything before it is the route, everything after is one query arg.
                int q = path.IndexOf('?');
                var args = new Dictionary<string, string>();
                if (q >= 0)
                {
                    foreach (var kv in path.Substring(q + 1).Split('&'))
                    {
                        var eq = kv.IndexOf('=');
                        if (eq > 0)
                        {
                            args[kv.Substring(0, eq)] = Uri.UnescapeDataString(kv.Substring(eq + 1));
                        }
                    }

                    path = path.Substring(0, q);
                }

                var len = 0;
                foreach (var l in lines)
                {
                    if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(l.Substring(15).Trim(), out len);
                    }
                }

                var body = new byte[Math.Max(0, Math.Min(len, 65536))];
                int got = 0;
                while (got < body.Length)
                {
                    var n = s.Read(body, got, body.Length - got);
                    if (n <= 0)
                    {
                        break;
                    }

                    got += n;
                }

                var text = Encoding.UTF8.GetString(body, 0, got);

                string result;
                if (method == "GET" && path.StartsWith("/status"))
                {
                    result = _status();
                }
                else if (method == "POST" && path.StartsWith("/command"))
                {
                    result = RunCommand(text);
                }
                else if (method == "GET" && path.StartsWith("/nav"))
                {
                    result = _nav();
                }
                else if (method == "GET" && path.StartsWith("/inventory"))
                {
                    result = _inventory();
                }
                else if (method == "GET" && path.StartsWith("/log"))
                {
                    result = _logAfter(args.TryGetValue("after", out var a) && int.TryParse(a, out var after) ? after : -1);
                }
                else
                {
                    result = "{ \"error\": \"GET /status, GET /nav, GET /inventory, GET /log or POST /command\" }";
                }

                Reply(s, result);
            }
            catch (Exception ex)
            {
                try
                {
                    _log($"API: request failed: {ex.Message}");
                }
                catch
                {
                }
            }
        }
    }

    // The command is ENQUEUED here and runs on the update thread; replies can come later than
    // the call (the command answers from its tick), so collect them until 0.6 s pass with none,
    // at most 2.5 s - the monitor's input waits for this and the HTTP timeout is 4 s.
    private string RunCommand(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
        {
            return "{ \"error\": \"empty command\" }";
        }

        var replies = new List<string>();
        var gate = new object();
        long last, start;
        last = start = Environment.TickCount64;
        _commands.Enqueue((text, r =>
        {
            lock (gate)
            {
                replies.Add(r);
                last = Environment.TickCount64;
            }
        }));
        while (true)
        {
            Thread.Sleep(100);
            long now;
            lock (gate)
            {
                now = Environment.TickCount64;
                if (now - start > 2500)
                {
                    break;
                }

                if (now - last > 600 && (replies.Count > 0 || now - start > 1200))
                {
                    break;
                }
            }
        }

        lock (gate)
        {
            var array = string.Join(", ", replies.Select(r => Newtonsoft.Json.JsonConvert.SerializeObject(r)));
            return $"{{\n  \"command\": {Newtonsoft.Json.JsonConvert.SerializeObject(text)},\n  \"replies\": [{array}]\n}}";
        }
    }

    private static void Reply(Stream s, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        s.Write(head, 0, head.Length);
        s.Write(body, 0, body.Length);
        s.Flush();
    }
}