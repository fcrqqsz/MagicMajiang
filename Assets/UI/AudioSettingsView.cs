using System;
using MahjongGame.Systems.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace MahjongGame.UI
{
    /// <summary>
    /// Binds shared lobby and battle sound controls to the persistent client audio runtime.
    /// </summary>
    internal sealed class AudioSettingsView : IDisposable
    {
        private readonly AudioManager manager;
        private readonly SliderInt masterVolumeSlider;
        private readonly SliderInt musicVolumeSlider;
        private readonly SliderInt sfxVolumeSlider;
        private readonly Label masterVolumePercentageLabel;
        private readonly Label musicVolumePercentageLabel;
        private readonly Label sfxVolumePercentageLabel;
        private readonly Button previewSfxButton;
        private readonly Button resetAudioSettingsButton;
        private readonly Label unavailableLabel;
        private bool isVisible;
        private bool disposed;

        public AudioSettingsView(VisualElement root, AudioManager manager)
        {
            this.manager = manager;
            masterVolumeSlider = root?.Q<SliderInt>("MasterVolumeSlider");
            musicVolumeSlider = root?.Q<SliderInt>("MusicVolumeSlider");
            sfxVolumeSlider = root?.Q<SliderInt>("SfxVolumeSlider");
            masterVolumePercentageLabel = root?.Q<Label>("MasterVolumePercentageLabel");
            musicVolumePercentageLabel = root?.Q<Label>("MusicVolumePercentageLabel");
            sfxVolumePercentageLabel = root?.Q<Label>("SfxVolumePercentageLabel");
            previewSfxButton = root?.Q<Button>("PreviewSfxButton");
            resetAudioSettingsButton = root?.Q<Button>("ResetAudioSettingsButton");
            unavailableLabel = root?.Q<Label>("AudioSettingsUnavailableLabel");

            masterVolumeSlider?.RegisterValueChangedCallback(OnMasterVolumeChanged);
            musicVolumeSlider?.RegisterValueChangedCallback(OnMusicVolumeChanged);
            sfxVolumeSlider?.RegisterValueChangedCallback(OnSfxVolumeChanged);
            if (previewSfxButton != null) previewSfxButton.clicked += OnPreviewSfxClicked;
            if (resetAudioSettingsButton != null) resetAudioSettingsButton.clicked += OnResetClicked;

            if (manager == null)
            {
                SetAudioControlsEnabled(false);
                if (unavailableLabel != null) unavailableLabel.style.display = DisplayStyle.Flex;
                return;
            }

            manager.SettingsChanged += HandleSettingsChanged;
            Sync(manager.CurrentSettings);
        }

        public void SetVisible(bool visible)
        {
            if (disposed || isVisible == visible) return;

            if (isVisible && !visible)
                manager?.FlushPendingSettings();

            isVisible = visible;
            if (visible && manager != null)
                Sync(manager.CurrentSettings);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            if (manager != null)
            {
                manager.SettingsChanged -= HandleSettingsChanged;
                manager.FlushPendingSettings();
            }

            masterVolumeSlider?.UnregisterValueChangedCallback(OnMasterVolumeChanged);
            musicVolumeSlider?.UnregisterValueChangedCallback(OnMusicVolumeChanged);
            sfxVolumeSlider?.UnregisterValueChangedCallback(OnSfxVolumeChanged);
            if (previewSfxButton != null) previewSfxButton.clicked -= OnPreviewSfxClicked;
            if (resetAudioSettingsButton != null) resetAudioSettingsButton.clicked -= OnResetClicked;
        }

        private void OnMasterVolumeChanged(ChangeEvent<int> changeEvent)
        {
            if (manager == null) return;
            ClientAudioSettings settings = manager.CurrentSettings;
            manager.SetVolumes(ToVolume(changeEvent.newValue), settings.MusicVolume, settings.SfxVolume);
        }

        private void OnMusicVolumeChanged(ChangeEvent<int> changeEvent)
        {
            if (manager == null) return;
            ClientAudioSettings settings = manager.CurrentSettings;
            manager.SetVolumes(settings.MasterVolume, ToVolume(changeEvent.newValue), settings.SfxVolume);
        }

        private void OnSfxVolumeChanged(ChangeEvent<int> changeEvent)
        {
            if (manager == null) return;
            ClientAudioSettings settings = manager.CurrentSettings;
            manager.SetVolumes(settings.MasterVolume, settings.MusicVolume, ToVolume(changeEvent.newValue));
        }

        private void OnPreviewSfxClicked()
        {
            manager?.PlayPreviewSfx();
        }

        private void OnResetClicked()
        {
            manager?.ResetVolumes();
        }

        private void HandleSettingsChanged(ClientAudioSettings settings)
        {
            if (!disposed) Sync(settings);
        }

        private void Sync(ClientAudioSettings settings)
        {
            SetVolume(masterVolumeSlider, masterVolumePercentageLabel, settings.MasterVolume);
            SetVolume(musicVolumeSlider, musicVolumePercentageLabel, settings.MusicVolume);
            SetVolume(sfxVolumeSlider, sfxVolumePercentageLabel, settings.SfxVolume);
        }

        private void SetAudioControlsEnabled(bool enabled)
        {
            masterVolumeSlider?.SetEnabled(enabled);
            musicVolumeSlider?.SetEnabled(enabled);
            sfxVolumeSlider?.SetEnabled(enabled);
            previewSfxButton?.SetEnabled(enabled);
            resetAudioSettingsButton?.SetEnabled(enabled);
        }

        private static void SetVolume(SliderInt slider, Label percentageLabel, float volume)
        {
            int percentage = Mathf.Clamp(Mathf.RoundToInt(volume * 100f), 0, 100);
            slider?.SetValueWithoutNotify(percentage);
            if (percentageLabel != null) percentageLabel.text = percentage + "%";
        }

        private static float ToVolume(int percentage) => Mathf.Clamp(percentage, 0, 100) / 100f;
    }
}
