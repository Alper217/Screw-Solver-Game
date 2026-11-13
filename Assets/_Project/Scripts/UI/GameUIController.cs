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
        [SerializeField] private RectTransform _progressFillViewport;
        [SerializeField] private Image _progressFillImage;
        [SerializeField] private Text _progressText;
        [SerializeField] private Text _boxesLeftText;
        [SerializeField] private Image _bottomStatusBg;
        [SerializeField] private Text _bottomStatusText;
        [SerializeField] private CanvasGroup _onboardingHintGroup;

        [Header("Zaman & Rekabet Ayarları")]
        [SerializeField] private bool _enableTimer = true;
        [SerializeField] private float _levelDuration = 120f;

        [Header("Zafer Popup (Victory)")]
        [SerializeField] private GameObject _victoryPanel;
        [SerializeField] private CanvasGroup _victoryCanvasGroup;
        [SerializeField] private Transform _victoryCard;
        [SerializeField] private Transform _victoryRibbon;
        [SerializeField] private Transform _victoryTrophyBadge;
        [SerializeField] private List<Image> _victoryStars = new();
        [SerializeField] private Text _victoryTitleText;
        [SerializeField] private Text _victoryScoreText;
        [SerializeField] private Button _nextLevelButton;

        [Header("Yenilgi Popup (Defeat)")]
        [SerializeField] private GameObject _defeatPanel;
        [SerializeField] private CanvasGroup _defeatCanvasGroup;
        [SerializeField] private Transform _defeatCard;
        [SerializeField] private Transform _defeatRibbon;
        [SerializeField] private Transform _defeatCenterBadge;
        [SerializeField] private Text _defeatTitleText;
        [SerializeField] private Text _defeatReasonText;
        [SerializeField] private Button _retryButton;

        [Header("Cartoon FX Remaster (Win / Lose VFX)")]
        [SerializeField] private GameObject _victoryFireworkVfxPrefab;
        [SerializeField] private GameObject _victoryStarsVfxPrefab;
        [SerializeField] private GameObject _victorySparkleVfxPrefab;
        [SerializeField] private GameObject _defeatBrokenHeartVfxPrefab;
        [SerializeField] private GameObject _defeatSmokeVfxPrefab;
        [SerializeField] private float _vfxSpawnDistance = 6f;

        public event Action OnNextLevelRequested;
        public event Action OnRetryRequested;

        private float _timeRemaining;
        private bool _isTimerRunning;
        private bool _isGameEnded;
        private bool _hasSavedPlaneDistance;
        private float _savedPlaneDistance = 100f;
        private bool _pendingWinShortcut;
        private bool _pendingLoseShortcut;
        private int _totalLevelScrews;
        private bool _hintDismissed;
        private Color _timerDefaultColor = Color.white;
        private bool _hasCapturedTimerColor;
        private readonly List<GameObject> _activeVfxInstances = new();

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
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 999;
            }

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
                    if (_victoryRibbon == null)
                        _victoryRibbon = _victoryCard.Find("RibbonBanner");
                    if (_victoryTrophyBadge == null)
                        _victoryTrophyBadge = _victoryCard.Find("RewardInset/TrophyIcon");
                    if (_victoryTitleText == null)
                        _victoryTitleText = _victoryCard.Find("RibbonBanner/Title")?.GetComponent<Text>()
                            ?? _victoryCard.Find("Title")?.GetComponent<Text>()
                            ?? _victoryCard.Find("TitleText")?.GetComponent<Text>();
                    if (_victoryScoreText == null)
                        _victoryScoreText = _victoryCard.Find("RewardInset/ScoreText")?.GetComponent<Text>()
                            ?? _victoryCard.Find("ScoreText")?.GetComponent<Text>();
                    if (_nextLevelButton == null)
                        _nextLevelButton = _victoryCard.Find("NextLevelButton")?.GetComponent<Button>();

                    if (_victoryStars.Count == 0)
                    {
                        Transform starContainer = _victoryCard.Find("StarsContainer");
                        if (starContainer != null)
                        {
                            for (int i = 0; i < starContainer.childCount; i++)
                            {
                                Transform child = starContainer.GetChild(i);
                                Transform starChild = child.Find($"Star_{i + 1}");
                                Image img = starChild != null ? starChild.GetComponent<Image>() : child.GetComponent<Image>();
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
                    if (_defeatRibbon == null)
                        _defeatRibbon = _defeatCard.Find("RibbonBanner");
                    if (_defeatCenterBadge == null)
                        _defeatCenterBadge = _defeatCard.Find("FailBadge");
                    if (_defeatTitleText == null)
                        _defeatTitleText = _defeatCard.Find("RibbonBanner/Title")?.GetComponent<Text>()
                            ?? _defeatCard.Find("Title")?.GetComponent<Text>()
                            ?? _defeatCard.Find("TitleText")?.GetComponent<Text>();
                    if (_defeatReasonText == null)
                        _defeatReasonText = _defeatCard.Find("ReasonInset/Reason")?.GetComponent<Text>()
                            ?? _defeatCard.Find("Reason")?.GetComponent<Text>()
                            ?? _defeatCard.Find("ReasonText")?.GetComponent<Text>();
                    if (_retryButton == null)
                        _retryButton = _defeatCard.Find("RetryButton")?.GetComponent<Button>();
                }
            }

            Transform hudBar = transform.Find("HUD_TopBar");
            if (hudBar != null)
            {
                if (_levelText == null)
                    _levelText = hudBar.Find("LevelBadge/LevelText")?.GetComponent<Text>()
                        ?? hudBar.Find("LevelText")?.GetComponent<Text>();
                if (_timerText == null)
                    _timerText = hudBar.Find("TimerBadge/TimerText")?.GetComponent<Text>()
                        ?? hudBar.Find("TimerText")?.GetComponent<Text>();
                if (_restartButton == null)
                    _restartButton = hudBar.Find("RestartButton")?.GetComponent<Button>();
                if (_boxesLeftText == null)
                    _boxesLeftText = hudBar.Find("BoxesLeftBadge/BoxesLeftText")?.GetComponent<Text>();
                if (_progressFillViewport == null)
                    _progressFillViewport = hudBar.Find("ProgressBar/FillViewport")?.GetComponent<RectTransform>();
                if (_progressFillImage == null)
                    _progressFillImage = hudBar.Find("ProgressBar/FillViewport/Fill")?.GetComponent<Image>()
                        ?? hudBar.Find("ProgressBar/Fill")?.GetComponent<Image>();
                if (_progressText == null)
                    _progressText = hudBar.Find("ProgressBar/ProgressText")?.GetComponent<Text>();
            }

            if (_timerText != null && !_hasCapturedTimerColor)
            {
                _timerDefaultColor = _timerText.color;
                _hasCapturedTimerColor = true;
            }

            Transform hudBottom = transform.Find("HUD_Bottom");
            if (hudBottom != null)
            {
                if (_bottomStatusBg == null)
                    _bottomStatusBg = hudBottom.Find("BottomStatusPill")?.GetComponent<Image>();
                if (_bottomStatusText == null)
                    _bottomStatusText = hudBottom.Find("BottomStatusPill/StatusText")?.GetComponent<Text>();
                if (_onboardingHintGroup == null)
                    _onboardingHintGroup = hudBottom.Find("OnboardingHint")?.GetComponent<CanvasGroup>();
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
                _screwManager.OnSelectionStateChanged += HandleScrewSelectionStateChanged;
                _screwManager.OnScrewActionCompleted += HandleScrewActionCompleted;
            }

            BoxQueueManager boxQueue = FindFirstObjectByType<BoxQueueManager>();
            if (boxQueue != null)
            {
                boxQueue.OnAllBoxesCompleted += HandleLevelCompleted;
                boxQueue.OnQueueProgressChanged += HandleQueueProgressChanged;
            }

            PuzzleRotator rotator = FindFirstObjectByType<PuzzleRotator>();
            if (rotator != null)
            {
                rotator.OnDragStarted += DismissOnboardingHint;
            }

            if (_waitingSlotManager != null)
            {
                _waitingSlotManager.OnGameOver += HandleSlotsGameOver;
            }

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                UnityEngine.InputSystem.Keyboard.current.onTextInput += HandleTextInput;
            }
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                UnityEngine.InputSystem.Keyboard.current.onTextInput -= HandleTextInput;
            }
