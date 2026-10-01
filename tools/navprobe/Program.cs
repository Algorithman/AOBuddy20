using AOBuddy20.Nav;
using AOSharp.Common.GameData;

var baseDir = @"F:\testcellao\AOBuddy20\AOBuddy20\Build";
var nav = AOBuddyNav.Load(baseDir, 566);
var grid = OverlandGrid.Build(baseDir, 566, nav, s => Console.WriteLine(s));

// the owner's ramp line and walkway: deck floors must survive the suppression
Console.WriteLine("=== cells along the ramp (395.8, 252.7->245.3) and walkway");
foreach (var (x, z, what) in new[]
{
    (395f, 252f, "ramp low end"), (395f, 249f, "ramp mid"), (395f, 246f, "ramp high"),
    (392f, 245f, "walkway beside ramp"), (396f, 242f, "ramp top landing"),
})
{
    Console.WriteLine("  " + grid.CellInfo(x, z) + $"   <- {what}");
}

return 0;
