// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: BotLoop.cs
//
// Last modified: 2026-10-01
// Created:       2026-09-29 23:09
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using AOBuddy20.Configuration;
using AOBuddy20.Controlling;
using AOBuddy20.Enums;
using AOBuddy20.Nav;
using AOBuddy20.Utils;
using AOSharp.Clientless;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace AOBuddy20;

/// <summary>
///     The bot's decision loop (Readme points 1 and 4): ticks on the SDK's update thread - the same
///     thread that processes the packets, and the only one allowed to touch SDK state (review.md #9) -
///     while the movement cycle runs on its own thread in the MovementController. Attack/Use and the
///     task switch belong here; nothing here tells the body HOW to move, only WHERE
///     (MovementController.SetDesiredGoal).
/// </summary>
[MinLogLevel(LogEventLevel.Debug)]
public sealed class BotLoop
{
    private readonly AccountInfo _config;
    private readonly ControlArbiter _controlArbiter;
    private readonly ILogger<BotLoop> _logger;
    private readonly MissionController _missionController;
    private readonly ResupplyController _resupply;
    private readonly SellController _sell;
    private readonly NavController _navMemory;
    private bool _running;

    public BotLoop(ControlArbiter controlArbiter, MissionController missionController, ResupplyController resupply, SellController sell, NavController navMemory, AccountInfo config, ILogger<BotLoop> logger)
    {
        _controlArbiter = controlArbiter;
        _missionController = missionController;
        _resupply = resupply;
        _sell = sell;
        _navMemory = navMemory;
        _config = config;
        _logger = logger;
    }

    /// <summary>What the bot is working on. Nothing idles; movement keeps running either way.</summary>
    public Tasks CurrentTask { get; set; } = Tasks.Nothing;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        Client.OnUpdate += OnUpdate;
        _logger.LogInformation("Bot loop started.");
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        Client.OnUpdate -= OnUpdate;
        _logger.LogInformation("Bot loop stopped.");
    }

    // Client.Update invokes OnUpdate only while in play, ~64 times a second (UpdateLoop). The
    // UpdateLoop carries no exception guard: one throw in here would kill the SDK's update thread
    // and with it all packet processing, so the tick is guarded end to end.
    private void OnUpdate(object? sender, double deltaTime)
    {
        try
        {
            // NAV memory (AOBuddy10 NavController): record the owner's footsteps and catalogue the
            // playfield's objects and mob spawns, so ground already walked is never guessed again.
            // Zone transitions are not recorded yet - they need the POSITION-JUMP (teleport)
            // detection, which is not ported (realCrossing: false).
            var me = DynelManager.LocalPlayer;
            if (me != null)
            {
                _navMemory.SetPlayfield((int)Playfield.ModelId, Playfield.Name, null, realCrossing: false);
                var owner = DynelManager.Players.FirstOrDefault(pl =>
                    string.Equals(pl.Name, _config.Owner, StringComparison.OrdinalIgnoreCase));
                _navMemory.RecordOwner(owner?.Transform.Position ?? default,
                    owner != null); // not visible: close the run, a break never becomes a segment
                _navMemory.Tick(deltaTime);

                // RESUPPLY (AOBuddy10 ResupplyController): the decision tick runs here on the update
                // thread, the same one its packet handlers fire on. While a run is active it owns
                // the body through a MovementController goal at ControlPriority.Resupply and holds
                // the arbiter at that priority; idle, it only answers the owner's trade. SELLING
                // (SellController) runs the same way one priority down.
                if (_resupply.Tick(me, deltaTime))
                {
                    CurrentTask = Tasks.Resupply;
                }
                else if (_sell.Tick(me, deltaTime))
                {
                    CurrentTask = Tasks.SellGoods;
                }
                else if (CurrentTask is Tasks.Resupply or Tasks.SellGoods)
                {
                    CurrentTask = Tasks.Nothing;
                }
            }

            switch (CurrentTask)
            {
                /*
                case Tasks.Mission: await _missionController.RunAsync(ct); break;
                case Tasks.Resupply: await _resupplier.RunAsync(ct); break;
                case Tasks.Buff: await _buffing.RunAsync(ct); break;
                */
                case Tasks.Nothing:
                default:
                    break; // idle: nothing to decide this tick, movement is on its own thread
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BotLoop tick failed.");
        }
    }
}