// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: OwnerChat.cs
//
// Last modified: 2026-10-01
// Created:       2026-10-01
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using System.Globalization;
using AOBuddy20.Configuration;
using AOBuddy20.Controlling;
using AOBuddy20.Enums;
using AOBuddy20.Nav;
using AOBuddy20.Utils;
using AOSharp.Clientless;
using AOSharp.Clientless.Chat;
using AOSharp.Common.GameData;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace AOBuddy20.Chat;

/// <summary>
///     The owner link, ported from AOBuddy10: the bot obeys /tells from the configured owner
///     character (AccountInfo.Owner) and answers by tell. Tells from anyone else are logged but
///     never obeyed. Commands are handled inline in PrivateMessageReceived - chat packets are
///     pumped by Client.Update on the update thread (the thread allowed to touch SDK state,
///     review.md #9), the same thread OnUpdate and the command handlers' state live on.
///     Commands: help, pos, status, goto x [y] z, come, travel, stop.
/// </summary>
[MinLogLevel(LogEventLevel.Debug)]
public sealed class OwnerChat
{
    private readonly AccountInfo _config;
    private readonly Dictionary<string, Action<Action<string>, string[]>> _commands;
    private readonly ILogger<OwnerChat> _logger;
    private readonly MovementController _movement;
    private readonly ResupplyController _resupply;

    private bool _greeted;
    private bool _running;

    // The owner's tell-sender id, proven once by name: a tell's SenderName is looked up in the
    // chat client's IdToNameMap and is literally "&lt;Unknown&gt;" until that map knows the id,
    // so a name-only check silently drops the owner's own commands (AOBuddy10 OwnerTracker).
    private uint _tellId;

    public OwnerChat(MovementController movement, ResupplyController resupply, AccountInfo config, ILogger<OwnerChat> logger)
    {
        _movement = movement;
        _resupply = resupply;
        _config = config;
        _logger = logger;
        _commands = BuildCommands();
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        if (Client.Chat == null)
        {
            _logger.LogWarning("No chat client - owner tells cannot reach the bot.");
            return;
        }

        _running = true;
        Client.Chat.PrivateMessageReceived += OnTell;
        Client.OnUpdate += GreetWhenOwnerVisible;
        _logger.LogInformation(string.IsNullOrEmpty(_config.Owner)
            ? "Owner chat started. No owner configured - tells will be logged but NOT obeyed."
            : $"Owner chat started. Obeying tells from '{_config.Owner}'.");
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        if (Client.Chat != null)
        {
            Client.Chat.PrivateMessageReceived -= OnTell;
        }

        Client.OnUpdate -= GreetWhenOwnerVisible;
        _logger.LogInformation("Owner chat stopped.");
    }

    // ── the link (update thread: chat packets are pumped in Client.Update) ─────────────────

    private void OnTell(object? sender, PrivateMessage msg)
    {
        // The owner's AFK auto-reply ('Veganbacon is AFK (Away from keyboard) since ...') answers
        // every tell we send him; it is not a command (AOBuddy10, owner 2026-09-25).
        if ((msg.Message ?? "").IndexOf(" is AFK (Away from keyboard)", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return;
        }

        if (!IsOwnerSender(msg.SenderName, msg.SenderId))
        {
            // Never obeyed, but logged: answers we provoked are visible in the record.
            _logger.LogInformation($"TELL (not obeyed) from {msg.SenderName} (id={msg.SenderId}): {msg.Message}");
            return;
        }

        _logger.LogInformation($"CMD from {msg.SenderName}: '{msg.Message}'");
        _resupply.SetTellId(msg.SenderId); // async replies (buys, credit nags) answer this id
        try
        {
            HandleCommand(msg.Message, text => Client.SendPrivateMessage(msg.SenderId, text));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command error.");
            try
            {
                Client.SendPrivateMessage(msg.SenderId, $"Command error: {ex.Message}");
            }
            catch
            {
                // answering is best-effort
            }
        }
    }

    // Name match first; then the remembered tell id; then the owner's dynel id while he is in
    // view. All three because the chat name map can lag behind the first tell.
    private bool IsOwnerSender(string name, uint senderId)
    {
        if (!string.IsNullOrEmpty(_config.Owner) &&
            string.Equals(name, _config.Owner, StringComparison.OrdinalIgnoreCase))
        {
            if (senderId != 0)
            {
                _tellId = senderId;
            }

            return true;
        }

        if (senderId != 0 && senderId == _tellId)
        {
            return true;
        }

        var owner = DynelManager.Players.FirstOrDefault(p =>
            string.Equals(p.Name, _config.Owner, StringComparison.OrdinalIgnoreCase));
        return owner != null && senderId != 0 && (uint)owner.Identity.Instance == senderId;
    }

    // AOBuddy10 greeted the owner when it could: "AOBuddy online - send 'help' for commands."
    private void GreetWhenOwnerVisible(object? sender, double deltaTime)
    {
        if (_greeted || string.IsNullOrEmpty(_config.Owner))
        {
            return;
        }

        var owner = DynelManager.Players.FirstOrDefault(p =>
            string.Equals(p.Name, _config.Owner, StringComparison.OrdinalIgnoreCase));
        if (owner == null)
        {
            return; // not in view yet; tried again next tick
        }

        _greeted = true;
        Client.OnUpdate -= GreetWhenOwnerVisible;
        try
        {
            Client.SendPrivateMessage((uint)owner.Identity.Instance, "AOBuddy20 online - send 'help' for commands.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Greeting failed.");
        }
    }

    // ── commands ──────────────────────────────────────────────────────────────────────────

    private void HandleCommand(string? message, Action<string> reply)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var parts = message.Trim().TrimStart('!').Split(' ');
        var cmd = parts[0].ToLowerInvariant();
        if (_commands.TryGetValue(cmd, out var handler))
        {
            handler(reply, parts);
        }
        else
        {
            reply($"Unknown command '{cmd}'. Try 'help'.");
        }
    }

