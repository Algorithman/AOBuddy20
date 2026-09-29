using AOSharp.Common.GameData;

namespace AOSharp.Common.SharedEventArgs;

public class AttemptingSpellCastEventArgs : EventArgs
{
    public readonly Identity Nano;
    public readonly Identity Target;

    public AttemptingSpellCastEventArgs(Identity nano, Identity target)
    {
        Nano = nano;
        Target = target;
        Blocked = false;
    }

    public bool Blocked { get; private set; }

    public void Block()
    {
        Blocked = true;
    }
}