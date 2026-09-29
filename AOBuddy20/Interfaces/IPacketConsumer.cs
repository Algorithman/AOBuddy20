// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: IPacketConsumer.cs
// 
// Last modified: 2026-09-29 13:47
// Created:       2026-09-28 18:09
// 
// Long live OmniCell and AOBuddy20
// ---------------------------------------------------------------------------------------

using AOBuddy20.Network;

namespace AOBuddy20.Interfaces;

public interface IPacketConsumer
{
    void RegisterPackets(PacketRouter router);
}