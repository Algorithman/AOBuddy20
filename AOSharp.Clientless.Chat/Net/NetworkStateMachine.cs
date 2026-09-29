using System.Net;
using Serilog;
using Stateless;

namespace AOSharp.Clientless.Chat.Net;

public enum Trigger
{
    Stop,
    Connect,
    Disconnect,
    OnTcpConnected,
    OnTcpDisconnected,
    OnTcpConnectionError,
    OnTcpConnectError,
    FailedToRetreiveDimensionInfo,
    ConnectionEstablished,
    LoginOK,
}

public enum State
{
    Idle,
    Disconnected,
    Connecting,
    Connected,
    Authenticating,
    CharacterSelect,
    Chatting,
}

public class NetworkStateMachine : StateMachine<State, Trigger>
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
        _logger.Debug($"Chat state transition from {obj.Source} to {obj.Destination} triggered by {obj.Trigger}. Re-entry is {obj.IsReentry}");
    }
}