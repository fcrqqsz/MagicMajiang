using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Data;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

internal static class TalentFoundationTests
{
    public static void Run(RegressionRunner runner)
    {
        LegacySlotsNormalizeWithoutDiscardingMainLoadout(runner);
        CarriedIdsEnumerateMainBeforeReserve(runner);
        DuplicateValidationCoversAllCarriedSlots(runner);
        ReserveSlotsEnforceTheirFixedTiers(runner);
        ProfileNormalizationRepairsLegacyDeckTalentSchema(runner);
        MetadataPreservesDefaultsAndExplicitPolicies(runner);
        ExistingTalentsRemainRegistrable(runner);
        RuntimeRejectsMalformedAndDuplicateCarriedLoadouts(runner);
        RuntimeRejectsIllegalLifecycleOrderAndForeignSessions(runner);
        SeatScopedContextsRejectInvalidSeatIndices(runner);
        RoundOutcomeRejectsInvalidSeatIndices(runner);
        InactiveMatchInitializationCannotMutateAuthoritativeSession(runner);
        DrawContextSnapshotsGameStateAndOwnerDeck(runner);
        CrossRoundRuntimePreservesMatchStateAndResetsRoundState(runner);
        RuntimeEventsRespectVisibilityWithoutDestructiveReads(runner);
        PostShufflePeekIsPrivateAndUsesShuffledOrder(runner);
        DrawAndDiscardPipelinesKeepStablePriorityOrder(runner);
        ScoringOptionsAreFreshForEveryEvaluation(runner);
        LoadoutDecodingEnforcesRoomAlienationPresets(runner);
        ProfileSettingsNormalizeUnknownAlienationPreset(runner);
    }

