using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Talents;

internal static class SideboardTests
{
    public static void Run(RegressionRunner runner)
    {
        TestPhaseEntryByGameMode(runner);
        TestDecisionTrackerCopiesAndLocksSelections(runner);
        TestLoadoutPolicyNormalizesAndCalculatesTotal(runner);
        TestLoadoutPolicyRejectsEveryInvalidShape(runner);
        TestLoadoutPolicyEnforcesLockedTalentsAndBudget(runner);
        TestRoomOpensSideboardBeforeNextRoundAndAppliesSelection(runner);
        TestInvalidSelectionLocksOriginalAndCannotRetry(runner);
        TestSideboardMessagesKeepOtherSeatsPrivate(runner);
        TestDisconnectLocksOriginal(runner);
        TestTimeoutLocksOriginal(runner);
        TestRoomManagerRoutesSubmitAndDeadline(runner);
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
                     && room.State == RoomState.WaitingForNextRound
                     && locked?.acceptedSelection == true
                     && locked.reason == "accepted"
                     && locked.ownTotalAlienation == 10,
            "a valid selection atomically replaces the runtime set, privately locks the owner, then enters next-round ready");
        runner.Check(room.Session.Scores[0] == 50,
            "sideboarding does not replay StartingCapital or any match-start effect");

        room.SetReady("host", ReadyPhase.NextRound, out _);
        room.GameServer?.CompleteDrawRound();
        runner.Check(room.Session.TotalRoundsPlayed == 5 && room.Session.Scores[0] == 50,
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

        runner.Check(disconnectedRoom.State == RoomState.WaitingForNextRound
                     && reconnected
                     && disconnectedSnapshot.sideboard.isActive
                     && disconnectedSnapshot.sideboard.decisionId == disconnectedDecisionId
                     && disconnectedSnapshot.sideboard.ownLocked
                     && disconnectedSnapshot.sideboard.seatLocked.SequenceEqual(new[] { true, false, true, true })
                     && !disconnectedSnapshotJson.Contains("Talent", System.StringComparison.OrdinalIgnoreCase)
                     && !disconnectedSnapshotJson.Contains("draft", System.StringComparison.OrdinalIgnoreCase)
                     && restoredLock?.acceptedSelection == false
                     && restoredLock.reason == "disconnected"
                     && restoredProgress?.isComplete == true
                     && !resubmit
                     && resubmitError == SideboardErrorCodes.AlreadyLocked,
            "disconnect locks the original immediately and reconnect restores only the locked state without selection rights");
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
        runner.Check(timeoutRoom.State == RoomState.WaitingForNextRound
                     && timeoutLock?.acceptedSelection == false
                     && timeoutLock.reason == "timeout",
            "the 45-second deadline locks every pending human to the original set and completes sideboarding");
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
            runner.Check(room.State == RoomState.WaitingForNextRound
                         && locked?.acceptedSelection == true
                         && locked.reason == "accepted",
                "RoomManager routes SideboardSubmit through the authenticated bound seat to the active room decision");
        }

        using (var manager = new RoomManager(
                   1, true, new ConnectionRegistry(int.MaxValue), messageCacheSize: 64))
        {
            Room room = CreateManagedSideboardRoom(manager, loadout, "manager-timeout", out GameEndpoint endpoint);
            long deadline = GetLastMessage<SideboardStartedMessage>(
                endpoint, "SideboardStarted").deadlineUnixMilliseconds;
            manager.Tick(DateTimeOffset.FromUnixTimeMilliseconds(deadline).UtcDateTime);

            SideboardLockedMessage locked = GetLastMessage<SideboardLockedMessage>(endpoint, "SideboardLocked");
            runner.Check(room.State == RoomState.WaitingForNextRound
                         && locked?.acceptedSelection == false
                         && locked.reason == "timeout",
                "RoomManager Tick advances a pending sideboard at its authoritative deadline");
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
            }),
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
            loadout = PlayerLoadoutCodec.CreateMessage(loadout.DeckConfig, loadout.TalentConfig)
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
