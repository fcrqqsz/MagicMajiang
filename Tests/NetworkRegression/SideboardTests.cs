using System.Collections.Generic;
using System;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Talents;

internal static class SideboardTests
{
    public static void Run(RegressionRunner runner)
    {
        TestDraftPolicyEditsImmutableLocalCopy(runner);
        TestDraftPolicyRejectsLockedUnknownDuplicateAndUncarriedIds(runner);
        TestDraftPolicyUsesCanonicalOrderWithoutFixedActiveCount(runner);
        TestDraftPolicyAllowsOverCapEditingButBlocksLock(runner);
        TestDraftPolicyDeepCopiesStartedAndRecoversReadOnly(runner);
        TestDraftPolicyRejectsMalformedStartedSelections(runner);
        TestDraftPolicyMatchesTrustedLoadoutSlotAdmission(runner);
        TestPanelStateSubmitsOnceAndNeverReopensLockedDecision(runner);
        TestPanelStateKeepsCompletedProgressUntilAuthoritativeReset(runner);
        TestPanelStateKeepsOverCapDraftEditableButCannotSubmit(runner);
        TestPanelStateRejectsWrongSeatPrivateStartAndRecoversReadOnly(runner);
        TestRecoveryRoutesActiveSideboardToGame(runner);
        TestRemoteProxyPublishesOnlySequenceGatedSideboardEvents(runner);
        TestPhaseEntryByGameMode(runner);
        TestDecisionTrackerCopiesAndLocksSelections(runner);
        TestLoadoutPolicyNormalizesAndCalculatesTotal(runner);
        TestLoadoutPolicyRejectsEveryInvalidShape(runner);
        TestLoadoutPolicyEnforcesLockedTalentsAndBudget(runner);
        TestRoomOpensSideboardBeforeNextRoundAndAppliesSelection(runner);
        TestFourthRoundBustSkipsSideboard(runner);
        TestInvalidSelectionLocksOriginalAndCannotRetry(runner);
        TestSideboardMessagesKeepOtherSeatsPrivate(runner);
        TestDisconnectLocksOriginal(runner);
        TestTimeoutLocksOriginal(runner);
        TestRoomManagerRoutesSubmitAndDeadline(runner);
    }

    private static void TestFourthRoundBustSkipsSideboard(RegressionRunner runner)
    {
        TrustedPlayerLoadout loadout = CreateTrustedLoadout(
            new string[TalentSlotConfig.MainSlotCount],
            new string[TalentSlotConfig.ReserveSlotCount]);
        using Room room = CreateStartedRoom(
            "sideboard-bust", GameMode.HalfGame, loadout, out GameEndpoint endpoint);

        CompleteRounds(room, 3, "host");
        room.SetReady("host", ReadyPhase.NextRound, out _);
        room.Session.Scores[0] = 0;
        room.GameServer?.CompleteDrawRound();

        runner.Check(room.State == RoomState.Closed,
            $"a fourth-round depleted score closes the room (actual={room.State})");
        runner.Check(room.Session.TotalRoundsPlayed == 4
                     && room.Session.EndReason == SessionEndReason.ScoreDepleted,
            $"a fourth-round depleted score sets terminal progress (rounds={room.Session.TotalRoundsPlayed}, reason={room.Session.EndReason})");
        runner.Check(endpoint.SentMessages
                .Select(MessageSerializer.DeserializeEnvelope)
                .All(envelope => envelope?.type != "SideboardStarted"),
            "a fourth-round depleted score ends the match before the halftime sideboard can open");
    }

    private static void TestDraftPolicyEditsImmutableLocalCopy(RegressionRunner runner)
    {
        SideboardStartedMessage started = CreateDraftStarted();
        SideboardDraft original = SideboardDraftPolicy.Create(started);
        SideboardDraft changed = SideboardDraftPolicy.SetActive(
            original, "interception", true, TalentRegistry.Instance);
        SideboardDraft disabled = SideboardDraftPolicy.SetActive(
            changed, "draw_reward", false, TalentRegistry.Instance);

        runner.Check(!ReferenceEquals(original, changed)
                     && !original.ActiveTalentIds.Contains("interception")
                     && changed.ActiveTalentIds.Contains("interception")
                     && !disabled.ActiveTalentIds.Contains("draw_reward")
                     && changed.ActiveTalentIds.Contains("draw_reward"),
            "sideboard add and disable edits return immutable local drafts");

        SideboardDraft lockedAttempt = SideboardDraftPolicy.SetActive(
            changed, "starting_capital", false, TalentRegistry.Instance);
        runner.Check(lockedAttempt.ErrorCode == SideboardDraftErrorCodes.LockedTalent
                     && lockedAttempt.ActiveTalentIds.Contains("starting_capital")
                     && changed.ActiveTalentIds.Contains("starting_capital"),
            "locked main talent cannot be disabled locally");
    }

    private static void TestDraftPolicyRejectsLockedUnknownDuplicateAndUncarriedIds(RegressionRunner runner)
    {
        SideboardStartedMessage started = CreateDraftStarted();
        SideboardDraft original = SideboardDraftPolicy.Create(started);
        SideboardDraft unknown = SideboardDraftPolicy.SetActive(
            original, "not_registered", true, TalentRegistry.Instance);
        SideboardDraft uncarried = SideboardDraftPolicy.SetActive(
            original, "peek", true, TalentRegistry.Instance);
        SideboardDraft duplicate = SideboardDraftPolicy.ReplaceActive(
            original,
            new[] { "starting_capital", "draw_reward", "draw_reward" },
            TalentRegistry.Instance);

        runner.Check(unknown.ErrorCode == SideboardDraftErrorCodes.UnknownTalent
                     && uncarried.ErrorCode == SideboardDraftErrorCodes.NotCarried
                     && duplicate.ErrorCode == SideboardDraftErrorCodes.DuplicateTalent
                     && original.ActiveTalentIds.SequenceEqual(new[] { "starting_capital", "draw_reward" }),
            "sideboard draft rejects unknown, uncarried, and duplicate active ids without mutating its source");
    }

    private static void TestDraftPolicyUsesCanonicalOrderWithoutFixedActiveCount(RegressionRunner runner)
    {
        SideboardStartedMessage started = CreateDraftStarted();
        SideboardDraft draft = SideboardDraftPolicy.ReplaceActive(
            SideboardDraftPolicy.Create(started),
            new[] { "interception", "starting_capital", "midas_touch" },
            TalentRegistry.Instance);
        SideboardDraft onlyLocked = SideboardDraftPolicy.ReplaceActive(
            draft,
            new[] { "starting_capital" },
            TalentRegistry.Instance);

        runner.Check(draft.ActiveTalentIds.SequenceEqual(new[]
                     {
                         "starting_capital", "midas_touch", "interception"
                     })
                     && onlyLocked.ActiveTalentIds.SequenceEqual(new[] { "starting_capital" })
                     && onlyLocked.CanLock,
            "sideboard canonicalizes by carried slot order and never requires six active talents");
    }

