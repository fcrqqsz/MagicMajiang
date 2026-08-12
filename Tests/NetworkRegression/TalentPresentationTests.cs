using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Data;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Systems;
using MahjongGame.Talents;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

internal static class TalentPresentationTests
{
    public static void Run(RegressionRunner runner)
    {
        RunLayeredTalentHudPolicyTests(runner);
        RunTalentEventPresentationPolicyTests(runner);
        RunAlienationPresentationPolicyTests(runner);
        RunTalentEditorAndLobbySourceTests(runner);
        RunLoadoutPresetTests(runner);
        RunServerAdmissionTests(runner);
        RunClientCommandTests(runner);
    }

    private static void RunLayeredTalentHudPolicyTests(RegressionRunner runner)
    {
        var snapshot = new RoomGameSnapshot
        {
            privateSeat = new SnapshotPrivateSeat
            {
                ownTalents = new[]
                {
                    new SnapshotOwnTalent { talentId = "peek", isActive = true },
                    new SnapshotOwnTalent { talentId = "midas_touch", isActive = false },
                    new SnapshotOwnTalent { talentId = "draw_reward", isActive = false },
                    new SnapshotOwnTalent { talentId = "head_start", isActive = false }
                }
            },
            knownTalents = new[]
            {
                new SnapshotKnownTalent { ownerSeatIndex = 1, talentId = "peek", isKnown = true, lastPublicEventType = "talent_revealed" },
                new SnapshotKnownTalent { ownerSeatIndex = 1, talentId = "midas_touch", isKnown = true, lastPublicEventType = "active_talent_applied" },
                new SnapshotKnownTalent { ownerSeatIndex = 1, talentId = "draw_reward", isKnown = true },
                new SnapshotKnownTalent { ownerSeatIndex = 1, talentId = "head_start", isKnown = true },
                new SnapshotKnownTalent { ownerSeatIndex = 1, talentId = "hidden", isKnown = false },
                new SnapshotKnownTalent { ownerSeatIndex = 2, talentId = "peek", isKnown = true }
            }
        };
        var events = new[]
        {
            new TalentRuntimeEventMessage { eventId = 12, ownerSeatIndex = 1, talentId = "midas_touch", eventType = "active_talent_applied", visibility = (int)TalentEventVisibility.Public },
            new TalentRuntimeEventMessage { eventId = 8, ownerSeatIndex = 1, talentId = "peek", eventType = "talent_revealed", visibility = (int)TalentEventVisibility.Public }
        };

        TalentHudView view = TalentHudProjectionPolicy.Build(snapshot, localSeatIndex: 0, publicEvents: events);
        runner.Check(view.OwnVisible.All(item => item.IsActive) && view.OwnCollapsedCount == 3,
            "only active own talents remain in the persistent hand-anchored row");
        runner.Check(view.Seats[1].Visible.Count == 2 && view.Seats[1].CollapsedCount == 2,
            "opponents show two authorized known talents and a +N summary");
        runner.Check(view.Seats[1].Visible.All(item => !item.ShowActiveState),
            "opponent chips never reveal post-sideboard active state");
        runner.Check(view.Seats[1].Visible[0].TalentId == "midas_touch"
            && view.Seats[1].Visible[1].TalentId == "peek",
            "public event recency orders authorized opponent talents without inspecting hidden entries");
        runner.Check(view.Seats[2].Visible.Count == 1 && view.Seats[2].CollapsedCount == 0,
            "each opponent summary is limited to server-authorized known talents only");

        TalentHudView pinned = TalentHudProjectionPolicy.Build(new RoomGameSnapshot
        {
            knownTalents = new[]
            {
                new SnapshotKnownTalent { ownerSeatIndex = 3, talentId = "starting_capital", isKnown = true },
                new SnapshotKnownTalent { ownerSeatIndex = 3, talentId = "midas_touch", isKnown = true }
            }
        }, 0, new[]
        {
            new TalentRuntimeEventMessage
            {
                eventId = 99, ownerSeatIndex = 3, talentId = "midas_touch",
                eventType = "active_talent_applied", visibility = (int)TalentEventVisibility.Public
            }
        });
        runner.Check(pinned.Seats[3].Visible[0].TalentId == "starting_capital",
            "public-at-match-start talents are pinned ahead of more recent public events");
    }

