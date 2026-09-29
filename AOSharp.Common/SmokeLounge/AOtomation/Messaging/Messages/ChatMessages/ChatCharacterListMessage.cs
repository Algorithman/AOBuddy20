using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

[AoContract((int)ChatMessageType.CharacterList)]
public class ChatCharacterListMessage : ChatMessageBody
{
    #region Public Properties

    public override ChatMessageType PacketType => ChatMessageType.CharacterList;

    #endregion

    [AoMember(0, SerializeSize = ArraySizeType.Int16)]
    public uint[] Ids { get; set; }

    [AoMember(1, SerializeSize = ArraySizeType.Int16)]
    public string[] Names { get; set; }

    [AoMember(2, SerializeSize = ArraySizeType.Int16)]
    public int[] Levels { get; set; }

    [AoMember(3, SerializeSize = ArraySizeType.Int16)]
    public bool[] Online { get; set; }

    public ChatCharacter[] Characters => ToCharacters();

    private ChatCharacter[] ToCharacters()
    {
        var characters = new ChatCharacter[Names.Length];

        for (var i = 0; i < Names.Length; i++)
        {
            characters[i] = new ChatCharacter
            {
                Name = Names[i],
                Id = Ids[i],
                Level = Levels[i],
                Online = Online[i],
            };
        }

        return characters;
    }
}

public class ChatCharacter
{
    public uint Id;
    public int Level;
    public string Name;
    public bool Online;
}