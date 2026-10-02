// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: HealController.cs
//
// Last modified: 2026-10-02
// Created:       2026-10-02
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using AOBuddy20.Configuration;
using AOBuddy20.Enums;
using AOBuddy20.Utils;
using AOSharp.Clientless;
using AOSharp.Common.GameData;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace AOBuddy20.Controlling;

/// <summary>
///     LOW HEALTH / NANO EMERGENCY (<see cref="ControlPriority.LowHealthNanoEmergency" />): pop the
///     heal items the bot already carries, in two scenarios.
///     IN COMBAT (attacking, fought, or hurt in the last CombatLingerSeconds) - Health and Nano Stims:
///       - nano under HealNanoCombatPct of max, or
///       - health, as soon as one stim's heal capacity no longer covers the missing health - from then
///         on every cooldown pops another until the wound is closed.
///     OUT OF COMBAT - Health and Nano Rechargers, when any health is missing or more than 30% of the
///     nano is (under HealNanoOutOfCombatPct of max). A convenience top-up, not an emergency: it takes
///     no arbiter control and does not claim the task.
///     The deficits above are NET of what the BUFFING controllers report (SetRegen): the combined
///     heal-over-time and nano-over-time of all running buffs, valid for the rest of the shortest
///     one. In combat only one cooldown's worth of that regen counts (the next item may go in when
///     the timer is up, so regen beyond it does not argue against this one); out of combat the whole
///     rest of the buffs counts - a long HoT that will close a wound on its own is exactly the case
///     the recharger is not for. Thresholds stay owner config: buffs change the need, not the rules,
///     and the forecast decays with the tick, so a buff side that stops reporting fades out by itself.
///     An in-combat episode holds the ControlArbiter at the priority until the wounds are closed - the
///     cooldowns between the stims of one episode included. (ReleaseControl, like resupply's and sell's,
///     writes None unconditionally: a system above us that took control meanwhile re-takes its own.)
///     Stims and rechargers sit on the game's shared stim timer, so one gate covers both. The names
///     come from the resupply config (ResupplyStimName / ResupplyRechargerName), and stock is counted
///     through HealItems - shopping and spending agree on what a stim is.
///     The decision tick runs on the update thread (BotLoop), the same one the packet handlers run on.
/// </summary>
[MinLogLevel(LogEventLevel.Debug)]
public sealed class HealController
{
    // Stims and rechargers share the game's 20 s stim timer; +1 s so a use the server put on the
    // last tick of its timer cannot race this gate.
    private const double UseCooldownSeconds = 21.0;

    // The fight is not over when the last blow lands: blows in flight, dots ticking, the mob still
    // swinging. A drop in health keeps "in combat" alive this long after the drop.
    private const double CombatLingerSeconds = 10.0;

    private enum Kind { Stim, Recharger }

    private readonly ILogger<HealController> _logger;
    private readonly ControlArbiter _controlArbiter;
    private readonly AccountInfo _config;

    private double _clock; // the controller's own clock, accumulated from dt
    private double _cooldownLeft;
    private int _lastHealth = -1;
    private double _lastHurtAt = double.NegativeInfinity;
    private int _pf = -1;

    private bool _holding; // an in-combat episode: arbiter held at LowHealthNanoEmergency
    private bool _loggedShort; // "none usable" once per episode, not once per tick

    // The buff controllers' regen forecast (SetRegen): what all running buffs pour in per second,
    // and how much longer the shortest of them lasts. _regenRemaining decays with the tick, so a
    // buff side that stops reporting fades out by construction - no staleness flag needed.
    private double _regenHealth, _regenNano, _regenRemaining;

    /// <summary>
    ///     The buff controllers' (Selfbuffing / ExternalBuffing) answer to "what are the running
    ///     buffs worth": their combined heal-over-time and nano-over-time per second, valid for
    ///     remainingSeconds (the shortest buff's rest). Replaces the previous forecast. The rates
    ///     never FIRE a trigger - they only shrink the deficits the triggers measure, over one
    ///     cooldown in combat and over the buffs' whole rest out of combat.
    /// </summary>
    public void SetRegen(double healthPerSecond, double nanoPerSecond, double remainingSeconds)
    {
        _regenHealth = Math.Max(0, healthPerSecond);
        _regenNano = Math.Max(0, nanoPerSecond);
        _regenRemaining = Math.Max(0, remainingSeconds);
    }