    private static void RunTalentEventPresentationPolicyTests(RegressionRunner runner)
    {
        TalentFeedbackView strong = TalentEventPresentationPolicy.Build(ActiveAppliedEvent(), false);
        runner.Check(strong.Level == TalentFeedbackLevel.Strong
            && strong.ShowToast && strong.AppendFeed && strong.PulseChip && strong.PlayAudio,
            "only standardized applied active effects produce the four-part strong feedback");

        runner.Check(TalentEventPresentationPolicy.Build(BlockedEvent(), false).Level == TalentFeedbackLevel.Medium,
            "control blocking is medium feedback");
        runner.Check(TalentEventPresentationPolicy.Build(PrivateRefresh(), false).Level == TalentFeedbackLevel.Weak,
            "ordinary projection refresh only updates chips");
        TalentFeedbackView recovery = TalentEventPresentationPolicy.Build(ActiveAppliedEvent(), true);
        runner.Check(recovery.IsSilent && !recovery.ShowToast && !recovery.AppendFeed
            && !recovery.PulseChip && !recovery.PlayAudio,
            "recovery suppresses all historical feedback without replaying any feed effects");

        TalentFeedbackView unknown = TalentEventPresentationPolicy.Build(new TalentRuntimeEventMessage
        {
            eventId = 9,
            talentId = "<b>untrusted</b>",
            eventType = "<script>alert(1)</script>"
        }, false);
        runner.Check(unknown.Level == TalentFeedbackLevel.Weak
            && unknown.Copy == "天赋状态已更新" && unknown.ShouldLogWarning,
            "unknown events use safe generic copy rather than server-provided rich text");

        var history = new TalentFeedbackHistory();
        runner.Check(!history.TryAccept(0) && history.TryAccept(10) && !history.TryAccept(10) && !history.TryAccept(9),
            "feedback history rejects non-positive duplicate and lower event IDs within a match");
        history.ResetForNewMatch();
        runner.Check(history.TryAccept(1), "a new match resets event-feedback deduplication");
    }

    private static TalentRuntimeEventMessage ActiveAppliedEvent() => new TalentRuntimeEventMessage
    {
        eventId = 1,
        ownerSeatIndex = 0,
        talentId = "peek",
        eventType = "active_talent_applied",
        visibility = (int)TalentEventVisibility.Public
    };

    private static TalentRuntimeEventMessage BlockedEvent() => new TalentRuntimeEventMessage
    {
        eventId = 2,
        ownerSeatIndex = 0,
        talentId = "interception",
        eventType = "blocked_negative_effect",
        visibility = (int)TalentEventVisibility.Public
    };

    private static TalentRuntimeEventMessage PrivateRefresh() => new TalentRuntimeEventMessage
    {
        eventId = 3,
        ownerSeatIndex = 0,
        talentId = "peek",
        eventType = "private_state_refresh",
        visibility = (int)TalentEventVisibility.OwnerOnly
    };

