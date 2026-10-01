// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: MovementController.cs
//
// Last modified: 2026-10-01
// Created:       2026-09-30 10:09
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using System.Diagnostics;
using AOBuddy20.Components;
using AOBuddy20.Enums;
using AOBuddy20.Interfaces;
using AOBuddy20.Network;
using AOBuddy20.Utils;
using AOSharp.Clientless;
using AOSharp.Common.GameData;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOBuddy20.Controlling;

/// <summary>
///     The movement cycle (Readme points 1, 4 and 5) on its own thread. Other components never move
///     the body: they hand in a goal (position + playfield) with their priority and read back the
///     server-confirmed CurrentPosition. The highest-priority goal for the current playfield wins -
///     its move replaces any unsent lower one (review.md #7) - and every CharDCMove that leaves the
///     bot goes out through here.
///     Goal contract: one goal per priority level; setting again replaces it. Several goals for the
///     current playfield coexist, and a controller walks a route by setting its next goal when
///     <see cref="IsGoalReached" /> turns true. A goal is NOT consumed on arrival - it stays until
///     its owner sets the next one or clears it, so the owner can advance its inner state (Readme
///     point 5). ONLY the active (highest) goal may ever read as reached: while a higher one is
///     set - walking or already reached - lower ones stay unsatisfied even if the body stands
///     inside their radius, and one reached earlier re-arms the moment a higher goal takes over.
///     Threading: SDK state (LocalPlayer, stats, Playfield) is read only on the update thread, which
///     publishes a snapshot each tick (review.md #9: foreign threads consume snapshots). The walk
///     thread is the only writer of the body's MovementComponent; a server SetPos is queued here and
///     applied by the walk thread, never from the packet thread. Set/Clear/IsGoalReached may be
///     called from any thread.
/// </summary>
[MinLogLevel(LogEventLevel.Debug)]
public sealed class MovementController : IPacketConsumer
{
    // Walk tuning, carried over from AOBuddy10's proven defaults: SendIntervalMs = 100 and
    // MaxStep = 1.5 (AOBuddy10 Config.cs:363-364), manual-target arrival radius 1.5 m
    // (AOBuddy10 FollowController.cs:532).
    private const int SendIntervalMs = 100;
    private const float MaxStep = 1.5f;
    private const float ArriveRadius = 1.5f;

    private readonly ILogger<MovementController> _logger;

    // The ONE place the body moves (Components/Movement.cs), ported from AOBuddy10.
    private readonly Movement _movement = new Movement();

    private readonly Dictionary<int, GoalLocation> goals = new Dictionary<int, GoalLocation>();
    private readonly object _goallock = new object();
    private readonly object _poslock = new object();

    private Vector3 _confirmedPosition; // where the server last had US
    private Quaternion _confirmedHeading;
    private Vector3? _pendingCorrection; // SetPos the walk thread will apply

    // Published by the update thread, consumed by the walk thread. One immutable snapshot per tick
    // so the walk never sees a torn combination (LocalPlayer is swapped on zone-in).
    private volatile Snapshot _snap = new Snapshot();

    private Thread? _thread;
    private volatile bool _running;
    private int _pf = -1; // playfield the walk state belongs to

    // Run-speed stickiness, as AOBuddy10 BotContext.RunVelocity: the last real reading holds until
    // a new one arrives, so a momentary unreadable stat does not reset the walk speed.
    private bool _runRead;
    private int _lastRunSpeed;

    public MovementController(ILogger<MovementController> logger)
    {
        _logger = logger;
        _logger.LogInformation("Movement controller initialized.");
    }

    /// <summary>Where the server last confirmed us. What other components read (Readme point 5).</summary>
    public Vector3 CurrentPosition
    {
        get
        {
            lock (_poslock)
            {
                return _confirmedPosition;
            }
        }
    }

    public Quaternion CurrentHeading
    {
        get
        {
            lock (_poslock)
            {
                return _confirmedHeading;
            }
        }
    }

    /// <summary>
    ///     Hand in where the body should go, at this controller's priority. Setting again replaces
    ///     the level's goal (and re-arms the reached flag). A goal for another playfield is not
    ///     walked from here - the component that set it owns getting us to that playfield first.
    ///     arriveRadius: how close counts as arrived (default from AOBuddy10's walker).
    /// </summary>
    public void SetDesiredGoal(Vector3 desiredGoal, int playfieldId, ControlPriority priority, float arriveRadius = ArriveRadius)
    {
        lock (_goallock)
        {
            goals[(int)priority] = new GoalLocation
            {
                Position = desiredGoal,
                PlayfieldId = playfieldId,
                ArriveRadius = arriveRadius,
            };
            _logger.LogDebug($"Goal set: priority {priority} ({(int)priority}), playfield {playfieldId}, " +
                             $"arrive {arriveRadius:0.0} m, ({desiredGoal.X:0.0} {desiredGoal.Y:0.0} {desiredGoal.Z:0.0}).");
        }
    }

    public void ClearDesiredGoal(ControlPriority priority)
    {
        lock (_goallock)
        {
            if (goals.Remove((int)priority))
            {
                _logger.LogDebug($"Goal cleared: priority {priority}.");
            }
        }
    }

