using AOBuddy20.Nav;
using AOSharp.Common.GameData;

var baseDir = @"F:\testcellao\AOBuddy20\AOBuddy20\Build";
var nav = AOBuddyNav.Load(baseDir, 1187);
if (nav == null) { Console.WriteLine("no nav data for 1187"); return 1; }
var grid = (IWalkGrid)FloorGrid.Build(baseDir, 1187, nav, s => Console.WriteLine(s));
if (grid == null) { Console.WriteLine("no grid for 1187"); return 1; }

Console.WriteLine("=== line door -> vendor (205,121.8) -> (211,129):");
var a = new Vector3(205f, 5f, 121.8f);
var b = new Vector3(211f, 5f, 129f);
var len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
var n = (int)(len / 0.5f);
var bad = 0;
for (var s = 0; s <= n; s++)
{
    var t = s / (float)n;
    var x = a.X + (b.X - a.X) * t;
    var z = a.Z + (b.Z - a.Z) * t;
    var ok = grid.OpenAt(new Vector3(x, 5f, z));
    if (!ok)
    {
        bad++;
    }

    Console.WriteLine($"  ({x:0.0},{z:0.0)} {(ok ? "open" : "BLOCKED")}".Replace("(0,0)", ")"));
}

Console.WriteLine(bad == 0 ? "  LINE CLEAN" : $"  {bad} blocked samples");

Console.WriteLine("=== map x 198..222, z 116..134 (2 m; # blocked, . open, D door, V vendor):");
Console.Write("     ");
for (var x = 198f; x <= 222f; x += 2f)
{
    Console.Write($"{(int)x % 10}");
}

Console.WriteLine();
for (var z = 134f; z >= 116f; z -= 2f)
{
    var row = "";
    for (var x = 198f; x <= 222f; x += 2f)
    {
        var c = grid.OpenAt(new Vector3(x, 5f, z)) ? '.' : '#';
        if (Math.Abs(x - 205) < 1.5f && Math.Abs(z - 120) < 1.5f) c = 'D';
        if (Math.Abs(x - 211) < 1.5f && Math.Abs(z - 129) < 1.5f) c = 'V';
        row += c;
    }

    Console.WriteLine($"z={z:000} {row}");
}

var route = grid.FindPath(a, b, new HashSet<int>(), 8f, 3f, out var why);
Console.WriteLine(route == null ? $"FindPath: NONE ({why})" : $"FindPath: {route.Count} points");
foreach (var p in route ?? new List<Vector3>())
{
    Console.WriteLine($"  ({p.X:0.0},{p.Y:0.0},{p.Z:0.0})");
}

return 0;