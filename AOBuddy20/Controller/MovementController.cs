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
using AOBuddy20.Nav;
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

    // WATER, the captured client's exact contract (AOBuddy10 OverlandController, capture 20260924-215811
    // s116, this very shore): NO swim-mode packet is ever sent - Y is THE WATER SURFACE while the bottom
    // is deeper than a wade, THE BOTTOM once it rises inside wading range, plain Update packets
    // throughout. Wet speed = RunVelocity * SwimSpeedFactor (~5.5 u/s, Config.cs:357-358).
    private const float SwimWadeMeters = 1.2f;
    private const float SwimSpeedFactor = 0.5f;
    private const float WadeProbeMeters = 0.5f; // probe this far ahead for the water verdict
    private const float WadeDepth = 0.3f; // SwimY's wade depth for the probe

    private readonly ILogger<MovementController> _logger;

    // The ONE place the body moves (Components/Movement.cs), ported from AOBuddy10.
    private readonly Movement _movement = new Movement();

    private readonly Dictionary<int, GoalLocation> goals = new Dictionary<int, GoalLocation>();
    private readonly object _goallock = new object();
    private readonly object _poslock = new object();

    private Vector3 _confirmedPosition; // where the server last had US
    private Quaternion _confirmedHeading;
    private Vector3? _pendingCorrection; // SetPos the walk thread will apply

    // The playfield's nav data and walk grid, built off every loop thread (NavGridCache). Nav supplies
    // the Y of every dictated step - without it the walk holds the login Y and floats (owner, 2026-10-01).
    private readonly NavGridCache _nav = new();
    private readonly Stopwatch _wetClock = Stopwatch.StartNew();

    // How far the server's floor sits above our data here (AOBuddy10: Rome Park walked y 16 over ground
    // the data puts at 13.6; with corrections ignored the bot sank back each step, log 2026-09-24 01:30).
    // _wetY: the server's own Y while in water, wet truth for 3 s (AOBuddy10 OverlandController).
    private float _yBias;
    private float _wetY;
    private double _wetYAt = -99;
    private float _pendingBias; // the bias the SetPos that queued _pendingCorrection carried

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

    // The bot's own folder (Build\): GameData\Nav and gridcache live beside the executable, as
    // AOBuddy10 kept them beside the plugin.
    private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

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

    /// <summary>Clears every goal - the body stops on its next tick, with nothing left to walk.</summary>
    public void ClearAllGoals()
    {
        lock (_goallock)
        {
            if (goals.Count > 0)
            {
                _logger.LogDebug("All goals cleared.");
            }

            goals.Clear();
        }
    }

    /// <summary>Compact one-line state for status replies. Safe from any thread.</summary>
    public string DescribeState()
    {
        lock (_goallock)
        {
            var goalText = goals.Count == 0
                ? "none"
                : string.Join("; ", goals.OrderByDescending(g => g.Key)
                    .Select(g => $"{(ControlPriority)g.Key} ({g.Key}) @ " +
                                 $"({g.Value.Position.X:0.0} {g.Value.Position.Y:0.0} {g.Value.Position.Z:0.0}) pf {g.Value.PlayfieldId}" +
                                 $"{(g.Value.Reached ? " REACHED" : "")}"));
            return $"pf {_pf}, pos ({CurrentPosition.X:0.0} {CurrentPosition.Y:0.0} {CurrentPosition.Z:0.0}) | nav: {NavState()} | goals: {goalText}";
        }
    }

    private string NavState()
    {
        if (_nav.LoadedPf != _pf)
        {
            return _nav.LoadedPf >= 0 ? $"loading (have {_nav.LoadedPf})" : "loading";
        }

        var nav = _nav.Nav;
        return nav == null ? "none (straight lines)" : $"{nav.Kind}, yBias {_yBias:+0.0;-0.0}";
    }

    /// <summary>Everything the nav data says about our current position (AOBuddy10's 'navdata' command,
    /// which exists so the data can be checked against the live character before anything relies on it).</summary>
    public string ExplainNav()
    {
        var nav = _nav.Nav;
        var p = CurrentPosition;
        if (nav == null || _nav.LoadedPf != _pf)
        {
            return $"nav: no data for pf {_pf} (loaded: {_nav.LoadedPf}).";
        }

        return nav.Explain(p.X, p.Y, p.Z);
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

        // The playfield's nav data, built off every loop thread (Lush Fields took 6.3 s, log 2026-09-24):
        // while it loads there is no honest Y, so hold rather than walk blind.
        if (!_nav.Request(snap.Playfield, BaseDir, s => _logger.LogInformation(s), "MOVE"))
        {
            _movement.Hold(me, SendIntervalMs);
            return;
        }

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

        // One capped step toward the goal, the proven outdoor walker (AOBuddy10 OverlandController):
        // mouse-look facing, then Advance at the run-speed formula with the step's Y taken from the NAV
        // DATA (the floor under the next position, the water surface over it), never from the goal.
        var delta = goal.Value.Value.Position - pos;
        var flat = new Vector3(delta.X, 0f, delta.Z);
        if (flat.Magnitude < 0.05f)
        {
            _movement.Hold(me, SendIntervalMs); // straight up/down (a lift, a stacked floor): nothing to walk
            return;
        }

        var dir = flat.Normalize();

        // WATER - the captured client's contract: probe just ahead for the surface verdict.
        var probe = Math.Min(dist, WadeProbeMeters);
        var plane = _nav.Nav?.Ground != null
            ? _nav.Nav.Ground.SwimY(pos.X + dir.X * probe, pos.Z + dir.Z * probe, WadeDepth)
            : double.NaN;
        var inWater = !double.IsNaN(plane);
        var speed = inWater ? RunVelocity(snap) * SwimSpeedFactor : RunVelocity(snap);
        var step = Movement.CappedStep(speed, dt, MaxStep, dist);
        var nx = pos.X + dir.X * step;
        var nz = pos.Z + dir.Z * step;
        var floorY = FloorY(nx, pos.Y, nz);

        // UPHILL THE SERVER IS STRICTER THAN ON THE FLAT (capture 20260925-141138, Wailing Wastes: a
        // 0.1/m rise refused every 1.5 m step at 15 u/s on the wire, while the captured client walks the
        // same slope in 0.1-0.2 m moves every 10-30 ms). Climb in steps small enough to be under run
        // speed per send interval.
        if (!inWater && floorY > pos.Y)
        {
            step = Math.Min(step, Math.Max(0.3f, speed * SendIntervalMs / 1000f * 0.6f));
            nx = pos.X + dir.X * step;
            nz = pos.Z + dir.Z * step;
            floorY = FloorY(nx, pos.Y, nz);
        }

        var now = _wetClock.Elapsed.TotalSeconds;
        var floating = inWater && now - _wetYAt < 3 && _wetY > floorY + 0.4f && _wetY <= plane + 0.3f;
        var nextY = floating ? _wetY // the server's own surface Y, while fresh
            : inWater && floorY < (float)plane - SwimWadeMeters ? (float)plane // swim at the surface
            : floorY; // wade the bottom / walk the shore

        // NEVER BELOW THE TERRAIN (2026-09-25, the Wailing Wastes rubberband): the heightfield is solid
        // ground outdoors - the step we claim can never be under it.
        var terr = _nav.Nav?.Ground != null ? _nav.Nav.Ground.HeightAt(nx, nz) : double.NaN;
        if (!double.IsNaN(terr) && terr > nextY)
        {
            nextY = (float)terr;
        }

        if (!inWater)
        {
            // ...AND STEP UP ONTO WHAT WE WALK INTO (the ICC steps, same day): under a staircase the
            // nearest floor is the terrain BENEATH THE STAIRS, so the walk would claim the plaza's height
            // while stepping into the rising treads. The surface we stand on at the next position is the
            // HIGHEST floor at most a step above us (a riser or two - anything higher is a wall).
            var sf = StepFloor(nx, pos.Y, nz);
            if (!float.IsNaN(sf) && sf >= pos.Y - 4f && sf > nextY)
            {
                nextY = sf;
            }
        }

        var want = Movement.SafeLook(dir, me.MovementComponent.Heading);
        _movement.Advance(me, new Vector3(nx, nextY, nz), want, run: true, dt, SendIntervalMs);
    }

    // ── floor sampling (AOBuddy10 OverlandController's FloorY/StepFloor, verbatim rules) ──

    private float FloorY(float x, float y, float z)
    {
        var h = RawFloorY(x, y - _yBias, z);
        if (float.IsNaN(h))
        {
            return y;
        }

        h += _yBias;
        return Math.Abs(h - y) > 4f ? y : h; // a jump of more than 4 m is a roof or a cave, not our floor
    }

    // Our data's floor under (x, z) nearest y; NaN when there is none.
    private float RawFloorY(float x, float y, float z)
    {
        var nav = _nav.Nav;
        if (nav == null)
        {
            return float.NaN;
        }

        double h = nav.FloorNear(x, y, z, out _);
        return double.IsNaN(h) ? float.NaN : (float)h;
    }

    // The HIGHEST surface at the next position that is at most a step above y (stairs, kerbs, sills) —
    // the surface we would walk ONTO. NaN when nothing qualifies.
    private float StepFloor(float x, float y, float z)
    {
        float best = float.NaN;
        void Consider(double h)
        {
            if (double.IsNaN(h) || h > y + 0.8f)
            {
                return;
            }

            if (float.IsNaN(best) || h > best)
            {
                best = (float)h;
            }
        }

        var nav = _nav.Nav;
        if (nav?.Ground != null)
        {
            Consider(nav.Ground.HeightAt(x, z));
        }

        if (nav?.Collision != null)
        {
            foreach (double h in nav.Collision.HeightsUnder(x, z))
            {
                Consider(h);
            }
        }

        return best;
    }

    // SetPos the packet thread accepted: applied here, on the thread that owns the body. Its height
    // against our floor data becomes the yBias carried forward, and its Y is wet truth for 3 s in water.
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
            _yBias = _pendingBias;
            _wetY = pos.Y;
            _wetYAt = _wetClock.Elapsed.TotalSeconds;
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

    // SetPos self-corrections. WALKING ON NAV DATA, every correction is taken and its height against our
    // floor data becomes the yBias carried forward (AOBuddy10 OverlandController: Rome Park walked y 16
    // over data ground 13.6, and with corrections ignored the bot sank back each step and was pulled
    // every 3 s). WITHOUT nav data the settled rule stands (review.md): apply 10 m or more, ignore
    // smaller ones while moving (the retail client ignores corrections under 10 m 86% of the time and
    // takes those of 10 m or more 64%); standing, everything applies. Application itself always happens
    // on the walk thread (the body's only writer), queued here.
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

        // The correction's height against our floor data: how far the server's floor sits above ours here.
        // Over water the floor says nothing about the bias (AOBuddy10), and neither does a wild jump.
        var raw = RawFloorY(m.Position.X, m.Position.Y, m.Position.Z);
        var bias = float.IsNaN(raw) ? 0f : m.Position.Y - raw;
        if (Math.Abs(bias) > 4f || _movement.Swimming)
        {
            bias = 0f;
        }

        var onNav = _nav.LoadedPf == _pf;
        var off = Movement.Flat(me.MovementComponent.Position, m.Position);
        if (onNav || off >= 10f || !_movement.Moving)
        {
            lock (_poslock)
            {
                _pendingCorrection = m.Position;
                _pendingBias = bias;
            }

            _logger.LogInformation($"Movement: SetPos APPLIED ({(onNav ? "nav walk" : off >= 10f ? "resync" : "standing")}): " +
                                   $"server ({m.Position.X:0.0} {m.Position.Y:0.0} {m.Position.Z:0.0}), {off:0.0} m off, " +
                                   $"floor {bias:+0.0;-0.0} m vs data.");
        }
        else
        {
            _logger.LogDebug($"Movement: SetPos of {off:0.0} m ignored while moving (no nav data).");
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