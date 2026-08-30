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

    public sealed class GameObject
    {
        public GameObject(string name) { }
        public T AddComponent<T>() where T : new() => new T();
    }

    public static class Time
    {
        public static float unscaledTime { get; set; }
    }

    public static class Mathf
    {
        public static int Min(int left, int right) => System.Math.Min(left, right);
        public static int Max(int left, int right) => System.Math.Max(left, right);
        public static int Abs(int value) => System.Math.Abs(value);
        public static int Clamp(int value, int min, int max) => System.Math.Clamp(value, min, max);
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

namespace WebSocketSharp
{
    public enum WebSocketState
    {
        Connecting,
        Open,
        Closing,
        Closed
    }
}

namespace MahjongGame.Systems
{
    public sealed class ProfileManager
    {
        public static ProfileManager Instance { get; } = new ProfileManager();
        public MahjongGame.Core.Network.Data.PlayerProfile CurrentProfile { get; set; } =
            new MahjongGame.Core.Network.Data.PlayerProfile();
    }
}

namespace MahjongGame.Core.Network.Transport
{
    public sealed class WebSocketClient
    {
        public static WebSocketClient Instance { get; private set; }
        public WebSocketSharp.WebSocketState ReadyState { get; private set; } = WebSocketSharp.WebSocketState.Closed;
        public string ActiveAddress { get; private set; }
        public bool AutoCompleteConnect { get; set; } = true;
        public bool AutoCompleteSends { get; set; } = true;
        public readonly System.Collections.Generic.List<string> SentMessages = new();
        public readonly System.Collections.Generic.List<string> ConnectAddresses = new();
        public readonly System.Collections.Generic.Queue<bool> SendCompletionResults = new();
        private readonly System.Collections.Generic.Queue<(string Message, bool Completed)> _pendingSendCompletions = new();

        public event System.Action OnConnected;
        public event System.Action<string> OnMessageReceived;
        public event System.Action<string> OnDisconnected;
        public event System.Action<string> OnError;
        public event System.Action<string> OnMessageSent;
        public event System.Action<string> OnMessageSendFailed;

        public WebSocketClient() => Instance = this;
        public void Connect(string address)
        {
            ActiveAddress = address;
            ConnectAddresses.Add(address);
            ReadyState = WebSocketSharp.WebSocketState.Connecting;
            if (AutoCompleteConnect) CompleteConnect();
        }
        public void CompleteConnect()
        {
            ReadyState = WebSocketSharp.WebSocketState.Open;
            OnConnected?.Invoke();
        }
        public void SendNetworkMessage(string message)
        {
            SentMessages.Add(message);
            bool completed = SendCompletionResults.Count == 0 || SendCompletionResults.Dequeue();
            if (!AutoCompleteSends)
            {
                _pendingSendCompletions.Enqueue((message, completed));
                return;
            }
            if (completed)
                OnMessageSent?.Invoke(message);
            else
                OnMessageSendFailed?.Invoke(message);
        }
        public void CompleteNextSend()
        {
            if (_pendingSendCompletions.Count == 0) return;
            (string message, bool completed) = _pendingSendCompletions.Dequeue();
            if (completed) OnMessageSent?.Invoke(message);
            else OnMessageSendFailed?.Invoke(message);
        }
        public void Disconnect()
        {
            bool wasConnected = ReadyState != WebSocketSharp.WebSocketState.Closed;
            ReadyState = WebSocketSharp.WebSocketState.Closed;
            ActiveAddress = null;
            if (wasConnected) OnDisconnected?.Invoke(string.Empty);
        }
        public void Receive(string message) => OnMessageReceived?.Invoke(message);
        public void Fail(string message) => OnError?.Invoke(message);
        public static void ResetForTests() => Instance = null;
    }
}

namespace MahjongGame.Core.Network
{
    public interface IServer
    {
        void SubmitAction(ClientAction action);
    }

    public sealed class PlayerPrefsClientReconnectTicketStore : IClientReconnectTicketStore
    {
        private readonly InMemoryClientReconnectTicketStore _inner = new();
        public void Save(ClientReconnectTicket ticket) => _inner.Save(ticket);
        public bool TryLoad(out ClientReconnectTicket ticket) => _inner.TryLoad(out ticket);
        public void Clear() => _inner.Clear();
    }
}

namespace MahjongGame.Core
{
    public sealed class GameManager
    {
        public static GameManager Instance { get; set; }
        public MahjongGame.Core.Network.GameSession Session { get; set; }
    }
}

namespace MahjongGame.UI
{
    public sealed class GameHUDController
    {
        public static GameHUDController Instance { get; set; }
        public void UpdateRoundInfo(MahjongGame.Core.Network.GameSession session) { }
        public void UpdateScores(int[] scores) { }
        public void BindServerProxy(MahjongGame.Core.Network.RemoteServerProxy proxy) { }
        public void UnbindServerProxy(MahjongGame.Core.Network.RemoteServerProxy proxy) { }
        public void CloseTalentDrawers() { }
        public void RefreshTalentHudStatus() { }
    }

    public sealed class SideboardPanelController
    {
        public static SideboardPanelController Instance { get; set; }
        public void BindServerProxy(MahjongGame.Core.Network.RemoteServerProxy proxy) { }
        public void UnbindServerProxy(MahjongGame.Core.Network.RemoteServerProxy proxy) { }
        public void ApplyRecoverySnapshot(MahjongGame.Core.Network.Messages.SnapshotSideboardState state) { }
    }

    public sealed class ResultPanelController
    {
        public static ResultPanelController Instance { get; set; }
        public void SetSessionInfo(MahjongGame.Core.Network.GameSession session) { }
    }
}

namespace MahjongGame.Core.Network.Transport
{
    public class GameEndpoint
    {
        public readonly System.Collections.Generic.List<string> SentMessages = new System.Collections.Generic.List<string>();
        public System.Func<string, bool> SendFailure { get; set; }
        public static event System.Action<string, GameEndpoint, long> OnClientConnected;
        public static event System.Action<string, string, GameEndpoint, long> OnMessageReceived;
        public static event System.Action<string, GameEndpoint, long> OnClientDisconnected;

        public void SendMessage(string message)
        {
            if (SendFailure?.Invoke(message) == true)
                throw new System.InvalidOperationException("injected endpoint send failure");
            SentMessages.Add(message);
        }

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
        public void OnTileDrawn(MahjongGame.Core.TileData drawnTile, bool isKongReplacementDraw) { }
        public void OnPlayerDrawn(int playerId) { }
        public void OnTurnWithoutDraw() { }
        public void OnWallCountChanged(int remainingCount) { }
        public void OnOtherPlayerDiscarded(int playerId, MahjongGame.Core.TileData discardedTile) { }
        public void OnAddedKongDeclared(int playerId, MahjongGame.Core.TileData targetTile) { }
        public void OnActionResolved(int playerId, MahjongGame.Core.Network.ClientActionType actionType, MahjongGame.Core.TileData targetTile, int[] chiCombinations = null) { }
        public void OnDrawGame() { }
        public void OnPlayerWin(int playerId, int totalFan, System.Collections.Generic.List<string> fanDetails, bool isSelfDraw,
            MahjongGame.Core.Network.Messages.WinKind winKind, int loserId,
            MahjongGame.Core.Network.Messages.WinningHandSnapshot winningHand,
            MahjongGame.Core.Network.Messages.TalentFanBreakdownMessage talentFanBreakdown) { }
        public void OnRoundStart(int roundNumber, MahjongGame.Core.WindDirection prevalentWind, MahjongGame.Core.WindDirection seatWind, int dealerIndex) { }
        public void OnSessionEnd(int[] finalScores) { }
        public void OnTimeout(MahjongGame.Core.TileData autoDiscardedTile) { }
        public void OnTalentInfo(MahjongGame.Core.ScoringOptions scoringOptions) { }
        public void OnPeekWallTiles(System.Collections.Generic.List<MahjongGame.Core.TileData> topTiles) { }
        public void OnPrivateTileReveal(MahjongGame.Talents.TalentPrivateTileReveal reveal) { }
        public void OnPrivateKnownTilesChanged(MahjongGame.Core.Network.PrivateKnownTilesProjection projection) { }
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
        public bool HumanSubmissionAllowed { get; set; } = true;
        public bool IsHumanSubmissionAllowed(long decisionId) => HumanSubmissionAllowed;
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
        public NetworkDecisionContext ActiveDecision { get; private set; }
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
        public TalentFanBreakdownMessage WinTalentFanBreakdown { get; private set; }
        public int CompletionNotifications { get; private set; }
        public int TalentActionSubmissionCount { get; private set; }
        public int LastTalentActionSeatIndex { get; private set; } = -1;
        public TalentActionMessage LastTalentActionMessage { get; private set; }
        public System.Collections.Generic.List<string> SubmittedTalentIds { get; } = new();
        private System.Collections.Generic.IReadOnlyList<MahjongGame.Talents.TalentActionOption>
            _availableTalentActions = System.Array.Empty<MahjongGame.Talents.TalentActionOption>();
        private System.Collections.Generic.Queue<System.Collections.Generic.IReadOnlyList<MahjongGame.Talents.TalentActionOption>>
            _availableTalentActionSequence;
        public MahjongGame.Talents.TalentActionResult NextTalentActionResult { get; set; } =
            MahjongGame.Talents.TalentActionResult.Reject(NetworkErrorCodes.NoActiveDecision);

        public bool SubmitNetworkAction(int seatIndex, long decisionId, ClientAction action, out string errorCode)
        {
            errorCode = NetworkErrorCodes.NoActiveDecision;
            return false;
        }

        public bool SubmitNetworkTalentAction(
            int seatIndex,
            TalentActionMessage message,
            out MahjongGame.Talents.TalentActionResult result)
        {
            TalentActionSubmissionCount++;
            LastTalentActionSeatIndex = seatIndex;
            LastTalentActionMessage = message;
            SubmittedTalentIds.Add(message?.talentId);
            result = NextTalentActionResult;
            if (_availableTalentActionSequence?.Count > 0)
                _availableTalentActions = _availableTalentActionSequence.Dequeue();
            return result?.Accepted == true;
        }

        public System.Collections.Generic.IReadOnlyList<MahjongGame.Talents.TalentActionOption>
            GetAvailableTalentActionsSnapshot(int seatIndex) =>
            ActiveDecision?.ActingSeatIndex == seatIndex
                ? _availableTalentActions
                : System.Array.Empty<MahjongGame.Talents.TalentActionOption>();

        public void SetAiTalentDecisionForTests(
            long decisionId,
            int actingSeatIndex,
            System.Collections.Generic.IReadOnlyList<MahjongGame.Talents.TalentActionOption> options,
            MahjongGame.Talents.TalentActionResult result)
        {
            ActiveDecision = new NetworkDecisionContext(
                decisionId,
                NetworkDecisionPhase.MainTurn,
                actingSeatIndex,
                -1,
                null,
                new[] { actingSeatIndex },
                System.Array.Empty<int>(),
                actingSeatIndex,
                long.MaxValue);
            _availableTalentActions = options
                ?? System.Array.Empty<MahjongGame.Talents.TalentActionOption>();
            NextTalentActionResult = result;
        }

        public void SetAiTalentDecisionSequenceForTests(
            long decisionId,
            int actingSeatIndex,
            System.Collections.Generic.IEnumerable<System.Collections.Generic.IReadOnlyList<MahjongGame.Talents.TalentActionOption>> optionSequence,
            MahjongGame.Talents.TalentActionResult result)
        {
            var sequence = new System.Collections.Generic.Queue<System.Collections.Generic.IReadOnlyList<MahjongGame.Talents.TalentActionOption>>(
                optionSequence ?? System.Array.Empty<System.Collections.Generic.IReadOnlyList<MahjongGame.Talents.TalentActionOption>>());
            SetAiTalentDecisionForTests(
                decisionId,
                actingSeatIndex,
                sequence.Count > 0 ? sequence.Dequeue() : System.Array.Empty<MahjongGame.Talents.TalentActionOption>(),
                result);
            _availableTalentActionSequence = sequence;
        }

        public System.Collections.Generic.List<MahjongGame.Core.TileData> GetHandSnapshot(int seatIndex) => new();
        public System.Collections.Generic.List<MahjongGame.Core.Meld> GetMeldSnapshot(int seatIndex) => new();
        public System.Collections.Generic.List<MahjongGame.Core.TileData> GetRiverSnapshot(int seatIndex) => new();
        public int[] GetDrawCountsSnapshot() => new int[4];
        public MahjongGame.Core.ScoringOptions GetScoringOptionsSnapshot(int seatIndex) =>
            TalentRuntime?.BuildScoringOptions(new MahjongGame.Talents.TalentScoringContext(_session, seatIndex))
            ?? new MahjongGame.Core.ScoringOptions();
        public System.Collections.Generic.List<MahjongGame.Core.TileData> GetPeekWallSnapshot(int seatIndex) =>
            TalentRuntime?.GetPrivatePeekTiles(seatIndex).ToList()
            ?? new System.Collections.Generic.List<MahjongGame.Core.TileData>();
        public PrivateKnownTilesProjection GetPrivateKnownTilesProjection(int seatIndex) =>
            new(seatIndex, System.Array.Empty<PrivateKnownHandProjection>());
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

        public void SetWinResult(
            int winnerId,
            int fan,
            TalentFanBreakdownMessage talentFanBreakdown)
        {
            WinnerId = winnerId;
            WinFan = fan;
            WinTalentFanBreakdown = TalentFanBreakdownMessage.Clone(talentFanBreakdown);
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
            TalentRuntime.CompleteInitialHands(new MahjongGame.Talents.TalentInitialHandsContext(
                session,
                new ServerGameState(4)));
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
