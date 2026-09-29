using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

[AoContract((int)ChatMessageType.LoginError)]
public class ChatLoginErrorMessage : ChatMessageBody
{
    #region Public Properties

    public override ChatMessageType PacketType => ChatMessageType.LoginError;

    #endregion

    [AoMember(0, SerializeSize = ArraySizeType.Int16)]
    public string Message { get; set; }
}