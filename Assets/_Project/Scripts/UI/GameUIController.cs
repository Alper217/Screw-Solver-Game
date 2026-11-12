using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using ScrewSolver.Gameplay.Boxes;
using ScrewSolver.Gameplay.Controllers;
using ScrewSolver.Gameplay.Slots;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ScrewSolver.UI
{
    /// <summary>
    /// Oyun içi arayüzü (HUD, Seviye Başarı ve Yenilgi Popup'ları) DOTween animasyonlarıyla yönetir.
    /// </summary>
    [AddComponentMenu("ScrewSolver/Game UI Controller")]
    public sealed class GameUIController : MonoBehaviour
    {
        [Header("Yöneticiler")]
        [SerializeField] private ScrewManager _screwManager;
        [SerializeField] private WaitingSlotManager _waitingSlotManager;

        [Header("HUD Elemanları")]
        [SerializeField] private Text _levelText;
        [SerializeField] private Text _timerText;
        [SerializeField] private Button _restartButton;

        [Header("Zaman & Rekabet Ayarları")]
        [SerializeField] private bool _enableTimer = true;
        [SerializeField] private float _levelDuration = 120f;

        [Header("Zafer Popup (Victory)")]
        [SerializeField] private GameObject _victoryPanel;
        [SerializeField] private CanvasGroup _victoryCanvasGroup;
        [SerializeField] private Transform _victoryCard;
        [SerializeField] private List<Image> _victoryStars = new();
        [SerializeField] private Text _victoryTitleText;
        [SerializeField] private Text _victoryScoreText;
        [SerializeField] private Button _nextLevelButton;

        [Header("Yenilgi Popup (Defeat)")]
        [SerializeField] private GameObject _defeatPanel;
        [SerializeField] private CanvasGroup _defeatCanvasGroup;
        [SerializeField] private Transform _defeatCard;
        [SerializeField] private Text _defeatTitleText;
        [SerializeField] private Text _defeatReasonText;
        [SerializeField] private Button _retryButton;

        public event Action OnNextLevelRequested;
        public event Action OnRetryRequested;

        private float _timeRemaining;
        private bool _isTimerRunning;
        private bool _isGameEnded;

        public float TimeRemaining => _timeRemaining;
        public bool IsGameEnded => _isGameEnded;

        private void Awake()
        {
            AutoBindUI();
            HideAllPopupsImmediately();
        }

        public void AutoBindUI()
        {
            if (_screwManager == null) _screwManager = FindFirstObjectByType<ScrewManager>();
            if (_waitingSlotManager == null) _waitingSlotManager = FindFirstObjectByType<WaitingSlotManager>();

            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 1920f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // Eğer sahnede paneller eksikse runtime'da otomatik inşa et
            EnsureRuntimePanelsExist();

            if (_victoryPanel == null)
            {
                Transform t = transform.Find("VictoryPanel");
                if (t != null) _victoryPanel = t.gameObject;
            }

            if (_victoryPanel != null)
            {
                if (_victoryCanvasGroup == null) _victoryCanvasGroup = _victoryPanel.GetComponent<CanvasGroup>();
                if (_victoryCard == null) _victoryCard = _victoryPanel.transform.Find("VictoryCard");
                if (_victoryCard != null)
                {
                    if (_victoryTitleText == null)
                        _victoryTitleText = _victoryCard.Find("Title")?.GetComponent<Text>() ?? _victoryCard.Find("TitleText")?.GetComponent<Text>();
                    if (_victoryScoreText == null)
                        _victoryScoreText = _victoryCard.Find("ScoreText")?.GetComponent<Text>();
                    if (_nextLevelButton == null)
                        _nextLevelButton = _victoryCard.Find("NextLevelButton")?.GetComponent<Button>();

                    if (_victoryStars.Count == 0)
                    {
                        Transform starContainer = _victoryCard.Find("StarsContainer");
                        if (starContainer != null)
                        {
                            for (int i = 0; i < starContainer.childCount; i++)
                            {
                                Image img = starContainer.GetChild(i).GetComponent<Image>();
                                if (img != null) _victoryStars.Add(img);
                            }
                        }
                    }
                }
            }

            if (_defeatPanel == null)
            {
                Transform t = transform.Find("DefeatPanel");
                if (t != null) _defeatPanel = t.gameObject;
            }

            if (_defeatPanel != null)
            {
                if (_defeatCanvasGroup == null) _defeatCanvasGroup = _defeatPanel.GetComponent<CanvasGroup>();
                if (_defeatCard == null) _defeatCard = _defeatPanel.transform.Find("DefeatCard");
                if (_defeatCard != null)
                {
                    if (_defeatTitleText == null) 
                        _defeatTitleText = _defeatCard.Find("Title")?.GetComponent<Text>() ?? _defeatCard.Find("TitleText")?.GetComponent<Text>();
                    if (_defeatReasonText == null) 
                        _defeatReasonText = _defeatCard.Find("Reason")?.GetComponent<Text>() ?? _defeatCard.Find("ReasonText")?.GetComponent<Text>();
                    if (_retryButton == null) 
                        _retryButton = _defeatCard.Find("RetryButton")?.GetComponent<Button>();
                }
            }

            Transform hudBar = transform.Find("HUD_TopBar");
            if (hudBar != null)
            {
                if (_levelText == null) _levelText = hudBar.Find("LevelText")?.GetComponent<Text>();
                if (_timerText == null) _timerText = hudBar.Find("TimerText")?.GetComponent<Text>();
                if (_restartButton == null) _restartButton = hudBar.Find("RestartButton")?.GetComponent<Button>();
            }

            if (_restartButton != null)
            {
                _restartButton.onClick.RemoveListener(HandleRestartClicked);
                _restartButton.onClick.AddListener(HandleRestartClicked);
            }
            if (_nextLevelButton != null)
            {
                _nextLevelButton.onClick.RemoveListener(HandleNextLevelClicked);
                _nextLevelButton.onClick.AddListener(HandleNextLevelClicked);
            }
            if (_retryButton != null)
            {
                _retryButton.onClick.RemoveListener(HandleRetryClicked);
                _retryButton.onClick.AddListener(HandleRetryClicked);
            }
        }

        private void EnsureRuntimePanelsExist()
        {
            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // 1. HUD_TopBar yoksa oluştur
            if (transform.Find("HUD_TopBar") == null)
            {
                GameObject barGo = new GameObject("HUD_TopBar", typeof(RectTransform));
                barGo.transform.SetParent(transform, false);
                RectTransform hudRt = barGo.GetComponent<RectTransform>();
                hudRt.anchorMin = new Vector2(0f, 1f);
                hudRt.anchorMax = new Vector2(1f, 1f);
                hudRt.pivot = new Vector2(0.5f, 1f);
                hudRt.anchoredPosition = new Vector2(0f, -40f);
                hudRt.sizeDelta = new Vector2(0f, 140f);

                CreateUIText(barGo.transform, "LevelText", "LEVEL 1", defaultFont, 44, Color.white, new Vector2(0f, 25f), new Vector2(400f, 60f));
                CreateUIText(barGo.transform, "TimerText", "02:00", defaultFont, 36, new Color(1f, 0.9f, 0.3f), new Vector2(0f, -25f), new Vector2(300f, 50f));

                GameObject rstGo = new GameObject("RestartButton", typeof(RectTransform), typeof(Image), typeof(Button));
                rstGo.transform.SetParent(barGo.transform, false);
                rstGo.GetComponent<Image>().color = new Color(0.2f, 0.25f, 0.35f, 0.85f);
                RectTransform rstRt = rstGo.GetComponent<RectTransform>();
                rstRt.anchorMin = new Vector2(1f, 0.5f);
                rstRt.anchorMax = new Vector2(1f, 0.5f);
                rstRt.anchoredPosition = new Vector2(-70f, 0f);
                rstRt.sizeDelta = new Vector2(85f, 85f);
                CreateUIText(rstGo.transform, "Icon", "↻", defaultFont, 42, Color.white, Vector2.zero, new Vector2(85f, 85f));
            }

            // 2. VictoryPanel yoksa oluştur
            if (transform.Find("VictoryPanel") == null)
            {
                GameObject vicGo = new GameObject("VictoryPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                vicGo.transform.SetParent(transform, false);
                vicGo.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.12f, 0.82f);
                RectTransform vicRt = vicGo.GetComponent<RectTransform>();
                vicRt.anchorMin = Vector2.zero;
                vicRt.anchorMax = Vector2.one;
                vicRt.sizeDelta = Vector2.zero;

                GameObject cardGo = new GameObject("VictoryCard", typeof(RectTransform), typeof(Image));
                cardGo.transform.SetParent(vicGo.transform, false);
                cardGo.GetComponent<Image>().color = new Color(0.12f, 0.18f, 0.28f, 0.98f);
                RectTransform cardRt = cardGo.GetComponent<RectTransform>();
                cardRt.sizeDelta = new Vector2(820f, 920f);

                CreateUIText(cardGo.transform, "Title", "LEVEL CLEARED!", defaultFont, 58, new Color(1f, 0.84f, 0.1f), new Vector2(0f, 320f), new Vector2(760f, 90f));

                GameObject starsGo = new GameObject("StarsContainer", typeof(RectTransform));
                starsGo.transform.SetParent(cardGo.transform, false);
                starsGo.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 80f);
                starsGo.GetComponent<RectTransform>().sizeDelta = new Vector2(660f, 200f);

                float[] starX = { -200f, 0f, 200f };
                float[] starY = { -15f, 20f, -15f };
                for (int i = 0; i < 3; i++)
                {
                    GameObject sGo = new GameObject($"Star_{i + 1}", typeof(RectTransform), typeof(Image));
                    sGo.transform.SetParent(starsGo.transform, false);
                    sGo.GetComponent<Image>().color = new Color(1f, 0.78f, 0.05f);
                    RectTransform sRt = sGo.GetComponent<RectTransform>();
                    sRt.anchoredPosition = new Vector2(starX[i], starY[i]);
                    sRt.sizeDelta = new Vector2(130f, 130f);
                    CreateUIText(sGo.transform, "TextIcon", "★", defaultFont, 68, Color.white, Vector2.zero, new Vector2(100f, 100f));
                }

                GameObject nextGo = new GameObject("NextLevelButton", typeof(RectTransform), typeof(Image), typeof(Button));
                nextGo.transform.SetParent(cardGo.transform, false);
                nextGo.GetComponent<Image>().color = new Color(0.18f, 0.80f, 0.44f);
                RectTransform nextRt = nextGo.GetComponent<RectTransform>();
                nextRt.anchoredPosition = new Vector2(0f, -260f);
                nextRt.sizeDelta = new Vector2(560f, 130f);
                CreateUIText(nextGo.transform, "Text", "NEXT LEVEL  ▶", defaultFont, 44, Color.white, Vector2.zero, new Vector2(560f, 130f));

                vicGo.SetActive(false);
            }

            // 3. DefeatPanel yoksa oluştur
            if (transform.Find("DefeatPanel") == null)
            {
                GameObject defGo = new GameObject("DefeatPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                defGo.transform.SetParent(transform, false);
                defGo.GetComponent<Image>().color = new Color(0.12f, 0.04f, 0.04f, 0.85f);
                RectTransform defRt = defGo.GetComponent<RectTransform>();
                defRt.anchorMin = Vector2.zero;
                defRt.anchorMax = Vector2.one;
                defRt.sizeDelta = Vector2.zero;

                GameObject cardGo = new GameObject("DefeatCard", typeof(RectTransform), typeof(Image));
                cardGo.transform.SetParent(defGo.transform, false);
                cardGo.GetComponent<Image>().color = new Color(0.18f, 0.14f, 0.16f, 0.98f);
                RectTransform cardRt = cardGo.GetComponent<RectTransform>();
                cardRt.sizeDelta = new Vector2(820f, 820f);

                CreateUIText(cardGo.transform, "Title", "NO VALID MOVES!", defaultFont, 54, new Color(1f, 0.35f, 0.35f), new Vector2(0f, 260f), new Vector2(760f, 80f));
                CreateUIText(cardGo.transform, "Reason", "The screw cannot go to any box,\nand no empty holes left to place!", defaultFont, 34, new Color(0.9f, 0.9f, 0.9f), new Vector2(0f, 40f), new Vector2(700f, 180f));

                GameObject retryGo = new GameObject("RetryButton", typeof(RectTransform), typeof(Image), typeof(Button));
                retryGo.transform.SetParent(cardGo.transform, false);
                retryGo.GetComponent<Image>().color = new Color(0.95f, 0.60f, 0.15f);
                RectTransform retryRt = retryGo.GetComponent<RectTransform>();
                retryRt.anchoredPosition = new Vector2(0f, -240f);
                retryRt.sizeDelta = new Vector2(560f, 130f);
                CreateUIText(retryGo.transform, "Text", "↻  TRY AGAIN", defaultFont, 44, Color.white, Vector2.zero, new Vector2(560f, 130f));

                defGo.SetActive(false);
            }
        }

        private static Text CreateUIText(Transform parent, string name, string content, Font font, int fontSize, Color color, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            Text txt = go.GetComponent<Text>();
            txt.font = font;
            txt.fontSize = fontSize;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = color;
            txt.text = content;
            return txt;
        }

        private void OnEnable()
        {
            AutoBindUI();

            if (_screwManager != null)
            {
                _screwManager.OnLevelCompleted += HandleLevelCompleted;
                _screwManager.OnGameOver += HandleNoMovesGameOver;
            }

            BoxQueueManager boxQueue = FindFirstObjectByType<BoxQueueManager>();
            if (boxQueue != null)
            {
                boxQueue.OnAllBoxesCompleted += HandleLevelCompleted;
            }

            if (_waitingSlotManager != null)
            {
                _waitingSlotManager.OnGameOver += HandleSlotsGameOver;
            }
        }

        private void OnDisable()
        {
            if (_screwManager != null)
            {
                _screwManager.OnLevelCompleted -= HandleLevelCompleted;
                _screwManager.OnGameOver -= HandleNoMovesGameOver;
            }

            BoxQueueManager boxQueue = FindFirstObjectByType<BoxQueueManager>();
            if (boxQueue != null)
            {
                boxQueue.OnAllBoxesCompleted -= HandleLevelCompleted;
            }

            if (_waitingSlotManager != null)
            {
                _waitingSlotManager.OnGameOver -= HandleSlotsGameOver;
            }
        }

        private void Start()
        {
            StartLevelTimer(_levelDuration);
        }

        private void Update()
        {
            if (!_enableTimer || !_isTimerRunning || _isGameEnded) return;

            _timeRemaining -= Time.deltaTime;
            UpdateTimerDisplay();

            if (_timeRemaining <= 0f)
            {
                _timeRemaining = 0f;
                _isTimerRunning = false;
                HandleTimeOutGameOver();
            }
        }

        public void StartLevelTimer(float duration)
        {
            _levelDuration = duration;
            _timeRemaining = duration;
            _isTimerRunning = true;
            _isGameEnded = false;
            UpdateTimerDisplay();
        }

        public void PauseTimer()
        {
            _isTimerRunning = false;
        }

        public void SetLevelNumber(int level)
        {
            if (_levelText != null)
            {
                _levelText.text = $"LEVEL {level}";
            }
        }

        private void UpdateTimerDisplay()
        {
            if (_timerText == null) return;

            int minutes = Mathf.FloorToInt(_timeRemaining / 60f);
            int seconds = Mathf.FloorToInt(_timeRemaining % 60f);
            _timerText.text = $"{minutes:00}:{seconds:00}";

            if (_timeRemaining <= 15f)
            {
                _timerText.color = Color.Lerp(Color.red, Color.yellow, Mathf.PingPong(Time.time * 3f, 1f));
            }
            else
            {
                _timerText.color = Color.white;
            }
        }

        private void HandleLevelCompleted()
        {
            if (_isGameEnded) return;
            _isGameEnded = true;
            _isTimerRunning = false;

            ShowVictoryPopup();
        }

        private void HandleSlotsGameOver()
        {
            if (_isGameEnded) return;
            _isGameEnded = true;
            _isTimerRunning = false;

            ShowDefeatPopup("OUT OF SLOTS!", "No empty slots left to park screws!");
        }

        private void HandleNoMovesGameOver()
        {
            if (_isGameEnded) return;
            _isGameEnded = true;
            _isTimerRunning = false;

            ShowDefeatPopup("NO VALID MOVES!", "The screw cannot go to any box, and no empty holes left to place!");
        }

        private void HandleTimeOutGameOver()
        {
            if (_isGameEnded) return;
            _isGameEnded = true;

            ShowDefeatPopup("TIME'S UP!", "You ran out of time! Try to be faster next time.");
        }

        public void ShowVictoryPopup()
        {
            if (_victoryPanel == null)
            {
                AutoBindUI();
            }

            if (_victoryPanel == null)
            {
                Debug.LogWarning("[GameUIController] VictoryPanel hiyerarşide bulunamadı!");
                return;
            }

            if (_victoryPanel.activeSelf) return;

            _isGameEnded = true;
            _isTimerRunning = false;

            _victoryPanel.SetActive(true);
            if (_defeatPanel != null) _defeatPanel.SetActive(false);

            // Arka planı yumuşakça soldur
            if (_victoryCanvasGroup != null)
            {
                _victoryCanvasGroup.alpha = 1f;
                _victoryCanvasGroup.blocksRaycasts = true;
                _victoryCanvasGroup.interactable = true;
            }

            // Kart zıplayarak açılsın
            if (_victoryCard != null)
            {
                _victoryCard.localScale = Vector3.one * 0.4f;
                _victoryCard.DOScale(Vector3.one, 0.45f).SetEase(Ease.OutBack).SetUpdate(true);
            }

            // Yıldızları sırayla patlat (3 Yıldız Animasyonu)
            StartCoroutine(AnimateStarsRoutine());

            if (_nextLevelButton != null)
            {
                _nextLevelButton.transform.localScale = Vector3.zero;
                _nextLevelButton.transform.DOScale(Vector3.one, 0.4f)
                    .SetEase(Ease.OutBack)
                    .SetDelay(0.7f)
                    .SetUpdate(true)
                    .OnComplete(() =>
                    {
                        _nextLevelButton.transform.DOPunchScale(Vector3.one * 0.08f, 1.2f, 2, 0.5f)
                            .SetLoops(-1, LoopType.Restart)
                            .SetUpdate(true);
                    });
            }
        }

        private IEnumerator AnimateStarsRoutine()
        {
            // Yıldızları sıfırla
            for (int i = 0; i < _victoryStars.Count; i++)
            {
                if (_victoryStars[i] != null)
                {
                    _victoryStars[i].transform.localScale = Vector3.zero;
                }
            }

            yield return new WaitForSecondsRealtime(0.35f);

            for (int i = 0; i < _victoryStars.Count; i++)
            {
                if (_victoryStars[i] != null)
                {
                    _victoryStars[i].transform.DOScale(Vector3.one, 0.30f).SetEase(Ease.OutBack).SetUpdate(true);
                    yield return new WaitForSecondsRealtime(0.18f);
                }
            }
        }

        public void ShowDefeatPopup(string title, string reason)
        {
            if (_defeatPanel == null)
            {
                AutoBindUI();
            }

            if (_defeatPanel == null)
            {
                Debug.LogWarning("[GameUIController] DefeatPanel bulunamadı!");
                return;
            }

            if (_defeatPanel.activeSelf) return;

            _isGameEnded = true;
            _isTimerRunning = false;

            _defeatPanel.SetActive(true);
            if (_victoryPanel != null) _victoryPanel.SetActive(false);

            if (_defeatTitleText != null) _defeatTitleText.text = title;
            if (_defeatReasonText != null) _defeatReasonText.text = reason;

            if (_defeatCanvasGroup != null)
            {
                _defeatCanvasGroup.alpha = 1f;
                _defeatCanvasGroup.blocksRaycasts = true;
                _defeatCanvasGroup.interactable = true;
            }

            if (_defeatCard != null)
            {
                _defeatCard.localScale = Vector3.one * 0.4f;
                _defeatCard.DOScale(Vector3.one, 0.45f).SetEase(Ease.OutBack).SetUpdate(true);
            }

            if (_retryButton != null)
            {
                _retryButton.transform.localScale = Vector3.zero;
                _retryButton.transform.DOScale(Vector3.one, 0.4f)
                    .SetEase(Ease.OutBack)
                    .SetDelay(0.35f)
                    .SetUpdate(true)
                    .OnComplete(() =>
                    {
                        _retryButton.transform.DOPunchScale(Vector3.one * 0.08f, 1.2f, 2, 0.5f)
                            .SetLoops(-1, LoopType.Restart)
                            .SetUpdate(true);
                    });
            }
        }

        public void HideAllPopupsImmediately()
        {
            if (_victoryPanel != null) _victoryPanel.SetActive(false);
            if (_defeatPanel != null) _defeatPanel.SetActive(false);
        }

        private void HandleNextLevelClicked()
        {
            Debug.Log("<color=#00e5ff><b>[GameUI]</b> Next Level tıklandı!</color>");
            if (OnNextLevelRequested != null)
            {
                OnNextLevelRequested.Invoke();
            }
            else
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        private void HandleRetryClicked()
        {
            Debug.Log("<color=#ff9800><b>[GameUI]</b> Retry tıklandı!</color>");
            if (OnRetryRequested != null)
            {
                OnRetryRequested.Invoke();
            }
            else
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        private void HandleRestartClicked()
        {
            HandleRetryClicked();
        }

        private void OnDestroy()
        {
            if (_victoryCard != null) _victoryCard.DOKill();
            if (_defeatCard != null) _defeatCard.DOKill();
            if (_nextLevelButton != null) _nextLevelButton.transform.DOKill();
            if (_retryButton != null) _retryButton.transform.DOKill();
        }
    }
}

