using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Network
{
    /// <summary>Ephemeral authoritative input used to build one requesting seat's safe snapshot.</summary>
    public sealed class RoomGameSnapshotSource
    {
        public string RoomId;
        public RoomState RoomState;
        public GameMode GameMode;
        public AlienationPreset AlienationPreset;
        public int OwnTotalAlienation;
        public RoomSnapshotSeatSource[] Seats;
        public GameSession Session;
        public List<TileData>[] Hands;
        public List<Meld>[] Melds;
        public List<TileData>[] Rivers;
        public int RemainingWallCount;
        public ScoringOptions[] ScoringOptions;
        public List<TileData>[] PeekWallTiles;
        public NetworkDecisionContext ActiveDecision;
        /// <summary>The authoritative draw that opened the current main decision, if any.</summary>
        public TileData MainTurnDrawnTile;
        public int WinnerId = -1;
        public int WinFan;
        public string[] FanDetails;
        public bool WinIsSelfDraw;
        public WinKind WinKind;
        public int LoserId = -1;
        public bool IsDrawGame;
        public WinningHandSnapshot WinningHand;
    }

    public sealed class RoomSnapshotSeatSource
    {
        public int SeatIndex;
        public bool IsOccupied;
        public bool IsAi;
        public bool IsOnline;
        public string DisplayName;
        public string Controller;
    }

    /// <summary>Builds a per-seat snapshot and deliberately contains no deck or talent configuration fields.</summary>
    public static class RoomGameSnapshotBuilder
    {
        public static RoomGameSnapshot Build(RoomGameSnapshotSource source, int requestingSeatIndex)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (requestingSeatIndex < 0 || requestingSeatIndex >= 4) throw new ArgumentOutOfRangeException(nameof(requestingSeatIndex));

            var session = source.Session;
            bool isCompletedSession = session?.IsSessionOver() ?? false;
            int projectedDealerIndex = GetProjectedDealerIndex(session, isCompletedSession);
            var winResult = source.WinnerId >= 0
                ? WinResultNormalizer.Normalize(source.WinKind, source.WinIsSelfDraw, source.LoserId, true)
                : new NormalizedWinResult(WinKind.Unknown, -1);
            var snapshot = new RoomGameSnapshot
            {
                roomId = source.RoomId,
                roomState = (int)source.RoomState,
                gameMode = (int)source.GameMode,
                alienationPreset = (int)source.AlienationPreset,
                requestingSeatIndex = requestingSeatIndex,
                seats = BuildSeats(source, requestingSeatIndex),
                roundNumber = GetProjectedRoundNumber(session, isCompletedSession),
                prevalentWind = session != null ? (int)GetProjectedPrevalentWind(session, isCompletedSession) : 0,
                requestingSeatWind = session != null ? (int)GetSeatWind(requestingSeatIndex, projectedDealerIndex) : 0,
                dealerIndex = projectedDealerIndex,
                scores = session?.Scores?.ToArray() ?? Array.Empty<int>(),
                privateSeat = new SnapshotPrivateSeat
                {
                    seatIndex = requestingSeatIndex,
                    ownTotalAlienation = source.OwnTotalAlienation,
                    concealedHand = ToSimpleTiles(GetAt(source.Hands, requestingSeatIndex)),
                    melds = ToMeldSnapshots(GetAt(source.Melds, requestingSeatIndex)),
                    scoringOptions = ToScoringOptions(GetAt(source.ScoringOptions, requestingSeatIndex)),
                    peekWallTiles = ToSimpleTiles(GetAt(source.PeekWallTiles, requestingSeatIndex))
                },
                rivers = Enumerable.Range(0, 4)
                    .Select(seatIndex => new SeatRiverSnapshot
                    {
                        seatIndex = seatIndex,
                        tiles = ToSimpleTiles(GetAt(source.Rivers, seatIndex))
                    })
                    .ToArray(),
                remainingWallCount = source.RemainingWallCount,
                activeDecision = CreateDecisionSnapshot(source.ActiveDecision),
                mainTurnDrawnTile = IsRequestingSeatMainTurn(source.ActiveDecision, requestingSeatIndex)
                    ? ToSimpleTile(source.MainTurnDrawnTile)
                    : null,
                result = new RoundResultSnapshot
                {
                    winnerId = source.WinnerId,
                    fanCount = source.WinFan,
                    fanDetails = source.FanDetails?.ToArray() ?? Array.Empty<string>(),
                    isSelfDraw = winResult.IsSelfDraw,
                    winKind = winResult.Kind,
                    loserId = winResult.LoserId,
                    isDrawGame = source.IsDrawGame,
                    isSessionOver = session?.IsSessionOver() ?? false,
                    winningHand = source.WinnerId >= 0
                        ? WinningHandSnapshotCodec.Normalize(source.WinningHand)
                        : null
                }
            };

            return snapshot;
        }

        private static int GetProjectedRoundNumber(GameSession session, bool isCompletedSession)
        {
            if (session == null) return 0;
            return isCompletedSession ? Math.Max(1, session.TotalRoundsPlayed) : session.TotalRoundsPlayed + 1;
        }

        private static int GetProjectedDealerIndex(GameSession session, bool isCompletedSession)
        {
            if (session == null) return -1;
            return isCompletedSession && session.TotalRoundsPlayed > 0
                ? (session.DealerIndex + 3) % 4
                : session.DealerIndex;
        }

        private static WindDirection GetProjectedPrevalentWind(GameSession session, bool isCompletedSession)
        {
            if (!isCompletedSession || session.TotalRoundsPlayed <= 0 || session.RoundInWind != 0)
                return session.PrevalentWind;

            int previousWind = (int)session.PrevalentWind - 1;
            return (WindDirection)(previousWind < (int)WindDirection.East ? (int)WindDirection.North : previousWind);
        }

        private static WindDirection GetSeatWind(int seatIndex, int dealerIndex)
        {
            int offset = (seatIndex - dealerIndex + 4) % 4;
            return (WindDirection)(offset + 1);
        }

        private static bool IsRequestingSeatMainTurn(NetworkDecisionContext decision, int requestingSeatIndex)
        {
            return decision != null
                && decision.Phase == NetworkDecisionPhase.MainTurn
                && decision.ActingSeatIndex == requestingSeatIndex;
        }

        private static RoomSnapshotSeat[] BuildSeats(RoomGameSnapshotSource source, int requestingSeatIndex)
        {
            return Enumerable.Range(0, 4).Select(seatIndex =>
            {
                var sourceSeat = GetAt(source.Seats, seatIndex);
                return new RoomSnapshotSeat
                {
                    seatIndex = seatIndex,
                    isOccupied = sourceSeat?.IsOccupied ?? false,
                    isAi = sourceSeat?.IsAi ?? false,
                    isOnline = sourceSeat?.IsOnline ?? false,
                    displayName = sourceSeat?.DisplayName,
                    controller = sourceSeat == null || !sourceSeat.IsOccupied ? "None" : sourceSeat.Controller ?? (sourceSeat.IsAi ? "PermanentAi" : "OnlineHuman"),
                    concealedTileCount = GetAt(source.Hands, seatIndex)?.Count ?? 0,
                    publicMelds = ToMeldSnapshots(GetAt(source.Melds, seatIndex))
                };
            }).ToArray();
        }

        private static SnapshotScoringOptions ToScoringOptions(ScoringOptions options)
        {
            return new SnapshotScoringOptions
            {
                bonusFan = options?.BonusFan ?? 0,
                relaxedPureStraight = options?.RelaxedPureStraight ?? false
            };
        }

        public static SnapshotDecision CreateDecisionSnapshot(NetworkDecisionContext decision)
        {
            if (decision == null) return null;
            return new SnapshotDecision
            {
                decisionId = decision.DecisionId,
                phase = (int)decision.Phase,
                actingSeatIndex = decision.ActingSeatIndex,
                discardingSeatIndex = decision.DiscardingSeatIndex,
                targetTile = ToSimpleTile(decision.TargetTile),
                eligibleSeats = decision.EligibleSeats?.ToArray() ?? Array.Empty<int>(),
                submittedSeats = decision.SubmittedSeats?.ToArray() ?? Array.Empty<int>(),
                controllerSeatIndex = decision.ControllerSeatIndex,
                deadlineUnixMilliseconds = decision.DeadlineUnixMilliseconds
            };
        }

        private static SnapshotMeld[] ToMeldSnapshots(List<Meld> melds)
        {
            return (melds ?? new List<Meld>()).Where(meld => meld != null).Select(meld => new SnapshotMeld
            {
                meldType = (int)meld.Type,
                sourceSeatIndex = meld.SourcePlayerID,
                isConcealed = meld.IsConcealed,
                tileCount = meld.Tiles?.Count ?? 0,
                // MCR declares the tile value of a concealed kong; only non-melded
                // opponent hand tiles remain private.
                tiles = ToSimpleTiles(meld.Tiles)
            }).ToArray();
        }

        private static SimpleTileData[] ToSimpleTiles(IEnumerable<TileData> tiles)
        {
            return (tiles ?? Enumerable.Empty<TileData>()).Select(ToSimpleTile).Where(tile => tile != null).ToArray();
        }

        private static SimpleTileData ToSimpleTile(TileData tile)
        {
            return tile == null ? null : new SimpleTileData(tile);
        }

        private static T GetAt<T>(T[] values, int index) where T : class
        {
            return values != null && index >= 0 && index < values.Length ? values[index] : null;
        }
    }
}