    public HealController(ILogger<HealController> logger, ControlArbiter controlArbiter, AccountInfo config)
    {
        _logger = logger;
        _controlArbiter = controlArbiter;
        _config = config;
        _logger.LogInformation("Heal controller initialized.");
    }

    /// <summary>
    ///     The decision tick. True while an IN-COMBAT episode is open - the wounds not yet closed,
    ///     the cooldown waits between its stims included.
    /// </summary>
    public bool Tick(LocalPlayer me, double dt)
    {
        _clock += dt;
        if (_cooldownLeft > 0)
        {
            _cooldownLeft -= dt;
        }

        if (_regenRemaining > 0)
        {
            _regenRemaining = Math.Max(0, _regenRemaining - dt);
        }

        if (me == null)
        {
            EndEpisode();
            return false;
        }

        var pf = (int)Playfield.ModelId;
        if (pf != _pf)
        {
            // A zone change ends every fight and every wound count we were tracking.
            _pf = pf;
            _lastHealth = -1;
            _lastHurtAt = double.NegativeInfinity;
        }

        if (!me.TryGetStat(Stat.Health, out var health) ||
            !me.TryGetStat(Stat.MaxHealth, out var maxHealth) ||
            !me.TryGetStat(Stat.CurrentNano, out var nano) ||
            !me.TryGetStat(Stat.MaxNanoEnergy, out var maxNano) ||
            maxHealth <= 0 || maxNano <= 0)
        {
            EndEpisode(); // stats unreadable: nothing fires blind
            return false;
        }

        if (health <= 0)
        {
            EndEpisode(); // dead - the claws are for the living
            return false;
        }

        // Every drop in health marks the hurt time; "in combat" stays alive CombatLingerSeconds past it.
        if (_lastHealth >= 0 && health < _lastHealth)
        {
            _lastHurtAt = _clock;
        }

        _lastHealth = health;

        var missingHealth = maxHealth - health;
        var nanoPct = nano * 100.0 / maxNano;
        var combat = me.IsAttacking || FoughtOver(me) || _clock - _lastHurtAt < CombatLingerSeconds;

        // THE WANT, per scenario: in combat stims, out of combat rechargers. Both deficits are NET
        // of the buffs' regen: what the running HoTs pour in over the horizon we care about is need
        // we do not have to spend an item on.
        string reason;
        bool want;
        Kind kind;
        if (combat)
        {
            kind = Kind.Stim;
            // In combat the horizon is one cooldown: the next item may go in when the timer is up,
            // so regen beyond it does not argue against this one.
            var horizon = Math.Min(_regenRemaining, UseCooldownSeconds);
            var missingNet = missingHealth - _regenHealth * horizon;
            var nanoShortNet = _config.HealNanoCombatPct / 100.0 * maxNano - nano - _regenNano * horizon;
            want = nanoShortNet > 0 || (missingNet > 0 && HealCapacity(me) < missingNet);
            reason = want
                ? nanoShortNet > 0
                    ? $"in combat, nano {nanoPct:0}% short {nanoShortNet:0}{RegenNote()}"
                    : $"in combat, one stim heals {HealCapacity(me)} < missing {missingNet:0}{RegenNote()}"
                : "";
        }
        else
        {
            kind = Kind.Recharger;
            // Out of combat the horizon is the whole rest of the buffs: a long HoT that will close
            // the wound on its own is exactly the case the recharger is not for.
            var missingNet = missingHealth - _regenHealth * _regenRemaining;
            var nanoShortNet = _config.HealNanoOutOfCombatPct / 100.0 * maxNano - nano - _regenNano * _regenRemaining;
            want = missingNet > 0 || nanoShortNet > 0;
            reason = want
                ? $"out of combat, {(missingNet > 0 ? $"{missingNet:0} health missing" : $"nano {nanoPct:0}%")}{RegenNote()}"
                : "";
        }

        if (!want)
        {
            EndEpisode();
            return false;
        }

        if (combat && !_holding)
        {
            // The episode takes the arbiter at its priority, so lower systems yield while the
            // wounds are being closed - resupply's walk home, a sale in flight.
            _holding = true;
            _controlArbiter.TakeControl(ControlPriority.LowHealthNanoEmergency);
            _logger.LogInformation($"HEAL: episode opens - {reason}.");
        }

        if (_cooldownLeft > 0)
        {
            return _holding; // mid-episode, the timer runs
        }

        var it = BestUsable(kind, me);
        if (it == null)
        {
            if (!_loggedShort)
            {
                _loggedShort = true;
                _logger.LogInformation(
                    $"HEAL: want {Name(kind)} ({reason}) but none usable in the packs - resupply stocks them.");
            }

            return _holding;
        }

        var left = Carry(kind, me) - 1;
        it.Use();
        _cooldownLeft = UseCooldownSeconds;
        _logger.LogInformation($"HEAL: used {it.Name} QL {it.Ql} ({reason}); {left} left.");
        return _holding;
    }

