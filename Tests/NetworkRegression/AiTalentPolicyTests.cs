using MahjongGame.Core;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Talents;
using System.Reflection;

internal static class AiTalentPolicyTests
{
    public static void Run(RegressionRunner runner)
    {
        TelemetrySerializationIsNarrowAndPrivacySafe(runner);
        RuntimeTelemetryEmitsAppliedAndBlockedExactlyOnce(runner);
        MatchRoundSideboardAndWinTelemetryUsesAuthoritativeAggregates(runner);
        ThrowingTelemetrySinkCannotInterruptRoomCompletion(runner);
        DeterministicLoadoutsPassTheAuthoritativeCodec(runner);
        ArchetypePrioritiesOccupyLegalActiveSlots(runner);
        ActivePolicyUsesOnlyAuthoritativeOptions(runner);
        PublicChargeOptionRemainsPrivateAndDeepCopied(runner);
        ActiveSubmissionUsesCurrentLongDecisionAndDoesNotLoop(runner);
        RoomFillsAiSeatsWithPresetLegalArchetypes(runner);
        SideboardRetainsLockedAndCountersPublicThreats(runner);
        SideboardDiscoversCountersThroughCapabilities(runner);
        RoomAiThreatInputExcludesInactiveAndNonpublicOpponents(runner);
        SideboardFailureReturnsOriginalForImmediateLock(runner);
        OneHundredSeededPolicyRuntimeSequencesStayLegal(runner);
    }

