using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Clientless;

public class PlayerChar : SimpleChar
{
    public PlayerChar(SimpleCharFullUpdateMessage simpleCharMsg) : base(simpleCharMsg)
    {
    }

    public Profession Profession => (Profession)GetStat(Stat.Profession);
}