    private static void LegacySlotsNormalizeWithoutDiscardingMainLoadout(RegressionRunner runner)
    {
        TalentSlotConfig legacy = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "midas_touch", null, null, null, null, null },
            ReserveTalentIds = null
        };

        legacy.Normalize();

        runner.Check(legacy.SlotTalentIds.Length == TalentSlotConfig.MainSlotCount,
            "legacy main slots normalize to six");
        runner.Check(legacy.ReserveTalentIds.Length == TalentSlotConfig.ReserveSlotCount,
            "legacy save without reserve slots normalizes to three empty entries");
        runner.Check(legacy.GetCarriedIds().SequenceEqual(new[] { "midas_touch" }),
            "legacy normalization preserves its main talent as the carried loadout");
    }

    private static void CarriedIdsEnumerateMainBeforeReserve(RegressionRunner runner)
    {
        TalentSlotConfig config = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "main_large", null, "main_medium", null, null, null },
            ReserveTalentIds = new[] { "reserve_medium", null, "reserve_small" }
        };

        runner.Check(config.GetCarriedIds().SequenceEqual(new[]
            { "main_large", "main_medium", "reserve_medium", "reserve_small" }),
            "carried ids enumerate all non-empty main slots before reserve slots");
        runner.Check(config.GetAllEquippedIds().SequenceEqual(new[] { "main_large", "main_medium" }),
            "legacy equipped ids continue to represent only active main slots");
    }

    private static void DuplicateValidationCoversAllCarriedSlots(RegressionRunner runner)
    {
        TalentSlotConfig duplicateAcrossAreas = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "midas_touch", null, null, null, null, null },
            ReserveTalentIds = new[] { "midas_touch", "", null }
        };
        TalentSlotConfig emptyIdsOnly = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "", null, null, null, null, null },
            ReserveTalentIds = new[] { "", null, null }
        };

        runner.Check(duplicateAcrossAreas.HasDuplicateCarriedIds(),
            "duplicate validation rejects one id shared by main and reserve slots");
        runner.Check(!emptyIdsOnly.HasDuplicateCarriedIds(),
            "duplicate validation ignores empty slot values");
    }

    private static void ProfileNormalizationRepairsLegacyDeckTalentSchema(RegressionRunner runner)
    {
        PlayerProfile profile = new PlayerProfile
        {
            Settings = null,
            SavedDecks = new System.Collections.Generic.List<SavedDeck>
            {
                new SavedDeck
                {
                    Talents = new TalentSlotConfig
                    {
                        SlotTalentIds = new[] { "starting_capital", null, null, null, null, null },
                        ReserveTalentIds = null
                    }
                },
                new SavedDeck { Talents = null }
            }
        };

        profile.Normalize();

        runner.Check(profile.Settings != null,
            "profile normalization restores missing settings");
        runner.Check(profile.SavedDecks[0].Talents.ReserveTalentIds.Length == TalentSlotConfig.ReserveSlotCount
                     && profile.SavedDecks[0].Talents.SlotTalentIds[0] == "starting_capital",
            "profile normalization upgrades legacy deck reserves without changing main slots");
        runner.Check(profile.SavedDecks[1].Talents.GetCarriedIds().Count() == 0,
            "profile normalization restores a missing deck talent config");
    }

    private static void ReserveSlotsEnforceTheirFixedTiers(RegressionRunner runner)
    {
        TalentSlotConfig config = new TalentSlotConfig();

        runner.Check(config.CanEquipReserve(0, TalentTier.Medium),
            "the first reserve slot accepts medium talents");
        runner.Check(!config.CanEquipReserve(0, TalentTier.Large),
            "reserve slots never accept large talents");
        runner.Check(config.CanEquipReserve(1, TalentTier.Small)
                     && !config.CanEquipReserve(1, TalentTier.Medium),
            "small reserve slots accept only small talents");
    }

    private static void MetadataPreservesDefaultsAndExplicitPolicies(RegressionRunner runner)
    {
        TalentMetadata startingCapital = TalentRegistry.Instance.GetMetadata("starting_capital");
        TalentMetadata peek = TalentRegistry.Instance.GetMetadata("peek");

        runner.Check(startingCapital.StateScope == TalentStateScope.Match,
            "starting capital metadata persists for the whole match");
        runner.Check(startingCapital.RevealPolicy == TalentRevealPolicy.PublicAtMatchStart,
            "starting capital becomes public when the match starts");
        runner.Check(startingCapital.SideboardPolicy == TalentSideboardPolicy.MainOnlyLocked,
            "starting capital is marked as locked main-only metadata");
        runner.Check(peek.StateScope == TalentStateScope.Round
                     && peek.ActivationWindow == TalentActivationWindow.None
                     && peek.RevealPolicy == TalentRevealPolicy.OwnerOnly
                     && peek.SideboardPolicy == TalentSideboardPolicy.Flexible,
            "peek keeps default state, activation, and sideboard metadata while remaining owner-only");
    }

    private static void ExistingTalentsRemainRegistrable(RegressionRunner runner)
    {
        string[] existingIds =
        {
            "midas_touch", "peek", "dragon_ascent", "draw_reward", "head_start", "starting_capital"
        };

        runner.Check(existingIds.All(TalentRegistry.Instance.HasTalent),
            "all existing talent ids remain available through registry reflection");
        runner.Check(existingIds.All(id => TalentRegistry.Instance.CreateInstance(id, 2)?.Id == id),
            "all existing talent ids still create their matching rule instances");
    }

    private static void RuntimeRejectsMalformedAndDuplicateCarriedLoadouts(RegressionRunner runner)
    {
        TalentSlotConfig valid = new TalentSlotConfig();
        TalentSlotConfig nullMain = new TalentSlotConfig
        {
            SlotTalentIds = null,
            ReserveTalentIds = new string[TalentSlotConfig.ReserveSlotCount]
        };
        TalentSlotConfig shortReserve = new TalentSlotConfig
        {
            SlotTalentIds = new string[TalentSlotConfig.MainSlotCount],
            ReserveTalentIds = new string[TalentSlotConfig.ReserveSlotCount - 1]
        };

        runner.Check(Throws<ArgumentException>(() => CreateRuntimeFromConfig(null)),
            "runtime rejects a null seat talent config");
        runner.Check(Throws<ArgumentException>(() => CreateRuntimeFromConfig(nullMain))
                     && Throws<ArgumentException>(() => CreateRuntimeFromConfig(shortReserve)),
            "runtime rejects null or abnormal-length slot arrays instead of normalizing locked loadouts");

        TalentSlotConfig duplicateMain = new TalentSlotConfig();
        duplicateMain.SlotTalentIds[0] = "network_test_lifecycle";
        duplicateMain.SlotTalentIds[1] = "network_test_lifecycle";
        TalentSlotConfig duplicateAcross = new TalentSlotConfig();
        duplicateAcross.SlotTalentIds[0] = "network_test_lifecycle";
        duplicateAcross.ReserveTalentIds[0] = "network_test_lifecycle";
        TalentSlotConfig duplicateReserve = new TalentSlotConfig();
        duplicateReserve.ReserveTalentIds[0] = "network_test_reserve_lifecycle";
        duplicateReserve.ReserveTalentIds[1] = "network_test_reserve_lifecycle";

        runner.Check(Throws<ArgumentException>(() => CreateRuntimeFromConfig(duplicateMain))
                     && Throws<ArgumentException>(() => CreateRuntimeFromConfig(duplicateAcross))
                     && Throws<ArgumentException>(() => CreateRuntimeFromConfig(duplicateReserve)),
            "runtime rejects duplicate carried identity in main, cross-area, and reserve slots");

        valid.SlotTalentIds[0] = " ";
        valid.SlotTalentIds[1] = "network_test_unknown";
        valid.SlotTalentIds[2] = "network_test_lifecycle";
        TalentMatchRuntime runtime = null;
        bool ignoredSafely = !Throws<Exception>(() => runtime = CreateRuntimeFromConfig(valid));
        GameSession session = new GameSession(GameMode.Single);
        if (runtime != null) runtime.BeginMatch(session);
        runner.Check(ignoredSafely && session.Scores.SequenceEqual(new[] { 7, 0, 0, 0 }),
            "blank and unknown ids are ignored without suppressing known carried talents");
    }

    private static void RuntimeRejectsIllegalLifecycleOrderAndForeignSessions(RegressionRunner runner)
    {
        LifecycleTestTalent.ResetObservations();
        WallLifecycleTestTalent.ResetObservations();
        RuntimePeekTestTalent.ResetObservations();
        GameSession session = new GameSession(GameMode.EastOnly);
        GameSession foreignSession = new GameSession(GameMode.EastOnly);
        TalentMatchRuntime runtime = CreateRuntime(mainIds: new[]
        {
            "network_test_lifecycle",
            "network_test_wall_lifecycle",
            "network_test_peek",
            "network_test_pipeline_add"
        });
        List<TileData> wall = new List<TileData>
        {
            new TileData(Suit.Man, 1, 0),
            new TileData(Suit.Pin, 2, 1)
        };

        runtime.BeginMatch(session);
        runner.Check(Throws<InvalidOperationException>(() =>
                         runtime.ApplyWallBuilding(new TalentWallContext(session, wall)))
                     && Throws<InvalidOperationException>(() =>
                         runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, wall)))
                     && Throws<InvalidOperationException>(() =>
                         runtime.EndRound(new TalentRoundOutcome(), session)),
            "wall, post-shuffle, and round-end calls are rejected before a round starts");
        runner.Check(WallLifecycleTestTalent.Calls == 0
                     && RuntimePeekTestTalent.Calls == 0
                     && LifecycleTestTalent.RoundEnds == 0,
            "rejected pre-round lifecycle calls do not execute rule hooks");

        runtime.BeginRound(new TalentRoundContext(session));
        runner.Check(Throws<InvalidOperationException>(() =>
                         runtime.BeginRound(new TalentRoundContext(session)))
                     && Throws<InvalidOperationException>(() =>
                         runtime.BeginRound(new TalentRoundContext(foreignSession))),
            "runtime rejects consecutive and foreign-session BeginRound calls");
        runner.Check(LifecycleTestTalent.MatchRoundCounts.SequenceEqual(new[] { 1 }),
            "rejected BeginRound calls do not increment match or round counters");
        runner.Check(Throws<InvalidOperationException>(() =>
                         runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0 }, session))
                     && Throws<InvalidOperationException>(() =>
                         runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, wall))),
            "round end and post-shuffle are rejected before wall building completes");

        runtime.ApplyWallBuilding(new TalentWallContext(session, wall));
        runner.Check(Throws<InvalidOperationException>(() =>
                         runtime.ApplyWallBuilding(new TalentWallContext(session, wall)))
                     && Throws<InvalidOperationException>(() =>
                         runtime.EndRound(new TalentRoundOutcome(), session)),
            "wall building runs once and a wall-built round cannot end before post-shuffle");
        runner.Check(WallLifecycleTestTalent.Calls == 1
                     && wall.Count == 3
                     && LifecycleTestTalent.RoundEnds == 0,
            "rejected wall/end repeats leave lifecycle hook counts unchanged");
        runner.Check(Throws<InvalidOperationException>(() => runtime.ApplyDraw(
                         new TalentDrawContext(session, 0),
                         new TileData(Suit.Man, 2, 0)))
                     && Throws<InvalidOperationException>(() => runtime.BuildScoringOptions(
                         new TalentScoringContext(session, 0))),
            "draw and scoring are unavailable until the round is post-shuffle ready");

        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, wall));
        runner.Check(Throws<InvalidOperationException>(() =>
                         runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, wall)))
                     && RuntimePeekTestTalent.Calls == 1,
            "post-shuffle resolution runs once per active round");
        runner.Check(Throws<InvalidOperationException>(() => runtime.ApplyDraw(
                         new TalentDrawContext(foreignSession, 0),
                         new TileData(Suit.Man, 2, 0)))
                     && Throws<InvalidOperationException>(() => runtime.ValidateAction(
                         new TalentActionContext(foreignSession, 0, ClientActionType.Discard, null)))
                     && Throws<InvalidOperationException>(() => runtime.BuildScoringOptions(
                         new TalentScoringContext(foreignSession, 0)))
                     && Throws<InvalidOperationException>(() => runtime.NotifyTileBecamePublic(
                         new TalentPublicTileContext(foreignSession, 0),
                         new TileData(Suit.Man, 1, 0)))
                     && Throws<InvalidOperationException>(() => runtime.ResolveAcceptedWinVisibility(
                         new TalentAcceptedWinContext(foreignSession, 0))),
            "ready-round draw, action, scoring, public, and win hooks reject foreign sessions");
        runner.Check(Throws<InvalidOperationException>(() => runtime.EndRound(
                         new TalentRoundOutcome { WinnerSeatIndex = 0 },
                         foreignSession))
                     && LifecycleTestTalent.RoundEnds == 0,
            "foreign-session EndRound is rejected before executing round-end hooks");

        TileData transformed = runtime.ApplyDraw(
            new TalentDrawContext(session, 0),
            new TileData(Suit.Man, 2, 0));
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0 }, session);
        runner.Check(transformed.Value == 3 && LifecycleTestTalent.RoundEnds == 1,
            "ready-round hooks execute for the match session and end exactly once");
        runner.Check(Throws<InvalidOperationException>(() =>
                         runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0 }, session))
                     && LifecycleTestTalent.RoundEnds == 1,
            "consecutive EndRound calls are rejected without repeating effects");
    }

    private static void SeatScopedContextsRejectInvalidSeatIndices(RegressionRunner runner)
    {
        GameSession session = new GameSession(GameMode.Single);
        foreach (int invalidSeat in new[] { -1, 4 })
        {
            runner.Check(Throws<ArgumentOutOfRangeException>(() => new TalentDrawContext(session, invalidSeat))
                         && Throws<ArgumentOutOfRangeException>(() => new TalentDiscardContext(session, invalidSeat))
                         && Throws<ArgumentOutOfRangeException>(() => new TalentActionContext(
                             session, invalidSeat, ClientActionType.Discard, null))
                         && Throws<ArgumentOutOfRangeException>(() => new TalentScoringContext(session, invalidSeat))
                         && Throws<ArgumentOutOfRangeException>(() => new TalentPublicTileContext(session, invalidSeat))
                         && Throws<ArgumentOutOfRangeException>(() => new TalentAcceptedWinContext(session, invalidSeat)),
                $"all seat-scoped contexts reject seat {invalidSeat}");
        }
    }

    private static void RoundOutcomeRejectsInvalidSeatIndices(RegressionRunner runner)
    {
        foreach (TalentRoundOutcome invalidOutcome in new[]
                 {
                     new TalentRoundOutcome { WinnerSeatIndex = -1 },
                     new TalentRoundOutcome { WinnerSeatIndex = 4 },
                     new TalentRoundOutcome { WinnerSeatIndex = 0, DiscarderSeatIndex = -1 },
                     new TalentRoundOutcome { WinnerSeatIndex = 0, DiscarderSeatIndex = 4 }
                 })
        {
            LifecycleTestTalent.ResetObservations();
            GameSession session = new GameSession(GameMode.Single);
            TalentMatchRuntime runtime = CreateRuntime(mainIds: new[] { "network_test_lifecycle" });
            runtime.BeginMatch(session);
            BeginReadyRound(runtime, session);

            runner.Check(Throws<ArgumentOutOfRangeException>(() => runtime.EndRound(invalidOutcome, session))
                         && LifecycleTestTalent.RoundEnds == 0,
                "round outcome rejects invalid winner/discarder seats before rule hooks");
        }
    }

    private static void InactiveMatchInitializationCannotMutateAuthoritativeSession(RegressionRunner runner)
    {
        LifecycleTestTalent.ResetObservations();
        ReserveLifecycleTestTalent.ResetObservations();
        GameSession session = new GameSession(GameMode.Single);
        TalentMatchRuntime runtime = CreateRuntime(
            mainIds: new[] { "network_test_lifecycle" },
            reserveIds: new[] { "network_test_reserve_lifecycle" });

        runtime.BeginMatch(session);

        runner.Check(!ReserveLifecycleTestTalent.MutableSessionLeaked
                     && session.Scores.SequenceEqual(new[] { 7, 0, 0, 0 }),
            "inactive match initialization receives a session snapshot and cannot mutate authoritative scores");
    }

    private static void DrawContextSnapshotsGameStateAndOwnerDeck(RegressionRunner runner)
    {
        ReadOnlyBoundaryTestTalent.ResetObservations();
        GameSession session = new GameSession(GameMode.Single);
        ServerGameState gameState = new ServerGameState(4);
        gameState.InitHand(0, new List<TileData> { new TileData(Suit.Man, 1, 0) });
        DeckConfig deck = DeckConfig.CreateStandard();
        Dictionary<int, DeckConfig> decks = new Dictionary<int, DeckConfig> { [0] = deck };
        TalentMatchRuntime runtime = CreateRuntime(mainIds: new[] { "network_test_read_only_boundary" });
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        runtime.ApplyDraw(
            new TalentDrawContext(session, 0, gameState, decks),
            new TileData(Suit.Pin, 2, 0));

        runner.Check(ReadOnlyBoundaryTestTalent.SawReadOnlyViews
                     && !ReadOnlyBoundaryTestTalent.MutableGameStateLeaked
                     && !ReadOnlyBoundaryTestTalent.MutableDeckLeaked,
            "draw rules receive game-state and owner-deck snapshots instead of mutable authority objects");
        runner.Check(gameState.GetHand(0).Count == 1 && deck.GetCardCount(Suit.Man, 1) == 1,
            "draw context snapshots cannot mutate authoritative hand or deck configuration");
    }

    private static void CrossRoundRuntimePreservesMatchStateAndResetsRoundState(RegressionRunner runner)
    {
        LifecycleTestTalent.ResetObservations();
        ReserveLifecycleTestTalent.ResetObservations();
        GameSession session = new GameSession(GameMode.EastOnly);
        TalentMatchRuntime runtime = CreateRuntime(
            mainIds: new[] { "network_test_lifecycle" },
            reserveIds: new[] { "network_test_reserve_lifecycle" });

        runtime.BeginMatch(session);
        bool duplicateBeginRejected = false;
        try
        {
            runtime.BeginMatch(session);
        }
        catch (InvalidOperationException)
        {
            duplicateBeginRejected = true;
        }

        BeginReadyRound(runtime, session);
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0, FinalFan = 8 }, session);
        BeginReadyRound(runtime, session);
        runtime.EndRound(new TalentRoundOutcome
        {
            WinnerSeatIndex = 2,
            DiscarderSeatIndex = 0,
            FinalFan = 12
        }, session);

        runner.Check(session.Scores.SequenceEqual(new[] { 7, 0, 0, 0 }),
            "match-start score applies exactly once to the owning seat");
        runner.Check(duplicateBeginRejected,
            "a talent match runtime rejects a second BeginMatch call");
        runner.Check(LifecycleTestTalent.MatchInitializations == 1
                     && ReserveLifecycleTestTalent.MatchInitializations == 1,
            "match state initializes once for active and reserve carried entries");
        runner.Check(ReserveLifecycleTestTalent.MatchStartEffects == 0
                     && ReserveLifecycleTestTalent.RoundStarts == 0,
            "inactive reserve entries initialize without applying match or round effects");
        runner.Check(LifecycleTestTalent.MatchRoundCounts.SequenceEqual(new[] { 1, 2 }),
            "match counters persist across two rounds on the same rule instance");
        runner.Check(LifecycleTestTalent.RoundCountsBeforeStart.SequenceEqual(new[] { 0, 0 }),
            "round counters reset before each round-start hook");
        runner.Check(LifecycleTestTalent.PreviousRoundWonAtStart.SequenceEqual(new[] { false, true })
                     && !LifecycleTestTalent.LastRoundWon,
            "match flags survive round reset and round outcome uses owner seat semantics");
    }

    private static void RuntimeEventsRespectVisibilityWithoutDestructiveReads(RegressionRunner runner)
    {
        LifecycleTestTalent.ResetObservations();
        GameSession session = new GameSession(GameMode.Single);
        TalentMatchRuntime runtime = CreateRuntime(mainIds: new[] { "network_test_lifecycle" });

        runtime.BeginMatch(session);
        IReadOnlyList<TalentRuntimeEvent>[] publicReads = Enumerable.Range(0, 4)
            .Select(runtime.DrainEventsForSeat)
            .ToArray();

        runner.Check(publicReads.All(events => events.Count == 1
                                               && events[0].Visibility == TalentEventVisibility.Public
                                               && events[0].OwnerSeatIndex == 0
                                               && events[0].TalentId == "network_test_lifecycle"),
            "a public match-start reveal is independently visible to all four seats");
        runner.Check(publicReads.Select(events => events[0].EventId).Distinct().Count() == 1
                     && publicReads[0][0].EventId > 0,
            "the same public event keeps one positive monotonic event id for every reader");

        runtime.BeginRound(new TalentRoundContext(session));
        IReadOnlyList<TalentRuntimeEvent> ownerEvents = runtime.DrainEventsForSeat(0);
        IReadOnlyList<TalentRuntimeEvent>[] otherEvents = Enumerable.Range(1, 3)
            .Select(runtime.DrainEventsForSeat)
            .ToArray();

        runner.Check(ownerEvents.Count == 1
                     && ownerEvents[0].Visibility == TalentEventVisibility.OwnerOnly
                     && ownerEvents[0].OwnerSeatIndex == 0
                     && ownerEvents[0].Value == 1,
            "an owner-only lifecycle event is visible to its owning seat");
        runner.Check(otherEvents.All(events => events.Count == 0),
            "owner-only events never enter another seat's event stream");
        runner.Check(ownerEvents[0].EventId > publicReads[0][0].EventId,
            "runtime-assigned event ids increase across public and private events");
    }

    private static void PostShufflePeekIsPrivateAndUsesShuffledOrder(RegressionRunner runner)
    {
        GameSession session = new GameSession(GameMode.Single);
        TalentMatchRuntime runtime = CreateRuntime(mainIds: new[] { "network_test_peek" });
        runtime.BeginMatch(session);
        runtime.BeginRound(new TalentRoundContext(session));
        List<TileData> shuffledWall = new List<TileData>
        {
            new TileData(Suit.Pin, 7, 2),
            new TileData(Suit.Man, 3, 1),
            new TileData(Suit.Sou, 9, 3)
        };

        runtime.ApplyWallBuilding(new TalentWallContext(session, shuffledWall));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, shuffledWall));

        runner.Check(runtime.GetPrivatePeekTiles(0).Select(tile => (tile.TileSuit, tile.Value))
                .SequenceEqual(new[] { (Suit.Pin, 7), (Suit.Man, 3) }),
            "post-shuffle peek stores the top tiles in shuffled draw order");
        runner.Check(Enumerable.Range(1, 3).All(seat => runtime.GetPrivatePeekTiles(seat).Count == 0),
            "post-shuffle peek results remain private to the owning seat");
    }

    private static void DrawAndDiscardPipelinesKeepStablePriorityOrder(RegressionRunner runner)
    {
        GameSession session = new GameSession(GameMode.Single);
        TalentMatchRuntime runtime = CreateRuntime(mainIds: new[]
        {
            "network_test_pipeline_add", "network_test_pipeline_multiply"
        });
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        TileData drawn = runtime.ApplyDraw(
            new TalentDrawContext(session, currentSeatIndex: 0),
            new TileData(Suit.Man, 2, 0));
        TileData discarded = runtime.ApplyDiscard(
            new TalentDiscardContext(session, currentSeatIndex: 0),
            new TileData(Suit.Pin, 2, 0));

        runner.Check(drawn.Value == 6,
            "equal-priority draw rules retain carried-loadout order and pipe returned tiles");
        runner.Check(discarded.Value == 6,
            "equal-priority discard rules retain carried-loadout order and pipe returned tiles");
    }

    private static void ScoringOptionsAreFreshForEveryEvaluation(RegressionRunner runner)
    {
        GameSession session = new GameSession(GameMode.Single);
        TalentMatchRuntime runtime = CreateRuntime(mainIds: new[] { "network_test_lifecycle" });
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        ScoringOptions first = runtime.BuildScoringOptions(new TalentScoringContext(session, 0));
        first.BonusFan = 99;
        ScoringOptions second = runtime.BuildScoringOptions(new TalentScoringContext(session, 0));

        runner.Check(!ReferenceEquals(first, second) && second.BonusFan == 3,
            "each scoring evaluation builds an independent mutable options object");
    }

    private static TalentMatchRuntime CreateRuntime(
        string[] mainIds = null,
        string[] reserveIds = null)
    {
        TalentSlotConfig config = new TalentSlotConfig();
        if (mainIds != null)
            Array.Copy(mainIds, config.SlotTalentIds, mainIds.Length);
        if (reserveIds != null)
            Array.Copy(reserveIds, config.ReserveTalentIds, reserveIds.Length);

        return new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
    }

    private static TalentMatchRuntime CreateRuntimeFromConfig(TalentSlotConfig config)
    {
        return new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
    }

    private static void BeginReadyRound(
        TalentMatchRuntime runtime,
        GameSession session,
        List<TileData> wall = null)
    {
        List<TileData> roundWall = wall ?? new List<TileData>();
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, roundWall));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, roundWall));
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void LoadoutDecodingEnforcesRoomAlienationPresets(RegressionRunner runner)
    {
        PlayerLoadoutMessage message = new PlayerLoadoutMessage
        {
            schemaVersion = TrustedPlayerLoadout.CurrentSchemaVersion,
            deckEntries = BuildValidDeckEntries(deckAlienation: 30),
            mainTalentSlotIds = new[] { "midas_touch", null, null, null, null, null },
            reserveTalentSlotIds = new[] { null, "network_test_small", null }
        };

        bool lowAccepted = PlayerLoadoutCodec.TryDecode(
            message, AlienationPreset.Low, out _, out string lowError);
        runner.Check(!lowAccepted && lowError == PlayerLoadoutErrorCodes.AlienationLimitExceeded,
            "low preset rejects 30 deck + 15 active talent");

        bool standardAccepted = PlayerLoadoutCodec.TryDecode(
            message, AlienationPreset.Standard, out TrustedPlayerLoadout standard, out _);
        runner.Check(standardAccepted && standard.TotalAlienation == 45,
            "inactive reserve cost is excluded from room-entry alienation");
        runner.Check(TrustedPlayerLoadout.CurrentSchemaVersion == 2,
            "carried-loadout wire schema is v2");

        message.schemaVersion = 1;
        runner.Check(!PlayerLoadoutCodec.TryDecode(message, AlienationPreset.Standard, out _, out string legacyError)
                     && legacyError == PlayerLoadoutErrorCodes.UnsupportedLoadoutVersion,
            "legacy v1 loadouts are explicitly rejected instead of being guessed");
    }

    private static void ProfileSettingsNormalizeUnknownAlienationPreset(RegressionRunner runner)
    {
        PlayerProfile profile = new PlayerProfile
        {
            Settings = new ProfileSettings
            {
                SelectedAlienationPreset = (AlienationPreset)999
            }
        };

        profile.Normalize();

        runner.Check(profile.Settings.SelectedAlienationPreset == AlienationPreset.Standard,
            "profile normalization restores an unknown alienation preview preset to Standard");
    }

    private static DeckTileCountMessage[] BuildValidDeckEntries(int deckAlienation)
    {
        if (deckAlienation != 30)
            throw new ArgumentOutOfRangeException(nameof(deckAlienation));

        DeckConfig deck = DeckConfig.CreateStandard();
        foreach (Suit suit in new[] { Suit.Man, Suit.Pin, Suit.Sou })
        {
            deck.SetCardCount(suit, 1, 6);
            for (int value = 2; value <= 6; value++) deck.SetCardCount(suit, value, 0);
        }

        var entries = new System.Collections.Generic.List<DeckTileCountMessage>(34);
        foreach (Suit suit in new[] { Suit.Man, Suit.Pin, Suit.Sou, Suit.Wind, Suit.Dragon })
        {
            int maximum = suit is Suit.Man or Suit.Pin or Suit.Sou ? 9 : suit == Suit.Wind ? 4 : 3;
            for (int value = 1; value <= maximum; value++)
            {
                entries.Add(new DeckTileCountMessage
                {
                    suit = (int)suit,
                    value = value,
                    count = deck.GetCardCount(suit, value)
                });
            }
        }
        return entries.ToArray();
    }
}

