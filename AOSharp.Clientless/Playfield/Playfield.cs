using System.Collections.ObjectModel;
using AOSharp.Clientless.Logging;
using AOSharp.Common.GameData;
using Newtonsoft.Json;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Clientless;

public static class Playfield
{
    public static PlayfieldId ModelId;
    public static EventHandler<TowerUpdateEventArgs> TowerUpdate;
    private static readonly Dictionary<Identity, PlayfieldTower> _towers = new Dictionary<Identity, PlayfieldTower>();
    private static Dictionary<int, string> _playfieldNames;

    // Static objects in an instanced zone (a Fair Trade, flags C77D) get their live ids from the zone-in packet: one
    // record per run of the zone's object list {type, start, count, first instance}. Our static id numbers each
    // object within its type ((id >> 16) & 0x3FFF); walking that type's records in order gives its live instance.
    // Capture 20260924-192208 (the alt, Borealis and Newland Fair Trade): Terminal records 0EE4CB07 x2 then
    // 0EE4CB09, and the client used the bank terminal (static C00104A2, type index 1) as C73D:0EE4CB08; in Newland
    // 0EE73631 x2 -> 0EE73632. Terminals never come from the server, so the static id is all we have to go on;
    // the server refuses a Use on it (GenericCmd echo Verification 2).
    private static PlayfieldAnarchyFMessage.PlayfieldDynelInfo[] _liveIds;
    public static string Name => _playfieldNames.TryGetValue((int)ModelId, out var name) ? name : ModelId.ToString();
    public static ReadOnlyCollection<PlayfieldTower> Towers => _towers.Values.ToList().AsReadOnly();

    internal static void Init(PlayfieldAnarchyFMessage playfieldMessage)
    {
        ModelId = (PlayfieldId)playfieldMessage.PlayfieldId1.Instance;
        _liveIds = playfieldMessage.Dynels;
        _towers.Clear();
        DynelManager.Reset();
        DynelManager.InitStaticDynels(ModelId);
        Inventory.ResetContainers();
        FullUpdateProxy.Reset();
    }

    public static Identity LiveIdentity(Identity staticId)
    {
        if (_liveIds == null || ((uint)staticId.Instance & 0xC0000000) != 0xC0000000)
        {
            return staticId;
        }

        var k = (int)(((uint)staticId.Instance >> 16) & 0x3FFF);
        foreach (var r in _liveIds)
        {
            if (r == null || r.IdentityType != staticId.Type)
            {
                continue;
            }

            if (k < r.Unknown3)
            {
                return new Identity(staticId.Type, r.Instance + k);
            }

            k -= r.Unknown3;
        }

        return staticId;
    }

    internal static void MakeTower(TowerInfo towerInfo, PlayfieldTowerUpdateType updateReason)
    {
        var tower = new PlayfieldTower
        {
            PlaceholderId = towerInfo.PlaceholderId,
            TowerCharId = towerInfo.TowerCharId,
            Position = towerInfo.Position,
            Side = towerInfo.Side,
            Class = towerInfo.Class,
        };

        _towers.Add(towerInfo.PlaceholderId, tower);

        TowerUpdate?.Invoke(null, new TowerUpdateEventArgs
        {
            Tower = tower,
            UpdateType = updateReason,
        });
    }

    internal static void DestroyTower(Identity placeholderId)
    {
        if (_towers.TryGetValue(placeholderId, out var tower))
        {
            _towers.Remove(placeholderId);

            TowerUpdate?.Invoke(null, new TowerUpdateEventArgs
            {
                Tower = tower,
                UpdateType = PlayfieldTowerUpdateType.Destroyed,
            });
        }
    }

    internal static void LoadPlayfieldNames()
    {
        try
        {
            _playfieldNames = JsonConvert.DeserializeObject<Dictionary<int, string>>(File.ReadAllText("GameData\\PlayfieldNames.json"));
        }
        catch
        {
            Logger.Error("Failed to load Playfield Names.");
            _playfieldNames = new Dictionary<int, string>();
        }
    }

    public static bool TryGetPlayfieldNameFromId(int id, out string playfieldName)
    {
        if (_playfieldNames == null)
        {
            playfieldName = string.Empty;
            return false;
        }

        return _playfieldNames.TryGetValue(id, out playfieldName);
    }
}

public enum PlayfieldTowerUpdateType
{
    InitialLoad,
    Planted,
    Destroyed,
}

public class TowerUpdateEventArgs : EventArgs
{
    public PlayfieldTower Tower { get; set; }
    public PlayfieldTowerUpdateType UpdateType { get; set; }
}