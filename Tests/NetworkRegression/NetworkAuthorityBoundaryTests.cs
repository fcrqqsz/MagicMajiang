using System;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Systems;

internal static class NetworkAuthorityBoundaryTests
{
    public static void Run(RegressionRunner runner)
    {
        TestGameSceneEntry(runner);
        TestRoomRequiresFourOccupiedSeats(runner);
        TestGameModeLengths(runner);
    }

    private static void TestGameSceneEntry(RegressionRunner runner)
    {
        runner.Check(NetworkGameSceneEntryPolicy.Decide(true, true, true)
            == NetworkGameSceneEntryDecision.InitializeNetworkClient,
            "Game scene initializes only for an existing network room.");

        foreach (var state in new[]
        {
            (Manager: false, Service: false, Room: false),
            (Manager: true, Service: false, Room: false),
            (Manager: true, Service: true, Room: false)
        })
        {
            runner.Check(NetworkGameSceneEntryPolicy.Decide(state.Manager, state.Service, state.Room)
                == NetworkGameSceneEntryDecision.ReturnToPersistent,
                "Missing network authority must return to Persistent without a local fallback.");
        }
    }

    private static void TestRoomRequiresFourOccupiedSeats(RegressionRunner runner)
    {
        runner.Check(RoomReadyPolicy.CanMarkMatchReady(occupiedSeatCount: 4),
            "A human can ready when all four authoritative seats are occupied.");
        runner.Check(!RoomReadyPolicy.CanMarkMatchReady(occupiedSeatCount: 3),
            "An empty seat blocks match ready instead of being filled implicitly.");
    }

    private static void TestGameModeLengths(RegressionRunner runner)
    {
        runner.Check(new GameSession(GameMode.Single).GetTotalRounds() == 1,
            "Single remains a one-round mode, not a local-play switch.");
        runner.Check(new GameSession(GameMode.EastOnly).GetTotalRounds() == 4,
            "EastOnly remains four rounds.");
        runner.Check(new GameSession(GameMode.HalfGame).GetTotalRounds() == 8,
            "HalfGame remains eight rounds.");
        runner.Check(new GameSession(GameMode.FullGame).GetTotalRounds() == 16,
            "FullGame remains sixteen rounds.");
    }

}
