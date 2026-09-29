// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: IPacketConsumer.cs
// 
// Last modified: 2026-09-30 00:19
// Created:       2026-09-29 23:09
// 
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using AOBuddy20.Network;

namespace AOBuddy20.Interfaces;

public interface IPacketConsumer
{
    void RegisterPackets(PacketRouter router);
}