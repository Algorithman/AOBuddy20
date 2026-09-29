using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;

namespace AOSharp.Clientless;

public static class Extensions
{
    public static bool Find(this IReadOnlyDictionary<Stat, Cooldown> cooldowns, Stat stat, out Cooldown cooldown)
    {
        return cooldowns.TryGetValue(stat, out cooldown);
    }

    public static bool Contains(this IReadOnlyDictionary<Stat, Cooldown> cooldowns, Stat stat)
    {
        return cooldowns.ContainsKey(stat);
    }

    public static bool Find(this IReadOnlyList<Buff> buffs, int id, out Buff buff)
    {
        return (buff = buffs.FirstOrDefault(x => x.Id == id)) != null;
    }

    public static bool Contains(this IReadOnlyList<Buff> buffs, int id)
    {
        return buffs.Contains(new[] { id, });
    }

    public static bool Remove(this List<Buff> buffs, int id)
    {
        var buff = buffs.First(x => x.Id == id);

        if (buff == null)
        {
            return false;
        }

        return buffs.Remove(buff);
    }

    public static bool Contains(this IReadOnlyList<Buff> buffs, NanoLine nanoLine)
    {
        return buffs.Any(b => nanoLine == b.NanoItem.NanoLine);
    }

    public static bool Contains(this IReadOnlyList<Buff> buffs, int[] ids)
    {
        return buffs.Any(b => ids.Contains(b.Id));
    }

    public static bool Find(this IReadOnlyList<Item> items, Identity slot, out Item item)
    {
        return (item = items.FirstOrDefault(x => x.Slot == slot)) != null;
    }

    public static bool FindByIdentity(this IReadOnlyList<Item> items, Identity identity, out Item item)
    {
        return (item = items.FirstOrDefault(x => x.UniqueIdentity == identity)) != null;
    }

    public static bool Find(this IReadOnlyList<Item> items, int id, out Item item)
    {
        return (item = items.FirstOrDefault(x => x.Id == id || x.HighId == id)) != null;
    }

    public static bool FindAtQl(this IReadOnlyList<Item> items, int id, int quality, out Item item)
    {
        return (item = items.FirstOrDefault(x => (x.Id == id || x.HighId == id) && x.Ql == quality)) != null;
    }

    public static bool Find(this IReadOnlyList<Item> items, int lowId, int highId, out Item item)
    {
        return (item = items.FirstOrDefault(x => x.Id == lowId && x.HighId == highId)) != null;
    }

    public static bool Find(this IEnumerable<Container> containers, Identity identity, out Container container)
    {
        return (container = containers.FirstOrDefault(x => x.Identity == identity)) != null;
    }

    public static bool Find(this IEnumerable<Container> containers, Identity slot, out Item item)
    {
        return (item = containers.SelectMany(x => x.Items).FirstOrDefault(x => x.Slot == slot)) != null;
    }

    public static void RemoveItem(this IEnumerable<Container> containers, Item item, out Container owningContainer)
    {
        containers.RemoveItem(item.Slot, out owningContainer);
    }

    public static void RemoveItem(this IEnumerable<Container> containers, Identity slot, out Container owningContainer)
    {
        owningContainer = null;

        foreach (var container in containers)
        {
            foreach (var contItem in container.Items.ToList())
            {
                if (contItem.Slot != slot)
                {
                    continue;
                }

                owningContainer = container;
                container.Items.Remove(contItem);
                return;
            }
        }
    }

    public static Dictionary<Stat, int> ToDict(this GameTuple<Stat, int>[] stats)
    {
        var dictStats = new Dictionary<Stat, int>();

        foreach (var stat in stats)
        {
            dictStats.Add(stat.Value1, stat.Value2);
        }

        return dictStats;
    }

    public static List<Item> FindAll(this IReadOnlyList<Item> items, IEnumerable<int> ids)
    {
        return items.Where(x => ids.Contains(x.Id)).ToList();
    }
}