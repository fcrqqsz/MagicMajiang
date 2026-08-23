using System.Text;
using System.Text.Json;
using MahjongGame.Core;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Interfaces;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

var failures = new List<string>();

Check(typeof(GameServer).Assembly == System.Reflection.Assembly.GetExecutingAssembly(),
    "telemetry authority regression must execute the production GameServer source compiled into this test assembly",
    failures);
await RealGameServerCountsOnlyMainLoopDrawAndEmitsAcceptedWinOnce(failures);
await ThrowingSinkCannotInterruptRealGameServerCompletion(failures);
await RealGameServerMarksAndScoresEveryCommittedKongReplacementDraw(failures);
await RealGameServerAutoTimeoutDiscardModifiedTileChargesFadingColor(failures);
await RealGameServerRobKongDoesNotChargeGatherMomentumUntilKongActuallyResolves(failures);
await RealGameServerRefreshesIndependentHuThresholdAfterCommittedMelds(failures);
JsonLineSinkWritesEscapedCompactUtf8Records(failures);
JsonLineFactoryFallsBackToNullWhenCreationFails(failures);

if (failures.Count == 0)
{
    Console.WriteLine("Real GameServer telemetry regression tests passed.");
    return 0;
}

Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
return 1;

static async Task RealGameServerCountsOnlyMainLoopDrawAndEmitsAcceptedWinOnce(List<string> failures)
{
    var sink = new MemoryTalentTelemetrySink();
    int acceptedBeforeFinalSubmission = -1;
    GameServer server = CreateWinningServer(sink, out List<IPlayerClient> clients, out List<DeckConfig> configs,
        out GameSession session,
        beforeWinningSubmission: (candidateServer, drawnTile) =>
        {
            object[] arguments = { 0, drawnTile, true, false, null, null, null };
            bool candidateLegal = (bool)typeof(GameServer)
                .GetMethod("TryResolveWin", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(candidateServer, arguments);
            if (!candidateLegal) failures.Add("real GameServer candidate fixture must be legal");
            if (arguments[6] is not TalentWinFacts candidateFacts
                || candidateFacts.WinnerSeatIndex != 0
                || !candidateFacts.IsSelfDraw
                || candidateFacts.WinningTile.Suit != Suit.Wind
                || candidateFacts.WinningTile.Value != 1)
            {
                failures.Add("real GameServer candidate resolution must produce authoritative immutable win facts");
            }
            acceptedBeforeFinalSubmission = sink.Records.Count(record => record.eventType == "accepted_win");
        });
    int completions = 0;
    var finished = new TaskCompletionSource<GameRoundCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
    server.OnRoundFinished += completion =>
    {
        completions++;
        finished.TrySetResult(completion);
    };

    server.StartGame(clients, configs, session);
    GameRoundCompletion result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
    server.SubmitAction(new ClientAction(0, ClientActionType.Hu, server.LastDrawnTile));
    await Task.Delay(20);

    TalentTelemetryRecord[] accepted = sink.Records
        .Where(record => record.eventType == "accepted_win")
        .ToArray();
    Check(result.Kind == GameRoundCompletionKind.Win && completions == 1,
        "real GameServer must complete one accepted self-draw exactly once", failures);
    Check(accepted.Length == 1,
        "real GameServer validation/counterfactual/final path must emit accepted_win exactly once", failures);
    Check(acceptedBeforeFinalSubmission == 0,
        "real GameServer legal candidate evaluation must not emit accepted_win before final acceptance", failures);
    Check(accepted.Length == 1 && accepted[0].drawsPerSeat.SequenceEqual(new[] { 1, 0, 0, 0 }),
        "real GameServer telemetry counts the seat-0 main-loop wall draw, excluding all initial deal tiles", failures);
    Check(ReferenceEquals(WinFactsObserverTalent.AttributionFacts,
                          WinFactsObserverTalent.AcceptedFacts),
        "real GameServer final evaluation, attribution, and acceptance share one TalentWinFacts instance", failures);
    TalentActionCommittedFacts acceptedHu = ActionFactsGlobalObserverTalent.Facts.SingleOrDefault();
    Check(acceptedHu?.ActionType == ClientActionType.Hu
          && acceptedHu.ActorSeatIndex == 0
          && acceptedHu.DecisionId > 0
          && ReferenceEquals(acceptedHu.WinFacts, WinFactsObserverTalent.AcceptedFacts),
        "real GameServer emits one committed Hu fact carrying the accepted TalentWinFacts instance", failures);
    Check(InitialHandObserverTalent.ByOwner.TryGetValue(0, out TalentInitialHandFacts initialHand)
          && initialHand.RoundNumber == 1
          && initialHand.Tiles.Count == 13
          && initialHand.Tiles.All(tile => tile.OriginalOwnerId == 0),
        "real GameServer completes the owner-private initial-hand hook after all 13 authoritative tiles are stored",
        failures);
}

static async Task ThrowingSinkCannotInterruptRealGameServerCompletion(List<string> failures)
{
    GameServer server = CreateWinningServer(new ThrowingSink(), out List<IPlayerClient> clients,
        out List<DeckConfig> configs, out GameSession session);
    int completions = 0;
    var finished = new TaskCompletionSource<GameRoundCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
    server.OnRoundFinished += completion =>
    {
        completions++;
        finished.TrySetResult(completion);
    };

    server.StartGame(clients, configs, session);
    GameRoundCompletion result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Check(result.Kind == GameRoundCompletionKind.Win && completions == 1 && server.WinnerId == 0,
        "throwing telemetry sink cannot interrupt real GameServer accepted-win completion", failures);
}

static async Task RealGameServerMarksAndScoresEveryCommittedKongReplacementDraw(
    List<string> failures)
{
    foreach (KongFlow flow in Enum.GetValues<KongFlow>())
    {
        var observedReplacement = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<GameRoundCompletion>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        GameServer server = CreateKongFlowServer(
            flow,
            observedReplacement,
            out List<IPlayerClient> clients,
            out List<DeckConfig> configs,
            out GameSession session);
        server.OnRoundFinished += completion => finished.TrySetResult(completion);

        server.StartGame(clients, configs, session);
        bool marked = await observedReplacement.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!marked)
        {
            server.StopGame();
            Check(false,
                $"real GameServer {flow} commit must mark the immediate replacement-draw decision",
                failures);
            continue;
        }

        GameRoundCompletion completion = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(completion.Kind == GameRoundCompletionKind.Win
              && server.WinFanDetails.Any(detail => detail.StartsWith("杠上开花("))
              && server.WinFanDetails.All(detail => !detail.StartsWith("自摸(")),
            $"real GameServer {flow} replacement self-draw must finish with kong win and exclude self-draw " +
            $"(kind={completion.Kind}, fan={server.WinFan}, details={string.Join("|", server.WinFanDetails)})",
            failures);
        ClientActionType[] expectedActions = flow switch
        {
            KongFlow.Concealed => new[] { ClientActionType.AnGan, ClientActionType.Hu },
            KongFlow.Added => new[] { ClientActionType.JiaGang, ClientActionType.Hu },
            _ => new[] { ClientActionType.Discard, ClientActionType.MingGan, ClientActionType.Hu }
        };
        TalentActionCommittedFacts[] committed = ActionFactsGlobalObserverTalent.Facts.ToArray();
        Check(committed.Select(facts => facts.ActionType).SequenceEqual(expectedActions)
              && committed.Select(facts => facts.DecisionId).Distinct().Count() == committed.Length,
            $"real GameServer {flow} records only resolved authoritative actions in decision order",
            failures);
    }
}

