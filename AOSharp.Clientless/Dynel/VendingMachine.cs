using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;

namespace AOSharp.Clientless;

public class VendingMachine : SimpleItem
{
    public VendingMachine(Identity identity, Vector3? pos, Quaternion? rot, GameTuple<Stat, int>[] stats) : base(identity, pos, rot, stats.ToDict())
    {
        SetName();
    }

    public new string Name { get; internal set; }

    private void SetName()
    {
        if (Stats.TryGetValue(Stat.StaticInstance, out var id) && id != 0)
        {
            Name = new UniqueItem(Identity.None, Identity, id, id, 1, Stats).Name;
        }
    }

    public override bool TryGetStat(Stat stat, out int value)
    {
        return Stats.TryGetValue(stat, out value);
    }
}