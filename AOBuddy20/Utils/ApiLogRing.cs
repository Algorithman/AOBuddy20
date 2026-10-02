// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: ApiLogRing.cs
//
// Last modified: 2026-10-02
// Created:       2026-10-02 (ported from AOBuddy10 Main.cs's log ring)
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using Newtonsoft.Json;
using Serilog.Core;
using Serilog.Events;

namespace AOBuddy20.Utils;

/// <summary>
///     The /log ring (AOBuddyMonitor, 2026-09-26): a Serilog sink that keeps the last 400 lines
///     (Information and above - the Debug chatter would flush the ring in seconds) with sequence
///     numbers, so the monitor and the MCP can follow with GET /log?after=N and never miss one.
///     The seq is a monotonic high-water mark: an empty lines array means "caught up"; a seq that
///     goes BACKWARDS means the bot restarted and the follower should drop its buffer and take
///     everything. Emit runs on whatever thread logs; Json takes the lock, so it is safe from the
///     API thread.
/// </summary>
public sealed class ApiLogRing : ILogEventSink
{
    private readonly object _gate = new();
    private readonly List<(int Seq, string T, string Line)> _ring = new();
    private readonly int _capacity;
    private int _seq;

    public ApiLogRing(int capacity)
    {
        _capacity = capacity;
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent == null || logEvent.Level < LogEventLevel.Information)
        {
            return;
        }

        lock (_gate)
        {
            _ring.Add((++_seq, logEvent.Timestamp.LocalDateTime.ToString("HH:mm:ss"), logEvent.RenderMessage()));
            if (_ring.Count > _capacity)
            {
                _ring.RemoveRange(0, _ring.Count - _capacity);
            }
        }
    }

    /// <summary>The current high-water mark (safe from any thread).</summary>
    public int Seq
    {
        get
        {
            lock (_gate)
            {
                return _seq;
            }
        }
    }

    /// <summary>
    ///     { "seq": high-water, "lines": [ { "seq", "t", "line" } ... ] } - the lines after
    ///     <paramref name="after" /> (the whole ring for after &lt; 0), as indented JSON.
    /// </summary>
    public string Json(int after)
    {
        lock (_gate)
        {
            var lines = new Newtonsoft.Json.Linq.JArray();
            foreach (var (seq, t, line) in _ring)
            {
                if (seq <= after)
                {
                    continue;
                }

                lines.Add(new Newtonsoft.Json.Linq.JObject
                {
                    ["seq"] = seq,
                    ["t"] = t,
                    ["line"] = line,
                });
            }

            return new Newtonsoft.Json.Linq.JObject
            {
                ["seq"] = _seq,
                ["lines"] = lines,
            }.ToString(Formatting.Indented);
        }
    }
}