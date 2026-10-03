using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CrossTheBoard.UI
{
    public sealed class MainMenuBootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject[] _sections;
        [SerializeField] private Button[] _navigation;
        [SerializeField] private GameObject _settingsPanel;
        [SerializeField] private Slider _bgmSlider;
        [SerializeField] private Slider _effectsSlider;
        [SerializeField] private Text _bgmValue;
        [SerializeField] private Text _effectsValue;
        [SerializeField] private Text _achievementSummary;
        [SerializeField] private Text _achievementDetails;
        [SerializeField] private Text _settingsStatus;
        [SerializeField] private Button _startButton;
        [SerializeField] private string _gameScene = "GameplayScene";
        [SerializeField] private Color _selectedColor = new(0.788f, 0.961f, 0.361f);
        [SerializeField] private Color _normalColor = new(0.1f, 0.141f, 0.196f);

        private SoundManager _sound;
        private AchievementTracker _achievements;
        private MainMenuCollectionView _collectionView;

        private void Start()
        {
            _sound = SoundManager.Instance;
            _sound.SetBgmVolume(SaveManager.Instance.Data.bgmVolume);
            _sound.SetEffectsVolume(SaveManager.Instance.Data.effectsVolume);
            _achievements = AchievementTracker.Instance;
            _achievements.ProgressChanged += RefreshAchievements;
            _bgmSlider.onValueChanged.AddListener(SetBgmVolume);
            _effectsSlider.onValueChanged.AddListener(SetEffectsVolume);
            GameStateManager.Instance.SetState(GameState.MainMenu);
            _settingsPanel.SetActive(false);
            _collectionView = new MainMenuCollectionView(this, _sections, _navigation, _achievementSummary.font);
            _sections = _collectionView.Sections;
            _navigation = _collectionView.Navigation;
            SelectSection(0);
            RefreshAchievements();
        }

        private void Update()
        {
            if (_settingsPanel.activeSelf && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                CloseSettings();
        }

        public void SelectSection(int index)
        {
            if (index < 0 || index >= _sections.Length)
                throw new System.ArgumentOutOfRangeException(nameof(index));
            for (int i = 0; i < _sections.Length; i++)
            {
                _sections[i].SetActive(i == index);
                var colors = _navigation[i].colors;
                colors.normalColor = i == index ? _selectedColor : _normalColor;
                colors.selectedColor = colors.normalColor;
                _navigation[i].colors = colors;
                _navigation[i].GetComponentInChildren<Text>().color = i == index ? _normalColor : Color.white;
            }
        }

        public void StartGame()
        {
            if (SceneLoadManager.Instance.LoadScene(_gameScene))
                _startButton.interactable = false;
        }

        public void OpenSettings()
        {
            _bgmSlider.SetValueWithoutNotify(_sound.BgmVolume);
            _effectsSlider.SetValueWithoutNotify(_sound.EffectsVolume);
            _bgmValue.text = $"{Mathf.RoundToInt(_sound.BgmVolume * 100f)}%";
            _effectsValue.text = $"{Mathf.RoundToInt(_sound.EffectsVolume * 100f)}%";
            _settingsStatus.text = string.Empty;
            _settingsPanel.transform.SetAsLastSibling();
            _settingsPanel.SetActive(true);
        }

        public void CloseSettings()
        {
            if (!SaveManager.Instance.Save())
            {
                _settingsStatus.text = "설정을 저장하지 못했습니다. 저장 경로를 확인해 주세요.";
                return;
            }
            _settingsPanel.SetActive(false);
        }

        private void SetBgmVolume(float value)
        {
            _sound.SetBgmVolume(value);
            SaveManager.Instance.Data.bgmVolume = _sound.BgmVolume;
            _bgmValue.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        private void SetEffectsVolume(float value)
        {
            _sound.SetEffectsVolume(value);
            SaveManager.Instance.Data.effectsVolume = _sound.EffectsVolume;
            _effectsValue.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        private void RefreshAchievements()
        {
            _achievementSummary.text = $"전체 달성률 {Mathf.RoundToInt(_achievements.CompletionRate * 100f)}%";
            var definitions = _achievements.Database.Achievements;
            if (definitions.Count == 0)
            {
                _achievementDetails.text = "등록된 업적이 없습니다.";
                return;
            }
            var lines = new System.Text.StringBuilder();
            foreach (var definition in definitions)
            {
                string achieved = _achievements.IsAchieved(definition.id) ? "달성" : "진행 중";
                lines.AppendLine($"{definition.goal}   [{achieved}]\n{_achievements.GetProgress(definition.id)} / {definition.target}   ·   보상 {definition.reward} 코인\n");
            }
            _achievementDetails.text = lines.ToString();
        }

        private void OnDestroy()
        {
            _collectionView?.Dispose();
            if (_achievements != null)
                _achievements.ProgressChanged -= RefreshAchievements;
            _bgmSlider.onValueChanged.RemoveListener(SetBgmVolume);
            _effectsSlider.onValueChanged.RemoveListener(SetEffectsVolume);
        }
    }
}
