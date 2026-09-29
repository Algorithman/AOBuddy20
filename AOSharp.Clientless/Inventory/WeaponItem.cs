using AOSharp.Common.GameData;

namespace AOSharp.Clientless;

public class WeaponItem : SimpleItem
{
    public WeaponItem(Identity identity, Vector3? position, Quaternion? rotation, Dictionary<Stat, int> stats) : base(identity, position, rotation,
        stats)
    {
    }

    public WeaponItem(Identity identity, Dictionary<Stat, int> stats) : base(identity, stats)
    {
    }
}