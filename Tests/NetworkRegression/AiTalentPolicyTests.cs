using MahjongGame.Core;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Talents;

internal static class AiTalentPolicyTests
{
    public static void Run(RegressionRunner runner)
    {
        DeterministicLoadoutsPassTheAuthoritativeCodec(runner);
        ArchetypePrioritiesOccupyLegalActiveSlots(runner);
        ActivePolicyUsesOnlyAuthoritativeOptions(runner);
        PublicChargeOptionRemainsPrivateAndDeepCopied(runner);
        ActiveSubmissionUsesCurrentLongDecisionAndDoesNotLoop(runner);
        RoomFillsAiSeatsWithPresetLegalArchetypes(runner);
        SideboardRetainsLockedAndCountersPublicThreats(runner);
        SideboardFailureReturnsOriginalForImmediateLock(runner);
        OneHundredSeededPolicyRuntimeSequencesStayLegal(runner);
    }

    private static void DeterministicLoadoutsPassTheAuthoritativeCodec(RegressionRunner runner)
    {
        foreach (AlienationPreset preset in new[]
                 {
                     AlienationPreset.Low,
                     AlienationPreset.Standard,
                     AlienationPreset.High
                 })
        {
            for (int seatIndex = 0; seatIndex < 4; seatIndex++)
            {
                PlayerLoadoutMessage first = AiTalentLoadoutFactory.Create(preset, seatIndex, seed: 7301);
                PlayerLoadoutMessage second = AiTalentLoadoutFactory.Create(preset, seatIndex, seed: 7301);
                bool accepted = PlayerLoadoutCodec.TryDecode(
                    first, preset, out TrustedPlayerLoadout trusted, out string errorCode);
                string[] carried = (first.mainTalentSlotIds ?? Array.Empty<string>())
                    .Concat(first.reserveTalentSlotIds ?? Array.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToArray();

                runner.Check(accepted && errorCode == null && trusted != null,
                    $"AI loadout {preset}/seat {seatIndex} passes authoritative codec validation");
                runner.Check(first.mainTalentSlotIds?.Length == TalentSlotConfig.MainSlotCount
                             && first.reserveTalentSlotIds?.Length == TalentSlotConfig.ReserveSlotCount,
                    $"AI loadout {preset}/seat {seatIndex} keeps the strict 6+3 slot shape");
                runner.Check(carried.Distinct(StringComparer.Ordinal).Count() == carried.Length,
                    $"AI loadout {preset}/seat {seatIndex} carries no duplicate talent ids");
                runner.Check(first.mainTalentSlotIds.SequenceEqual(second.mainTalentSlotIds)
                             && first.reserveTalentSlotIds.SequenceEqual(second.reserveTalentSlotIds),
                    $"AI loadout {preset}/seat {seatIndex} is deterministic for one seed");

                if (carried.Contains("starting_capital", StringComparer.Ordinal))
                {
                    runner.Check(first.mainTalentSlotIds.Contains("starting_capital", StringComparer.Ordinal)
                                 && !first.reserveTalentSlotIds.Contains("starting_capital", StringComparer.Ordinal)
                                 && TalentRegistry.Instance.GetMetadata("starting_capital").SideboardPolicy
                                     == TalentSideboardPolicy.MainOnlyLocked,
                        $"AI loadout {preset}/seat {seatIndex} keeps Starting Capital locked active");
                }
            }
        }
    }

    private static void ArchetypePrioritiesOccupyLegalActiveSlots(RegressionRunner runner)
    {
        PlayerLoadoutMessage burst = AiTalentLoadoutFactory.Create(AlienationPreset.Low, 0, seed: 0);
        PlayerLoadoutMessage control = AiTalentLoadoutFactory.Create(AlienationPreset.Low, 0, seed: 1);
        PlayerLoadoutMessage value = AiTalentLoadoutFactory.Create(AlienationPreset.Low, 0, seed: 2);

        runner.Check(burst.mainTalentSlotIds.Contains("sheathed_edge", StringComparer.Ordinal)
                     && burst.mainTalentSlotIds.Contains("head_start", StringComparer.Ordinal),
            "burst archetype activates sheathed edge and head start when the preset fits exactly");
        runner.Check(control.mainTalentSlotIds.Contains("interception", StringComparer.Ordinal)
                     && control.mainTalentSlotIds.Contains("composure", StringComparer.Ordinal),
            "control archetype activates interception and composure");
        runner.Check(value.mainTalentSlotIds.Contains("peek", StringComparer.Ordinal)
                     && value.mainTalentSlotIds.Contains("starting_capital", StringComparer.Ordinal),
            "information/value archetype activates peek and locked Starting Capital");
    }

    private static void ActivePolicyUsesOnlyAuthoritativeOptions(RegressionRunner runner)
    {
        TalentActionOption finisher = AiTalentDecisionPolicy.ChooseActiveAction(new[]
        {
            new TalentActionOption
            {
                TalentId = "interception", TargetSeatIndex = 1,
                TargetTalentId = "sheathed_edge", TargetPublicCharge = 3
            },
            new TalentActionOption { TalentId = "sheathed_edge" }
        });
        TalentActionOption interception = AiTalentDecisionPolicy.ChooseActiveAction(new[]
        {
            new TalentActionOption
            {
                TalentId = "interception", TargetSeatIndex = 2,
                TargetTalentId = "sheathed_edge", TargetPublicCharge = 3
            },
            new TalentActionOption
            {
                TalentId = "interception", TargetSeatIndex = 1,
                TargetTalentId = "zeta_charge", TargetPublicCharge = 3
            },
            new TalentActionOption
            {
                TalentId = "interception", TargetSeatIndex = 1,
                TargetTalentId = "alpha_charge", TargetPublicCharge = 3
            },
            new TalentActionOption
            {
                TalentId = "interception", TargetSeatIndex = 0,
                TargetTalentId = "larger_seat_lower_charge", TargetPublicCharge = 2
            }
        });

        runner.Check(finisher?.TalentId == "sheathed_edge",
            "AI active policy prefers an authoritative armed-finisher option");
        runner.Check(interception?.TalentId == "interception"
                     && interception.TargetPublicCharge == 3
                     && interception.TargetSeatIndex == 1
                     && interception.TargetTalentId == "alpha_charge",
            "AI interception chooses highest public charge then target seat and talent id");
        runner.Check(AiTalentDecisionPolicy.ChooseActiveAction(Array.Empty<TalentActionOption>()) == null
                     && AiTalentDecisionPolicy.ChooseActiveAction(null) == null,
            "AI active policy returns null when the authoritative option set is empty");
    }

    private static void PublicChargeOptionRemainsPrivateAndDeepCopied(RegressionRunner runner)
    {
        var sourceOption = new TalentActionOption
        {
            TalentId = "interception",
            TargetSeatIndex = 2,
            TargetTalentId = "sheathed_edge",
            TargetPublicCharge = 3
        };
        RoomGameSnapshot ownerSnapshot = CreateTalentOptionSnapshot(
            requestingSeatIndex: 0,
            new[] { sourceOption });
        RoomGameSnapshot otherSnapshot = CreateTalentOptionSnapshot(
            requestingSeatIndex: 1,
            Array.Empty<TalentActionOption>());
        var state = new ClientGameState();
        state.ApplySnapshot(ownerSnapshot, 0);
        sourceOption.TargetPublicCharge = 99;
        ownerSnapshot.privateSeat.availableTalentActions[0].targetPublicCharge = 77;
        TalentActionOption firstRead = state.AvailableTalentActions.Single();
        firstRead.TargetPublicCharge = 55;
        TalentActionOption secondRead = state.AvailableTalentActions.Single();

        runner.Check(secondRead.TargetPublicCharge == 3
                     && secondRead.TargetSeatIndex == 2
                     && secondRead.TargetTalentId == "sheathed_edge",
            "public target charge crosses snapshot/recovery projection as a deep-copied authoritative option");
        runner.Check(otherSnapshot.privateSeat.availableTalentActions.Length == 0
                     && !UnityEngine.JsonUtility.ToJson(otherSnapshot)
                         .Contains("sheathed_edge", StringComparison.Ordinal),
            "another seat receives no private talent action or target charge projection");
    }

    private static RoomGameSnapshot CreateTalentOptionSnapshot(
        int requestingSeatIndex,
        IReadOnlyList<TalentActionOption> options)
    {
        var tracker = new NetworkDecisionTracker();
        NetworkDecisionContext decision = tracker.OpenMainTurn(
            requestingSeatIndex,
            DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeMilliseconds());
        return RoomGameSnapshotBuilder.Build(new RoomGameSnapshotSource
        {
            RoomId = "ai-private-options",
            RoomState = RoomState.InRound,
            GameMode = GameMode.EastOnly,
            AlienationPreset = AlienationPreset.Standard,
            Seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeatSource
            {
                SeatIndex = index,
                IsOccupied = true,
                IsAi = index != requestingSeatIndex
            }).ToArray(),
            Session = new GameSession(GameMode.EastOnly),
            Hands = Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray(),
            Melds = Enumerable.Range(0, 4).Select(_ => new List<Meld>()).ToArray(),
            Rivers = Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray(),
            ScoringOptions = Enumerable.Range(0, 4).Select(_ => new ScoringOptions()).ToArray(),
            PeekWallTiles = Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray(),
            ActiveDecision = decision,
            AvailableTalentActions = options,
            Sideboard = new RoomSnapshotSideboardSource { SeatLocked = new bool[4] }
        }, requestingSeatIndex);
    }

