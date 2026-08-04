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

namespace MahjongGame.Talents
{
    public sealed class TalentContext
    {
        public bool IsOwnersTurn { get; set; }
    }
}
namespace MahjongGame.Core.Network.Transport
{
    public class GameEndpoint
    {
        public readonly System.Collections.Generic.List<string> SentMessages = new System.Collections.Generic.List<string>();
        public void SendMessage(string message) => SentMessages.Add(message);
    }
}
