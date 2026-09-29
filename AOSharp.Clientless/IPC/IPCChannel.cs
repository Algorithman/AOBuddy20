using AOSharp.Core.IPC;

namespace AOSharp.Clientless;

public class IPCChannel<TOpcode> : IPCChannel where TOpcode : Enum, IConvertible
{
    public IPCChannel(byte channelId) : base(channelId)
    {
    }

    public void RegisterCallback(TOpcode opCode, Action<int, IPCMessage> callback)
    {
        RegisterCallback(opCode.ToInt16(null), callback);
    }
}

public class IPCChannel : IPCChannelBase
{
    public IPCChannel(byte channelId) : base(channelId)
    {
    }

    protected override int _localDynelId => Client.LocalDynelId;

    internal static void UpdateInternal()
    {
        Update();
    }
}