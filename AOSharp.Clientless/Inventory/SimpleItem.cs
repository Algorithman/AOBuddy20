using AOSharp.Common.GameData;

namespace AOSharp.Clientless;

public class SimpleItem : Dynel
{
    public ACGItemQueryData ACGItem;
    public Dictionary<Stat, int> Stats;

    public SimpleItem(Identity identity, Vector3? position, Quaternion? rotation, Dictionary<Stat, int> stats) : this(identity, stats)
    {
        if (position.HasValue && rotation.HasValue)
        {
            InitTransform(position.Value, rotation.Value);
        }
    }

    public SimpleItem(Identity identity, Dictionary<Stat, int> stats) : base(identity)
    {
        SetStats(stats);
    }


    private void SetStats(Dictionary<Stat, int> stats)
    {
        Stats = stats;

        if (Stats.TryGetValue(Stat.ACGItemTemplateID, out var lowId) && lowId == 0)
        {
            return;
        }

        ACGItem = new ACGItemQueryData
        {
            LowId = Stats.FirstOrDefault(x => x.Key == Stat.ACGItemTemplateID).Value,
            HighId = Stats.FirstOrDefault(x => x.Key == Stat.ACGItemTemplateID2).Value,
            QL = Stats.FirstOrDefault(x => x.Key == Stat.ACGItemLevel).Value,
        };
    }
}