    private static void ActiveSubmissionUsesCurrentLongDecisionAndDoesNotLoop(RegressionRunner runner)
    {
        const long DecisionId = 9000000001L;
        var server = new GameServer(new MahjongGame.Core.Services.WallService(),
            new GameServerOptions());
        server.SetAiTalentDecisionForTests(
            DecisionId,
            actingSeatIndex: 2,
            new[]
            {
                new TalentActionOption
                {
                    TalentId = "interception",
                    TargetSeatIndex = 1,
                    TargetTalentId = "sheathed_edge",
                    TargetPublicCharge = 3
                }
            },
            TalentActionResult.Reject(TalentActionErrorCodes.AlreadyUsedThisTurn));

        bool accepted = AiTalentDecisionPolicy.TrySubmitActiveAction(server, 2);

        runner.Check(!accepted
                     && server.TalentActionSubmissionCount == 1
                     && server.LastTalentActionSeatIndex == 2
                     && server.LastTalentActionMessage?.decisionId == DecisionId
                     && server.LastTalentActionMessage.talentId == "interception"
                     && server.LastTalentActionMessage.targetSeatIndex == 1
                     && server.LastTalentActionMessage.targetTalentId == "sheathed_edge",
            "AI submits one chosen option through GameServer with the current long decision id and does not loop on rejection");

        server.SetAiTalentDecisionForTests(
            DecisionId + 1,
            actingSeatIndex: 1,
            Array.Empty<TalentActionOption>(),
            TalentActionResult.Success(effectApplied: true));
        bool emptyAccepted = AiTalentDecisionPolicy.TrySubmitActiveAction(server, 1);
        runner.Check(!emptyAccepted && server.TalentActionSubmissionCount == 1,
            "AI performs no GameServer submission when the authoritative option set is empty");
    }

