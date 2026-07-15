using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Systems;

var failures = new List<string>();

Assert(TurnActionPolicy.IsMainTurnAction(ClientActionType.Discard), "Discard must be a main-turn action.");
Assert(TurnActionPolicy.IsMainTurnAction(ClientActionType.Hu), "Self-draw Hu must be a main-turn action.");
Assert(!TurnActionPolicy.IsMainTurnAction(ClientActionType.Skip), "A stale response Skip must never complete a main turn.");
Assert(!TurnActionPolicy.IsMainTurnAction(ClientActionType.Chi), "Chi must not complete a main turn.");
Assert(TurnActionPolicy.IsResponseAction(ClientActionType.Skip), "Skip must be a response action.");
Assert(TurnActionPolicy.IsResponseAction(ClientActionType.Chi), "Chi must be a response action.");
Assert(!TurnActionPolicy.IsResponseAction(ClientActionType.Discard), "Discard must not complete a response phase.");

var connectedAt = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
Assert(!ConnectionLivenessPolicy.IsExpired(connectedAt, connectedAt.AddSeconds(9)), "A connection must remain alive before the heartbeat timeout.");
Assert(ConnectionLivenessPolicy.IsExpired(connectedAt, connectedAt.AddSeconds(10)), "A connection must expire at the heartbeat timeout.");

// A room-close notification can arrive while another additive scene is active.
// Returning to the lobby must depend on the Game scene being loaded, not active.
Assert(SceneTransitionPolicy.ShouldUnloadGameScene(true), "A loaded Game scene must be unloaded after the room closes even when it is not the active scene.");
Assert(!SceneTransitionPolicy.ShouldUnloadGameScene(false), "No Game scene should be unloaded when it is not loaded.");

var completedRoom = new ClientRoomState();
completedRoom.ApplyJoined(new RoomJoinedMessage
{
    roomId = "R0001",
    seatIndex = 1,
    gameMode = (int)GameMode.Single,
    roomState = 3,
    seats = new[]
    {
        new RoomSeatMessage { seatIndex = 0, isOccupied = true, displayName = "Host" },
        new RoomSeatMessage { seatIndex = 1, isOccupied = true, displayName = "Guest" }
    }
});
completedRoom.CompleteSession();
Assert(!completedRoom.HasRoom, "A SessionEnd must clear the active room binding immediately.");
Assert(completedRoom.IsSessionCompleted, "A SessionEnd must enter the client completion state.");
Assert(completedRoom.ResultSeatIndex == 1, "The completion state must retain the local seat for result rendering.");
Assert(completedRoom.ResultSeats.Length == 2 && completedRoom.ResultSeats[0].displayName == "Host", "The completion state must retain the seat snapshot for result rendering.");
completedRoom.Reset();
Assert(!completedRoom.IsSessionCompleted && completedRoom.ResultSeats.Length == 0, "Returning to the lobby must clear the completed-room snapshot.");

Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.LoadingGameScene, true, true), "AI fill must preserve a loading room after a departure.");
Assert(!RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.LoadingGameScene, true, false), "A loading room without AI fill must close after a departure.");
Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForNextRound, true, true), "AI fill must preserve a between-round room after a departure.");
Assert(!RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForNextRound, true, false), "A between-round room without AI fill must close after a departure.");
Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForMatchReady, true, false), "A pre-match room without AI fill must retain an empty seat for a replacement human.");
Assert(!RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.InRound, true, true), "An in-round departure must always close the room.");
Assert(!RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForMatchReady, false, true), "A room with no remaining humans must be closed.");

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("Network regression tests passed.");
return 0;

void Assert(bool condition, string message)
{
    if (!condition) failures.Add(message);
}
