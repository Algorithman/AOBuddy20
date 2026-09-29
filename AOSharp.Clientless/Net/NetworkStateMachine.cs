using System.Net;
using Serilog;
using Stateless;

namespace AOSharp.Clientless.Net;

internal enum Trigger
{
    Stop,
    Connect,
    Disconnect,
    OnTcpConnected,
    OnTcpDisconnected,
    OnTcpConnectionError,
    OnTcpConnectError,
    ConnectionEstablished,
    FailedToRetreiveDimensionInfo,
    FailedToLogin,
    Error107,
    CharInPlay,
    BeginZoning,
    EndZoning,
}

internal enum State
{
    Idle,
    Disconnected,
    Connecting,
    Connected,
    Authenticating,
    CharacterSelect,
    InPlay,
    Zoning,
}

internal class NetworkStateMachine : StateMachine<State, Trigger>
{
    private readonly ILogger _logger;
    public TriggerWithParameters<IPEndPoint, Exception> ConnectErrorTrigger;
    public TriggerWithParameters<IPEndPoint> ConnectTrigger;

    public NetworkStateMachine(ILogger logger) : base(State.Idle)
    {
        _logger = logger;
        ConnectTrigger = SetTriggerParameters<IPEndPoint>(Trigger.Connect);
        ConnectErrorTrigger = SetTriggerParameters<IPEndPoint, Exception>(Trigger.OnTcpConnectionError);
        OnTransitioned(OnTransitionAction);
    }

    private void OnTransitionAction(Transition obj)
    {
        _logger.Debug($"Gameserver state transition from {obj.Source} to {obj.Destination} triggered by {obj.Trigger}. Re-entry is {obj.IsReentry}");
    }
}