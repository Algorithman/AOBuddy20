using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AOBuddy20.Nav;

// GRIDWARMUP — build every zone's walk grid ahead of the bot's first run (owner, 2026-09-25: "the bot
// should create all navdata on the first run"). Walks the bot's GameData/Nav folders, skips zones
// whose GridCache file is already valid, and builds + saves the rest exactly the way the bot would when
// entering the zone — so a freshly set-up bot starts with every grid on disk and never waits out the
// 0.6-3.8 s build at a zone border. Re-run it any time; it only pays for zones that changed (a bake-rule
// change bumps GridCache.CodeVersion, which invalidates every cache at once).
//
//   gridwarmup [botDir] [--only <pf> <pf> ...]
//
// botDir is the folder the bot runs from (GameData under it), default: the repo's Build\.

internal static class Program
{
    private static int Main(string[] args)
    {
        string botDir = null;
        var only = new HashSet<int>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--only")
            {
                for (int j = i + 1; j < args.Length && int.TryParse(args[j], out int p); j++) only.Add(p);
                break;
            }
            botDir = args[i];
        }
        if (botDir == null)
        {
            botDir = FindBuildDir(AppContext.BaseDirectory);
        }

        string navRoot = Path.Combine(botDir, "GameData", "Nav");
        if (!Directory.Exists(navRoot))
        {
            Console.Error.WriteLine($"No GameData/Nav under {botDir} — pass the bot's folder as the argument.");
            return 1;
        }

        try { Zoning.Load(botDir, m => { }); } catch { }   // zone names, best effort

        List<int> zones = Directory.GetDirectories(navRoot)
            .Select(Path.GetFileName)
            .Where(n => int.TryParse(n, out _))
            .Select(int.Parse)
            .OrderBy(p => p)
            .ToList();
        if (only.Count > 0) zones = zones.Where(only.Contains).ToList();

        int built = 0, kept = 0, none = 0, noData = 0;
        var all = Stopwatch.StartNew();
        foreach (int pf in zones)
        {
            var nav = AOBuddyNav.Load(botDir, pf);
            if (nav == null) { noData++; Console.WriteLine($"pf {pf,-5} {Name(pf)}: no nav data"); continue; }

            if (GridCache.TryLoad(botDir, pf, nav, null) != null)
            {
                kept++;
                Console.WriteLine($"pf {pf,-5} {Name(pf)}: cache already valid");
                continue;
            }

            var sw = Stopwatch.StartNew();
            IWalkGrid grid = (IWalkGrid)OverlandGrid.Build(botDir, pf, nav, null) ?? FloorGrid.Build(botDir, pf, nav, null);
            if (grid == null) { none++; Console.WriteLine($"pf {pf,-5} {Name(pf)}: no walk grid from this data"); continue; }
            GridCache.Save(botDir, pf, grid, null);
            built++;
            Console.WriteLine($"pf {pf,-5} {Name(pf)}: built in {sw.ElapsedMilliseconds} ms");
        }

        Console.WriteLine();
        Console.WriteLine($"{zones.Count} zone(s): {built} built, {kept} already cached, {none} with no grid, {noData} with no data — {all.Elapsed.TotalMinutes:0.0} min.");
        return 0;
    }

    // The repo root is the nearest ancestor holding AOBuddy20.sln; the bot runs from its Build\.
    private static string FindBuildDir(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Build");
            if (File.Exists(Path.Combine(dir.FullName, "AOBuddy20.sln")) && Directory.Exists(Path.Combine(candidate, "GameData", "Nav")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Build"));
    }

    private static string Name(int pf)
    {
        string n = Zoning.Name(pf);
        return string.IsNullOrEmpty(n) ? "?" : n;
    }
}
