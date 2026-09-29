using AOSharp.Common.GameData;

namespace AOSharp.Clientless;

public class ItemBase
{
    public Dictionary<ItemActionInfo, List<RequirementCriterion>> Criteria = new Dictionary<ItemActionInfo, List<RequirementCriterion>>();
    public int Icon;
    public int Id;
    public Dictionary<SpellListType, Dictionary<Stat, int>> Modifiers = new Dictionary<SpellListType, Dictionary<Stat, int>>();
    public string Name;
    public int Ql;

    public ItemBase(int id, int ql)
    {
        Id = id;
        Ql = ql;
    }

    public Dictionary<Stat, int> UseModifiers => Modifiers[SpellListType.Use];
    public Dictionary<Stat, int> WearModifiers => Modifiers[SpellListType.Wear];

    /// <summary>
    ///     True if the local player (optionally against <paramref name="target" />) currently
    ///     meets this item/nano's use requirements. For a NanoItem this answers "can I cast it?"
    ///     — profession, level and skill gates are all evaluated from live stats.
    /// </summary>
    public bool MeetsUseReqs(SimpleChar target = null, bool ignoreTargetReqs = false)
    {
        return MeetsUseReqs(target, ignoreTargetReqs, false);
    }

    /// <param name="ignorePetLimit">Treat the summon "pet slot is free" gate (TestNumPets) as met.</param>
    public bool MeetsUseReqs(SimpleChar target, bool ignoreTargetReqs = false, bool ignorePetLimit = false)
    {
        if (!Criteria.TryGetValue(ItemActionInfo.UseCriteria, out var useCriteria))
        {
            return true; // no gate = usable
        }

        return new ReqChecker(useCriteria).MeetsReqs(target, ignoreTargetReqs, ignorePetLimit);
    }
}