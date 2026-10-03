using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Tilemaps;

namespace CrossTheBoard
{
    public sealed class MapManager : MonoBehaviour
    {
        public const int Width = 7;
        public const int RowsAhead = 13;
        public const int RowsBehind = 1;
        public const int VisibleRows = RowsAhead + RowsBehind + 1;
        public const int HalfWidth = Width / 2;

        [SerializeField] private Tilemap _ground;
        [SerializeField] private Tilemap _structures;
        [SerializeField] private TileBase _groundTile;
        [SerializeField, Range(0f, 1f)] private float _coinChance = 0.14f;
        [SerializeField, Tooltip("0 chooses a new coin layout each run.")] private int _coinSeed;
        [SerializeField] private CellTriggerDefinition[] _cellTriggers = Array.Empty<CellTriggerDefinition>();
        [SerializeField] private RowTriggerDefinition[] _rowTriggers = Array.Empty<RowTriggerDefinition>();
        public static MapManager Instance { get; private set; }
        public event Action<string, Vector2Int> CellTriggered;
        public event Action<string, int> RowTriggered;
        public event Action<int> RowReached;
        public IReadOnlyDictionary<Vector2Int, int> Coins => _coins;
        private int _firstRow;
        private bool _loaded;
        private readonly Dictionary<Vector2Int, int> _coins = new();
        private readonly Dictionary<Vector2Int, CellTriggerDefinition> _registeredCells = new();
        private readonly Dictionary<string, RowTriggerDefinition> _registeredRows = new();
        private readonly HashSet<string> _firedCellTriggers = new();
        private readonly HashSet<string> _firedRowTriggers = new();
        private readonly HashSet<int> _reachedRows = new();
        private Tilemap _coinLayer;
        private Tile _coinTile;
        private int _runSeed;
        private int _highestGeneratedRow = int.MinValue;
        private bool _contentInitialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            if (_ground == null || _structures == null || _groundTile == null)
                throw new InvalidOperationException("MapManager requires ground, structures and a ground tile.");
            Instance = this;
        }

        public bool CanMoveTo(Vector2Int position)
        {
            return position.x >= -HalfWidth && position.x <= HalfWidth &&
                !_structures.HasTile(new Vector3Int(position.x, position.y, 0));
        }

        public Vector3 GetWorldPosition(Vector2Int position) => _ground.GetCellCenterWorld(new Vector3Int(position.x, position.y, 0));

        public void LoadRows(int playerRow)
        {
            EnsureContentInitialized();
            int firstRow = playerRow - RowsBehind;
            if (_loaded && firstRow == _firstRow)
                return;
            if (!_loaded)
                _ground.ClearAllTiles();
            else
            {
                for (int row = _firstRow; row < _firstRow + VisibleRows; row++)
                {
                    if (row < firstRow || row >= firstRow + VisibleRows)
                        SetGroundRow(row, null);
                }
            }
            for (int row = firstRow; row < firstRow + VisibleRows; row++)
            {
                if (!_loaded || row < _firstRow || row >= _firstRow + VisibleRows)
                    SetGroundRow(row, _groundTile);
            }
            _firstRow = firstRow;
            _loaded = true;
            _ground.CompressBounds();
            var expiredCoins = new List<Vector2Int>();
            foreach (var coin in _coins)
                if (coin.Key.y < firstRow || coin.Key.y >= firstRow + VisibleRows)
                    expiredCoins.Add(coin.Key);
            foreach (var position in expiredCoins)
                RemoveCoin(position);
            for (int row = firstRow; row < firstRow + VisibleRows; row++)
            {
                if (row > _highestGeneratedRow)
                    GenerateCoins(row);
            }
        }

        private void EnsureContentInitialized()
        {
            if (_contentInitialized) return;
            _contentInitialized = true;
            _runSeed = _coinSeed == 0 ? Environment.TickCount : _coinSeed;
            var layer = new GameObject("Coins", typeof(Tilemap), typeof(TilemapRenderer));
            layer.transform.SetParent(_ground.transform.parent, false);
            layer.transform.localPosition = _ground.transform.localPosition;
            layer.transform.localRotation = _ground.transform.localRotation;
            layer.transform.localScale = _ground.transform.localScale;
            _coinLayer = layer.GetComponent<Tilemap>();
            _coinLayer.tileAnchor = _ground.tileAnchor;
            var renderer = layer.GetComponent<TilemapRenderer>();
            var groundRenderer = _ground.GetComponent<TilemapRenderer>();
            if (groundRenderer != null)
            {
                renderer.sharedMaterial = groundRenderer.sharedMaterial;
                renderer.sortingLayerID = groundRenderer.sortingLayerID;
                renderer.sortingOrder = groundRenderer.sortingOrder + 1;
            }
            _coinTile = ScriptableObject.CreateInstance<Tile>();
            _coinTile.name = "Coin Tile";
            _coinTile.sprite = PlaceholderSprites.Coin();
            _coinTile.colliderType = Tile.ColliderType.None;
            _coinTile.hideFlags = HideFlags.DontSave;
            foreach (var definition in _cellTriggers)
                RegisterCellTrigger(definition);
            foreach (var definition in _rowTriggers)
                RegisterRowTrigger(definition);
        }

