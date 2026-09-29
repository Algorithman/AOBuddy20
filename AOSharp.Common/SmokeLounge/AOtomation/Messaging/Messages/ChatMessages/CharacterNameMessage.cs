using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

[AoContract((int)ChatMessageType.CharacterName)]
public class CharacterNameMessage : ChatMessageBody
{
    #region Public Properties

    public override ChatMessageType PacketType => ChatMessageType.CharacterName;

    #endregion

    [AoMember(0)] public uint Id { get; set; }

    [AoMember(1, SerializeSize = ArraySizeType.Int16)]
    public string Name { get; set; }
}