    /// <summary>
    ///     Has the walk reached the goal this priority set? Readable from the originating controller,
    ///     from any thread. False when no goal is set at this priority, when the goal is for another
    ///     playfield, or while ANY higher-priority goal is set - walking or already reached: only the
    ///     active goal can be satisfied, so a preempted goal re-arms the moment a higher one takes
    ///     over the body, even if the body happens to stand inside its radius. It is satisfied again
    ///     once the walk comes back down to it. If the body is pushed off the goal (a big SetPos), it
    ///     re-arms and the walk returns.
    /// </summary>
    public bool IsGoalReached(ControlPriority priority)
    {
        lock (_goallock)
        {
            return goals.TryGetValue((int)priority, out var goal) && goal.Reached;
        }
    }

    public void RegisterPackets(PacketRouter router)
    {
        router.Register(DCMoveHandler, N3MessageType.CharDCMove, (int)ControlPriority.None);
        router.Register(SetPosHandler, N3MessageType.SetPos, (int)ControlPriority.None);
    }

    // ── lifecycle ─────────────────────────────────────────────────────────────────────────

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        Client.OnUpdate += PublishSnapshot;
        _thread = new Thread(WalkLoop)
        {
            IsBackground = true,
            Name = "AOBuddy-Movement",
        };
        _thread.Start();
        _logger.LogInformation("Movement loop started.");
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        Client.OnUpdate -= PublishSnapshot;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _logger.LogInformation("Movement loop stopped.");
    }

    // Runs on the SDK update thread: the only place SDK state may be read (review.md #9).
    // OnUpdate fires only while in play, so a snapshot is proof we are up; the initial snapshot
    // (null player) keeps the walk idle until then.
    private void PublishSnapshot(object? sender, double deltaTime)
    {
        var me = DynelManager.LocalPlayer;
        var runSpeed = -1;
        if (me != null && me.TryGetStat(Stat.RunSpeed, out var rs) && rs != -1)
        {
            runSpeed = rs;
        }

        _snap = new Snapshot
        {
            Me = me,
            Playfield = (int)Playfield.ModelId,
            RunSpeed = runSpeed,
        };
    }

    // ── the movement thread ───────────────────────────────────────────────────────────────

    private void WalkLoop()
    {
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed.TotalSeconds;
        while (_running)
        {
            var now = clock.Elapsed.TotalSeconds;
            var dt = Math.Min(now - last, 0.25d); // a stall (GC, debugger) must not become one giant step
            last = now;
            try
            {
                Tick(dt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Movement tick failed.");
            }

            Thread.Sleep(15); // ~64 Hz, the SDK UpdateLoop cadence
        }
    }

    private void Tick(double dt)
    {
        var snap = _snap;
        if (snap.Me == null)
        {
            return; // not in play yet - nothing to move
        }

        var me = snap.Me;

        if (snap.Playfield != _pf)
        {
            // A new playfield puts the body wherever the server placed it; gait and mode start over.
            _pf = snap.Playfield;
            _movement.Reset();
            lock (_poslock)
            {
                _confirmedPosition = me.MovementComponent.Position;
                _confirmedHeading = me.MovementComponent.Heading;
                _pendingCorrection = null;
            }

            _logger.LogInformation($"Movement: playfield {_pf}, position " +
                                   $"({_confirmedPosition.X:0.0} {_confirmedPosition.Y:0.0} {_confirmedPosition.Z:0.0}).");
        }

        ApplyPendingCorrection(me);

        var goal = SelectActiveGoal(_pf);
        if (goal == null)
        {
            _movement.Hold(me, SendIntervalMs); // nowhere to go: hold position (guarded, no packet spam)
            return;
        }

        var pos = me.MovementComponent.Position;
        var dist = Movement.Flat(pos, goal.Value.Value.Position);
        if (dist <= goal.Value.Value.ArriveRadius)
        {
            _movement.Hold(me, SendIntervalMs);
            if (!goal.Value.Value.Reached)
            {
                goal.Value.Value.Reached = true;
                _logger.LogInformation($"Movement: reached the priority {goal.Value.Key} goal " +
                                       $"({goal.Value.Value.Position.X:0.0} {goal.Value.Value.Position.Y:0.0} {goal.Value.Value.Position.Z:0.0}), {dist:0.0} m out.");
            }

            return;
        }

        // Not there (yet, or anymore): the flag only ever says reached while we stand on it.
        goal.Value.Value.Reached = false;

        // One capped step toward the goal, the proven walker pattern (AOBuddy10
        // FollowController.Step): mouse-look facing (instant, server-proven legal - Movement.Face),
        // then Advance at the run-speed formula. Y is not walked: the goal's floor is found by
        // the terrain, not by us (Movement.Flat).
        var delta = goal.Value.Value.Position - pos;
        var flat = new Vector3(delta.X, 0f, delta.Z);
        if (flat.Magnitude < 0.05f)
        {
            _movement.Hold(me, SendIntervalMs); // straight up/down (a lift, a stacked floor): nothing to walk
            return;
        }

        var dir = flat.Normalize();
        var want = Movement.SafeLook(dir, me.MovementComponent.Heading);
        var step = Movement.CappedStep(RunVelocity(snap), dt, MaxStep, dist);
        _movement.Advance(me, pos + dir * step, want, run: true, dt, SendIntervalMs);
    }

    // SetPos the packet thread accepted: applied here, on the thread that owns the body.
    private void ApplyPendingCorrection(LocalPlayer me)
    {
        Vector3 pos;
        lock (_poslock)
        {
            if (_pendingCorrection == null)
            {
                return;
            }

            pos = _pendingCorrection.Value;
            _pendingCorrection = null;
            _confirmedPosition = pos;
        }

        Movement.SetPose(me, pos, me.MovementComponent.Heading);
        _movement.ResetKeepGait(); // a correction is not a gait change (Movement.cs, Newland 21:40)
    }

    // Velocity = 4.82 + 0.003615 x Run Speed (stat 156), capped at 15.5 (review.md, wire-proven).
    // A snare drives the stat negative (-289, 2026-09-23): a real reading, floored at 1.5 so the
    // walker slows instead of snapping every 3 s. Only -1 means unreadable: base 4.82 then.
    private float RunVelocity(Snapshot snap)
    {
        if (snap.RunSpeed != -1)
        {
            _lastRunSpeed = snap.RunSpeed;
            _runRead = true;
        }

        if (!_runRead)
        {
            return 4.82f;
        }

        return Math.Max(1.5f, Math.Min(15.5f, 4.82f + _lastRunSpeed * 0.003615f));
    }

    // The highest-priority goal for the playfield, with the ONLY one allowed to read as reached
    // (owner, 2026-10-01): while a higher goal is set - walking or already reached - a lower one is
    // not being serviced, even if the body happens to stand inside its radius, and one that was
    // reached before a higher goal took the body over re-arms here. Selection and disarming sit
    // under one lock so a goal set mid-tick cannot slip between the two.
    private KeyValuePair<int, GoalLocation>? SelectActiveGoal(int playfield)
    {
        lock (_goallock)
        {
            KeyValuePair<int, GoalLocation>? best = null;
            foreach (var g in goals)
            {
                if (g.Value.PlayfieldId != playfield)
                {
                    continue; // a goal for another playfield is not walkable from here
                }

                if (best == null || g.Key > best.Value.Key)
                {
                    best = g;
                }
            }

            foreach (var g in goals.Values)
            {
                if (best == null || !ReferenceEquals(g, best.Value.Value))
                {
                    g.Reached = false;
                }
            }

            return best;
        }
    }

    // ── packet handlers (update thread) ───────────────────────────────────────────────────

    // Our own CharDCMove echo is the server's confirmation of where it has us: that is
    // CurrentPosition. Everyone else's moves are not ours to consume - the SDK's DynelManager
    // keeps their transforms, and we return false so it always sees the packet.
    private bool DCMoveHandler(AOMessage arg)
    {
        if (arg.Body is CharDCMoveMessage m)
        {
            var me = DynelManager.LocalPlayer;
            if (me != null && m.Identity == me.Identity)
            {
                lock (_poslock)
                {
                    _confirmedPosition = m.Position;
                    _confirmedHeading = m.Heading;
                }
            }
        }

        return false;
    }

    // SetPos self-corrections, the settled rule (review.md): apply 10 m or more, ignore smaller
    // ones while moving (the retail client ignores corrections under 10 m 86% of the time and
    // takes those of 10 m or more 64%). While standing there is nothing in flight to fight, so a
    // small one is taken too. Never applied here: queued for the walk thread, the body's only writer.
    private bool SetPosHandler(AOMessage arg)
    {
        if (arg.Body is not SetPosMessage m)
        {
            return false;
        }

        var me = DynelManager.LocalPlayer;
        if (me == null || m.Identity != me.Identity)
        {
            return false;
        }

        var off = Movement.Flat(me.MovementComponent.Position, m.Position);
        if (off >= 10f)
        {
            lock (_poslock)
            {
                _pendingCorrection = m.Position;
            }

            _logger.LogInformation($"Movement: SetPos correction of {off:0.0} m accepted.");
        }
        else if (!_movement.Moving)
        {
            lock (_poslock)
            {
                _pendingCorrection = m.Position;
            }

            _logger.LogDebug($"Movement: standing SetPos of {off:0.0} m accepted.");
        }
        else
        {
            _logger.LogDebug($"Movement: SetPos of {off:0.0} m ignored while moving.");
        }

        return false;
    }

    /// <summary>What the update thread publishes for the walk thread each tick.</summary>
    private sealed class Snapshot
    {
        public LocalPlayer? Me;
        public int Playfield;
        public int RunSpeed; // -1 = unreadable this tick
    }

    internal sealed class GoalLocation
    {
        internal Vector3 Position { get; set; }
        internal int PlayfieldId { get; set; }
        internal float ArriveRadius { get; set; }

        // Written by the walk thread (arrival sets it, preemption and walking off re-arm it), read
        // from any thread by the goal's owner through IsGoalReached.
        internal volatile bool Reached;
    }
}