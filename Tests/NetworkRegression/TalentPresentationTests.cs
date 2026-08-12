using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Data;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Systems;
using MahjongGame.Talents;
using System;
using System.Linq;

internal static class TalentPresentationTests
{
    public static void Run(RegressionRunner runner)
    {
        RunLoadoutPresetTests(runner);
        RunServerAdmissionTests(runner);
        RunClientCommandTests(runner);
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
