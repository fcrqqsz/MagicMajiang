using System;
using MahjongGame.Core;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Network
{
    /// <summary>Active room binding plus the immutable snapshot needed by a completed session's result UI.</summary>
    public sealed class ClientRoomState
    {
        public string RoomId { get; private set; }
        public int SeatIndex { get; private set; } = -1;
        public GameMode GameMode { get; private set; } = GameMode.Single;
        public int RoomStateValue { get; private set; }
        public RoomSeatMessage[] Seats { get; private set; } = Array.Empty<RoomSeatMessage>();
        public bool HasRoom => !string.IsNullOrEmpty(RoomId) && SeatIndex >= 0;

        public bool IsSessionCompleted { get; private set; }
        public string ResultRoomId { get; private set; }
        public int ResultSeatIndex { get; private set; } = -1;
        public RoomSeatMessage[] ResultSeats { get; private set; } = Array.Empty<RoomSeatMessage>();

        public void ApplyJoined(RoomJoinedMessage joined)
        {
            if (joined == null) return;

            ClearCompletedSession();
            RoomId = joined.roomId;
            SeatIndex = joined.seatIndex;
            GameMode = (GameMode)joined.gameMode;
            RoomStateValue = joined.roomState;
            Seats = CloneSeats(joined.seats);
        }

        public void SetRoomState(int roomState) => RoomStateValue = roomState;

        public void SetSeats(RoomSeatMessage[] seats) => Seats = CloneSeats(seats);

        public void CompleteSession()
        {
            if (!HasRoom) return;

            IsSessionCompleted = true;
            ResultRoomId = RoomId;
            ResultSeatIndex = SeatIndex;
            ResultSeats = CloneSeats(Seats);
            ClearActiveRoom();
        }

        public void Reset()
        {
            ClearActiveRoom();
            ClearCompletedSession();
        }

        private void ClearActiveRoom()
        {
            RoomId = null;
            SeatIndex = -1;
            GameMode = GameMode.Single;
            RoomStateValue = 0;
            Seats = Array.Empty<RoomSeatMessage>();
        }

        private void ClearCompletedSession()
        {
            IsSessionCompleted = false;
            ResultRoomId = null;
            ResultSeatIndex = -1;
            ResultSeats = Array.Empty<RoomSeatMessage>();
        }

        private static RoomSeatMessage[] CloneSeats(RoomSeatMessage[] seats)
        {
            if (seats == null || seats.Length == 0) return Array.Empty<RoomSeatMessage>();

            var clone = new RoomSeatMessage[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                var seat = seats[i];
                if (seat == null) continue;
                clone[i] = new RoomSeatMessage
                {
                    seatIndex = seat.seatIndex,
                    isOccupied = seat.isOccupied,
                    isAi = seat.isAi,
                    isReady = seat.isReady,
                    displayName = seat.displayName
                };
            }
            return clone;
        }
    }
}
