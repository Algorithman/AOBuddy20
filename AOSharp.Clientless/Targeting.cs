using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Clientless;

public static class Targeting
{
    public static void SetTarget(SimpleChar target)
    {
        SetTarget(target.Identity);
    }

    public static void SetTarget(Identity target)
    {
        Client.Send(new LookAtMessage
        {
            Target = target,
        });
    }
}