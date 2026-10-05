using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace CrossTheBoard
{
    public sealed class PlayerController : MonoBehaviour
    {
        public const int MaxBackwardSteps = 1;
        [SerializeField] private Vector2Int _startPosition = new(0, 1);
        [SerializeField] private SpriteRenderer _appearance;
        [SerializeField, Range(0.01f, 0.25f)] private float _swipeThreshold = 0.04f;
        [SerializeField, Min(1)] private int _maxHealth = 3;
        [SerializeField, Min(0f)] private float _damageInterval = 0.75f;
        public Vector2Int Position { get; private set; }
        public int FurthestRow { get; private set; }
        public int Health { get; private set; }
        public int MaxHealth => _maxHealth;
        public event Action<Vector2Int, Vector2Int> Moved;
        public event Action<int> HealthChanged;
        private MapManager _map;
        private float _nextDamageTime;
        private Vector2 _swipeStart;
        private int _fingerId = -1;
        private bool _mouseDragging;

        private void OnEnable() => EnhancedTouchSupport.Enable();

        public void Initialize(MapManager map)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (!map.CanMoveTo(_startPosition))
                throw new InvalidOperationException("The player start cell is outside the map or blocked.");
            if (_maxHealth <= 0 || float.IsNaN(_damageInterval) || float.IsInfinity(_damageInterval) || _damageInterval < 0f)
                throw new InvalidOperationException("Player health must be positive and damage interval must be finite and nonnegative.");
            _map = map;
            Position = _startPosition;
            FurthestRow = Position.y;
            Health = _maxHealth;
            _nextDamageTime = float.NegativeInfinity;
            transform.position = _map.GetWorldPosition(Position);
            HealthChanged?.Invoke(Health);
            LogPosition();
        }

        private void Update()
        {
            if (_map == null || GameStateManager.Instance.State != GameState.Playing)
            {
                _fingerId = -1;
                _mouseDragging = false;
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                Vector2Int direction = Vector2Int.zero;
                if (keyboard.upArrowKey.wasPressedThisFrame) direction = Vector2Int.up;
                else if (keyboard.downArrowKey.wasPressedThisFrame) direction = Vector2Int.down;
                else if (keyboard.leftArrowKey.wasPressedThisFrame) direction = Vector2Int.left;
                else if (keyboard.rightArrowKey.wasPressedThisFrame) direction = Vector2Int.right;
                if (direction != Vector2Int.zero)
                {
                    _fingerId = -1;
                    _mouseDragging = false;
                    TryMove(direction);
                    return;
                }
            }
            foreach (var touch in Touch.activeTouches)
            {
                if (_fingerId == -1 && touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    _fingerId = touch.finger.index;
                    _swipeStart = touch.screenPosition;
                    _mouseDragging = false;
                }
                if (touch.finger.index != _fingerId)
                    continue;
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                    _fingerId = -1;
                else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    _fingerId = -1;
                    MoveFromSwipe(touch.screenPosition);
                    return;
                }
            }
            if (Touch.activeTouches.Count > 0)
                return;
            _fingerId = -1;
            var mouse = Mouse.current;
            if (mouse == null)
                return;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _swipeStart = mouse.position.ReadValue();
                _mouseDragging = true;
            }
            if (_mouseDragging && mouse.leftButton.wasReleasedThisFrame)
            {
                _mouseDragging = false;
                MoveFromSwipe(mouse.position.ReadValue());
            }
        }

        private void MoveFromSwipe(Vector2 end)
        {
            Vector2 delta = end - _swipeStart;
            float threshold = Mathf.Min(Screen.width, Screen.height) * _swipeThreshold;
            if (delta.sqrMagnitude < threshold * threshold)
                return;
            Vector2Int direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                ? new Vector2Int(delta.x > 0f ? 1 : -1, 0)
                : new Vector2Int(0, delta.y > 0f ? 1 : -1);
            TryMove(direction);
        }

        public bool TryMove(Vector2Int direction)
        {
            if (direction != Vector2Int.up && direction != Vector2Int.down &&
                direction != Vector2Int.left && direction != Vector2Int.right)
                throw new ArgumentException("Movement must be one cardinal cell.", nameof(direction));
            if (_map == null || !isActiveAndEnabled || GameStateManager.Instance.State != GameState.Playing)
                return false;
            Vector2Int destination = Position + direction;
            if (destination.y < FurthestRow - MaxBackwardSteps)
                return false;
            if (!_map.CanMoveTo(destination))
                return false;
            Vector2Int previous = Position;
            Position = destination;
            FurthestRow = Mathf.Max(FurthestRow, Position.y);
            transform.position = _map.GetWorldPosition(Position);
            LogPosition();
            Moved?.Invoke(previous, Position);
            return true;
        }

        public bool TakeDamage(int amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (_map == null || !isActiveAndEnabled || GameStateManager.Instance.State != GameState.Playing ||
                Health == 0 || Time.time < _nextDamageTime)
                return false;
            Health = Mathf.Max(0, Health - amount);
            _nextDamageTime = Time.time + _damageInterval;
            if (Health == 0)
                GameStateManager.Instance.SetState(GameState.GameOver);
            HealthChanged?.Invoke(Health);
            return true;
        }

        private void LogPosition()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Player] Position: ({Position.x}, {Position.y})", this);
#endif
        }

        public void SetCharacter(Sprite sprite)
        {
            if (sprite == null)
                throw new ArgumentNullException(nameof(sprite));
            _appearance.sprite = sprite;
        }

        private void OnDisable()
        {
            _fingerId = -1;
            _mouseDragging = false;
            EnhancedTouchSupport.Disable();
        }
    }
}
