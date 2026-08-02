using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Network
{
    public readonly struct NormalizedWinResult
    {
        public WinKind Kind { get; }
        public bool IsSelfDraw => Kind == WinKind.SelfDraw;
        public int LoserId { get; }

        public NormalizedWinResult(WinKind kind, int loserId)
        {
            Kind = kind;
            LoserId = loserId;
        }
    }

    public static class WinResultNormalizer
    {
        public static NormalizedWinResult Normalize(
            WinKind kind,
            bool legacyIsSelfDraw,
            int loserId,
            bool acceptLegacyLoserId = false)
        {
            bool hasExplicitKind = kind == WinKind.Discard
                || kind == WinKind.SelfDraw
                || kind == WinKind.RobKong;
            WinKind normalizedKind = hasExplicitKind
                ? kind
                : legacyIsSelfDraw ? WinKind.SelfDraw : WinKind.Discard;
            int normalizedLoserId = (hasExplicitKind || acceptLegacyLoserId)
                && normalizedKind != WinKind.SelfDraw
                && loserId >= 0
                && loserId < 4
                    ? loserId
                    : -1;
            return new NormalizedWinResult(normalizedKind, normalizedLoserId);
        }
    }
}