static GameServer CreateKongFlowServer(
    KongFlow flow,
    TaskCompletionSource<bool> observedReplacement,
    out List<IPlayerClient> clients,
    out List<DeckConfig> configs,
    out GameSession session)
{
    ActionFactsGlobalObserverTalent.Reset();
    var loadouts = Enumerable.Range(0, 4)
        .ToDictionary(index => index, _ => new TalentSlotConfig());
    loadouts[0].SlotTalentIds[3] = "network_test_action_global_observer";
    var runtime = new TalentMatchRuntime(loadouts, TalentRegistry.Instance);
    session = new GameSession(GameMode.Single);
    runtime.BeginMatch(session);

    TileData firstDraw = flow == KongFlow.Exposed
        ? KongFixtures.Tile(Suit.Man, 9, 0)
        : KongFixtures.Tile(Suit.Sou, 9, 0);
    var wall = new ScriptedDrawWallService(firstDraw, KongFixtures.Tile(Suit.Dragon, 1, 0));
    var server = new GameServer(wall, runtime, new GameServerOptions
    {
        ActionTimeoutMs = 1000,
        ResponseTimeoutMs = 1000,
        UseDebugHand = true,
        DebugHand = Enumerable.Range(0, 13).Select(_ => KongFixtures.Tile(Suit.Pin, 1, 0)).ToList()
    });
    clients = Enumerable.Range(0, 4)
        .Select(index => (IPlayerClient)new KongFlowClient(
            index,
            server,
            flow,
            observedReplacement))
        .ToList();
    configs = Enumerable.Range(0, 4).Select(_ => DeckConfig.CreateStandard()).ToList();
    return server;
}

