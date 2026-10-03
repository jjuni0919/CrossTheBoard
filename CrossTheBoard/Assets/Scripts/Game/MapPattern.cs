using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CrossTheBoard
{
    [RequireComponent(typeof(Grid))]
    public sealed class MapPattern : MonoBehaviour
    {
        public const int MinimumLength = 6;
        public const int MaximumLength = 30;

        [SerializeField, Range(MinimumLength, MaximumLength)] private int _length = MinimumLength;
        [SerializeField] private string _themeId = "meadow";
        [SerializeField] private Tilemap _ground;
        [SerializeField] private Tilemap _structures;
        [SerializeField] private HazardRowDefinition[] _lavaRows = Array.Empty<HazardRowDefinition>();
        [SerializeField] private MovingObstacleDefinition[] _movingObstacles = Array.Empty<MovingObstacleDefinition>();
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
            var tile = _structures.GetTile(new Vector3Int(position.x, position.y, 0));
            if (tile != null) return tile;
            foreach (var obstacle in _movingObstacles)
                if (obstacle.position == position) return obstacle.tile;
            return null;
        }

        [ContextMenu("Validate Pattern")]
        public void Validate()
        {
            if (_length < MinimumLength || _length > MaximumLength || string.IsNullOrWhiteSpace(_themeId) ||
                _ground == null || _structures == null || _ground == _structures ||
                _lavaRows == null || _movingObstacles == null || _rowTriggers == null)
                throw new InvalidOperationException($"Pattern '{name}' requires a theme, 6–30 rows and separate ground/structure Tilemaps.");
            var grid = GetComponent<Grid>();
            if (grid.cellSize != Vector3.one || grid.cellGap != Vector3.zero || grid.cellLayout != GridLayout.CellLayout.Rectangle ||
                _ground.transform.parent != transform || _structures.transform.parent != transform ||
                _ground.transform.localPosition != Vector3.zero || _structures.transform.localPosition != Vector3.zero ||
                _ground.transform.localScale != Vector3.one || _structures.transform.localScale != Vector3.one ||
                _ground.transform.localRotation != Quaternion.identity || _structures.transform.localRotation != Quaternion.identity)
                throw new InvalidOperationException($"Pattern '{name}' must use an untransformed unit grid.");
            foreach (var position in _ground.cellBounds.allPositionsWithin)
                if (_ground.HasTile(position) && !Contains(position))
                    throw new InvalidOperationException($"Ground in '{name}' is outside x=-4..4, y=0..{_length - 1}.");
            var blocked = new HashSet<Vector2Int>();
            foreach (var position in _structures.cellBounds.allPositionsWithin)
            {
                if (!_structures.HasTile(position)) continue;
                if (!Contains(position))
                    throw new InvalidOperationException($"Obstacle in '{name}' is outside the pattern.");
                blocked.Add(new Vector2Int(position.x, position.y));
            }
            foreach (var obstacle in _movingObstacles)
            {
                if (obstacle == null || obstacle.tile == null || !Contains((Vector3Int)obstacle.position) ||
                    !Enum.IsDefined(typeof(ObstacleMovement), obstacle.movement) ||
                    float.IsNaN(obstacle.stepInterval) || float.IsInfinity(obstacle.stepInterval) || obstacle.stepInterval <= 0f ||
                    !blocked.Add(obstacle.position))
                    throw new InvalidOperationException($"Invalid or overlapping moving obstacle in '{name}'.");
                if (obstacle.movement == ObstacleMovement.Patrol &&
                    (obstacle.direction != Vector2Int.up && obstacle.direction != Vector2Int.down &&
                        obstacle.direction != Vector2Int.left && obstacle.direction != Vector2Int.right ||
                        obstacle.distance < 1 || obstacle.distance > MaximumLength))
                    throw new InvalidOperationException($"Patrol in '{name}' requires a cardinal direction and a positive distance.");
            }
            var lava = new Dictionary<int, HashSet<int>>();
            foreach (var row in _lavaRows)
            {
                if (row == null || row.row <= 0 || row.row >= _length - 1 || row.steppingStoneColumns == null ||
                    row.steppingStoneColumns.Length == 0 || lava.ContainsKey(row.row))
                    throw new InvalidOperationException($"Lava in '{name}' requires unique interior rows and stepping stones.");
                var columns = new HashSet<int>();
                foreach (int x in row.steppingStoneColumns)
                    if (x < -MapManager.HalfWidth || x > MapManager.HalfWidth || !columns.Add(x))
                        throw new InvalidOperationException($"Invalid stepping stone in '{name}'.");
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
            foreach (var obstacle in _movingObstacles)
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
                for (int y = row - 1; y <= row; y++)
                    for (int x = -MapManager.HalfWidth; x <= MapManager.HalfWidth; x++)
                        if (Safe(new Vector2Int(x, y))) cells.Add(new Vector2Int(x, y));
                var queue = new Queue<Vector2Int>();
                foreach (var position in cells) { queue.Enqueue(position); break; }
                if (queue.Count == 0)
                    throw new InvalidOperationException($"Pattern '{name}' has no forward route at row {row}.");
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
                    Gizmos.color = Array.IndexOf(row.steppingStoneColumns, x) >= 0 ? _steppingStoneColor : _lavaColor;
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

    public enum ObstacleMovement { Patrol, Chase }

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
