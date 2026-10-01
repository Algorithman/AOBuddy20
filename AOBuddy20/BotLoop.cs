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

using AOBuddy20.Controlling;
using AOBuddy20.Enums;
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
    private readonly ControlArbiter _controlArbiter;
    private readonly ILogger<BotLoop> _logger;
    private readonly MissionController _missionController;
    private bool _running;

    public BotLoop(ControlArbiter controlArbiter, MissionController missionController, ILogger<BotLoop> logger)
    {
        _controlArbiter = controlArbiter;
        _missionController = missionController;
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