    private static void TelemetrySerializationIsNarrowAndPrivacySafe(RegressionRunner runner)
    {
        var record = new TalentTelemetryRecord
        {
            anonymousSessionId = "7ac785e44f18459bb9a9caf33a6b99cd",
            preset = "standard",
            mode = "half_game",
            completedRound = 4,
            eventType = "active_talent_applied",
            seatIndex = 2,
            talentId = "interception",
            publicValue = 1,
            drawsPerSeat = new[] { 8, 9, 7, 8 },
            baseFan = 8,
            eligibilityFan = 2,
            postLegalBonusFan = 16,
            negativeFan = -4,
            finalFan = 22,
            winnerSeatIndex = 2,
            controlApplied = true,
            controlBlocked = false,
            sideboardAccepted = false,
            sideboardOriginal = false,
            sideboardTimeout = false
        };

        string json = TalentTelemetry.Serialize(record);
        runner.Check(json.Contains("\"eventType\":\"active_talent_applied\"", StringComparison.Ordinal)
                     && json.Contains("\"drawsPerSeat\":[8,9,7,8]", StringComparison.Ordinal)
                     && !json.Contains('\n')
                     && !json.Contains('\r'),
            "telemetry serializes one compact JSON gameplay-fact object");

        string[] forbiddenNameFragments =
        {
            "username", "displayname", "playerid", "credential", "hand", "concealed",
            "deckorder", "peektile", "roomticket", "connectionid", "streamid"
        };
        string[] recordFieldNames = typeof(TalentTelemetryRecord)
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(field => field.Name.ToLowerInvariant())
            .ToArray();
        bool hasForbiddenField = recordFieldNames.Any(fieldName =>
            forbiddenNameFragments.Any(fragment => fieldName.Contains(fragment, StringComparison.Ordinal)));
        runner.Check(!hasForbiddenField
                     && forbiddenNameFragments.All(fragment =>
                         !json.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
            "telemetry record members and serialized output exclude identity and hidden state");
    }

    private static void RuntimeTelemetryEmitsAppliedAndBlockedExactlyOnce(RegressionRunner runner)
    {
        var sink = new MemoryTalentTelemetrySink();
        RunInterceptionTelemetryScenario(sink, targetHasDefense: false, sessionId: "cc8b5f9622e04f28992af407c776771a");
        RunInterceptionTelemetryScenario(sink, targetHasDefense: true, sessionId: "6f10b7d1e41d47e78998df52095ea7d2");

        TalentTelemetryRecord[] applied = sink.Records
            .Where(record => record.eventType == "active_talent_applied")
            .ToArray();
        TalentTelemetryRecord[] blocked = sink.Records
            .Where(record => record.eventType == "blocked_negative_effect")
            .ToArray();
        runner.Check(applied.Length == 1
                     && applied[0].seatIndex == 1
                     && applied[0].talentId == "interception"
                     && applied[0].controlApplied
                     && !applied[0].controlBlocked,
            "one authoritative applied control result emits one telemetry record");
        runner.Check(blocked.Length == 1
                     && blocked[0].seatIndex == 0
                     && blocked[0].talentId == "composure"
                     && blocked[0].controlBlocked
                     && !blocked[0].controlApplied,
            "one authoritative blocked control result emits one telemetry record");
    }

    private static void RunInterceptionTelemetryScenario(
        ITalentTelemetrySink sink,
        bool targetHasDefense,
        string sessionId)
    {
        var target = new TalentSlotConfig();
        target.SlotTalentIds[0] = "sheathed_edge";
        if (targetHasDefense) target.SlotTalentIds[3] = "composure";
        var actor = new TalentSlotConfig();
        actor.SlotTalentIds[3] = "interception";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = target, [1] = actor },
            TalentRegistry.Instance,
            sink,
            sessionId,
            AlienationPreset.Standard);
        var session = new GameSession(GameMode.HalfGame);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);
        for (int round = 0; round < 3; round++)
        {
            runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);
            session.AdvanceRound();
            BeginReadyRound(runtime, session);
        }

        const long DecisionId = 8000000001L;
        runtime.OpenMainDecision(1, DecisionId);
        TalentActionResult result = runtime.TryActivate(
            1,
            new TalentActionRequest
            {
                DecisionId = DecisionId,
                TalentId = "interception",
                TargetSeatIndex = 0,
                TargetTalentId = "sheathed_edge"
            },
            new TalentActivationContext(
                session, 1, TalentActivationWindow.MainTurn, DecisionId));
        if (!result.Accepted) throw new InvalidOperationException("Telemetry fixture action was rejected.");

        for (int seatIndex = 0; seatIndex < 4; seatIndex++)
            runtime.DrainEventsForSeat(seatIndex);
    }

    private static void MatchRoundSideboardAndWinTelemetryUsesAuthoritativeAggregates(
        RegressionRunner runner)
    {
        var sink = new MemoryTalentTelemetrySink();
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = new TalentSlotConfig() },
            TalentRegistry.Instance,
            sink,
            "27f658e5bb9d471daaa0212ca65ba75b",
            AlienationPreset.High);
        var session = new GameSession(GameMode.FullGame);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);
        runtime.RecordAcceptedWinTelemetry(
            3,
            new TalentFanResolution
            {
                IsAttributionComplete = true,
                BaseFan = 8,
                EligibilityFan = 2,
                PostLegalBonusFan = 16,
                NegativeFan = -4,
                FinalFan = 22,
                Contributions = new[]
                {
                    new TalentFanContribution
                    {
                        TalentId = "must_not_be_serialized",
                        Category = TalentFanContributionCategory.PostLegal,
                        FanDelta = 16
                    }
                }
            },
            new[] { 7, 8, 9, 10 });
        runtime.EndRound(
            new TalentRoundOutcome { WinnerSeatIndex = 3, FinalFan = 22 },
            session,
            new[] { 7, 8, 9, 10 });
        session.AdvanceRound();
        runtime.RecordSideboardLockTelemetry(2, accepted: false, original: true, timeout: true);

        TalentTelemetryRecord[] records = sink.Records.ToArray();
        TalentTelemetryRecord win = records.Single(record => record.eventType == "accepted_win");
        TalentTelemetryRecord roundEnd = records.Single(record => record.eventType == "round_end");
        TalentTelemetryRecord sideboard = records.Single(record => record.eventType == "sideboard_lock");
        string winJson = TalentTelemetry.Serialize(win);
        runner.Check(records.Count(record => record.eventType == "match_start") == 1
                     && records.Count(record => record.eventType == "round_start") == 1
                     && records.Count(record => record.eventType == "round_end") == 1
                     && records.Count(record => record.eventType == "accepted_win") == 1
                     && records.Count(record => record.eventType == "sideboard_lock") == 1,
            "authoritative match, round, accepted win, and sideboard boundaries emit exactly once");
        runner.Check(win.preset == "high"
                     && win.mode == "full_game"
                     && win.completedRound == 1
                     && win.winnerSeatIndex == 3
                     && win.baseFan == 8
                     && win.eligibilityFan == 2
                     && win.postLegalBonusFan == 16
                     && win.negativeFan == -4
                     && win.finalFan == 22
                     && win.drawsPerSeat.SequenceEqual(new[] { 7, 8, 9, 10 })
                     && !winJson.Contains("must_not_be_serialized", StringComparison.Ordinal),
            "accepted-win telemetry records only Task 3 aggregate attribution and safe draw counts");
        runner.Check(roundEnd.completedRound == 1
                     && roundEnd.finalFan == 22
                     && roundEnd.winnerSeatIndex == 3
                     && sideboard.completedRound == 1
                     && !sideboard.sideboardAccepted
                     && sideboard.sideboardOriginal
                     && sideboard.sideboardTimeout,
            "round completion and original-timeout sideboard facts use completed-round authority");
    }

    private static void ThrowingTelemetrySinkCannotInterruptRoomCompletion(RegressionRunner runner)
    {
        TrustedPlayerLoadout loadout = DecodeEmptyLoadout(AlienationPreset.Low);
        using var room = new Room(
            "throwing-telemetry-room",
            GameMode.Single,
            AlienationPreset.Low,
            "host",
            true,
            64,
            new ThrowingTalentTelemetrySink());
        var endpoint = new GameEndpoint();
        bool started = room.TryAddHuman(
                           "host", endpoint, "dev:telemetry-host", "Host", loadout, out _)
                       && room.SetReady("host", ReadyPhase.MatchStart, out _)
                       && room.SetReady("host", ReadyPhase.GameSceneLoaded, out _);
        room.GameServer?.CompleteDrawRound();

        runner.Check(started
                     && room.State == RoomState.SessionCompleted
                     && room.Session.TotalRoundsPlayed == 1
                     && room.GameServer?.CompletionNotifications == 1,
            "throwing telemetry sink cannot interrupt Room round completion or completion latch delivery");
    }

    private sealed class ThrowingTalentTelemetrySink : ITalentTelemetrySink
    {
        public void Record(TalentTelemetryRecord record) =>
            throw new InvalidOperationException("expected telemetry sink failure");
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
        TalentActionOption priority = AiTalentDecisionPolicy.ChooseActiveAction(new[]
        {
            new TalentActionOption
            {
                TalentId = "arbitrary_control", AiPriority = 100,
                TargetSeatIndex = 1, TargetTalentId = "arbitrary_charge", TargetPublicCharge = 3
            },
            new TalentActionOption { TalentId = "arbitrary_expiring", AiPriority = 300 }
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

        runner.Check(priority?.TalentId == "arbitrary_expiring",
            "AI active policy obeys server-authored priority without recognizing talent ids");
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
            TargetPublicCharge = 3,
            AiPriority = 222,
            Choice = new TalentChoiceSet(
                TalentChoiceKind.Mode,
                "talent.choice.mode",
                "safe",
                new[]
                {
                    new TalentChoiceOption("safe", "talent.choice.safe"),
                    new TalentChoiceOption("risk", "talent.choice.risk")
                })
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
        ownerSnapshot.privateSeat.availableTalentActions[0].choice.options[0].choiceId = "forged";
        TalentActionOption firstRead = state.AvailableTalentActions.Single();
        firstRead.TargetPublicCharge = 55;
        TalentActionOption secondRead = state.AvailableTalentActions.Single();

        runner.Check(secondRead.TargetPublicCharge == 3
                     && secondRead.AiPriority == 222
                     && secondRead.TargetSeatIndex == 2
                     && secondRead.TargetTalentId == "sheathed_edge"
                     && secondRead.Choice.DefaultChoiceId == "safe"
                     && secondRead.Choice.Options[0].ChoiceId == "safe",
            "public target charge and private choices cross recovery as deep-copied authoritative options");
        runner.Check(otherSnapshot.privateSeat.availableTalentActions.Length == 0
                     && !UnityEngine.JsonUtility.ToJson(otherSnapshot)
                         .Contains("talent.choice.safe", StringComparison.Ordinal),
            "another seat receives no private talent action, target charge, or choice projection");
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

        var sequenceServer = new GameServer(new MahjongGame.Core.Services.WallService(),
            new GameServerOptions());
        sequenceServer.SetAiTalentDecisionSequenceForTests(
            DecisionId + 2,
            actingSeatIndex: 0,
            new[]
            {
                new[] { new TalentActionOption { TalentId = "first", AiPriority = 300 } },
                new[] { new TalentActionOption { TalentId = "second", AiPriority = 200 } },
                Array.Empty<TalentActionOption>()
            },
            TalentActionResult.Success(effectApplied: true));

        bool sequenceAccepted = AiTalentDecisionPolicy.TrySubmitActiveAction(sequenceServer, 0);
        runner.Check(sequenceAccepted
                     && sequenceServer.TalentActionSubmissionCount == 2
                     && sequenceServer.SubmittedTalentIds.SequenceEqual(new[] { "first", "second" }),
            "AI re-queries authoritative options and submits multiple distinct talents within one main decision");

        var repeatedServer = new GameServer(new MahjongGame.Core.Services.WallService(),
            new GameServerOptions());
        repeatedServer.SetAiTalentDecisionSequenceForTests(
            DecisionId + 3,
            actingSeatIndex: 0,
            new[]
            {
                new[] { new TalentActionOption { TalentId = "repeated", AiPriority = 10 } },
                new[] { new TalentActionOption { TalentId = "repeated", AiPriority = 10 } }
            },
            TalentActionResult.Success(effectApplied: true));
        bool repeatedAccepted = AiTalentDecisionPolicy.TrySubmitActiveAction(repeatedServer, 0);
        runner.Check(repeatedAccepted && repeatedServer.TalentActionSubmissionCount == 1,
            "AI stops when an accepted action is advertised again with the same authoritative fingerprint");
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

        NetworkMessageEnvelope[] messages = endpoint.SentMessages
            .Select(MessageSerializer.DeserializeEnvelope)
            .Where(envelope => envelope != null)
            .ToArray();
        RoomSeatMessage[] aiSeatUpdates = messages
            .Where(envelope => envelope.type == "RoomSeatUpdated")
            .Select(envelope => MessageSerializer.DeserializePayload<RoomSeatUpdatedMessage>(envelope.data)?.seat)
            .Where(seat => seat?.isAi == true)
            .ToArray();
        int lastAiUpdateIndex = Array.FindLastIndex(messages, envelope =>
            envelope.type == "RoomSeatUpdated"
            && MessageSerializer.DeserializePayload<RoomSeatUpdatedMessage>(envelope.data)?.seat?.isAi == true);
        int roomReadyIndex = Array.FindIndex(messages, envelope => envelope.type == "RoomReady");
        runner.Check(aiSeatUpdates.Select(seat => seat.displayName).SequenceEqual(
                         new[] { "AI 2", "AI 3", "AI 4" })
                     && lastAiUpdateIndex >= 0
                     && roomReadyIndex > lastAiUpdateIndex,
            "AI fill broadcasts every generated AI identity before clients enter the game scene");
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
                isActive = true,
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
                     && !normalized.Contains("composure", StringComparer.Ordinal)
                     && total <= AlienationBudgetPolicy.GetLimit(AlienationPreset.Low)
                     && normalized.Distinct(StringComparer.Ordinal).Count() == normalized.Length,
            "AI sideboard retains locked talent and performs one public-charge counter swap");
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

    private static void SideboardDiscoversCountersThroughCapabilities(RegressionRunner runner)
    {
        TrustedPlayerLoadout carried = DecodeLoadout(
            AlienationPreset.Standard,
            new[] { null, null, null, "starting_capital", "peek", "draw_reward" },
            new[]
            {
                "network_test_charge_control_capability",
                "network_test_charge_defense_capability",
                null
            });
        var chargeThreat = new[]
        {
            new SnapshotKnownTalent
            {
                ownerSeatIndex = 1,
                talentId = "sheathed_edge",
                isKnown = true,
                isActive = true,
                lastPublicValue = 2
            }
        };
        string[] againstCharge = AiTalentDecisionPolicy.ChooseSideboard(
            carried,
            new[] { "starting_capital", "peek", "draw_reward" },
            chargeThreat,
            AlienationPreset.Standard,
            seatIndex: 0,
            seed: 0,
            out bool chargeAccepted);

        var controlThreat = new[]
        {
            new SnapshotKnownTalent
            {
                ownerSeatIndex = 1,
                talentId = "interception",
                isKnown = true,
                isActive = true,
                lastPublicValue = 2
            }
        };
        string[] againstControl = AiTalentDecisionPolicy.ChooseSideboard(
            carried,
            new[] { "starting_capital", "peek", "draw_reward" },
            controlThreat,
            AlienationPreset.Standard,
            seatIndex: 0,
            seed: 0,
            out bool controlAccepted);

        runner.Check(chargeAccepted
                     && againstCharge.Contains("network_test_charge_control_capability", StringComparer.Ordinal)
                     && !againstCharge.Contains("network_test_charge_defense_capability", StringComparer.Ordinal),
            "AI sideboard discovers a carried public-charge counter through its capability interface");
        runner.Check(controlAccepted
                     && againstControl.Contains("network_test_charge_defense_capability", StringComparer.Ordinal)
                     && !againstControl.Contains("network_test_charge_control_capability", StringComparer.Ordinal),
            "AI sideboard discovers a carried charge-control defense through its capability interface");
    }

    private static void RoomAiThreatInputExcludesInactiveAndNonpublicOpponents(RegressionRunner runner)
    {
        var requester = new TalentSlotConfig();
        requester.SlotTalentIds[0] = "sheathed_edge";
        var activeOpponent = new TalentSlotConfig();
        activeOpponent.SlotTalentIds[0] = "sheathed_edge";
        var sideboardedOpponent = new TalentSlotConfig();
        sideboardedOpponent.SlotTalentIds[0] = "sheathed_edge";
        var hiddenOpponent = new TalentSlotConfig();
        hiddenOpponent.SlotTalentIds[3] = "interception";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig>
            {
                [0] = requester,
                [1] = activeOpponent,
                [2] = sideboardedOpponent,
                [3] = hiddenOpponent
            },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);
        for (int round = 0; round < 3; round++)
        {
            runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 3 }, session);
            BeginReadyRound(runtime, session);
        }

        runtime.ReplaceActiveSet(2, Array.Empty<string>());
        TalentSnapshotEntry inactiveEntry = runtime.GetSnapshotEntries().Single(entry =>
            entry.OwnerSeatIndex == 2 && entry.TalentId == "sheathed_edge");
        TrustedPlayerLoadout carried = DecodeLoadout(
            AlienationPreset.Low,
            new[] { null, null, null, "starting_capital", "peek", "draw_reward" },
            new[] { null, "interception", "composure" },
            CreateTwentyAlienationDeck());
        using var room = new Room(
            "ai-active-threat-filter", GameMode.Single, AlienationPreset.Standard, "host", true, 64);
        typeof(Room).GetField("_talentRuntime", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(room, runtime);
        runtime.ReplaceActiveSet(1, Array.Empty<string>());
        runtime.ReplaceActiveSet(2, new[] { "sheathed_edge" });
        SnapshotKnownTalent[] activeKnown = (SnapshotKnownTalent[])typeof(Room)
            .GetMethod("BuildPublicKnownOpponentTalents", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(room, new object[] { 0 });
        string[] promoted = AiTalentDecisionPolicy.ChooseSideboard(
            carried,
            new[] { "starting_capital", "peek", "draw_reward" },
            activeKnown,
            AlienationPreset.Low,
            seatIndex: 0,
            seed: 2,
            out bool activeAccepted);
        runtime.ReplaceActiveSet(1, Array.Empty<string>());
        runtime.ReplaceActiveSet(2, Array.Empty<string>());
        SnapshotKnownTalent[] known = (SnapshotKnownTalent[])typeof(Room)
            .GetMethod("BuildPublicKnownOpponentTalents", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(room, new object[] { 0 });
        string[] unpromoted = AiTalentDecisionPolicy.ChooseSideboard(
            carried,
            new[] { "starting_capital", "peek", "draw_reward" },
            known,
            AlienationPreset.Low,
            seatIndex: 0,
            seed: 2,
            out bool inactiveAccepted);

        runner.Check(!inactiveEntry.IsActive
                     && inactiveEntry.IsRevealed
                     && inactiveEntry.LastPublicValue == 3,
            "sideboarded large talent retains its sticky revealed public charge state");
        SnapshotKnownTalent activeThreat = activeKnown.Single(talent => talent.ownerSeatIndex == 2);
        runner.Check(activeKnown.Length == 2
                     && activeThreat.talentId == "sheathed_edge"
                     && activeThreat.isActive
                     && !activeKnown.Single(talent => talent.ownerSeatIndex == 1).isActive
                     && activeAccepted
                     && promoted.Contains("interception", StringComparer.Ordinal)
                     && !promoted.Contains("composure", StringComparer.Ordinal),
            "active revealed charged large threat promotes one AI counter while retaining inactive history");
        runner.Check(inactiveAccepted
                     && !unpromoted.Contains("interception", StringComparer.Ordinal)
                     && !unpromoted.Contains("composure", StringComparer.Ordinal),
            "sideboarded sticky public threat does not promote AI counter talents");
        runner.Check(known.Count(talent => talent.ownerSeatIndex == 1) == 1
                     && !known.Single(talent => talent.ownerSeatIndex == 1).isActive
                     && known.Count(talent => talent.ownerSeatIndex == 2) == 1
                     && !known.Single(talent => talent.ownerSeatIndex == 2).isActive
                     && !known.Any(talent => talent.ownerSeatIndex == 3)
                     && !known.Any(talent => talent.ownerSeatIndex == 0),
            "Room preserves inactive public history while excluding self and hidden opponents");
    }

    private static DeckConfig CreateTwentyAlienationDeck()
    {
        DeckConfig deck = DeckConfig.CreateStandard();
        foreach (Suit suit in new[] { Suit.Man, Suit.Pin })
        {
            deck.SetCardCount(suit, 1, 6);
            for (int value = 2; value <= 6; value++) deck.SetCardCount(suit, value, 0);
        }
        deck.CalculateAlienationScore();
        return deck;
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
                        isActive = true,
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
        runtime.CompleteInitialHands(new TalentInitialHandsContext(session, new ServerGameState(4)));
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
        string[] reserve,
        DeckConfig deck = null)
    {
        PlayerLoadoutMessage message = PlayerLoadoutCodec.CreateMessage(
            deck ?? DeckConfig.CreateStandard(),
            new TalentSlotConfig { SlotTalentIds = main, ReserveTalentIds = reserve },
            preset);
        if (!PlayerLoadoutCodec.TryDecode(message, preset, out TrustedPlayerLoadout loadout, out string error))
            throw new InvalidOperationException($"Invalid AI policy fixture: {error}");
        return loadout;
    }
}

[TalentRule("network_test_charge_control_capability", "Capability Control", "test",
    TalentTier.Small, 0)]
internal sealed class CapabilityControlTalent : TalentRule, IPublicChargeControlTalent
{
}

[TalentRule("network_test_charge_defense_capability", "Capability Defense", "test",
    TalentTier.Small, 0)]
internal sealed class CapabilityDefenseTalent : TalentRule, IPublicChargeDefenseTalent
{
}
