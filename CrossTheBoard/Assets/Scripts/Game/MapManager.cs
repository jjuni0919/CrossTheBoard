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

        [SerializeField] private Material _spriteMaterial;
        [SerializeField] private SteppingStone _steppingStonePrefab;
        [SerializeField] private Sprite _coinSprite;
        [SerializeField] private Sprite _rewardSprite;
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
        private readonly Dictionary<Vector2Int, MapObstacle> _obstacles = new();
        private readonly Dictionary<Vector2Int, SpriteRenderer> _collectibleViews = new();
        private readonly List<MovingStone> _stoneViews = new();
        private readonly Dictionary<Vector2Int, int> _coins = new();
        private readonly Dictionary<Vector2Int, int> _rewards = new();
        private readonly List<PlacedPattern> _placedPatterns = new();
        private readonly List<MovingObstacle> _movingObstacles = new();
        private readonly Dictionary<Vector2Int, CellTriggerDefinition> _registeredCells = new();
        private readonly Dictionary<string, RowTriggerDefinition> _registeredRows = new();
        private readonly HashSet<string> _firedCellTriggers = new();
        private readonly HashSet<string> _firedRowTriggers = new();
        private readonly HashSet<int> _reachedRows = new();
        private readonly HashSet<int> _lavaRows = new();
        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.left, Vector2Int.right, Vector2Int.down };

        private int _runSeed;
        private int _highestGeneratedRow = int.MinValue;
        private bool _contentInitialized;
        private MapPattern[] _patterns;
        private System.Random _patternRandom;
        private MapPattern _previousPattern;
        private string _generationThemeId;
        private int _nextPatternRow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            if (_player == null)
                throw new InvalidOperationException("MapManager requires a player.");
            Instance = this;
        }

        private void Start() => LoadRows(_player.FurthestRow);

        public bool CanMoveTo(Vector2Int position) =>
            position.x >= -HalfWidth && position.x <= HalfWidth && position.y >= 0 &&
            (!_obstacles.TryGetValue(position, out var obstacle) || !obstacle.BlocksMovement);

        public int GetContactDamage(Vector2Int position) =>
            _obstacles.TryGetValue(position, out var obstacle) && GetMovingObstacle(position)?.IsMoving != true
                ? obstacle.Damage : 0;

        public void DamagePlayerAt(Vector2Int position)
        {
            if (position == _player.Position && _obstacles.ContainsKey(position) &&
                GetMovingObstacle(position)?.IsMoving != true) _player.Die();
        }

        public bool IsLethal(Vector2Int position)
        {
            if (!_lavaRows.Contains(position.y)) return false;
            foreach (var stone in _stoneViews)
                if (stone.Position.y == position.y &&
                    (stone.Rider == _player && _player.Position == position ||
                    Mathf.Abs(stone.View.transform.localPosition.x - position.x - 0.5f) <= stone.View.SupportHalfWidth))
                    return false;
            return true;
        }

        private bool IsSafeCell(Vector2Int position) => CanMoveTo(position) && !IsLethal(position) &&
            (GetContactDamage(position) == 0 || GetMovingObstacle(position) != null);

        private MovingObstacle GetMovingObstacle(Vector2Int position)
        {
            foreach (var obstacle in _movingObstacles)
                if (obstacle.Position == position) return obstacle;
            return null;
        }

        private bool IsObstacleDestinationReserved(Vector2Int position) =>
            _movingObstacles.Exists(obstacle => obstacle.IsMoving && obstacle.Destination == position);

        private bool HasGround(Vector2Int position) => position.x >= -HalfWidth && position.x <= HalfWidth &&
            GetPattern(position.y) != null;

        private bool CanStandOn(Vector2Int position) => IsSafeCell(position) && HasGround(position);

        public Vector3 GetWorldPosition(Vector2Int position) => transform.TransformPoint(new Vector3(position.x + 0.5f, position.y + 0.5f, 0));

        internal bool TryBoardStone(Vector2Int position, out Vector2Int destination, out Vector3 offset)
        {
            destination = position;
            offset = Vector3.zero;
            foreach (var stone in _stoneViews)
            {
                if (!stone.IsMoving || stone.Position.y != position.y ||
                    Mathf.Abs(stone.View.transform.localPosition.x - position.x - 0.5f) > stone.View.SupportHalfWidth) continue;
                stone.Rider = _player;
                destination = stone.Position;
                offset = stone.View.transform.position - GetWorldPosition(destination);
                return true;
            }
            return false;
        }

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
                _currentPatternFirstRow = boundary;
                while (_placedPatterns.Count < MaximumRenderedPatterns)
                    EnsurePatternsThrough(_nextPatternRow);
            }
            int firstRow = _placedPatterns[0].FirstRow;
            int contentFirstRow = Math.Max(0, playerRow - RowsBehind);
            int lastRow = _placedPatterns[_placedPatterns.Count - 1].LastRow;
            if (_loaded && firstRow == _firstRow && contentFirstRow == _contentFirstRow &&
                lastRow == _lastRow) return;
            UpdatePatternViews();
            _firstRow = firstRow;
            _contentFirstRow = contentFirstRow;
            _lastRow = lastRow;
            _loaded = true;

            var expiredCoins = new List<Vector2Int>();
            foreach (var coin in _coins)
                if (coin.Key.y < contentFirstRow || coin.Key.y > lastRow)
                    expiredCoins.Add(coin.Key);
            foreach (var position in expiredCoins)
                RemoveCoin(position);
            var expiredRewards = new List<Vector2Int>();
            foreach (var reward in _rewards)
                if (reward.Key.y < contentFirstRow || reward.Key.y > lastRow) expiredRewards.Add(reward.Key);
            foreach (var position in expiredRewards) RemoveReward(position);
            UpdateChasers();
            if (!HasValidRoutes())
                throw new InvalidOperationException("The configured map has an unreachable trigger or no safe forward route.");
            var reachable = GetReachableCells();
            for (int row = firstRow; row <= lastRow; row++)
            {
                if (row > _highestGeneratedRow)
                    GenerateRowContent(row, reachable);
            }

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
            _lavaRows.Clear();
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
            if (_coinSprite == null) _coinSprite = PlaceholderSprites.Coin();
            if (_rewardSprite == null) _rewardSprite = PlaceholderSprites.Reward();
            if (_steppingStonePrefab == null)
                _steppingStonePrefab = Resources.Load<SteppingStone>("MapObjects/SteppingStone");
            if (_steppingStonePrefab == null)
                throw new InvalidOperationException("A stepping-stone prefab is required.");
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
                    !_obstacles.ContainsKey(position) && !IsObstacleDestinationReserved(position) &&
                    !_coins.ContainsKey(position) && !_registeredCells.ContainsKey(position))
                {
                    _rewards.Add(position, _rewardPoints);
                    CreateCollectibleView(position, _rewardSprite);
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
            if (!CanStandOn(position) || _obstacles.ContainsKey(position) || _registeredCells.ContainsKey(position) || _coins.ContainsKey(position) || _rewards.ContainsKey(position) ||
                IsObstacleDestinationReserved(position) || !reachable.Contains(position))
                return false;
            _coins.Add(position, amount);
            CreateCollectibleView(position, _coinSprite);
            return true;
        }

        public int GetCoinAmount(Vector2Int position) => _coins.TryGetValue(position, out int amount) ? amount : 0;

        public int GetRewardPoints(Vector2Int position) => _rewards.TryGetValue(position, out int points) ? points : 0;

        public void RemoveReward(Vector2Int position)
        {
            if (!_rewards.Remove(position)) return;
            RemoveCollectibleView(position);
        }

        public void RemoveCoin(Vector2Int position)
        {
            if (!_coins.Remove(position)) return;
            RemoveCollectibleView(position);
        }

        public bool TrySetObstacle(Vector2Int position, TileBase tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            if (tile is DamageTile damageTile && damageTile.Damage <= 0)
                throw new ArgumentException("Damage tiles require positive damage.", nameof(tile));
            EnsureContentInitialized();
            if (!CanStandOn(position) || position == _player.Position || _coins.ContainsKey(position) || _rewards.ContainsKey(position) || _registeredCells.ContainsKey(position))
                return false;
            if (_obstacles.ContainsKey(position) ||
                IsObstacleDestinationReserved(position)) return false;
            var obstacle = CreateTileObstacle(tile, position, transform);
            _obstacles.Add(position, obstacle);
            if (HasValidRoutes()) return true;
            _obstacles.Remove(position);
            DestroyObject(obstacle.gameObject);
            return false;
        }

        public bool TryMoveObstacle(Vector2Int from, Vector2Int to)
        {
            if (!_loaded || (to - from).sqrMagnitude != 1 || !CanStandOn(to) ||
                _obstacles.ContainsKey(to) ||
                IsObstacleDestinationReserved(to) ||
                !_obstacles.TryGetValue(from, out var view)) return false;
            var moving = GetMovingObstacle(from);
            if (moving != null)
            {
                if (moving.IsMoving) return false;
                if (moving.Definition.movement == ObstacleMovement.Chase && _lavaRows.Contains(to.y)) return false;
                moving.FromWorld = view.transform.position;
                moving.Destination = to;
                moving.MoveStartTime = Time.time;
                moving.IsMoving = true;
                return true;
            }
            if (to == _player.Position || _coins.ContainsKey(to) || _rewards.ContainsKey(to) || _registeredCells.ContainsKey(to)) return false;
            _obstacles.Remove(from);
            _obstacles.Add(to, view);
            if (!HasValidRoutes())
            {
                _obstacles.Remove(to);
                _obstacles.Add(from, view);
                return false;
            }
            view.transform.position = GetWorldPosition(to);
            return true;
        }

        public bool TryMoveSteppingStone(Vector2Int from, Vector2Int to)
        {
            if (!_loaded || from.y != to.y || Mathf.Abs(from.x - to.x) != 1 ||
                to.x < -HalfWidth || to.x > HalfWidth || _obstacles.ContainsKey(to) ||
                IsObstacleDestinationReserved(to)) return false;
            var stone = _stoneViews.Find(candidate => candidate.Position == from);
            if (stone == null || stone.IsMoving || _registeredCells.ContainsKey(from) ||
                _stoneViews.Exists(candidate => candidate != stone &&
                    (candidate.Position == to || candidate.IsMoving && Vector3.Distance(candidate.FromWorld, GetWorldPosition(to)) < 0.01f)) ||
                _coins.ContainsKey(to) || _rewards.ContainsKey(to)) return false;
            stone.FromWorld = stone.View.transform.position;
            stone.Position = to;
            stone.MoveStartTime = Time.time;
            stone.IsMoving = true;
            if (_coins.Remove(from, out int amount)) _coins.Add(to, amount);
            if (_rewards.Remove(from, out int points)) _rewards.Add(to, points);
            if (_collectibleViews.Remove(from, out var collectible))
            {
                _collectibleViews.Add(to, collectible);
                stone.Collectible = collectible;
            }
            if (_player.Position == from)
            {
                stone.Rider = _player;
                _player.RideStone(to, stone.View.transform.position - GetWorldPosition(to));
            }
            return true;
        }

        public bool TryGetNextStep(Vector2Int from, Vector2Int target, out Vector2Int next)
        {
            next = from;
            var moving = GetMovingObstacle(from);
            bool playerPath = from == _player.Position;
            bool movingObstacle = moving != null && !playerPath;
            bool chaser = movingObstacle && moving.Definition.movement == ObstacleMovement.Chase;
            if (!_loaded || from == target || !CanStandOn(target) ||
                chaser && (_lavaRows.Contains(from.y) || _lavaRows.Contains(target.y)) ||
                !HasGround(from) || IsLethal(from)) return false;
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
                    if (!CanStandOn(destination) || playerPath && (GetContactDamage(destination) > 0 ||
                        IsObstacleDestinationReserved(destination) ||
                        destination.y < state.z - PlayerController.MaxBackwardSteps) ||
                        chaser && _lavaRows.Contains(destination.y) ||
                        movingObstacle && (IsObstacleDestinationReserved(destination) ||
                        destination != target && GetMovingObstacle(destination) != null)) continue;
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
                !CanMoveTo(position) || _obstacles.ContainsKey(position) || IsObstacleDestinationReserved(position) ||
                _coins.ContainsKey(position) || _rewards.ContainsKey(position) || _registeredCells.ContainsKey(position) ||
                _loaded && position.y < _firstRow ||
                _loaded && HasGround(position) && !GetReachableCells().Contains(position))
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
            }
        }

        private void UpdatePatternViews()
        {
            foreach (var pattern in _placedPatterns)
            {
                if (pattern.View != null) continue;
                var view = Instantiate(pattern.Template, transform);
                pattern.View = view;
                view.name = $"Pattern {pattern.FirstRow} - {pattern.Template.name}";
                view.transform.localPosition = new Vector3(0, pattern.FirstRow, 0);
                view.gameObject.SetActive(true);
                if (_spriteMaterial != null) view.Ground.GetComponent<TilemapRenderer>().sharedMaterial = _spriteMaterial;
                view.Ground.color = Color.white;
                for (int row = 0; row < view.Length; row++)
                    for (int x = -HalfWidth; x <= HalfWidth; x++)
                    {
                        var cell = new Vector3Int(x, row, 0);
                        view.Ground.SetTileFlags(cell, TileFlags.None);
                        view.Ground.SetColor(cell, pattern.Template.Ground.GetColor(cell) * pattern.Template.Ground.color);
                    }
                foreach (var source in view.GetComponentsInChildren<MapObstacle>(true))
                {
                    var local = view.GetCell(source.transform);
                    RegisterObstacle(source, local + new Vector2Int(0, pattern.FirstRow));
                }
                if (view.Structures != null)
                {
                    foreach (var local in view.Structures.cellBounds.allPositionsWithin)
                    {
                        var tile = view.Structures.GetTile(local);
                        if (tile == null) continue;
                        var position = new Vector2Int(local.x, local.y + pattern.FirstRow);
                        var obstacle = CreateTileObstacle(tile, position, view.transform);
                        obstacle.GetComponentInChildren<SpriteRenderer>().color *= view.Structures.color * view.Structures.GetColor(local);
                        RegisterObstacle(obstacle, position);
                    }
                    view.Structures.gameObject.SetActive(false);
                }
                foreach (var definition in view.MovingObstacles)
                {
                    var position = definition.position + new Vector2Int(0, pattern.FirstRow);
                    RegisterObstacle(CreateTileObstacle(definition.tile, position, view.transform, definition), position);
                }
                foreach (var lava in view.LavaRows)
                {
                    int row = pattern.FirstRow + lava.row;
                    for (int x = -HalfWidth; x <= HalfWidth; x++)
                    {
                        var cell = new Vector3Int(x, lava.row, 0);
                        view.Ground.SetTileFlags(cell, TileFlags.None);
                        view.Ground.SetColor(cell, view.LavaColor);
                    }
                    foreach (var definition in lava.GetStones())
                    {
                        var stoneView = Instantiate(definition.prefab != null ? definition.prefab : _steppingStonePrefab, view.transform);
                        var position = new Vector2Int(definition.column, row);
                        stoneView.transform.position = GetWorldPosition(position);
                        var renderer = stoneView.GetComponent<SpriteRenderer>();
                        if (_spriteMaterial != null) renderer.sharedMaterial = _spriteMaterial;
                        renderer.color *= view.SteppingStoneColor;
                        _stoneViews.Add(new MovingStone
                        {
                            Definition = definition, View = stoneView, Origin = position, Position = position,
                            NextMoveTime = Time.time, FromWorld = stoneView.transform.position
                        });
                    }
                }
            }
        }

        private MapObstacle CreateTileObstacle(TileBase tile, Vector2Int position, Transform parent, MovingObstacleDefinition movement = null)
        {
            var instance = new GameObject(tile.name, typeof(MapObstacle));
            instance.transform.SetParent(parent, false);
            instance.transform.position = GetWorldPosition(position);
            var obstacle = instance.GetComponent<MapObstacle>();
            obstacle.InitializeTile(tile, movement);
            return obstacle;
        }

        private void RegisterObstacle(MapObstacle view, Vector2Int position)
        {
            _obstacles.Add(position, view);
            foreach (var renderer in view.GetComponentsInChildren<SpriteRenderer>(true))
                if (_spriteMaterial != null) renderer.sharedMaterial = _spriteMaterial;
            if (view.Movement == ObstacleMovement.None) return;
            _movingObstacles.Add(new MovingObstacle
            {
                View = view, Definition = view.GetMovement(position), Origin = position, Position = position,
                FromWorld = view.transform.position, NextMoveTime = Time.time
            });
        }

        private void CreateCollectibleView(Vector2Int position, Sprite sprite)
        {
            var instance = new GameObject(sprite.name, typeof(SpriteRenderer));
            instance.transform.SetParent(transform, false);
            instance.transform.position = GetWorldPosition(position);
            var renderer = instance.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 2;
            if (_spriteMaterial != null) renderer.sharedMaterial = _spriteMaterial;
            _collectibleViews.Add(position, renderer);
        }

        private void RemoveCollectibleView(Vector2Int position)
        {
            if (!_collectibleViews.Remove(position, out var view)) return;
            DestroyObject(view.gameObject);
        }

        private static void DestroyObject(GameObject instance)
        {
            instance.SetActive(false);
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
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

        private bool IsRouteCell(Vector2Int position)
        {
            if (!CanMoveTo(position) || GetContactDamage(position) > 0 && GetMovingObstacle(position) == null) return false;
            var pattern = GetPattern(position.y);
            if (pattern == null) return true;
            foreach (var lava in pattern.Template.LavaRows)
            {
                if (lava.row != position.y - pattern.FirstRow) continue;
                foreach (var stone in _stoneViews)
                {
                    if (stone.Position.y != position.y) continue;
                    if (stone.Definition.movement == SteppingStoneMovement.Stationary)
                    {
                        if (position.x == stone.Position.x) return true;
                        continue;
                    }
                    int end = stone.Origin.x + stone.Definition.direction * stone.Definition.distance;
                    if (position.x >= Math.Min(stone.Origin.x, end) && position.x <= Math.Max(stone.Origin.x, end)) return true;
                }
                return false;
            }
            return true;
        }

        private HashSet<Vector2Int> GetReachableCells(bool planned = false)
        {
            var reachable = new HashSet<Vector2Int>();
            if (!_loaded || !CanMoveTo(_player.Position) || IsLethal(_player.Position) ||
                !HasGround(_player.Position)) return reachable;
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
                    if (destination.y < state.z - PlayerController.MaxBackwardSteps ||
                        !(planned ? HasGround(destination) && IsRouteCell(destination) : CanStandOn(destination))) continue;
                    var next = new Vector3Int(destination.x, destination.y, Math.Max(state.z, destination.y));
                    if (visited.Add(next)) queue.Enqueue(next);
                }
            }
            return reachable;
        }

        private bool HasValidRoutes()
        {
            if (!_loaded || !CanMoveTo(_player.Position) || IsLethal(_player.Position) ||
                !HasGround(_player.Position)) return false;
            int lastRow = _lastRow;
            // Adjacent rows stay connected so advancing cannot strand a collectible
            // behind a wall that would require two backward steps to go around.
            // Include the next row before it appears, since it may be a configured hazard.
            for (int row = _firstRow + 1; row <= lastRow + 1; row++)
                if (!AreRowsConnected(row)) return false;
            var reachable = GetReachableCells(true);
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
                if (!IsRouteCell(position)) continue;
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
                    if (destination.y < row - 1 || destination.y > row || !IsRouteCell(destination)) continue;
                    if (connected.Add(destination)) queue.Enqueue(destination);
                }
            }
            for (int y = row - 1; y <= row; y++)
                for (int x = -HalfWidth; x <= HalfWidth; x++)
                {
                    var position = new Vector2Int(x, y);
                    if (IsRouteCell(position) && !connected.Contains(position)) return false;
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
                            if (lava.row == start.y && !lava.HasColumn(start.x)) lethal = true;
                        if (pattern.HasObstacle(start) || lethal) continue;
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
                    _lavaRows.Add(placement.FirstRow + lava.row);
                int nextThemeRow = -1;
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
                    if (!string.IsNullOrEmpty(trigger.nextThemeId) && trigger.row >= nextThemeRow)
                    {
                        _generationThemeId = trigger.nextThemeId;
                        nextThemeRow = trigger.row;
                    }
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
            _generationThemeId = themeId;
        }

        private void RemovePattern(int index)
        {
            var pattern = _placedPatterns[index];
            var removed = new List<Vector2Int>();
            foreach (var pair in _obstacles)
            {
                var moving = GetMovingObstacle(pair.Key);
                bool belongsToPattern = pair.Value.transform.IsChildOf(pattern.View.transform);
                bool onRemovedFloor = pair.Key.y >= pattern.FirstRow && pair.Key.y <= pattern.LastRow;
                if (!belongsToPattern && !onRemovedFloor) continue;
                bool preserve = moving != null && moving.Definition.movement == ObstacleMovement.Chase &&
                    moving.HasStartedChasing && pattern.FirstRow <= _currentPatternFirstRow &&
                    (!onRemovedFloor || moving.IsMoving &&
                        (moving.Destination.y < pattern.FirstRow || moving.Destination.y > pattern.LastRow));
                if (preserve)
                {
                    moving.View.transform.SetParent(transform, true);
                    continue;
                }
                removed.Add(pair.Key);
                if (moving != null) _movingObstacles.Remove(moving);
                if (!belongsToPattern) DestroyObject(pair.Value.gameObject);
            }
            foreach (var position in removed) _obstacles.Remove(position);
            _stoneViews.RemoveAll(stone => stone.Origin.y >= pattern.FirstRow && stone.Origin.y <= pattern.LastRow);
            ReleasePatternView(pattern);
            foreach (var lava in pattern.Template.LavaRows) _lavaRows.Remove(pattern.FirstRow + lava.row);
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
            AdvanceSteppingStones(Time.time);
            DamagePlayerAt(_player.Position);
        }

        private void AdvanceMovingObstacles(float now)
        {
            UpdateChasers();
            foreach (var obstacle in _movingObstacles)
            {
                if (GameStateManager.Instance.State != GameState.Playing) return;
                if (obstacle.IsMoving)
                {
                    float progress = Mathf.Clamp01((now - obstacle.MoveStartTime) / obstacle.Definition.stepInterval);
                    obstacle.View.transform.position = Vector3.Lerp(obstacle.FromWorld, GetWorldPosition(obstacle.Destination), progress);
                    if (progress < 1f) continue;
                    _obstacles.Remove(obstacle.Position);
                    _obstacles.Add(obstacle.Destination, obstacle.View);
                    obstacle.Position = obstacle.Destination;
                    obstacle.IsMoving = false;
                    DamagePlayerAt(obstacle.Position);
                    if (GameStateManager.Instance.State != GameState.Playing) return;
                }
                if (now < obstacle.NextMoveTime) continue;
                var definition = obstacle.Definition;
                obstacle.NextMoveTime = now + definition.stepInterval;
                Vector2Int next;
                if (definition.movement == ObstacleMovement.Chase)
                {
                    if (!obstacle.HasStartedChasing || !TryGetNextStep(obstacle.Position, _player.Position, out next))
                        continue;
                }
                else
                {
                    var offset = obstacle.Position - obstacle.Origin;
                    int step = offset.x * definition.direction.x + offset.y * definition.direction.y;
                    if (step == definition.distance) obstacle.Direction = -1;
                    else if (step == 0) obstacle.Direction = 1;
                    next = obstacle.Position + definition.direction * obstacle.Direction;
                }
                if (TryMoveObstacle(obstacle.Position, next)) obstacle.MoveStartTime = now;
                else if (definition.movement == ObstacleMovement.Patrol) obstacle.Direction = -obstacle.Direction;
            }
            DamagePlayerAt(_player.Position);
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
                    _obstacles.Remove(obstacle.Position);
                    DestroyObject(obstacle.View.gameObject);
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
            public MapObstacle View;
            public MovingObstacleDefinition Definition;
            public Vector3 FromWorld;
            public float MoveStartTime;
            public bool IsMoving;
            public Vector2Int Origin;
            public Vector2Int Position;
            public Vector2Int Destination;
            public int Direction = 1;
            public float NextMoveTime;
            public bool HasStartedChasing;
        }

        private void AdvanceSteppingStones(float now)
        {
            for (int i = 0; i < _stoneViews.Count; i++)
            {
                if (GameStateManager.Instance.State != GameState.Playing) return;
                var stone = _stoneViews[i];
                if (stone.IsMoving)
                {
                    float progress = Mathf.Clamp01((now - stone.MoveStartTime) * stone.Definition.speed);
                    stone.View.transform.position = Vector3.Lerp(stone.FromWorld, GetWorldPosition(stone.Position), progress);
                    if (stone.Collectible != null) stone.Collectible.transform.position = stone.View.transform.position;
                    if (stone.Rider != null)
                    {
                        if (stone.Rider.Position == stone.Position)
                            stone.Rider.SetPlatformOffset(stone.View.transform.position - GetWorldPosition(stone.Position));
                        else stone.Rider = null;
                    }
                    if (progress < 1f) continue;
                    stone.IsMoving = false;
                    stone.Rider = null;
                }
                if (stone.Definition.movement == SteppingStoneMovement.Stationary || now < stone.NextMoveTime) continue;
                int end = stone.Origin.x + stone.Definition.direction * stone.Definition.distance;
                if (stone.Position.x == end) stone.Direction = -1;
                else if (stone.Position.x == stone.Origin.x) stone.Direction = 1;
                var next = stone.Position + new Vector2Int(stone.Definition.direction * stone.Direction, 0);
                stone.NextMoveTime = now + 1f / stone.Definition.speed;
                TryMoveSteppingStone(stone.Position, next);
            }
        }

        private sealed class MovingStone
        {
            public SteppingStoneDefinition Definition;
            public SteppingStone View;
            public Vector2Int Origin;
            public Vector2Int Position;
            public Vector3 FromWorld;
            public int Direction = 1;
            public float NextMoveTime;
            public float MoveStartTime;
            public bool IsMoving;
            public PlayerController Rider;
            public SpriteRenderer Collectible;
        }

        private void OnDestroy()
        {
            foreach (var pattern in _placedPatterns) ReleasePatternView(pattern);
            foreach (var view in _collectibleViews.Values)
                if (view != null) DestroyObject(view.gameObject);
            foreach (var obstacle in _movingObstacles)
                if (obstacle.View != null) DestroyObject(obstacle.View.gameObject);
            if (Instance == this) Instance = null;
        }
    }

    [Serializable]
    public sealed class HazardRowDefinition
    {
        public int row;
        [HideInInspector] public int[] steppingStoneColumns = { 0 };
        public SteppingStoneDefinition[] steppingStones = Array.Empty<SteppingStoneDefinition>();

        public IEnumerable<SteppingStoneDefinition> GetStones()
        {
            if (steppingStones.Length > 0)
            {
                foreach (var stone in steppingStones) yield return stone;
            }
            else
                foreach (int column in steppingStoneColumns) yield return new SteppingStoneDefinition { column = column };
        }

        public bool HasColumn(int column)
        {
            foreach (var stone in GetStones())
                if (stone.column == column) return true;
            return false;
        }
    }

    public enum SteppingStoneMovement { Stationary, PingPong }

    [Serializable]
    public sealed class SteppingStoneDefinition
    {
        public int column;
        public SteppingStone prefab;
        public SteppingStoneMovement movement;
        [Min(0.01f)] public float speed = 1f;
        [Range(-1, 1)] public int direction = 1;
        [Min(1)] public int distance = 2;
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