static async Task RealGameServerRefreshesIndependentHuThresholdAfterCommittedMelds(
    List<string> failures)
{
    var loadouts = Enumerable.Range(0, 4)
        .ToDictionary(index => index, _ => new TalentSlotConfig());
    loadouts[0].SlotTalentIds[1] = "last_stand_formation";
    var runtime = new TalentMatchRuntime(loadouts, TalentRegistry.Instance);
    var session = new GameSession(GameMode.Single);
    runtime.BeginMatch(session);

    var thresholdObserved = new TaskCompletionSource<int>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var trace = new List<string>();
    var wall = new ScriptedDrawWallService(
        KongFixtures.Tile(Suit.Sou, 9, 0),
        KongFixtures.Tile(Suit.Man, 2, 1),
        KongFixtures.Tile(Suit.Pin, 5, 1));
    var server = new GameServer(wall, runtime, new GameServerOptions
    {
        ActionTimeoutMs = 1000,
        ResponseTimeoutMs = 1000,
        UseDebugHand = true,
        DebugHand = Enumerable.Range(0, 13)
            .Select(_ => KongFixtures.Tile(Suit.Sou, 1, 0))
            .ToList()
    });
    var clients = Enumerable.Range(0, 4)
        .Select(index => (IPlayerClient)new MinimumFanFlowClient(
            index,
            server,
            thresholdObserved,
            trace))
        .ToList();
    var configs = Enumerable.Range(0, 4).Select(_ => DeckConfig.CreateStandard()).ToList();

    server.StartGame(clients, configs, session);
    int notifiedMinimum;
    try
    {
        notifiedMinimum = await thresholdObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    catch (TimeoutException)
    {
        server.StopGame();
        failures.Add($"real GameServer independent Hu threshold flow timed out: {string.Join(" | ", trace)}");
        return;
    }
    ScoringOptions authoritative = server.GetScoringOptionsSnapshot(0);
    server.StopGame();

    Check(notifiedMinimum == 10
          && authoritative.MinimumFan == 10
          && authoritative.BonusFan == 0,
        $"real GameServer must refresh and privately notify the independent Hu threshold after the second committed meld " +
        $"(notified={notifiedMinimum}, authoritative={authoritative.MinimumFan}, bonus={authoritative.BonusFan})",
        failures);
}

static void JsonLineSinkWritesEscapedCompactUtf8Records(List<string> failures)
{
    string directory = Path.Combine(Path.GetTempPath(), $"supermajiang-telemetry-{Guid.NewGuid():N}");
    string path = Path.Combine(directory, "playtest.jsonl");
    try
    {
        using (var sink = new JsonLineTalentTelemetrySink(path))
        {
            sink.Record(new TalentTelemetryRecord
            {
                anonymousSessionId = "session-\"one\"",
                eventType = "line\none"
            });
            using (var readerStream = new FileStream(
                       path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(readerStream, new UTF8Encoding(false)))
            using (JsonDocument openWriterRecord = JsonDocument.Parse(reader.ReadLine() ?? string.Empty))
            {
                Check(openWriterRecord.RootElement.GetProperty("anonymousSessionId").GetString()
                      == "session-\"one\""
                      && openWriterRecord.RootElement.GetProperty("eventType").GetString() == "line\none",
                    "JSONL sink flushes a complete readable record while its writer remains open", failures);
            }
            sink.Record(new TalentTelemetryRecord
            {
                anonymousSessionId = "session-two",
                eventType = "line-\"two\""
            });
        }
        using (var appendSink = new JsonLineTalentTelemetrySink(path))
        {
            appendSink.Record(new TalentTelemetryRecord
            {
                anonymousSessionId = "session-three",
                eventType = "append"
            });
        }

        byte[] bytes = File.ReadAllBytes(path);
        string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));
        bool parsed = lines.Length == 3;
        if (parsed)
        {
            using JsonDocument first = JsonDocument.Parse(lines[0]);
            using JsonDocument second = JsonDocument.Parse(lines[1]);
            using JsonDocument third = JsonDocument.Parse(lines[2]);
            parsed = first.RootElement.GetProperty("anonymousSessionId").GetString() == "session-\"one\""
                     && first.RootElement.GetProperty("eventType").GetString() == "line\none"
                     && second.RootElement.GetProperty("eventType").GetString() == "line-\"two\""
                     && third.RootElement.GetProperty("eventType").GetString() == "append";
        }

        Check(bytes.Length >= 3 && !(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF),
            "JSONL sink writes UTF-8 without a BOM", failures);
        Check(parsed,
            "JSONL sink writes escaped parseable records on distinct physical lines and appends after reopen", failures);

        bool disposedThrows = false;
        var disposed = new JsonLineTalentTelemetrySink(path);
        disposed.Dispose();
        try
        {
            disposed.Record(new TalentTelemetryRecord { eventType = "after-dispose" });
        }
        catch (ObjectDisposedException)
        {
            disposedThrows = true;
        }
        Check(disposedThrows,
            "JSONL sink explicitly rejects records after Dispose", failures);
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static void JsonLineFactoryFallsBackToNullWhenCreationFails(List<string> failures)
{
    string directory = Path.Combine(Path.GetTempPath(), $"supermajiang-telemetry-factory-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        string existingDirectoryAsFile = Path.Combine(directory, "occupied");
        Directory.CreateDirectory(existingDirectoryAsFile);
        ITalentTelemetrySink sink = TalentTelemetry.CreateJsonLineSinkSafely(existingDirectoryAsFile);
        Check(ReferenceEquals(sink, NullTalentTelemetrySink.Instance),
            "Dedicated JSONL factory falls back to the null sink when file creation fails", failures);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static GameServer CreateWinningServer(
    ITalentTelemetrySink sink,
    out List<IPlayerClient> clients,
    out List<DeckConfig> configs,
    out GameSession session,
    Action<GameServer, TileData> beforeWinningSubmission = null)
{
    WinFactsObserverTalent.Reset();
    ActionFactsGlobalObserverTalent.Reset();
    InitialHandObserverTalent.Reset();
    var loadouts = Enumerable.Range(0, 4)
        .ToDictionary(index => index, _ => new TalentSlotConfig());
    loadouts[0].SlotTalentIds[3] = "network_test_win_facts_observer";
    loadouts[0].SlotTalentIds[4] = "network_test_action_global_observer";
    loadouts[0].SlotTalentIds[5] = "network_test_initial_hand_observer";
    var runtime = new TalentMatchRuntime(
        loadouts,
        TalentRegistry.Instance,
        sink,
        Guid.NewGuid().ToString("N"),
        AlienationPreset.Standard);
    session = new GameSession(GameMode.Single);
    runtime.BeginMatch(session);

    var wall = new DeterministicWallService(new TileData(Suit.Wind, 1, 0));
    var server = new GameServer(wall, runtime, new GameServerOptions
    {
        ActionTimeoutMs = 1000,
        ResponseTimeoutMs = 1000,
        UseDebugHand = true,
        DebugHand = CreateThirteenOrphansWaitingHand()
    });
    clients = Enumerable.Range(0, 4)
        .Select(index => (IPlayerClient)new WinningClient(index, server, beforeWinningSubmission))
        .ToList();
    configs = Enumerable.Range(0, 4).Select(_ => DeckConfig.CreateStandard()).ToList();
    return server;
}

static List<TileData> CreateThirteenOrphansWaitingHand()
{
    return new List<TileData>
    {
        new(Suit.Man, 1, 0), new(Suit.Man, 9, 0),
        new(Suit.Pin, 1, 0), new(Suit.Pin, 9, 0),
        new(Suit.Sou, 1, 0), new(Suit.Sou, 9, 0),
        new(Suit.Wind, 1, 0), new(Suit.Wind, 2, 0),
        new(Suit.Wind, 3, 0), new(Suit.Wind, 4, 0),
        new(Suit.Dragon, 1, 0), new(Suit.Dragon, 2, 0), new(Suit.Dragon, 3, 0)
    };
}

static void Check(bool condition, string message, List<string> failures)
{
    if (!condition) failures.Add(message);
}

static async Task RealGameServerAutoTimeoutDiscardModifiedTileChargesFadingColor(List<string> failures)
{
    var loadouts = Enumerable.Range(0, 4)
        .ToDictionary(index => index, _ => new TalentSlotConfig());
    loadouts[0].SlotTalentIds[3] = "fading_color";
    var runtime = new TalentMatchRuntime(loadouts, TalentRegistry.Instance);
    var session = new GameSession(GameMode.Single);
    runtime.BeginMatch(session);

    // Initial hand for seat 0 with 13 standard tiles
    var debugHand = new List<TileData>();
    for (int i = 1; i <= 9; i++) debugHand.Add(new TileData(Suit.Man, i, 0));
    for (int i = 1; i <= 4; i++) debugHand.Add(new TileData(Suit.Wind, i, 0));

    // First drawn tile is a modified tile!
    var wall = new ScriptedDrawWallService(new TileData(Suit.Sou, 1, 0) { IsModified = true });
    var server = new GameServer(wall, runtime, new GameServerOptions
    {
        ActionTimeoutMs = 100,
        ResponseTimeoutMs = 100,
        UseDebugHand = true,
        DebugHand = debugHand
    });

    var discardObserved = new TaskCompletionSource<TileData>(TaskCreationOptions.RunContinuationsAsynchronously);
    var clients = Enumerable.Range(0, 4)
        .Select(index => (IPlayerClient)new TimeoutDiscardClient(index, server, onDiscarded: tile =>
        {
            if (index == 1) discardObserved.TrySetResult(tile);
        }))
        .ToList();
    var configs = Enumerable.Range(0, 4).Select(_ => DeckConfig.CreateStandard()).ToList();

    server.StartGame(clients, configs, session);
    TileData discarded = await discardObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    server.StopGame();

    int ink = runtime.GetPrivateCounter(0, "fading_color", "ink");
    var snapshotEntries = runtime.GetSnapshotEntries().Where(e => e.OwnerSeatIndex == 0).ToArray();
    var fadingEntry = snapshotEntries.FirstOrDefault(e => e.TalentId == "fading_color");
    Check(discarded != null && discarded.IsModified && ink == 1 && fadingEntry != null && fadingEntry.PrivateValue == 1 && fadingEntry.IsRevealed,
        $"real GameServer auto timeout discard of modified tile must charge fading_color ink to 1 and reveal its public counter (discarded={discarded}, actual={ink})",
        failures);
}

static async Task RealGameServerRobKongDoesNotChargeGatherMomentumUntilKongActuallyResolves(List<string> failures)
{
    // Part 1: JiaGang is robbed -> does NOT charge momentum
    {
        var loadouts = Enumerable.Range(0, 4)
            .ToDictionary(index => index, _ => new TalentSlotConfig());
        loadouts[0].SlotTalentIds[0] = "gather_momentum";
        var runtime = new TalentMatchRuntime(loadouts, TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);

        var finished = new TaskCompletionSource<GameRoundCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wall = new ScriptedDrawWallService(
            KongFixtures.Tile(Suit.Man, 9, 0),
            KongFixtures.Tile(Suit.Dragon, 1, 0));
        var server = new GameServer(wall, runtime, new GameServerOptions
        {
            ActionTimeoutMs = 1000,
            ResponseTimeoutMs = 1000,
            UseDebugHand = true,
            DebugHand = Enumerable.Range(0, 13).Select(_ => KongFixtures.Tile(Suit.Pin, 1, 0)).ToList()
        });
        server.OnRoundFinished += completion => finished.TrySetResult(completion);

        var clients = Enumerable.Range(0, 4)
            .Select(index => (IPlayerClient)new RobKongTestClient(index, server, shouldRob: true))
            .ToList();
        var configs = Enumerable.Range(0, 4).Select(_ => DeckConfig.CreateStandard()).ToList();

        server.StartGame(clients, configs, session);
        GameRoundCompletion completion = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        server.StopGame();

        int momentum = runtime.GetPublicCounter(0, "gather_momentum", "momentum");
        Check(completion.Kind == GameRoundCompletionKind.Win && momentum == 0,
            $"real GameServer robbed added kong must not charge gather_momentum (completion={completion.Kind}, momentum={momentum})",
            failures);
    }

    // Part 2: JiaGang resolves without rob -> DOES charge momentum
    {
        var loadouts = Enumerable.Range(0, 4)
            .ToDictionary(index => index, _ => new TalentSlotConfig());
        loadouts[0].SlotTalentIds[0] = "gather_momentum";
        var runtime = new TalentMatchRuntime(loadouts, TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);

        var replacementObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wall = new ScriptedDrawWallService(
            KongFixtures.Tile(Suit.Man, 9, 0),
            KongFixtures.Tile(Suit.Dragon, 1, 0));
        var server = new GameServer(wall, runtime, new GameServerOptions
        {
            ActionTimeoutMs = 1000,
            ResponseTimeoutMs = 1000,
            UseDebugHand = true,
            DebugHand = Enumerable.Range(0, 13).Select(_ => KongFixtures.Tile(Suit.Pin, 1, 0)).ToList()
        });

        var clients = Enumerable.Range(0, 4)
            .Select(index => (IPlayerClient)new RobKongTestClient(index, server, shouldRob: false, onReplacementDraw: () => replacementObserved.TrySetResult(true)))
            .ToList();
        var configs = Enumerable.Range(0, 4).Select(_ => DeckConfig.CreateStandard()).ToList();

        server.StartGame(clients, configs, session);
        bool replacementDrew = await replacementObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        server.StopGame();

        int momentum = runtime.GetPublicCounter(0, "gather_momentum", "momentum");
        Check(replacementDrew && momentum == 1,
            $"real GameServer resolved added kong must charge gather_momentum to 1 (replacementDrew={replacementDrew}, momentum={momentum})",
            failures);
    }
}

sealed class DeterministicWallService : IWallService
{
    private readonly TileData _winningTile;
    private readonly Queue<TileData> _tiles = new();

    public DeterministicWallService(TileData winningTile) => _winningTile = winningTile;

    public int RemainingCount => _tiles.Count;

    public void BuildWall(List<DeckConfig> playerConfigs)
    {
        _tiles.Clear();
        for (int index = 0; index < 39; index++)
            _tiles.Enqueue(new TileData(Suit.Man, index % 9 + 1, index % 4));
        _tiles.Enqueue(_winningTile);
    }

    public List<TileData> GetWallTiles() => _tiles.ToList();
    public void ShuffleWall() { }
    public TileData DrawTile() => _tiles.Dequeue();
    public List<TileData> PeekTopTiles(int count) => _tiles.Take(count).ToList();
}

enum KongFlow
{
    Concealed,
    Added,
    Exposed
}

sealed class ScriptedDrawWallService : IWallService
{
    private readonly TileData[] _draws;
    private readonly Queue<TileData> _tiles = new();

    public ScriptedDrawWallService(params TileData[] draws) => _draws = draws;
    public int RemainingCount => _tiles.Count;

    public void BuildWall(List<DeckConfig> playerConfigs)
    {
        _tiles.Clear();
        for (int index = 0; index < 39; index++)
            _tiles.Enqueue(KongFixtures.Tile(Suit.Sou, index % 9 + 1, index % 4));
        foreach (TileData draw in _draws) _tiles.Enqueue(draw);
    }

    public List<TileData> GetWallTiles() => _tiles.ToList();
    public void ShuffleWall() { }
    public TileData DrawTile() => _tiles.Dequeue();
    public List<TileData> PeekTopTiles(int count) => _tiles.Take(count).ToList();
}

sealed class MinimumFanFlowClient : IPlayerClient
{
    private readonly GameServer _server;
    private readonly TaskCompletionSource<int> _thresholdObserved;
    private readonly List<string> _trace;
    private int _ownDrawCount;

    public MinimumFanFlowClient(
        int playerId,
        GameServer server,
        TaskCompletionSource<int> thresholdObserved,
        List<string> trace)
    {
        PlayerId = playerId;
        _server = server;
        _thresholdObserved = thresholdObserved;
        _trace = trace;
    }

    public int PlayerId { get; }
    public CancellationToken TurnCancellationToken { get; set; }
    public void OnGameStart(List<TileData> startingHand) { }

    public void OnTileDrawn(TileData drawnTile, bool isKongReplacementDraw)
    {
        _ownDrawCount++;
        _trace.Add($"draw:{PlayerId}:{_ownDrawCount}");
        ServerGameState state = GetServerState();
        if (PlayerId == 0 && _ownDrawCount == 1)
        {
            state.InitHand(0, new List<TileData>
            {
                KongFixtures.Tile(Suit.Man, 2, 0),
                KongFixtures.Tile(Suit.Man, 2, 0),
                KongFixtures.Tile(Suit.Pin, 5, 0),
                KongFixtures.Tile(Suit.Pin, 5, 0),
                KongFixtures.Tile(Suit.Sou, 8, 0),
                KongFixtures.Tile(Suit.Sou, 9, 0)
            });
            Submit(ClientAction.Discard(0, KongFixtures.Tile(Suit.Sou, 9, 0)));
        }
        else if (PlayerId == 1)
        {
            TileData target = _ownDrawCount == 1
                ? KongFixtures.Tile(Suit.Man, 2, 1)
                : KongFixtures.Tile(Suit.Pin, 5, 1);
            state.InitHand(1, new List<TileData> { target });
            Submit(ClientAction.Discard(1, target));
        }
    }

    public void OnTurnWithoutDraw()
    {
        _trace.Add($"no-draw:{PlayerId}");
        if (PlayerId != 0) return;
        TileData tile = GetServerState().GetHand(0)
            .FirstOrDefault(candidate => candidate.TileSuit == Suit.Sou);
        if (tile != null) Submit(ClientAction.Discard(0, tile));
    }

    public void OnOtherPlayerDiscarded(int playerId, TileData discardedTile)
    {
        _trace.Add($"response:{PlayerId}:from{playerId}:{discardedTile.TileSuit}{discardedTile.Value}");
        if (PlayerId == playerId) return;
        if (PlayerId == 0
            && playerId == 1
            && discardedTile.TileSuit is Suit.Man or Suit.Pin)
        {
            Submit(new ClientAction(0, ClientActionType.Pon, discardedTile));
            return;
        }

        Submit(ClientAction.Skip(PlayerId));
    }

    public void OnTalentInfo(ScoringOptions scoringOptions)
    {
        _trace.Add($"talent:{PlayerId}:{scoringOptions?.MinimumFan}");
        if (PlayerId == 0 && scoringOptions?.MinimumFan > 8)
            _thresholdObserved.TrySetResult(scoringOptions.MinimumFan);
    }

    private void Submit(ClientAction action)
    {
        NetworkDecisionContext decision = _server.ActiveDecision;
        if (decision != null)
            _server.SubmitNetworkAction(PlayerId, decision.DecisionId, action, out _);
    }

    private ServerGameState GetServerState() => (ServerGameState)typeof(GameServer)
        .GetField("_gameState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        .GetValue(_server);

    public void OnPlayerDrawn(int playerId) { }
    public void OnWallCountChanged(int remainingCount) { }
    public void OnAddedKongDeclared(int playerId, TileData targetTile) { }
    public void OnActionResolved(int playerId, ClientActionType actionType, TileData targetTile, int[] chiCombinations = null) { }
    public void OnDrawGame() { }
    public void OnPlayerWin(int playerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
        WinKind winKind, int loserId, WinningHandSnapshot winningHand,
        TalentFanBreakdownMessage talentFanBreakdown) { }
    public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex) { }
    public void OnSessionEnd(int[] finalScores) { }
    public void OnTimeout(TileData autoDiscardedTile) { }
    public void OnPeekWallTiles(List<TileData> topTiles) { }
}

sealed class KongFlowClient : IPlayerClient
{
    private readonly GameServer _server;
    private readonly KongFlow _flow;
    private readonly TaskCompletionSource<bool> _observedReplacement;
    private int _ownDrawCount;

    public KongFlowClient(
        int playerId,
        GameServer server,
        KongFlow flow,
        TaskCompletionSource<bool> observedReplacement)
    {
        PlayerId = playerId;
        _server = server;
        _flow = flow;
        _observedReplacement = observedReplacement;
    }

    public int PlayerId { get; }
    public CancellationToken TurnCancellationToken { get; set; }
    public void OnGameStart(List<TileData> startingHand) { }

    public void OnTileDrawn(TileData drawnTile, bool isKongReplacementDraw)
    {
        _ownDrawCount++;
        if (_flow == KongFlow.Exposed)
        {
            if (PlayerId == 0 && _ownDrawCount == 1)
            {
                ServerGameState state = GetServerState();
                TileData target = KongFixtures.Tile(Suit.Man, 9, 0);
                state.InitHand(0, new List<TileData> { target });
                state.InitHand(1, Enumerable.Range(0, 3)
                    .Select(_ => KongFixtures.Tile(Suit.Man, 9, 1))
                    .Concat(KongFixtures.CreateReadyTenTiles(1))
                    .ToList());
                Submit(new ClientAction(0, ClientActionType.Discard, target));
            }
            else if (PlayerId == 1 && _ownDrawCount == 1)
            {
                ObserveReplacementAndWin(drawnTile);
            }
            return;
        }

        if (PlayerId != 0) return;
        if (_ownDrawCount == 1)
        {
            ServerGameState state = GetServerState();
            TileData target = KongFixtures.Tile(Suit.Man, 9, 0);
            if (_flow == KongFlow.Concealed)
            {
                state.InitHand(0, Enumerable.Range(0, 4)
                    .Select(_ => KongFixtures.Tile(Suit.Man, 9, 0))
                    .Concat(KongFixtures.CreateReadyTenTiles(0))
                    .ToList());
                Submit(new ClientAction(0, ClientActionType.AnGan, target));
            }
            else
            {
                state.InitHand(0, Enumerable.Range(0, 2)
                    .Select(_ => KongFixtures.Tile(Suit.Man, 9, 0))
                    .Concat(KongFixtures.CreateReadyTenTiles(0))
                    .ToList());
                state.ApplyMeld(0, ClientActionType.Pon, target, null);
                state.AddTile(0, target);
                Submit(new ClientAction(0, ClientActionType.JiaGang, target));
            }
        }
        else if (_ownDrawCount == 2)
        {
            ObserveReplacementAndWin(drawnTile);
        }
    }

    public void OnOtherPlayerDiscarded(int playerId, TileData discardedTile)
    {
        if (_flow != KongFlow.Exposed || playerId != 0 || PlayerId == 0) return;
        Submit(PlayerId == 1
            ? new ClientAction(PlayerId, ClientActionType.MingGan, discardedTile)
            : ClientAction.Skip(PlayerId));
    }

    public void OnAddedKongDeclared(int playerId, TileData targetTile)
    {
        if (_flow == KongFlow.Added && PlayerId != playerId)
            Submit(ClientAction.Skip(PlayerId));
    }

    private void ObserveReplacementAndWin(TileData drawnTile)
    {
        NetworkDecisionContext decision = _server.ActiveDecision;
        bool marked = decision?.IsKongReplacementDraw == true;
        _observedReplacement.TrySetResult(marked);
        if (!marked) return;
        Submit(new ClientAction(PlayerId, ClientActionType.Hu, drawnTile));
    }

    private void Submit(ClientAction action)
    {
        NetworkDecisionContext decision = _server.ActiveDecision;
        _server.SubmitNetworkAction(PlayerId, decision.DecisionId, action, out _);
    }

    private ServerGameState GetServerState() => (ServerGameState)typeof(GameServer)
        .GetField("_gameState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        .GetValue(_server);

    public void OnPlayerDrawn(int playerId) { }
    public void OnTurnWithoutDraw() { }
    public void OnWallCountChanged(int remainingCount) { }
    public void OnActionResolved(int playerId, ClientActionType actionType, TileData targetTile, int[] chiCombinations = null) { }
    public void OnDrawGame() { }
    public void OnPlayerWin(int playerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
        WinKind winKind, int loserId, WinningHandSnapshot winningHand,
        TalentFanBreakdownMessage talentFanBreakdown) { }
    public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex) { }
    public void OnSessionEnd(int[] finalScores) { }
    public void OnTimeout(TileData autoDiscardedTile) { }
    public void OnTalentInfo(ScoringOptions scoringOptions) { }
    public void OnPeekWallTiles(List<TileData> topTiles) { }
}

static class KongFixtures
{
    public static TileData Tile(Suit suit, int value, int ownerId) => new(suit, value, ownerId);

    public static List<TileData> CreateReadyTenTiles(int ownerId) => new()
    {
        Tile(Suit.Man, 2, ownerId), Tile(Suit.Man, 3, ownerId), Tile(Suit.Man, 4, ownerId),
        Tile(Suit.Man, 5, ownerId), Tile(Suit.Man, 6, ownerId), Tile(Suit.Man, 7, ownerId),
        Tile(Suit.Pin, 2, ownerId), Tile(Suit.Pin, 3, ownerId), Tile(Suit.Pin, 4, ownerId),
        Tile(Suit.Dragon, 1, ownerId)
    };
}

sealed class WinningClient : IPlayerClient
{
    private readonly GameServer _server;
    private readonly Action<GameServer, TileData> _beforeWinningSubmission;

    public WinningClient(
        int playerId,
        GameServer server,
        Action<GameServer, TileData> beforeWinningSubmission)
    {
        PlayerId = playerId;
        _server = server;
        _beforeWinningSubmission = beforeWinningSubmission;
    }

    public int PlayerId { get; }
    public CancellationToken TurnCancellationToken { get; set; }
    public void OnGameStart(List<TileData> startingHand) { }
    public void OnTileDrawn(TileData drawnTile, bool isKongReplacementDraw)
    {
        if (PlayerId != 0) return;
        _beforeWinningSubmission?.Invoke(_server, drawnTile);
        NetworkDecisionContext decision = _server.ActiveDecision;
        _server.SubmitNetworkAction(
            PlayerId,
            decision.DecisionId,
            new ClientAction(PlayerId, ClientActionType.Hu, drawnTile),
            out _);
    }
    public void OnPlayerDrawn(int playerId) { }
    public void OnTurnWithoutDraw() { }
    public void OnWallCountChanged(int remainingCount) { }
    public void OnOtherPlayerDiscarded(int playerId, TileData discardedTile) { }
    public void OnAddedKongDeclared(int playerId, TileData targetTile) { }
    public void OnActionResolved(int playerId, ClientActionType actionType, TileData targetTile, int[] chiCombinations = null) { }
    public void OnDrawGame() { }
    public void OnPlayerWin(int playerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
        WinKind winKind, int loserId, WinningHandSnapshot winningHand,
        TalentFanBreakdownMessage talentFanBreakdown) { }
    public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex) { }
    public void OnSessionEnd(int[] finalScores) { }
    public void OnTimeout(TileData autoDiscardedTile) { }
    public void OnTalentInfo(ScoringOptions scoringOptions) { }
    public void OnPeekWallTiles(List<TileData> topTiles) { }
}

sealed class ThrowingSink : ITalentTelemetrySink
{
    public void Record(TalentTelemetryRecord record) =>
        throw new InvalidOperationException("expected telemetry sink failure");
}

sealed class TimeoutDiscardClient : IPlayerClient
{
    private readonly GameServer _server;
    private readonly Action<TileData> _onDiscarded;

    public TimeoutDiscardClient(int playerId, GameServer server, Action<TileData> onDiscarded)
    {
        PlayerId = playerId;
        _server = server;
        _onDiscarded = onDiscarded;
    }

    public int PlayerId { get; }
    public CancellationToken TurnCancellationToken { get; set; }
    public void OnGameStart(List<TileData> startingHand) { }
    public void OnTileDrawn(TileData drawnTile, bool isKongReplacementDraw) { }
    public void OnPlayerDrawn(int playerId) { }
    public void OnTurnWithoutDraw() { }
    public void OnWallCountChanged(int remainingCount) { }
    public void OnOtherPlayerDiscarded(int playerId, TileData discardedTile)
    {
        if (playerId == 0)
        {
            _onDiscarded?.Invoke(discardedTile);
        }
        NetworkDecisionContext decision = _server.ActiveDecision;
        if (decision != null && decision.Phase == NetworkDecisionPhase.Response)
        {
            _server.SubmitNetworkAction(PlayerId, decision.DecisionId, ClientAction.Skip(PlayerId), out _);
        }
    }
    public void OnAddedKongDeclared(int playerId, TileData targetTile) { }
    public void OnActionResolved(int playerId, ClientActionType actionType, TileData targetTile, int[] chiCombinations = null) { }
    public void OnDrawGame() { }
    public void OnPlayerWin(int playerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
        WinKind winKind, int loserId, WinningHandSnapshot winningHand,
        TalentFanBreakdownMessage talentFanBreakdown) { }
    public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex) { }
    public void OnSessionEnd(int[] finalScores) { }
    public void OnTimeout(TileData autoDiscardedTile) { }
    public void OnTalentInfo(ScoringOptions scoringOptions) { }
    public void OnPeekWallTiles(List<TileData> topTiles) { }
}

sealed class RobKongTestClient : IPlayerClient
{
    private readonly GameServer _server;
    private readonly bool _shouldRob;
    private readonly Action _onReplacementDraw;
    private int _ownDrawCount;

    public RobKongTestClient(int playerId, GameServer server, bool shouldRob, Action onReplacementDraw = null)
    {
        PlayerId = playerId;
        _server = server;
        _shouldRob = shouldRob;
        _onReplacementDraw = onReplacementDraw;
    }

    public int PlayerId { get; }
    public CancellationToken TurnCancellationToken { get; set; }
    public void OnGameStart(List<TileData> startingHand) { }

    public void OnTileDrawn(TileData drawnTile, bool isKongReplacementDraw)
    {
        _ownDrawCount++;
        ServerGameState state = (ServerGameState)typeof(GameServer)
            .GetField("_gameState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(_server);

        if (PlayerId == 0 && _ownDrawCount == 1)
        {
            TileData target = KongFixtures.Tile(Suit.Man, 9, 0);
            state.InitHand(0, Enumerable.Range(0, 2)
                .Select(_ => KongFixtures.Tile(Suit.Man, 9, 0))
                .Concat(KongFixtures.CreateReadyTenTiles(0))
                .ToList());
            state.ApplyMeld(0, ClientActionType.Pon, target, null);
            state.AddTile(0, target);

            if (_shouldRob)
            {
                // Set up Seat 1 for Thirteen Orphans waiting on 9万
                state.InitHand(1, new List<TileData>
                {
                    new(Suit.Man, 1, 1),
                    new(Suit.Pin, 1, 1), new(Suit.Pin, 9, 1),
                    new(Suit.Sou, 1, 1), new(Suit.Sou, 9, 1),
                    new(Suit.Wind, 1, 1), new(Suit.Wind, 2, 1),
                    new(Suit.Wind, 3, 1), new(Suit.Wind, 4, 1),
                    new(Suit.Dragon, 1, 1), new(Suit.Dragon, 2, 1), new(Suit.Dragon, 3, 1),
                    new(Suit.Dragon, 1, 1)
                });
            }

            NetworkDecisionContext decision = _server.ActiveDecision;
            _server.SubmitNetworkAction(0, decision.DecisionId,
                new ClientAction(0, ClientActionType.JiaGang, target), out _);
        }
        else if (isKongReplacementDraw)
        {
            _onReplacementDraw?.Invoke();
            NetworkDecisionContext decision = _server.ActiveDecision;
            _server.SubmitNetworkAction(PlayerId, decision.DecisionId,
                ClientAction.Discard(PlayerId, drawnTile), out _);
        }
    }

    public void OnPlayerDrawn(int playerId) { }
    public void OnTurnWithoutDraw() { }
    public void OnWallCountChanged(int remainingCount) { }
    public void OnOtherPlayerDiscarded(int playerId, TileData discardedTile)
    {
        NetworkDecisionContext decision = _server.ActiveDecision;
        if (decision != null && decision.Phase == NetworkDecisionPhase.Response)
        {
            _server.SubmitNetworkAction(PlayerId, decision.DecisionId, ClientAction.Skip(PlayerId), out _);
        }
    }

    public void OnAddedKongDeclared(int playerId, TileData targetTile)
    {
        if (PlayerId == playerId) return;
        NetworkDecisionContext decision = _server.ActiveDecision;
        if (_shouldRob && PlayerId == 1)
        {
            _server.SubmitNetworkAction(PlayerId, decision.DecisionId,
                new ClientAction(PlayerId, ClientActionType.Hu, targetTile), out _);
        }
        else
        {
            _server.SubmitNetworkAction(PlayerId, decision.DecisionId,
                ClientAction.Skip(PlayerId), out _);
        }
    }

    public void OnActionResolved(int playerId, ClientActionType actionType, TileData targetTile, int[] chiCombinations = null) { }
    public void OnDrawGame() { }
    public void OnPlayerWin(int playerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
        WinKind winKind, int loserId, WinningHandSnapshot winningHand,
        TalentFanBreakdownMessage talentFanBreakdown) { }
    public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex) { }
    public void OnSessionEnd(int[] finalScores) { }
    public void OnTimeout(TileData autoDiscardedTile) { }
    public void OnTalentInfo(ScoringOptions scoringOptions) { }
    public void OnPeekWallTiles(List<TileData> topTiles) { }
}