    private static void RunTalentEditorAndLobbySourceTests(RegressionRunner runner)
    {
        RunTalentPickerDuplicateTests(runner);

        string editorUxmlPath = GetRepoPath("Assets", "UI", "DeckEditorView.uxml");
        XDocument editorUxml = XDocument.Load(editorUxmlPath);
        List<string> queryNames = editorUxml.Descendants()
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        runner.Check(queryNames.Count == queryNames.Distinct(StringComparer.Ordinal).Count(),
            "deck editor UXML query names stay unique");
        runner.Check(queryNames.Contains("AlienationPresetSelector")
            && queryNames.Contains("BtnPresetPrev")
            && queryNames.Contains("PresetLabel")
            && queryNames.Contains("BtnPresetNext")
            && queryNames.Contains("AlienationTrack")
            && queryNames.Contains("AlienationFill")
            && queryNames.Contains("AlienationBreakdownLabel")
            && queryNames.Contains("AlienationWarning")
            && queryNames.Contains("MainTalentSlots")
            && queryNames.Contains("ReserveTalentSlots")
            && !queryNames.Contains("ScoreText"),
            "deck editor exposes one gauge plus separate main and reserve slot containers");

        string editorSource = File.ReadAllText(GetRepoPath("Assets", "UI", "DeckEditorToolkit.cs"));
        runner.Check(editorSource.Contains("_btnSave.SetEnabled(total == 34);", StringComparison.Ordinal)
            && !editorSource.Contains("_btnSave.SetEnabled(total == 34 && !gauge.IsOverLimit)", StringComparison.Ordinal),
            "deck editor Save depends on 34 tiles and never on the over-limit presentation flag");
        runner.Check(editorSource.Contains("CanEquip(slotIndex, tier)", StringComparison.Ordinal)
            && editorSource.Contains("CanEquipReserve(slotIndex, tier)", StringComparison.Ordinal),
            "main and reserve talent pickers use their respective slot policies");

        XDocument lobbyUxml = XDocument.Load(GetRepoPath("Assets", "UI", "MainLobby.uxml"));
        List<string> lobbyNames = lobbyUxml.Descendants()
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        runner.Check(lobbyNames.Count == lobbyNames.Distinct(StringComparer.Ordinal).Count()
            && lobbyNames.Contains("RoomPresetSelector")
            && lobbyNames.Contains("BtnRoomPresetPrev")
            && lobbyNames.Contains("RoomPresetLabel")
            && lobbyNames.Contains("BtnRoomPresetNext")
            && lobbyNames.Contains("RoomAdmissionBlocker"),
            "lobby provides a unique pending room preset selector and explicit admission blocker");

        string lobbySource = File.ReadAllText(GetRepoPath("Assets", "UI", "LobbyController.cs"));
        runner.Check(lobbySource.Contains(
                "CreateRoom(GetSelectedGameMode(), _pendingRoomAlienationPreset, GetNickname())",
                StringComparison.Ordinal),
            "create-room sends the explicit pending room preset");
        runner.Check(!lobbySource.Contains("AlienationPreset = _pendingRoomAlienationPreset", StringComparison.Ordinal)
            && !lobbySource.Contains(".AlienationPreset = room", StringComparison.OrdinalIgnoreCase),
            "LobbyController never writes a pending or authoritative room preset back to SavedDeck");
        runner.Check(lobbySource.Contains("HandleRoomError(string message)", StringComparison.Ordinal)
            && lobbySource.Contains("ShowRoomAdmissionBlocker(message);", StringComparison.Ordinal),
            "authoritative join-room rejections open the explicit admission blocker");
        runner.Check(lobbySource.Contains("RoomAlienationPresentationPolicy.Build", StringComparison.Ordinal)
            && lobbySource.Contains("roomAlienation.PublicSummary", StringComparison.Ordinal)
            && !lobbySource.Contains("room-seat-alienation", StringComparison.Ordinal)
            && !lobbySource.Contains("row.Alienation", StringComparison.Ordinal),
            "room preset appears only in the public summary and never in a seat row");

        RoomAlienationVisibilityView roomAlienation = RoomAlienationPresentationPolicy.Build(
            AlienationPreset.Standard, ownTotal: 45);
        runner.Check(roomAlienation.PublicSummary == "异化档位：标准 80"
            && roomAlienation.OwnSummary == "本家异化：45 / 80"
            && string.IsNullOrEmpty(roomAlienation.SeatSummary),
            "room alienation presentation exposes one public preset, one private own total, and no seat copy");
    }

