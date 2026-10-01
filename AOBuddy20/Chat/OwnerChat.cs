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
///     Commands: help, pos, status, goto x [y] z, come, stop.
/// </summary>
[MinLogLevel(LogEventLevel.Debug)]
public sealed class OwnerChat
{
    private readonly AccountInfo _config;
    private readonly Dictionary<string, Action<Action<string>, string[]>> _commands;
    private readonly ILogger<OwnerChat> _logger;
    private readonly MovementController _movement;

    private bool _greeted;
    private bool _running;

    // The owner's tell-sender id, proven once by name: a tell's SenderName is looked up in the
    // chat client's IdToNameMap and is literally "&lt;Unknown&gt;" until that map knows the id,
    // so a name-only check silently drops the owner's own commands (AOBuddy10 OwnerTracker).
    private uint _tellId;

    public OwnerChat(MovementController movement, AccountInfo config, ILogger<OwnerChat> logger)
    {
        _movement = movement;
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
            reply("Commands: pos | status | goto <x> [y] <z> | come | stop | help." +
                  " goto/come walk at priority Travel; anything the bot does later preempts them.");
        };

        t["pos"] = (reply, p) => { reply(_movement.DescribeState()); };

        t["status"] = (reply, p) => { reply(_movement.DescribeState()); };

        // Walk to bare coordinates on this playfield. Y is optional: the walk steers on the X/Z
        // plane and the ground decides Y (Movement.Flat), so 'goto x z' is enough outdoors.
        t["goto"] = (reply, p) =>
        {
            if (p.Length < 3 || !TryParse(p[1], out var x) || !TryParse(p[p.Length - 1], out var z))
            {
                reply("Usage: goto <x> [y] <z>");
                return;
            }

            var y = 0f;
            if (p.Length >= 4 && !TryParse(p[2], out y))
            {
                reply("Usage: goto <x> [y] <z>");
                return;
            }

            var pf = (int)Playfield.ModelId;
            _movement.SetDesiredGoal(new Vector3(x, y, z), pf, ControlPriority.Travel);
            reply($"Walking to ({x:0.0} {y:0.0} {z:0.0}), playfield {pf}, priority Travel.");
        };

        t["come"] = (reply, p) =>
        {
            var owner = DynelManager.Players.FirstOrDefault(o =>
                string.Equals(o.Name, _config.Owner, StringComparison.OrdinalIgnoreCase));
            if (owner == null)
            {
                reply("Can't see you (out of range?).");
                return;
            }

            _movement.SetDesiredGoal(owner.Transform.Position, (int)Playfield.ModelId, ControlPriority.Travel);
            reply("On my way.");
        };

        t["stop"] = (reply, p) =>
        {
            _movement.ClearAllGoals();
            reply("Stopped - no goals. Standing down.");
        };

        return t;
    }

    private static bool TryParse(string s, out float value)
    {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}