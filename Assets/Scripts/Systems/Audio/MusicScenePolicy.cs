using MahjongGame.Core;

namespace MahjongGame.Systems.Audio
{
    public enum MusicTrack { None, Lobby, Battle }

    public static class MusicScenePolicy
    {
        public static MusicTrack Resolve(string activeScene, bool loginLoaded, bool lobbyLoaded, bool gameLoaded)
        {
            // The activated destination wins during additive overlap, including recovery to login/lobby.
            if (activeScene == SceneNames.Game) return MusicTrack.Battle;
            if (activeScene == SceneNames.Login || activeScene == SceneNames.MainLobby) return MusicTrack.Lobby;
            if (gameLoaded) return MusicTrack.Battle;
            return lobbyLoaded || loginLoaded ? MusicTrack.Lobby : MusicTrack.None;
        }
    }

    /// <summary>Same-track requests preserve playback; superseded async work cannot commit.</summary>
    public sealed class MusicTransitionState
    {
        public MusicTrack Target { get; private set; }
        public int Revision { get; private set; }

        public bool Request(MusicTrack target)
        {
            if (Target == target) return false;
            Target = target;
            Revision++;
            return true;
        }

        public bool IsCurrent(int revision) => revision == Revision;
    }
}
