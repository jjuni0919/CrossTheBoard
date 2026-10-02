using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrossTheBoard
{
    public sealed class GameplayController : MonoBehaviour
    {
        [SerializeField] private MapManager _map;
        [SerializeField] private PlayerController _player;
        [SerializeField] private string[] _moveAchievementIds = Array.Empty<string>();

        private void Start()
        {
            var ids = new HashSet<string>();
            foreach (string id in _moveAchievementIds)
            {
                if (!ids.Add(id))
                    throw new InvalidOperationException($"Movement achievement '{id}' is duplicated.");
                bool registered = false;
                foreach (var definition in AchievementTracker.Instance.Database.Achievements)
                {
                    if (definition.id == id)
                    {
                        registered = true;
                        break;
                    }
                }
                if (!registered)
                    throw new InvalidOperationException($"Movement achievement '{id}' is not registered.");
            }
            _player.Initialize(_map);
            _map.LoadRows(_player.Position.y);
            _player.Moved += OnPlayerMoved;
            GameStateManager.Instance.SetState(GameState.Playing);
        }

        private void OnPlayerMoved(Vector2Int previous, Vector2Int position)
        {
            _map.LoadRows(position.y);
            foreach (string id in _moveAchievementIds)
            {
                if (!AchievementTracker.Instance.AddProgress(id))
                    Debug.LogWarning($"Movement achievement '{id}' could not be saved.", this);
            }
        }

        private void OnDestroy()
        {
            if (_player != null)
                _player.Moved -= OnPlayerMoved;
        }
    }
}
