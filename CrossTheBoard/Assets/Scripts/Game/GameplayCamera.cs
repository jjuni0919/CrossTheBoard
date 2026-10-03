using UnityEngine;

namespace CrossTheBoard
{
    [RequireComponent(typeof(Camera))]
    public sealed class GameplayCamera : MonoBehaviour
    {
        [SerializeField] private PlayerController _player;
        [SerializeField] private MapManager _map;
        private Camera _camera;

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
            if (screenAspect < targetAspect)
            {
                float height = screenAspect / targetAspect;
                _camera.rect = new Rect(0f, (1f - height) * 0.5f, 1f, height);
            }
            else
            {
                float width = targetAspect / screenAspect;
                _camera.rect = new Rect((1f - width) * 0.5f, 0f, width, 1f);
            }
            Vector3 center = _map.GetWorldPosition(new Vector2Int(0, _player.FurthestRow));
            center.y += (MapManager.RowsAhead - MapManager.RowsBehind) * 0.5f;
            center.z = transform.position.z;
            transform.position = center;
        }
    }
}
