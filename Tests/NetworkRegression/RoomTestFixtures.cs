using MahjongGame.Core.Network;

internal static class RoomTestFixtures
{
    public static void FillEmptySeatsWithAi(
        Room room,
        string hostPlayerId,
        TrustedPlayerLoadout loadout,
        AiDifficulty difficulty = AiDifficulty.Beginner,
        AiLoadoutTemplate template = AiLoadoutTemplate.Stable)
    {
        for (int seatIndex = 0; seatIndex < room.Seats.Count; seatIndex++)
        {
            if (room.Seats[seatIndex] != null) continue;
            if (!room.TryAddAi(hostPlayerId, seatIndex, difficulty, template, loadout, out string errorCode))
                throw new InvalidOperationException($"Could not fill AI test seat {seatIndex}: {errorCode}");
        }
    }
}
