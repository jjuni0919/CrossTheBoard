using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CrossTheBoard
{
    public sealed class MapManager : MonoBehaviour
    {
        public const int Width = 9;
        public const int RowsAhead = 13;
        public const int RowsBehind = 1;
        public const int VisibleRows = RowsAhead + RowsBehind + 1;
        public const int HalfWidth = Width / 2;

        [SerializeField] private Tilemap _ground;
        [SerializeField] private Tilemap _structures;
        [SerializeField] private TileBase _groundTile;
        public static MapManager Instance { get; private set; }
        private int _firstRow;
        private bool _loaded;

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
        }

        private void SetGroundRow(int row, TileBase tile)
        {
            for (int column = -HalfWidth; column <= HalfWidth; column++)
                _ground.SetTile(new Vector3Int(column, row, 0), tile);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
