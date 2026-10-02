using UnityEngine;

namespace CrossTheBoard
{
    public sealed class SoundSetter : MonoBehaviour
    {
        [SerializeField] private AudioClip _bgm;
        [SerializeField] private AudioClip[] _effects = System.Array.Empty<AudioClip>();

        public void PlayBgm()
        {
            if (_bgm == null)
            {
                Debug.LogWarning("No BGM clip is assigned to SoundSetter.", this);
                return;
            }
            SoundManager.Instance.PlayBgm(_bgm);
        }

        public void PlayEffect(int index)
        {
            if (index < 0 || index >= _effects.Length || _effects[index] == null)
            {
                Debug.LogWarning($"No effect clip is assigned at index {index}.", this);
                return;
            }
            SoundManager.Instance.PlayEffect(_effects[index]);
        }
    }
}
