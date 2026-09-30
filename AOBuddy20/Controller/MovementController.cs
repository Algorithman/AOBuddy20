// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: MovementController.cs
// 
// Last modified: 2026-09-30 10:48
// Created:       2026-09-30 10:09
// 
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using System.Diagnostics;
using AOBuddy20.Enums;
using AOBuddy20.Interfaces;
using AOBuddy20.Network;
using AOBuddy20.Utils;
using AOSharp.Clientless;
using AOSharp.Common.GameData;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOBuddy20.Controlling;

[MinLogLevel(LogEventLevel.Debug)]
public sealed class MovementController : MovementComponent, IPacketConsumer
{
    public static readonly Vector3 Up = new Vector3(0f, 1f, 0f);


    private readonly object _goallock = new object();

    private readonly object _lock = new object();
    private readonly ILogger<MovementController> _logger;

    // DeltaTime since the previous movement packet, as the real client sends it: a capture of a live
    // client walking (20260925-141138) carries 9-29 ms between moves, and the server reads the field to
    // judge how far a step may claim. It was never set here — every packet said 0 ms, i.e. infinite
    // implied speed — and the server rubberbanded the bot back after ~8 m of "instant" progress
    // (Wailing Wastes 2026-09-25, the easy-slope rubberbanding).
    private readonly Stopwatch _sinceLastMove = Stopwatch.StartNew();

    private readonly Dictionary<int, GoalLocation> goals = new Dictionary<int, GoalLocation>();

    private Vector3 _currentPosition;

    private DateTime _lastMovementPacket = DateTime.MaxValue;
    private volatile bool _leashed;

  

    public MovementController(ILogger<MovementController> logger)
    {
        _logger = logger;
        _logger.Log(LogLevel.Information, "Movement controller initialized.");
    }

    public Vector3 CurrentPosition
    {
        get
        {
            lock (_lock)
            {
                return _currentPosition;
            }
        }
        private set
        {
            lock (_lock)
            {
                _currentPosition = value;
            }
        }
    }

    public void RegisterPackets(PacketRouter router)
    {
        router.Register(DCMoveHandler, N3MessageType.CharDCMove, (int)ControlPriority.None);
    }
    

    private bool DCMoveHandler(AOMessage arg)
    {
        // TODO: movement read from server here
        // confirmed movement is written to local storage
        var dcmove = (CharDCMoveMessage)arg.Body;
        switch (dcmove.MoveType)
        {
            case MovementAction.Update:
                // TODO implement
                break;
        }

        throw new NotImplementedException();
    }

 

    public void SetDesiredGoal(Vector3 desiredGoal, int playfieldId, int priority)
    {
        lock (_goallock)
        {
            goals[priority] = new GoalLocation { Position = desiredGoal, PlayfieldId = playfieldId, };
        }
    }

    internal struct GoalLocation
    {
        internal Vector3 Position { get; set; }
        internal int PlayfieldId { get; set; }
    }
}