    private void EndEpisode()
    {        if (_holding)
        {
            _holding = false;
            _controlArbiter.ReleaseControl();
            _logger.LogInformation("HEAL: episode closed.");
        }

        _loggedShort = false;
    }

    // Someone is fighting US - not near us, not fighting the owner: their FightingIdentity points at us.
    // DynelManager.Characters covers the NPC chars too (NpcChar is a SimpleChar), so one pass does both.
    private static bool FoughtOver(LocalPlayer me)
    {
        var mine = me.Identity;
        foreach (var c in DynelManager.Characters)
        {
            if (c != null && c.FightingIdentity == mine)
            {
                return true;
            }
        }

        return false;
    }

    // ---- The heal items ------------------------------------------------------

    // By EXACT name from the resupply config, the same rule resupply buys by: a keyword like "Stim"
    // also matches Boosted Stim, Burst of Speed Stim...
    private bool Is(Item it, Kind kind)
    {
        return it?.Name != null &&
               string.Equals(it.Name, kind == Kind.Stim ? _config.ResupplyStimName : _config.ResupplyRechargerName,
                   StringComparison.OrdinalIgnoreCase);
    }

    // The best one we carry that our skills can use (First Aid / Treatment decide, as everywhere).
    private Item? BestUsable(Kind kind, LocalPlayer me)
    {
        Item? best = null;
        foreach (var it in HealItems.AllInvItems())
        {
            if (it == null || !Is(it, kind) || !HealItems.MeetsHealReqs(it, me))
            {
                continue;
            }

            if (best == null || it.Ql > best.Ql)
            {
                best = it;
            }
        }

        return best;
    }

    // How many of the kind we carry and can use, by stack count (the resupply count).
    private int Carry(Kind kind, LocalPlayer me)
    {
        var items = HealItems.AllInvItems();
        return items.Where(it => Is(it, kind) && HealItems.MeetsHealReqs(it, me))
            .Sum(it => Math.Max(1, it.Count));
    }

    // The heal capacity of the stim we would pop (the best usable), from its Use-modifier health
    // stat, interpolated by QL by the item model (Item.CreateItem). Unreadable means 0 - the health
    // trigger then fires on any wound, which only makes us use a stim earlier, and the cooldown
    // keeps it at one per timer.
    private int HealCapacity(LocalPlayer me)
    {
        var it = BestUsable(Kind.Stim, me);
        if (it == null)
        {
            return 0;
        }

        return it.Modifiers.TryGetValue(SpellListType.Use, out var mods) && mods.TryGetValue(Stat.HealthChange, out var v)
            ? v
            : 0;
    }

    private static string Name(Kind kind)
    {
        return kind == Kind.Stim ? "stim" : "recharger";
    }

    // The regen context for a fire line, only when a forecast is live (why the recharger/stim was
    // still needed despite it - or how much of the deficit it ate).
    private string RegenNote()
    {
        return _regenRemaining > 0
            ? $" (buffs: {_regenHealth:0} hp/s, {_regenNano:0} nano/s, {_regenRemaining:0}s left)"
            : "";
    }
}