[TalentRule("network_test_lifecycle", "Lifecycle", "test", TalentTier.Small, 0,
    TalentPhase.OnDraw, StateScope = TalentStateScope.Match,
    RevealPolicy = TalentRevealPolicy.PublicAtMatchStart)]
internal sealed class LifecycleTestTalent : TalentRule
{
    public static int MatchInitializations { get; private set; }
    public static List<int> MatchRoundCounts { get; } = new List<int>();
    public static List<int> RoundCountsBeforeStart { get; } = new List<int>();
    public static List<bool> PreviousRoundWonAtStart { get; } = new List<bool>();
    public static bool LastRoundWon { get; private set; }
    public static int RoundEnds { get; private set; }

    public static void ResetObservations()
    {
        MatchInitializations = 0;
        MatchRoundCounts.Clear();
        RoundCountsBeforeStart.Clear();
        PreviousRoundWonAtStart.Clear();
        LastRoundWon = false;
        RoundEnds = 0;
    }

    public override void InitializeMatchState(TalentMatchContext context)
    {
        MatchInitializations++;
    }

    public override int GetMatchStartScoreDelta(TalentMatchContext context) => 7;

    public override void OnRoundStarted(TalentRoundContext context)
    {
        RoundCountsBeforeStart.Add(context.State.GetCounter("round_started", TalentStateScope.Round));
        PreviousRoundWonAtStart.Add(context.State.GetFlag("last_round_won", TalentStateScope.Match));
        context.State.IncrementCounter("round_started", TalentStateScope.Round);
        int matchRounds = context.State.IncrementCounter("match_rounds", TalentStateScope.Match);
        MatchRoundCounts.Add(matchRounds);
        context.Emit(new TalentRuntimeEvent
        {
            OwnerSeatIndex = 99,
            TalentId = "spoofed",
            EventType = "round_started",
            Visibility = TalentEventVisibility.OwnerOnly,
            Value = matchRounds
        });
    }

