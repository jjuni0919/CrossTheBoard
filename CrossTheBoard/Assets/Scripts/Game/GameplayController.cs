using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CrossTheBoard
{
    public sealed class GameplayController : MonoBehaviour
    {
        public const int PointsPerForwardCell = 100;
        public const float GameOverDisplaySeconds = 3f;

        [SerializeField] private MapManager _map;
        [SerializeField] private PlayerController _player;
        [SerializeField] private string[] _moveAchievementIds = Array.Empty<string>();
        [SerializeField, Min(0)] private int _pointsPerCoin = 10;
        [SerializeField] private string _mainMenuScene = "MainMenuScene";

        public int Score { get; private set; }
        public int ForwardScore { get; private set; }
        public int GoldScore { get; private set; }
        public int ItemScore { get; private set; }
        public string ActiveCharacterId { get; private set; }
        public int CollectedCoins { get; private set; }
        public PlayerController Player => _player;
        public event Action<int> ScoreChanged;
        private int _scoredRow;
        private bool _started;
        private int _startRow;
        private int _savedForwardCells;
        private bool _finished;
        private GameStateManager _state;

        private void Start()
        {
            if (_pointsPerCoin < 0)
                throw new InvalidOperationException("Coin bonus points cannot be negative.");
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
            _scoredRow = _player.FurthestRow;
            _startRow = _player.FurthestRow;
            Score = ForwardScore = GoldScore = ItemScore = 0;
            CollectedCoins = _savedForwardCells = 0;
            var save = SaveManager.Instance;
            ActiveCharacterId = save != null ? save.Data.selectedCharacterId : CharacterCatalog.StarterId;
            _player.SetCharacter(CharacterCatalog.GetSprite(ActiveCharacterId));
            _map.LoadRows(_player.FurthestRow);
            _player.Moved += OnPlayerMoved;
            _state = GameStateManager.Instance;
            _state.StateChanged += OnStateChanged;
            _started = true;
            _state.SetState(GameState.Playing);
            ScoreChanged?.Invoke(Score);
            _map.NotifyPlayerEntered(_player.Position);
            if (Application.isPlaying) GameplayHud.Create(this);
        }

        private void OnPlayerMoved(Vector2Int previous, Vector2Int position)
        {
            if (_map.IsLethal(position))
            {
                _player.Die();
                return;
            }
            _map.DamagePlayerAt(position);
            if (GameStateManager.Instance.State != GameState.Playing) return;
            // The camera, loaded map and score share the same forward-only frontier.
            // Returning to an already visited row never awards points again.
            _map.LoadRows(_player.FurthestRow);
            int newRows = _player.FurthestRow - _scoredRow;
            if (newRows > 0)
            {
                int points = checked(newRows * PointsPerForwardCell);
                int total = checked(Score + points);
                ForwardScore = checked(ForwardScore + points);
                Score = total;
                _scoredRow = _player.FurthestRow;
                ScoreChanged?.Invoke(Score);
            }
            PersistProgressAndCollect(position);
            int rewardPoints = _map.GetRewardPoints(position);
            if (rewardPoints > 0)
            {
                int total = checked(Score + rewardPoints);
                int itemScore = checked(ItemScore + rewardPoints);
                _map.RemoveReward(position);
                ItemScore = itemScore;
                Score = total;
                ScoreChanged?.Invoke(Score);
            }
            _map.NotifyPlayerEntered(position);
            foreach (string id in _moveAchievementIds)
            {
                if (!AchievementTracker.Instance.AddProgress(id))
                    Debug.LogWarning($"Movement achievement '{id}' could not be saved.", this);
            }
        }

        private void PersistProgressAndCollect(Vector2Int position)
        {
            int amount = _map.GetCoinAmount(position);
            int distance = _player.FurthestRow - _startRow;
            int unsavedCells = distance - _savedForwardCells;
            if (amount == 0 && unsavedCells == 0) return;
            var save = SaveManager.Instance;
            if (save == null) return;
            int collectedCoins = checked(CollectedCoins + amount);
            int coinPoints = checked(amount * _pointsPerCoin);
            int goldScore = checked(GoldScore + coinPoints);
            int score = checked(Score + coinPoints);
            if (!save.TryUpdate(data =>
            {
                data.coins = checked(data.coins + amount);
                data.totalCoinsCollected = checked(data.totalCoinsCollected + amount);
                data.totalForwardCells = checked(data.totalForwardCells + unsavedCells);
                data.bestDistance = Math.Max(data.bestDistance, distance);
            }))
            {
                Debug.LogWarning("코인과 진행도를 저장하지 못했습니다. 코인은 수집되지 않았습니다.", this);
                return;
            }
            _savedForwardCells = distance;
            if (amount > 0)
            {
                _map.RemoveCoin(position);
                CollectedCoins = collectedCoins;
                GoldScore = goldScore;
                Score = score;
                if (coinPoints > 0) ScoreChanged?.Invoke(Score);
            }
        }

        public void AddGoldScore(int points) => AddCollectionScore(points, true);

        public void AddItemScore(int points) => AddCollectionScore(points, false);

        private void OnStateChanged(GameState state)
        {
            if (state != GameState.GameOver || !_started || _finished) return;
            _finished = true;
            var save = SaveManager.Instance;
            int distance = _scoredRow - _startRow;
            int unsavedCells = distance - _savedForwardCells;
            if (save != null && !save.TryUpdate(data =>
            {
                data.lastScore = Score;
                data.bestScore = Math.Max(data.bestScore, Score);
                data.lastRunCoins = CollectedCoins;
                data.totalForwardCells = checked(data.totalForwardCells + unsavedCells);
                data.bestDistance = Math.Max(data.bestDistance, distance);
            }))
                Debug.LogWarning("이번 판 결과를 저장하지 못했습니다. 기존 저장 데이터는 유지됩니다.", this);
            if (!Application.isPlaying) return;
            StartCoroutine(ReturnToMenu());
        }

        private IEnumerator ReturnToMenu()
        {
            yield return new WaitForSecondsRealtime(GameOverDisplaySeconds);
            var scenes = SceneLoadManager.Instance;
            if (!scenes.IsLoading && !scenes.LoadScene(_mainMenuScene, GameState.MainMenu))
                Debug.LogError("게임 종료 후 메인 메뉴로 이동하지 못했습니다.", this);
        }

        private void AddCollectionScore(int points, bool gold)
        {
            if (points < 0)
                throw new ArgumentOutOfRangeException(nameof(points), "Bonus points cannot be negative.");
            if (!_started || GameStateManager.Instance.State != GameState.Playing || points == 0)
                return;

            int total = checked(Score + points);
            if (gold)
                GoldScore = checked(GoldScore + points);
            else
                ItemScore = checked(ItemScore + points);
            Score = total;
            ScoreChanged?.Invoke(Score);
        }

        private void OnDestroy()
        {
            if (_player != null)
                _player.Moved -= OnPlayerMoved;
            if (_state != null) _state.StateChanged -= OnStateChanged;
        }
    }
}