    private Dictionary<string, Action<Action<string>, string[]>> BuildCommands()
    {
        var t = new Dictionary<string, Action<Action<string>, string[]>>();

        t["help"] = (reply, p) =>
        {
            reply("Commands: follow | stay | pos | status | goto x [y] z | come | travel <pf> | travel x z <pf> | resupply [stop|status|forget|machines] | stop | sit | stand | navdata | help." +
                  " follow stacks me on you and mirrors your movement; goto/come walk at priority Travel and hand me back to follow on arrival;" +
                  " travel crosses playfields by zone lines, whompas and pads (id or name); resupply shops for stims and rechargers by my own skills.");
        };

        // FOLLOW (AOBuddy10's stack/mirror tier): once on, the body's idle state is the owner - run
        // to him, stack on his spot, and replay his movement packets as ours. Any goal preempts it;
        // when the last goal clears, follow resumes on its own.
        t["follow"] = (reply, p) =>
        {
            _movement.SetFollow(true);
            reply("Following: I'll stack on you and mirror your movement.");
        };

        t["stay"] = (reply, p) =>
        {
            _movement.SetFollow(false);
            reply("Staying put (follow off).");
        };

        t["pos"] = (reply, p) => { reply(_movement.DescribeState()); };

        t["status"] = (reply, p) => { reply(_movement.DescribeState()); };

        // Walk to bare coordinates on this playfield. Y is optional: the walk takes its Y from the nav
        // data's floor, so a bare 'goto x z' keeps our height (y=0 would aim underground, 2026-10-01).
        t["goto"] = (reply, p) =>
        {
            if (p.Length < 3 || !TryParse(p[1], out var x) || !TryParse(p[p.Length - 1], out var z))
            {
                reply("Usage: goto <x> [y] <z>");
                return;
            }

            float y;
            if (p.Length >= 4)
            {
                if (!TryParse(p[2], out y))
                {
                    reply("Usage: goto <x> [y] <z>");
                    return;
                }
            }
            else
            {
                y = _movement.CurrentPosition.Y;
            }

            var pf = (int)Playfield.ModelId;
            _movement.CancelTravel(); // a manual order takes the body from any travel plan (owner, 2026-10-01)
            _movement.SetDesiredGoal(new Vector3(x, y, z), pf, ControlPriority.Travel);
            reply($"Walking to ({x:0.0} {y:0.0} {z:0.0}), playfield {pf}, priority Travel.");
        };

        // The nav data's own verdict about where we stand (AOBuddy10's 'navdata'), to check the data
        // against the live character before anything relies on it.
        t["navdata"] = (reply, p) => { reply(_movement.ExplainNav()); };

        t["come"] = (reply, p) =>
        {
            var owner = DynelManager.Players.FirstOrDefault(o =>
                string.Equals(o.Name, _config.Owner, StringComparison.OrdinalIgnoreCase));
            if (owner == null)
            {
                reply("Can't see you (out of range?).");
                return;
            }

            _movement.CancelTravel(); // a manual order takes the body from any travel plan (owner, 2026-10-01)
            _movement.SetDesiredGoal(owner.Transform.Position, (int)Playfield.ModelId, ControlPriority.Travel);
            reply("On my way.");
        };

        // TRAVEL (cross-playfield): the Zoning graph plans the cheapest way - zone lines walked across,
        // whompas used, pads walked onto - then a plain walk to the coordinates. 'travel <playfield>' or
        // 'travel <x> <z> <playfield>'; the playfield takes its id or any unambiguous part of its name.
        t["travel"] = (reply, p) =>
        {
            string pfText;
            Vector3? target = null;
            if (p.Length == 2)
            {
                pfText = p[1];
            }
            else if (p.Length == 4 && TryParse(p[1], out var tx) && TryParse(p[2], out var tz))
            {
                pfText = p[3];
                target = new Vector3(tx, _movement.CurrentPosition.Y, tz);
            }
            else
            {
                reply("Usage: travel <playfield> | travel <x> <z> <playfield>");
                return;
            }

            var pf = Zoning.FindPlayfield(pfText);
            if (pf == 0)
            {
                reply($"Unknown playfield '{pfText}'.");
                return;
            }

            reply(_movement.PlanTravel(pf, target));
        };

        t["stop"] = (reply, p) =>
        {
            _resupply.Stop("owner stop"); // stop means stop: an open shop run ends with everything else
            _movement.SetFollow(false); // stop means stop: follow must not grab the body back (owner, 2026-10-01)
            _movement.ClearAllGoals();
            reply("Stopped - follow off, no goals. Standing down.");
        };

        // RESUPPLY (AOBuddy10's command, moved verbatim): bare 'resupply' shops for stims and
        // rechargers; stop/status/forget/machines manage the run and its terminal memory.
        t["resupply"] = (reply, p) => { _resupply.Command(p, reply); };

        // Sit/stand go through the movement controller's posture track: the bot then knows it is
        // seated and stands up on the next movement order (the 'come' after a 'sit').
        t["stand"] = (reply, p) =>
        {
            _movement.StandNow();
            reply("Standing up.");
        };

        t["sit"] = (reply, p) =>
        {
            _movement.SetFollow(false); // a sit ends follow: standing up later must not resume it (owner, 2026-10-01)
            _movement.SitNow();
            reply("Sitting down - follow off, goals cleared. 'come' or 'goto' stands me up.");
        };

        return t;
    }

    private static bool TryParse(string s, out float value)
    {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}