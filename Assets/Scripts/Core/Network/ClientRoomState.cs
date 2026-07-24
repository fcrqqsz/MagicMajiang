using System;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Network
{
    public enum ClientSequenceDisposition
    {
        Accepted,
        IgnoredDuplicate,
        ResyncRequired
    }

    /// <summary>Applies one room-lifetime, one-based server message sequence on the client.</summary>
    public sealed class ClientSequenceGate
    {
        public int LastSequence { get; private set; }
        public bool IsResyncRequired { get; private set; }

        public ClientSequenceDisposition Apply(int sequence)
        {
            if (sequence <= LastSequence) return ClientSequenceDisposition.IgnoredDuplicate;
            if (IsResyncRequired) return ClientSequenceDisposition.ResyncRequired;

            if (sequence == LastSequence + 1)
            {
                LastSequence = sequence;
                return ClientSequenceDisposition.Accepted;
            }

            IsResyncRequired = true;
            return ClientSequenceDisposition.ResyncRequired;
        }

        public void Reset()
        {
            LastSequence = 0;
            IsResyncRequired = false;
        }

        /// <summary>Accepts an authoritative reconnect snapshot as the new stream baseline.</summary>
        public void RestoreBaseline(int baselineSequence)
        {
            LastSequence = Math.Max(0, baselineSequence);
            IsResyncRequired = false;
        }
    }

    /// <summary>Active room binding plus the immutable snapshot needed by a completed session's result UI.</summary>
    public sealed class ClientRoomState
    {
        public string RoomId { get; private set; }
        public int SeatIndex { get; private set; } = -1;
        public GameMode GameMode { get; private set; } = GameMode.Single;
        public int RoomStateValue { get; private set; }
        public bool AiFillEnabled { get; private set; }
        public int AcceptedSchemaVersion { get; private set; }
        public int AcceptedTotalAlienation { get; private set; }
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
            AiFillEnabled = joined.aiFillEnabled;
            AcceptedSchemaVersion = joined.acceptedSchemaVersion;
            AcceptedTotalAlienation = joined.acceptedTotalAlienation;
            Seats = CloneSeats(joined.seats);
        }

        public void SetRoomState(int roomState) => RoomStateValue = roomState;

        public void ApplyRecoverySnapshot(Messages.RoomGameSnapshot snapshot)
        {
            if (snapshot == null) return;

            RoomId = snapshot.roomId;
            SeatIndex = snapshot.requestingSeatIndex;
            GameMode = (GameMode)snapshot.gameMode;
            RoomStateValue = snapshot.roomState;
            Seats = (snapshot.seats ?? Array.Empty<Messages.RoomSnapshotSeat>()).Select(seat => seat == null ? null : new RoomSeatMessage
            {
                seatIndex = seat.seatIndex,
                isOccupied = seat.isOccupied,
                isAi = seat.isAi,
                isOnline = seat.isOnline,
                isTemporarilyAiControlled = seat.controller == "AiControlled",
                controlState = seat.controller,
                displayName = seat.displayName
            }).ToArray();
        }

        public void SetSeats(RoomSeatMessage[] seats) => Seats = CloneSeats(seats);

        public bool ApplySeatUpdate(RoomSeatMessage seat)
        {
            if (seat == null || seat.seatIndex < 0 || seat.seatIndex > 3) return false;

            var snapshot = (RoomSeatMessage[])Seats.Clone();
            if (snapshot.Length != 4) snapshot = new RoomSeatMessage[4];
            snapshot[seat.seatIndex] = seat;
            SetSeats(snapshot);
            return true;
        }

        public void CompleteSession()
        {
            if (!HasRoom) return;

            IsSessionCompleted = true;
            ResultRoomId = RoomId;
            ResultSeatIndex = SeatIndex;
            ResultSeats = CloneSeats(Seats);
            RoomStateValue = (int)RoomState.SessionCompleted;
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
            AiFillEnabled = false;
            AcceptedSchemaVersion = 0;
            AcceptedTotalAlienation = 0;
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
                    isOnline = seat.isOnline,
                    isTemporarilyAiControlled = seat.isTemporarilyAiControlled,
                    controlState = seat.controlState,
                    isReady = seat.isReady,
                    displayName = seat.displayName,
                    totalAlienation = seat.totalAlienation
                };
            }
            return clone;
        }
    }
}