    public override void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome)
    {
        RoundEnds++;
        LastRoundWon = outcome.WinnerSeatIndex == context.OwnerSeatIndex;
        context.State.SetFlag("last_round_won", LastRoundWon, TalentStateScope.Match);
    }

    public override void ConfigureScoring(TalentScoringContext context, ScoringOptions options)
    {
        options.BonusFan += 3;
    }
}

[TalentRule("network_test_reserve_lifecycle", "Reserve Lifecycle", "test", TalentTier.Small, 0,
    StateScope = TalentStateScope.Match)]
internal sealed class ReserveLifecycleTestTalent : TalentRule
{
    public static int MatchInitializations { get; private set; }
    public static int MatchStartEffects { get; private set; }
    public static int RoundStarts { get; private set; }
    public static bool MutableSessionLeaked { get; private set; }

    public static void ResetObservations()
    {
        MatchInitializations = 0;
        MatchStartEffects = 0;
        RoundStarts = 0;
        MutableSessionLeaked = false;
    }

    public override void InitializeMatchState(TalentMatchContext context)
    {
        MatchInitializations++;
        if ((object)context.Session is GameSession authority)
        {
            MutableSessionLeaked = true;
            authority.Scores[context.OwnerSeatIndex] = 900;
        }
    }

    public override int GetMatchStartScoreDelta(TalentMatchContext context)
    {
        MatchStartEffects++;
        return 100;
    }

