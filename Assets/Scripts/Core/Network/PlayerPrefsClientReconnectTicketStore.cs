using UnityEngine;

namespace MahjongGame.Core.Network
{
    /// <summary>Unity-backed storage for the deliberately non-secret E3 reconnect hint.</summary>
    public sealed class PlayerPrefsClientReconnectTicketStore : IClientReconnectTicketStore
    {
        private const string Key = "SuperMajiang.ClientReconnectTicket";

        public void Save(ClientReconnectTicket ticket)
        {
            if (ticket == null) { Clear(); return; }
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(ticket));
            PlayerPrefs.Save();
        }

        public bool TryLoad(out ClientReconnectTicket ticket)
        {
            ticket = null;
            if (!PlayerPrefs.HasKey(Key)) return false;
            ticket = JsonUtility.FromJson<ClientReconnectTicket>(PlayerPrefs.GetString(Key));
            if (ticket != null && !string.IsNullOrWhiteSpace(ticket.serverAddress)
                && !string.IsNullOrWhiteSpace(ticket.username)
                && !string.IsNullOrWhiteSpace(ticket.roomId)
                && !string.IsNullOrWhiteSpace(ticket.streamId)) return true;
            Clear();
            ticket = null;
            return false;
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
