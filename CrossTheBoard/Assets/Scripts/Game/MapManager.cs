using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Tilemaps;

namespace CrossTheBoard
{
    public sealed class MapManager : MonoBehaviour
    {
        public const int Width = 9;
        public const int RowsAhead = 9;
        public const int RowsBehind = 1;
        public const int VisibleRows = RowsAhead + RowsBehind + 1;
        public const int HalfWidth = Width / 2;

        [SerializeField] private Tilemap _ground;
        [SerializeField] private Tilemap _structures;
        [SerializeField] private TileBase _groundTile;
        [SerializeField] private PlayerController _player;
        [SerializeField] private string _patternResourcesPath = "MapPatterns";
        [SerializeField] private string _initialThemeId = "meadow";
        [SerializeField, Range(0f, 1f)] private float _repeatWeight = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _coinChance = 0.14f;
        [SerializeField, Tooltip("0 chooses a new coin layout each run.")] private int _coinSeed;
        [SerializeField, Range(0f, 1f)] private float _rewardChance = 0.03f;
        [SerializeField, Min(1)] private int _rewardPoints = 100;
        [SerializeField] private CellTriggerDefinition[] _cellTriggers = Array.Empty<CellTriggerDefinition>();
        [SerializeField] private RowTriggerDefinition[] _rowTriggers = Array.Empty<RowTriggerDefinition>();
        public static MapManager Instance { get; private set; }
        public event Action<string, Vector2Int> CellTriggered;
        public event Action<string, int> RowTriggered;
        public event Action<int> RowReached;
        public event Action<string> ThemeChanged;
        public string CurrentThemeId { get; private set; }
        public IReadOnlyDictionary<Vector2Int, int> Coins => _coins;
        public IReadOnlyDictionary<Vector2Int, int> Rewards => _rewards;
        private int _firstRow;
        private bool _loaded;
        private readonly Dictionary<Vector2Int, int> _coins = new();
        private readonly Dictionary<Vector2Int, int> _rewards = new();
        private readonly List<PlacedPattern> _placedPatterns = new();
        private readonly List<MovingObstacle> _movingObstacles = new();
        private readonly Dictionary<Vector2Int, CellTriggerDefinition> _registeredCells = new();
        private readonly Dictionary<string, RowTriggerDefinition> _registeredRows = new();
        private readonly HashSet<string> _firedCellTriggers = new();
        private readonly HashSet<string> _firedRowTriggers = new();
        private readonly HashSet<int> _reachedRows = new();
        private readonly Dictionary<int, HashSet<int>> _steppingStones = new();
        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.left, Vector2Int.right, Vector2Int.down };
        private Tilemap _coinLayer;
        private Tile _coinTile;
        private Tile _rewardTile;
        private int _runSeed;
        private int _highestGeneratedRow = int.MinValue;
        private bool _contentInitialized;
        private MapPattern[] _patterns;
        private System.Random _patternRandom;
        private MapPattern _previousPattern;
        private string _generationThemeId;
        private int _nextPatternRow = 1;
        private int _regenerateFromRow = int.MaxValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            if (_ground == null || _structures == null || _groundTile == null || _player == null)
                throw new InvalidOperationException("MapManager requires ground, structures, a ground tile and a player.");
            Instance = this;
        }

        public bool CanMoveTo(Vector2Int position)
        {
            if (position.x < -HalfWidth || position.x > HalfWidth) return false;
            var cell = new Vector3Int(position.x, position.y, 0);
            if (_structures.HasTile(cell)) return false;
            if (_ground.HasTile(cell)) return true;
            var pattern = GetPattern(position.y);
            return pattern == null || pattern.Template.GetObstacle(new Vector2Int(position.x, position.y - pattern.FirstRow)) == null;
        }

        public bool IsLethal(Vector2Int position) =>
            _steppingStones.TryGetValue(position.y, out var columns) && !columns.Contains(position.x);

        private bool IsSafeCell(Vector2Int position) => CanMoveTo(position) && !IsLethal(position);

        private bool CanStandOn(Vector2Int position) => IsSafeCell(position) &&
            _ground.HasTile(new Vector3Int(position.x, position.y, 0));

        public Vector3 GetWorldPosition(Vector2Int position) => _ground.GetCellCenterWorld(new Vector3Int(position.x, position.y, 0));

        public void LoadRows(int playerRow)
        {
            EnsureContentInitialized();
            int firstRow = playerRow - RowsBehind;
            if (_loaded && firstRow == _firstRow && _regenerateFromRow == int.MaxValue)
                return;
            EnsurePatternsThrough(firstRow + VisibleRows);
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
                if (!_loaded || row < _firstRow || row >= _firstRow + VisibleRows || row >= _regenerateFromRow)
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
            var expiredRewards = new List<Vector2Int>();
            foreach (var reward in _rewards)
                if (reward.Key.y < firstRow || reward.Key.y >= firstRow + VisibleRows) expiredRewards.Add(reward.Key);
            foreach (var position in expiredRewards) RemoveReward(position);
            _movingObstacles.RemoveAll(obstacle => obstacle.Position.y < firstRow);
            foreach (var pattern in _placedPatterns)
                foreach (var definition in pattern.Template.MovingObstacles)
                {
                    var position = definition.position + new Vector2Int(0, pattern.FirstRow);
                    if (position.y <= _highestGeneratedRow || position.y < firstRow || position.y >= firstRow + VisibleRows) continue;
                    if (!TrySetObstacle(position, definition.tile))
                        throw new InvalidOperationException($"Moving obstacle in '{pattern.Template.name}' could not be placed at {position}.");
                    _movingObstacles.Add(new MovingObstacle { Definition = definition, Origin = position, Position = position,
                        NextMoveTime = Time.time + definition.stepInterval });
                }
            if (!HasValidRoutes())
                throw new InvalidOperationException("The configured map has an unreachable trigger or no safe forward route.");
            for (int row = firstRow; row < firstRow + VisibleRows; row++)
            {
                if (row > _highestGeneratedRow)
                    GenerateRowContent(row);
            }
            _regenerateFromRow = int.MaxValue;
            for (int i = _placedPatterns.Count - 1; i >= 0; i--)
                if (_placedPatterns[i].LastRow < firstRow) RemovePattern(i);
            _reachedRows.RemoveWhere(row => row < firstRow);
        }

        private void EnsureContentInitialized()
        {
            if (_contentInitialized) return;
            _steppingStones.Clear();
            _registeredCells.Clear();
            _registeredRows.Clear();
            _runSeed = _coinSeed == 0 ? Environment.TickCount : _coinSeed;
            if (string.IsNullOrWhiteSpace(_patternResourcesPath))
                throw new InvalidOperationException("A Resources-relative pattern directory is required.");
            if (_rewardPoints <= 0 || float.IsNaN(_repeatWeight) || _repeatWeight < 0f || _repeatWeight > 1f)
                throw new InvalidOperationException("Reward points must be positive and repeat weight must be between zero and one.");
            if (_patterns == null)
            {
                var prefabs = Resources.LoadAll<GameObject>(_patternResourcesPath);
                var patterns = new List<MapPattern>();
                foreach (var prefab in prefabs)
                {
                    var pattern = prefab.GetComponent<MapPattern>();
                    if (pattern == null)
                        throw new InvalidOperationException($"Prefab '{prefab.name}' in '{_patternResourcesPath}' has no MapPattern component.");
                    patterns.Add(pattern);
                }
                patterns.Sort((left, right) => string.CompareOrdinal(left.ThemeId + "/" + left.name, right.ThemeId + "/" + right.name));
                _patterns = patterns.ToArray();
            }
            var ids = new HashSet<string>();
            foreach (var pattern in _patterns)
            {
                pattern.Validate();
                if (!ids.Add(pattern.ThemeId + "/" + pattern.name))
                    throw new InvalidOperationException($"Duplicate pattern '{pattern.name}' in theme '{pattern.ThemeId}'.");
                foreach (var trigger in pattern.RowTriggers)
                    if (!string.IsNullOrEmpty(trigger.nextThemeId) && !Array.Exists(_patterns, item => item.ThemeId == trigger.nextThemeId))
                        throw new InvalidOperationException($"Trigger '{trigger.id}' requests unknown theme '{trigger.nextThemeId}'.");
            }
            if (!Array.Exists(_patterns, pattern => pattern.ThemeId == _initialThemeId))
                throw new InvalidOperationException($"No patterns for theme '{_initialThemeId}' in Resources/{_patternResourcesPath}.");
            _generationThemeId = CurrentThemeId = _initialThemeId;
            _patternRandom = new System.Random(_runSeed);
            foreach (var definition in _cellTriggers)
                if (!TryRegisterCellTrigger(definition))
                    throw new InvalidOperationException($"Cell trigger '{definition.id}' is on an invalid or occupied cell.");
            foreach (var definition in _rowTriggers)
                if (!TryRegisterRowTrigger(definition))
                    throw new InvalidOperationException($"Row trigger '{definition.id}' is duplicated.");
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
            _rewardTile = ScriptableObject.CreateInstance<Tile>();
            _rewardTile.name = "Bonus Reward Tile";
            _rewardTile.sprite = PlaceholderSprites.Reward();
            _rewardTile.colliderType = Tile.ColliderType.None;
            _rewardTile.hideFlags = HideFlags.DontSave;
            _contentInitialized = true;
        }

        private void GenerateRowContent(int row)
        {
            _highestGeneratedRow = row;
            // Keep the starting row and the single row behind it clear.
            if (row <= 0) return;
            var random = new System.Random(unchecked(_runSeed * 397 ^ row));
            var reachable = GetReachableCells();
            for (int x = -HalfWidth; x <= HalfWidth; x++)
            {
                var position = new Vector2Int(x, row);
                if (random.NextDouble() < _coinChance && reachable.Contains(position))
                    TryPlaceCoin(position, 1, reachable);
                else if (random.NextDouble() < _rewardChance && reachable.Contains(position) &&
                    !_coins.ContainsKey(position) && !_registeredCells.ContainsKey(position))
                {
                    _rewards.Add(position, _rewardPoints);
                    _coinLayer.SetTile((Vector3Int)position, _rewardTile);
                }
            }
        }

        public bool TryPlaceCoin(Vector2Int position, int amount = 1)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            EnsureContentInitialized();
            return TryPlaceCoin(position, amount, GetReachableCells());
        }

        private bool TryPlaceCoin(Vector2Int position, int amount, HashSet<Vector2Int> reachable)
        {
            var cell = new Vector3Int(position.x, position.y, 0);
            if (!CanStandOn(position) || _registeredCells.ContainsKey(position) || _coins.ContainsKey(position) || _rewards.ContainsKey(position) ||
                !reachable.Contains(position))
                return false;
            _coins.Add(position, amount);
            _coinLayer.SetTile(cell, _coinTile);
            return true;
        }

        public int GetCoinAmount(Vector2Int position) => _coins.TryGetValue(position, out int amount) ? amount : 0;

        public int GetRewardPoints(Vector2Int position) => _rewards.TryGetValue(position, out int points) ? points : 0;

        public void RemoveReward(Vector2Int position)
        {
            if (!_rewards.Remove(position)) return;
            if (_coinLayer != null) _coinLayer.SetTile((Vector3Int)position, null);
        }

        public void RemoveCoin(Vector2Int position)
        {
            if (!_coins.Remove(position)) return;
            if (_coinLayer != null)
                _coinLayer.SetTile(new Vector3Int(position.x, position.y, 0), null);
        }

        public bool TrySetObstacle(Vector2Int position, TileBase tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            EnsureContentInitialized();
            if (!CanStandOn(position) || position == _player.Position || _coins.ContainsKey(position) || _rewards.ContainsKey(position) || _registeredCells.ContainsKey(position))
                return false;
            var cell = new Vector3Int(position.x, position.y, 0);
            _structures.SetTile(cell, tile);
            if (!HasValidRoutes())
            {
                _structures.SetTile(cell, null);
                return false;
            }
            return true;
        }

        public bool TryMoveObstacle(Vector2Int from, Vector2Int to)
        {
            if (!_loaded || (to - from).sqrMagnitude != 1 || !CanStandOn(to) || to == _player.Position ||
                _coins.ContainsKey(to) || _rewards.ContainsKey(to) || _registeredCells.ContainsKey(to)) return false;
            var source = new Vector3Int(from.x, from.y, 0);
            var destination = new Vector3Int(to.x, to.y, 0);
            var tile = _structures.GetTile(source);
            if (tile == null || !_ground.HasTile(source)) return false;
            _structures.SetTile(source, null);
            _structures.SetTile(destination, tile);
            if (HasValidRoutes()) return true;
            _structures.SetTile(destination, null);
            _structures.SetTile(source, tile);
            return false;
        }

        public bool TryMoveSteppingStone(Vector2Int from, Vector2Int to)
        {
            if (!_loaded || from.y != to.y || Mathf.Abs(from.x - to.x) != 1 ||
                !_steppingStones.TryGetValue(from.y, out var columns) || !columns.Contains(from.x) || columns.Contains(to.x) ||
                to.x < -HalfWidth || to.x > HalfWidth || from == _player.Position ||
                _coins.ContainsKey(from) || _rewards.ContainsKey(from) || _registeredCells.ContainsKey(from) ||
                !_ground.HasTile(new Vector3Int(from.x, from.y, 0)) ||
                _structures.HasTile(new Vector3Int(from.x, from.y, 0)) ||
                _structures.HasTile(new Vector3Int(to.x, to.y, 0))) return false;
            columns.Remove(from.x);
            columns.Add(to.x);
            if (!HasValidRoutes())
            {
                columns.Remove(to.x);
                columns.Add(from.x);
                return false;
            }
            var pattern = GetPattern(from.y);
            _ground.SetColor(new Vector3Int(from.x, from.y, 0), pattern.Template.LavaColor);
            _ground.SetColor(new Vector3Int(to.x, to.y, 0), pattern.Template.SteppingStoneColor);
            return true;
        }

        public bool TryGetNextStep(Vector2Int from, Vector2Int target, out Vector2Int next)
        {
            next = from;
            if (!_loaded || from == target || !CanStandOn(target) ||
                !_ground.HasTile(new Vector3Int(from.x, from.y, 0)) || IsLethal(from)) return false;
            var queue = new Queue<Vector2Int>();
            var parents = new Dictionary<Vector2Int, Vector2Int> { [from] = from };
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var position = queue.Dequeue();
                foreach (var direction in Directions)
                {
                    var destination = position + direction;
                    if (!CanStandOn(destination) || parents.ContainsKey(destination)) continue;
                    parents.Add(destination, position);
                    if (destination == target)
                    {
                        next = target;
                        while (parents[next] != from) next = parents[next];
                        return true;
                    }
                    queue.Enqueue(destination);
                }
            }
            return false;
        }

        public bool RegisterCellTrigger(CellTriggerDefinition definition)
        {
            EnsureContentInitialized();
            return TryRegisterCellTrigger(definition);
        }

        private bool TryRegisterCellTrigger(CellTriggerDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                throw new ArgumentException("A cell trigger requires a stable ID.", nameof(definition));
            var position = definition.position;
            if (position.x < -HalfWidth || position.x > HalfWidth || IsLethal(position) ||
                !CanMoveTo(position) ||
                _coins.ContainsKey(position) || _rewards.ContainsKey(position) || _registeredCells.ContainsKey(position) ||
                _loaded && position.y < _firstRow ||
                _loaded && _ground.HasTile(new Vector3Int(position.x, position.y, 0)) && !GetReachableCells().Contains(position))
                return false;
            foreach (var existing in _registeredCells.Values)
                if (existing.id == definition.id) return false;
            _registeredCells.Add(definition.position, definition);
            return true;
        }

        public bool RegisterRowTrigger(RowTriggerDefinition definition)
        {
            EnsureContentInitialized();
            return TryRegisterRowTrigger(definition);
        }

        private bool TryRegisterRowTrigger(RowTriggerDefinition definition)
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
            var pattern = GetPattern(_player.FurthestRow);
            if (pattern != null && CurrentThemeId != pattern.Template.ThemeId)
            {
                CurrentThemeId = pattern.Template.ThemeId;
                ThemeChanged?.Invoke(CurrentThemeId);
            }
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
                if (!string.IsNullOrEmpty(row.nextThemeId)) RequestThemeChange(row.nextThemeId);
            }
        }

        private void SetGroundRow(int row, TileBase tile)
        {
            var pattern = GetPattern(row);
            for (int column = -HalfWidth; column <= HalfWidth; column++)
            {
                var cell = new Vector3Int(column, row, 0);
                var local = new Vector3Int(column, row - (pattern?.FirstRow ?? 0), 0);
                _ground.SetTile(cell, tile != null && pattern != null ? pattern.Template.Ground.GetTile(local) : tile);
                if (tile == null)
                {
                    _structures.SetTile(cell, null);
                    continue;
                }
                _ground.SetTileFlags(cell, TileFlags.None);
                _ground.SetTransformMatrix(cell, pattern != null ? pattern.Template.Ground.GetTransformMatrix(local) : Matrix4x4.identity);
                _ground.SetColor(cell, _steppingStones.ContainsKey(row)
                    ? (IsLethal(new Vector2Int(column, row)) ? pattern.Template.LavaColor : pattern.Template.SteppingStoneColor)
                    : pattern != null ? pattern.Template.Ground.GetColor(local) * pattern.Template.Ground.color : Color.white);
                _structures.SetTile(cell, pattern?.Template.Structures.GetTile(local));
                if (pattern != null && _structures.HasTile(cell))
                {
                    _structures.SetTileFlags(cell, TileFlags.None);
                    _structures.SetColor(cell, pattern.Template.Structures.GetColor(local) * pattern.Template.Structures.color);
                    _structures.SetTransformMatrix(cell, pattern.Template.Structures.GetTransformMatrix(local));
                }
            }
        }

        private HashSet<Vector2Int> GetReachableCells()
        {
            var reachable = new HashSet<Vector2Int>();
            if (!_loaded || !CanStandOn(_player.Position)) return reachable;
            var start = new Vector3Int(_player.Position.x, _player.Position.y, _player.FurthestRow);
            var visited = new HashSet<Vector3Int> { start };
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                var position = new Vector2Int(state.x, state.y);
                reachable.Add(position);
                foreach (var direction in Directions)
                {
                    var destination = position + direction;
                    if (destination.y < state.z - PlayerController.MaxBackwardSteps || !CanStandOn(destination)) continue;
                    var next = new Vector3Int(destination.x, destination.y, Math.Max(state.z, destination.y));
                    if (visited.Add(next)) queue.Enqueue(next);
                }
            }
            return reachable;
        }

        private bool HasValidRoutes()
        {
            if (!_loaded || !CanStandOn(_player.Position)) return false;
            int lastRow = _firstRow + VisibleRows - 1;
            // Adjacent rows stay connected so advancing cannot strand a collectible
            // behind a wall that would require two backward steps to go around.
            // Include the next row before it appears, since it may be a configured hazard.
            for (int row = _firstRow + 1; row <= lastRow + 1; row++)
                if (!AreRowsConnected(row)) return false;
            var reachable = GetReachableCells();
            foreach (var coin in _coins)
                if (!reachable.Contains(coin.Key)) return false;
            foreach (var reward in _rewards)
                if (!reachable.Contains(reward.Key)) return false;
            foreach (var trigger in _registeredCells.Values)
                if (trigger.position.y >= _firstRow && trigger.position.y <= lastRow && !reachable.Contains(trigger.position)) return false;
            return true;
        }

        private bool AreRowsConnected(int row)
        {
            var connected = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            for (int x = -HalfWidth; x <= HalfWidth; x++)
            {
                var position = new Vector2Int(x, row);
                if (!IsSafeCell(position)) continue;
                connected.Add(position);
                queue.Enqueue(position);
                break;
            }
            if (queue.Count == 0) return false;
            while (queue.Count > 0)
            {
                var position = queue.Dequeue();
                foreach (var direction in Directions)
                {
                    var destination = position + direction;
                    if (destination.y < row - 1 || destination.y > row || !IsSafeCell(destination)) continue;
                    if (connected.Add(destination)) queue.Enqueue(destination);
                }
            }
            for (int y = row - 1; y <= row; y++)
                for (int x = -HalfWidth; x <= HalfWidth; x++)
                {
                    var position = new Vector2Int(x, y);
                    if (IsSafeCell(position) && !connected.Contains(position)) return false;
                }
            return true;
        }

        private PlacedPattern GetPattern(int row)
        {
            foreach (var pattern in _placedPatterns)
                if (row >= pattern.FirstRow && row <= pattern.LastRow) return pattern;
            return null;
        }

        private void EnsurePatternsThrough(int lastRow)
        {
            while (_nextPatternRow <= lastRow)
            {
                var candidates = new List<MapPattern>();
                float weight = 0f;
                foreach (var pattern in _patterns)
                {
                    if (pattern.ThemeId != _generationThemeId) continue;
                    candidates.Add(pattern);
                    weight += pattern == _previousPattern ? _repeatWeight : 1f;
                }
                MapPattern selected = candidates[0];
                if (weight > 0f)
                {
                    double choice = _patternRandom.NextDouble() * weight;
                    foreach (var candidate in candidates)
                    {
                        choice -= candidate == _previousPattern ? _repeatWeight : 1f;
                        if (choice < 0d) { selected = candidate; break; }
                    }
                }
                var placement = new PlacedPattern { Template = selected, FirstRow = _nextPatternRow };
                _placedPatterns.Add(placement);
                foreach (var lava in selected.LavaRows)
                    _steppingStones.Add(placement.FirstRow + lava.row, new HashSet<int>(lava.steppingStoneColumns));
                foreach (var trigger in selected.RowTriggers)
                {
                    var instance = new RowTriggerDefinition
                    {
                        id = placement.FirstRow + "/" + selected.name + "/" + trigger.id,
                        row = placement.FirstRow + trigger.row,
                        oncePerRun = trigger.oncePerRun,
                        onReached = trigger.onReached,
                        nextThemeId = trigger.nextThemeId
                    };
                    if (!TryRegisterRowTrigger(instance))
                        throw new InvalidOperationException($"Pattern trigger '{instance.id}' is duplicated.");
                    placement.TriggerIds.Add(instance.id);
                }
                _nextPatternRow = checked(placement.LastRow + 1);
                _previousPattern = selected;
            }
        }

        public void RequestThemeChange(string themeId)
        {
            EnsureContentInitialized();
            if (string.IsNullOrWhiteSpace(themeId) || !Array.Exists(_patterns, pattern => pattern.ThemeId == themeId))
                throw new ArgumentException($"No patterns for theme '{themeId}'.", nameof(themeId));
            var current = GetPattern(Math.Max(1, _player.FurthestRow));
            int boundary = current != null ? checked(current.LastRow + 1) : 1;
            var next = GetPattern(boundary);
            if (next?.Template.ThemeId == themeId && _generationThemeId == themeId) return;
            // Lookahead may already contain the next pattern. Replace only unvisited rows past the current boundary.
            for (int i = _placedPatterns.Count - 1; i >= 0; i--)
                if (_placedPatterns[i].FirstRow >= boundary) RemovePattern(i);
            var coins = new List<Vector2Int>(_coins.Keys);
            foreach (var position in coins) if (position.y >= boundary) RemoveCoin(position);
            var rewards = new List<Vector2Int>(_rewards.Keys);
            foreach (var position in rewards) if (position.y >= boundary) RemoveReward(position);
            foreach (var obstacle in _movingObstacles)
                if (obstacle.Origin.y >= boundary || obstacle.Position.y >= boundary)
                    _structures.SetTile((Vector3Int)obstacle.Position, null);
            _movingObstacles.RemoveAll(obstacle => obstacle.Origin.y >= boundary || obstacle.Position.y >= boundary);
            for (int row = boundary; _loaded && row < _firstRow + VisibleRows; row++) SetGroundRow(row, null);
            _generationThemeId = themeId;
            _previousPattern = current?.Template;
            _nextPatternRow = boundary;
            _highestGeneratedRow = Math.Min(_highestGeneratedRow, boundary - 1);
            _regenerateFromRow = boundary;
            if (_loaded) LoadRows(_player.FurthestRow);
        }

        private void RemovePattern(int index)
        {
            var pattern = _placedPatterns[index];
            foreach (var lava in pattern.Template.LavaRows) _steppingStones.Remove(pattern.FirstRow + lava.row);
            foreach (string id in pattern.TriggerIds)
            {
                _registeredRows.Remove(id);
                _firedRowTriggers.Remove(id);
            }
            _placedPatterns.RemoveAt(index);
        }

        private void Update()
        {
            if (!_loaded || GameStateManager.Instance == null || GameStateManager.Instance.State != GameState.Playing) return;
            AdvanceMovingObstacles(Time.time);
        }

        private void AdvanceMovingObstacles(float now)
        {
            foreach (var obstacle in _movingObstacles)
            {
                if (now < obstacle.NextMoveTime) continue;
                var definition = obstacle.Definition;
                obstacle.NextMoveTime = now + definition.stepInterval;
                Vector2Int next;
                if (definition.movement == ObstacleMovement.Chase)
                {
                    if (!TryGetNextStep(obstacle.Position, _player.Position, out next)) continue;
                }
                else
                {
                    var offset = obstacle.Position - obstacle.Origin;
                    int step = offset.x * definition.direction.x + offset.y * definition.direction.y;
                    if (step == definition.distance) obstacle.Direction = -1;
                    else if (step == 0) obstacle.Direction = 1;
                    next = obstacle.Position + definition.direction * obstacle.Direction;
                }
                if (TryMoveObstacle(obstacle.Position, next)) obstacle.Position = next;
                else if (definition.movement == ObstacleMovement.Patrol) obstacle.Direction = -obstacle.Direction;
            }
        }

        private sealed class PlacedPattern
        {
            public MapPattern Template;
            public int FirstRow;
            public int LastRow => FirstRow + Template.Length - 1;
            public readonly List<string> TriggerIds = new();
        }

        private sealed class MovingObstacle
        {
            public MovingObstacleDefinition Definition;
            public Vector2Int Origin;
            public Vector2Int Position;
            public int Direction = 1;
            public float NextMoveTime;
        }

        private void OnDestroy()
        {
            if (_rewardTile != null)
            {
                if (Application.isPlaying) Destroy(_rewardTile);
                else DestroyImmediate(_rewardTile);
            }
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
    public sealed class HazardRowDefinition
    {
        public int row;
        public int[] steppingStoneColumns = { 0 };
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
        public string nextThemeId;
        public UnityEvent onReached = new();
    }
}
