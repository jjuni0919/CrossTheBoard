using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrossTheBoard
{
    [DefaultExecutionOrder(-300)]
    public sealed class SceneLoadManager : MonoBehaviour
    {
        public static SceneLoadManager Instance { get; private set; }
        public bool IsLoading => _operation != null;
        public float Progress => _operation == null ? 0f : Mathf.Clamp01(_operation.progress / 0.9f);
        public event Action<string> SceneLoaded;
        private AsyncOperation _operation;
        private string _sceneName;
        private GameState _nextState;

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

        public bool LoadScene(string sceneName, GameState nextState = GameState.Playing)
        {
            if (IsLoading)
                return false;
            if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"Scene '{sceneName}' is not enabled in the build settings.", this);
                return false;
            }
            if (!Enum.IsDefined(typeof(GameState), nextState) || nextState == GameState.Paused)
                throw new ArgumentOutOfRangeException(nameof(nextState));
            _sceneName = sceneName;
            _nextState = nextState;
            _operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (_operation == null)
                return false;
            _operation.completed += CompleteLoad;
            return true;
        }

        private void CompleteLoad(AsyncOperation operation)
        {
            operation.completed -= CompleteLoad;
            string loadedScene = _sceneName;
            _operation = null;
            GameStateManager.Instance.SetState(_nextState);
            SceneLoaded?.Invoke(loadedScene);
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;
            if (_operation != null)
                _operation.completed -= CompleteLoad;
            Instance = null;
        }
    }
}
