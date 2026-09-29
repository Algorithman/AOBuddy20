using AOSharp.Common.GameData;

namespace AOSharp.Clientless;

public class ChestItem : SimpleItem
{
    public ChestItem(Identity identity, Vector3? position, Quaternion? rotation, Dictionary<Stat, int> stats) : base(identity, position, rotation,
        stats)
    {
    }

    public ChestItem(Identity identity, Dictionary<Stat, int> stats) : base(identity, stats)
    {
    }
}