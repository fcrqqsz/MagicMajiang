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
        public static int Abs(int value) => System.Math.Abs(value);
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
    }
}

namespace MahjongGame.Core.Fan { public sealed class FanContext { } }
namespace MahjongGame.Core.Network { public sealed class ServerGameState { } }
namespace MahjongGame.Talents { public sealed class TalentContext { } }