#endif
            if (_screwManager != null)
            {
                _screwManager.OnLevelCompleted -= HandleLevelCompleted;
                _screwManager.OnGameOver -= HandleNoMovesGameOver;
                _screwManager.OnSelectionStateChanged -= HandleScrewSelectionStateChanged;
                _screwManager.OnScrewActionCompleted -= HandleScrewActionCompleted;
            }

            BoxQueueManager boxQueue = FindFirstObjectByType<BoxQueueManager>();
            if (boxQueue != null)
            {
                boxQueue.OnAllBoxesCompleted -= HandleLevelCompleted;
                boxQueue.OnQueueProgressChanged -= HandleQueueProgressChanged;
            }

            PuzzleRotator rotator = FindFirstObjectByType<PuzzleRotator>();
            if (rotator != null)
            {
                rotator.OnDragStarted -= DismissOnboardingHint;
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
            HandleDebugShortcuts();

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

        private void HandleTextInput(char ch)
        {
            if (ch == 'ğ' || ch == 'Ğ') _pendingWinShortcut = true;
            else if (ch == 'ü' || ch == 'Ü') _pendingLoseShortcut = true;
        }

        private void HandleDebugShortcuts()
        {
            bool winPressed = _pendingWinShortcut;
            bool losePressed = _pendingLoseShortcut;
            _pendingWinShortcut = false;
            _pendingLoseShortcut = false;

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                if (UnityEngine.InputSystem.Keyboard.current.leftBracketKey.wasPressedThisFrame) winPressed = true;
                if (UnityEngine.InputSystem.Keyboard.current.rightBracketKey.wasPressedThisFrame) losePressed = true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.LeftBracket)) winPressed = true;
            if (Input.GetKeyDown(KeyCode.RightBracket)) losePressed = true;
