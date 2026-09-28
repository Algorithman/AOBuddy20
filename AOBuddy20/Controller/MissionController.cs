// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: MissionController.cs
// 
// Last modified: 2026-09-28 17:00
// Created:       2026-09-28 17:09
// 
// Copyright: 2026 Algorithman
// ---------------------------------------------------------------------------------------

using AOBuddy20.Interfaces;

namespace AOBuddy20.Controlling;

public class MissionController : IPacketConsumer
{
    public async Task RollMission(CancellationToken ct)
    {
    }
    
    public async Task AcceptMission(int missionId, CancellationToken ct)
    {
    }
    
    public async Task WorkGoal(CancellationToken ct)
    {
    } 

    // Finish the mission and get your reward
    public async Task FinishMission(CancellationToken ct)
    {
    }
}