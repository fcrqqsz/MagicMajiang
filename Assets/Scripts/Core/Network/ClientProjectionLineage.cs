namespace MahjongGame.Core.Network
{
    /// <summary>Non-secret identity of the client projection currently held in memory.</summary>
    public sealed class ClientProjectionLineage
    {
        private string _roomId;
        private string _streamId;

        public void Bind(string roomId, string streamId)
        {
            _roomId = roomId;
            _streamId = streamId;
        }

        public bool Matches(string roomId, string streamId) =>
            !string.IsNullOrWhiteSpace(_roomId)
            && !string.IsNullOrWhiteSpace(_streamId)
            && _roomId == roomId
            && _streamId == streamId;

        public void Clear()
        {
            _roomId = null;
            _streamId = null;
        }
    }
}
