using System;
using UnityEngine;

namespace CrossTheBoard
{
    public enum GameState { MainMenu, Playing, Paused, GameOver }

    [DefaultExecutionOrder(-300)]
    public sealed class GameStateManager : MonoBehaviour
    {
        public static GameStateManager Instance { get; private set; }
        public GameState State { get; private set; } = GameState.MainMenu;
        public event Action<GameState> StateChanged;
        private float _resumeTimeScale = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void SetState(GameState state)
        {
            if (!Enum.IsDefined(typeof(GameState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            if (State == state || state == GameState.Paused && State != GameState.Playing)
                return;
            if (state == GameState.Paused)
            {
                _resumeTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
            else if (State == GameState.Paused)
            {
                Time.timeScale = state == GameState.Playing ? _resumeTimeScale : 1f;
            }
            if (state == GameState.MainMenu || state == GameState.GameOver)
                Time.timeScale = 1f;
            State = state;
            StateChanged?.Invoke(state);
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;
            if (State == GameState.Paused)
                Time.timeScale = _resumeTimeScale;
            Instance = null;
        }
    }
}
