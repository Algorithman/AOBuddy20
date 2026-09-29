using System.Net.Sockets;
using Serilog;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Serialization.Serializers;
using StreamReader = SmokeLounge.AOtomation.Messaging.Serialization.StreamReader;

namespace AOSharp.Clientless.Chat.Net;

internal class TcpClientEx : TcpClient
{
    private const ushort HeaderSize = 4;
    private const ushort RecvBufferSize = 8192;
    private readonly List<byte> _buffer;
    private readonly ChatHeaderSerializer _headerSerializer;
    private readonly ILogger _logger;

    public EventHandler Disconnected;
    public EventHandler<byte[]> PacketRecv;
    private byte[] _recvBuffer;

    public TcpClientEx(ILogger logger) : base(AddressFamily.InterNetwork)
    {
        _logger = logger;
        _buffer = new List<byte>();
        _headerSerializer = new ChatHeaderSerializer();
        ReceiveTimeout = 90000;
    }

    public void Send(byte[] bytes)
    {
        if (Connected)
        {
            GetStream().BeginWrite(bytes, 0, bytes.Length, SendCallback, null);
        }
    }

    private void SendCallback(IAsyncResult result)
    {
        try
        {
            GetStream().EndWrite(result);
        }
        catch (Exception e)
        {
            _logger.Error($"Failed to send message: {e}");
        }
    }

    private void ProcessBuffer()
    {
        while (_buffer.Count >= HeaderSize)
        {
            var header = DeserializeHeader(_buffer.Take(HeaderSize).ToArray());

            if (_buffer.Count < header.Size)
            {
                break;
            }

            PacketRecv?.Invoke(null, _buffer.Take(header.Size + HeaderSize).ToArray());
            _buffer.RemoveRange(0, header.Size + HeaderSize);
        }
    }

    private void ReceiveCallback(IAsyncResult result)
    {
        if (!Connected)
        {
            return;
        }

        try
        {
            var bytesRead = GetStream().EndRead(result);

            if (bytesRead == 0)
            {
                Disconnected?.Invoke(null, null);
                return;
            }

            var readBytes = _recvBuffer.Take(bytesRead).ToArray();
            _buffer.AddRange(readBytes);

            ProcessBuffer();
        }
        catch (Exception e)
        {
            Disconnected?.Invoke(null, null); // here

            _logger.Error("Error on EndRead:\n" + e);
            return;
        }

        BeginReceiving();
    }

    public void BeginReceiving()
    {
        if (!Connected)
        {
            return;
        }

        _recvBuffer = new byte[RecvBufferSize];

        GetStream().BeginRead(_recvBuffer, 0, RecvBufferSize, ReceiveCallback, null);
    }

    private ChatHeader DeserializeHeader(byte[] header)
    {
        using (var memStream = new MemoryStream(header))
        using (var reader = new StreamReader(memStream))
        {
            return (ChatHeader)_headerSerializer.Deserialize(reader, null);
        }
    }
}