#endif

            if (winPressed)
            {
                HideAllPopupsImmediately();
                ShowVictoryPopup();
            }
            else if (losePressed)
            {
                HideAllPopupsImmediately();
                ShowDefeatPopup("TRY AGAIN!", "No valid moves left!\nTry a different order next time.");
            }
        }

        public void StartLevelTimer(float duration)
        {
            _levelDuration = duration;
            _timeRemaining = duration;
            _isTimerRunning = true;
            _isGameEnded = false;
            _totalLevelScrews = 0;
            UpdateTimerDisplay();
            RefreshGameplayHud();
        }

        private void HandleScrewSelectionStateChanged(bool hasSelection)
        {
            if (hasSelection)
            {
                DismissOnboardingHint();
            }
            RefreshGameplayHud();
        }

        private void HandleScrewActionCompleted()
        {
            DismissOnboardingHint();
            RefreshGameplayHud();
        }

        private void HandleQueueProgressChanged()
        {
            RefreshGameplayHud();
        }

        public void RefreshGameplayHud()
        {
            var allScrews = FindObjectsByType<ScrewSolver.Gameplay.Screws.ScrewObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (_totalLevelScrews <= 0)
            {
                _totalLevelScrews = allScrews.Length;
            }

            int remainingScrews = 0;
            for (int i = 0; i < allScrews.Length; i++)
            {
                if (allScrews[i] != null && !allScrews[i].IsUnscrewed)
                {
                    remainingScrews++;
                }
            }

            int totalScrews = Mathf.Max(1, _totalLevelScrews);
            int clearedScrews = Mathf.Clamp(_totalLevelScrews - remainingScrews, 0, totalScrews);
            float progressRatio = (float)clearedScrews / totalScrews;

            if (_progressFillViewport != null)
            {
                _progressFillViewport.DOKill();
                if (clearedScrews == 0)
                {
                    _progressFillViewport.anchorMax = new Vector2(0f, 1f);
                }
                else
                {
                    _progressFillViewport.DOAnchorMax(new Vector2(progressRatio, 1f), 0.25f).SetEase(Ease.OutQuad).SetUpdate(true);
                }
            }
            else if (_progressFillImage != null)
            {
                _progressFillImage.DOKill();
                _progressFillImage.DOFillAmount(progressRatio, 0.25f).SetEase(Ease.OutQuad).SetUpdate(true);
            }

            if (_progressText != null)
            {
                _progressText.text = $"{clearedScrews} / {_totalLevelScrews}";
            }

            if (_boxesLeftText != null)
            {
                BoxQueueManager boxQueue = FindFirstObjectByType<BoxQueueManager>();
                if (boxQueue != null && boxQueue.TotalBoxesCount > 0)
                {
                    _boxesLeftText.text = $"KUTU: {boxQueue.RemainingBoxesCount}/{boxQueue.TotalBoxesCount}";
                }
            }

            if (_bottomStatusText != null)
            {
                bool hasSelection = _screwManager != null && _screwManager.HasSelectedScrew;
                if (hasSelection)
                {
                    _bottomStatusText.text = "Yeşil parlak deliklerden birine dokun!";
                    _bottomStatusText.color = new Color(0.75f, 0.28f, 0.04f, 1f);
                    if (_bottomStatusBg != null) _bottomStatusBg.color = new Color(1f, 0.93f, 0.78f, 1f);
                }
                else
                {
                    var allHoles = FindObjectsByType<ScrewSolver.Gameplay.Holes.PuzzleHole>(FindObjectsSortMode.None);
                    int emptyHoleCount = 0;
                    for (int i = 0; i < allHoles.Length; i++)
                    {
                        var h = allHoles[i];
                        if (h == null || h.IsOccupied) continue;
                        if (h.ParentPlate != null && h.ParentPlate.HasFallen) continue;
                        emptyHoleCount++;
                    }

                    _bottomStatusText.text = $"Boş Delik: {emptyHoleCount}   •   Kalan Vida: {remainingScrews}";
                    _bottomStatusText.color = new Color(0.24f, 0.19f, 0.15f, 1f);
                    if (_bottomStatusBg != null) _bottomStatusBg.color = new Color(1f, 0.98f, 0.94f, 1f);
                }
            }
        }

        private void DismissOnboardingHint()
        {
            if (_hintDismissed || _onboardingHintGroup == null) return;
            _hintDismissed = true;
            _onboardingHintGroup.DOKill();
            _onboardingHintGroup.DOFade(0f, 0.4f).SetUpdate(true).OnComplete(() =>
            {
                if (_onboardingHintGroup != null)
                {
                    _onboardingHintGroup.gameObject.SetActive(false);
                }
            });
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
                _timerText.color = _hasCapturedTimerColor ? _timerDefaultColor : Color.white;
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
            ClearActiveVfx();
            BringCanvasForwardForPopup();

            _victoryPanel.SetActive(true);
            if (_defeatPanel != null) _defeatPanel.SetActive(false);

            if (_victoryScoreText != null)
            {
                int bonus = Mathf.Max(50, Mathf.RoundToInt(_timeRemaining * 2f));
                _victoryScoreText.text = $"TIME BONUS +{bonus}";
            }

            // Arka plan yumuşakça belirsin
            if (_victoryCanvasGroup != null)
            {
                _victoryCanvasGroup.alpha = 0f;
                _victoryCanvasGroup.DOFade(1f, 0.25f).SetUpdate(true);
                _victoryCanvasGroup.blocksRaycasts = true;
                _victoryCanvasGroup.interactable = true;
            }

            // Kart zıplayarak açılsın (Royal Match / Peak tarzı)
            if (_victoryCard != null)
            {
                _victoryCard.DOKill();
                _victoryCard.localScale = Vector3.one * 0.45f;
                _victoryCard.DOScale(Vector3.one, 0.45f).SetEase(Ease.OutBack).SetUpdate(true);
            }

            // Üst kurdele pankartı gecikmeli vursun
            if (_victoryRibbon != null)
            {
                _victoryRibbon.DOKill();
                _victoryRibbon.localScale = Vector3.one * 0.6f;
                _victoryRibbon.DOScale(Vector3.one, 0.42f).SetEase(Ease.OutBack).SetDelay(0.12f).SetUpdate(true);
            }

            // Kupa rozeti tatlı yaylansın
            if (_victoryTrophyBadge != null)
            {
                _victoryTrophyBadge.DOKill();
                _victoryTrophyBadge.localScale = Vector3.zero;
                _victoryTrophyBadge.DOScale(Vector3.one, 0.42f).SetEase(Ease.OutBack).SetDelay(0.50f).SetUpdate(true);
            }

            // Cartoon FX Remaster zafer efektlerini patlat
            SpawnVictoryVfx();

            // Yıldızları sırayla patlat (3 Yıldız Animasyonu)
            StartCoroutine(AnimateStarsRoutine());

            if (_nextLevelButton != null)
            {
                _nextLevelButton.transform.DOKill();
                _nextLevelButton.transform.localScale = Vector3.zero;
                _nextLevelButton.transform.DOScale(Vector3.one, 0.42f)
                    .SetEase(Ease.OutBack)
                    .SetDelay(0.75f)
                    .SetUpdate(true)
                    .OnComplete(() =>
                    {
                        _nextLevelButton.transform.DOPunchScale(Vector3.one * 0.06f, 1.3f, 2, 0.5f)
                            .SetLoops(-1, LoopType.Restart)
                            .SetUpdate(true);
                    });
            }
        }

        private IEnumerator AnimateStarsRoutine()
        {
            for (int i = 0; i < _victoryStars.Count; i++)
            {
                if (_victoryStars[i] != null)
                {
                    _victoryStars[i].transform.DOKill();
                    _victoryStars[i].transform.localScale = Vector3.zero;
                }
            }

            yield return new WaitForSecondsRealtime(0.28f);

            for (int i = 0; i < _victoryStars.Count; i++)
            {
                if (_victoryStars[i] != null)
                {
                    Transform starTr = _victoryStars[i].transform;
                    starTr.DOScale(Vector3.one, 0.34f).SetEase(Ease.OutBack).SetUpdate(true);
                    starTr.DOPunchRotation(new Vector3(0f, 0f, (i % 2 == 0 ? 12f : -12f)), 0.36f, 6, 0.6f).SetUpdate(true);
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
            ClearActiveVfx();
            BringCanvasForwardForPopup();

            _defeatPanel.SetActive(true);
            if (_victoryPanel != null) _victoryPanel.SetActive(false);

            if (_defeatTitleText != null) _defeatTitleText.text = title;
            if (_defeatReasonText != null) _defeatReasonText.text = reason;

            // VFX pozisyonunu kart animasyon için küçültülmeden önce gerçek (1x) rozet konumundan oku
            if (_defeatCard != null) _defeatCard.localScale = Vector3.one;
            if (_defeatCenterBadge != null) _defeatCenterBadge.localScale = Vector3.one;
            Canvas.ForceUpdateCanvases();
            SpawnDefeatVfx();

            if (_defeatCanvasGroup != null)
            {
                _defeatCanvasGroup.alpha = 0f;
                _defeatCanvasGroup.DOFade(1f, 0.25f).SetUpdate(true);
                _defeatCanvasGroup.blocksRaycasts = true;
                _defeatCanvasGroup.interactable = true;
            }

            if (_defeatCard != null)
            {
                _defeatCard.DOKill();
                _defeatCard.localScale = Vector3.one * 0.45f;
                _defeatCard.DOScale(Vector3.one, 0.42f).SetEase(Ease.OutBack).SetUpdate(true);
            }

            if (_defeatRibbon != null)
            {
                _defeatRibbon.DOKill();
                _defeatRibbon.localScale = Vector3.one * 0.6f;
                _defeatRibbon.DOScale(Vector3.one, 0.40f).SetEase(Ease.OutBack).SetDelay(0.10f).SetUpdate(true);
            }

            if (_defeatCenterBadge != null)
            {
                _defeatCenterBadge.DOKill();
                _defeatCenterBadge.localScale = Vector3.zero;
                _defeatCenterBadge.DOScale(Vector3.one, 0.38f)
                    .SetEase(Ease.OutBack)
                    .SetDelay(0.22f)
                    .SetUpdate(true)
                    .OnComplete(() =>
                    {
                        _defeatCenterBadge.DOShakeRotation(0.45f, new Vector3(0f, 0f, 14f), 10, 90f).SetUpdate(true);
                    });
            }

            if (_retryButton != null)
            {
                _retryButton.transform.DOKill();
                _retryButton.transform.localScale = Vector3.zero;
                _retryButton.transform.DOScale(Vector3.one, 0.4f)
                    .SetEase(Ease.OutBack)
                    .SetDelay(0.38f)
                    .SetUpdate(true)
                    .OnComplete(() =>
                    {
                        _retryButton.transform.DOPunchScale(Vector3.one * 0.06f, 1.3f, 2, 0.5f)
                            .SetLoops(-1, LoopType.Restart)
                            .SetUpdate(true);
                    });
            }
        }

        private void SpawnVictoryVfx()
        {
            SpawnPopupVfx(_victoryFireworkVfxPrefab, new Vector2(-0.95f, 1.15f), 0.70f, 4.5f);
            SpawnPopupVfx(_victoryFireworkVfxPrefab, new Vector2(0.95f, 0.95f), 0.70f, 4.5f);
            SpawnPopupVfx(_victoryStarsVfxPrefab, new Vector2(0f, 1.45f), 0.80f, 4.5f);
            SpawnPopupVfx(_victorySparkleVfxPrefab, new Vector2(-0.55f, -0.18f), 0.35f, 2.0f, 0.32f);
        }

        private void SpawnDefeatVfx()
        {
            RectTransform badgeRect = _defeatCenterBadge as RectTransform;
            SpawnPopupVfx(_defeatBrokenHeartVfxPrefab, new Vector2(0f, -0.02f), 0.25f, 4.0f, 1f, badgeRect);
            SpawnPopupVfx(_defeatSmokeVfxPrefab, new Vector2(0f, -0.04f), 0.28f, 4.0f, 1f, badgeRect);
        }

        private void SpawnPopupVfx(GameObject prefab, Vector2 cameraViewOffset, float baseScale, float autoDestroySeconds, float alphaMultiplier = 1f, RectTransform uiAnchor = null)
        {
            if (prefab == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            Canvas canvas = GetComponent<Canvas>();
            float dist = _vfxSpawnDistance;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.planeDistance > 0.5f)
            {
                dist = Mathf.Min(_vfxSpawnDistance, canvas.planeDistance * 0.55f);
            }

            float scaleFactor = (dist / 6f) * baseScale;
            Vector3 worldPos;
            if (uiAnchor != null)
            {
                Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? (canvas.worldCamera != null ? canvas.worldCamera : cam)
                    : null;
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCam, uiAnchor.position);
                worldPos = cam.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, dist))
                    + cam.transform.right * (cameraViewOffset.x * (dist / 6f))
                    + cam.transform.up * (cameraViewOffset.y * (dist / 6f));
            }
            else
            {
                worldPos = cam.transform.position
                    + cam.transform.forward * dist
                    + cam.transform.right * (cameraViewOffset.x * (dist / 6f))
                    + cam.transform.up * (cameraViewOffset.y * (dist / 6f));
            }

            GameObject instance = Instantiate(prefab, worldPos, Quaternion.LookRotation(-cam.transform.forward, cam.transform.up));
            instance.transform.localScale = Vector3.one * scaleFactor;

            // ScreenSpaceCamera modunda kamera sallanması UI kenarlarında mavi boşluk/kayma yaratacağı için CFXR kamera sarsıntısını kapat
            CartoonFX.CFXR_Effect[] cfxrEffects = instance.GetComponentsInChildren<CartoonFX.CFXR_Effect>(true);
            for (int i = 0; i < cfxrEffects.Length; i++)
            {
                if (cfxrEffects[i] != null && cfxrEffects[i].cameraShake != null)
                {
                    cfxrEffects[i].cameraShake.StopShake();
                    cfxrEffects[i].cameraShake.enabled = false;
                }
            }

            // Alt partiküllerin ebeveyn ölçeğine (scaleFactor) tam uyması için Hierarchy ölçekleme + opsiyonel parlaklık azaltma
            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (alphaMultiplier < 0.99f)
                {
                    var sc = main.startColor;
                    if (sc.mode == ParticleSystemGradientMode.Color)
                    {
                        Color c = sc.color;
                        c.a *= alphaMultiplier;
                        sc.color = c;
                        main.startColor = sc;
                    }
                    else if (sc.mode == ParticleSystemGradientMode.TwoColors)
                    {
                        Color cMin = sc.colorMin; cMin.a *= alphaMultiplier; sc.colorMin = cMin;
                        Color cMax = sc.colorMax; cMax.a *= alphaMultiplier; sc.colorMax = cMax;
                        main.startColor = sc;
                    }
                }
            }
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null && systems[i].main.playOnAwake)
                {
                    systems[i].Play(false);
                }
            }

            // Tüm alt partiküllerin Canvas'ın önünde çizilmesi için sortingOrder ve sortingFudge'ı zorla
            int targetSortingLayer = canvas != null ? canvas.sortingLayerID : 0;
            int targetSortingOrder = (canvas != null ? canvas.sortingOrder : 0) + 50;
            ParticleSystemRenderer[] psRenderers = instance.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (int i = 0; i < psRenderers.Length; i++)
            {
                if (psRenderers[i] != null)
                {
                    psRenderers[i].sortingLayerID = targetSortingLayer;
                    psRenderers[i].sortingOrder = targetSortingOrder;
                    psRenderers[i].sortingFudge = -5f;
                }
            }

            _activeVfxInstances.Add(instance);

            if (autoDestroySeconds > 0f)
            {
                Destroy(instance, autoDestroySeconds);
            }
        }

        private void ClearActiveVfx()
        {
            for (int i = _activeVfxInstances.Count - 1; i >= 0; i--)
            {
                if (_activeVfxInstances[i] != null)
                {
                    Destroy(_activeVfxInstances[i]);
                }
            }
            _activeVfxInstances.Clear();
        }

        private void BringCanvasForwardForPopup()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceCamera) return;

            if (!_hasSavedPlaneDistance)
            {
                _savedPlaneDistance = canvas.planeDistance;
                _hasSavedPlaneDistance = true;
            }
            if (canvas.planeDistance > 2.2f)
            {
                canvas.planeDistance = 2.0f;
            }
        }

        private void RestoreCanvasPlaneDistance()
        {
            if (!_hasSavedPlaneDistance) return;
            Canvas canvas = GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                canvas.planeDistance = _savedPlaneDistance;
            }
            _hasSavedPlaneDistance = false;
        }

        public void HideAllPopupsImmediately()
        {
            ClearActiveVfx();
            RestoreCanvasPlaneDistance();
            if (_victoryPanel != null) _victoryPanel.SetActive(false);
            if (_defeatPanel != null) _defeatPanel.SetActive(false);
        }

        private void HandleNextLevelClicked()
        {
            ClearActiveVfx();
            RestoreCanvasPlaneDistance();
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
            ClearActiveVfx();
            RestoreCanvasPlaneDistance();
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
            ClearActiveVfx();
            if (_victoryCard != null) _victoryCard.DOKill();
            if (_victoryRibbon != null) _victoryRibbon.DOKill();
            if (_victoryTrophyBadge != null) _victoryTrophyBadge.DOKill();
            if (_defeatCard != null) _defeatCard.DOKill();
            if (_defeatRibbon != null) _defeatRibbon.DOKill();
            if (_defeatCenterBadge != null) _defeatCenterBadge.DOKill();
            if (_nextLevelButton != null) _nextLevelButton.transform.DOKill();
            if (_retryButton != null) _retryButton.transform.DOKill();
            if (_progressFillViewport != null) _progressFillViewport.DOKill();
            if (_progressFillImage != null) _progressFillImage.DOKill();
            if (_onboardingHintGroup != null) _onboardingHintGroup.DOKill();
            for (int i = 0; i < _victoryStars.Count; i++)
            {
                if (_victoryStars[i] != null) _victoryStars[i].transform.DOKill();
            }
        }
    }
}

