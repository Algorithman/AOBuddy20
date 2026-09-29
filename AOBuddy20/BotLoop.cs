// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: BotLoop.cs
// 
// Last modified: 2026-09-30 00:19
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

[MinLogLevel(LogEventLevel.Debug)]
public sealed class BotLoop : ClientlessPluginEntry
{
    private readonly Tasks CurrentTask = Tasks.Nothing;
    private readonly ControlArbiter _controlArbiter;
    private readonly ILogger<BotLoop> _logger;
    private readonly MissionController _missionController;

    public BotLoop(ControlArbiter controlArbiter, MissionController missionController, ILogger<BotLoop> logger)
    {
        _controlArbiter = controlArbiter;
        _missionController = missionController;
        _logger = logger;
        _logger.LogInformation("Bot loop started");
    }


    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                switch (CurrentTask)
                {
                    /*
                    case Tasks.Mission: await _mission.RunAsync(ct); break;
                    case Tasks.Resupply: await _resupplier.RunAsync(ct); break;
                    case Tasks.Buff: await _buffing.RunAsync(ct); break;
                    */
                    // no current task? Just do nothing 
                    default: await Task.Delay(TimeSpan.FromMilliseconds(50)); break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // log, back off, retry
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }

    public override void Init(string pluginDir)
    {
    }
}