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
///     nano is (under HealNanoOutOfCombatPct of max). The recharger is SIT-ONLY and its healing ticks
///     run while seated, so it goes through the rest cycle (AOBuddy10 SupportController's, wire-proven):
///     sit, WAIT for the server's echo of the sit (MovementController.SeatedConfirmed - the only
///     reliable seated proof there is; stat 173 never updates after login), use once, KEEP SITTING
///     while the ticks run, and stand when the thresholds are met - or on combat, a stall, or
///     HealRestMaxSeconds. The walk the rest interrupted resumes on the stand-up (goals were kept);
///     a blitz never sits (SuppressCombat). No arbiter control: the posture track holds the body
///     while seated. A convenience top-up, not an emergency.
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

    // THE REST CYCLE timings, from AOBuddy10's wire-proven SupportController: a sit whose echo was
    // lost gets one hopeful use after this long; a rest whose health AND nano stopped climbing for
    // this long is a dead item - stand (the window is long because the server sends the stats
    // sparsely - 5 s stood him up mid-recharge once, and the re-sit made the sit/stand loop); and
    // after any rest ends, no instant re-sit.
    private const double SitConfirmFallbackSeconds = 1.5;
    private const double RestStallSeconds = 15.0;
    private const double RestCooldownSeconds = 6.0;

    private enum Kind { Stim, Recharger }

    private readonly ILogger<HealController> _logger;
    private readonly ControlArbiter _controlArbiter;
    private readonly MovementController _movement;
    private readonly MissionController _mission;
    private readonly AccountInfo _config;

    private double _clock; // the controller's own clock, accumulated from dt
    private double _cooldownLeft;
    private int _lastHealth = -1;
    private double _lastHurtAt = double.NegativeInfinity;
    private int _pf = -1;

    private bool _holding; // an in-combat episode: arbiter held at LowHealthNanoEmergency
    private bool _loggedShort; // "none usable" once per episode, not once per tick

    // The rest cycle's state (out of combat, recharger): we sat for it, when, and what the body
    // has gained since - the stall guard reads the peaks, not the tick-to-tick deltas.
    private bool _resting;
    private double _restStartedAt;
    private double _restLastGainAt;
    private int _restPeakHealth = -1, _restPeakNano = -1;
    private bool _sitHopefulLogged; // the use-without-echo fallback, logged once per rest
    private double _restCooldownLeft; // no instant re-sit after a rest ends

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

    public HealController(ILogger<HealController> logger, ControlArbiter controlArbiter,
        MovementController movement, MissionController mission, AccountInfo config)
    {
        _logger = logger;
        _controlArbiter = controlArbiter;
        _movement = movement;
        _mission = mission;
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

        if (_restCooldownLeft > 0)
        {
            _restCooldownLeft -= dt;
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
            // A zone change ends every fight and every wound count we were tracking - and a rest:
            // the body arrives wherever the server put it, its posture the track's business again.
            _pf = pf;
            _lastHealth = -1;
            _lastHurtAt = double.NegativeInfinity;
            DropRest();
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

        if (_resting && combat)
        {
            // The fight interrupts the rest: up and onto the stims - a seated body cannot move.
            EndRest("combat interrupts the rest");
        }

        // THE WANT, per scenario: in combat stims, out of combat rechargers. Both deficits are NET
        // of the buffs' regen: what the running HoTs pour in over the horizon we care about is need
        // we do not have to spend an item on.
        string reason;
        bool want;
        if (combat)
        {
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
            if (_resting)
            {
                // The recharger's ticks carried us over the thresholds: up. The walk this rest
                // interrupted resumes on its own (the goals were kept when we sat).
                EndRest($"healed (nano {nanoPct:0}%, health {health * 100.0 / maxHealth:0}%)");
                return false;
            }

            EndEpisode();
            return false;
        }

        if (combat)
        {
            if (!_holding)
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

            var stim = BestUsable(Kind.Stim, me);
            if (stim == null)
            {
                if (!_loggedShort)
                {
                    _loggedShort = true;
                    _logger.LogInformation(
                        $"HEAL: want {Name(Kind.Stim)} ({reason}) but none usable in the packs - resupply stocks them.");
                }

                return _holding;
            }

            stim.Use();
            _cooldownLeft = UseCooldownSeconds;
            _logger.LogInformation($"HEAL: used {stim.Name} QL {stim.Ql} ({reason}); {Carry(Kind.Stim, me) - 1} left.");
            return _holding;
        }

        // OUT OF COMBAT: the recharger REST CYCLE. The item is sit-only and the server checks
        // posture at processing time - a use sent standing is refused ("Target must be sitting on
        // ground": every login, the stand-up stood the body before the heal's first use arrived)
        // and the healing ticks only run while seated. So: sit, WAIT for the server's echo, use
        // once, KEEP SITTING while the ticks run. See RestTick for how the rest ends.
        if (_mission.SuppressCombat)
        {
            return false; // a blitz owns the body: no sitting mid-run (a standing use would be refused anyway)
        }

        if (_resting)
        {
            RestTick(me);
            return false;
        }

        if (!_movement.Standing || _restCooldownLeft > 0)
        {
            return false; // a stand-up is in flight (the login one, say) or a rest just ended: no sit races it
        }

        _movement.Sit("recharger");
        _resting = true;
        _restStartedAt = _clock;
        _restLastGainAt = _clock;
        _restPeakHealth = -1;
        _restPeakNano = -1;
        _sitHopefulLogged = false;
        _loggedShort = false;
        _logger.LogInformation($"HEAL: sitting for a recharger ({reason}).");
        return false;
    }

    private void EndEpisode()
    {
        if (_holding)
        {
            _holding = false;
            _controlArbiter.ReleaseControl();
            _logger.LogInformation("HEAL: episode closed.");
        }

        DropRest();
        _loggedShort = false;
    }

    // One rest tick: the sit is out, the echo may or may not have landed, the item may or may not
    // be off cooldown. Staying seated IS the treatment - the recharger's ticks run under the sit.
    private void RestTick(LocalPlayer me)
    {
        if (_mission.SuppressCombat)
        {
            // The blitz took the body while we sat: up, out of the run's way.
            EndRest("the blitz needs the body");
            return;
        }

        // Progress, peak-tracked: the server sends health/nano sparsely, so a working recharger's
        // climb can look flat for several seconds (the stall window is long for exactly that).
        if (me.TryGetStat(Stat.Health, out var hp) && (_restPeakHealth < 0 || hp > _restPeakHealth))
        {
            _restPeakHealth = hp;
            _restLastGainAt = _clock;
        }

        if (me.TryGetStat(Stat.CurrentNano, out var nano) && (_restPeakNano < 0 || nano > _restPeakNano))
        {
            _restPeakNano = nano;
            _restLastGainAt = _clock;
        }

        if (_clock - _restStartedAt > _config.HealRestMaxSeconds)
        {
            EndRest("the rest ran out of time");
            return;
        }

        if (_clock - _restLastGainAt > RestStallSeconds)
        {
            EndRest("no gain - the item is not helping");
            return;
        }

        // REALLY seated? The server's echo of our sit is the proof (stat 173 never updates). A
        // lost echo gets one hopeful use after the fallback window, logged - AOBuddy10 shipped
        // the same 1.5 s and pressed anyway.
        var seated = _movement.SeatedConfirmed;
        if (!seated && _clock - _restStartedAt >= SitConfirmFallbackSeconds)
        {
            if (!_sitHopefulLogged)
            {
                _sitHopefulLogged = true;
                _logger.LogInformation("HEAL: no sit confirmation from the server - using the recharger anyway.");
            }

            seated = true;
        }

        if (!seated || _cooldownLeft > 0)
        {
            return; // waiting for the echo, or the stim timer runs: the sit does the work
        }

        var it = BestUsable(Kind.Recharger, me);
        if (it == null)
        {
            if (!_loggedShort)
            {
                _loggedShort = true;
                _logger.LogInformation("HEAL: seated for a recharger but none usable in the packs - resupply stocks them.");
            }

            return; // stay seated: the last use's ticks may still be running; the stall guard ends the rest
        }

        it.Use();
        _cooldownLeft = UseCooldownSeconds;
        _logger.LogInformation($"HEAL: used {it.Name} QL {it.Ql} (seated {(_clock - _restStartedAt):0.#}s); " +
                               $"{Carry(Kind.Recharger, me) - 1} left - staying seated while it ticks.");
    }

    // The rest is over on purpose: up, and a cooldown so the next want does not re-sit on the spot
    // (the sit/stand loop guard). The stand goes through the echo-driven campaign, never blind.
    private void EndRest(string why)
    {
        _resting = false;
        _restCooldownLeft = RestCooldownSeconds;
        _loggedShort = false;
        _movement.Stand("rest over: " + why);
        _logger.LogInformation($"HEAL: rest over - {why}.");
    }

    // The rest dies without a stand (dead, zoned, stats unreadable): the posture is the track's
    // business again, and a later movement command re-arms the stand-up campaign from there.
    private void DropRest()
    {
        if (!_resting)
        {
            return;
        }

        _resting = false;
        _restCooldownLeft = RestCooldownSeconds;
        _loggedShort = false;
        _logger.LogInformation("HEAL: rest dropped.");
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