using UnityEngine;

namespace CrossTheBoard
{
    [RequireComponent(typeof(Camera))]
    public sealed class GameplayCamera : MonoBehaviour
    {
        [SerializeField] private PlayerController _player;
        [SerializeField] private MapManager _map;
        [SerializeField] private RectTransform _bottomArea;
        private Camera _camera;
        private const float MinimumViewportHeight = 0.05f;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = MapManager.VisibleRows * 0.5f;
        }

        private void LateUpdate()
        {
            float targetAspect = (float)MapManager.Width / MapManager.VisibleRows;
            float screenAspect = _camera.targetTexture != null
                ? (float)_camera.targetTexture.width / _camera.targetTexture.height
                : (float)Screen.width / Screen.height;
            float bottomSpace = 0f;
            if (_bottomArea != null)
            {
                var canvasRect = (RectTransform)_bottomArea.parent;
                bottomSpace = _bottomArea.anchorMax.y;
                if (_bottomArea.offsetMax.y != 0f && canvasRect.rect.height > 0f)
                    bottomSpace += _bottomArea.offsetMax.y / canvasRect.rect.height;
                bottomSpace = Mathf.Clamp(bottomSpace, 0f, 1f - MinimumViewportHeight);
            }
            float height = Mathf.Min(1f - bottomSpace, screenAspect / targetAspect);
            float width = height * targetAspect / screenAspect;
            _camera.rect = new Rect((1f - width) * 0.5f, 1f - height, width, height);
            if (GameStateManager.Instance != null && GameStateManager.Instance.State == GameState.GameOver)
                return;
            Vector3 center = _map.GetWorldPosition(new Vector2Int(0, _player.FurthestRow));
            center.y += (MapManager.RowsAhead - MapManager.RowsBehind) * 0.5f;
            center.z = transform.position.z;
            transform.position = center;
        }
    }
}
