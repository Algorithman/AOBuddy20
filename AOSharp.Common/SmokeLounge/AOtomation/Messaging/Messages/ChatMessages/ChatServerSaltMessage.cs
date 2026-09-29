using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

[AoContract((int)ChatMessageType.ServerSalt)]
public class ChatServerSaltMessage : ChatMessageBody
{
    #region Public Properties

    public override ChatMessageType PacketType => ChatMessageType.ServerSalt;

    #endregion

    [AoMember(0, SerializeSize = ArraySizeType.Int16)]
    public byte[] ServerSalt { get; set; }
}