        private void GenerateCoins(int row)
        {
            _highestGeneratedRow = row;
            // Keep the starting row and the single row behind it clear.
            if (row <= 0) return;
            var random = new System.Random(unchecked(_runSeed * 397 ^ row));
            for (int x = -HalfWidth; x <= HalfWidth; x++)
            {
                var position = new Vector2Int(x, row);
                if (random.NextDouble() < _coinChance)
                    TryPlaceCoin(position);
            }
        }

        public bool TryPlaceCoin(Vector2Int position, int amount = 1)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            EnsureContentInitialized();
            var cell = new Vector3Int(position.x, position.y, 0);
            if (!CanMoveTo(position) || !_ground.HasTile(cell) || _registeredCells.ContainsKey(position) || _coins.ContainsKey(position))
                return false;
            _coins.Add(position, amount);
            _coinLayer.SetTile(cell, _coinTile);
            return true;
        }

        public int GetCoinAmount(Vector2Int position) => _coins.TryGetValue(position, out int amount) ? amount : 0;

        /// <summary>Call only after the wallet update has been successfully persisted.</summary>
        public void RemoveCoin(Vector2Int position)
        {
            _coins.Remove(position);
            if (_coinLayer != null)
                _coinLayer.SetTile(new Vector3Int(position.x, position.y, 0), null);
        }

        public bool TrySetObstacle(Vector2Int position, TileBase tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            EnsureContentInitialized();
            if (position.x < -HalfWidth || position.x > HalfWidth || _coins.ContainsKey(position) || _registeredCells.ContainsKey(position))
                return false;
            _structures.SetTile(new Vector3Int(position.x, position.y, 0), tile);
            return true;
        }

        public bool RegisterCellTrigger(CellTriggerDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                throw new ArgumentException("A cell trigger requires a stable ID.", nameof(definition));
            if (!CanMoveTo(definition.position) || _coins.ContainsKey(definition.position) || _registeredCells.ContainsKey(definition.position))
                return false;
            foreach (var existing in _registeredCells.Values)
                if (existing.id == definition.id) return false;
            _registeredCells.Add(definition.position, definition);
            return true;
        }

        public bool RegisterRowTrigger(RowTriggerDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                throw new ArgumentException("A row trigger requires a stable ID.", nameof(definition));
            if (_registeredRows.ContainsKey(definition.id)) return false;
            _registeredRows.Add(definition.id, definition);
            return true;
        }

        public void NotifyPlayerEntered(Vector2Int position)
        {
            EnsureContentInitialized();
            if (_registeredCells.TryGetValue(position, out var cell) && (!cell.oncePerRun || _firedCellTriggers.Add(cell.id)))
            {
                cell.onReached?.Invoke();
                CellTriggered?.Invoke(cell.id, position);
            }
            if (_reachedRows.Add(position.y)) RowReached?.Invoke(position.y);
            // Snapshot the list so listeners can register future events during a callback.
            foreach (var row in new List<RowTriggerDefinition>(_registeredRows.Values))
            {
                if (row.row != position.y || row.oncePerRun && !_firedRowTriggers.Add(row.id)) continue;
                row.onReached?.Invoke();
                RowTriggered?.Invoke(row.id, position.y);
            }
        }

        private void SetGroundRow(int row, TileBase tile)
        {
            for (int column = -HalfWidth; column <= HalfWidth; column++)
                _ground.SetTile(new Vector3Int(column, row, 0), tile);
        }

        private void OnDestroy()
        {
            if (_coinTile != null)
            {
                if (Application.isPlaying) Destroy(_coinTile);
                else DestroyImmediate(_coinTile);
            }
            if (_coinLayer != null)
            {
                if (Application.isPlaying) Destroy(_coinLayer.gameObject);
                else DestroyImmediate(_coinLayer.gameObject);
            }
            if (Instance == this)
                Instance = null;
        }
    }

    [Serializable]
    public sealed class CellTriggerDefinition
    {
        public string id;
        public Vector2Int position;
        public bool oncePerRun = true;
        public UnityEvent onReached = new();
    }

    [Serializable]
    public sealed class RowTriggerDefinition
    {
        public string id;
        public int row;
        public bool oncePerRun = true;
        public UnityEvent onReached = new();
    }
}
