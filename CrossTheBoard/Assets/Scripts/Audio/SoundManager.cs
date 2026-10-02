using System;
using UnityEngine;

namespace CrossTheBoard
{
    [DefaultExecutionOrder(-200)]
    public sealed class SoundManager : MonoBehaviour
    {
        [SerializeField] private AudioSource _bgmSource;
        [SerializeField] private AudioSource _effectsSource;
        public static SoundManager Instance { get; private set; }
        public float BgmVolume => _bgmSource.volume;
        public float EffectsVolume => _effectsSource.volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            if (_bgmSource == null || _effectsSource == null || _bgmSource == _effectsSource)
                throw new InvalidOperationException("SoundManager requires separate BGM and effects AudioSources.");
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void PlayBgm(AudioClip clip, bool loop = true)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            if (_bgmSource.clip == clip && _bgmSource.isPlaying && _bgmSource.loop == loop)
                return;
            _bgmSource.clip = clip;
            _bgmSource.loop = loop;
            _bgmSource.Play();
        }

        public void PlayEffect(AudioClip clip)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            _effectsSource.PlayOneShot(clip);
        }

        public void StopBgm() => _bgmSource.Stop();

        public void SetBgmVolume(float volume)
        {
            if (float.IsNaN(volume) || float.IsInfinity(volume))
                throw new ArgumentOutOfRangeException(nameof(volume));
            _bgmSource.volume = Mathf.Clamp01(volume);
        }

        public void SetEffectsVolume(float volume)
        {
            if (float.IsNaN(volume) || float.IsInfinity(volume))
                throw new ArgumentOutOfRangeException(nameof(volume));
            _effectsSource.volume = Mathf.Clamp01(volume);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
