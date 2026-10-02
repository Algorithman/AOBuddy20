// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: LootBagStore.cs
//
// Last modified: 2026-10-02
// Created:       2026-10-02
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using AOBuddy20.Configuration;
using AOBuddy20.Utils;
using AOSharp.Common.GameData;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog.Events;

namespace AOBuddy20.Storage;

/// <summary>
///     THE LOOT BAG BOOK: which of the character's bags are designated loot bags. Pure STORAGE -
///     it knows nothing about opening bags, looting or commands; the API that designates and
///     undesignates (and whatever consumes the designations) comes later and calls this.
///     Bags are identified by their IDENTITY (the container's wire identity, the same identity
///     the bag item carries as its UniqueIdentity) - NEVER by name: bag renames are client-side
///     and invisible on the wire (owner-proven), so the name travels only as an informational
///     note, never as a key. The bag's template id is stored too, so the later API can tell a
///     designation made for one bag from another bag that happens to sit in the same place.
///     Designations are per CHARACTER and persist across restarts in lootbags-&lt;character&gt;.json
///     (BaseDirectory, through JsonStore's atomic writes).
///     All of this runs on the update thread (the house thread rule) - no locks.
/// </summary>
[MinLogLevel(LogEventLevel.Debug)]
public sealed class LootBagStore
{
    private readonly ILogger<LootBagStore> _logger;
    private readonly string _file;
    private readonly Dictionary<Identity, LootBag> _bags = new();

    public LootBagStore(AccountInfo config, ILogger<LootBagStore> logger)
    {
        _logger = logger;
        _file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"lootbags-{config.Character}.json");
        foreach (var bag in JsonStore.Load<List<LootBag>>(_file, s => _logger.LogInformation(s)) ?? new List<LootBag>())
        {
            _bags[bag.ToIdentity()] = bag;
        }

        _logger.LogInformation($"Loot bag store: {_bags.Count} designated ({Path.GetFileName(_file)}).");
    }

    /// <summary>The designated bags (read-only view; do not mutate - use Designate/Undesignate).</summary>
    public IReadOnlyCollection<LootBag> Bags => _bags.Values;

    /// <summary>
    ///     Designate a bag as a loot bag. Idempotent: an already-designated bag keeps its original
    ///     date, but a supplied template/note refreshes. False was only possible before - this
    ///     always succeeds (the store accepts what it is told; whether the identity is really a
    ///     bag in the packs is the caller API's business).
    /// </summary>
    public bool Designate(Identity bag, int templateId = 0, string note = "")
    {
        if (_bags.TryGetValue(bag, out var existing))
        {
            if (templateId != 0)
            {
                existing.Template = templateId;
            }

            if (!string.IsNullOrEmpty(note))
            {
                existing.Note = note;
            }

            _logger.LogInformation($"LOOTBAG: {Describe(existing)} already designated - refreshed.");
        }
        else
        {
            existing = new LootBag
            {
                Type = (int)bag.Type,
                Instance = bag.Instance,
                Template = templateId,
                Note = note ?? "",
                At = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };
            _bags[bag] = existing;
            _logger.LogInformation($"LOOTBAG: designated {Describe(existing)}.");
        }

        Save();
        return true;
    }

    /// <summary>Undesignate a bag. True when it was designated, false when it was not.</summary>
    public bool Undesignate(Identity bag)
    {
        if (!_bags.TryGetValue(bag, out var existing))
        {
            _logger.LogInformation($"LOOTBAG: {bag.Type}/{bag.Instance} was not designated.");
            return false;
        }

        _bags.Remove(bag);
        _logger.LogInformation($"LOOTBAG: undesignated {Describe(existing)}.");
        Save();
        return true;
    }

    /// <summary>Is this bag designated as a loot bag?</summary>
    public bool IsLootBag(Identity bag)
    {
        return _bags.ContainsKey(bag);
    }

    private void Save()
    {
        JsonStore.Save(_file, JsonConvert.SerializeObject(_bags.Values.ToList(), Formatting.Indented),
            s => _logger.LogInformation(s));
    }

    private static string Describe(LootBag bag)
    {
        return $"bag {bag.Type}/{bag.Instance}{(bag.Template != 0 ? $" (template {bag.Template})" : "")}" +
               $"{(string.IsNullOrEmpty(bag.Note) ? "" : $" '{bag.Note}'")}";
    }

    /// <summary>One designated bag, as it travels to and from lootbags-&lt;character&gt;.json.</summary>
    public sealed class LootBag
    {
        public int Type; // Identity.Type as int (the JSON has no enum strings)
        public int Instance;
        public int Template; // the bag item's template id, 0 when unknown - informational, never a key
        public string Note = ""; // the bag's name at designation time - informational ONLY (renames
        // are client-side and invisible on the wire; nothing matches on this)
        public string At = ""; // designated at

        [JsonIgnore]
        public Identity Key => new((IdentityType)Type, Instance);

        public Identity ToIdentity()
        {
            return new Identity((IdentityType)Type, Instance);
        }
    }
}