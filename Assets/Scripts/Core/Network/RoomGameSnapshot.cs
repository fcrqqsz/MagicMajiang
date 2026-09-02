using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

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
        public PrivateKnownTilesProjection PrivateKnownTiles;
        public NetworkDecisionContext ActiveDecision;
        public RoomSnapshotTalentSource[] Talents;
        public IReadOnlyList<TalentActionOption> AvailableTalentActions;
        public RoomSnapshotSideboardSource Sideboard;
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
        public TalentFanBreakdownMessage TalentFanBreakdown;
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

    public sealed class RoomSnapshotTalentSource
    {
        public int OwnerSeatIndex;
        public string TalentId;
        public bool IsActive;
        public bool IsRevealed;
        public int PrivateValue;
        public string PrivateStatusKey;
        public string LastPublicEventType;
        public int LastPublicValue;
    }

    public sealed class RoomSnapshotSideboardSource
    {
        public bool IsActive;
        public long DecisionId;
        public long DeadlineUnixMilliseconds;
        public bool OwnLocked;
        public bool[] SeatLocked;
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
                knownTalents = BuildKnownTalents(source, requestingSeatIndex),
                roundNumber = GetProjectedRoundNumber(session, isCompletedSession),
                prevalentWind = session != null ? (int)GetProjectedPrevalentWind(session, isCompletedSession) : 0,
                requestingSeatWind = session != null ? (int)GetSeatWind(requestingSeatIndex, projectedDealerIndex) : 0,
                dealerIndex = projectedDealerIndex,
                scores = session?.Scores?.ToArray() ?? Array.Empty<int>(),
                privateSeat = new SnapshotPrivateSeat
                {
                    seatIndex = requestingSeatIndex,
                    ownTotalAlienation = source.OwnTotalAlienation,
                    concealedHand = ToOwnerPrivateTiles(GetAt(source.Hands, requestingSeatIndex)),
                    melds = ToOwnerPrivateMeldSnapshots(GetAt(source.Melds, requestingSeatIndex)),
                    scoringOptions = ToScoringOptions(GetAt(source.ScoringOptions, requestingSeatIndex)),
                    peekWallTiles = ToOwnerPrivateTiles(GetAt(source.PeekWallTiles, requestingSeatIndex)),
                    privateTileReveal = null,
                    knownOpponentHands = CreateKnownHandSnapshots(source.PrivateKnownTiles, requestingSeatIndex),
                    ownTalents = BuildOwnTalents(source, requestingSeatIndex),
                    availableTalentActions = BuildTalentActionOptions(source.AvailableTalentActions)
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
                    ? ToOwnerPrivateTile(source.MainTurnDrawnTile)
                    : null,
                sideboard = BuildSideboard(source.Sideboard),
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
                    completedRounds = session?.TotalRoundsPlayed ?? 0,
                    sessionEndReason = session?.EndReason ?? SessionEndReason.None,
                    depletedSeatIndices = session?.DepletedSeatIndices?.ToArray() ?? Array.Empty<int>(),
                    winningHand = source.WinnerId >= 0
                        ? WinningHandSnapshotCodec.Normalize(source.WinningHand)
                        : null,
                    talentFanBreakdown = TalentFanBreakdownMessage.Clone(source.TalentFanBreakdown)
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
                minimumFan = options?.MinimumFan ?? 8,
                relaxedPureStraight = options?.RelaxedPureStraight ?? false
            };
        }

        private static SnapshotKnownTalent[] BuildKnownTalents(
            RoomGameSnapshotSource source,
            int requestingSeatIndex)
        {
            return (source.Talents ?? Array.Empty<RoomSnapshotTalentSource>())
                .Where(talent => talent != null
                                 && talent.OwnerSeatIndex != requestingSeatIndex
                                 && talent.IsRevealed
                                 && !string.IsNullOrWhiteSpace(talent.TalentId))
                .Select(talent => new SnapshotKnownTalent
                {
                    ownerSeatIndex = talent.OwnerSeatIndex,
                    talentId = talent.TalentId,
                    isKnown = true,
                    isActive = talent.IsActive,
                    lastPublicEventType = talent.LastPublicEventType,
                    lastPublicValue = talent.LastPublicValue
                })
                .ToArray();
        }

        private static SnapshotOwnTalent[] BuildOwnTalents(
            RoomGameSnapshotSource source,
            int requestingSeatIndex)
        {
            return (source.Talents ?? Array.Empty<RoomSnapshotTalentSource>())
                .Where(talent => talent != null
                                 && talent.OwnerSeatIndex == requestingSeatIndex
                                 && !string.IsNullOrWhiteSpace(talent.TalentId))
                .Select(talent => new SnapshotOwnTalent
                {
                    talentId = talent.TalentId,
                    isActive = talent.IsActive,
                    privateValue = talent.PrivateValue,
                    privateStatusKey = talent.PrivateStatusKey
                })
                .ToArray();
        }

        private static SnapshotTalentActionOption[] BuildTalentActionOptions(
            IReadOnlyList<TalentActionOption> options)
        {
            return (options ?? Array.Empty<TalentActionOption>())
                .Where(option => option != null && !string.IsNullOrWhiteSpace(option.TalentId))
                .Select(TalentActionSnapshotCodec.ToSnapshot)
                .Where(option => option != null)
                .ToArray();
        }

        private static SnapshotSideboardState BuildSideboard(RoomSnapshotSideboardSource source)
        {
            return new SnapshotSideboardState
            {
                isActive = source?.IsActive ?? false,
                decisionId = source?.DecisionId ?? 0,
                deadlineUnixMilliseconds = source?.DeadlineUnixMilliseconds ?? 0,
                ownLocked = source?.OwnLocked ?? false,
                seatLocked = source?.SeatLocked?.ToArray() ?? Array.Empty<bool>()
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
                deadlineUnixMilliseconds = decision.DeadlineUnixMilliseconds,
                isKongReplacementDraw = decision.IsKongReplacementDraw
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

        private static SnapshotMeld[] ToOwnerPrivateMeldSnapshots(List<Meld> melds)
        {
            return (melds ?? new List<Meld>()).Where(meld => meld != null).Select(meld => new SnapshotMeld
            {
                meldType = (int)meld.Type,
                sourceSeatIndex = meld.SourcePlayerID,
                isConcealed = meld.IsConcealed,
                tileCount = meld.Tiles?.Count ?? 0,
                tiles = ToOwnerPrivateTiles(meld.Tiles)
            }).ToArray();
        }

        private static SimpleTileData[] ToSimpleTiles(IEnumerable<TileData> tiles)
        {
            return (tiles ?? Enumerable.Empty<TileData>()).Select(ToSimpleTile).Where(tile => tile != null).ToArray();
        }

        private static SimpleTileData[] ToOwnerPrivateTiles(IEnumerable<TileData> tiles)
        {
            return (tiles ?? Enumerable.Empty<TileData>())
                .Select(ToOwnerPrivateTile)
                .Where(tile => tile != null)
                .ToArray();
        }

        private static SimpleTileData ToSimpleTile(TileData tile)
        {
            return tile == null ? null : new SimpleTileData(tile);
        }

        private static SimpleTileData ToOwnerPrivateTile(TileData tile)
        {
            return tile == null ? null : new SimpleTileData(tile, true);
        }

        private static SnapshotPrivateTileReveal CreatePrivateTileRevealSnapshot(TalentPrivateTileReveal reveal)
        {
            if (reveal == null) return null;
            return new SnapshotPrivateTileReveal
            {
                talentId = reveal.TalentId,
                viewerSeatIndex = reveal.ViewerSeatIndex,
                targetSeatIndex = reveal.TargetSeatIndex,
                roundNumber = reveal.RoundNumber,
                tiles = (reveal.Tiles ?? Array.Empty<TileData>())
                    .Select(t => new SnapshotRevealedTile
                    {
                        suit = (int)t.TileSuit,
                        value = t.Value,
                        isModified = t.IsModified
                    })
                    .ToArray()
            };
        }

        private static SnapshotKnownHand[] CreateKnownHandSnapshots(
            PrivateKnownTilesProjection projection,
            int requestingSeatIndex)
        {
            if (projection == null || projection.ViewerSeatIndex != requestingSeatIndex)
                return Array.Empty<SnapshotKnownHand>();

            return (projection.Hands ?? Array.Empty<PrivateKnownHandProjection>())
                .Where(hand => hand != null
                               && hand.TargetSeatIndex >= 0
                               && hand.TargetSeatIndex < 4
                               && hand.TargetSeatIndex != requestingSeatIndex)
                .Select(hand => new SnapshotKnownHand
                {
                    targetSeatIndex = hand.TargetSeatIndex,
                    tiles = (hand.Tiles ?? Array.Empty<PrivateKnownTileFace>())
                        .Where(tile => tile != null)
                        .Select(tile => new SnapshotKnownTile
                        {
                            suit = (int)tile.Suit,
                            value = tile.Value,
                            isModified = tile.IsModified
                        })
                        .ToArray()
                })
                .Where(hand => hand.tiles.Length > 0)
                .ToArray();
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
        public SnapshotKnownTalent[] knownTalents;
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
        public SnapshotSideboardState sideboard;
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
        public SnapshotPrivateTileReveal privateTileReveal;
        public SnapshotKnownHand[] knownOpponentHands;
        public SnapshotOwnTalent[] ownTalents;
        public SnapshotTalentActionOption[] availableTalentActions;
    }

    [Serializable]
    public sealed class SnapshotKnownTalent
    {
        public int ownerSeatIndex;
        public string talentId;
        public bool isKnown;
        public bool isActive;
        public string lastPublicEventType;
        public int lastPublicValue;
    }

    [Serializable]
    public sealed class SnapshotOwnTalent
    {
        public string talentId;
        public bool isActive;
        public int privateValue;
        public string privateStatusKey;
    }

    [Serializable]
    public sealed class SnapshotTalentActionOption
    {
        public string talentId;
        public int targetSeatIndex = -1;
        public string targetTalentId;
        public int targetPublicCharge;
        public int aiPriority;
        public SnapshotTalentChoiceSet choice;
    }

    [Serializable]
    public sealed class SnapshotTalentChoiceSet
    {
        public int kind;
        public string promptKey;
        public string defaultChoiceId;
        public SnapshotTalentChoiceOption[] options;
    }

    [Serializable]
    public sealed class SnapshotTalentChoiceOption
    {
        public string choiceId;
        public string displayKey;
        public int value;
        public SnapshotTalentTileFacts tile;
    }

    [Serializable]
    public sealed class SnapshotTalentTileFacts
    {
        public int suit;
        public int value;
        public string id;
        public int originalOwnerId;
        public bool isModified;
        public string specialEffectId;
        public bool isValid;
    }

    [Serializable]
    public sealed class SnapshotSideboardState
    {
        public bool isActive;
        public long decisionId;
        public long deadlineUnixMilliseconds;
        public bool ownLocked;
        public bool[] seatLocked;
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
        public int minimumFan = 8;
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
        public bool isKongReplacementDraw;
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
        public int completedRounds;
        public SessionEndReason sessionEndReason;
        public int[] depletedSeatIndices;
        public WinningHandSnapshot winningHand;
        public TalentFanBreakdownMessage talentFanBreakdown;
    }
}
