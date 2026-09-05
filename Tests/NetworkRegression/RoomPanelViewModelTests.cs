using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.UI;

internal static class RoomPanelViewModelTests
{
    public static void Run(RegressionRunner runner)
    {
        BuildsDistinctSeatStates(runner);
        KeepsReadyActionReversible(runner);
        BlocksReadyWhileASeatIsEmpty(runner);
        ExposesHostAiMutationNotice(runner);
    }

    private static void BuildsDistinctSeatStates(RegressionRunner runner)
    {
        RoomSeatMessage[] seats =
        {
            Human(0, "房主", isHost: true, isReady: true),
            Human(1, "离线玩家", isOnline: false, temporarilyAiControlled: true),
            Ai(2, "标准 AI", AiDifficulty.Standard, AiLoadoutTemplate.Stable),
            new RoomSeatMessage { seatIndex = 3, isOccupied = false }
        };

        RoomPanelViewModel view = RoomPanelViewModel.Build(
            "R1208", RoomState.WaitingForPlayers, seats, 0, null);

        runner.Check(view.Seats[0].IsLocal && view.Seats[0].IsHost && view.Seats[0].IsReady,
            "room panel identifies the local host and ready state");
        runner.Check(view.Seats[1].State == RoomSeatVisualState.TemporaryAiControl
                     && view.Seats[1].StatusText.Contains("托管"),
            "room panel keeps temporary AI control distinct from permanent AI");
        runner.Check(view.Seats[2].State == RoomSeatVisualState.PermanentAi
                     && view.Seats[2].DifficultyText == "标准",
            "room panel exposes permanent AI difficulty");
        runner.Check(view.Seats[3].State == RoomSeatVisualState.Empty && view.Seats[3].CanAddAi,
            "room panel exposes an empty seat as an AI insertion target for the host");
    }

    private static void KeepsReadyActionReversible(RegressionRunner runner)
    {
        RoomSeatMessage[] readySeats =
        {
            Human(0, "本家", isHost: true, isReady: true),
            Human(1, "玩家二", isReady: true),
            Ai(2, "AI 二号", AiDifficulty.Beginner, AiLoadoutTemplate.Aggressive),
            Ai(3, "AI 三号", AiDifficulty.Standard, AiLoadoutTemplate.Stable)
        };

        RoomPanelViewModel ready = RoomPanelViewModel.Build(
            "R1001", RoomState.WaitingForMatchReady, readySeats, 0, null);
        runner.Check(ready.CanToggleReady && ready.ReadyButtonText == "取消准备" && !ready.ReadyTarget,
            "a ready human can cancel readiness before match start");

        readySeats[0].isReady = false;
        RoomPanelViewModel notReady = RoomPanelViewModel.Build(
            "R1001", RoomState.WaitingForMatchReady, readySeats, 0, null);
        runner.Check(notReady.CanToggleReady && notReady.ReadyButtonText == "确认准备" && notReady.ReadyTarget,
            "an unready human can ready when all seats are occupied");
    }

    private static void BlocksReadyWhileASeatIsEmpty(RegressionRunner runner)
    {
        RoomSeatMessage[] seats =
        {
            Human(0, "本家", isHost: true),
            Human(1, "玩家二"),
            Ai(2, "AI", AiDifficulty.Standard, AiLoadoutTemplate.Stable),
            new RoomSeatMessage { seatIndex = 3, isOccupied = false }
        };

        RoomPanelViewModel view = RoomPanelViewModel.Build(
            "R1002", RoomState.WaitingForPlayers, seats, 0, null);
        runner.Check(!view.CanToggleReady && view.ReadyBlockedReason.Contains("四个席位"),
            "room panel blocks readiness until all four seats are occupied");
    }

    private static void ExposesHostAiMutationNotice(RegressionRunner runner)
    {
        RoomPanelViewModel view = RoomPanelViewModel.Build(
            "R1003", RoomState.WaitingForMatchReady,
            new[]
            {
                Human(0, "本家", isHost: true),
                Human(1, "玩家二", isReady: true),
                Ai(2, "AI 二号", AiDifficulty.Standard, AiLoadoutTemplate.Stable),
                Ai(3, "AI 三号", AiDifficulty.Standard, AiLoadoutTemplate.Stable)
            },
            0,
            "房主调整了 AI，等待房主重新准备。");

        runner.Check(view.NoticeText.Contains("房主调整了 AI") && view.HumanCount == 2
                     && view.AiCount == 2 && view.EmptyCount == 0,
            "room panel preserves authoritative AI mutation notice and seat counts");
    }

    private static RoomSeatMessage Human(int index, string name, bool isHost = false,
        bool isReady = false, bool isOnline = true, bool temporarilyAiControlled = false)
    {
        return new RoomSeatMessage
        {
            seatIndex = index,
            isOccupied = true,
            isAi = false,
            seatKind = (int)RoomSeatKind.Human,
            displayName = name,
            isHost = isHost,
            isReady = isReady,
            isOnline = isOnline,
            isTemporarilyAiControlled = temporarilyAiControlled,
            controlState = temporarilyAiControlled ? "AiControlled" : "HumanControlled"
        };
    }

    private static RoomSeatMessage Ai(int index, string name, AiDifficulty difficulty,
        AiLoadoutTemplate template)
    {
        return new RoomSeatMessage
        {
            seatIndex = index,
            isOccupied = true,
            isAi = true,
            isOnline = true,
            isReady = true,
            seatKind = (int)RoomSeatKind.PermanentAi,
            displayName = name,
            controlState = "AiControlled",
            aiConfig = new AiSeatConfigMessage
            {
                difficulty = (int)difficulty,
                template = (int)template
            }
        };
    }
}
