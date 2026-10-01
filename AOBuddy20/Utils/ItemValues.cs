// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: ItemValues.cs
//
// Last modified: 2026-10-01
// Created:       2026-10-01 (ported from AOBuddy10 ItemValues.cs, resupply's price tables)
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using System.Text;

namespace AOBuddy20.Utils;

/// <summary>
///     Item Value (stat 74) per item template, each shop terminal template's Sell/BuyModifier
///     (427/426), and the NODROP templates (item Flags bit 26 - never sellable), none of which the
///     SDK's item pack carries. Extracted once from OmniCell's items.ocp (AOBuddy10 tools
///     mp-itemvalues / eng-gear-extractor):
///       GameData/ItemValues.bin  "AOBV", 2, count, per template int id, int ql, int value
///                                count, per terminal template int id, int sellModifier, int buyModifier
///       GameData/ItemNoDrop.bin  "AOND", 1, count, ids
///
///     Between its low and high template an item's Value follows the SQUARE of how far its QL is
///     along the range - unlike its requirements, which are linear.
///
///     What a terminal charges (ShopPrice) is Value x SellModifier/100 x the buyer's Computer
///     Literacy discount, 1% per full 40 CL. Checked against eight Health and Nano Stim prices at
///     ICC Fair Trade, QL30-200 over the Basic (SellModifier 105) and Advanced (505) pharmacy: the
///     buyer's CL put the discount at 4%, and every price matched to within 2 credits. The
///     terminal's own spawn packet does NOT carry the modifier - only its template (StaticInstance)
///     does.
/// </summary>
public static class ItemValues
{
    private static Dictionary<int, (int Ql, int Value)>? _values;
    private static Dictionary<int, (int Sell, int Buy)>? _shops;
    private static HashSet<int>? _noDrop;

    /// <summary>Loaded once at startup, before the session starts (Program, next to Zoning.Load).</summary>
    public static bool Loaded => _values != null;

    public static void Load(string baseDir, Action<string> log)
    {
        _values = new Dictionary<int, (int, int)>();
        _shops = new Dictionary<int, (int, int)>();
        var file = Path.Combine(baseDir, "GameData", "ItemValues.bin");
        try
        {
            using var r = new BinaryReader(File.OpenRead(file));
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "AOBV" || r.ReadInt32() != 2)
            {
                log($"ITEMVALUES: {file} is not a version 2 value table.");
                return;
            }

            var count = r.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var id = r.ReadInt32();
                var ql = r.ReadInt32();
                var value = r.ReadInt32();
                _values[id] = (ql, value);
            }

            count = r.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var id = r.ReadInt32();
                var sell = r.ReadInt32();
                var buy = r.ReadInt32();
                _shops[id] = (sell, buy);
            }

            log($"ITEMVALUES: {_values.Count} item values, {_shops.Count} shop terminal modifiers loaded.");
        }
        catch (Exception ex)
        {
            log($"ITEMVALUES: couldn't load {file}: {ex.Message}");
        }

        _noDrop = ReadIds(Path.Combine(baseDir, "GameData", "ItemNoDrop.bin"), "AOND", log, "NODROP");
    }

    /// <summary>NODROP templates (Flags bit 26): can never be dropped, traded or sold.</summary>
    public static bool IsNoDrop(int lowId, int highId)
    {
        return _noDrop != null && (_noDrop.Contains(lowId) || _noDrop.Contains(highId));
    }

    private static HashSet<int>? ReadIds(string file, string magic, Action<string> log, string what)
    {
        try
        {
            using var r = new BinaryReader(File.OpenRead(file));
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != magic || r.ReadInt32() != 1)
            {
                log($"ITEMVALUES: {file} is not a version 1 {what} table.");
                return null;
            }

            var n = r.ReadInt32();
            var set = new HashSet<int>(n);
            for (var i = 0; i < n; i++)
            {
                set.Add(r.ReadInt32());
            }

            log($"ITEMVALUES: {set.Count} {what} templates loaded.");
            return set;
        }
        catch (Exception ex)
        {
            log($"ITEMVALUES: couldn't load {file}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The Value of the item at `ql` between its low and high template; false when unknown.</summary>
    public static bool TryGet(int lowId, int highId, int ql, out int value)
    {
        value = 0;
        if (_values == null || !_values.TryGetValue(lowId, out var low))
        {
            return false;
        }

        if (highId == lowId || !_values.TryGetValue(highId, out var high) || high.Ql == low.Ql)
        {
            value = low.Value;
            return true;
        }

        var t = (double)(ql - low.Ql) / (high.Ql - low.Ql);
        value = (int)Math.Round(low.Value + (high.Value - low.Value) * t * t);
        return true;
    }

    /// <summary>A terminal template's markups, in percent. The terminal names its template in StaticInstance.</summary>
    public static bool TryGetShopModifiers(int terminalTemplate, out int sellModifier, out int buyModifier)
    {
        sellModifier = buyModifier = 0;
        if (_shops == null || !_shops.TryGetValue(terminalTemplate, out var m))
        {
            return false;
        }

        sellModifier = m.Sell;
        buyModifier = m.Buy;
        return true;
    }

    /// <summary>Computer Literacy knocks 1% off a shop's price per full 40 points.</summary>
    public static double ClDiscount(int computerLiteracy)
    {
        return 1.0 - computerLiteracy / 40 / 100.0;
    }

    /// <summary>What a terminal charges this buyer for one item of this Value.</summary>
    public static int ShopPrice(int value, int sellModifier, int computerLiteracy)
    {
        return (int)Math.Round(value * (sellModifier / 100.0) * ClDiscount(computerLiteracy));
    }
}