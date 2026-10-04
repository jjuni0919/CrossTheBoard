using UnityEngine;
using UnityEngine.Tilemaps;

namespace CrossTheBoard
{
    [CreateAssetMenu(menuName = "CrossTheBoard/Damage Tile")]
    public sealed class DamageTile : Tile
    {
        [SerializeField, Min(1)] private int _damage = 1;
        public int Damage => _damage;
    }
}
