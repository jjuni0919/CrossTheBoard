using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CrossTheBoard
{
    [RequireComponent(typeof(Grid))]
    public sealed class MapPattern : MonoBehaviour
    {
        public const int MinimumLength = 15;
        public const int MaximumLength = 50;

        [SerializeField, Range(MinimumLength, MaximumLength)] private int _length = MinimumLength;
        [SerializeField] private string _themeId = "meadow";
        [SerializeField] private Tilemap _ground;
        [SerializeField, HideInInspector] private Tilemap _structures;
        [SerializeField] private HazardRowDefinition[] _lavaRows = Array.Empty<HazardRowDefinition>();
        [SerializeField, HideInInspector] private MovingObstacleDefinition[] _movingObstacles = Array.Empty<MovingObstacleDefinition>();
        [SerializeField] private RowTriggerDefinition[] _rowTriggers = Array.Empty<RowTriggerDefinition>();
        [SerializeField] private Color _lavaColor = new(0.95f, 0.25f, 0.08f);
        [SerializeField] private Color _steppingStoneColor = new(0.84f, 0.81f, 0.65f);

        public int Length => _length;
        public string ThemeId => _themeId;
        public Tilemap Ground => _ground;
        public Tilemap Structures => _structures;
        public IReadOnlyList<HazardRowDefinition> LavaRows => _lavaRows;
        public IReadOnlyList<MovingObstacleDefinition> MovingObstacles => _movingObstacles;
        public IReadOnlyList<RowTriggerDefinition> RowTriggers => _rowTriggers;
        public Color LavaColor => _lavaColor;
        public Color SteppingStoneColor => _steppingStoneColor;

        public TileBase GetObstacle(Vector2Int position)
        {
            var tile = _structures != null ? _structures.GetTile(new Vector3Int(position.x, position.y, 0)) : null;
            if (tile != null) return tile;
            foreach (var obstacle in _movingObstacles)
                if (obstacle.position == position) return obstacle.tile;
            foreach (var obstacle in GetComponentsInChildren<MapObstacle>(true))
                if (GetCell(obstacle.transform) == position) return obstacle.Tile;
            return null;
        }

        public Vector2Int GetCell(Transform obstacle)
        {
            var local = transform.InverseTransformPoint(obstacle.position) - new Vector3(0.5f, 0.5f, 0);
            return new Vector2Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.y));
        }

        public bool HasObstacle(Vector2Int position)
        {
            if (GetObstacle(position) != null) return true;
            foreach (var obstacle in GetComponentsInChildren<MapObstacle>(true))
                if (GetCell(obstacle.transform) == position) return true;
            return false;
        }

        [ContextMenu("Validate Pattern")]
        public void Validate()
        {
            if (_length < MinimumLength || _length > MaximumLength || string.IsNullOrWhiteSpace(_themeId) ||
                _ground == null || _ground.GetComponent<TilemapRenderer>() == null ||
                _lavaRows == null || _movingObstacles == null || _rowTriggers == null)
                throw new InvalidOperationException($"Pattern '{name}' requires a theme, {MinimumLength}–{MaximumLength} rows and a ground Tilemap with a renderer.");
            var grid = GetComponent<Grid>();
            if (grid.cellSize != Vector3.one || grid.cellGap != Vector3.zero || grid.cellLayout != GridLayout.CellLayout.Rectangle ||
                _ground.transform.parent != transform ||
                _ground.transform.localPosition != Vector3.zero || _ground.tileAnchor != new Vector3(0.5f, 0.5f, 0) ||
                _ground.transform.localScale != Vector3.one || _ground.transform.localRotation != Quaternion.identity)
                throw new InvalidOperationException($"Pattern '{name}' must use an untransformed unit grid.");
            foreach (var position in _ground.cellBounds.allPositionsWithin)
                if (_ground.HasTile(position) && !Contains(position))
                    throw new InvalidOperationException($"Ground in '{name}' is outside x=-4..4, y=0..{_length - 1}.");
            var blocked = new HashSet<Vector2Int>();
            if (_structures != null)
            {
                foreach (var position in _structures.cellBounds.allPositionsWithin)
                {
                    if (!_structures.HasTile(position)) continue;
                    if (!Contains(position))
                        throw new InvalidOperationException($"Obstacle in '{name}' is outside the pattern.");
                    if (_structures.GetTile(position) is DamageTile damageTile && damageTile.Damage <= 0)
                        throw new InvalidOperationException($"Damage tiles in '{name}' require positive damage.");
                    blocked.Add(new Vector2Int(position.x, position.y));
                }
            }
            var movers = new List<MovingObstacleDefinition>(_movingObstacles);
            foreach (var obstacle in GetComponentsInChildren<MapObstacle>(true))
            {
                obstacle.Validate();
                var cell = GetCell(obstacle.transform);
                var local = transform.InverseTransformPoint(obstacle.transform.position);
                if (!Contains((Vector3Int)cell) || (local - new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0)).sqrMagnitude > 0.0001f ||
                    !blocked.Add(cell))
                    throw new InvalidOperationException($"Obstacle '{obstacle.name}' in '{name}' must occupy a unique cell centre.");
                if (obstacle.Movement != ObstacleMovement.None)
                    movers.Add(obstacle.GetMovement(cell));
            }
            foreach (var obstacle in _movingObstacles)
            {
                if (obstacle == null || obstacle.tile is not DamageTile damageTile || damageTile.Damage <= 0 || !Contains((Vector3Int)obstacle.position) ||
                    obstacle.movement != ObstacleMovement.Patrol && obstacle.movement != ObstacleMovement.Chase ||
                    float.IsNaN(obstacle.stepInterval) || float.IsInfinity(obstacle.stepInterval) || obstacle.stepInterval <= 0f)
                    throw new InvalidOperationException($"Invalid moving obstacle in '{name}' at {obstacle?.position}: " +
                        $"tile '{obstacle?.tile?.name}' ({obstacle?.tile?.GetType().Name ?? "missing"}), movement {obstacle?.movement}, interval {obstacle?.stepInterval}. " +
                        "A positive-damage DamageTile and valid position/movement/interval are required.");
                if (!blocked.Add(obstacle.position))
                    throw new InvalidOperationException($"Moving obstacle in '{name}' overlaps another obstacle at {obstacle.position}.");
                if (obstacle.movement == ObstacleMovement.Patrol &&
                    (obstacle.direction != Vector2Int.left && obstacle.direction != Vector2Int.right ||
                        obstacle.distance < 1 || obstacle.distance > MaximumLength))
                    throw new InvalidOperationException($"Rolling rocks in '{name}' require a horizontal direction and a positive distance.");
            }
            var lava = new Dictionary<int, HashSet<int>>();
            foreach (var row in _lavaRows)
            {
                if (row == null || row.row <= 0 || row.row >= _length - 1 || row.steppingStones == null || row.steppingStoneColumns == null || lava.ContainsKey(row.row))
                    throw new InvalidOperationException($"Lava in '{name}' requires unique interior rows and stepping stones.");
                foreach (var obstacle in movers)
                    if (obstacle.movement == ObstacleMovement.Chase && obstacle.position.y == row.row)
                        throw new InvalidOperationException($"Monster in '{name}' cannot start on a stepping-stone row.");
                var columns = new HashSet<int>();
                var routes = new HashSet<int>();
                foreach (var stone in row.GetStones())
                {
                    if (stone == null || !Enum.IsDefined(typeof(SteppingStoneMovement), stone.movement) ||
                        !float.IsFinite(stone.speed) || stone.speed <= 0f ||
                        stone.column < -MapManager.HalfWidth || stone.column > MapManager.HalfWidth || !columns.Add(stone.column) ||
                        stone.movement != SteppingStoneMovement.Stationary &&
                        (Mathf.Abs(stone.direction) != 1 || stone.distance < 1 ||
                        Mathf.Abs(stone.column + stone.direction * stone.distance) > MapManager.HalfWidth))
                        throw new InvalidOperationException($"Invalid stepping stone in '{name}' at row {row.row}.");
                    int end = stone.movement == SteppingStoneMovement.Stationary ? stone.column : stone.column + stone.direction * stone.distance;
                    for (int x = Math.Min(stone.column, end); x <= Math.Max(stone.column, end); x++)
                        if (!routes.Add(x) || blocked.Contains(new Vector2Int(x, row.row)))
                            throw new InvalidOperationException($"Stepping-stone route in '{name}' overlaps another stone or obstacle at ({x}, {row.row}).");
                }
                if (columns.Count == 0) throw new InvalidOperationException($"Lava row {row.row} in '{name}' requires stepping stones.");
                lava.Add(row.row, columns);
            }
            bool Safe(Vector2Int position) => !blocked.Contains(position) &&
                (!lava.TryGetValue(position.y, out var stones) || stones.Contains(position.x));
            for (int row = 0; row < _length; row++)
                for (int x = -MapManager.HalfWidth; x <= MapManager.HalfWidth; x++)
                {
                    var position = new Vector2Int(x, row);
                    if (!_ground.HasTile((Vector3Int)position) || blocked.Contains(position) &&
                        (row == 0 || row == _length - 1 || lava.TryGetValue(row, out var stones) && !stones.Contains(x)))
                        throw new InvalidOperationException($"Pattern '{name}' needs a full floor, clear boundary rows and no obstacles in lava.");
                }
            foreach (var obstacle in movers)
            {
                if (obstacle.movement != ObstacleMovement.Patrol) continue;
                for (int step = 1; step <= obstacle.distance; step++)
                {
                    var position = obstacle.position + obstacle.direction * step;
                    if (!Contains((Vector3Int)position) || !Safe(position))
                        throw new InvalidOperationException($"Patrol route in '{name}' leaves the map or crosses an occupied/lethal cell.");
                }
            }
            var ids = new HashSet<string>();
            foreach (var trigger in _rowTriggers)
                if (trigger == null || string.IsNullOrWhiteSpace(trigger.id) || trigger.row < 0 || trigger.row >= _length || !ids.Add(trigger.id))
                    throw new InvalidOperationException($"Invalid or duplicate row trigger in '{name}'.");
            // Every pair is connected, not just the entrance and exit: the player cannot backtrack two rows.
            Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            for (int row = 1; row < _length; row++)
            {
                var cells = new HashSet<Vector2Int>();
                bool previousRowOpen = false, currentRowOpen = false;
                for (int y = row - 1; y <= row; y++)
                    for (int x = -MapManager.HalfWidth; x <= MapManager.HalfWidth; x++)
                        if (Safe(new Vector2Int(x, y)))
                        {
                            cells.Add(new Vector2Int(x, y));
                            if (y == row) currentRowOpen = true;
                            else previousRowOpen = true;
                        }
                if (!previousRowOpen || !currentRowOpen)
                    throw new InvalidOperationException($"Pattern '{name}' has no safe forward route around row {row}.");
                var queue = new Queue<Vector2Int>();
                foreach (var position in cells) { queue.Enqueue(position); break; }
                var visited = new HashSet<Vector2Int>();
                while (queue.Count > 0)
                {
                    var position = queue.Dequeue();
                    if (!visited.Add(position)) continue;
                    foreach (var direction in directions)
                        if (cells.Contains(position + direction)) queue.Enqueue(position + direction);
                }
                if (visited.Count != cells.Count)
                    throw new InvalidOperationException($"Pattern '{name}' has disconnected cells around row {row}.");
            }
        }

        private bool Contains(Vector3Int position) => position.z == 0 && position.x >= -MapManager.HalfWidth &&
            position.x <= MapManager.HalfWidth && position.y >= 0 && position.y < _length;

        private void OnDrawGizmosSelected()
        {
            if (_ground == null || _lavaRows == null || _movingObstacles == null) return;
            foreach (var row in _lavaRows)
            {
                if (row?.steppingStoneColumns == null) continue;
                for (int x = -MapManager.HalfWidth; x <= MapManager.HalfWidth; x++)
                {
                    Gizmos.color = row.HasColumn(x) ? _steppingStoneColor : _lavaColor;
                    Gizmos.DrawCube(_ground.GetCellCenterWorld(new Vector3Int(x, row.row, 0)), new Vector3(0.85f, 0.85f, 0.01f));
                }
            }
            foreach (var obstacle in _movingObstacles)
            {
                if (obstacle == null) continue;
                Gizmos.color = obstacle.movement == ObstacleMovement.Chase ? Color.magenta : Color.cyan;
                Vector3 center = _ground.GetCellCenterWorld((Vector3Int)obstacle.position);
                Gizmos.DrawWireCube(center, new Vector3(0.8f, 0.8f, 0.01f));
                if (obstacle.movement == ObstacleMovement.Patrol)
                    Gizmos.DrawLine(center, _ground.GetCellCenterWorld((Vector3Int)(obstacle.position + obstacle.direction * obstacle.distance)));
            }
        }
    }

    public enum ObstacleMovement { Patrol, Chase, None }

    [Serializable]
    public sealed class MovingObstacleDefinition
    {
        public Vector2Int position;
        public TileBase tile;
        public ObstacleMovement movement;
        public Vector2Int direction = Vector2Int.right;
        [Min(1)] public int distance = 2;
        [Min(0.1f)] public float stepInterval = 0.75f;
    }
}
