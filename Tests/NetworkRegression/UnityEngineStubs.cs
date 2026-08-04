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
        public StableSeatController(int playerId, SeatMessageStream messageStream, GameSession session,
            System.Func<bool> isOnline, System.Action<DecisionControllerKind> controllerChanged) => PlayerId = playerId;

        public bool IsAiControllingActiveDecision => false;
        public bool IsHumanSubmissionAllowed(long decisionId) => true;
        public void MarkOffline() { }
        public void MarkOnline() { }
        public void SetPermanentAi() { }
        public void SetSession(GameSession session) { }
        public void SetServer(GameServer server) { }
    }

    public sealed class GameServerOptions
    {
        public NetworkDecisionTracker DecisionTracker;
    }

    public sealed class GameServer
    {
        public GameServer(MahjongGame.Core.Interfaces.IWallService wallService, GameServerOptions options) { }
        public event System.Action OnRoundFinished { add { } remove { } }
        public NetworkDecisionContext ActiveDecision => null;
        public int RemainingWallCount => 0;
        public MahjongGame.Core.TileData LastDrawnTile => null;
        public int WinnerId => -1;
        public int WinFan => 0;
        public System.Collections.Generic.List<string> WinFanDetails => new();
        public bool WinIsSelfDraw => false;
        public WinKind WinResultKind => WinKind.Unknown;
        public int LoserId => -1;
        public bool IsDrawGame => false;
        public WinningHandSnapshot WinningHandSnapshot => null;

        public bool SubmitNetworkAction(int seatIndex, long decisionId, ClientAction action, out string errorCode)
        {
            errorCode = NetworkErrorCodes.NoActiveDecision;
            return false;
        }

        public System.Collections.Generic.List<MahjongGame.Core.TileData> GetHandSnapshot(int seatIndex) => new();
        public System.Collections.Generic.List<MahjongGame.Core.Meld> GetMeldSnapshot(int seatIndex) => new();
        public System.Collections.Generic.List<MahjongGame.Core.TileData> GetRiverSnapshot(int seatIndex) => new();
        public MahjongGame.Core.ScoringOptions GetScoringOptionsSnapshot(int seatIndex) => new();
        public System.Collections.Generic.List<MahjongGame.Core.TileData> GetPeekWallSnapshot(int seatIndex) => new();
        public void StartGame(System.Collections.Generic.List<IPlayerClient> clients,
            System.Collections.Generic.List<MahjongGame.Core.DeckConfig> deckConfigs,
            GameSession session,
            System.Collections.Generic.Dictionary<int, MahjongGame.Talents.TalentSlotConfig> talentConfigs) { }
        public void StopGame() { }
    }
}
