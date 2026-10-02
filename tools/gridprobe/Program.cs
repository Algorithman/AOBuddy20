using System;
using System.Collections.Generic;
using System.Linq;
using AOBuddy20.Nav;
using AOSharp.Common.GameData;

// Nav-data probe for a dungeon playfield's walk grid and the monitor's plan rendering:
//   gridprobe <pluginDir> <pf>                          - FindPath across every rooms.json door connection
//   gridprobe <pluginDir> <pf> <cx> <cz> [span]         - OpenAt lattice around a world point
//   gridprobe <pluginDir> <pf> <x1> <z1> <x2> <z2> [any]- one FindPath with its points
//   gridprobe <pluginDir> <pf> plan <cx> <cz> [span]    - the monitor's floor-0 plan as ASCII
//                                                         ('.' room floor, 't' threshold, '#' wall, ' ' void)
var pluginDir = args[0];
var pf = int.Parse(args[1]);
var nav = AOBuddyNav.Load(pluginDir, pf);
if (nav?.Dungeon == null) { Console.WriteLine("no dungeon data"); return 1; }
var d = nav.Dungeon;
var grid = FloorGrid.Build(pluginDir, pf, nav, s => Console.WriteLine("  [grid] " + s));
if (grid == null) { Console.WriteLine("no floor grid built"); return 1; }
float y = d.Rooms.Count > 0 ? d.Rooms[0].Pos[1] : 0;

if (args.Length > 2 && args[2] == "parity")
{
    // placement test: for each room, which parity shift (0/1 m per axis) puts its tiled cells
    // on the real ground-level collision floor? Decisive for the stored-centre-vs-floor-centre
    // question (the AOBuddy10 mission offset, for static rooms).
    if (nav.Collision == null) { Console.WriteLine("no collision.bin"); return 1; }
    var ground = new HashSet<(int, int)>();
    foreach (var ch in nav.Collision.Chunks)
        for (int i = 0; i + 2 < ch.Verts.Length; i += 3)
            if (ch.Verts[i + 1] > 4.5f && ch.Verts[i + 1] < 5.5f)
                ground.Add(((int)Math.Round(ch.Verts[i] / d.Cell), (int)Math.Round(ch.Verts[i + 2] / d.Cell)));
    Console.WriteLine($"ground collision cells (2 m lattice): {ground.Count}");
    foreach (var rm in d.Rooms)
    {
        int x1 = rm.Rect[0], z1 = rm.Rect[1];
        double mx = (rm.Rect[0] + rm.Rect[2] + 1) / 2.0, mz = (rm.Rect[1] + rm.Rect[3] + 1) / 2.0;
        int W = rm.Rect[2] - rm.Rect[0] + 1, H = rm.Rect[3] - rm.Rect[1] + 1;
        var turns = ((-rm.Rot) % 4 + 4) % 4;
        Console.WriteLine($"  room {rm.Index} '{rm.Name}' ({W}x{H}, rot {rm.Rot}):");
        for (int sz = 0; sz <= 1; sz++)
            for (int sx = 0; sx <= 1; sx++)
            {
                int hit = 0, miss = 0;
                for (int r = 0; r < rm.Tile.Length; r++)
                    for (int c = 0; c < rm.Tile[r].Length; c++)
                    {
                        if (rm.Tile[r][c] == 0) continue;
                        double tx = (x1 + c - mx) * d.Cell + sx, tz = (z1 + r - mz) * d.Cell + sz;
                        for (var t2 = 0; t2 < turns; t2++) { var t3 = tx; tx = -tz; tz = t3; }
                        var k = ((int)Math.Round((rm.Pos[0] + tx) / d.Cell), (int)Math.Round((rm.Pos[2] + tz) / d.Cell));
                        if (ground.Contains(k)) hit++; else miss++;
                    }
                var parity = (sx == (W % 2 == 0 ? 1 : 0) && sz == (H % 2 == 0 ? 1 : 0)) ? "  <-- parity (floor centre)" : "";
                Console.WriteLine($"    shift ({sx},{sz}): {hit,3} on floor, {miss,3} off  ({hit * 100 / Math.Max(1, hit + miss)} %){parity}");
            }
    }
    return 0;
}

