// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: HealItems.cs
//
// Last modified: 2026-10-01
// Created:       2026-10-01 (ported from AOBuddy10 SupportController.cs, the heal-item queries)
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using AOSharp.Clientless;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;

namespace AOBuddy20.Utils;

/// <summary>
///     The heal-item inventory queries (AOBuddy10 SupportController's AllInvItems / MeetsHealReqs):
///     what the bot carries, and which of it the character's own skills can actually use. Resupply
///     picks stock and counts supplies through these, so "what I have" and "what I can use" are
///     decided by the same rules everywhere.
/// </summary>
public static class HealItems
{
    /// <summary>
    ///     All items in the main inventory AND open bags - THE one pooling query, so every inventory
    ///     question (resupply stock, heal items) counts the same set.
    /// </summary>
    public static List<Item> AllInvItems()
    {
        var all = new List<Item>();
        if (Inventory.Items != null)
        {
            all.AddRange(Inventory.Items);
        }

        if (Inventory.Containers != null)
        {
            foreach (var c in Inventory.Containers)
            {
                if (c?.Items != null)
                {
                    all.AddRange(c.Items);
                }
            }
        }

        return all;
    }

    /// <summary>
    ///     The reliable heal-usability check: enforce ONLY the real skill gate - First Aid (123) for
    ///     stims, Treatment (124) for rechargers - against the bot's live skill. No skill criterion =
    ///     usable (the offline no-criteria stim case; ItemBase rule: no criteria = usable). Other
    ///     criteria are left to the server. This is what stops the bot buying the highest-QL item it
    ///     can't actually use.
    ///     The skill gate is First Aid (123) / Treatment (124) terms in the item's criteria (reverse
    ///     Polish). A stim has one: First Aid &gt; N. A recharger has two joined by Or: Treatment &gt; N Or
    ///     First Aid &gt; N (Health and Nano Recharger 291082/291083 - "124 GreaterThan 126, 123
    ///     GreaterThan 126, Or"), so either skill will do. Requiring every term, as an earlier
    ///     version did, turned that Or into an And.
    /// </summary>
    public static bool MeetsHealReqs(Item it, LocalPlayer me)
    {
        try
        {
            if (me == null || it == null || it.Criteria == null)
            {
                return true;
            }

            if (!it.Criteria.TryGetValue(ItemActionInfo.UseCriteria, out var crits) || crits == null || crits.Count == 0)
            {
                return true;
            }

            var skill = new List<bool?>(); // one per term: met / not met / skill unreadable
            var skillOrs = 0;
            for (var i = 0; i < crits.Count; i++)
            {
                var c = crits[i];
                if (c.Param1 == 123 || c.Param1 == 124)
                {
                    // A skill we cannot READ is not a skill of zero. Defaulting the missing stat to 0
                    // failed every requirement and quietly shrank the usable pool (AOBuddy10: the bot
                    // reported "8 stims left" while carrying twenty-four it could use). Unknown means
                    // don't refuse; if the item really is out of reach the server refuses the Use.
                    if (!me.TryGetStat((Stat)c.Param1, out var have))
                    {
                        skill.Add(null);
                        continue;
                    }

                    // GreaterThan in the item data means greater: Health and Nano Stim QL30 is
                    // First Aid > 227, QL20 > 152 (item data 291043/291044), so equal is not enough.
                    skill.Add(c.Operator == UseCriteriaOperator.GreaterThan ? have > c.Param2
                        : c.Operator == UseCriteriaOperator.LessThan ? have < c.Param2
                        : c.Operator == UseCriteriaOperator.EqualTo ? have == c.Param2
                        : have >= c.Param2);
                }

                // An Or right after two skill terms joins them.
                else if (c.Operator == UseCriteriaOperator.Or && i >= 2
                         && (crits[i - 1].Param1 == 123 || crits[i - 1].Param1 == 124)
                         && (crits[i - 2].Param1 == 123 || crits[i - 2].Param1 == 124))
                {
                    skillOrs++;
                }
            }

            if (skill.Count == 0 || skill.Any(x => x == null))
            {
                return true;
            }

            return skillOrs > 0 ? skill.Any(x => x == true) : skill.All(x => x == true);
        }
        catch
        {
            return true; // criteria unreadable - don't hard-refuse
        }
    }
}