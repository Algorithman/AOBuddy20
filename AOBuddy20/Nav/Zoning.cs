// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: Zoning.cs
//
// Last modified: 2026-10-01
// Created:       2026-10-01 (ported from AOBuddy10 Zoning.cs, data layer)
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

#nullable disable

using AOBuddy20.Enums;
using AOSharp.Common.GameData;
using Newtonsoft.Json;

namespace AOBuddy20.Nav;

/// <summary>
///     One way out of a playfield. Zone lines are crossed by walking over A-B; the rest are objects at A
///     (== B) that are used. This port carries the DATA layer only (names, zone lines, contact exits) so
///     the walk can keep off exits that do not lead to its goal; AOBuddy10's cross-playfield route
///     planner (ZoneRoute/ZoneHop, Scotty warps, requirement checks, arrival guessing) is not ported yet.
///     ExitKind lives in AOBuddy20.Enums (the skeleton's stub - same members as AOBuddy10's).
/// </summary>
public sealed class ZoneExit
{
    public ExitKind Kind;
    public int FromPf, ToPf;
    public Vector3 A, B;
    public Vector3? Arrival; // where you come out; null = not known
    public Vector3? ArrivalA, ArrivalB; // zone lines: the far side's arrival line, runs opposite to A-B
    public bool ArrivalGuessed; // Arrival is estimated from the exits leading back
    public int Idx, Flags;
    public int[][] Reqs; // [stat, operator, value], postfix; null = none
    public string ReqText;
    public int ObjType, ObjInstance, Template;
    public string Tell, Label; // Scotty only

    public override string ToString()
    {
        return Kind switch
        {
            ExitKind.ZoneLine => $"zone line to {Zoning.Name(ToPf)}",
            ExitKind.Scotty => $"/tell scty {Tell} ({Label}, {Zoning.Name(ToPf)})",
            _ => $"{Kind.ToString().ToLower()} {ObjType}:{ObjInstance} to {Zoning.Name(ToPf)}"
        };
    }
}

/// <summary>
///     The playfield graph's data: every playfield's name and its ways out (zone lines as walked A-B
///     segments; doors, whompas, teleporters and proxies as objects at a spot), from GameData/Zoning.json.
///     THREADING: Load once at startup, before the session starts and before any walk - the loaded maps
///     are immutable afterwards, so ExitsFrom/Name are pure reads from any thread.
/// </summary>
public static class Zoning
{
    private static Dictionary<int, string> _names = new();
    private static Dictionary<int, List<ZoneExit>> _exits = new();

    public static bool Loaded => _names.Count > 0;

    public static string Name(int pf) => _names.TryGetValue(pf, out var n) ? $"{n} ({pf})" : pf.ToString();

    public static IReadOnlyList<ZoneExit> ExitsFrom(int pf) =>
        _exits.TryGetValue(pf, out var l) ? l : Array.Empty<ZoneExit>();

    public static void Load(string dataDir, Action<string> log)
    {
        var names = new Dictionary<int, string>();
        var exits = new Dictionary<int, List<ZoneExit>>();
        var zf = Path.Combine(dataDir, "GameData", "Zoning.json");
        try
        {
            var z = JsonConvert.DeserializeObject<ZoningFile>(File.ReadAllText(zf));
            foreach (var kv in z.playfields)
            {
                names[int.Parse(kv.Key)] = kv.Value.name;
            }

            foreach (var kv in z.playfields)
            {
                int pf = int.Parse(kv.Key);
                var list = new List<ZoneExit>();
                foreach (var l in kv.Value.zoneLines ?? new List<ZlDto>())
                {
                    if (!z.playfields.ContainsKey(l.to.ToString()))
                    {
                        continue;
                    }

                    var e = new ZoneExit
                    { Kind = ExitKind.ZoneLine, FromPf = pf, ToPf = l.to, A = V(l.a), B = V(l.b), Idx = l.idx, Flags = l.flags };
                    if (z.playfields[l.to.ToString()].arrivals.TryGetValue(l.idx.ToString(), out var ar))
                    {
                        e.ArrivalA = V(ar[0]);
                        e.ArrivalB = V(ar[1]);
                        e.Arrival = Mid(e.ArrivalA.Value, e.ArrivalB.Value);
                    }

                    list.Add(e);
                }

                foreach (var t in kv.Value.teleports ?? new List<TpDto>())
                {
                    int to = t.kind == "teleport" && t.to == 0 ? pf : t.to; // teleport to 0: same playfield (lifts)
                    if (!z.playfields.ContainsKey(to.ToString()))
                    {
                        continue; // proxies to instances / garbage args
                    }

                    var e = new ZoneExit
                    {
                        FromPf = pf, ToPf = to, A = V(t.pos), B = V(t.pos), Idx = t.idx, Reqs = t.reqs, ReqText = t.reqText,
                        ObjType = t.id?[0] ?? 0, ObjInstance = t.id?[1] ?? 0, Template = t.template,
                    };
                    switch (t.kind)
                    {
                        case "teleport":
                            e.Kind = ExitKind.Teleport;
                            e.Arrival = V(t.dest);
                            break;
                        case "line":
                            e.Kind = ExitKind.Line;
                            if (z.playfields[to.ToString()].arrivals.TryGetValue(t.idx.ToString(), out var ar))
                            {
                                e.Arrival = Mid(V(ar[0]), V(ar[1]));
                            }

                            break;
                        default:
                            e.Kind = ExitKind.Proxy;
                            break;
                    }

                    list.Add(e);
                }

                exits[pf] = list;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"ZONING: couldn't load {zf}: {ex.Message}");
            return;
        }

        _names = names;
        _exits = exits;
        var count = exits.Values.Sum(l => l.Count);
        log?.Invoke($"ZONING: {names.Count} playfields, {count} exits loaded.");
    }

    /// <summary>Playfield by id, exact name, then name prefix / substring (case-insensitive). 0 when none.</summary>
    public static int FindPlayfield(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return 0;
        }

        s = s.Trim();
        if (int.TryParse(s, out int id))
        {
            return _names.ContainsKey(id) ? id : 0;
        }

        foreach (var m in new Func<string, bool>[]
                 {
                     n => n.Equals(s, StringComparison.OrdinalIgnoreCase),
                     n => n.StartsWith(s, StringComparison.OrdinalIgnoreCase),
                     n => n.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                 })
        {
            var hit = _names.Where(kv => m(kv.Value)).OrderBy(kv => kv.Key).ToList();
            if (hit.Count > 0)
            {
                return hit[0].Key;
            }
        }

        return 0;
    }

    private static Vector3 V(float[] a) => new(a[0], a[1], a[2]);

    private static Vector3 Mid(Vector3 a, Vector3 b) => new(a.X + (b.X - a.X) * 0.5f, a.Y + (b.Y - a.Y) * 0.5f, a.Z + (b.Z - a.Z) * 0.5f);

    // ---- file shapes (filled by Json.NET) ----
#pragma warning disable CS0649
    private sealed class ZoningFile
    {
        public int version;
        public Dictionary<string, PfDto> playfields;
    }

    private sealed class PfDto
    {
        public string name;
        public List<ZlDto> zoneLines;
        public List<TpDto> teleports;
        public Dictionary<string, float[][]> arrivals;
    }

    private sealed class ZlDto
    {
        public int to, idx, flags;
        public float[] a, b;
    }

    private sealed class TpDto
    {
        public string kind;
        public int to, idx, template;
        public float[] pos, dest;
        public int[] id;
        public int[][] reqs;
        public string reqText;
    }
#pragma warning restore CS0649
}