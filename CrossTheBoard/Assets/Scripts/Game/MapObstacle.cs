using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CrossTheBoard
{
    public sealed class MapObstacle : MonoBehaviour
    {
        [SerializeField] private bool _blocksMovement = true;
        [SerializeField, Min(0)] private int _damage;
        [SerializeField, Range(0.1f, 0.5f)] private float _contactRadius = 0.5f;
        [SerializeField] private ObstacleMovement _movement = ObstacleMovement.None;
        [SerializeField] private Vector2Int _direction = Vector2Int.right;
        [SerializeField, Min(1)] private int _distance = 2;
        [SerializeField, Min(0.01f)] private float _speed = 1.33f;
        [SerializeField, HideInInspector] private TileBase _tile;

        public bool BlocksMovement => _blocksMovement;
        public int Damage => _damage;
        public float ContactRadius => _contactRadius;
        public ObstacleMovement Movement => _movement;
        public float Speed => _speed;
        public TileBase Tile => _tile;

        public MovingObstacleDefinition GetMovement(Vector2Int position) => new()
        {
            position = position, tile = _tile, movement = _movement,
            direction = _direction, distance = _distance, stepInterval = 1f / _speed
        };

        public void Validate()
        {
            if (_damage < 0 || !float.IsFinite(_contactRadius) || _contactRadius <= 0f ||
                !Enum.IsDefined(typeof(ObstacleMovement), _movement) ||
                !float.IsFinite(_speed) || _speed <= 0f ||
                _movement != ObstacleMovement.None && (_blocksMovement || _damage <= 0) ||
                _movement == ObstacleMovement.Patrol &&
                (_direction != Vector2Int.left && _direction != Vector2Int.right || _distance < 1))
                throw new InvalidOperationException($"Invalid obstacle settings on '{name}'. Moving obstacles must deal damage without blocking movement.");
            if (GetComponentInChildren<SpriteRenderer>(true) == null)
                throw new InvalidOperationException($"Obstacle '{name}' requires a SpriteRenderer on itself or a visual child.");
        }

        internal void InitializeTile(TileBase tile, MovingObstacleDefinition movement = null)
        {
            _tile = tile;
            _damage = tile is DamageTile damageTile ? damageTile.Damage : 0;
            _blocksMovement = tile is not DamageTile;
            if (movement != null)
            {
                _movement = movement.movement;
                _direction = movement.direction;
                _distance = movement.distance;
                _speed = 1f / movement.stepInterval;
            }
            var visual = new GameObject("Visual", typeof(SpriteRenderer));
            visual.transform.SetParent(transform, false);
            var renderer = visual.GetComponent<SpriteRenderer>();
            if (tile is Tile spriteTile)
            {
                renderer.sprite = spriteTile.sprite;
                renderer.color = spriteTile.color;
                visual.transform.localPosition = spriteTile.transform.GetColumn(3);
                visual.transform.localRotation = spriteTile.transform.rotation;
                visual.transform.localScale = spriteTile.transform.lossyScale;
            }
            renderer.sortingOrder = 1;
        }
    }
}
