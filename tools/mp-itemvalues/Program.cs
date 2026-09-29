// Usage: dotnet run -- <items.ocp> <ItemValues.bin>
// Version 2:
//   "AOBV", 2, count, per item template carrying Value (stat 74): int id, int ql, int value — sorted by id
//   count, per template carrying SellModifier (427): int id, int sellModifier, int buyModifier (426, 0 if absent)
//          — the shop terminals' own templates; a VendingMachine names its template in StaticInstance.
using System; using System.IO; using System.Linq; using System.Text;
var all = OmniCell.Core.Content.OmniCellContentPack.ReadItems(args[0]).Where(i => i.Stats != null).OrderBy(i => i.ID).ToList();
var valued = all.Where(i => i.Stats.ContainsKey(74)).ToList();
var shops = all.Where(i => i.Stats.ContainsKey(427)).ToList();
using (var w = new BinaryWriter(File.Create(args[1])))
{
    w.Write(Encoding.ASCII.GetBytes("AOBV")); w.Write(2);
    w.Write(valued.Count);
    foreach (var i in valued) { w.Write(i.ID); w.Write(i.Quality); w.Write(i.Stats[74]); }
    w.Write(shops.Count);
    foreach (var i in shops) { w.Write(i.ID); w.Write(i.Stats[427]); w.Write(i.Stats.TryGetValue(426, out int b) ? b : 0); }
}
Console.WriteLine($"wrote {valued.Count} item values and {shops.Count} shop modifiers to {args[1]}");
