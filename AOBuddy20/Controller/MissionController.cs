// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: MissionController.cs
// 
// Last modified: 2026-09-30 00:19
// Created:       2026-09-30 00:09
// 
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using AOBuddy20.Interfaces;
using AOBuddy20.Network;
using AOBuddy20.Utils;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using SmokeLounge.AOtomation.Messaging.Messages;

namespace AOBuddy20.Controlling;

[MinLogLevel(LogEventLevel.Debug)]
public class MissionController : IPacketConsumer
{
    private readonly ILogger<MissionController> _logger;

    public MissionController(ILogger<MissionController> logger)
    {
        _logger = logger;
        _logger.LogInformation("MissionController initialized");
    }

    public void RegisterPackets(PacketRouter router)
    {
        router.Register(QuestMessageHandler, N3MessageType.Quest, 0);
        router.Register(QuestAlternativeMessageHandler, N3MessageType.QuestAlternative, 0);
        router.Register(CreateQuestMessageHandler, N3MessageType.CreateQuest, 0);
    }

    public async Task RollMission(CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public async Task AcceptMission(int missionId, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public async Task WorkGoal(CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    // Finish the mission and get your reward
    public async Task FinishMission(CancellationToken ct)
    {
        throw new NotImplementedException();
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