    public override void OnRoundStarted(TalentRoundContext context) => RoundStarts++;
}

[TalentRule("network_test_peek", "Peek", "test", TalentTier.Small, 0)]
internal sealed class RuntimePeekTestTalent : TalentRule
{
    public static int Calls { get; private set; }

    public static void ResetObservations() => Calls = 0;

    public override int GetRoundStartPeekCount(TalentRoundContext context)
    {
        Calls++;
        return 2;
    }
}

[TalentRule("network_test_wall_lifecycle", "Wall Lifecycle", "test", TalentTier.Small, 0,
    TalentPhase.WallBuilding)]
internal sealed class WallLifecycleTestTalent : TalentRule
{
    public static int Calls { get; private set; }

    public static void ResetObservations() => Calls = 0;

    public override void OnWallBuilding(TalentWallContext context)
    {
        Calls++;
        context.WallTiles.Add(new TileData(Suit.Dragon, 3, 0));
    }
}

[TalentRule("network_test_read_only_boundary", "Read-only Boundary", "test", TalentTier.Small, 0,
    TalentPhase.OnDraw)]
internal sealed class ReadOnlyBoundaryTestTalent : TalentRule
{
    public static bool SawReadOnlyViews { get; private set; }
    public static bool MutableGameStateLeaked { get; private set; }
    public static bool MutableDeckLeaked { get; private set; }