    private static void TestDraftPolicyAllowsOverCapEditingButBlocksLock(RegressionRunner runner)
    {
        SideboardStartedMessage started = CreateDraftStarted();
        started.currentTotalAlienation = 35;
        SideboardDraft overCap = SideboardDraftPolicy.SetActive(
            SideboardDraftPolicy.Create(started),
            "midas_touch",
            true,
            TalentRegistry.Instance);

        runner.Check(overCap.ActiveTalentIds.Contains("midas_touch")
                     && overCap.TotalAlienation == 50
                     && overCap.AlienationLimit == 40
                     && overCap.IsOverLimit
                     && !overCap.CanLock
                     && overCap.ErrorCode == SideboardDraftErrorCodes.AlienationLimitExceeded,
            "over-cap sideboard remains editable but exposes a warning and cannot lock");
    }

    private static void TestDraftPolicyDeepCopiesStartedAndRecoversReadOnly(RegressionRunner runner)
    {
        SideboardStartedMessage started = CreateDraftStarted();
        started.currentTotalAlienation = 18;
        SideboardDraft draft = SideboardDraftPolicy.Create(started);
        started.carriedMainTalentIds[3] = "mutated_main";
        started.carriedReserveTalentIds[0] = "mutated_reserve";
        started.currentActiveTalentIds[0] = "mutated_active";

        SideboardDraft recovery = SideboardDraftPolicy.CreateReadOnly(new SnapshotSideboardState
        {
            isActive = true,
            decisionId = 923,
            deadlineUnixMilliseconds = 5000,
            ownLocked = true,
            seatLocked = new[] { true, false, true, false }
        });
        SideboardDraft recoveryEdit = SideboardDraftPolicy.SetActive(
            recovery, "peek", true, TalentRegistry.Instance);

        runner.Check(draft.CarriedMainTalentIds.Contains("starting_capital")
                     && draft.CarriedReserveTalentIds.Contains("interception")
                     && draft.ActiveTalentIds.Contains("starting_capital")
                     && draft.DeckAlienation == 10
                     && draft.ActiveTalentAlienation == 8
                     && draft.TotalAlienation == 18
                     && draft.AlienationLimit == 40,
            "sideboard draft deep-copies every private Started collection");
        runner.Check(recovery.IsReadOnly
                     && recovery.DecisionId == 923
                     && recovery.SeatLocked.SequenceEqual(new[] { true, false, true, false })
                     && recoveryEdit.ErrorCode == SideboardDraftErrorCodes.ReadOnly
                     && !recoveryEdit.ActiveTalentIds.Contains("peek"),
            "own-locked recovery opens a read-only wait state without rebuilding a private editable draft");
    }

    private static SideboardStartedMessage CreateDraftStarted() => new SideboardStartedMessage
    {
        decisionId = 901,
        deadlineUnixMilliseconds = 123456789,
        carriedMainTalentIds = new[]
        {
            null, null, null, "starting_capital", "draw_reward", null
        },
        carriedReserveTalentIds = new[] { "midas_touch", "interception", null },
        currentActiveTalentIds = new[] { "draw_reward", "starting_capital" },
        alienationLimit = 40,
        currentTotalAlienation = 8
    };

    private static void TestDraftPolicyRejectsMalformedStartedSelections(RegressionRunner runner)
    {
        SideboardStartedMessage unknownCarried = CreateDraftStarted();
        unknownCarried.carriedReserveTalentIds[2] = "not_registered";
        SideboardStartedMessage duplicateCarried = CreateDraftStarted();
        duplicateCarried.carriedReserveTalentIds[2] = "draw_reward";
        SideboardStartedMessage uncarriedActive = CreateDraftStarted();
        uncarriedActive.currentActiveTalentIds = new[] { "starting_capital", "peek" };

        SideboardDraft unknown = SideboardDraftPolicy.Create(unknownCarried);
        SideboardDraft duplicate = SideboardDraftPolicy.Create(duplicateCarried);
        SideboardDraft uncarried = SideboardDraftPolicy.Create(uncarriedActive);

        runner.Check(unknown.ErrorCode == SideboardDraftErrorCodes.UnknownTalent
                     && unknown.IsReadOnly && !unknown.CanLock
                     && duplicate.ErrorCode == SideboardDraftErrorCodes.DuplicateTalent
                     && duplicate.IsReadOnly && !duplicate.CanLock
                     && uncarried.ErrorCode == SideboardDraftErrorCodes.NotCarried
                     && uncarried.IsReadOnly && !uncarried.CanLock,
            "malformed private Started ids degrade to readonly and can never become a lockable local draft");
    }

    private static void TestDraftPolicyMatchesTrustedLoadoutSlotAdmission(RegressionRunner runner)
    {
        SideboardStartedMessage wrongShape = CreateDraftStarted();
        wrongShape.carriedMainTalentIds = wrongShape.carriedMainTalentIds.Take(5).ToArray();

        SideboardStartedMessage incompatibleMainTier = CreateDraftStarted();
        incompatibleMainTier.carriedMainTalentIds[3] = "midas_touch";
        incompatibleMainTier.carriedReserveTalentIds[0] = null;
        incompatibleMainTier.currentActiveTalentIds = new[] { "midas_touch", "draw_reward" };

        SideboardStartedMessage nonFlexibleReserve = CreateDraftStarted();
        nonFlexibleReserve.carriedMainTalentIds[3] = null;
        nonFlexibleReserve.carriedReserveTalentIds[1] = "starting_capital";
        nonFlexibleReserve.currentActiveTalentIds = new[] { "draw_reward", "starting_capital" };

        SideboardStartedMessage lockedTalentMissing = CreateDraftStarted();
        lockedTalentMissing.currentActiveTalentIds = new[] { "draw_reward" };

        SideboardStartedMessage[] malformed =
        {
            wrongShape, incompatibleMainTier, nonFlexibleReserve, lockedTalentMissing
        };
        SideboardDraft[] drafts = malformed.Select(SideboardDraftPolicy.Create).ToArray();
        bool[] serverAccepted = malformed.Take(3).Select(started =>
            PlayerLoadoutCodec.TryDecode(CreateAdmissionMessage(started), out _, out _)).ToArray();
        TrustedPlayerLoadout lockedLoadout = CreateTrustedLoadout(
            lockedTalentMissing.carriedMainTalentIds,
            lockedTalentMissing.carriedReserveTalentIds);
        bool serverAcceptedMissingLocked = SideboardLoadoutPolicy.TryValidate(
            lockedLoadout,
            lockedTalentMissing.currentActiveTalentIds,
            AlienationPreset.Low,
            TalentRegistry.Instance,
            out _, out _, out _);
        SideboardPanelViewState malformedPanel = SideboardPanelStatePolicy.OpenStarted(
            SideboardPanelViewState.Closed, wrongShape, receivedSeatIndex: 1, localSeatIndex: 1);
        bool malformedSubmitted = SideboardPanelStatePolicy.TryBeginSubmit(
            malformedPanel, out SideboardPanelViewState unchangedMalformedPanel, out _);

        runner.Check(drafts.All(draft => draft.IsReadOnly && !draft.CanLock)
                     && drafts.Take(3).All(draft => draft.ErrorCode == SideboardDraftErrorCodes.InvalidSelection)
                     && drafts[3].ErrorCode == SideboardDraftErrorCodes.LockedTalent
                     && serverAccepted.All(accepted => !accepted)
                     && !serverAcceptedMissingLocked
                     && malformedPanel.IsReadOnly
                     && malformedPanel.IsVisible
                     && !malformedSubmitted
                     && ReferenceEquals(malformedPanel, unchangedMalformedPanel),
            "Started uses the same strict 6+3 slot admission as trusted server loadouts and requires every locked carried talent active");
    }

