// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: GeneralExternalBuffingBrain.cs
//
// Last modified: 2026-10-02
// Created:       2026-10-02
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using AOBuddy20.Controlling;
using AOBuddy20.Enums;
using AOBuddy20.Utils;
using AOSharp.Clientless;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace AOBuddy20.Brains;

/// <summary>
///     The GENERAL external buffing brain: the fallback when no brain for the character's
///     profession is registered (and the base profession brains inherit from). DORMANT for
///     now - it asks nobody for anything and stays in Stage.Idle, logging once that it is
///     dormant, until the external buffing family is implemented.
/// </summary>
[MinLogLevel(LogEventLevel.Debug)]
[Brain(BrainKind.ExternalBuffing)]
public class GeneralExternalBuffingBrain : ExternalBuffingBrain
{
    private bool _loggedDormant;

    public GeneralExternalBuffingBrain(ILogger<GeneralExternalBuffingBrain> logger, ControlArbiter controlArbiter)
        : base(logger, controlArbiter)
    {
    }

    protected override bool PolicyTick(LocalPlayer me, double dt)
    {
        if (!_loggedDormant)
        {
            _loggedDormant = true;
            _logger.LogInformation("EXTBUFF: general brain is dormant - no external buffing this session.");
        }

        return false;
    }
}