    private static void RoomFillsAiSeatsWithPresetLegalArchetypes(RegressionRunner runner)
    {
        TrustedPlayerLoadout hostLoadout = DecodeEmptyLoadout(AlienationPreset.Low);
        using var room = new Room(
            "ai-loadout-room", GameMode.Single, AlienationPreset.Low, "host", true, 64);
        var endpoint = new GameEndpoint();
        bool started = room.TryAddHuman(
                           "host", endpoint, "dev:host", "Host", hostLoadout, out _)
                       && room.SetReady("host", ReadyPhase.MatchStart, out _);

        bool allLegal = started;
        for (int seatIndex = 1; seatIndex < 4; seatIndex++)
        {
            RoomSeat seat = room.Seats[seatIndex];
            PlayerLoadoutMessage message = PlayerLoadoutCodec.CreateMessage(
                seat?.Loadout?.DeckConfig,
                seat?.Loadout?.TalentConfig,
                AlienationPreset.Low);
            allLegal &= seat?.IsAi == true
                        && seat.IsLoadoutLocked
                        && seat.Loadout.AlienationPreset == AlienationPreset.Low
                        && seat.Loadout.TalentConfig.GetCarriedIds().Any()
                        && PlayerLoadoutCodec.TryDecode(
                            message, AlienationPreset.Low, out _, out _);
        }

        runner.Check(allLegal,
            "Room AI fill locks preset-legal archetype loadouts instead of empty standard placeholders");
    }

