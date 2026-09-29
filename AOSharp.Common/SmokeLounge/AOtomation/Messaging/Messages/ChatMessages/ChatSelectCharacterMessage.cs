using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

[AoContract((int)ChatMessageType.SelectCharacter)]
public class ChatSelectCharacterMessage : ChatMessageBody
{
    #region Public Properties

    public override ChatMessageType PacketType => ChatMessageType.SelectCharacter;

    #endregion

    [AoMember(0)] public uint CharacterId { get; set; }
}