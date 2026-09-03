using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using MahjongGame.Core;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace MahjongGame.Systems.Audio
{
    /// <summary>Client-only playback host. Gameplay and network state never depend on audio.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }
        [SerializeField] private AudioMixer _mixer;
        [SerializeField] private AudioMixerGroup _musicGroup;
        [SerializeField] private AudioMixerGroup _sfxGroup;
        [SerializeField] private AudioClip _lobbyClip;
        [SerializeField] private AudioClip _battleClip;
        [SerializeField] private AudioClip _previewSfxClip;
        [SerializeField] private AudioSource _lobbySource;
        [SerializeField] private AudioSource _battleSource;
        [SerializeField] private AudioSource _sfxSource;

        private readonly MusicTransitionState _musicState = new MusicTransitionState();
        private readonly HashSet<string> _warnings = new HashSet<string>();
        private AudioPreferences _preferences;
        private Coroutine _loadRoutine;
        private Tween _fade;
        private bool _started;
        private bool _mixerReady;
        public ClientAudioSettings CurrentSettings => _preferences?.Current ?? ClientAudioSettings.Default;
        public event Action<ClientAudioSettings> SettingsChanged;

        private void Awake()
        {
#if UNITY_SERVER && !UNITY_EDITOR
            Destroy(gameObject);
            return;
#else
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            _preferences = new AudioPreferences(new PlayerPrefsAudioSettingsStore());
            _preferences.Changed += HandleSettingsChanged;
            ConfigureSource(_lobbySource, _musicGroup, _lobbyClip, true);
            ConfigureSource(_battleSource, _musicGroup, _battleClip, true);
            ConfigureSource(_sfxSource, _sfxGroup, null, false);
#endif
        }

        private static void ConfigureSource(AudioSource source, AudioMixerGroup group, AudioClip clip, bool loop)
        {
            if (source == null) return;
            source.Stop();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0;
            source.dopplerLevel = 0;
            source.outputAudioMixerGroup = group;
            source.clip = clip;
            source.volume = loop ? 0 : 1;
            source.mute = true;
        }

        private void Start()
        {
            if (Instance != this || _preferences == null) return;
            _started = true;
            // SetFloat is intentionally deferred until Start, before the first Play.
            ApplyVolumes();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            RefreshMusic();
        }

        private void Update()
        {
            if (Instance != this || _preferences == null) return;
            try { _preferences.Tick(Time.unscaledTime); }
            catch (Exception exception) { WarnOnce("save", "Could not save audio preferences: " + exception.Message); }
        }

        public void SetVolumes(float master, float music, float sfx) =>
            _preferences?.SetVolumes(master, music, sfx, Time.unscaledTime);
        public void ResetVolumes() => _preferences?.Reset(Time.unscaledTime);

        public void FlushPendingSettings()
        {
            try { _preferences?.Flush(); }
            catch (Exception exception) { WarnOnce("save", "Could not save audio preferences: " + exception.Message); }
        }

        private void HandleSettingsChanged(ClientAudioSettings settings)
        {
            if (_started) ApplyVolumes();
            SettingsChanged?.Invoke(settings);
        }

        private void ApplyVolumes()
        {
            var settings = CurrentSettings;
            _mixerReady = _mixer != null && _musicGroup != null && _sfxGroup != null
                && _musicGroup.audioMixer == _mixer && _sfxGroup.audioMixer == _mixer;
            if (_mixerReady)
            {
                bool master = _mixer.SetFloat("MasterVolume", AudioVolumePolicy.ToDecibels(settings.MasterVolume));
                bool music = _mixer.SetFloat("MusicVolume", AudioVolumePolicy.ToDecibels(settings.MusicVolume));
                bool sfx = _mixer.SetFloat("SfxVolume", AudioVolumePolicy.ToDecibels(settings.SfxVolume));
                _mixerReady = master && music && sfx;
            }
            if (!_mixerReady) WarnOnce("mixer", "Missing audio mixer routing or exposed volume parameters; playback is muted.");
            // Exact silence without stopping the music clock; fading never writes these user settings.
            if (_lobbySource != null) _lobbySource.mute = !_mixerReady || settings.MusicGain == 0;
            if (_battleSource != null) _battleSource.mute = !_mixerReady || settings.MusicGain == 0;
            if (_sfxSource != null) _sfxSource.mute = !_mixerReady || settings.SfxGain == 0;
        }

        public void PlayPreviewSfx() => PlaySfx(_previewSfxClip);

        public void PlaySfx(AudioClip clip)
        {
            if (!_started || !_mixerReady) return;
            if (_sfxSource == null || clip == null)
            {
                WarnOnce("sfx", "Missing sound-effect source or clip.");
                return;
            }
            if (CurrentSettings.SfxGain == 0) return;
            _sfxSource.PlayOneShot(clip);
        }

        private void OnActiveSceneChanged(Scene previous, Scene next) => RefreshMusic();
        private void OnSceneUnloaded(Scene scene) => RefreshMusic();

        private void RefreshMusic()
        {
            MusicTrack track = MusicScenePolicy.Resolve(SceneManager.GetActiveScene().name,
                SceneManager.GetSceneByName(SceneNames.Login).isLoaded,
                SceneManager.GetSceneByName(SceneNames.MainLobby).isLoaded,
                SceneManager.GetSceneByName(SceneNames.Game).isLoaded);
            if (!_musicState.Request(track)) return;
            CancelTransition();
            if (_sfxSource != null) _sfxSource.Stop();
            if (track == MusicTrack.None)
            {
                FadeTo(null, _musicState.Revision);
                return;
            }
            _loadRoutine = StartCoroutine(LoadAndPlay(track, _musicState.Revision));
        }

        private IEnumerator LoadAndPlay(MusicTrack track, int revision)
        {
            AudioSource source = track == MusicTrack.Lobby ? _lobbySource : _battleSource;
            AudioClip clip = track == MusicTrack.Lobby ? _lobbyClip : _battleClip;
            if (source == null || clip == null)
            {
                WarnOnce("music-" + track, "Missing music source or clip for " + track + ".");
                FadeTo(null, revision);
                yield break;
            }
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < deadline)
            {
                if (!_musicState.IsCurrent(revision)) yield break;
                yield return null;
            }
            if (!_musicState.IsCurrent(revision)) yield break;
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                WarnOnce("load-" + track, "Music failed to load for " + track + ".");
                FadeTo(null, revision);
                yield break;
            }
            // A different category always starts at the beginning, even during a reversed crossfade.
            source.Stop();
            source.volume = 0;
            source.time = 0;
            source.Play();
            FadeTo(source, revision);
            _loadRoutine = null;
        }

        private void FadeTo(AudioSource target, int revision)
        {
            float lobbyStart = _lobbySource != null ? _lobbySource.volume : 0;
            float battleStart = _battleSource != null ? _battleSource.volume : 0;
            _fade = DOVirtual.Float(0, 1, 1f, progress =>
                {
                    if (!_musicState.IsCurrent(revision)) return;
                    if (_lobbySource != null) _lobbySource.volume = Mathf.Lerp(lobbyStart, target == _lobbySource ? 1 : 0, progress);
                    if (_battleSource != null) _battleSource.volume = Mathf.Lerp(battleStart, target == _battleSource ? 1 : 0, progress);
                })
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    if (!_musicState.IsCurrent(revision)) return;
                    if (_lobbySource != null && target != _lobbySource) _lobbySource.Stop();
                    if (_battleSource != null && target != _battleSource) _battleSource.Stop();
                    _fade = null;
                });
        }

        private void CancelTransition()
        {
            if (_loadRoutine != null) StopCoroutine(_loadRoutine);
            _loadRoutine = null;
            _fade?.Kill();
            _fade = null;
        }

        private void OnApplicationPause(bool paused) { if (paused) FlushPendingSettings(); }
        private void OnApplicationQuit() => FlushPendingSettings();

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            CancelTransition();
            FlushPendingSettings();
            if (_preferences != null) _preferences.Changed -= HandleSettingsChanged;
            SettingsChanged = null;
            Instance = null;
        }

        private void WarnOnce(string key, string message)
        {
            if (_warnings.Add(key)) Debug.LogWarning("[Audio] " + message);
        }
    }
}
