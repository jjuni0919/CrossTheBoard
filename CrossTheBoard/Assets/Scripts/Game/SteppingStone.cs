using UnityEngine;

namespace CrossTheBoard
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SteppingStone : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 0.5f)] private float _supportHalfWidth = 0.5f;
        public float SupportHalfWidth => _supportHalfWidth;
    }
}