    private static PlayerLoadoutMessage CreateAdmissionMessage(SideboardStartedMessage started)
    {
        PlayerLoadoutMessage message = PlayerLoadoutCodec.CreateMessage(
            DeckConfig.CreateStandard(), new TalentSlotConfig(), AlienationPreset.Low);
        message.mainTalentSlotIds = started.carriedMainTalentIds?.ToArray();
        message.reserveTalentSlotIds = started.carriedReserveTalentIds?.ToArray();
        return message;
    }

    private static void TestPanelStateSubmitsOnceAndNeverReopensLockedDecision(RegressionRunner runner)
    {
        SideboardPanelViewState editable = SideboardPanelStatePolicy.OpenStarted(
            SideboardPanelViewState.Closed,
            CreateDraftStarted(),
            receivedSeatIndex: 2,
            localSeatIndex: 2);
        bool began = SideboardPanelStatePolicy.TryBeginSubmit(
            editable,
            out SideboardPanelViewState pending,
            out string[] submittedIds);
        bool duplicate = SideboardPanelStatePolicy.TryBeginSubmit(
            pending,
            out SideboardPanelViewState duplicateState,
            out _);
        SideboardPanelViewState locked = SideboardPanelStatePolicy.ApplyLocked(
            pending,
            new SideboardLockedMessage
            {
                decisionId = 901,
                acceptedSelection = false,
                reason = "invalid",
                ownTotalAlienation = 8
            });
        SideboardPanelViewState staleStarted = SideboardPanelStatePolicy.OpenStarted(
            locked,
            CreateDraftStarted(),
            receivedSeatIndex: 2,
            localSeatIndex: 2);
        SideboardPanelViewState progressed = SideboardPanelStatePolicy.ApplyProgress(
            staleStarted,
            new SideboardProgressMessage
            {
                decisionId = 901,
                isComplete = false,
                seats = new[]
                {
                    new SideboardSeatLockStateMessage { seatIndex = 0, locked = true },
                    new SideboardSeatLockStateMessage { seatIndex = 1, locked = false },
                    new SideboardSeatLockStateMessage { seatIndex = 2, locked = true },
                    new SideboardSeatLockStateMessage { seatIndex = 3, locked = false }
                }
            });

        runner.Check(editable.IsVisible && editable.IsEditable
                     && began
                     && pending.IsSubmissionPending
                     && pending.IsReadOnly
                     && submittedIds.SequenceEqual(new[] { "starting_capital", "draw_reward" })
                     && !duplicate
                     && ReferenceEquals(pending, duplicateState),
            "sideboard panel disables input immediately and emits a lock selection only once");
        runner.Check(locked.PrivateDraft == null
                     && locked.IsReadOnly
                     && locked.LockReason == "invalid"
                     && staleStarted.PrivateDraft == null
                     && staleStarted.IsReadOnly
                     && progressed.SeatLocked.SequenceEqual(new[] { true, false, true, false }),
            "authoritative lock discards the draft and a stale Started cannot reopen an invalidly locked selection");
    }

    private static void TestPanelStateRejectsWrongSeatPrivateStartAndRecoversReadOnly(RegressionRunner runner)
    {
        SideboardPanelViewState wrongSeat = SideboardPanelStatePolicy.OpenStarted(
            SideboardPanelViewState.Closed,
            CreateDraftStarted(),
            receivedSeatIndex: 1,
            localSeatIndex: 2);
        SideboardPanelViewState recovery = SideboardPanelStatePolicy.Recover(
            new SnapshotSideboardState
            {
                isActive = true,
                decisionId = 950,
                deadlineUnixMilliseconds = 6500,
                ownLocked = true,
                seatLocked = new[] { false, true, true, true }
            });
        SideboardPanelViewState pendingPrivateRecovery = SideboardPanelStatePolicy.Recover(
            new SnapshotSideboardState
            {
                isActive = true,
                decisionId = 951,
                deadlineUnixMilliseconds = 7000,
                ownLocked = false,
                seatLocked = new[] { false, true, true, true }
            });
        SideboardStartedMessage resumedStarted = CreateDraftStarted();
        resumedStarted.decisionId = 951;
        SideboardPanelViewState resumedEditing = SideboardPanelStatePolicy.OpenStarted(
            pendingPrivateRecovery,
            resumedStarted,
            receivedSeatIndex: 2,
            localSeatIndex: 2);

        runner.Check(!wrongSeat.IsVisible && wrongSeat.PrivateDraft == null,
            "a private SideboardStarted payload tagged for another seat cannot populate local cards");
        runner.Check(recovery.IsVisible
                     && recovery.IsReadOnly
                     && recovery.PrivateDraft == null
                     && recovery.DecisionId == 950
                     && recovery.SeatLocked.SequenceEqual(new[] { false, true, true, true }),
            "own-locked snapshot recovery restores only the readonly confirmation state");
        runner.Check(pendingPrivateRecovery.IsReadOnly
                     && pendingPrivateRecovery.PrivateDraft == null
                     && resumedEditing.IsEditable
                     && resumedEditing.PrivateDraft != null,
            "an unlocked recovery waits readonly until the ordered private Started restores editing");
    }