namespace MahjongGame.Core.Network.Messages
{
    [Serializable]
    public sealed class RoomGameSnapshot
    {
        public string roomId;
        public int roomState;
        public int gameMode;
        public int alienationPreset;
        public int requestingSeatIndex;
        public RoomSnapshotSeat[] seats;
        public int roundNumber;
        public int prevalentWind;
        public int requestingSeatWind;
        public int dealerIndex;
        public int[] scores;
        public SnapshotPrivateSeat privateSeat;
        public SeatRiverSnapshot[] rivers;
        public int remainingWallCount;
        public SnapshotDecision activeDecision;
        public SimpleTileData mainTurnDrawnTile;
        public RoundResultSnapshot result;
    }

    [Serializable]
    public sealed class SeatRiverSnapshot
    {
        public int seatIndex;
        public SimpleTileData[] tiles;
    }

    [Serializable]
    public sealed class RoomSnapshotSeat
    {
        public int seatIndex;
        public bool isOccupied;
        public bool isAi;
        public bool isOnline;
        public string controller;
        public string displayName;
        public int concealedTileCount;
        public SnapshotMeld[] publicMelds;
    }

    [Serializable]
    public sealed class SnapshotPrivateSeat
    {
        public int seatIndex;
        public int ownTotalAlienation;
        public SimpleTileData[] concealedHand;
        public SnapshotMeld[] melds;
        public SnapshotScoringOptions scoringOptions;
        public SimpleTileData[] peekWallTiles;
    }

    [Serializable]
    public sealed class SnapshotMeld
    {
        public int meldType;
        public int sourceSeatIndex;
        public bool isConcealed;
        public int tileCount;
        public SimpleTileData[] tiles;
    }

    [Serializable]
    public sealed class SnapshotScoringOptions
    {
        public int bonusFan;
        public bool relaxedPureStraight;
    }

    [Serializable]
    public sealed class SnapshotDecision
    {
        public long decisionId;
        public int phase;
        public int actingSeatIndex;
        public int discardingSeatIndex;
        public SimpleTileData targetTile;
        public int[] eligibleSeats;
        public int[] submittedSeats;
        public int controllerSeatIndex;
        public long deadlineUnixMilliseconds;
    }

    [Serializable]
    public sealed class RoundResultSnapshot
    {
        public int winnerId;
        public int fanCount;
        public string[] fanDetails;
        public bool isSelfDraw;
        public WinKind winKind;
        public int loserId = -1;
        public bool isDrawGame;
        public bool isSessionOver;
        public WinningHandSnapshot winningHand;
    }
}
