using System;
using System.IO;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Systems;

internal static class NetworkAuthorityBoundaryTests
{
    public static void Run(RegressionRunner runner)
    {
        TestGameSceneEntry(runner);
        TestGameManagerHasNoAuthority(runner);
        TestPresentationHasNoLocalAuthority(runner);
        TestSingleHumanAiFill(runner);
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

    private static void TestSingleHumanAiFill(RegressionRunner runner)
    {
        runner.Check(RoomReadyPolicy.CanMarkMatchReady(aiFill: true, humanCount: 1),
            "One human can start when AI fill is enabled.");
        runner.Check(!RoomReadyPolicy.CanMarkMatchReady(aiFill: false, humanCount: 1),
            "One human cannot start when AI fill is disabled.");
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

    private static void TestGameManagerHasNoAuthority(RegressionRunner runner)
    {
        string source = ReadRepoFile("Assets", "Scripts", "Core", "GameManager.cs");
        string[] forbidden =
        {
            "new GameServer(",
            "new SimpleAIClient(",
            "Session.AdvanceRound(",
            "starting_capital",
            "BuildTalentConfigs(",
            "StartGameWithConfig(",
            "StartSession("
        };

        foreach (string fragment in forbidden)
            runner.Check(!source.Contains(fragment, StringComparison.Ordinal),
                $"GameManager must not contain authority fragment: {fragment}");

        string roomSource = ReadRepoFile("Assets", "Scripts", "Core", "Network", "Room.cs");
        runner.Check(Count(roomSource, "Session.AdvanceRound(") == 1,
            "Room owns the single authoritative round advance call.");
    }

    private static void TestPresentationHasNoLocalAuthority(RegressionRunner runner)
    {
        string hud = ReadRepoFile("Assets", "UI", "GameHUD", "GameHUDController.cs");
        runner.Check(!hud.Contains("DeckManager.Instance", StringComparison.Ordinal),
            "HUD wall count comes only from server projection.");
        runner.Check(!hud.Contains("IsNetworkRoom", StringComparison.Ordinal),
            "HUD has no local/network authority branch.");

        string result = ReadRepoFile("Assets", "UI", "ResultPanelController.cs");
        runner.Check(!result.Contains("GameManager.Instance.EndSession", StringComparison.Ordinal),
            "Result UI never broadcasts a client-owned session end.");
        runner.Check(!result.Contains("SceneManager.LoadScene(SceneNames.Game", StringComparison.Ordinal),
            "Result fallback never reloads the Game scene.");
    }

    private static string ReadRepoFile(params string[] segments)
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ProjectSettings", "ProjectVersion.txt")))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Repository root not found.");
        return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray()));
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }
}
