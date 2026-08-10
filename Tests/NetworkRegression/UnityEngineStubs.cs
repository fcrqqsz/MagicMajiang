// The console regression runner intentionally compiles without Unity assemblies.
namespace UnityEngine
{
    public interface ISerializationCallbackReceiver
    {
        void OnBeforeSerialize();
        void OnAfterDeserialize();
    }

    public sealed class SerializeField : System.Attribute { }
    public sealed class HideInInspector : System.Attribute { }

    public static class Mathf
    {
        public static int Min(int left, int right) => System.Math.Min(left, right);
        public static int Max(int left, int right) => System.Math.Max(left, right);
        public static int Abs(int value) => System.Math.Abs(value);
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public static class JsonUtility
    {
        private static readonly System.Text.Json.JsonSerializerOptions Options = new System.Text.Json.JsonSerializerOptions
        {
            IncludeFields = true
        };

        public static string ToJson(object value) => System.Text.Json.JsonSerializer.Serialize(value, Options);
        public static T FromJson<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json, Options);
    }
}

namespace MahjongGame.Core.Network.Transport
{
    public class GameEndpoint
    {
        public readonly System.Collections.Generic.List<string> SentMessages = new System.Collections.Generic.List<string>();
        public static event System.Action<string, GameEndpoint, long> OnClientConnected;
        public static event System.Action<string, string, GameEndpoint, long> OnMessageReceived;
        public static event System.Action<string, GameEndpoint, long> OnClientDisconnected;

        public void SendMessage(string message) => SentMessages.Add(message);

        public void Connect(string connectionId, long generation) =>
            OnClientConnected?.Invoke(connectionId, this, generation);

        public void Receive(string connectionId, long generation, string message) =>
            OnMessageReceived?.Invoke(connectionId, message, this, generation);

        public void Disconnect(string connectionId, long generation) =>
            OnClientDisconnected?.Invoke(connectionId, this, generation);
    }
}

namespace MahjongGame.Core.Agents
{
    public abstract class StubPlayerClient : IPlayerClient
    {
        public int PlayerId { get; protected set; }
        public System.Threading.CancellationToken TurnCancellationToken { get; set; }
        public void OnGameStart(System.Collections.Generic.List<MahjongGame.Core.TileData> startingHand) { }
        public void OnTileDrawn(MahjongGame.Core.TileData drawnTile) { }
        public void OnPlayerDrawn(int playerId) { }
        public void OnTurnWithoutDraw() { }
        public void OnWallCountChanged(int remainingCount) { }
        public void OnOtherPlayerDiscarded(int playerId, MahjongGame.Core.TileData discardedTile) { }
        public void OnAddedKongDeclared(int playerId, MahjongGame.Core.TileData targetTile) { }
        public void OnActionResolved(int playerId, MahjongGame.Core.Network.ClientActionType actionType, MahjongGame.Core.TileData targetTile, int[] chiCombinations = null) { }
        public void OnDrawGame() { }
        public void OnPlayerWin(int playerId, int totalFan, System.Collections.Generic.List<string> fanDetails, bool isSelfDraw,
            MahjongGame.Core.Network.Messages.WinKind winKind, int loserId, MahjongGame.Core.Network.Messages.WinningHandSnapshot winningHand) { }
        public void OnRoundStart(int roundNumber, MahjongGame.Core.WindDirection prevalentWind, MahjongGame.Core.WindDirection seatWind, int dealerIndex) { }
        public void OnSessionEnd(int[] finalScores) { }
        public void OnTimeout(MahjongGame.Core.TileData autoDiscardedTile) { }
        public void OnTalentInfo(MahjongGame.Core.ScoringOptions scoringOptions) { }
        public void OnPeekWallTiles(System.Collections.Generic.List<MahjongGame.Core.TileData> topTiles) { }
    }

    public sealed class SimpleAIClient : StubPlayerClient
    {
        public SimpleAIClient(int playerId, object server) => PlayerId = playerId;
        public void SetServer(MahjongGame.Core.Network.GameServer server) { }
    }
}

namespace MahjongGame.Core.Network
{
    using MahjongGame.Core.Agents;
    using MahjongGame.Core.Network.Messages;

    public sealed class StableSeatController : StubPlayerClient
    {
        private readonly SeatMessageStream _messageStream;

        public StableSeatController(int playerId, SeatMessageStream messageStream, GameSession session,
            System.Func<bool> isOnline, System.Action<DecisionControllerKind> controllerChanged)
        {
            PlayerId = playerId;
            _messageStream = messageStream;
        }

        public bool IsAiControllingActiveDecision => false;
        public bool IsHumanSubmissionAllowed(long decisionId) => true;
        public void MarkOffline() { }
        public void MarkOnline() { }
        public void SetPermanentAi() { }
        public void SetSession(GameSession session) { }
        public void SetServer(GameServer server) { }
        public new void OnSessionEnd(int[] finalScores) => _messageStream.Send(
            "SessionEnd",
            new SessionEndMessage { scores = finalScores });
    }

    public sealed class GameServerOptions
    {
        public NetworkDecisionTracker DecisionTracker;
    }

    public sealed class GameServer
    {
        public static readonly System.Collections.Generic.List<MahjongGame.Talents.TalentMatchRuntime>
            ReceivedTalentRuntimes = new();

