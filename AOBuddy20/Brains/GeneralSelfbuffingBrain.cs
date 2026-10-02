// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: GeneralSelfbuffingBrain.cs
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
///     The GENERAL selfbuffing brain: the fallback when no brain for the character's profession
///     is registered (and the base profession brains inherit from). DORMANT for now - it makes
///     no decisions, enqueues nothing and claims nothing, logging once that it is dormant,
///     until the selfbuffing family is implemented.
/// </summary>
[MinLogLevel(LogEventLevel.Debug)]
[Brain(BrainKind.Selfbuffing)]
public class GeneralSelfbuffingBrain : SelfbuffingBrain
{
    private bool _loggedDormant;

    public GeneralSelfbuffingBrain(ILogger<GeneralSelfbuffingBrain> logger, ControlArbiter controlArbiter,
        HealController heal)
        : base(logger, controlArbiter, heal)
    {
    }

    protected override bool PolicyTick(LocalPlayer me, double dt)
    {
        if (!_loggedDormant)
        {
            _loggedDormant = true;
            _logger.LogInformation("SELFBUFF: general brain is dormant - no self-buffing this session.");
        }

        return false;
    }
}