    private static void SideboardRetainsLockedAndCountersPublicThreats(RegressionRunner runner)
    {
        TrustedPlayerLoadout carried = DecodeLoadout(
            AlienationPreset.Low,
            new[] { null, null, null, "starting_capital", "peek", "draw_reward" },
            new[] { "midas_touch", "interception", "composure" });
        var publicKnown = new[]
        {
            new SnapshotKnownTalent
            {
                ownerSeatIndex = 0,
                talentId = "sheathed_edge",
                isKnown = true,
                lastPublicEventType = "edge",
                lastPublicValue = 3
            }
        };

        string[] selection = AiTalentDecisionPolicy.ChooseSideboard(
            carried,
            originalActiveTalentIds: new[] { "starting_capital", "peek", "draw_reward" },
            publicKnownOpponentTalents: publicKnown,
            preset: AlienationPreset.Low,
            seatIndex: 1,
            seed: 0,
            out bool accepted);
        bool serverAccepted = SideboardLoadoutPolicy.TryValidate(
            carried,
            selection,
            AlienationPreset.Low,
            TalentRegistry.Instance,
            out string[] normalized,
            out int total,
            out _);

        runner.Check(accepted && serverAccepted
                     && normalized.Contains("starting_capital", StringComparer.Ordinal)
                     && normalized.Contains("interception", StringComparer.Ordinal)
                     && normalized.Contains("composure", StringComparer.Ordinal)
                     && total <= AlienationBudgetPolicy.GetLimit(AlienationPreset.Low)
                     && normalized.Distinct(StringComparer.Ordinal).Count() == normalized.Length,
            "AI sideboard retains locked talent and prioritizes counters to a public charged large threat");
    }

    private static void SideboardFailureReturnsOriginalForImmediateLock(RegressionRunner runner)
    {
        TrustedPlayerLoadout carried = DecodeLoadout(
            AlienationPreset.Low,
            new[] { null, null, null, "starting_capital", null, null },
            new string[] { null, null, null });
        string[] malformedOriginal = { "not_carried" };
        string[] selection = AiTalentDecisionPolicy.ChooseSideboard(
            carried,
            malformedOriginal,
            Array.Empty<SnapshotKnownTalent>(),
            AlienationPreset.Low,
            seatIndex: 2,
            seed: 0,
            out bool accepted);

        runner.Check(!accepted && selection.SequenceEqual(malformedOriginal),
            "AI sideboard failure returns the original verbatim for explicit original locking");
    }

    private static void OneHundredSeededPolicyRuntimeSequencesStayLegal(RegressionRunner runner)
    {
        bool legal = true;
        for (int seed = 0; seed < 100; seed++)
        {
            AlienationPreset preset = (seed % 3) switch
            {
                0 => AlienationPreset.Low,
                1 => AlienationPreset.Standard,
                _ => AlienationPreset.High
            };
            int seatIndex = seed % 4;
            PlayerLoadoutMessage message = AiTalentLoadoutFactory.Create(preset, seatIndex, seed);
            legal &= PlayerLoadoutCodec.TryDecode(
                message, preset, out TrustedPlayerLoadout loadout, out _);
            string[] carried = loadout?.TalentConfig.GetCarriedIds().ToArray() ?? Array.Empty<string>();
            legal &= carried.Distinct(StringComparer.Ordinal).Count() == carried.Length
                     && loadout?.TotalAlienation <= AlienationBudgetPolicy.GetLimit(preset);

            string[] original = loadout?.TalentConfig.GetMainIds().ToArray() ?? Array.Empty<string>();
            string[] sideboard = AiTalentDecisionPolicy.ChooseSideboard(
                loadout,
                original,
                new[]
                {
                    new SnapshotKnownTalent
                    {
                        ownerSeatIndex = (seatIndex + 1) % 4,
                        talentId = "sheathed_edge",
                        isKnown = true,
                        lastPublicEventType = "edge",
                        lastPublicValue = seed % 4
                    }
                },
                preset,
                seatIndex,
                seed,
                out bool sideboardAccepted);
            string[] normalized = Array.Empty<string>();
            int total = 0;
            bool validated = sideboardAccepted && SideboardLoadoutPolicy.TryValidate(
                loadout,
                sideboard,
                preset,
                TalentRegistry.Instance,
                out normalized,
                out total,
                out _);
            legal &= validated
                     && normalized.Distinct(StringComparer.Ordinal).Count() == normalized.Length
                     && total <= AlienationBudgetPolicy.GetLimit(preset);

            legal &= RunOneRuntimeActionSequence(seed);
        }

        runner.Check(legal,
            "100 seeded policy/runtime sequences finish sideboarding without invalid targets, negative uses/charge, budget excess, duplicates, or stale acceptance");
    }