        public GameServer(MahjongGame.Core.Interfaces.IWallService wallService, GameServerOptions options)
        {
            ReceivedTalentRuntimes.Add(null);
        }

        public GameServer(
            MahjongGame.Core.Interfaces.IWallService wallService,
            MahjongGame.Talents.TalentMatchRuntime talentRuntime,
            GameServerOptions options)
        {
            TalentRuntime = talentRuntime;
            ReceivedTalentRuntimes.Add(talentRuntime);
        }

        private readonly GameRoundCompletionLatch _completionLatch = new();

        public static StubGameStartFailure NextStartFailure { get; set; }
        public event System.Action<GameRoundCompletion> OnRoundFinished;
        public event System.Action OnTalentEventsAvailable;
        public MahjongGame.Talents.TalentMatchRuntime TalentRuntime { get; }
        public NetworkDecisionContext ActiveDecision => null;
        public int RemainingWallCount => 0;
        public MahjongGame.Core.TileData LastDrawnTile => null;
        public int WinnerId { get; private set; } = -1;
        public int WinFan { get; private set; }
        public System.Collections.Generic.List<string> WinFanDetails => new();
        public bool WinIsSelfDraw { get; private set; }
        public WinKind WinResultKind { get; private set; } = WinKind.Unknown;
        public int LoserId { get; private set; } = -1;
        public bool IsDrawGame { get; private set; }
        public WinningHandSnapshot WinningHandSnapshot => null;
        public int CompletionNotifications { get; private set; }

        public bool SubmitNetworkAction(int seatIndex, long decisionId, ClientAction action, out string errorCode)
        {
            errorCode = NetworkErrorCodes.NoActiveDecision;
            return false;
        }

        public System.Collections.Generic.List<MahjongGame.Core.TileData> GetHandSnapshot(int seatIndex) => new();
        public System.Collections.Generic.List<MahjongGame.Core.Meld> GetMeldSnapshot(int seatIndex) => new();
        public System.Collections.Generic.List<MahjongGame.Core.TileData> GetRiverSnapshot(int seatIndex) => new();
        public MahjongGame.Core.ScoringOptions GetScoringOptionsSnapshot(int seatIndex) =>
            TalentRuntime?.BuildScoringOptions(new MahjongGame.Talents.TalentScoringContext(_session, seatIndex))
            ?? new MahjongGame.Core.ScoringOptions();
        public System.Collections.Generic.List<MahjongGame.Core.TileData> GetPeekWallSnapshot(int seatIndex) =>
            TalentRuntime?.GetPrivatePeekTiles(seatIndex).ToList()
            ?? new System.Collections.Generic.List<MahjongGame.Core.TileData>();
        public void StartGame(System.Collections.Generic.List<IPlayerClient> clients,
            System.Collections.Generic.List<MahjongGame.Core.DeckConfig> deckConfigs,
            GameSession session,
            System.Collections.Generic.Dictionary<int, MahjongGame.Talents.TalentSlotConfig> talentConfigs) =>
            BeginReadyRound(session);
        public void StartGame(System.Collections.Generic.List<IPlayerClient> clients,
            System.Collections.Generic.List<MahjongGame.Core.DeckConfig> deckConfigs,
            GameSession session) => BeginReadyRound(session);
        public void StopGame() { }

        public void CompleteDrawRound()
        {
            IsDrawGame = true;
            WinnerId = -1;
            Complete(GameRoundCompletionKind.Draw);
        }

        public static void ResetObservations()
        {
            ReceivedTalentRuntimes.Clear();
            NextStartFailure = StubGameStartFailure.None;
        }

        private GameSession _session;

        private void BeginReadyRound(GameSession session)
        {
            _session = session;
            if (TalentRuntime == null) return;

            if (NextStartFailure == StubGameStartFailure.Startup)
            {
                TalentRuntime.BeginRound(new MahjongGame.Talents.TalentRoundContext(session));
                Complete(
                    GameRoundCompletionKind.Aborted,
                    new System.InvalidOperationException("startup failure"));
                return;
            }

            var wall = new System.Collections.Generic.List<MahjongGame.Core.TileData>
            {
                new(MahjongGame.Core.Suit.Man, 1, 0),
                new(MahjongGame.Core.Suit.Pin, 2, 1),
                new(MahjongGame.Core.Suit.Sou, 3, 2),
                new(MahjongGame.Core.Suit.Wind, 4, 3),
                new(MahjongGame.Core.Suit.Dragon, 1, 0)
            };
            TalentRuntime.BeginRound(new MahjongGame.Talents.TalentRoundContext(session));
            TalentRuntime.ApplyWallBuilding(new MahjongGame.Talents.TalentWallContext(session, wall));
            TalentRuntime.ResolvePostShuffle(new MahjongGame.Talents.TalentPostShuffleContext(session, wall));
            OnTalentEventsAvailable?.Invoke();
            if (NextStartFailure == StubGameStartFailure.Loop)
            {
                Complete(
                    GameRoundCompletionKind.Aborted,
                    new System.InvalidOperationException("loop failure"));
            }
        }

        private void Complete(GameRoundCompletionKind kind, System.Exception error = null)
        {
            if (!_completionLatch.TryComplete(kind, error, out GameRoundCompletion completion)) return;
            CompletionNotifications++;
            OnRoundFinished?.Invoke(completion);
        }
    }

    public enum StubGameStartFailure
    {
        None,
        Startup,
        Loop
    }
}
