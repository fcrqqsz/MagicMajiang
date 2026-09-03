using System;

namespace MahjongGame.Systems.Audio
{
    /// <summary>Device-local amplitudes. A deliberate zero survives normalization and persistence.</summary>
    public readonly struct ClientAudioSettings : IEquatable<ClientAudioSettings>
    {
        public static ClientAudioSettings Default => new ClientAudioSettings(0.8f, 0.6f, 1f);
        public float MasterVolume { get; }
        public float MusicVolume { get; }
        public float SfxVolume { get; }
        public float MusicGain => MasterVolume * MusicVolume;
        public float SfxGain => MasterVolume * SfxVolume;

        public ClientAudioSettings(float masterVolume, float musicVolume, float sfxVolume)
        {
            MasterVolume = AudioVolumePolicy.Normalize(masterVolume, 0.8f);
            MusicVolume = AudioVolumePolicy.Normalize(musicVolume, 0.6f);
            SfxVolume = AudioVolumePolicy.Normalize(sfxVolume, 1f);
        }

        public bool Equals(ClientAudioSettings other) => MasterVolume == other.MasterVolume
            && MusicVolume == other.MusicVolume && SfxVolume == other.SfxVolume;
        public override bool Equals(object obj) => obj is ClientAudioSettings other && Equals(other);
        public override int GetHashCode() => (MasterVolume.GetHashCode() * 397 ^ MusicVolume.GetHashCode()) * 397 ^ SfxVolume.GetHashCode();
    }

    public static class AudioVolumePolicy
    {
        public static float Normalize(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value)
            ? fallback : Math.Max(0f, Math.Min(1f, value));

        public static float ToDecibels(float amplitude)
        {
            float value = Normalize(amplitude, 0f);
            return value <= 0f ? -80f : Math.Max(-80f, (float)(20 * Math.Log10(value)));
        }
    }
}
