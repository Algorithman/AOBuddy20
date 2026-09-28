// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: PacketRouter.cs
// 
// Last modified: 2026-09-28 17:55
// Created:       2026-09-28 17:09
// 
// Long live OmniCell and AOBuddy20
// ---------------------------------------------------------------------------------------

using AOBuddy20.Extensions;
using SmokeLounge.AOtomation.Messaging.Messages;

namespace AOBuddy20.Network;

public sealed class PacketRouter
{
    private readonly Dictionary<Type, List<PacketHandlerEntry>> _handlers
        = new Dictionary<Type, List<PacketHandlerEntry>>();

    public void Register<T>(Action<T> handler, bool endSequence = false) where T : IPacket
    {
        Action<IPacket> wrapped = p => handler((T)p);
        _handlers.GetOrAdd(typeof(T), _ => new List<PacketHandlerEntry>()).Add(new PacketHandlerEntry(wrapped, endSequence));
    }

    public void Dispatch(IPacket packet)
    {
        if (!_handlers.TryGetValue(packet.GetType(), out var list))
        {
            return;
        }

        foreach (var entry in list)
        {
            entry.Handler(packet);
            if (entry.canEndSequence)
            {
                break; // ← handled=true, stop propagation
            }
        }
    }

    public record PacketHandlerEntry(Action<IPacket> Handler, bool canEndSequence)
    {
    }
}