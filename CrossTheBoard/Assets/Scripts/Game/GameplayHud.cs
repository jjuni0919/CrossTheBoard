using System;
using UnityEngine;
using UnityEngine.UI;

namespace CrossTheBoard
{
    public sealed class GameplayHud : MonoBehaviour
    {
        [SerializeField] private Text _score;
        [SerializeField] private Text _coins;
        [SerializeField] private Text _health;
        [SerializeField] private Button _back;
        [SerializeField] private GameObject _deathPanel;
        [SerializeField] private Text _deathScore;

        private GameplayController _gameplay;
        private SaveManager _save;
        private PlayerController _player;
        private GameStateManager _state;

        public static void Create(GameplayController gameplay)
        {
            var hud = FindFirstObjectByType<GameplayHud>();
            if (hud == null) throw new InvalidOperationException("GameplayScene requires an authored GameplayHud.");
            hud.Initialize(gameplay);
        }

        private void Initialize(GameplayController gameplay)
        {
            if (_score == null || _coins == null || _health == null || _back == null || _deathPanel == null || _deathScore == null)
                throw new InvalidOperationException("GameplayHud requires its authored labels, menu button and death panel.");
            if (_gameplay != null) return;
            _gameplay = gameplay;
            _player = gameplay.Player;
            _save = SaveManager.Instance;
            _state = GameStateManager.Instance;
            _back.onClick.AddListener(ReturnToMenu);
            _gameplay.ScoreChanged += RefreshScore;
            _player.HealthChanged += RefreshHealth;
            _state.StateChanged += RefreshState;
            if (_save != null) _save.DataChanged += RefreshCoins;
            RefreshScore(_gameplay.Score);
            RefreshHealth(_player.Health);
            RefreshCoins();
            RefreshState(_state.State);
        }

        private void ReturnToMenu()
        {
            if (_state.State != GameState.Playing) return;
            if (SceneLoadManager.Instance.LoadScene("MainMenuScene", GameState.MainMenu))
                _back.interactable = false;
        }

        private void RefreshState(GameState state)
        {
            bool gameOver = state == GameState.GameOver;
            _deathPanel.SetActive(gameOver);
            _back.interactable = state == GameState.Playing;
            if (gameOver) _deathScore.text = $"점수\n{_gameplay.Score:N0}";
        }

        private void RefreshScore(int value) => _score.text = $"점수  {value:N0}";
        private void RefreshHealth(int value) => _health.text = $"HP  {value}/{_player.MaxHealth}";
        private void RefreshCoins() => _coins.text = $"코인  {(_save != null ? _save.Data.coins : 0):N0}";

        private void OnDestroy()
        {
            if (_gameplay != null) _gameplay.ScoreChanged -= RefreshScore;
            if (_player != null) _player.HealthChanged -= RefreshHealth;
            if (_state != null) _state.StateChanged -= RefreshState;
            if (_save != null) _save.DataChanged -= RefreshCoins;
            if (_back != null) _back.onClick.RemoveListener(ReturnToMenu);
        }
    }
}
