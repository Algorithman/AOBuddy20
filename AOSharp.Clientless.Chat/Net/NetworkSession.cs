using System.Collections.Concurrent;
using System.Net;
using AOSharp.Clientless.Common;
using AOSharp.Common;
using Serilog;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Serialization;

namespace AOSharp.Clientless.Chat.Net;

public class NetworkSession
{
    private readonly ConcurrentQueue<byte[]> _inboundPacketQueue = new ConcurrentQueue<byte[]>();

    private readonly ILogger _logger;
    private readonly ChatMessageSerializer _serializer = new ChatMessageSerializer();
    public Dimension Dimension;
    public EventHandler<GroupMsg> GroupMessageReceived;
    public EventHandler<ChatMessage> NetworkMessageReceived;
    public EventHandler<PrivateGroupInviteArgs> PrivateGroupInviteMessageReceived;
    public EventHandler<PrivateGroupMsg> PrivateGroupMessageReceived;
    public EventHandler<PrivateMessage> PrivateMessageReceived;

    public Action Ready;
    public EventHandler<VicinityMsg> VicinityMessageReceived;
    protected bool _autoReconnect = true;

    protected Dictionary<ChatMessageType, Action<ChatMessageBody>> _chatMsgCallbacks;
    private bool _isFirstLogin = true;
    protected int _reconnectDelay = 5000;
    protected NetworkStateMachine _stateMachine;
    private TcpClientEx _tcpClient;

    internal NetworkSession(Dimension dimension, ILogger logger)
    {
        Dimension = dimension;
        _logger = logger;
        InitializeStateMachine();
        RegisterChatMessageHandlers();
    }

    public virtual void Update(double deltaTime)
    {
        while (_inboundPacketQueue.TryDequeue(out var packet))
        {
            ProcessCachedPacket(packet);
        }
    }

    protected void Connect()
    {
        try
        {
            var dimensionInfo = Dimension == Dimension.RubiKa ? DimensionInfo.RubiKa : DimensionInfo.RubiKa2019;
            var chatServerEndpoint = new IPEndPoint(Dns.GetHostEntry(dimensionInfo.ChatServerEndpoint.Host).AddressList[0],
                dimensionInfo.ChatServerEndpoint.Port);
            _stateMachine.Fire(_stateMachine.ConnectTrigger, chatServerEndpoint);
        }
        catch (WebException ex)
        {
            _logger.Error($"Failed to retrieve dimension info. {ex}");
            _stateMachine.Fire(Trigger.FailedToRetreiveDimensionInfo);
        }
    }

    private void Connect(IPEndPoint endpoint)
    {
        _logger.Debug($"Connecting to {endpoint}");

        if (_tcpClient != null && _tcpClient.Connected)
        {
            _tcpClient.Close();
        }

        _tcpClient = new TcpClientEx(_logger);
        _tcpClient.PacketRecv += (e, p) => _inboundPacketQueue.Enqueue(p);
        _tcpClient.Disconnected += (e, p) => _stateMachine.Fire(Trigger.Disconnect);
        _tcpClient.BeginConnect(endpoint.Address, endpoint.Port, ConnectCallback, endpoint);
    }

    private void ConnectCallback(IAsyncResult result)
    {
        try
        {
            _tcpClient.EndConnect(result);
            _stateMachine.Fire(Trigger.OnTcpConnected);
        }
        catch (Exception)
        {
            var endpoint = result.AsyncState as IPEndPoint;

            _logger.Debug($"Failed to connect to {endpoint}");

            _stateMachine.Fire(Trigger.OnTcpConnectError);
        }
    }

    public virtual void Disconnect()
    {
        _stateMachine.Fire(Trigger.Disconnect);
    }

    public void Send(ChatMessageBody messageBody)
    {
        var message = new ChatMessage
        {
            Body = messageBody,
            Header = new ChatHeader
            {
                PacketType = messageBody.PacketType,
            },
        };

        Send(message);
    }

    public void Send(ChatMessage message)
    {
        using (var stream = new MemoryStream())
        {
            _serializer.Serialize(stream, message);
            _tcpClient.Send(stream.ToArray());
        }
    }

    public void Send(byte[] packet)
    {
        _tcpClient.Send(packet);
    }

    private void ProcessCachedPacket(byte[] packet)
    {
        try
        {
            //_logger.Debug($"Recv - {BitConverter.ToString(packet).Replace("-", "")}");

            var message = _serializer.Deserialize(packet);

            if (message == null)
            {
                return;
            }

            NetworkMessageReceived?.Invoke(null, message);

            if (_chatMsgCallbacks.TryGetValue(message.Body.PacketType, out var callback))
            {
                callback.Invoke(message.Body);
            }
        }
        catch (Exception e)
        {
            _logger.Error($"Failed to deserialize packet: {packet.ToHexString()}");
            _logger.Error(e.ToString());
        }
    }

    private void InitializeStateMachine()
    {
        _stateMachine = new NetworkStateMachine(_logger);

        _stateMachine.Configure(State.Idle)
            .Permit(Trigger.Connect, State.Connecting);

        _stateMachine.Configure(State.Disconnected)
            .OnEntry(() => Reconnect())
            .Permit(Trigger.Connect, State.Connecting)
            .PermitReentry(Trigger.FailedToRetreiveDimensionInfo);

        _stateMachine.Configure(State.Connecting)
            .OnEntryFrom(_stateMachine.ConnectTrigger, endpoint => Connect(endpoint))
            .OnEntryFrom(_stateMachine.ConnectErrorTrigger, (endpoint, exception) => Connect(endpoint))
            .Permit(Trigger.OnTcpConnectError, State.Disconnected)
            .Permit(Trigger.Disconnect, State.Disconnected)
            .Permit(Trigger.OnTcpConnected, State.Connected);

        _stateMachine.Configure(State.Connected)
            .OnEntryFrom(Trigger.OnTcpConnected, () =>
            {
                _tcpClient.BeginReceiving();
                _stateMachine.Fire(Trigger.ConnectionEstablished);
            })
            .PermitIf(Trigger.OnTcpConnectionError, State.Connecting, () => _autoReconnect)
            .PermitIf(Trigger.OnTcpConnectionError, State.Disconnected, () => !_autoReconnect)
            .Permit(Trigger.ConnectionEstablished, State.Authenticating)
            .Permit(Trigger.Connect, State.Connecting)
            .Permit(Trigger.Disconnect, State.Disconnected);

        _stateMachine.Configure(State.Authenticating)
            .SubstateOf(State.Connected)
            .Permit(Trigger.Disconnect, State.Disconnected)
            .Permit(Trigger.LoginOK, State.Chatting);

        _stateMachine.Configure(State.Chatting)
            .OnEntry(() =>
            {
                if (_isFirstLogin)
                {
                    Ready?.Invoke();
                    _isFirstLogin = false;
                }
            })
            .SubstateOf(State.Connected)
            .Permit(Trigger.Disconnect, State.Disconnected);
    }

    private void Reconnect()
    {
        _tcpClient.Close();

        if (_autoReconnect)
        {
            Task.Delay(_reconnectDelay).ContinueWith(t => Connect());
        }
        else
        {
            _stateMachine.Fire(Trigger.Stop);
        }
    }

    protected virtual void RegisterChatMessageHandlers()
    {
    }
}