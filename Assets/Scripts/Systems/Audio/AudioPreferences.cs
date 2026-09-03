using System;

namespace MahjongGame.Systems.Audio
{
    public interface IAudioSettingsStore
    {
        ClientAudioSettings Load();
        void Save(ClientAudioSettings settings);
    }

    /// <summary>Immediate settings changes with a debounced persistence boundary.</summary>
    public sealed class AudioPreferences
    {
        private readonly IAudioSettingsStore _store;
        private bool _dirty;
        private double _saveAt;
        public ClientAudioSettings Current { get; private set; }
        public event Action<ClientAudioSettings> Changed;

        public AudioPreferences(IAudioSettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            Current = store.Load();
        }

        public void SetVolumes(float master, float music, float sfx, double now)
        {
            var next = new ClientAudioSettings(master, music, sfx);
            if (Current.Equals(next)) return;
            Current = next;
            _dirty = true;
            _saveAt = now + 0.5;
            Changed?.Invoke(Current);
        }

        public void Reset(double now)
        {
            var defaults = ClientAudioSettings.Default;
            SetVolumes(defaults.MasterVolume, defaults.MusicVolume, defaults.SfxVolume, now);
        }

        public void Tick(double now)
        {
            if (!_dirty || now < _saveAt) return;
            // A failing store must not be retried every frame.
            _saveAt = now + 0.5;
            Flush();
        }

        public void Flush()
        {
            if (!_dirty) return;
            _store.Save(Current);
            _dirty = false;
        }
    }
}