    private static void RunTalentPickerDuplicateTests(RegressionRunner runner)
    {
        var mainCurrent = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "peek", null, null, null, null, null },
            ReserveTalentIds = new string[TalentSlotConfig.ReserveSlotCount]
        };
        runner.Check(!TalentPickerDuplicatePolicy.IsDuplicateOutsideSlot(
                mainCurrent, "peek", slotIndex: 0, isReserve: false),
            "the currently equipped main talent remains selectable");

        mainCurrent.ReserveTalentIds[0] = "peek";
        runner.Check(TalentPickerDuplicatePolicy.IsDuplicateOutsideSlot(
                mainCurrent, "peek", slotIndex: 0, isReserve: false),
            "a reserve talent at the same numeric index still disables the main picker item");

        var reserveCurrent = new TalentSlotConfig
        {
            SlotTalentIds = new string[TalentSlotConfig.MainSlotCount],
            ReserveTalentIds = new[] { "peek", null, null }
        };
        runner.Check(!TalentPickerDuplicatePolicy.IsDuplicateOutsideSlot(
                reserveCurrent, "peek", slotIndex: 0, isReserve: true),
            "the currently equipped reserve talent remains selectable");
    }

    private static string GetRepoPath(params string[] segments)
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, "ProjectSettings", "ProjectVersion.txt")))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Repository root not found.");
        return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
    }

    private static void RunAlienationPresentationPolicyTests(RegressionRunner runner)
    {
        AlienationGaugeView over = AlienationGaugePolicy.Build(
            deckCost: 28, talentCost: 17, AlienationPreset.Low);
        runner.Check(over.Total == 45 && over.Limit == 40 && over.Fill01 == 1f
            && over.Overflow == 5 && over.IsOverLimit && over.CanSave
            && over.DeckCost == 28 && over.TalentCost == 17,
            "over-cap decks remain saveable while exposing the exact overflow");

        AlienationGaugeView exact = AlienationGaugePolicy.Build(
            deckCost: 60, talentCost: 20, AlienationPreset.Standard);
        runner.Check(exact.Total == 80 && exact.Limit == 80 && exact.Fill01 == 1f
            && exact.Overflow == 0 && !exact.IsOverLimit && exact.CanSave,
            "exact-limit decks fill the gauge without becoming over-cap");

        AlienationGaugeView fallback = AlienationGaugePolicy.Build(
            deckCost: -9, talentCost: -3, (AlienationPreset)999);
        runner.Check(fallback.DeckCost == 0 && fallback.TalentCost == 0
            && fallback.Total == 0 && fallback.Limit == 80 && fallback.Fill01 == 0f,
            "gauge display clamps negative costs and falls back to Standard for an undefined preset");

        RoomLoadoutAdmissionView mismatch = RoomLoadoutAdmissionPresentationPolicy.Validate(
            AlienationPreset.Low, AlienationPreset.Standard, total: 35);
        runner.Check(!mismatch.CanEnter
            && mismatch.Code == PlayerLoadoutErrorCodes.AlienationPresetMismatch
            && mismatch.Message.Contains("低异化 40", StringComparison.Ordinal)
            && mismatch.Message.Contains("标准 80", StringComparison.Ordinal),
            "room admission shows both mismatched presets");

        RoomLoadoutAdmissionView overMatching = RoomLoadoutAdmissionPresentationPolicy.Validate(
            AlienationPreset.Low, AlienationPreset.Low, total: 45);
        runner.Check(!overMatching.CanEnter
            && overMatching.Code == PlayerLoadoutErrorCodes.AlienationLimitExceeded
            && overMatching.Message.Contains("45", StringComparison.Ordinal)
            && overMatching.Message.Contains("40", StringComparison.Ordinal),
            "matching room admission still blocks an over-cap loadout with exact values");

        RoomLoadoutAdmissionView invalidDisplay = RoomLoadoutAdmissionPresentationPolicy.Validate(
            (AlienationPreset)999, (AlienationPreset)777, total: 34);
        runner.Check(invalidDisplay.CanEnter && string.IsNullOrEmpty(invalidDisplay.Code)
            && invalidDisplay.Message.Contains("标准 80", StringComparison.Ordinal),
            "undefined presets fall back to Standard only for presentation validation");

        var saved = new SavedDeck { AlienationPreset = (AlienationPreset)999, AlienationScore = 123 };
        RoomLoadoutAdmissionPresentationPolicy.Validate(
            saved.AlienationPreset, AlienationPreset.Low, saved.AlienationScore);
        runner.Check((int)saved.AlienationPreset == 999 && saved.AlienationScore == 123,
            "room admission presentation never mutates a saved deck");
    }

    private static void RunLoadoutPresetTests(RegressionRunner runner)
    {
        runner.Check(NetworkProtocol.IsSupported(4) && !NetworkProtocol.IsSupported(3),
            "protocol v4 rejects protocol v3 before room loadout admission");

        var legacy = new SavedDeck { Config = DeckConfig.CreateStandard(), Talents = new TalentSlotConfig() };
        legacy.Normalize();
        runner.Check(legacy.AlienationPreset == AlienationPreset.Standard,
            "legacy saved decks default to Standard without changing their contents");

        var invalid = new SavedDeck
        {
            Config = DeckConfig.CreateStandard(),
            Talents = new TalentSlotConfig(),
            AlienationPreset = (AlienationPreset)999
        };
        invalid.Normalize();
        runner.Check(invalid.AlienationPreset == AlienationPreset.Standard,
            "undefined saved-deck presets normalize to Standard");

        var lowDeck = new SavedDeck
        {
            Config = DeckConfig.CreateStandard(),
            Talents = new TalentSlotConfig(),
            AlienationPreset = AlienationPreset.Low
        };
        lowDeck.Normalize();
        runner.Check(lowDeck.AlienationPreset == AlienationPreset.Low,
            "defined saved-deck presets survive normalization");

        PlayerLoadoutMessage wire = PlayerLoadoutCodec.CreateMessage(
            lowDeck.Config, lowDeck.Talents, lowDeck.AlienationPreset);
        runner.Check(wire.schemaVersion == 3 && wire.alienationPreset == (int)AlienationPreset.Low,
            "loadout schema v3 carries the saved deck preset");

        runner.Check(PlayerLoadoutCodec.TryDecode(
                wire, AlienationPreset.Low, out TrustedPlayerLoadout trusted, out _)
            && PlayerLoadoutCodec.CloneTrustedLoadout(trusted)?.AlienationPreset == AlienationPreset.Low,
            "trusted loadout clones preserve the saved deck preset");

        PlayerLoadoutMessage legacyWire = PlayerLoadoutCodec.CreateMessage(
            lowDeck.Config, lowDeck.Talents, AlienationPreset.Low);
        legacyWire.schemaVersion = 2;
        legacyWire.alienationPreset = 999;
        runner.Check(!PlayerLoadoutCodec.TryDecode(
                legacyWire, (AlienationPreset)999, out _, out string legacyError)
            && legacyError == PlayerLoadoutErrorCodes.UnsupportedLoadoutVersion,
            "schema v2 is rejected before any preset or loadout reconstruction checks");

        runner.Check(RoomErrorPresentationPolicy.GetDisplayMessage(new RoomErrorMessage
            {
                code = PlayerLoadoutErrorCodes.AlienationPresetMismatch,
                message = "arbitrary server wording",
                loadoutAlienationPreset = (int)AlienationPreset.Low,
                roomAlienationPreset = (int)AlienationPreset.Standard
            }) == "所选构筑的异化档位（40）与房间档位（80）不一致。"
            && RoomErrorPresentationPolicy.GetDisplayMessage(new RoomErrorMessage
            {
                code = PlayerLoadoutErrorCodes.AlienationLimitExceeded,
                message = "arbitrary server wording",
                actual = 45,
                limit = 40
            }) == "所选构筑异化值 45 超过房间上限 40。",
            "structured loadout errors use stable Chinese UI text without parsing server strings");

        var profile = new PlayerProfile
        {
            Settings = new ProfileSettings { MasterVolume = 0.35f, SelectedGameMode = 2 },
            SavedDecks = new()
            {
                new SavedDeck { AlienationPreset = (AlienationPreset)999 },
                new SavedDeck { AlienationPreset = AlienationPreset.High }
            }
        };
        profile.Normalize();
        runner.Check(profile.Settings.MasterVolume == 0.35f
            && profile.Settings.SelectedGameMode == 2
            && profile.SavedDecks[0].AlienationPreset == AlienationPreset.Standard
            && profile.SavedDecks[1].AlienationPreset == AlienationPreset.High,
            "profile normalization migrates every saved deck and preserves unrelated settings");
    }

    private static void RunServerAdmissionTests(RegressionRunner runner)
    {
        var connections = new ConnectionRegistry();
        using var manager = new RoomManager(4, true, connections, messageCacheSize: 8);

        var legacyProtocolEndpoint = new GameEndpoint();
        legacyProtocolEndpoint.Connect("protocol-v3", 1);
        legacyProtocolEndpoint.Receive("protocol-v3", 1, MessageSerializer.Serialize("Hello", 0,
            new HelloMessage { protocolVersion = 3, username = "Legacy" }));
        RoomErrorMessage protocolError = GetLastRoomError(legacyProtocolEndpoint);
        connections.TryGet("protocol-v3", out ConnectionRegistry.ConnectionRecord legacyRecord);
        runner.Check(protocolError.code == NetworkErrorCodes.ProtocolMismatch
            && legacyRecord != null && !legacyRecord.IsAuthenticated,
            "protocol v3 fails during Hello before room admission");

        GameEndpoint host = ConnectAuthenticated("preset-host", "Host");

        host.Receive("preset-host", 1, MessageSerializer.Serialize("CreateRoom", 0, new CreateRoomMessage
        {
            gameMode = (int)GameMode.Single,
            alienationPreset = (int)AlienationPreset.Standard,
            loadout = PlayerLoadoutCodec.CreateMessage(
                DeckConfig.CreateStandard(), new TalentSlotConfig(), AlienationPreset.Low)
        }));
        RoomErrorMessage mismatch = GetLastRoomError(host);
        connections.TryGet("preset-host", out ConnectionRegistry.ConnectionRecord hostAfterMismatch);
        runner.Check(mismatch.code == PlayerLoadoutErrorCodes.AlienationPresetMismatch
            && mismatch.loadoutAlienationPreset == (int)AlienationPreset.Low
            && mismatch.roomAlienationPreset == (int)AlienationPreset.Standard
            && string.IsNullOrEmpty(hostAfterMismatch.RoomId)
            && hostAfterMismatch.SeatIndex == -1,
            "create reports the saved-deck and requested-room preset without conflating budget");

        host.Receive("preset-host", 1, MessageSerializer.Serialize("CreateRoom", 0, new CreateRoomMessage
        {
            gameMode = (int)GameMode.Single,
            alienationPreset = (int)AlienationPreset.Low,
            loadout = BuildOverLowPresetLoadout()
        }));
        RoomErrorMessage over = GetLastRoomError(host);
        connections.TryGet("preset-host", out ConnectionRegistry.ConnectionRecord hostAfterOverCap);
        runner.Check(over.code == PlayerLoadoutErrorCodes.AlienationLimitExceeded
            && over.actual == 45 && over.limit == 40
            && string.IsNullOrEmpty(hostAfterOverCap.RoomId)
            && hostAfterOverCap.SeatIndex == -1,
            "matching preset still reports an independent over-cap rejection");

        host.Receive("preset-host", 1, MessageSerializer.Serialize("CreateRoom", 0, new CreateRoomMessage
        {
            gameMode = (int)GameMode.Single,
            alienationPreset = (int)AlienationPreset.Standard,
            loadout = PlayerLoadoutCodec.CreateMessage(
                DeckConfig.CreateStandard(), new TalentSlotConfig(), AlienationPreset.Standard)
        }));
        RoomJoinedMessage joined = GetLastPayload<RoomJoinedMessage>(host, "RoomJoined");

        GameEndpoint guest = ConnectAuthenticated("preset-guest", "Guest");
        guest.Receive("preset-guest", 1, MessageSerializer.Serialize("JoinRoom", 0, new JoinRoomMessage
        {
            roomId = joined.roomId,
            loadout = PlayerLoadoutCodec.CreateMessage(
                DeckConfig.CreateStandard(), new TalentSlotConfig(), AlienationPreset.Low)
        }));
        RoomErrorMessage joinMismatch = GetLastRoomError(guest);
        connections.TryGet("preset-guest", out ConnectionRegistry.ConnectionRecord guestAfterMismatch);
        runner.Check(joinMismatch.code == PlayerLoadoutErrorCodes.AlienationPresetMismatch
            && joinMismatch.loadoutAlienationPreset == (int)AlienationPreset.Low
            && joinMismatch.roomAlienationPreset == (int)AlienationPreset.Standard
            && string.IsNullOrEmpty(guestAfterMismatch.RoomId)
            && guestAfterMismatch.SeatIndex == -1,
            "join reports a saved-deck preset mismatch without allocating a room seat");
    }

    private static void RunClientCommandTests(RegressionRunner runner)
    {
        ProfileManager.Instance.CurrentProfile = new PlayerProfile
        {
            SelectedDeckIndex = 0,
            SavedDecks = new()
            {
                new SavedDeck
                {
                    Config = DeckConfig.CreateStandard(),
                    Talents = new TalentSlotConfig(),
                    AlienationPreset = AlienationPreset.Low
                }
            }
        };
        using (ClientRoomService service = CreateClientService())
        {
            runner.Check(service.CreateRoom(GameMode.HalfGame, AlienationPreset.High, "Host"),
                "create accepts an explicit room preset");
            AcceptHello();
            CreateRoomMessage create = GetLastClientPayload<CreateRoomMessage>("CreateRoom");
            runner.Check(create.alienationPreset == (int)AlienationPreset.High
                && create.loadout.alienationPreset == (int)AlienationPreset.Low,
                "create keeps the requested room preset distinct from the saved deck preset");
        }

        using (ClientRoomService service = CreateClientService())
        {
            runner.Check(service.CreateRoom(GameMode.Single, "Compatible"),
                "the compatibility create entry accepts a valid selected deck");
            AcceptHello();
            CreateRoomMessage create = GetLastClientPayload<CreateRoomMessage>("CreateRoom");
            runner.Check(create.alienationPreset == (int)AlienationPreset.Low
                && create.loadout.alienationPreset == (int)AlienationPreset.Low,
                "the compatibility create entry uses the selected saved deck preset");
        }

        using (ClientRoomService service = CreateClientService())
        {
            runner.Check(service.JoinRoom("R1000", "Guest"), "join accepts a valid selected deck");
            AcceptHello();
            JoinRoomMessage join = GetLastClientPayload<JoinRoomMessage>("JoinRoom");
            runner.Check(join.loadout.alienationPreset == (int)AlienationPreset.Low,
                "join carries the selected saved deck preset for server comparison");
        }

        ProfileManager.Instance.CurrentProfile = new PlayerProfile();
        using (ClientRoomService service = CreateClientService())
        {
            runner.Check(service.CreateRoom(GameMode.Single, AlienationPreset.Standard, "Default"),
                "create builds a standard loadout when no decks exist");
            AcceptHello();
            CreateRoomMessage create = GetLastClientPayload<CreateRoomMessage>("CreateRoom");
            runner.Check(create.loadout.alienationPreset == (int)AlienationPreset.Standard,
                "an implicit standard loadout carries the Standard preset");
        }

        ProfileManager.Instance.CurrentProfile = new PlayerProfile
        {
            SelectedDeckIndex = 2,
            SavedDecks = new() { new SavedDeck { Config = DeckConfig.CreateStandard() } }
        };
        using (ClientRoomService service = CreateClientService())
        {
            runner.Check(!service.CreateRoom(GameMode.Single, AlienationPreset.Standard, "Invalid")
                && WebSocketClient.Instance.SentMessages.Count == 0,
                "an invalid selected deck index prevents any room command from being sent");
        }
    }

    private static GameEndpoint ConnectAuthenticated(string connectionId, string username)
    {
        var endpoint = new GameEndpoint();
        endpoint.Connect(connectionId, 1);
        endpoint.Receive(connectionId, 1, MessageSerializer.Serialize("Hello", 0,
            new HelloMessage { protocolVersion = NetworkProtocol.Version, username = username }));
        return endpoint;
    }

    private static PlayerLoadoutMessage BuildOverLowPresetLoadout()
    {
        DeckConfig deck = DeckConfig.CreateStandard();
        foreach (Suit suit in new[] { Suit.Man, Suit.Pin, Suit.Sou })
        {
            deck.SetCardCount(suit, 1, 6);
            for (int value = 2; value <= 6; value++) deck.SetCardCount(suit, value, 0);
        }
        var talents = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "midas_touch", null, null, null, null, null },
            ReserveTalentIds = new string[TalentSlotConfig.ReserveSlotCount]
        };
        return PlayerLoadoutCodec.CreateMessage(deck, talents, AlienationPreset.Low);
    }

    private static RoomErrorMessage GetLastRoomError(GameEndpoint endpoint) =>
        GetLastPayload<RoomErrorMessage>(endpoint, "RoomError");

    private static T GetLastPayload<T>(GameEndpoint endpoint, string type) =>
        MessageSerializer.DeserializePayload<T>(endpoint.SentMessages
            .Select(MessageSerializer.DeserializeEnvelope).Last(message => message.type == type).data);

    private static ClientRoomService CreateClientService()
    {
        WebSocketClient.ResetForTests();
        return new ClientRoomService("ws://test", new InMemoryClientReconnectTicketStore());
    }

    private static void AcceptHello() => WebSocketClient.Instance.Receive(
        MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage
        {
            protocolVersion = NetworkProtocol.Version,
            playerId = "dev:test",
            displayName = "Test"
        }));

    private static T GetLastClientPayload<T>(string type) =>
        MessageSerializer.DeserializePayload<T>(WebSocketClient.Instance.SentMessages
            .Select(MessageSerializer.DeserializeEnvelope).Last(message => message.type == type).data);
}
