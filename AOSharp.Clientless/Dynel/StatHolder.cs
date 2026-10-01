using System.Collections.Concurrent;
using AOSharp.Common.GameData;

namespace AOSharp.Clientless;

// STATS FROM ANY THREAD (owner, 2026-10-01: "there may be later need to read the stats from
// other threads"). The update thread writes stats as packets arrive (Stat, Skill, HealthDamage,
// TeamMemberInfo), while other threads read them - the movement thread's run speed today,
// Awareness on its own thread next (Readme point 8). A plain Dictionary is undefined under a
// concurrent read/write (a resize mid-read throws or spins), so the store is a
// ConcurrentDictionary: lock-free reads, per-key writes, same get/try-get semantics.
// ONE exception: LocalPlayer.GetStat adds equipment bonuses by enumerating the inventory, and
// the inventory is only safe on the update thread - cross-thread readers use TryGetStat (the
// raw wire stat).
public class StatHolder
{
    private readonly ConcurrentDictionary<Stat, int> _stats = new ConcurrentDictionary<Stat, int>();

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
