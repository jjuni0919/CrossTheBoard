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
        public const int MonsterDespawnDistance = 8;
        private const int MaximumRenderedPatterns = 3;

        [SerializeField] private Tilemap _ground;
        [SerializeField] private Tilemap _structures;
        [SerializeField] private Tilemap _coinLayer;
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
        private int _lastRow;
        private int _contentFirstRow;
        private int _currentPatternFirstRow;
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
        private Tile _coinTile;
        private Tile _rewardTile;
        private int _runSeed;
        private int _highestGeneratedRow = int.MinValue;
        private bool _contentInitialized;
        private MapPattern[] _patterns;
        private System.Random _patternRandom;
        private MapPattern _previousPattern;
        private string _generationThemeId;
        private string _pendingThemeId;
        private int _nextPatternRow;
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

        private void Start() => LoadRows(_player.FurthestRow);

        public bool CanMoveTo(Vector2Int position)
        {
            if (position.x < -HalfWidth || position.x > HalfWidth) return false;
            var tile = GetStructure(position);
            return tile == null || tile is DamageTile;
        }

        private TileBase GetStructure(Vector2Int position)
        {
            var cell = (Vector3Int)position;
            var tile = _structures.GetTile(cell);
            if (tile != null || _ground.HasTile(cell)) return tile;
            var pattern = GetPattern(position.y);
            return pattern?.Template.Structures.GetTile(new Vector3Int(position.x, position.y - pattern.FirstRow, 0));
        }

        public int GetContactDamage(Vector2Int position) => GetStructure(position) is DamageTile tile ? tile.Damage : 0;

        public void DamagePlayerAt(Vector2Int position)
        {
            if (position != _player.Position) return;
            int damage = GetContactDamage(position);
            if (damage > 0) _player.TakeDamage(damage);
        }

        public bool IsLethal(Vector2Int position) =>
            _steppingStones.TryGetValue(position.y, out var columns) && !columns.Contains(position.x);

        private bool IsSafeCell(Vector2Int position) => CanMoveTo(position) && !IsLethal(position) &&
            (GetContactDamage(position) == 0 || GetMovingObstacle(position) != null);

        private MovingObstacle GetMovingObstacle(Vector2Int position)
        {
            foreach (var obstacle in _movingObstacles)
                if (obstacle.Position == position) return obstacle;
            return null;
        }

        private bool CanStandOn(Vector2Int position) => IsSafeCell(position) &&
            _ground.HasTile(new Vector3Int(position.x, position.y, 0));

        public Vector3 GetWorldPosition(Vector2Int position) => _ground.GetCellCenterWorld(new Vector3Int(position.x, position.y, 0));

        public void LoadRows(int playerRow)
        {
            EnsureContentInitialized();
            while (_placedPatterns.Count < MaximumRenderedPatterns)
                EnsurePatternsThrough(_nextPatternRow);
            UpdateChasers();
            while (playerRow > GetPattern(_currentPatternFirstRow).LastRow)
            {
                var current = GetPattern(_currentPatternFirstRow);
                int boundary = checked(current.LastRow + 1);
                while (_placedPatterns[0].FirstRow < current.FirstRow) RemovePattern(0);
                if (_pendingThemeId != null)
                {
                    _generationThemeId = _pendingThemeId;
                    _pendingThemeId = null;
                    _previousPattern = current.Template;
                    for (int i = _placedPatterns.Count - 1; i > 0; i--) RemovePattern(i);
                    _nextPatternRow = boundary;
                    _highestGeneratedRow = boundary - 1;
                    _regenerateFromRow = boundary;
                }
                _currentPatternFirstRow = boundary;
                while (_placedPatterns.Count < MaximumRenderedPatterns)
                    EnsurePatternsThrough(_nextPatternRow);
            }
            int firstRow = _placedPatterns[0].FirstRow;
            int contentFirstRow = Math.Max(0, playerRow - RowsBehind);
            int lastRow = _placedPatterns[_placedPatterns.Count - 1].LastRow;
            if (_loaded && firstRow == _firstRow && contentFirstRow == _contentFirstRow &&
                lastRow == _lastRow && _regenerateFromRow == int.MaxValue) return;
            if (!_loaded)
            {
                _ground.ClearAllTiles();
                _structures.ClearAllTiles();
            }
            else
            {
                for (int row = _firstRow; row <= _lastRow; row++)
                {
                    if (row < firstRow || row > lastRow || row >= _regenerateFromRow)
                        SetGroundRow(row, null);
                }
            }
            for (int row = firstRow; row <= lastRow; row++)
            {
                if (!_loaded || row < _firstRow || row > _lastRow || row >= _regenerateFromRow)
                    SetGroundRow(row, _groundTile);
            }
            _firstRow = firstRow;
            _contentFirstRow = contentFirstRow;
            _lastRow = lastRow;
            _loaded = true;
            _ground.CompressBounds();
            var expiredCoins = new List<Vector2Int>();
            foreach (var coin in _coins)
                if (coin.Key.y < contentFirstRow || coin.Key.y > lastRow || coin.Key.y >= _regenerateFromRow)
                    expiredCoins.Add(coin.Key);
            foreach (var position in expiredCoins)
                RemoveCoin(position);
            var expiredRewards = new List<Vector2Int>();
            foreach (var reward in _rewards)
                if (reward.Key.y < contentFirstRow || reward.Key.y > lastRow || reward.Key.y >= _regenerateFromRow) expiredRewards.Add(reward.Key);
            foreach (var position in expiredRewards) RemoveReward(position);
            foreach (var obstacle in _movingObstacles)
                if (obstacle.Position.y < firstRow || obstacle.Position.y >= _regenerateFromRow)
                    SetStructureTile((Vector3Int)obstacle.Position, null);
            _movingObstacles.RemoveAll(obstacle => obstacle.Position.y < firstRow || obstacle.Position.y >= _regenerateFromRow);
            foreach (var pattern in _placedPatterns)
                foreach (var definition in pattern.Template.MovingObstacles)
                {
                    var position = definition.position + new Vector2Int(0, pattern.FirstRow);
                    if (position.y <= _highestGeneratedRow || position.y < firstRow || position.y > lastRow) continue;
                    var cell = (Vector3Int)position;
                    if (!CanStandOn(position) || _structures.HasTile(cell) || position == _player.Position)
                        throw new InvalidOperationException($"Moving obstacle in '{pattern.Template.name}' could not be placed at {position}.");
                    SetStructureTile(cell, definition.tile);
                    _movingObstacles.Add(new MovingObstacle { Definition = definition, Origin = position, Position = position,
                        NextMoveTime = Time.time + definition.stepInterval });
                }
            UpdateChasers();
            if (!HasValidRoutes())
                throw new InvalidOperationException("The configured map has an unreachable trigger or no safe forward route.");
            var reachable = GetReachableCells();
            for (int row = firstRow; row <= lastRow; row++)
            {
                if (row > _highestGeneratedRow)
                    GenerateRowContent(row, reachable);
            }
            _regenerateFromRow = int.MaxValue;
            _structures.CompressBounds();
            _coinLayer.CompressBounds();
            UpdatePatternViews();
            _reachedRows.RemoveWhere(row => row < firstRow);
            var expiredCells = new List<Vector2Int>();
            foreach (var trigger in _registeredCells.Values)
                if (trigger.position.y < firstRow) expiredCells.Add(trigger.position);
            foreach (var position in expiredCells)
            {
                _firedCellTriggers.Remove(_registeredCells[position].id);
                _registeredCells.Remove(position);
            }
            var expiredRows = new List<string>();
            foreach (var trigger in _registeredRows.Values)
                if (trigger.row < firstRow) expiredRows.Add(trigger.id);
            foreach (var id in expiredRows)
            {
                _firedRowTriggers.Remove(id);
                _registeredRows.Remove(id);
            }
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
            if (_coinLayer == null)
            {
                var layer = new GameObject("Coins", typeof(Tilemap), typeof(TilemapRenderer));
                layer.transform.SetParent(transform, false);
                _coinLayer = layer.GetComponent<Tilemap>();
            }
            _coinLayer.transform.localPosition = _ground.transform.localPosition;
            _coinLayer.transform.localRotation = _ground.transform.localRotation;
            _coinLayer.transform.localScale = _ground.transform.localScale;
            _coinLayer.ClearAllTiles();
            _coinLayer.tileAnchor = _ground.tileAnchor;
            var renderer = _coinLayer.GetComponent<TilemapRenderer>();
            var groundRenderer = _ground.GetComponent<TilemapRenderer>();
            if (groundRenderer != null)
            {
                renderer.sharedMaterial = groundRenderer.sharedMaterial;
                renderer.sortingLayerID = groundRenderer.sortingLayerID;
                renderer.sortingOrder = Mathf.Max(groundRenderer.sortingOrder, _structures.GetComponent<TilemapRenderer>().sortingOrder) + 1;
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

        private void GenerateRowContent(int row, HashSet<Vector2Int> reachable)
        {
            _highestGeneratedRow = row;
            if (row <= 1) return;
            var random = new System.Random(unchecked(_runSeed * 397 ^ row));
            for (int x = -HalfWidth; x <= HalfWidth; x++)
            {
                var position = new Vector2Int(x, row);
                if (random.NextDouble() < _coinChance && reachable.Contains(position))
                    TryPlaceCoin(position, 1, reachable);
                else if (random.NextDouble() < _rewardChance && reachable.Contains(position) &&
                    GetContactDamage(position) == 0 && !_coins.ContainsKey(position) && !_registeredCells.ContainsKey(position))
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
            if (!CanStandOn(position) || GetContactDamage(position) > 0 || _registeredCells.ContainsKey(position) || _coins.ContainsKey(position) || _rewards.ContainsKey(position) ||
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
            if (tile is DamageTile damageTile && damageTile.Damage <= 0)
                throw new ArgumentException("Damage tiles require positive damage.", nameof(tile));
            EnsureContentInitialized();
            if (!CanStandOn(position) || position == _player.Position || _coins.ContainsKey(position) || _rewards.ContainsKey(position) || _registeredCells.ContainsKey(position))
                return false;
            var cell = new Vector3Int(position.x, position.y, 0);
            if (_structures.HasTile(cell)) return false;
            SetStructureTile(cell, tile);
            if (!HasValidRoutes())
            {
                SetStructureTile(cell, null);
                return false;
            }
            return true;
        }

        public bool TryMoveObstacle(Vector2Int from, Vector2Int to)
        {
            var moving = GetMovingObstacle(from);
            if (moving != null)
            {
                if (!_loaded || (to - from).sqrMagnitude != 1 || !CanStandOn(to) ||
                    moving.Definition.movement == ObstacleMovement.Chase && _steppingStones.ContainsKey(to.y) ||
                    _structures.HasTile((Vector3Int)to)) return false;
                SetStructureTile((Vector3Int)from, null);
                SetStructureTile((Vector3Int)to, moving.Definition.tile);
                moving.Position = to;
                DamagePlayerAt(to);
                return true;
            }
            if (!_loaded || (to - from).sqrMagnitude != 1 || !CanStandOn(to) || to == _player.Position ||
                _coins.ContainsKey(to) || _rewards.ContainsKey(to) || _registeredCells.ContainsKey(to)) return false;
            var source = new Vector3Int(from.x, from.y, 0);
            var destination = new Vector3Int(to.x, to.y, 0);
            var tile = _structures.GetTile(source);
            if (tile == null || !_ground.HasTile(source)) return false;
            SetStructureTile(source, null);
            SetStructureTile(destination, tile);
            if (HasValidRoutes()) return true;
            SetStructureTile(destination, null);
            SetStructureTile(source, tile);
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
            SyncPatternCell((Vector3Int)from);
            SyncPatternCell((Vector3Int)to);
            return true;
        }

        public bool TryGetNextStep(Vector2Int from, Vector2Int target, out Vector2Int next)
        {
            next = from;
            var moving = GetMovingObstacle(from);
            // Player paths still cross stepping stones when a monster shares the player's cell.
            bool playerPath = from == _player.Position;
            bool movingObstacle = moving != null && !playerPath;
            bool chaser = movingObstacle && moving.Definition.movement == ObstacleMovement.Chase;
            if (!_loaded || from == target || !CanStandOn(target) ||
                chaser && (_steppingStones.ContainsKey(from.y) || _steppingStones.ContainsKey(target.y)) ||
                !_ground.HasTile(new Vector3Int(from.x, from.y, 0)) || IsLethal(from)) return false;
            var start = new Vector3Int(from.x, from.y, playerPath ? _player.FurthestRow : 0);
            var queue = new Queue<Vector3Int>();
            var parents = new Dictionary<Vector3Int, Vector3Int> { [start] = start };
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                var position = new Vector2Int(state.x, state.y);
                foreach (var direction in Directions)
                {
                    var destination = position + direction;
                    if (!CanStandOn(destination) || playerPath && destination.y < state.z - PlayerController.MaxBackwardSteps ||
                        chaser && _steppingStones.ContainsKey(destination.y) ||
                        movingObstacle && destination != target && GetMovingObstacle(destination) != null) continue;
                    var nextState = new Vector3Int(destination.x, destination.y, playerPath ? Math.Max(state.z, destination.y) : 0);
                    if (parents.ContainsKey(nextState)) continue;
                    parents.Add(nextState, state);
                    if (destination == target)
                    {
                        while (parents[nextState] != start) nextState = parents[nextState];
                        next = new Vector2Int(nextState.x, nextState.y);
                        return true;
                    }
                    queue.Enqueue(nextState);
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
                !CanMoveTo(position) || GetContactDamage(position) > 0 ||
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
                    SyncPatternCell(cell);
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
                SyncPatternCell(cell);
            }
        }

        private void UpdatePatternViews()
        {
            for (int i = 0; i < _placedPatterns.Count; i++)
            {
                var pattern = _placedPatterns[i];
                if (i >= MaximumRenderedPatterns)
                {
                    ReleasePatternView(pattern);
                    continue;
                }
                if (pattern.View != null) continue;
                var view = Instantiate(pattern.Template, transform);
                pattern.View = view;
                view.name = $"Pattern {pattern.FirstRow} - {pattern.Template.name}";
                view.transform.localPosition = _ground.transform.localPosition + new Vector3(0, pattern.FirstRow, 0);
                view.gameObject.SetActive(true);
                view.Ground.GetComponent<TilemapRenderer>().sharedMaterial = _ground.GetComponent<TilemapRenderer>().sharedMaterial;
                view.Structures.GetComponent<TilemapRenderer>().sharedMaterial = _structures.GetComponent<TilemapRenderer>().sharedMaterial;
                view.Ground.color = view.Structures.color = Color.white;
                for (int row = 0; row < pattern.Template.Length; row++)
                    for (int x = -HalfWidth; x <= HalfWidth; x++)
                    {
                        var local = new Vector3Int(x, row, 0);
                        int worldRow = pattern.FirstRow + row;
                        view.Ground.SetTileFlags(local, TileFlags.None);
                        view.Ground.SetColor(local, _steppingStones.ContainsKey(worldRow)
                            ? (IsLethal(new Vector2Int(x, worldRow)) ? pattern.Template.LavaColor : pattern.Template.SteppingStoneColor)
                            : pattern.Template.Ground.GetColor(local) * pattern.Template.Ground.color);
                        if (!view.Structures.HasTile(local)) continue;
                        view.Structures.SetTileFlags(local, TileFlags.None);
                        view.Structures.SetColor(local, pattern.Template.Structures.GetColor(local) * pattern.Template.Structures.color);
                    }
            }
            // The shared grid is gameplay data; drawing it would duplicate the pattern prefabs.
            _ground.GetComponent<TilemapRenderer>().enabled = false;
            _structures.GetComponent<TilemapRenderer>().enabled = false;
            for (int row = _firstRow; row <= _lastRow; row++)
                for (int x = -HalfWidth; x <= HalfWidth; x++) SyncPatternCell(new Vector3Int(x, row, 0));
        }

        private void SyncPatternCell(Vector3Int cell)
        {
            var pattern = GetPattern(cell.y);
            if (pattern?.View == null) return;
            var local = new Vector3Int(cell.x, cell.y - pattern.FirstRow, cell.z);
            var view = pattern.View;
            view.Ground.SetTile(local, _ground.GetTile(cell));
            view.Ground.SetTileFlags(local, TileFlags.None);
            view.Ground.SetColor(local, _ground.GetColor(cell) * _ground.color);
            view.Ground.SetTransformMatrix(local, _ground.GetTransformMatrix(cell));
            view.Structures.SetTile(local, _structures.GetTile(cell));
            view.Structures.SetTileFlags(local, TileFlags.None);
            view.Structures.SetColor(local, _structures.GetColor(cell) * _structures.color);
            view.Structures.SetTransformMatrix(local, _structures.GetTransformMatrix(cell));
        }

        private void SetStructureTile(Vector3Int cell, TileBase tile)
        {
            _structures.SetTile(cell, tile);
            SyncPatternCell(cell);
        }

        private static void ReleasePatternView(PlacedPattern pattern)
        {
            if (pattern.View == null) return;
            var instance = pattern.View.gameObject;
            // Destroy is deferred until frame end, so hide this before creating the next pattern.
            instance.SetActive(false);
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
            pattern.View = null;
        }

        private HashSet<Vector2Int> GetReachableCells()
        {
            var reachable = new HashSet<Vector2Int>();
            if (!_loaded || !CanMoveTo(_player.Position) || IsLethal(_player.Position) ||
                !_ground.HasTile((Vector3Int)_player.Position)) return reachable;
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
            if (!_loaded || !CanMoveTo(_player.Position) || IsLethal(_player.Position) ||
                !_ground.HasTile((Vector3Int)_player.Position)) return false;
            int lastRow = _lastRow;
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
                if (trigger.position.y >= _contentFirstRow && trigger.position.y <= lastRow && !reachable.Contains(trigger.position)) return false;
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
                    if (_nextPatternRow == 0)
                    {
                        var start = new Vector2Int(0, 1);
                        bool lethal = false;
                        foreach (var lava in pattern.LavaRows)
                            if (lava.row == start.y && Array.IndexOf(lava.steppingStoneColumns, start.x) < 0) lethal = true;
                        if (pattern.GetObstacle(start) != null || lethal) continue;
                    }
                    candidates.Add(pattern);
                    weight += pattern == _previousPattern ? _repeatWeight : 1f;
                }
                if (candidates.Count == 0)
                    throw new InvalidOperationException($"No pattern in theme '{_generationThemeId}' has a safe starting cell at (0, 1).");
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
            _pendingThemeId = themeId == _generationThemeId ? null : themeId;
        }

        private void RemovePattern(int index)
        {
            var pattern = _placedPatterns[index];
            ReleasePatternView(pattern);
            bool RemoveWithPattern(MovingObstacle obstacle) =>
                obstacle.Origin.y >= pattern.FirstRow && obstacle.Origin.y <= pattern.LastRow &&
                (obstacle.Definition.movement != ObstacleMovement.Chase || !obstacle.HasStartedChasing ||
                    pattern.FirstRow > _currentPatternFirstRow);
            foreach (var obstacle in _movingObstacles)
                if (RemoveWithPattern(obstacle))
                    SetStructureTile((Vector3Int)obstacle.Position, null);
            _movingObstacles.RemoveAll(RemoveWithPattern);
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
            if (GameStateManager.Instance == null || GameStateManager.Instance.State != GameState.Playing) return;
            LoadRows(_player.FurthestRow);
            DamagePlayerAt(_player.Position);
            if (GameStateManager.Instance.State != GameState.Playing) return;
            AdvanceMovingObstacles(Time.time);
        }

        private void AdvanceMovingObstacles(float now)
        {
            UpdateChasers();
            foreach (var obstacle in _movingObstacles)
            {
                if (GameStateManager.Instance.State != GameState.Playing) return;
                if (now < obstacle.NextMoveTime) continue;
                var definition = obstacle.Definition;
                obstacle.NextMoveTime = now + definition.stepInterval;
                Vector2Int next;
                if (definition.movement == ObstacleMovement.Chase)
                {
                    if (!obstacle.HasStartedChasing) continue;
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
                if (!TryMoveObstacle(obstacle.Position, next) && definition.movement == ObstacleMovement.Patrol)
                    obstacle.Direction = -obstacle.Direction;
            }
        }

        private void UpdateChasers()
        {
            for (int i = _movingObstacles.Count - 1; i >= 0; i--)
            {
                var obstacle = _movingObstacles[i];
                if (obstacle.Definition.movement != ObstacleMovement.Chase) continue;
                long distance = Math.Abs((long)obstacle.Position.x - _player.Position.x) +
                    Math.Abs((long)obstacle.Position.y - _player.Position.y);
                if (distance < MonsterDespawnDistance) obstacle.HasStartedChasing = true;
                else if (obstacle.HasStartedChasing)
                {
                    SetStructureTile((Vector3Int)obstacle.Position, null);
                    _movingObstacles.RemoveAt(i);
                }
            }
        }

        private sealed class PlacedPattern
        {
            public MapPattern Template;
            public MapPattern View;
            public int FirstRow;
            public int LastRow => checked(FirstRow + Template.Length - 1);
            public readonly List<string> TriggerIds = new();
        }

        private sealed class MovingObstacle
        {
            public MovingObstacleDefinition Definition;
            public Vector2Int Origin;
            public Vector2Int Position;
            public int Direction = 1;
            public float NextMoveTime;
            public bool HasStartedChasing;
        }

        private void OnDestroy()
        {
            foreach (var pattern in _placedPatterns) ReleasePatternView(pattern);
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
