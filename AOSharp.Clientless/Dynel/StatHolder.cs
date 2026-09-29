using AOSharp.Common.GameData;

namespace AOSharp.Clientless;

public class StatHolder
{
    private readonly Dictionary<Stat, int> _stats = new Dictionary<Stat, int>();

    internal void SetStat(Stat stat, int value)
    {
        _stats[stat] = value;
    }

    public virtual int GetStat(Stat stat)
    {
        return _stats[stat];
    }

    public T GetStat<T>(Stat stat)
    {
        return (T)(object)_stats[stat];
    }

    public virtual bool TryGetStat(Stat stat, out int value)
    {
        return _stats.TryGetValue(stat, out value);
    }
}