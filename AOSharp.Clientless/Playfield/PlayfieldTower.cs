using AOSharp.Common.GameData;

namespace AOSharp.Clientless;

public class PlayfieldTower
{
    internal PlayfieldTower()
    {
    }

    public Identity PlaceholderId { get; internal set; }
    public Identity TowerCharId { get; internal set; }
    public Vector3 Position { get; internal set; }
    public TowerClass Class { get; internal set; }
    public Side Side { get; internal set; }
}