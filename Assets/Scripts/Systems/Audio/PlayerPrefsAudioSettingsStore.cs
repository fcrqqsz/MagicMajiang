using System;
using UnityEngine;

namespace MahjongGame.Systems.Audio
{
    public sealed class PlayerPrefsAudioSettingsStore : IAudioSettingsStore
    {
        private const string Prefix = "SuperMajiang.Audio.v1.";

        public ClientAudioSettings Load()
        {
            var defaults = ClientAudioSettings.Default;
            return new ClientAudioSettings(Read("Master", defaults.MasterVolume),
                Read("Music", defaults.MusicVolume), Read("Sfx", defaults.SfxVolume));
        }

        private static float Read(string channel, float fallback)
        {
            try { return PlayerPrefs.GetFloat(Prefix + channel, fallback); }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Audio] Could not read {channel} preference: {exception.Message}");
                return fallback;
            }
        }

        public void Save(ClientAudioSettings settings)
        {
            PlayerPrefs.SetFloat(Prefix + "Master", settings.MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "Music", settings.MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "Sfx", settings.SfxVolume);
            PlayerPrefs.Save();
        }
    }
}
