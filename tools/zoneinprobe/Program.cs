using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AOBuddy20.Nav;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.Serialization;

// zoneinprobe: offline checks against saved packets and the nav data.
//   zoneinprobe <missionsDir>                    - parse every zonein-*.bin through the real serializer,
//                                                  print the typed fields and the building compose.
//   zoneinprobe grid <pluginDir> <pf> <x> <y> <z> <goalX> <goalZ>
//                                                - build the pf's walk grid and dump it around (x,z),
//                                                  then try FindPath to the goal.
if (args.Length >= 2 && args[0] == "grid")
{
    var dir = args[1];
    var pf = int.Parse(args[2], CultureInfo.InvariantCulture);
    var p = new Vector3(float.Parse(args[3], CultureInfo.InvariantCulture),
        float.Parse(args[4], CultureInfo.InvariantCulture), float.Parse(args[5], CultureInfo.InvariantCulture));
    var g = new Vector3(float.Parse(args[6], CultureInfo.InvariantCulture), p.Y,
        float.Parse(args[7], CultureInfo.InvariantCulture));

    Zoning.Load(dir, _ => { });
    var nav = AOBuddyNav.Load(dir, pf);
    Console.WriteLine($"nav: {nav.Kind}, name {nav.Name}");
    if (nav.Ground != null)
    {
        Console.WriteLine($"ground: {nav.Ground.SamplesX}x{nav.Ground.SamplesZ} samples, cell {nav.Ground.Cell}, " +
                          $"extent {(nav.Ground.SamplesX - 1) * nav.Ground.Cell:0} x {(nav.Ground.SamplesZ - 1) * nav.Ground.Cell:0} m");
    }

    var nearExits = Zoning.ExitsFrom(pf).Where(e => Math.Abs(e.A.X - p.X) < 25 && Math.Abs(e.A.Z - p.Z) < 25).ToList();
    Console.WriteLine($"{Zoning.ExitsFrom(pf).Count} exits, near the landing: " +
                      string.Join("; ", nearExits.Select(e => $"{e.Kind} at ({e.A.X:0.0},{e.A.Z:0.0}) y {e.A.Y:0.0} to {e.ToPf} back={e.Back}")));

    var log = new Action<string>(s => Console.WriteLine("  " + s));
    var grid = (IWalkGrid)OverlandGrid.Build(dir, pf, nav, log) ?? FloorGrid.Build(dir, pf, nav, log);
    if (grid == null)
    {
        Console.WriteLine("no grid could be built");
        return;
    }

    Console.WriteLine($"grid: {grid.GetType().Name}, OpenAt(landing) = {grid.OpenAt(p)}");
    foreach (var e in nearExits)
        Console.WriteLine($"  OpenAt(exit {e.Kind} {e.A.X:0.0},{e.A.Z:0.0}) = {grid.OpenAt(e.A)}");
    for (float dy = -3; dy <= 3; dy += 1)
    {
        var row = "";
        for (float dx = -3; dx <= 3; dx += 1)
            row += grid.OpenAt(new Vector3(2210.8f + dx, 22.7f + dy, 1567.1f)) ? "O" : ".";
        Console.WriteLine($"  door y={22.7f + dy:0}  {row}   (x 2207.8..2213.8, z fixed 1567.1)");
    }
    for (float dz = -3; dz <= 3; dz += 1)
    {
        var row = "";
        for (float dx = -3; dx <= 3; dx += 1)
            row += grid.OpenAt(new Vector3(2210.8f + dx, 22.7f, 1567.1f + dz)) ? "O" : ".";
        Console.WriteLine($"  door z+{dz,2}  {row}");
    }
    for (float dz = -10; dz <= 10; dz += 1)
    {
        var row = "";
        for (float dx = -10; dx <= 10; dx += 1)
        {
            var q = new Vector3(p.X + dx, p.Y, p.Z + dz);
            row += grid.OpenAt(q) ? "O" : ".";
        }

        Console.WriteLine($"z{p.Z + dz,7:0}  {row}");
    }

    Console.WriteLine($"ground at goal ({g.X:0.0},{g.Z:0.0}) = " +
                      $"{(nav.Ground != null ? nav.Ground.HeightAt(g.X, g.Z).ToString("0.00") : "n/a")}, OpenAt(goal) = {grid.OpenAt(g)}");
    var path = grid.FindPath(p, g, null, 8f, 3f, out var why);
    Console.WriteLine($"FindPath -> {(path == null ? $"NULL ({why})" : $"{path.Count} points")}");
    return;
}

// default mode: zone-in packets
var s = new MessageSerializer();
foreach (var f in Directory.GetFiles(args[0], "zonein-*.bin"))
{
    try
    {
        var msg = s.Deserialize(File.ReadAllBytes(f));
        if (msg?.Body is PlayfieldAnarchyFMessage p)
        {
            Console.WriteLine($"{Path.GetFileName(f)}: v={p.Version} pf1={p.PlayfieldId1.Type}:{p.PlayfieldId1.Instance} " +
                              $"ret=0x{p.ProxyReturn:X}->{p.ReturnPlayfield} dynels={(p.Dynels?.Length.ToString() ?? "null")}");
            if (msg.RawPacket != null)
            {
                var m = AOBuddyNav.DecodeZoneIn(msg.RawPacket);
                Console.WriteLine($"    RawPacket {msg.RawPacket.Length}B -> layout: " +
                                  (m == null ? "none (not a mission)" :
                                   $"pool {m.TemplatePlayfield}, building {m.Instance}, {m.Rooms.Count} room(s), landing ({m.LandX:0.0},{m.LandZ:0.0})"));
            }
        }
        else
        {
            Console.WriteLine($"{Path.GetFileName(f)}: body={msg?.Body?.GetType().Name ?? "null"}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{Path.GetFileName(f)}: THREW {ex.GetType().Name} {ex.Message}");
    }
}