    private static bool RunOneRuntimeActionSequence(int seed)
    {
        var target = new TalentSlotConfig();
        target.SlotTalentIds[0] = "sheathed_edge";
        var actor = new TalentSlotConfig();
        actor.SlotTalentIds[3] = "interception";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = target, [1] = actor },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);
        for (int round = 0; round < 3; round++)
        {
            runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);
            BeginReadyRound(runtime, session);
        }

        long decisionId = 5000000000L + seed;
        runtime.OpenMainDecision(1, decisionId);
        IReadOnlyList<TalentActionOption> options = runtime.GetAvailableActions(
            1,
            new TalentActionQueryContext(
                session, 1, TalentActivationWindow.MainTurn, decisionId));
        TalentActionOption chosen = AiTalentDecisionPolicy.ChooseActiveAction(options);
        if (chosen == null
            || chosen.TargetSeatIndex != 0
            || chosen.TargetTalentId != "sheathed_edge"
            || chosen.TargetPublicCharge != 3)
        {
            return false;
        }

        TalentActionResult stale = runtime.TryActivate(
            1,
            new TalentActionRequest
            {
                DecisionId = decisionId - 1,
                TalentId = chosen.TalentId,
                TargetSeatIndex = chosen.TargetSeatIndex,
                TargetTalentId = chosen.TargetTalentId
            },
            new TalentActivationContext(
                session, 1, TalentActivationWindow.MainTurn, decisionId));
        TalentActionResult accepted = runtime.TryActivate(
            1,
            new TalentActionRequest
            {
                DecisionId = decisionId,
                TalentId = chosen.TalentId,
                TargetSeatIndex = chosen.TargetSeatIndex,
                TargetTalentId = chosen.TargetTalentId
            },
            new TalentActivationContext(
                session, 1, TalentActivationWindow.MainTurn, decisionId));

        return !stale.Accepted
               && accepted.Accepted
               && runtime.GetPrivateCounter(1, "interception", "uses_remaining") >= 0
               && runtime.GetPublicCounter(0, "sheathed_edge", "edge") >= 0;
    }

    private static void BeginReadyRound(TalentMatchRuntime runtime, GameSession session)
    {
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>()));
    }

    private static TrustedPlayerLoadout DecodeEmptyLoadout(AlienationPreset preset) =>
        DecodeLoadout(
            preset,
            new string[TalentSlotConfig.MainSlotCount],
            new string[TalentSlotConfig.ReserveSlotCount]);

    private static TrustedPlayerLoadout DecodeLoadout(
        AlienationPreset preset,
        string[] main,
        string[] reserve)
    {
        PlayerLoadoutMessage message = PlayerLoadoutCodec.CreateMessage(
            DeckConfig.CreateStandard(),
            new TalentSlotConfig { SlotTalentIds = main, ReserveTalentIds = reserve },
            preset);
        if (!PlayerLoadoutCodec.TryDecode(message, preset, out TrustedPlayerLoadout loadout, out string error))
            throw new InvalidOperationException($"Invalid AI policy fixture: {error}");
        return loadout;
    }
}
