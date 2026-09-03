using MahjongGame.Core;
using MahjongGame.Systems.Audio;

internal static class AudioSettingsTests
{
    public static void Run(RegressionRunner runner)
    {
        var store = new MemoryStore();
        var preferences = new AudioPreferences(store);
        runner.Check(preferences.Current.MasterVolume == 0.8f && preferences.Current.MusicVolume == 0.6f
            && preferences.Current.SfxVolume == 1f, "Missing local preferences start with safe audible defaults.");

        int changes = 0;
        preferences.Changed += _ => changes++;
        preferences.SetVolumes(0f, 0.3f, 0.7f, 10);
        preferences.Tick(10.49);
        runner.Check(store.Saves == 0 && changes == 1 && preferences.Current.MasterVolume == 0,
            "Muting applies immediately without writing on every slider movement.");
        preferences.SetVolumes(0f, 0.4f, 0.7f, 10.4);
        preferences.Tick(10.6);
        runner.Check(store.Saves == 0, "Further slider movement restarts the save debounce.");
        preferences.Tick(10.91);
        runner.Check(store.Saves == 1 && store.Value.MasterVolume == 0 && store.Value.MusicVolume == 0.4f,
            "Debounced save includes the latest values and preserves deliberate zero.");
        var restored = new AudioPreferences(store);
        runner.Check(restored.Current.MasterVolume == 0 && restored.Current.SfxVolume == 0.7f,
            "Restart restores muted master and independent category choices.");
        preferences.SetVolumes(0f, 0.4f, 0.7f, 12);
        preferences.Flush();
        runner.Check(store.Saves == 1 && changes == 2, "Unchanged settings neither save nor notify again.");
        preferences.SetVolumes(0.5f, 0.5f, 1f, 13);
        preferences.Flush();
        runner.Check(store.Saves == 2 && Math.Abs(preferences.Current.MusicGain - 0.25f) < 0.0001f
            && preferences.Current.SfxGain == 0.5f, "Leaving settings flushes and master multiplies each category independently.");
        preferences.Reset(14);
        preferences.Flush();
        runner.Check(store.Value.MasterVolume == 0.8f && store.Value.MusicVolume == 0.6f && store.Value.SfxVolume == 1,
            "Reset replaces all three persisted channels with defaults.");

        var malformed = new ClientAudioSettings(float.NaN, float.PositiveInfinity, float.NegativeInfinity);
        runner.Check(malformed.MasterVolume == 0.8f && malformed.MusicVolume == 0.6f && malformed.SfxVolume == 1,
            "Nonfinite persisted values cannot reach the mixer.");
        var clipped = new ClientAudioSettings(-1, 2, 0);
        runner.Check(clipped.MasterVolume == 0 && clipped.MusicVolume == 1 && clipped.SfxVolume == 0,
            "Out of range values clamp without treating zero as missing.");
        runner.Check(AudioVolumePolicy.ToDecibels(0) == -80f && AudioVolumePolicy.ToDecibels(1) == 0f
            && Math.Abs(AudioVolumePolicy.ToDecibels(0.5f) + 6.0206f) < 0.001f,
            "Mixer gain conversion handles silence and half amplitude safely.");

        foreach (var fixture in new[]
        {
            (SceneNames.Login, true, true, true, MusicTrack.Lobby),
            (SceneNames.MainLobby, true, true, true, MusicTrack.Lobby),
            (SceneNames.Game, true, true, true, MusicTrack.Battle),
            (SceneNames.Persistent, true, false, true, MusicTrack.Battle),
            (SceneNames.Persistent, false, true, false, MusicTrack.Lobby),
            (SceneNames.Persistent, true, false, false, MusicTrack.Lobby),
            (SceneNames.Persistent, false, false, false, MusicTrack.None)
        })
            runner.Check(MusicScenePolicy.Resolve(fixture.Item1, fixture.Item2, fixture.Item3, fixture.Item4) == fixture.Item5,
                "Music follows the activated destination or remaining scene after unload: " + fixture);

        var transition = new MusicTransitionState();
        runner.Check(transition.Request(MusicTrack.Lobby), "First lobby request starts playback.");
        int initial = transition.Revision;
        runner.Check(!transition.Request(MusicTrack.Lobby) && transition.IsCurrent(initial),
            "Login to lobby, reconnect and settings requests keep the same playback operation.");
        transition.Request(MusicTrack.Battle);
        int battle = transition.Revision;
        transition.Request(MusicTrack.Lobby);
        runner.Check(!transition.IsCurrent(initial) && !transition.IsCurrent(battle)
            && transition.IsCurrent(transition.Revision) && transition.Target == MusicTrack.Lobby,
            "Rapid lobby battle lobby switch invalidates both stale loading/fade callbacks.");
        transition.Request(MusicTrack.None);
        runner.Check(transition.Target == MusicTrack.None && !transition.IsCurrent(battle),
            "Leaving the client scenes cancels pending playback.");

        store.FailSave = true;
        preferences.SetVolumes(0.1f, 0.2f, 0.3f, 20);
        bool failed = false;
        try { preferences.Flush(); } catch (IOException) { failed = true; }
        store.FailSave = false;
        preferences.Flush();
        runner.Check(failed && store.Value.MasterVolume == 0.1f,
            "Failed persistence remains pending so a later lifecycle flush can retry.");
    }

    private sealed class MemoryStore : IAudioSettingsStore
    {
        public ClientAudioSettings Value = ClientAudioSettings.Default;
        public int Saves;
        public bool FailSave;
        public ClientAudioSettings Load() => Value;
        public void Save(ClientAudioSettings value)
        {
            if (FailSave) throw new IOException("Simulated unavailable preference storage");
            Value = value;
            Saves++;
        }
    }
}
