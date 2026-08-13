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
            object[] arguments = { 0, drawnTile, true, false, null, null };
            bool candidateLegal = (bool)typeof(GameServer)
                .GetMethod("TryResolveWin", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(candidateServer, arguments);
            if (!candidateLegal) failures.Add("real GameServer candidate fixture must be legal");
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
    var loadouts = Enumerable.Range(0, 4)
        .ToDictionary(index => index, _ => new TalentSlotConfig());
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
    public void OnTileDrawn(TileData drawnTile)
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
