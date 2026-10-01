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
using AOBuddy20.Configuration;
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

    // ROUTING (AOBuddy10 OverlandController): grid-route snap and reaches, waypoint advance, and the
    // stuck watch that blocks the few metres ahead and replans round what the data does not show.
    private const float SnapMeters = 8f;
    private const float GoalReach = 3f;
    private const float WideGoalReach = 8f; // a goal in a wall: the nearest reachable ground within this
    private const float WaypointRange = 0.5f;
    private const float WaypointLastRange = 3f; // == GoalReach: the route's last point stands for the goal
    private const double StuckSeconds = 4;
    private const int MaxStuck = 4; // re-routes around a stuck spot per goal

    // TRAVEL legs (AOBuddy10 OverlandController): stand still a moment before using (the server must
    // have our stop before we use from where it has us), wait per kind for the zone, and give each exit
    // a try budget before it is written off and the plan re-routes round it. Booths, grid exits and lift
    // beams (kind Line) are pads WALKED ONTO (PadReach, exactly on the centre); whompas and teleporters
    // are terminals walked up to (ObjectReach) and used with GenericCmd Use.
    private const int MaxLegTries = 3;
    private const double SettleSeconds = 0.6;
    private const double ZoneLineWait = 6;
    private const double PadWait = 10;
    private const double ObjectWait = 8;
    private const float PadReach = 0.6f;
    private const float ObjectReach = 2.5f;

    // A proxy playfield's exit door takes the crossing only from inside it: activation radius is
    // under half a metre (owner, 2026-10-01) - walk ONTO the door, then use it.
    private const float DoorReach = 0.4f;

    // The stand-up campaign: re-send the toggle until the server's 0x57 echo confirms it, at most
    // this often and this many times - then walk anyway and let the server have the last word.
    private const int MaxStandTries = 3;
    private const double StandRetrySeconds = 1.5;

    // Server's CurrentMovementMode (Stat 173) values — OmniCell MoveModes (AOBuddy10 Main.cs:48-50). A
    // character in a SEATED mode cannot move: the server rejects every move packet and snaps him back
    // to where he sat. You log out by sitting, so you can log back in seated.
    private const int MoveModeSit = 8;
    private const int MoveModeSleep = 11;
    private const int MoveModeLounge = 12;

    private readonly ILogger<MovementController> _logger;

    // The ONE place the body moves (Components/Movement.cs), ported from AOBuddy10.
    private readonly Movement _movement = new Movement();

    private readonly Dictionary<int, GoalLocation> goals = new Dictionary<int, GoalLocation>();
    private readonly object _goallock = new object();
    private readonly object _poslock = new object();

    // TRAVEL (PlanTravel): the cross-playfield plan - the destination playfield, its optional
    // coordinates, and the leg currently walked. Legs are re-planned from wherever we actually are
    // after every zone, so a surprise zone cannot derail the plan. Guarded by _travelLock; the lock
    // order is always _travelLock -> _goallock, never the reverse. _travelFailed is the exits written
    // off on THIS trip (an exit that gave nothing MaxLegTries times is routed round), same lock.
    private TravelPlan? _travel;
    private readonly HashSet<ZoneExit> _travelFailed = new();
    private readonly object _travelLock = new object();
    private int _wetPf = -1; // the playfield whose wet verdict Zoning carries for line crossings

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

    // The route to the active goal through the walk grid: walls, zone lines and doors only stop the
    // body when the step is ROUTED round them - a beeline walks straight through (owner, 2026-10-01:
    // 'come' ran him into a door to another playfield). Replanned when the goal changes.
    private readonly Movement.StuckWatch _stuck = new();
    private readonly HashSet<int> _stuckCells = new(); // cells we got stuck walking into, this goal
    private List<Vector3> _route = new();
    private int _routeIdx;
    private int _routePrio = -1; // the priority the route was planned for
    private Vector3 _routeGoal; // and the goal position it was planned to
    private int _routePf = -1;
    private int _stuckCount;

    // Set once the ONE login stand-up decision is made (see Tick): the login mode decides, at most
    // one toggle goes out, and never again - stat 173 never updates, so re-reading it re-toggles.
    private volatile bool _stoodUp;

    // The posture we believe the body is in, tracked from the only sources there are (owner,
    // 2026-10-01: "only stand up if needed"): the login FullCharacter's stat 173, the toggles the
    // bot itself sends (SitNow/StandNow, and the stand-up every movement goal requires), and the
    // server's 0x57 echo - a toggle that never came back means the body is still sitting, so the
    // stand-up is re-sent, bounded (owner, 2026-10-01: 'goto' left the bot sitting because the one
    // stand-up went unheard and nothing ever checked again).
    private volatile bool _seated;
    private volatile bool _standEchoPending; // a stand-up toggle is on the wire, no 0x57 echo yet
    private double _standSentAt = -1;
    private int _standTries;

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

    public MovementController(ILogger<MovementController> logger, AccountInfo config)
    {
        _logger = logger;
        _config = config;
        _follow = new FollowController(logger, _movement, SendIntervalMs, MaxStep);
        _logger.LogInformation("Movement controller initialized.");
    }

    private readonly AccountInfo _config; // the owner name: follow's target
    private readonly FollowController _follow;

    // The body driver and the owner's wire fingerprint, published for the packet thread. He is known
    // by dynel instance: his CharDCMove packets carry it.
    private volatile bool _followOn;
    private volatile int _ownerInstance;
    private long _ownerLastMoveAt = long.MinValue;

    /// <summary>Follow mode: when on and no goal wants the body, stack on the owner and mirror him.</summary>
    public void SetFollow(bool on)
    {
        if (_followOn == on)
        {
            return;
        }

        _followOn = on;
        if (!on)
        {
            _follow.BreakMirror("follow off");
        }

        _logger.LogInformation(on
            ? $"FOLLOW: on - will stack on '{_config.Owner}' and mirror his movement."
            : "FOLLOW: off (stay).");
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

    ///     Hand in where the body should go, at this controller's priority. Setting again replaces
    ///     the level's goal (and re-arms the reached flag). A goal for another playfield is not
    ///     walked from here - the component that set it owns getting us to that playfield first.
    ///     Travel (<see cref="PlanTravel" />) is the one built-in cross-playfield order: a zone-line
    ///     route from the Zoning graph, one walked leg per hop, re-planned from wherever we actually
    ///     are after every zone.
    ///     arriveRadius: how close counts as arrived (default from AOBuddy10's walker).
    ///     A seated body is stood up first: a movement order is the one thing that may end a sit.
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

        if (_seated)
        {
            var me = DynelManager.LocalPlayer;
            if (me != null)
            {
                _standTries = 0; // a fresh order, a fresh campaign
                SendStandUp(me, "new goal");
            }
        }
    }

    /// <summary>The owner's sit command: stop (goals go) and sit. The posture track follows the order.</summary>
    public void SitNow()
    {
        ClearAllGoals();
        var me = DynelManager.LocalPlayer;
        if (me == null)
        {
            return;
        }

        me.MovementComponent.ChangeMovement(MovementAction.SwitchToSit);
        _seated = true;
        _standEchoPending = false; // the sit is ours; no stand-up is in flight any more
        _standTries = 0;
        _follow.BreakMirror("sitting");
        _logger.LogInformation("Movement: sitting (owner command); goals cleared.");
    }

    /// <summary>The owner's stand command - the explicit way out of a sit whose echo went missing.</summary>
    public void StandNow()
    {
        var me = DynelManager.LocalPlayer;
        if (me == null)
        {
            return;
        }

        _standTries = 0;
        SendStandUp(me, "owner command");
    }

    // The stand-up toggle (action 87) and the campaign around it. Sent only when the posture track
    // says seated - never blind, a blind toggle SITS a standing character - but a toggle the server
    // never echoes leaves the body sitting with the track saying standing (owner, 2026-10-01: a
    // real-client logout always sits the character; the bot's login stand-up raced the character
    // load and went unheard, and 'goto' then never stood him up). So every send continues a
    // confirmation campaign: Tick re-sends, bounded, until the 0x57 echo lands or the tries run
    // out. The campaign only runs while a goal wants the body - a chosen sit is never fought.
    private void SendStandUp(LocalPlayer me, string why)
    {
        me.MovementComponent.ChangeMovement(MovementAction.LeaveSit); // the StandUp toggle (action 87)
        _seated = false;
        _standEchoPending = true;
        _standSentAt = _wetClock.Elapsed.TotalSeconds;
        _standTries++;
        _logger.LogInformation($"Movement: standing up ({why}, try {_standTries}/{MaxStandTries}).");
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

    /// <summary>Clears every goal - the body stops on its next tick, with nothing left to walk.
    /// A travel plan is part of what "all" means: stop and sit end a travel too.</summary>
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

        lock (_travelLock)
        {
            if (_travel != null)
            {
                _travel = null;
                _logger.LogInformation("TRAVEL: plan cleared with the goals.");
            }

            _travelFailed.Clear(); // the write-offs belonged to the trip, not to the bot
        }
    }

    /// <summary>
    ///     Travel to another playfield, optionally to coordinates in it: the Zoning graph's Dijkstra
    ///     plans the cheapest way - zone lines walked across, whompas and teleporters walked up to and
    ///     used, booth/grid/lift pads walked onto - every hop is one leg, and the final leg is the
    ///     coordinates themselves. Returns a one-line summary for the owner's tell, including the
    ///     refusal when nothing usable connects the two. Safe from any thread.
    /// </summary>
    public string PlanTravel(int targetPf, Vector3? targetPos)
    {
        if (_pf < 0)
        {
            return "Not in a playfield yet - travel needs us on the ground first.";
        }

        if (!Zoning.Loaded)
        {
            return "No zoning data loaded - travel needs GameData/Zoning.json.";
        }

        lock (_travelLock)
        {
            if (targetPf == _pf)
            {
                _travel = null;
                if (targetPos.HasValue)
                {
                    SetDesiredGoal(targetPos.Value, targetPf, ControlPriority.Travel);
                    return $"Already in {Zoning.Name(targetPf)} - walking to ({targetPos.Value.X:0.0} {targetPos.Value.Z:0.0}).";
                }

                ClearDesiredGoal(ControlPriority.Travel); // any leg goal of a former plan is spent
                return $"Already in {Zoning.Name(targetPf)}.";
            }

            _travelFailed.Clear(); // a fresh order starts with a clean exit blacklist
            var route = Zoning.FindRoute(_pf, CurrentPosition, targetPf, targetPos, TravelOptions(null));
            if (route == null)
            {
                return $"No route from {Zoning.Name(_pf)} to {Zoning.Name(targetPf)} - nothing usable connects them in the zoning data.";
            }

            _travel = new TravelPlan { TargetPf = targetPf, TargetPos = targetPos };
            SetLeg(route.Hops[0].Exit);
            return $"Travel to {Zoning.Name(targetPf)} - {route.Describe()}" +
                   (targetPos.HasValue ? $", then ({targetPos.Value.X:0.0} {targetPos.Value.Z:0.0})." : ".");
        }
    }

    /// <summary>
    ///     Drops the travel plan - a manual order (goto/come) takes the body from it. The plan's
    ///     own leg goal goes with it: a cancelled trip must not leave the walk heading for the old
    ///     crossing point. A manual order that already replaced the leg goal is untouched - the
    ///     goto/come flow cancels BEFORE setting its own goal, and a plan that is already gone
    ///     (manual order cancelled it) makes this a no-op.
    /// </summary>
    public void CancelTravel()
    {
        lock (_travelLock)
        {
            if (_travel == null)
            {
                return;
            }

            _travel = null;
            ClearDesiredGoal(ControlPriority.Travel); // the leg goal was the plan's own (travelLock -> goalLock, as everywhere)
            _logger.LogInformation("TRAVEL: cancelled (a manual order takes the body).");
        }
    }

    /// <summary>
    ///     The target playfield of the active travel plan, or 0 when none is running - how another
    ///     controller tells "the plan died / was cancelled" from "still en route". Safe from any thread.
    /// </summary>
    public int TravelTargetPf
    {
        get
        {
            lock (_travelLock)
            {
                return _travel?.TargetPf ?? 0;
            }
        }
    }

    /// <summary>Compact one-line state for status replies. Safe from any thread.</summary>
    public string DescribeState()
    {
        string goalText;
        lock (_goallock)
        {
            goalText = goals.Count == 0
                ? "none"
                : string.Join("; ", goals.OrderByDescending(g => g.Key)
                    .Select(g => $"{(ControlPriority)g.Key} ({g.Key}) @ " +
                                 $"({g.Value.Position.X:0.0} {g.Value.Position.Y:0.0} {g.Value.Position.Z:0.0}) pf {g.Value.PlayfieldId}" +
                                 $"{(g.Value.Reached ? " REACHED" : "")}"));
        }

        return $"pf {_pf}, pos ({CurrentPosition.X:0.0} {CurrentPosition.Y:0.0} {CurrentPosition.Z:0.0}) | " +
               $"nav: {NavState()} | travel: {DescribeTravel()} | goals: {goalText}";
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
        Client.PostureToggled += OnPostureToggled;
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
        Client.PostureToggled -= OnPostureToggled;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _logger.LogInformation("Movement loop stopped.");
    }

    // The server's echo of the sit/stand toggle (action 0x57, Client.cs): the only reliable
    // "the posture change took effect" signal there is. Evidence only - the stand-up decision
    // itself is made once from the login mode and is never re-toggled.
    private void OnPostureToggled(Identity identity)
    {
        var me = DynelManager.LocalPlayer;
        if (me != null && identity == me.Identity)
        {
            _standEchoPending = false; // the toggle we were waiting for landed
            _logger.LogInformation("Movement: server confirmed the posture change (stand-up echo).");
        }
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

        // The login movement mode: authoritative ONCE, at login. It never updates afterwards, so the
        // walk reads it only until the stand-up decision is made.
        var movementMode = -1;
        if (me != null && me.TryGetStat(Stat.CurrentMovementMode, out var mm))
        {
            movementMode = mm;
        }

        // The owner, for follow: visible or not, his latest reported spot and facing ride the snapshot.
        PlayerChar? owner = null;
        if (me != null && !string.IsNullOrEmpty(_config.Owner))
        {
            owner = DynelManager.Players.FirstOrDefault(pl =>
                string.Equals(pl.Name, _config.Owner, StringComparison.OrdinalIgnoreCase));
        }

        _ownerInstance = owner?.Identity.Instance ?? 0;
        var moveFresh = owner != null && Environment.TickCount64 - Interlocked.Read(ref _ownerLastMoveAt) < 600;

        _snap = new Snapshot
        {
            Me = me,
            Playfield = (int)Playfield.ModelId,
            RunSpeed = runSpeed,
            MovementMode = movementMode,
            OwnerVisible = owner != null,
            OwnerPos = owner?.Transform.Position ?? default,
            OwnerHeading = owner?.Transform.Heading ?? Quaternion.Identity,
            OwnerMoveFresh = moveFresh,
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

        // The ONE login stand-up decision (AOBuddy10 Main.cs, wire-proven): the stand-up wire action
        // (CharacterAction 87 / 0x57) is a sit/stand TOGGLE, and stat 173 is only set from the login
        // FullCharacter and NEVER updates afterwards. Read it once through the snapshot, send at most
        // one stand-up if the login mode was seated, and never toggle again - firing it blind sat a
        // standing character, and re-reading a stuck 8=Sit and re-sending made him sit/stand in a
        // loop. Until the decision is made nothing walks: a seated body cannot move, the server
        // rejects every step and snaps him back to where he sat.
        if (!_stoodUp)
        {
            if (snap.MovementMode > 0)
            {
                _stoodUp = true;
                if (snap.MovementMode is MoveModeSit or MoveModeSleep or MoveModeLounge)
                {
                    _seated = true;
                    SendStandUp(me, "login mode " + snap.MovementMode);
                    _movement.Hold(me, SendIntervalMs);
                    return; // give the server the beat to apply it before the first step
                }

                _logger.LogInformation($"Movement: login mode {snap.MovementMode} (standing) - no stand-up needed.");
            }
            else
            {
                return; // the login FullCharacter has not carried the mode yet: hold, never walk blind
            }
        }

        // Seated bodies do not walk: the server rejects every step. Whatever sat us (the owner's sit
        // command) also cleared the goals; a NEW goal stands us up first (SetDesiredGoal).
        if (_seated)
        {
            _movement.Hold(me, SendIntervalMs);
            return;
        }

        // The stand-up campaign: a toggle the server never echoed (a login stand-up racing the
        // character load - a real-client logout always sits the character - or a dropped packet)
        // leaves the body sitting with the track saying standing. While any goal wants the body,
        // keep re-sending until the 0x57 echo lands or the tries run out. No goal: a chosen sit is
        // never fought.
        var nowS = _wetClock.Elapsed.TotalSeconds;
        bool anyGoal;
        lock (_goallock)
        {
            anyGoal = goals.Count > 0;
        }

        if (_standEchoPending && anyGoal)
        {
            if (_standTries >= MaxStandTries)
            {
                _standEchoPending = false;
                _logger.LogInformation($"Movement: no posture echo after {MaxStandTries} stand-ups - walking anyway.");
            }
            else if (nowS - _standSentAt >= StandRetrySeconds)
            {
                SendStandUp(me, "no posture echo");
            }
        }

        if (snap.Playfield != _pf)
        {
            // A new playfield puts the body wherever the server placed it; gait, mode, route and
            // follow state start over. The STOP GOES TO THE SERVER FIRST: a zone interrupts a run
            // mid-stream (the follow's lost-push onto the zone line, say), and wiping the local
            // state alone would leave the server holding ForwardStart with no ForwardStop - it
            // keeps the body running in the new playfield (owner, 2026-10-01: "after zoning is
            // done, the bot just runs on"). Movement.Stop sends the FullStop while Moving still
            // says so; Reset then clears the rest.
            var prevPf = _pf;
            Vector3? legExitPos = null; // the object exit we crossed through, for the proxy origin
            lock (_travelLock)
            {
                var leg = _travel?.LegExit;
                if (leg != null && leg.ObjInstance != 0)
                {
                    legExitPos = leg.A;
                }
            }

            _pf = snap.Playfield;
            _movement.Stop(me, SendIntervalMs);
            _movement.Reset();
            _follow.Reset();
            _route.Clear();
            _routeIdx = 0;
            _routePrio = -1;
            _routePf = -1;
            _stuckCells.Clear();
            _stuck.Reset();
            _logger.LogInformation($"Movement: playfield {_pf}, run state cleared (server stopped).");
            lock (_poslock)
            {
                _confirmedPosition = me.MovementComponent.Position;
                _confirmedHeading = me.MovementComponent.Heading;
                _pendingCorrection = null;
            }

            _logger.LogInformation($"Movement: playfield {_pf}, position " +
                                   $"({_confirmedPosition.X:0.0} {_confirmedPosition.Y:0.0} {_confirmedPosition.Z:0.0}).");

            TravelAfterZone(); // a travel plan takes its next leg here, or lands its final walk
            ProxyCrossed(prevPf, legExitPos);
        }

        ApplyPendingCorrection(me);

        // The playfield's nav data, built off every loop thread (Lush Fields took 6.3 s, log 2026-09-24):
        // while it loads there is no honest Y, so hold rather than walk blind.
        if (!_nav.Request(snap.Playfield, BaseDir, s => _logger.LogInformation(s), "MOVE"))
        {
            _movement.Hold(me, SendIntervalMs);
            return;
        }

        // This playfield's wet verdict, so a zone line is crossed at a dry point: Zoning.CrossLine
        // moves along the line to dry ground (Stret East Bank's line to Andromeda runs the whole south
        // border, and its nearest point was open water that never took the bot, owner 2026-09-27).
        if (_wetPf != snap.Playfield)
        {
            Func<float, float, bool> wet = null;
            var ground = _nav.Nav?.Ground;
            if (ground != null)
            {
                wet = (x, z) => !double.IsNaN(ground.SwimY(x, z, WadeDepth));
            }

            Zoning.SetWet(snap.Playfield, wet);
            _wetPf = snap.Playfield;
        }

        TravelTick(); // the leg watchdog: a line that never zones us must not strand the plan

        var goal = SelectActiveGoal(_pf);
        if (goal != null)
        {
            if (_follow.MirrorLocked)
            {
                _follow.BreakMirror("a goal took the body");
            }

            GoalWalk(me, snap, goal.Value, dt);
            return;
        }

        // No goal: follow drives the body (stack on the owner and mirror his movement packets);
        // without it, hold.
        if (_followOn)
        {
            _follow.Tick(me, snap.OwnerVisible, snap.OwnerPos, snap.OwnerHeading, snap.OwnerMoveFresh,
                RunVelocity(snap), dt, (m, target, stepDt) => WalkStep(m, snap, target, stepDt));
            return;
        }

        _movement.Hold(me, SendIntervalMs); // nowhere to go: hold position (guarded, no packet spam)
    }

    // The goal walk: arrival marking, the grid route round doors and zone lines, the stuck watch,
    // and the nav-Y step toward the current step target.
    private void GoalWalk(LocalPlayer me, Snapshot snap, KeyValuePair<int, GoalLocation> goal, double dt)
    {
        var pos = me.MovementComponent.Position;
        var dist = Movement.Flat(pos, goal.Value.Position);
        if (dist <= goal.Value.ArriveRadius)
        {
            _movement.Hold(me, SendIntervalMs);
            if (!goal.Value.Reached)
            {
                goal.Value.Reached = true;
                _logger.LogInformation($"Movement: reached the priority {goal.Key} goal " +
                                       $"({goal.Value.Position.X:0.0} {goal.Value.Position.Y:0.0} {goal.Value.Position.Z:0.0}), {dist:0.0} m out.");
            }

            // A manual order (priority Travel) hands the body straight back: 'come' should not park
            // the follow until 'stop'. Controllers' own goals are theirs to clear. A travel LEG is
            // not a handback: its crossing point is reached only to stand there while the zone
            // lands - mid-plan the body stays with the plan.
            if (_followOn && goal.Key == (int)ControlPriority.Travel)
            {
                bool travelActive;
                lock (_travelLock)
                {
                    travelActive = _travel != null;
                }

                if (!travelActive)
                {
                    ClearDesiredGoal(ControlPriority.Travel);
                    _logger.LogInformation("Movement: manual goal done - handing the body back to follow.");
                }
            }

            return;
        }

        // Not there (yet, or anymore): the flag only ever says reached while we stand on it.
        goal.Value.Reached = false;

        // WHERE TO STEP: the goal itself, or the next point of a grid ROUTE to it. The grid's
        // per-search blocked set keeps 2 m off every zone line and 3 m off every door/whompa/
        // teleporter that is not the goal itself (AOBuddy10 OverlandController.BeginLeg).
        var target = goal.Value.Position;
        var grid = _nav.Grid;
        if (grid != null)
        {
            if (_routePrio != goal.Key || _routePf != _pf || Movement.Flat(_routeGoal, target) > 0.01f)
            {
                PlanRoute(pos, target, goal.Key);
            }

            if (_route.Count == 0)
            {
                // The grid covers the whole playfield and says the goal cannot be walked to. HOLD:
                // the old beeline respected nothing and ran the bot through walls until the server
                // yanked it back (Newland City, 2026-10-01 21:25 and 21:44 - both log lines ended
                // in "walking straight"). Only a playfield WITHOUT grid data may walk straight
                // lines; with data, unreachable means stand and say so (the goal stays set, and a
                // changed goal or playfield re-plans).
                _movement.Hold(me, SendIntervalMs);
                return;
            }

            while (_routeIdx < _route.Count &&
                   Movement.Flat(pos, _route[_routeIdx]) <= (_routeIdx == _route.Count - 1 ? WaypointLastRange : WaypointRange))
            {
                _routeIdx++;
                _stuck.Reset();
            }

            if (_routeIdx < _route.Count)
            {
                target = _route[_routeIdx];
            }
        }

        var tdist = Movement.Flat(pos, target);
        var delta = target - pos;
        var flat = new Vector3(delta.X, 0f, delta.Z);
        if (flat.Magnitude < 0.05f)
        {
            _movement.Hold(me, SendIntervalMs); // straight up/down (a lift, a stacked floor): nothing to walk
            return;
        }

        var dir = flat.Normalize();

        // STUCK (AOBuddy10): no progress toward the step target for StuckSeconds - something the data
        // does not show is in the way (a gap in the walls, a crate, a fence). Block the few metres
        // ahead and route round them; after MaxStuck of those, say where we are and try again.
        if (grid != null && _stuck.Tick(tdist, dt, 0.3f, StuckSeconds))
        {
            _stuckCount++;
            _stuck.Reset();
            if (_stuckCount > MaxStuck)
            {
                _logger.LogInformation($"Movement: stuck at ({pos.X:0.0} {pos.Z:0.0}), {dist:0.0} m short of the goal - something is in the way.");
            }
            else
            {
                grid.CellsAlong(new Vector3(pos.X + dir.X, 0f, pos.Z + dir.Z),
                    new Vector3(pos.X + dir.X * 3f, 0f, pos.Z + dir.Z * 3f), 1f, _stuckCells);
                _logger.LogInformation($"Movement: no progress for {StuckSeconds:0} s at ({pos.X:0.0} {pos.Z:0.0}), routing round it ({_stuckCount}/{MaxStuck}).");
                _movement.Hold(me, SendIntervalMs);
                PlanRoute(pos, goal.Value.Position, goal.Key);
                return;
            }
        }

        WalkStep(me, snap, target, dt);
    }

    // One capped step toward a target, the proven outdoor walker (AOBuddy10 OverlandController):
    // mouse-look facing, then Advance at the run-speed formula with the step's Y taken from the NAV
    // DATA (the floor under the next position, the water surface over it), never from the target.
    // Shared by the goal walk and follow's approach/chase, so both ride the terrain identically.
    private void WalkStep(LocalPlayer me, Snapshot snap, Vector3 target, double dt)
    {
        var pos = me.MovementComponent.Position;
        var dist = Movement.Flat(pos, target);
        var delta = target - pos;
        var flat = new Vector3(delta.X, 0f, delta.Z);
        if (flat.Magnitude < 0.05f)
        {
            _movement.Hold(me, SendIntervalMs);
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

    // Plan the grid route to a goal (AOBuddy10 OverlandController.BeginLeg): every zone line is a
    // corridor of blocked cells (a same-playfield goal never wants one), and every contact exit
    // (door, whompa, teleporter, proxy) is a 3 m disc - except the one the goal stands on, and one we
    // are standing in (the first step must be able to leave it; owner, 2026-09-27: a Longest Road
    // walk stepped onto the Broken Shores booth 4 m from the whompa landing). No grid, or no route
    // even at wide reach: the walk beelines, and says so.
    private void PlanRoute(Vector3 from, Vector3 goalPos, int priority)
    {
        _routePrio = priority;
        _routeGoal = goalPos;
        _routePf = _pf;
        _routeIdx = 0;
        _stuckCount = 0;
        _stuck.Reset();
        _route.Clear();

        var grid = _nav.Grid;
        if (grid == null)
        {
            return; // no walk grid for this playfield: straight lines
        }

        var extra = new HashSet<int>(_stuckCells);

        // The travel leg's own line stays open: its goal IS the crossing point beyond it (a travel
        // goal never matches by accident - a manual goal in the same spot cancelled the plan).
        ZoneExit? passLine = null;
        lock (_travelLock)
        {
            var t = _travel;
            if (t?.LegExit != null && t.LegPf == _pf && Movement.Flat(t.LegGoal, goalPos) <= 0.01f)
            {
                passLine = t.LegExit;
            }
        }

        foreach (var e in Zoning.ExitsFrom(_pf))
        {
            if (e.Kind == ExitKind.ZoneLine)
            {
                if (!ReferenceEquals(e, passLine))
                {
                    grid.CellsAlong(e.A, e.B, 2f, extra);
                }
            }
            else if ((e.Kind == ExitKind.Line || e.Kind == ExitKind.Proxy || e.Kind == ExitKind.Teleport)
                     && Movement.Flat(e.A, goalPos) > 1.5f && Movement.Flat(e.A, from) > 3.5f)
            {
                grid.CellsAlong(e.A, e.A, 3f, extra);
            }
        }

        var route = grid.FindPath(from, goalPos, extra, SnapMeters, GoalReach, out var why)
                    ?? grid.FindPath(from, goalPos, extra, SnapMeters, WideGoalReach, out _);
        if (route == null)
        {
            _logger.LogInformation($"Movement: no grid route to the goal ({why}) - holding; a beeline would cross walls.");
            return;
        }

        _route = route;
        _logger.LogInformation($"Movement: {route.Count}-point route to the priority {priority} goal.");
    }

    // ── proxy origin (the memory a proxy playfield's back exit resolves against) ──────────

    // PROXY playfields (shops, houses - entered through a proxy door) have no static destination
    // in their exit data: the server wires the door per instance. Zoning carries the door as a
    // back exit (to 0); its destination is where we CAME FROM, which only the running bot knows.
    // Every zone-in records it here (and to proxy.json, so a reconnect inside the instance - the
    // packets then tell us only WHICH playfield we are in - keeps the way out). Zoning reads the
    // origin under a volatile swap.
    private sealed class ProxyMemory
    {
        public int pf, from;
        public float[]? fromPos;
    }

    private static string ProxyFile => Path.Combine(BaseDir, "proxy.json");

    private void ProxyCrossed(int prevPf, Vector3? legExitPos)
    {
        if (prevPf < 0)
        {
            // Login: the packets name the playfield, the file names the way we got in. A file for
            // another playfield stays untouched - it may become true again on a later zone-in.
            var mem = JsonStore.Load<ProxyMemory>(ProxyFile, s => _logger.LogInformation(s));
            if (mem != null && mem.pf == _pf && mem.from > 0)
            {
                Vector3? pos = mem.fromPos is { Length: 3 } p ? new Vector3(p[0], p[1], p[2]) : null;
                Zoning.SetProxyOrigin(_pf, mem.from, pos);
                _logger.LogInformation($"Movement: logged in inside {Zoning.Name(_pf)} - the way back to " +
                                       $"{Zoning.Name(mem.from)} is remembered from proxy.json.");
            }

            return;
        }

        Zoning.SetProxyOrigin(_pf, prevPf, legExitPos);
        var save = new ProxyMemory
        {
            pf = _pf,
            from = prevPf,
            fromPos = legExitPos.HasValue ? new[] { legExitPos.Value.X, legExitPos.Value.Y, legExitPos.Value.Z } : null,
        };
        JsonStore.Save(ProxyFile, Newtonsoft.Json.JsonConvert.SerializeObject(save), s => _logger.LogInformation(s));
    }

    // ── travel legs (the PlanTravel state machine, owner 2026-10-01) ──────────────────────

    // The current leg, shaped by its exit's kind (AOBuddy10 OverlandController.BeginLeg): a zone line
    // is walked THROUGH (the goal is a few metres OUT of the playfield, so the crossing - which is
    // what the server zones on - happens while the walk still has metres in hand); a booth, grid exit
    // or lift beam (kind Line) is a pad walked ONTO, exactly on its centre; a whompa or teleporter is
    // a terminal walked up to and then used. Called under _travelLock.
    private void SetLeg(ZoneExit hop)
    {
        var t = _travel!;
        t.LegExit = hop;
        t.LegPf = _pf;
        t.LegReachedAt = -1;
        t.AwaitAt = -1;
        if (hop.Back)
        {
            // A proxy playfield's exit door: the crossing is taken from inside the doorway only -
            // activation radius under half a metre (owner, 2026-10-01) - so walk ONTO the door and
            // use it on the stand (TravelTick), not through it.
            t.LegGoal = hop.A;
            SetDesiredGoal(hop.A, _pf, ControlPriority.Travel, DoorReach);
            _logger.LogInformation($"TRAVEL: leg out of {Zoning.Name(_pf)} - stepping into the exit door at ({hop.A.X:0.0} {hop.A.Z:0.0}).");
            return;
        }

        switch (hop.Kind)
        {
            case ExitKind.ZoneLine:
            {
                var (at, beyond, _) = Zoning.CrossLine(hop, CurrentPosition);
                t.LegGoal = beyond;
                SetDesiredGoal(beyond, _pf, ControlPriority.Travel, 1.5f);
                _logger.LogInformation($"TRAVEL: leg to {Zoning.Name(hop.ToPf)} - crossing the zone line at ({at.X:0.0} {at.Z:0.0}).");
                break;
            }
            case ExitKind.Line:
                t.LegGoal = hop.A;
                SetDesiredGoal(hop.A, _pf, ControlPriority.Travel, PadReach);
                _logger.LogInformation($"TRAVEL: leg to {Zoning.Name(hop.ToPf)} - stepping onto the pad at ({hop.A.X:0.0} {hop.A.Z:0.0}).");
                break;
            default:
                t.LegGoal = hop.A;
                SetDesiredGoal(hop.A, _pf, ControlPriority.Travel, ObjectReach);
                _logger.LogInformation($"TRAVEL: leg to {Zoning.Name(hop.ToPf)} - walking up to the {hop.Kind.ToString().ToLower()} at ({hop.A.X:0.0} {hop.A.Z:0.0}).");
                break;
        }
    }

    // A zone during a travel plan: arrived (final coordinates or done), or on to the next leg -
    // re-planned from wherever the server actually put us, so even a surprise zone (a beeline that
    // crossed a line the data placed elsewhere) cannot derail the plan.
    private void TravelAfterZone()
    {
        TravelPlan? t;
        lock (_travelLock)
        {
            t = _travel;
        }

        if (t == null)
        {
            return;
        }

        if (_pf == t.TargetPf)
        {
            lock (_travelLock)
            {
                if (_travel != t)
                {
                    return; // replaced meanwhile (a fresh travel order)
                }

                _travel = null;
            }

            if (t.TargetPos.HasValue)
            {
                SetDesiredGoal(t.TargetPos.Value, _pf, ControlPriority.Travel); // replaces the leg goal
                _logger.LogInformation($"TRAVEL: arrived in {Zoning.Name(_pf)} - walking to " +
                                       $"({t.TargetPos.Value.X:0.0} {t.TargetPos.Value.Z:0.0}).");
            }
            else
            {
                ClearDesiredGoal(ControlPriority.Travel); // the old playfield's leg goal is spent
                _logger.LogInformation($"TRAVEL: complete - {Zoning.Name(_pf)}.");
            }

            return;
        }

        var route = Zoning.FindRoute(_pf, CurrentPosition, t.TargetPf, t.TargetPos, TravelOptions(SnapshotFailed()));
        bool dead;
        lock (_travelLock)
        {
            if (_travel != t)
            {
                return;
            }

            if (route == null || route.Hops.Count == 0)
            {
                _travel = null;
                dead = true;
            }
            else
            {
                dead = false;
                SetLeg(route.Hops[0].Exit);
            }
        }

        if (dead)
        {
            ClearDesiredGoal(ControlPriority.Travel);
            _logger.LogWarning($"TRAVEL: no way on from {Zoning.Name(_pf)} to {Zoning.Name(t.TargetPf)} - travel abandoned.");
        }
    }

    // The blacklist as the planner sees it: a stable copy (the live set is mutated under the lock).
    private HashSet<ZoneExit> SnapshotFailed()
    {
        lock (_travelLock)
        {
            return new HashSet<ZoneExit>(_travelFailed);
        }
    }

    // The route ask for travel: requirements against our live stats (TryGetStat is the sanctioned
    // cross-thread read - the ConcurrentDictionary stats), Scotty off until its data lands, an exit
    // we cannot name (no object identity) or that failed on this trip left out.
    private ZoneRouteOptions TravelOptions(HashSet<ZoneExit> failed)
    {
        var o = Zoning.RouteOptions(DynelManager.LocalPlayer);
        o.Filter = e =>
            (e.Kind == ExitKind.ZoneLine || e.Kind == ExitKind.Scotty || e.ObjInstance != 0) &&
            (failed == null || !failed.Contains(e));
        return o;
    }

    // The wire-proven use (AOBuddy10 GameCommands.UseObject, capture 20260923-201746): GenericCmd Use
    // on a WORLD object - whompa, grid terminal, lift - Count=1, Temp4=1, the exact bytes the owner's
    // client sends. Sent from the walk thread; the send lock (the packet id) makes that safe.
    private void SendUse(ZoneExit e)
    {
        var me = DynelManager.LocalPlayer;
        if (me == null || e.ObjInstance == 0)
        {
            return; // no character, or an exit the data cannot name
        }

        Client.Send(new GenericCmdMessage
        { Action = GenericCmdAction.Use, User = me.Identity, Target = new Identity((IdentityType)e.ObjType, e.ObjInstance), Count = 1, Temp4 = 1 });
        _logger.LogInformation($"TRAVEL: used {e.ObjType}:{e.ObjInstance} at ({e.A.X:0.0} {e.A.Y:0.0} {e.A.Z:0.0}).");
    }

    // The leg watchdog. The NORMAL beat is the zone-in above, which advances the plan while the
    // server is still handing us the new playfield. This fires when a leg's goal reads reached and
    // the leg must finish its own business (AOBuddy10's Settle/Use/AwaitZone): stand still a moment
    // so the server has our stop, use terminals (a pad only on its last stand - standing is what
    // takes you), then wait per kind for the zone. When the wait runs out with no zone, the exit is
    // walked up to again - until the try budget is spent, and then it is written off and the plan
    // re-routes round it. A same-playfield exit (lift beam, inner teleporter) zones nobody: when a
    // server correction lands us at its arrival point, the ride is simply taken and the plan moves
    // on from there.
    private void TravelTick()
    {
        TravelPlan? t;
        lock (_travelLock)
        {
            t = _travel;
        }

        if (t?.LegExit == null)
        {
            return;
        }

        bool retry = false, writeOff = false, rideTaken = false;
        lock (_goallock)
        {
            if (!goals.TryGetValue((int)ControlPriority.Travel, out var g) ||
                g.PlayfieldId != t.LegPf || Movement.Flat(g.Position, t.LegGoal) > 0.01f || !g.Reached)
            {
                // Not (any more) standing on the leg goal: restart any window. But a same-playfield
                // exit that has meanwhile MOVED us to its arrival point has done its job.
                t.LegReachedAt = -1;
                t.AwaitAt = -1;
                var e = t.LegExit;
                if (e.ToPf == t.LegPf && e.Arrival.HasValue &&
                    Movement.Flat(CurrentPosition, e.Arrival.Value) < 5f &&
                    Movement.Flat(CurrentPosition, t.LegGoal) > 2f)
                {
                    rideTaken = true;
                }
                else
                {
                    return;
                }
            }
            else
            {
                var now = _wetClock.Elapsed.TotalSeconds;
                if (t.LegReachedAt < 0)
                {
                    t.LegReachedAt = now;
                }

                if (now - t.LegReachedAt < SettleSeconds)
                {
                    return; // settle: the server has our stop before we use from where it has us
                }

                if (t.AwaitAt < 0)
                {
                    t.AwaitAt = now;
                    t.Tries++;
                    var kind = t.LegExit.Kind;
                    if (t.LegExit.Back || kind == ExitKind.Teleport || kind == ExitKind.Proxy
                        || kind == ExitKind.Line && t.Tries >= MaxLegTries)
                    {
                        SendUse(t.LegExit); // terminals and exit doors are used; a pad only ever on its last stand
                    }

                    return; // the window starts
                }

                var wait = t.LegExit.Back || t.LegExit.Kind == ExitKind.Teleport || t.LegExit.Kind == ExitKind.Proxy ? ObjectWait
                    : t.LegExit.Kind == ExitKind.Line ? PadWait
                    : ZoneLineWait;
                if (now - t.AwaitAt < wait)
                {
                    return; // the zone normally lands long before this
                }

                if (t.Tries < MaxLegTries)
                {
                    retry = true; // walk up / over again
                }
                else
                {
                    writeOff = true; // the exit gave nothing: route round it
                }
            }
        }

        if (rideTaken)
        {
            _logger.LogInformation("TRAVEL: the exit moved us within its own playfield - planning on from there.");
            TravelAfterZone();
            return;
        }

        if (retry)
        {
            _logger.LogInformation($"TRAVEL: try {t.Tries + 1}/{MaxLegTries} at {t.LegExit}.");
            lock (_travelLock)
            {
                if (_travel == t)
                {
                    SetLeg(t.LegExit);
                }
            }

            return;
        }

        if (writeOff)
        {
            WriteOffLeg(t);
        }
    }

    // An exit that gave nothing MaxLegTries times: on the blacklist, and the plan re-routes round it
    // (AOBuddy10 FailExit) - or, when nothing left connects, ends honestly.
    private void WriteOffLeg(TravelPlan t)
    {
        HashSet<ZoneExit> failed;
        lock (_travelLock)
        {
            if (_travel != t)
            {
                return;
            }

            _travelFailed.Add(t.LegExit);
            failed = new HashSet<ZoneExit>(_travelFailed);
            _logger.LogInformation($"TRAVEL: {t.LegExit} gave nothing after {MaxLegTries} tries - routing round it.");
        }

        var route = Zoning.FindRoute(_pf, CurrentPosition, t.TargetPf, t.TargetPos, TravelOptions(failed));
        bool dead;
        lock (_travelLock)
        {
            if (_travel != t)
            {
                return;
            }

            if (route == null || route.Hops.Count == 0)
            {
                _travel = null;
                dead = true;
            }
            else
            {
                dead = false;
                SetLeg(route.Hops[0].Exit);
            }
        }

        if (dead)
        {
            ClearDesiredGoal(ControlPriority.Travel);
            _logger.LogWarning($"TRAVEL: nothing left between {Zoning.Name(_pf)} and {Zoning.Name(t.TargetPf)} " +
                               "once the dead exits are out - travel abandoned.");
        }
    }

    // The travel line for 'status'.
    private string DescribeTravel()
    {
        lock (_travelLock)
        {
            if (_travel == null)
            {
                return "none";
            }

            var to = $"to {Zoning.Name(_travel.TargetPf)}";
            if (_travel.TargetPos.HasValue)
            {
                to += $" at ({_travel.TargetPos.Value.X:0.0} {_travel.TargetPos.Value.Z:0.0})";
            }

            return _travel.LegExit == null
                ? to
                : $"{to}, leg: {_travel.LegExit} (try {_travel.Tries}/{MaxLegTries})";
        }
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

        // A correction moved us off his stream: the mirror cannot copy what the server overrode
        // (AOBuddy10 OnServerCorrectedMe broke the mirror here too); the catch-up re-locks.
        _follow.BreakMirror("server correction");

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
    // CurrentPosition. The OWNER's moves are follow's raw material: stamped as "he is moving" and,
    // while the mirror is locked, queued for the walk thread to replay as ours (AOBuddy10 Main.cs:
    // "his move is our move"). Everyone's packets return false - the SDK's DynelManager keeps the
    // transforms fresh, and we are observers here.
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
            else if (_ownerInstance != 0 && m.Identity.Instance == _ownerInstance)
            {
                Interlocked.Exchange(ref _ownerLastMoveAt, Environment.TickCount64);
                if (_follow.MirrorLocked && Movement.IsMirrorable(m.MoveType))
                {
                    _follow.MirrorQueue.Enqueue(m);
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
        public int MovementMode; // the login CurrentMovementMode (stat 173); -1 = not sent yet
        public bool OwnerVisible;
        public Vector3 OwnerPos;
        public Quaternion OwnerHeading;
        public bool OwnerMoveFresh; // his last movement packet is under 600 ms old
    }

    // A travel order's state (guarded by _travelLock): where the trip ends and the leg currently
    // walked - its goal, the settle/use/await beat, and the try budget. The route itself is re-derived
    // from the Zoning graph after every zone, not stored.
    private sealed class TravelPlan
    {
        public int TargetPf;
        public Vector3? TargetPos; // null = just get to the playfield
        public ZoneExit LegExit; // the exit this leg takes; null between legs
        public Vector3 LegGoal; // where the leg walks: past the line, on the pad, or at the terminal
        public int LegPf; // the playfield the leg walks in
        public double LegReachedAt = -1; // standing on the leg goal since (settle beat starts here)
        public double AwaitAt = -1; // the post-settle window: use sent (terminals) or standing (pads)
        public int Tries; // visits to this leg's goal: uses, stand-ons, walk-throughs
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