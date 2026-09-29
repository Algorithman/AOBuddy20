using AOSharp.Common.GameData;

namespace AOSharp.Clientless;

public static class BuffStatus
{
    public static EventHandler<BuffChangedArgs> BuffChanged;

    internal static void OnBuffMessage(SimpleChar simpleChar, int buffId)
    {
        var buffStatus = simpleChar.Buffs.Find(buffId, out var buff) && buff.Cooldown.RemainingTime > 1f ? BuffState.Refreshed : BuffState.Removed;
        var buffChangedArgs = new BuffChangedArgs(simpleChar.Identity, buffStatus, buffId);
        BuffChanged?.Invoke(null, buffChangedArgs);
    }
}

public class BuffChangedArgs : EventArgs
{
    public BuffChangedArgs(Identity character, BuffState status, int id)
    {
        Identity = character;
        Status = status;
        Id = id;
    }

    public Identity Identity { get; }
    public int Id { get; }
    public BuffState Status { get; set; }
}

public enum BuffState
{
    Removed,
    Refreshed,
}