    private static void TestPanelStateKeepsCompletedProgressUntilAuthoritativeReset(RegressionRunner runner)
    {
        SideboardPanelViewState started = SideboardPanelStatePolicy.OpenStarted(
            SideboardPanelViewState.Closed, CreateDraftStarted(), 2, 2);
        SideboardPanelViewState locked = SideboardPanelStatePolicy.ApplyLocked(started,
            new SideboardLockedMessage { decisionId = 901, acceptedSelection = true, reason = "accepted" });
        SideboardPanelViewState complete = SideboardPanelStatePolicy.ApplyProgress(locked,
            new SideboardProgressMessage
            {
                decisionId = 901,
                isComplete = true,
                seats = Enumerable.Range(0, 4)
                    .Select(index => new SideboardSeatLockStateMessage { seatIndex = index, locked = true })
                    .ToArray()
            });
        SideboardPanelViewState reset = SideboardPanelStatePolicy.Reset(complete);

        runner.Check(complete.IsVisible
                     && complete.IsReadOnly
                     && complete.PrivateDraft == null
                     && complete.IsComplete
                     && complete.SeatLocked.SequenceEqual(new[] { true, true, true, true }),
            "complete public progress remains visible readonly with all four confirmations after the private lock");
        runner.Check(!reset.IsVisible && !reset.IsComplete,
            "the next authoritative phase can explicitly reset and close the completed sideboard");
    }

