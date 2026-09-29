// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: PacketRouter.cs
// 
// Last modified: 2026-09-30 00:19
// Created:       2026-09-29 23:09
// 
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using AOBuddy20.Extensions;
using AOBuddy20.Utils;
using AOSharp.Clientless;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using SmokeLounge.AOtomation.Messaging.Messages;

namespace AOBuddy20.Network;

[MinLogLevel(LogEventLevel.Debug)]
public sealed class PacketRouter
{
    private readonly Dictionary<ChatMessageType, List<ChatPacketHandlerEntry>> _chatHandlers
        = new Dictionary<ChatMessageType, List<ChatPacketHandlerEntry>>();

    private readonly ILogger<PacketRouter> _logger;

    private readonly Dictionary<N3MessageType, List<PacketHandlerEntry>> _n3Handlers
        = new Dictionary<N3MessageType, List<PacketHandlerEntry>>();

    private readonly Dictionary<SystemMessageType, List<SystemPacketHandlerEntry>> _systemHandlers
        = new Dictionary<SystemMessageType, List<SystemPacketHandlerEntry>>();

    public PacketRouter(ILogger<PacketRouter> logger)
    {
        _logger = logger;
        _logger.LogInformation("PacketRouter initialized.");
    }


    public void Init()
    {
        Client.Chat.NetworkMessageReceived += DispatchChatMessage;
        Client.MessageReceived += Dispatch;
    }

    private void DispatchChatMessage(object? sender, ChatMessage e)
    {
        if (_chatHandlers.TryGetValue(e.Header.PacketType, out var list))
        {
            foreach (var entry in list.OrderBy(x => x.canEndSequence).ThenBy(x => x.receivePriority))
            {
                var end = entry.Handler(e);
                if (entry.canEndSequence && end)
                {
                    break; // ← handled=true, stop propagation
                }
            }
        }
    }


    public void RegisterChatHandler(Func<ChatMessage, bool> handler, ChatMessageType type, int receivePriority, bool endSequence = false)
    {
        _chatHandlers.GetOrAdd(type, _ => new List<ChatPacketHandlerEntry>()).Add(new ChatPacketHandlerEntry(handler, endSequence, receivePriority));
    }

    public void RegisterSystemHandler(Func<SystemMessage, bool> handler, SystemMessageType type, int receivePriority, bool endSequence = false)
    {
        _systemHandlers.GetOrAdd(type, _ => new List<SystemPacketHandlerEntry>())
            .Add(new SystemPacketHandlerEntry(handler, endSequence, receivePriority));
    }

    public void Register(Func<AOMessage, bool> handler, N3MessageType type, int receivePriority, bool canEndSequence = false)
    {
        _n3Handlers.GetOrAdd(type, _ => new List<PacketHandlerEntry>()).Add(new PacketHandlerEntry(handler, canEndSequence, receivePriority));
    }


    public void Dispatch(object? sender, AOMessage e)
    {
        if (e.Body is N3Message n3Message)
        {
            if (_n3Handlers.TryGetValue(n3Message.N3MessageType, out var list))
            {
                foreach (var entry in list.OrderBy(x => x.canEndSequence).ThenBy(x => x.receivePriority))
                {
                    var end = entry.Handler(e);
                    if (entry.canEndSequence && end)
                    {
                        break; // ← handled=true, stop propagation
                    }
                }
            }
        }

        if (e.Body is SystemMessage system)
        {
            if (_systemHandlers.TryGetValue(system.SystemMessageType, out var list))
            {
                foreach (var entry in list.OrderBy(x => x.canEndSequence).ThenBy(x => x.receivePriority))
                {
                    var end = entry.Handler(system);
                    if (entry.canEndSequence && end)
                    {
                        break; // ← handled=true, stop propagation
                    }
                }
            }
        }
    }

    public record PacketHandlerEntry(Func<AOMessage, bool> Handler, bool canEndSequence, int receivePriority)
    {
    }

    public record SystemPacketHandlerEntry(Func<SystemMessage, bool> Handler, bool canEndSequence, int receivePriority)
    {
    }

    public record ChatPacketHandlerEntry(Func<ChatMessage, bool> Handler, bool canEndSequence, int receivePriority)
    {
    }
}