    public static void ResetObservations()
    {
        SawReadOnlyViews = false;
        MutableGameStateLeaked = false;
        MutableDeckLeaked = false;
    }

    public override TileData OnDraw(TalentContext context, TileData tile)
    {
        SawReadOnlyViews = context.GameState != null && context.OwnerDeckConfig != null;
        if ((object)context.GameState is ServerGameState authority)
        {
            MutableGameStateLeaked = true;
            authority.AddTile(0, new TileData(Suit.Wind, 1, 0));
        }
        if ((object)context.OwnerDeckConfig is DeckConfig deck)
        {
            MutableDeckLeaked = true;
            deck.SetCardCount(Suit.Man, 1, 9);
        }
        return tile;
    }
}

[TalentRule("network_test_pipeline_add", "Pipeline Add", "test", TalentTier.Small, 0,
    TalentPhase.OnDraw, TalentPhase.OnDiscard)]
internal sealed class PipelineAddTestTalent : TalentRule
{
    public override TileData OnDraw(TalentContext context, TileData tile)
    {
        tile.Value += 1;
        return tile;
    }

    public override TileData OnDiscard(TalentContext context, TileData tile)
    {
        tile.Value += 1;
        return tile;
    }
}

[TalentRule("network_test_pipeline_multiply", "Pipeline Multiply", "test", TalentTier.Small, 0,
    TalentPhase.OnDraw, TalentPhase.OnDiscard)]
internal sealed class PipelineMultiplyTestTalent : TalentRule
{
    public override TileData OnDraw(TalentContext context, TileData tile)
    {
        tile.Value *= 2;
        return tile;
    }

    public override TileData OnDiscard(TalentContext context, TileData tile)
    {
        tile.Value *= 2;
        return tile;
    }
}
