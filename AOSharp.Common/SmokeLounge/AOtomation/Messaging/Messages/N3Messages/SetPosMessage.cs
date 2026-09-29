using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

[AoContract((int)N3MessageType.SetPos)]
public class SetPosMessage : N3Message
{
    #region Constructors and Destructors

    public SetPosMessage()
    {
        N3MessageType = N3MessageType.SetPos;
    }

    #endregion

    #region AoMember Properties

    [AoMember(0)] public Vector3 Position { get; set; }

    #endregion
}