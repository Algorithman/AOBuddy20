using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

[AoContract((int)ChatMessageType.Ping)]
public class ChatPingMessage : ChatMessageBody
{
    public ChatPingMessage()
    {
        Data = new byte[] { 0, 1, 2, };
    }

    #region Public Properties

    public override ChatMessageType PacketType => ChatMessageType.Ping;

    #endregion

    [AoMember(0)] public byte[] Data { get; set; }
}