    private static void TestRecoveryRoutesActiveSideboardToGame(RegressionRunner runner)
    {
        runner.Check(ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.WaitingForSideboard)
                         == ClientRecoverySceneTarget.Game
                     && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.WaitingForNextRound)
                         == ClientRecoverySceneTarget.Game
                     && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.WaitingForPlayers)
                         == ClientRecoverySceneTarget.Lobby,
            "recovery routes an active sideboard to Game without changing adjacent waiting-room semantics");
    }

    private static void TestPanelStateKeepsOverCapDraftEditableButCannotSubmit(RegressionRunner runner)
    {
        SideboardStartedMessage started = CreateDraftStarted();
        started.currentTotalAlienation = 35;
        SideboardPanelViewState state = SideboardPanelStatePolicy.OpenStarted(
            SideboardPanelViewState.Closed,
            started,
            receivedSeatIndex: 0,
            localSeatIndex: 0);
        SideboardDraft overCap = SideboardDraftPolicy.SetActive(
            state.PrivateDraft,
            "midas_touch",
            true,
            TalentRegistry.Instance);
        state = SideboardPanelStatePolicy.UpdateDraft(state, overCap);
        bool submitted = SideboardPanelStatePolicy.TryBeginSubmit(state, out _, out _);
        SideboardDraft adjusted = SideboardDraftPolicy.SetActive(
            state.PrivateDraft,
            "midas_touch",
            false,
            TalentRegistry.Instance);

        runner.Check(state.IsEditable
                     && state.PrivateDraft.IsOverLimit
                     && !state.PrivateDraft.CanLock
                     && !submitted
                     && adjusted.CanLock,
            "over-cap panel cards remain editable while the lock action stays unavailable");
    }

    private static void TestRemoteProxyPublishesOnlySequenceGatedSideboardEvents(RegressionRunner runner)
    {
        WebSocketClient.ResetForTests();
        using var service = new ClientRoomService("ws://test", new InMemoryClientReconnectTicketStore());
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("RoomJoined", 1, new RoomJoinedMessage
        {
            roomId = "sideboard-client-room",
            streamId = "sideboard-client-stream",
            seatIndex = 2,
            gameMode = (int)GameMode.HalfGame,
            alienationPreset = (int)AlienationPreset.Low,
            roomState = (int)RoomState.WaitingForSideboard,
            seats = Array.Empty<RoomSeatMessage>()
        }));
        using var proxy = new SideboardProxyLifetime(new RemoteServerProxy(new SimpleAIClient(2, null), service));
        int startedCount = 0;
        int lockedCount = 0;
        int progressCount = 0;
        int deliveredSeat = -1;
        proxy.Value.SideboardStartedReceived += (seat, _) =>
        {
            deliveredSeat = seat;
            startedCount++;
        };
        proxy.Value.SideboardLockedReceived += (seat, _) =>
        {
            deliveredSeat = seat;
            lockedCount++;
        };
        proxy.Value.SideboardProgressReceived += _ => progressCount++;

        SideboardStartedMessage started = CreateDraftStarted();
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("SideboardStarted", 2, started));
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("SideboardStarted", 2, started));
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("SideboardProgress", 4,
            new SideboardProgressMessage { decisionId = 901, seats = Array.Empty<SideboardSeatLockStateMessage>() }));

        runner.Check(startedCount == 1
                     && lockedCount == 0
                     && progressCount == 0
                     && deliveredSeat == 2
                     && service.IsResyncRequired,
            "RemoteServerProxy publishes sideboard presentation only after the duplicate and gap sequence gate");

        proxy.Value.Cleanup();
        WebSocketClient.ResetForTests();
        using var cleanupService = new ClientRoomService("ws://test", new InMemoryClientReconnectTicketStore());
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("RoomJoined", 1, new RoomJoinedMessage
        {
            roomId = "sideboard-cleanup-room",
            streamId = "sideboard-cleanup-stream",
            seatIndex = 2,
            gameMode = (int)GameMode.HalfGame,
            alienationPreset = (int)AlienationPreset.Low,
            roomState = (int)RoomState.WaitingForSideboard,
            seats = Array.Empty<RoomSeatMessage>()
        }));
        var cleanupProxy = new RemoteServerProxy(new SimpleAIClient(2, null), cleanupService);
        int cleanupLockedCount = 0;
        int resetCount = 0;
        cleanupProxy.SideboardLockedReceived += (_, _) => cleanupLockedCount++;
        cleanupProxy.SideboardResetRequested += () => resetCount++;
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("RoundStart", 2,
            new RoundStartMessage
            {
                roundNumber = 5,
                prevalentWind = (int)WindDirection.South,
                seatWind = (int)WindDirection.West,
                dealerIndex = 0,
                scores = new[] { 0, 0, 0, 0 }
            }));
        cleanupProxy.Cleanup();
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("SideboardLocked", 3,
            new SideboardLockedMessage { decisionId = 901 }));
        runner.Check(resetCount == 1 && cleanupLockedCount == 0 && !cleanupService.IsResyncRequired,
            "RoundStart explicitly resets sideboard presentation and proxy cleanup unsubscribes its accepted stream");
        WebSocketClient.ResetForTests();
    }

    private sealed class SideboardProxyLifetime : IDisposable
    {
        public RemoteServerProxy Value { get; }

        public SideboardProxyLifetime(RemoteServerProxy value) => Value = value;

        public void Dispose() => Value.Cleanup();
    }

    private static void TestPhaseEntryByGameMode(RegressionRunner runner)
    {
        runner.Check(!SideboardPhasePolicy.ShouldOpen(GameMode.Single, completedRounds: 1),
            "single game has no sideboard");
        runner.Check(!SideboardPhasePolicy.ShouldOpen(GameMode.EastOnly, completedRounds: 4),
            "east-only has no sideboard");
        runner.Check(SideboardPhasePolicy.ShouldOpen(GameMode.HalfGame, completedRounds: 4),
            "half game opens sideboard after round four");
        runner.Check(SideboardPhasePolicy.ShouldOpen(GameMode.FullGame, completedRounds: 4),
            "full game opens sideboard once after round four");
        runner.Check(!SideboardPhasePolicy.ShouldOpen(GameMode.FullGame, completedRounds: 8),
            "full game does not reopen sideboard later");
    }

    private static void TestDecisionTrackerCopiesAndLocksSelections(RegressionRunner runner)
    {
        string[] seatZeroOriginal = { "starting_capital", "draw_reward" };
        var originals = new IReadOnlyCollection<string>[]
        {
            seatZeroOriginal,
            new[] { "peek" },
            new string[0],
            new[] { "head_start" }
        };
        var tracker = new SideboardDecisionTracker(
            decisionId: 4001,
            deadlineUnixMilliseconds: 123456789,
            originals);
        seatZeroOriginal[0] = "mutated_after_construction";

        string[] submitted = { "starting_capital", "peek" };
        bool accepted = tracker.TrySubmit(0, submitted, out string submitError);
        submitted[1] = "mutated_after_submit";
        bool duplicate = tracker.TrySubmit(0, new[] { "draw_reward" }, out string duplicateError);
        tracker.LockOriginal(1, "ai_default");
        tracker.LockOriginal(2, "timeout");
        tracker.LockOriginal(3, "disconnected");

        runner.Check(accepted
                     && submitError == null
                     && tracker.DecisionId == 4001
                     && tracker.DeadlineUnixMilliseconds == 123456789
                     && tracker.GetSelectedActiveTalentIds(0).SequenceEqual(new[]
                     {
                         "starting_capital", "peek"
                     })
                     && tracker.GetOriginalActiveTalentIds(0).SequenceEqual(new[]
                     {
                         "starting_capital", "draw_reward"
                     }),
            "sideboard tracker copies original and submitted active sets before locking a seat");
        runner.Check(!duplicate
                     && duplicateError == SideboardErrorCodes.AlreadyLocked
                     && tracker.AllLocked
                     && tracker.GetSelectedActiveTalentIds(1).SequenceEqual(new[] { "peek" })
                     && tracker.GetLockReason(1) == "ai_default",
            "sideboard tracker locks each seat once and defaults non-submitters to their copied original set");
    }

    private static void TestLoadoutPolicyNormalizesAndCalculatesTotal(RegressionRunner runner)
    {
        TrustedPlayerLoadout loadout = CreateTrustedLoadout(
            new[] { null, null, null, "starting_capital", "draw_reward", null },
            new[] { null, "peek", null });

        bool accepted = SideboardLoadoutPolicy.TryValidate(
            loadout,
            new[] { " peek ", "starting_capital" },
            AlienationPreset.Low,
            TalentRegistry.Instance,
            out string[] normalized,
            out int total,
            out string errorCode);

        runner.Check(accepted
                     && errorCode == null
                     && normalized.SequenceEqual(new[] { "starting_capital", "peek" })
                     && total == 10,
            "a valid sideboard selection is trimmed, normalized by the original nine slots, and priced exactly");

        TrustedPlayerLoadout emptyLoadout = CreateTrustedLoadout(
            new string[TalentSlotConfig.MainSlotCount],
            new string[TalentSlotConfig.ReserveSlotCount]);
        runner.Check(SideboardLoadoutPolicy.TryValidate(
                         emptyLoadout,
                         new string[0],
                         AlienationPreset.Low,
                         TalentRegistry.Instance,
                         out string[] emptyNormalized,
                         out int emptyTotal,
                         out _)
                     && emptyNormalized.Length == 0
                     && emptyTotal == 0,
            "an empty active set is valid when the carried loadout has no locked talent");
    }

    private static void TestLoadoutPolicyRejectsEveryInvalidShape(RegressionRunner runner)
    {
        TrustedPlayerLoadout loadout = CreateTrustedLoadout(
            new[] { null, null, null, "draw_reward", null, null },
            new[] { null, "peek", null });

        bool rejectsNull = !SideboardLoadoutPolicy.TryValidate(
            loadout, null, AlienationPreset.Low, TalentRegistry.Instance, out _, out _, out _);
        bool rejectsTooMany = !SideboardLoadoutPolicy.TryValidate(
            loadout, Enumerable.Repeat("draw_reward", 10).ToArray(), AlienationPreset.Low,
            TalentRegistry.Instance, out _, out _, out _);
        bool rejectsUnknown = !SideboardLoadoutPolicy.TryValidate(
            loadout, new[] { "not_carried" }, AlienationPreset.Low,
            TalentRegistry.Instance, out _, out _, out _);
        bool rejectsDuplicate = !SideboardLoadoutPolicy.TryValidate(
            loadout, new[] { "peek", " peek " }, AlienationPreset.Low,
            TalentRegistry.Instance, out _, out _, out _);

        runner.Check(rejectsNull && rejectsTooMany && rejectsUnknown && rejectsDuplicate,
            "sideboard rejects null, overlong, uncarried, and duplicate active-id submissions");
    }

    private static void TestLoadoutPolicyEnforcesLockedTalentsAndBudget(RegressionRunner runner)
    {
        TrustedPlayerLoadout lockedLoadout = CreateTrustedLoadout(
            new[] { null, null, null, "starting_capital", null, null },
            new[] { "midas_touch", null, null });
        bool rejectsMissingLocked = !SideboardLoadoutPolicy.TryValidate(
            lockedLoadout, new[] { "midas_touch" }, AlienationPreset.Low,
            TalentRegistry.Instance, out _, out _, out string lockedError);

        DeckConfig expensiveDeck = DeckConfig.CreateStandard();
        ConfigureThirtyAlienationDeck(expensiveDeck);
        TrustedPlayerLoadout budgetLoadout = CreateTrustedLoadout(
            new[] { null, null, null, "starting_capital", null, null },
            new[] { "midas_touch", null, null },
            expensiveDeck);
        bool rejectsOverBudget = !SideboardLoadoutPolicy.TryValidate(
            budgetLoadout, new[] { "starting_capital", "midas_touch" }, AlienationPreset.Low,
            TalentRegistry.Instance, out string[] rejectedNormalized, out int rejectedTotal,
            out string budgetError);

        runner.Check(rejectsMissingLocked && lockedError == SideboardErrorCodes.LockedTalentMissing,
            "every carried MainOnlyLocked talent remains active after sideboarding");
        runner.Check(rejectsOverBudget
                     && budgetError == SideboardErrorCodes.AlienationLimitExceeded
                     && rejectedNormalized.Length == 0
                     && rejectedTotal == 50,
            "sideboard budget validation uses the immutable deck plus only the requested active talents");
    }

    private static void TestRoomOpensSideboardBeforeNextRoundAndAppliesSelection(RegressionRunner runner)
    {
        TrustedPlayerLoadout loadout = CreateTrustedLoadout(
            new[] { null, null, null, "starting_capital", "draw_reward", null },
            new[] { null, "peek", null });
        using Room room = CreateStartedRoom("sideboard-phase", GameMode.HalfGame, loadout,
            out GameEndpoint endpoint);

        CompleteRounds(room, 4, "host");
        SideboardStartedMessage started = GetLastMessage<SideboardStartedMessage>(endpoint, "SideboardStarted");
        SideboardProgressMessage progress = GetLastMessage<SideboardProgressMessage>(endpoint, "SideboardProgress");
        long remainingMilliseconds = started.deadlineUnixMilliseconds - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        runner.Check(room.State == RoomState.WaitingForSideboard
                     && room.Session.TotalRoundsPlayed == 4
                     && progress?.seats[0].locked == false
                     && progress.seats[1].locked
                     && progress.seats[2].locked
                     && progress.seats[3].locked
                     && remainingMilliseconds > 44000
                     && remainingMilliseconds <= 45000,
            "round four enters sideboard first while all AI seats immediately lock their original sets");
        runner.Check(started != null
                     && started.decisionId == progress.decisionId
                     && started.carriedMainTalentIds.SequenceEqual(loadout.TalentConfig.SlotTalentIds)
                     && started.carriedReserveTalentIds.SequenceEqual(loadout.TalentConfig.ReserveTalentIds)
                     && started.currentActiveTalentIds.SequenceEqual(new[]
                     {
                         "starting_capital", "draw_reward"
                     })
                     && started.alienationLimit == 40
                     && started.currentTotalAlienation == 8,
            "SideboardStarted privately carries the owner's slots, canonical active set, deadline, limit, and exact total");

        bool submitted = room.SubmitSideboard(0, new SideboardSubmitMessage
        {
            decisionId = started.decisionId,
            activeTalentIds = new[] { "peek", "starting_capital" }
        }, out string errorCode);
        SideboardLockedMessage locked = GetLastMessage<SideboardLockedMessage>(endpoint, "SideboardLocked");

        runner.Check(submitted
                     && errorCode == null
                     && room.State == RoomState.InRound
                     && locked?.acceptedSelection == true
                     && locked.reason == "accepted"
                     && locked.ownTotalAlienation == 10,
            "a valid selection atomically replaces the runtime set, privately locks the owner, then starts the second half without another ready gate");
        runner.Check(room.Session.Scores[0] == 200,
            "sideboarding does not replay StartingCapital or any match-start effect");

        room.GameServer?.CompleteDrawRound();
        runner.Check(room.Session.TotalRoundsPlayed == 5 && room.Session.Scores[0] == 200,
            "the next round uses the replacement set, with the deactivated draw reward no longer firing");
    }

    private static void TestInvalidSelectionLocksOriginalAndCannotRetry(RegressionRunner runner)
    {
        TrustedPlayerLoadout hostLoadout = CreateTrustedLoadout(
            new[] { null, null, null, "starting_capital", "draw_reward", null },
            new[] { null, "peek", null });
        TrustedPlayerLoadout guestLoadout = CreateTrustedLoadout(
            new string[TalentSlotConfig.MainSlotCount],
            new string[TalentSlotConfig.ReserveSlotCount]);
        using Room room = CreateStartedTwoHumanRoom(
            "sideboard-invalid", hostLoadout, guestLoadout, out GameEndpoint host, out _);
        CompleteRounds(room, 4, "host", "guest");
        long decisionId = GetLastMessage<SideboardStartedMessage>(host, "SideboardStarted").decisionId;

        bool invalidAccepted = room.SubmitSideboard(0, new SideboardSubmitMessage
        {
            decisionId = decisionId,
            activeTalentIds = new[] { "peek" }
        }, out string invalidError);
        bool retryAccepted = room.SubmitSideboard(0, new SideboardSubmitMessage
        {
            decisionId = decisionId,
            activeTalentIds = new[] { "starting_capital", "peek" }
        }, out string retryError);
        SideboardLockedMessage locked = GetLastMessage<SideboardLockedMessage>(host, "SideboardLocked");

        runner.Check(!invalidAccepted
                     && invalidError == SideboardErrorCodes.InvalidSelection
                     && room.State == RoomState.WaitingForSideboard
                     && room.GameServer.TalentRuntime.GetActiveTalentIds(0).SequenceEqual(new[]
                     {
                         "starting_capital", "draw_reward"
                     })
                     && locked?.acceptedSelection == false
                     && locked.reason == "invalid"
                     && locked.ownTotalAlienation == 8,
            "an invalid submission leaves runtime unchanged and immediately locks the original selection");
        runner.Check(!retryAccepted && retryError == SideboardErrorCodes.AlreadyLocked,
            "an invalidly locked player cannot probe the validator with another submission");
    }

    private static void TestSideboardMessagesKeepOtherSeatsPrivate(RegressionRunner runner)
    {
        TrustedPlayerLoadout hostLoadout = CreateTrustedLoadout(
            new[] { null, null, null, "draw_reward", null, null },
            new[] { null, "peek", null });
        TrustedPlayerLoadout guestLoadout = CreateTrustedLoadout(
            new string[TalentSlotConfig.MainSlotCount],
            new string[TalentSlotConfig.ReserveSlotCount]);
        using Room room = CreateStartedTwoHumanRoom(
            "sideboard-private", hostLoadout, guestLoadout, out GameEndpoint host, out GameEndpoint guest);
        CompleteRounds(room, 4, "host", "guest");

        SideboardStartedMessage hostStarted = GetLastMessage<SideboardStartedMessage>(host, "SideboardStarted");
        SideboardStartedMessage guestStarted = GetLastMessage<SideboardStartedMessage>(guest, "SideboardStarted");
        int guestMessageCount = guest.SentMessages.Count;
        room.SubmitSideboard(0, new SideboardSubmitMessage
        {
            decisionId = hostStarted.decisionId,
            activeTalentIds = new[] { "peek" }
        }, out _);
        NetworkMessageEnvelope[] guestNewMessages = guest.SentMessages.Skip(guestMessageCount)
            .Select(MessageSerializer.DeserializeEnvelope)
            .ToArray();

        runner.Check(hostStarted?.carriedMainTalentIds.Contains("draw_reward") == true
                     && hostStarted.currentTotalAlienation == 3
                     && guestStarted != null
                     && guestStarted.carriedMainTalentIds.All(string.IsNullOrEmpty)
                     && guestStarted.currentActiveTalentIds.Length == 0
                     && guestStarted.currentTotalAlienation == 0,
            "each human receives only their own private SideboardStarted payload");
        runner.Check(guestNewMessages.Length == 1
                     && guestNewMessages[0].type == "SideboardProgress"
                     && !guestNewMessages[0].data.Contains("Talent", StringComparison.OrdinalIgnoreCase)
                     && !guestNewMessages[0].data.Contains("Alienation", StringComparison.OrdinalIgnoreCase)
                     && !guestNewMessages[0].data.Contains("acceptedSelection", StringComparison.Ordinal),
            "another seat receives only boolean lock progress, never a choice, active set, validation result, or exact total");
    }

    private static void TestDisconnectLocksOriginal(RegressionRunner runner)
    {
        TrustedPlayerLoadout hostLoadout = CreateTrustedLoadout(
            new[] { null, null, null, "draw_reward", null, null },
            new[] { null, "peek", null });
        TrustedPlayerLoadout guestLoadout = CreateTrustedLoadout(
            new string[TalentSlotConfig.MainSlotCount],
            new string[TalentSlotConfig.ReserveSlotCount]);
        using Room disconnectedRoom = CreateStartedTwoHumanRoom(
            "sideboard-disconnect", hostLoadout, guestLoadout,
            out GameEndpoint disconnectedEndpoint, out _);
        CompleteRounds(disconnectedRoom, 4, "host", "guest");
        string streamId = disconnectedRoom.Seats[0].MessageStream.StreamId;
        long disconnectedDecisionId = GetLastMessage<SideboardStartedMessage>(
            disconnectedEndpoint, "SideboardStarted").decisionId;
        disconnectedRoom.HandleDisconnect(
            "dev:host", "host", disconnectedEndpoint, DateTime.UtcNow, TimeSpan.FromMinutes(1),
            out _, out _);
        RoomGameSnapshot disconnectedSnapshot = disconnectedRoom.BuildSnapshot(0);
        string disconnectedSnapshotJson = UnityEngine.JsonUtility.ToJson(disconnectedSnapshot.sideboard);
        disconnectedRoom.SubmitSideboard(1, new SideboardSubmitMessage
        {
            decisionId = disconnectedDecisionId,
            activeTalentIds = new string[0]
        }, out _);
        var restoredEndpoint = new GameEndpoint();
        bool reconnected = disconnectedRoom.TryReconnect(
            "dev:host", streamId, "host-reconnected", restoredEndpoint, int.MaxValue, true,
            DateTime.UtcNow, out _, out _, out _);
        bool resubmit = disconnectedRoom.SubmitSideboard(0, new SideboardSubmitMessage
        {
            decisionId = disconnectedDecisionId,
            activeTalentIds = new[] { "peek" }
        }, out string resubmitError);
        SideboardLockedMessage restoredLock = GetLastMessage<SideboardLockedMessage>(restoredEndpoint, "SideboardLocked");
        SideboardProgressMessage restoredProgress = GetLastMessage<SideboardProgressMessage>(restoredEndpoint, "SideboardProgress");

        runner.Check(disconnectedRoom.State == RoomState.InRound
                     && reconnected
                     && disconnectedSnapshot.sideboard.isActive
                     && disconnectedSnapshot.sideboard.decisionId == disconnectedDecisionId
                     && disconnectedSnapshot.sideboard.ownLocked
                     && disconnectedSnapshot.sideboard.seatLocked.SequenceEqual(new[] { true, false, true, true })
                     && !disconnectedSnapshotJson.Contains("Talent", System.StringComparison.OrdinalIgnoreCase)
                     && !disconnectedSnapshotJson.Contains("draft", System.StringComparison.OrdinalIgnoreCase)
                     && restoredLock == null
                     && restoredProgress == null
                     && !resubmit
                     && resubmitError == SideboardErrorCodes.WrongPhase,
            "disconnect locks the original immediately; once every seat locks, reconnect restores the running second half without reopening sideboard");
    }

    private static void TestTimeoutLocksOriginal(RegressionRunner runner)
    {
        TrustedPlayerLoadout hostLoadout = CreateTrustedLoadout(
            new[] { null, null, null, "draw_reward", null, null },
            new[] { null, "peek", null });
        using Room timeoutRoom = CreateStartedRoom(
            "sideboard-timeout", GameMode.FullGame, hostLoadout, out GameEndpoint timeoutEndpoint);
        CompleteRounds(timeoutRoom, 4, "host");
        long deadline = GetLastMessage<SideboardStartedMessage>(
            timeoutEndpoint, "SideboardStarted").deadlineUnixMilliseconds;
        timeoutRoom.ProcessSideboardDeadline(DateTimeOffset.FromUnixTimeMilliseconds(deadline).UtcDateTime);
        SideboardLockedMessage timeoutLock = GetLastMessage<SideboardLockedMessage>(timeoutEndpoint, "SideboardLocked");
        runner.Check(timeoutRoom.State == RoomState.InRound
                     && timeoutLock?.acceptedSelection == false
                     && timeoutLock.reason == "timeout",
            "the 45-second deadline locks every pending human to the original set and starts the second half");
    }

    private static void TestRoomManagerRoutesSubmitAndDeadline(RegressionRunner runner)
    {
        TrustedPlayerLoadout loadout = CreateTrustedLoadout(
            new[] { null, null, null, "draw_reward", null, null },
            new[] { null, "peek", null });

        using (var manager = new RoomManager(
                   1, true, new ConnectionRegistry(int.MaxValue), messageCacheSize: 64))
        {
            Room room = CreateManagedSideboardRoom(manager, loadout, "manager-submit", out GameEndpoint endpoint);
            SideboardStartedMessage started = GetLastMessage<SideboardStartedMessage>(endpoint, "SideboardStarted");
            long decisionId = started.decisionId;
            endpoint.Receive("manager-submit", 1, MessageSerializer.Serialize("SideboardSubmit", 0,
                new SideboardSubmitMessage
                {
                    decisionId = decisionId,
                    activeTalentIds = new[] { "peek" }
                }));

            SideboardLockedMessage locked = GetLastMessage<SideboardLockedMessage>(endpoint, "SideboardLocked");
            runner.Check(room.State == RoomState.InRound
                         && locked?.acceptedSelection == true
                         && locked.reason == "accepted",
                "RoomManager routes SideboardSubmit through the authenticated bound seat and starts the second half when all seats lock");
        }

        using (var manager = new RoomManager(
                   1, true, new ConnectionRegistry(int.MaxValue), messageCacheSize: 64))
        {
            Room room = CreateManagedSideboardRoom(manager, loadout, "manager-timeout", out GameEndpoint endpoint);
            long deadline = GetLastMessage<SideboardStartedMessage>(
                endpoint, "SideboardStarted").deadlineUnixMilliseconds;
            manager.Tick(DateTimeOffset.FromUnixTimeMilliseconds(deadline).UtcDateTime);

            SideboardLockedMessage locked = GetLastMessage<SideboardLockedMessage>(endpoint, "SideboardLocked");
            runner.Check(room.State == RoomState.InRound
                         && locked?.acceptedSelection == false
                         && locked.reason == "timeout",
                "RoomManager Tick locks a pending sideboard at its authoritative deadline and starts the second half");
        }
    }

    private static TrustedPlayerLoadout CreateTrustedLoadout(
        string[] mainIds,
        string[] reserveIds,
        DeckConfig deck = null)
    {
        bool decoded = PlayerLoadoutCodec.TryDecode(
            PlayerLoadoutCodec.CreateMessage(deck ?? DeckConfig.CreateStandard(), new TalentSlotConfig
            {
                SlotTalentIds = mainIds,
                ReserveTalentIds = reserveIds
            }, AlienationPreset.Low),
            out TrustedPlayerLoadout loadout,
            out string errorCode);
        if (!decoded) throw new InvalidOperationException($"Invalid sideboard test loadout: {errorCode}");
        return loadout;
    }

    private static Room CreateStartedRoom(
        string roomId,
        GameMode gameMode,
        TrustedPlayerLoadout loadout,
        out GameEndpoint endpoint)
    {
        endpoint = new GameEndpoint();
        var room = new Room(roomId, gameMode, AlienationPreset.Low, "host", true, 64);
        if (!room.TryAddHuman("host", endpoint, "dev:host", "Host", loadout, out _)
            || !room.SetReady("host", ReadyPhase.MatchStart, out _)
            || !room.SetReady("host", ReadyPhase.GameSceneLoaded, out _))
        {
            room.Dispose();
            throw new InvalidOperationException("Could not start one-human sideboard room.");
        }
        return room;
    }

    private static Room CreateStartedTwoHumanRoom(
        string roomId,
        TrustedPlayerLoadout hostLoadout,
        TrustedPlayerLoadout guestLoadout,
        out GameEndpoint host,
        out GameEndpoint guest)
    {
        host = new GameEndpoint();
        guest = new GameEndpoint();
        var room = new Room(roomId, GameMode.HalfGame, AlienationPreset.Low, "host", true, 64);
        if (!room.TryAddHuman("host", host, "dev:host", "Host", hostLoadout, out _)
            || !room.TryAddHuman("guest", guest, "dev:guest", "Guest", guestLoadout, out _)
            || !room.SetReady("host", ReadyPhase.MatchStart, out _)
            || !room.SetReady("guest", ReadyPhase.MatchStart, out _)
            || !room.SetReady("host", ReadyPhase.GameSceneLoaded, out _)
            || !room.SetReady("guest", ReadyPhase.GameSceneLoaded, out _))
        {
            room.Dispose();
            throw new InvalidOperationException("Could not start two-human sideboard room.");
        }
        return room;
    }

    private static Room CreateManagedSideboardRoom(
        RoomManager manager,
        TrustedPlayerLoadout loadout,
        string connectionId,
        out GameEndpoint endpoint)
    {
        endpoint = new GameEndpoint();
        endpoint.Connect(connectionId, 1);
        endpoint.Receive(connectionId, 1, MessageSerializer.Serialize("Hello", 0, new HelloMessage
        {
            protocolVersion = NetworkProtocol.Version,
            username = connectionId
        }));
        endpoint.Receive(connectionId, 1, MessageSerializer.Serialize("CreateRoom", 0, new CreateRoomMessage
        {
            gameMode = (int)GameMode.HalfGame,
            alienationPreset = (int)AlienationPreset.Low,
            loadout = PlayerLoadoutCodec.CreateMessage(
                loadout.DeckConfig, loadout.TalentConfig, AlienationPreset.Low)
        }));

        var roomsField = typeof(RoomManager).GetField("_rooms", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic);
        var rooms = roomsField?.GetValue(manager) as Dictionary<string, Room>;
        Room room = rooms?.Values.SingleOrDefault();
        if (room == null) throw new InvalidOperationException("RoomManager did not create the sideboard test room.");

        endpoint.Receive(connectionId, 1, MessageSerializer.Serialize("Ready", 0,
            new ReadyMessage { phase = (int)ReadyPhase.MatchStart }));
        endpoint.Receive(connectionId, 1, MessageSerializer.Serialize("Ready", 0,
            new ReadyMessage { phase = (int)ReadyPhase.GameSceneLoaded }));
        CompleteRounds(room, 4, connectionId);
        return room;
    }

    private static void CompleteRounds(Room room, int count, params string[] humanConnectionIds)
    {
        for (int round = 1; round <= count; round++)
        {
            room.GameServer?.CompleteDrawRound();
            if (round == count) continue;
            foreach (string connectionId in humanConnectionIds)
            {
                if (!room.SetReady(connectionId, ReadyPhase.NextRound, out string error))
                    throw new InvalidOperationException($"Could not ready {connectionId}: {error}");
            }
        }
    }

    private static T GetLastMessage<T>(GameEndpoint endpoint, string type)
    {
        NetworkMessageEnvelope envelope = endpoint.SentMessages
            .Select(MessageSerializer.DeserializeEnvelope)
            .LastOrDefault(candidate => candidate?.type == type);
        return envelope == null ? default : MessageSerializer.DeserializePayload<T>(envelope.data);
    }

    private static void ConfigureThirtyAlienationDeck(DeckConfig deck)
    {
        foreach (Suit suit in new[] { Suit.Man, Suit.Pin, Suit.Sou })
        {
            deck.SetCardCount(suit, 1, 6);
            for (int value = 2; value <= 6; value++) deck.SetCardCount(suit, value, 0);
        }
    }
}