if (args.Length > 2 && args[2] == "plan")
{
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    var cx = float.Parse(args[3], inv); var cz = float.Parse(args[4], inv);
    var span = args.Length > 5 ? float.Parse(args[5], inv) : 4f;
    var render = new AOBuddyMonitor.MapRender(pluginDir);
    AOBuddyMonitor.MapRender.MissionPlan plan = null;
    for (var i = 0; i < 60 && plan == null; i++) { plan = render.GetDungeon(pf); System.Threading.Thread.Sleep(200); }
    if (plan == null) { Console.WriteLine("no plan built"); return 1; }
    Console.WriteLine($"plan: {plan.Name}, {plan.W}x{plan.H} px, cell {plan.Cell} m, px/cell {plan.PxPerCell}, floors [{string.Join(",", plan.Floors)}]");
    for (float z = cz + span; z >= cz - span - 1e-3f; z -= 0.5f)
    {
        var row = "";
        for (float x = cx - span; x <= cx + span + 1e-3f; x += 0.5f)
        {
            int px = (int)((x - plan.MinX) / plan.Cell * plan.PxPerCell);
            int py = (int)((z - plan.MinZ) / plan.Cell * plan.PxPerCell);
            if (px < 0 || py < 0 || px >= plan.W || py >= plan.H) { row += '?'; continue; }
            int i = (py * plan.W + px) * 4;
            var bgra = plan.FloorBgra[plan.Floors[0]];
            row += bgra[i + 3] == 0 ? ' '
                 : bgra[i + 2] >= 190 && bgra[i] <= 80 ? 'D'
                 : bgra[i + 2] >= 140 ? '#'
                 : bgra[i + 2] is >= 65 and <= 75 ? 't'
                 : '.';
        }
        Console.WriteLine($"z={z,7:0.0}  {row}");
    }
    Console.WriteLine($"           x {cx - span:0.0} -> {cx + span:0.0} (centre {cx})");
    return 0;
}

if (args.Length > 2 && args.Length < 5 && args[2] != "parity")
{
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    var cx = float.Parse(args[2], inv); var cz = float.Parse(args[3], inv);
    var span = args.Length > 4 ? float.Parse(args[4], inv) : 4f;
    for (float z = cz + span; z >= cz - span - 1e-3f; z -= 0.5f)
    {
        var row = "";
        for (float x = cx - span; x <= cx + span + 1e-3f; x += 0.5f)
            row += grid.OpenAt(new Vector3(x, y, z)) ? "." : " ";
        Console.WriteLine($"z={z,7:0.0}  {row}");
    }
    Console.WriteLine($"           x {cx - span:0.0} -> {cx + span:0.0} (centre {cx})");
    return 0;
}

if (args.Length >= 6)
{
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    var a = new Vector3(float.Parse(args[2], inv), y, float.Parse(args[3], inv));
    var b = new Vector3(float.Parse(args[4], inv), args.Length >= 7 && args[6] == "any" ? float.NaN : y,
        float.Parse(args[5], inv));
    var one = grid.FindPath(a, b, null, 3f, 2f, out var whyOne);
    Console.WriteLine($"({a.X:0},{a.Z:0}) -> ({b.X:0},{b.Z:0}): " +
        (one != null ? "PATH " + string.Join(" -> ", one.Select(p => $"({p.X:0.0},{p.Z:0.0})")) : "NO PATH - " + whyOne));
    return 0;
}

Vector3 Centre(NavDungeon.Room rm)
{
    if (rm?.Tile == null || rm.Rect == null || rm.Pos == null) return default;
    int a1 = rm.Rect[0], b1 = rm.Rect[1];
    double amx = (rm.Rect[0] + rm.Rect[2] + 1) / 2.0, amz = (rm.Rect[1] + rm.Rect[3] + 1) / 2.0;
    var tn = ((-rm.Rot) % 4 + 4) % 4;
    double sx = 0, sz = 0; int n = 0;
    for (int r = 0; r < rm.Tile.Length; r++)
    for (int c = 0; c < rm.Tile[r].Length; c++)
    {
        if (rm.Tile[r][c] == 0) continue;
        double tx = (a1 + c - amx) * d.Cell, tz = (b1 + r - amz) * d.Cell;
        for (int i = 0; i < tn; i++) (tx, tz) = (-tz, tx);
        sx += rm.Pos[0] + tx; sz += rm.Pos[2] + tz; n++;
    }
    return n == 0 ? default : new Vector3((float)(sx / n), rm.Pos[1], (float)(sz / n));
}

int ok = 0, fail = 0;
foreach (var rm in d.Rooms)
{
    if (rm?.Doors == null) continue;
    foreach (var door in rm.Doors)
    {
        if (door == null || door.Length < 2 || door[0] == 65535 || door[0] >= d.Rooms.Count) continue;
        var a = Centre(rm); var b = Centre(d.Rooms[door[0]]);
        if (a == default || b == default) continue;
        var pts = grid.FindPath(a, b, null, 3f, 2f, out var why);
        if (pts != null) ok++; else fail++;
        Console.WriteLine($"  room {rm.Index} ({a.X:0.0},{a.Z:0.0}) -> room {door[0]} ({b.X:0.0},{b.Z:0.0}): " +
            (pts != null ? $"PATH ({pts.Count} pts)" : "NO PATH - " + why));
    }
}
Console.WriteLine($"  {ok} of {ok + fail} door connections pathable");
return 0;