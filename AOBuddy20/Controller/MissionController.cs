// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: MissionController.cs
// 
// Last modified: 2026-09-29 13:47
// Created:       2026-09-28 17:09
// 
// Long live OmniCell and AOBuddy20
// ---------------------------------------------------------------------------------------

using AOBuddy20.Interfaces;
using AOBuddy20.Network;
using SmokeLounge.AOtomation.Messaging.Messages;

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

    public void RegisterPackets(PacketRouter router)
    {
        router.Register(QuestMessageHandler, N3MessageType.Quest);
        router.Register(QuestAlternativeMessageHandler, N3MessageType.QuestAlternative);
        router.Register(CreateQuestMessageHandler, N3MessageType.CreateQuest);
    }

    private bool CreateQuestMessageHandler(AOMessage arg)
    {
        throw new NotImplementedException();
    }

    private bool QuestAlternativeMessageHandler(AOMessage arg)
    {
        throw new NotImplementedException();
    }


    private bool QuestMessageHandler(AOMessage arg)
    {
        throw new NotImplementedException();
    }
}