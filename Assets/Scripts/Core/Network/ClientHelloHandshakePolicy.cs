namespace MahjongGame.Core.Network
{
    public enum ClientHelloHandshakeAction
    {
        SendHello,
        SendRoomCommand,
        AwaitingHello
    }

    /// <summary>Ensures room commands are sent only after this physical connection has completed Hello.</summary>
    public sealed class ClientHelloHandshakePolicy
    {
        public bool IsHelloAccepted { get; private set; }
        public bool IsHelloPending { get; private set; }

        public ClientHelloHandshakeAction BeginRoomCommand()
        {
            if (IsHelloAccepted) return ClientHelloHandshakeAction.SendRoomCommand;
            if (IsHelloPending) return ClientHelloHandshakeAction.AwaitingHello;

            IsHelloPending = true;
            return ClientHelloHandshakeAction.SendHello;
        }

        /// <summary>Returns whether one queued room command may now be sent.</summary>
        public bool AcceptHello()
        {
            bool hadPendingRoomCommand = IsHelloPending;
            IsHelloPending = false;
            IsHelloAccepted = true;
            return hadPendingRoomCommand;
        }

        /// <summary>Cancels the room command that was waiting on a rejected Hello.</summary>
        public bool RejectHello()
        {
            if (!IsHelloPending) return false;
            IsHelloPending = false;
            return true;
        }

        public void Reset()
        {
            IsHelloAccepted = false;
            IsHelloPending = false;
        }
    }
}
