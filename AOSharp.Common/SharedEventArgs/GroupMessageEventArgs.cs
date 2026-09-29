using AOSharp.Common.GameData;

namespace AOSharp.Common.SharedEventArgs;

public class GroupMessageEventArgs : EventArgs
{
    public readonly GroupMessage Message;

    public GroupMessageEventArgs(GroupMessage message)
    {
        Message = message;
    }

    public bool Cancel { get; set; } = false;
}