using System.Text;

namespace MahjongGame.Core.Network
{
    /// <summary>Shared limits enforced before inbound client text is deserialized.</summary>
    public static class NetworkMessageLimits
    {
        public const int MaximumInboundClientTextBytes = 64 * 1024;

        public static bool IsWithinClientTextLimit(string message) =>
            message != null && Encoding.UTF8.GetByteCount(message) <= MaximumInboundClientTextBytes;
    }
}
