using System;
using System.Collections.Generic;
using System.Linq;
using ScrewSolver.Core;
using ScrewSolver.Editor.Solver;
using ScrewSolver.Gameplay.Boxes;
using ScrewSolver.Gameplay.Level;
using ScrewSolver.Gameplay.Plates;
using ScrewSolver.Gameplay.Screws;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScrewSolver.Editor.Generator
{
    /// <summary>
    /// Profesyonel Seviye Üretimi, Manuel Tasarım Fırçası, Dış FBX Dönüştürücü ve
    /// Adım Adım Görsel Çözüm Oynatıcılı (Solution Playback) Headless Solver Workbench'i.
    /// </summary>
    [InitializeOnLoad]
    public sealed class LevelGeneratorWindow : EditorWindow
    {
        private const string AutoGenerateFirstTenKey = "ScrewSolver_First10Levels_Built_v1";
        private const string HexTowerFixKey = "ScrewSolver_HexTower_MeshFix_v2";

        private enum SceneBrushMode
        {
            Off = 0,
            RotateOnly = 1,
            PlaceScrew = 2,
            PaintColor = 3,
            DeleteScrew = 4,
            RelocateLocked = 5
        }

        private static readonly ColorType[] OrderedPaletteColors =
        {
            ColorType.Blue, ColorType.Yellow, ColorType.Green, ColorType.Purple,
            ColorType.Red, ColorType.Orange, ColorType.Cyan, ColorType.Pink
        };

        private int _selectedTab = 0;
        private readonly string[] _tabNames =
        {
            "İlk 10 Seviye",
            "Parametrik Üretici",
            "Solver & Çözüm Analizi",
            "Elle Tasarım & FBX"
        };

        private Vector2 _scrollPos;
        private Vector2 _traceScrollPos;
        private readonly List<string> _lastLogs = new();

        // Tab 1: Custom Parametric Generator State
        private int _customLevelNumber = 11;
        private string _customTitle = "Özel Bulmaca";
        private LevelShapeKind _customShape = LevelShapeKind.HexagonTower;
        private LevelDifficulty _customDifficulty = LevelDifficulty.Medium;
        private float _customTimeLimit = 120f;
        private int _customColorCount = 4;
        private int _customSeed = 1111;
        private readonly ColorType[] _customPaletteOrder =
        {
            ColorType.Blue, ColorType.Yellow, ColorType.Purple,
            ColorType.Green, ColorType.Red, ColorType.Orange
        };

        // Shared Target & Metadata State (Tabs 2 & 3)
        private GameObject _pendingFbxAsset;
        private GameObject _targetModelRoot;
        private string _chassisKeywords = "Core,Chassis,Body,Frame,Skeleton,Base";
        private string _markerPrefixes = "Screw_,Pin_,Bolt_,Socket_";
        private int _extLevelNumber = 11;
        private string _extTitle = "Özel El Yapımı Bölüm";
        private LevelDifficulty _extDifficulty = LevelDifficulty.Medium;
        private float _extTimeLimit = 130f;
        private int _extColorCount = 4;
        private int _extSeed = 2026;
        private bool _wireSavedToLevelManager = true;

        private bool IsWorkbenchActive =>
            _targetModelRoot != null &&
            _targetModelRoot.name.StartsWith("__Workbench_", StringComparison.OrdinalIgnoreCase);

        // Manual Design Brush State (Tab 3)
        private SceneBrushMode _brushMode = SceneBrushMode.Off;
        private ColorType _brushColor = ColorType.Blue;
        private bool _brushSingleClickNoShift = true;
        private bool _brushDragToRotateModel = true;
        private float _brushFineOffset = 0f;
        private bool _brushMirrorOpposite = false;
        private bool _showBlockerLinksInScene = true;
        private bool _showPlateHierarchy = false;
        private bool _showMarkerUtility = false;

        // Zorluk Bazlı Otomatik Dağıtım & Kilitli Düzeltme Modu (Sayı/Renk Dengesi Koruması)
        private bool _autoAdvanceSeedOnDistribute = true;
        private readonly Dictionary<ColorType, int> _relocateBaselineCounts = new();
        private bool _hasRelocateBaseline = false;

        // Dış Prefab Başlangıç Boyutu & Scene HUD Boyut/Konum Paneli
        private float _spawnTargetExtent = 1.65f;
        private bool _showSceneTransformHud = false;

        // Scene View Turntable Drag State (Tab 3 & Ready-Made Preview)
        private bool _sceneMouseDown = false;
        private bool _sceneDragStarted = false;
        private Vector2 _sceneMouseDownPos;
        private int _sceneBrushControlId = 0;
        private bool _weHidUnityTools = false;

        // Editable Queue & Solver Analysis / Playback State (Tab 2 & 3)
        private readonly List<ColorType> _editableBoxQueue = new();
        private GameObject _queueSourceRoot;
        private string _solverAnalysisText = "Analiz için 'Mevcut Renk & Sırayı Test Et' veya 'Çözülebilir Kutu Sırası Bul' butonuna basın.";
        private HeadlessLevelSolver.LevelGraph _lastGraph;
        private HeadlessLevelSolver.SolveReport _lastReport;
        private int _playbackStepIndex = 0;
        private bool _isPlayingSolution = false;
        private double _lastPlaybackTick = 0;
        private float _playbackInterval = 0.85f;
        private bool _focusCameraOnStep = true;
        private bool _showSolutionOverlayInScene = true;
        private bool _showStepList = true;

        // Kayıtlı Aktif Leveller Önbelleği (Oyun Başlatmadan İnceleme Gezgini)
        private readonly List<ExternalModelLevelTool.SavedLevelEntry> _cachedSavedLevels = new();
        private bool _savedLevelsDirty = true;
        private int _selectedSavedLevelIdx = 0;
        private GameObject _lastNavigatorTargetRoot;

        static LevelGeneratorWindow()
        {
            EditorApplication.delayCall += TryRunPendingAutoSetup;
        }

        private static void TryRunPendingAutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeChangedForPendingSetup;
                EditorApplication.playModeStateChanged += OnPlayModeChangedForPendingSetup;
                return;
            }

            if (GameObject.Find("PuzzleRoot") == null && FindFirstObjectByType<LevelManager>() == null) return;

            if (!EditorPrefs.GetBool(AutoGenerateFirstTenKey, false))
            {
                EditorPrefs.SetBool(AutoGenerateFirstTenKey, true);
                EditorPrefs.SetBool(HexTowerFixKey, true);
                List<string> logs = ParametricLevelArchitect.GenerateAndVerifyFirstTenLevels(loadLevelOneIntoScene: true);
                Debug.Log("<color=#00ff88><b>[LevelGenerator] İlk 10 Seviye Solver ile Üretildi & Doğrulandı:</b></color>\n" +
                          string.Join("\n", logs));
                return;
            }

            if (!EditorPrefs.GetBool(HexTowerFixKey, false))
            {
                EditorPrefs.SetBool(HexTowerFixKey, true);
                GameObject rebuilt = ParametricLevelArchitect.RebuildLevelFiveHexagon(out string reportLine);
                if (rebuilt != null)
                {
                    Debug.Log("<color=#00ff88><b>[LevelGenerator] Level 5 (Altıgen Kule) Mesh & Görsel Onarımı Tamamlandı:</b></color>\n" + reportLine);
                }
            }
        }

        private static void OnPlayModeChangedForPendingSetup(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChangedForPendingSetup;
            EditorApplication.delayCall += TryRunPendingAutoSetup;
        }

        [MenuItem("ScrewSolver/Seviye/Level Generator & Solver Penceresi %#l")]
        public static void ShowWindow()
        {
            LevelGeneratorWindow window = GetWindow<LevelGeneratorWindow>("Level Studio");
            window.minSize = new Vector2(490, 620);
            window.RefreshTargetFromScene();
        }

        [MenuItem("ScrewSolver/Seviye/İlk 10 Seviyeyi Tek Tıkla Üret ve Doğrula")]
        public static void RunFirstTenMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Uyarı", "Lütfen Play Mode'dan çıkın.", "Tamam");
                return;
            }

            EditorPrefs.SetBool(AutoGenerateFirstTenKey, true);
            EditorPrefs.SetBool(HexTowerFixKey, true);
            List<string> logs = ParametricLevelArchitect.GenerateAndVerifyFirstTenLevels(loadLevelOneIntoScene: true);
            string summary = string.Join("\n", logs);
            Debug.Log("<color=#00ff88><b>[LevelGenerator] İlk 10 Seviye Raporu:</b></color>\n" + summary);
            EditorUtility.DisplayDialog("İlk 10 Seviye Hazır!", summary, "Harika!");
        }

        private void OnEnable()
        {
            _savedLevelsDirty = true;
            RefreshTargetFromScene();
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnProjectChange()
        {
            _savedLevelsDirty = true;
            Repaint();
        }

        private void OnDisable()
        {
            _isPlayingSolution = false;
            _sceneMouseDown = false;
            _sceneDragStarted = false;
            UpdateUnityToolsHidden(false);
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= OnEditorUpdate;
            SceneView.RepaintAll();
        }

        private void OnEditorUpdate()
        {
            if (!_isPlayingSolution || _lastReport == null || !_lastReport.IsSolvable || _lastReport.Steps.Count == 0)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastPlaybackTick < _playbackInterval) return;

            _lastPlaybackTick = now;
            if (_playbackStepIndex < _lastReport.Steps.Count)
            {
                SetPlaybackStep(_playbackStepIndex + 1);
            }
            else
            {
                _isPlayingSolution = false;
            }
            Repaint();
        }

        private void RefreshTargetFromScene()
        {
            if (_targetModelRoot == null)
            {
                GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
                if (puzzleRoot != null && puzzleRoot.transform.childCount > 0)
                {
                    GameObject fallback = puzzleRoot.transform.GetChild(0).gameObject;
                    GameObject activeChild = null;
                    GameObject workbenchChild = null;

                    for (int i = 0; i < puzzleRoot.transform.childCount; i++)
                    {
                        GameObject child = puzzleRoot.transform.GetChild(i).gameObject;
                        if (child.name.StartsWith("__Workbench_", StringComparison.OrdinalIgnoreCase))
                        {
                            workbenchChild = child;
                            break;
                        }
                        if (activeChild == null && child.activeSelf) activeChild = child;
                    }

                    _targetModelRoot = workbenchChild != null ? workbenchChild : (activeChild != null ? activeChild : fallback);
                }
            }

            SyncTargetMetadataAndQueueIfChanged();
        }

        private void SyncTargetMetadataAndQueueIfChanged()
        {
            if (_targetModelRoot == _queueSourceRoot) return;
            _queueSourceRoot = _targetModelRoot;
            ClearRelocateBaseline();
            _isPlayingSolution = false;
            _playbackStepIndex = 0;
            _lastGraph = null;
            _lastReport = null;
            _editableBoxQueue.Clear();

            if (_targetModelRoot == null) return;

            LevelConfig cfg = _targetModelRoot.GetComponent<LevelConfig>();
            if (cfg != null)
            {
                _extLevelNumber = Mathf.Max(1, cfg.LevelNumber);
                if (!string.IsNullOrEmpty(cfg.LevelTitle)) _extTitle = cfg.LevelTitle;
                _extDifficulty = cfg.Difficulty;
                _extTimeLimit = Mathf.Max(45f, cfg.TimeLimitSeconds);
                if (cfg.OptimizedBoxQueue != null && cfg.OptimizedBoxQueue.Count > 0)
                {
                    _editableBoxQueue.AddRange(cfg.OptimizedBoxQueue);
                }
            }

            if (_editableBoxQueue.Count == 0)
            {
                SyncQueueFromScrews();
            }
        }

        private void SyncQueueFromScrews()
        {
            _editableBoxQueue.Clear();
            if (_targetModelRoot == null) return;
            HeadlessLevelSolver.LevelGraph graph = HeadlessLevelSolver.ExtractGraph(_targetModelRoot);
            _editableBoxQueue.AddRange(HeadlessLevelSolver.BuildDefaultBoxQueueFromGraph(graph));
        }

        private void OnGUI()
        {
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 175f;

            EditorGUILayout.Space(8);
            GUIStyle headerStyle = new(EditorStyles.boldLabel) { fontSize = 14 };
            EditorGUILayout.LabelField("Screw Solver — Seviye Tasarım Stüdyosu & AI Doğrulayıcı", headerStyle);
            EditorGUILayout.Space(4);

            int newTab = GUILayout.Toolbar(_selectedTab, _tabNames, GUILayout.Height(28));
            if (newTab != _selectedTab)
            {
                _selectedTab = newTab;
                if (_selectedTab != 2) _isPlayingSolution = false;
                SceneView.RepaintAll();
            }
            EditorGUILayout.Space(4);
            DrawActiveLevelNavigatorBar();
            EditorGUILayout.Space(6);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_selectedTab)
            {
                case 0: DrawFirstTenTab(); break;
                case 1: DrawParametricTab(); break;
                case 2: DrawSolverAnalysisTab(); break;
                case 3: DrawManualFbxStudioTab(); break;
            }

            EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = prevLabelWidth;
        }

        #region Tab 0: First 10 Levels

        private void DrawFirstTenTab()
        {
            EditorGUILayout.HelpBox(
                "Kolaydan zora (12 -> 40 Vida • 3 -> 10 Kutu • 3 -> 6 Renk) tasarlanan ilk 10 seviyeyi tek tıkla üretir.\n" +
                "Her seviye HeadlessLevelSolver ile simüle edilerek %100 çözülebilirlik ve zorluk onayı ile kaydedilir.",
                MessageType.Info);

            EditorGUILayout.Space(6);
            GUI.backgroundColor = new Color(0.28f, 0.85f, 0.52f);
            if (GUILayout.Button("▶ İlk 10 Seviyeyi Üret, Solver ile Doğrula ve Bağla", GUILayout.Height(38)))
            {
                if (Application.isPlaying)
                {
                    EditorUtility.DisplayDialog("Uyarı", "Play Mode'dan çıkın.", "Tamam");
                    return;
                }
                EditorPrefs.SetBool(AutoGenerateFirstTenKey, true);
                EditorPrefs.SetBool(HexTowerFixKey, true);
                _lastLogs.Clear();
                _lastLogs.AddRange(ParametricLevelArchitect.GenerateAndVerifyFirstTenLevels(loadLevelOneIntoScene: true));
                Debug.Log("<color=#00ff88><b>[LevelGenerator] 10 Seviye Raporu:</b></color>\n" + string.Join("\n", _lastLogs));
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            bool rotateOn = _brushMode == SceneBrushMode.RotateOnly;
            GUI.backgroundColor = rotateOn ? new Color(0.32f, 0.78f, 0.96f) : Color.white;
            if (GUILayout.Button(rotateOn ? "🔄 Sürükle-Döndür: AÇIK (Kapat)" : "🔄 Sahnedeki Leveli Sürükle-Döndür", GUILayout.Height(26)))
            {
                _brushMode = rotateOn ? SceneBrushMode.Off : SceneBrushMode.RotateOnly;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("↺ Ön Yüz (0,0,0)", GUILayout.Width(120), GUILayout.Height(26)))
            {
                ResetTargetModelRotation();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Tasarım Eğrisi (İlk 10 Bölüm Planı)", EditorStyles.boldLabel);

            IReadOnlyList<LevelBlueprintSpec> specs = ParametricLevelArchitect.GetCurriculum();
            for (int i = 0; i < specs.Count; i++)
            {
                LevelBlueprintSpec spec = specs[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Lvl {spec.LevelNumber:00}", EditorStyles.boldLabel, GUILayout.Width(50));
                EditorGUILayout.LabelField($"{spec.Title} ({spec.Shape})", GUILayout.MinWidth(170));
                EditorGUILayout.LabelField($"{spec.Difficulty} • {spec.Palette.Length}R • {spec.TimeLimitSeconds:0}s", GUILayout.Width(130));

                if (GUILayout.Button("Sahnede Aç", GUILayout.Width(82)))
                {
                    int targetNum = spec.LevelNumber;
                    EditorApplication.delayCall += () =>
                    {
                        LoadLevelPrefabIntoPuzzleRoot(targetNum);
                        Repaint();
                    };
                }
                EditorGUILayout.EndHorizontal();
            }

            EnsureSavedLevelsCache();
            bool hasExtraLevels = false;
            foreach (var entry in _cachedSavedLevels)
            {
                if (entry.LevelNumber > 10 && entry.Prefab != null)
                {
                    if (!hasExtraLevels)
                    {
                        EditorGUILayout.Space(8);
                        EditorGUILayout.LabelField("Eklenen / Özel Leveller (11+)", EditorStyles.boldLabel);
                        hasExtraLevels = true;
                    }

                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    string numLabel = entry.LevelNumber < 1000 ? $"Lvl {entry.LevelNumber:00}" : "Özel";
                    EditorGUILayout.LabelField(numLabel, EditorStyles.boldLabel, GUILayout.Width(50));
                    EditorGUILayout.LabelField($"{entry.Title} ({entry.Prefab.name})", GUILayout.MinWidth(170));
                    EditorGUILayout.LabelField($"{entry.ScrewCount} vida", GUILayout.Width(72));
                    if (GUILayout.Button("Sahnede Aç", GUILayout.Width(82)))
                    {
                        var chosen = entry;
                        EditorApplication.delayCall += () =>
                        {
                            LoadLevelPrefabIntoPuzzleRoot(chosen.Prefab, chosen.LevelNumber);
                            Repaint();
                        };
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (_lastLogs.Count > 0)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Son Doğrulama Sonuçları", EditorStyles.boldLabel);
                foreach (string line in _lastLogs)
                {
                    MessageType type = line.StartsWith("[✓]") ? MessageType.None : MessageType.Warning;
                    EditorGUILayout.HelpBox(line, type);
                }
            }
        }

        #endregion

        #region Tab 1: Parametric Generator

        private void DrawParametricTab()
        {
            EditorGUILayout.HelpBox(
                "Yeni bir şablondan özel seviye üretin ve tek tıkla Solver'dan geçirip Prefab olarak kaydedin.",
                MessageType.Info);

            _customLevelNumber = EditorGUILayout.IntSlider("Seviye Numarası", _customLevelNumber, 1, 100);
            _customTitle = EditorGUILayout.TextField("Seviye Başlığı", _customTitle);
            _customShape = (LevelShapeKind)EditorGUILayout.EnumPopup("3D Yapı Şablonu", _customShape);
            _customDifficulty = (LevelDifficulty)EditorGUILayout.EnumPopup("Hedef Zorluk", _customDifficulty);
            _customTimeLimit = EditorGUILayout.Slider("Süre Sınırı (sn)", _customTimeLimit, 45f, 300f);
            _customColorCount = EditorGUILayout.IntSlider("Renk Çeşidi Sayısı", _customColorCount, 3, 6);
            _customSeed = EditorGUILayout.IntField("Varyasyon Tohumu (Seed)", _customSeed);

            EditorGUILayout.Space(10);
            GUI.backgroundColor = new Color(0.35f, 0.78f, 1f);
            if (GUILayout.Button("⚙ Özel Seviyeyi Üret ve Solver ile Doğrula", GUILayout.Height(36)))
            {
                if (Application.isPlaying) return;

                ColorType[] palette = new ColorType[_customColorCount];
                Array.Copy(_customPaletteOrder, palette, _customColorCount);

                GameObject prefab = ParametricLevelArchitect.GenerateCustomLevel(
                    _customLevelNumber, _customTitle, _customShape, _customDifficulty,
                    _customTimeLimit, palette, _customSeed, out string log);

                _lastLogs.Clear();
                _lastLogs.Add(log);
                if (prefab != null)
                {
                    Selection.activeObject = prefab;
                    EditorGUIUtility.PingObject(prefab);
                }
                Debug.Log(log);
            }
            GUI.backgroundColor = Color.white;

            if (_lastLogs.Count > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(_lastLogs[0], MessageType.Info);
            }
        }

        #endregion

        #region Tab 2: Solver & Visual Solution Playback

        private void DrawSolverAnalysisTab()
        {
            EditorGUILayout.HelpBox(
                "El yapımı veya otomatik tasarlanan bölümü simüle ederek çözülebilirliğini kanıtlar.\n" +
                "Çözülebilir bölümlerde 'Adım Adım Çözüm Oynatıcısı' ile her hamleyi 3B Scene ekranında canlı gösterir.",
                MessageType.Info);

            DrawTargetObjectSelector();
            EditorGUILayout.Space(6);

            // Doğrulama ve Optimizasyon Butonları
            if (GUILayout.Button("1. Fiziksel Engelleri (Plaka -> Vida) Otomatik Tara ve Bağla", GUILayout.Height(28)))
            {
                if (_targetModelRoot != null)
                {
                    HeadlessLevelSolver.AutoWirePhysicalDependencies(_targetModelRoot, clearExistingBlockers: true);
                    _solverAnalysisText = "Fiziksel engeller tarandı ve _blockingPlates / _coveredScrews listeleri güncellendi.";
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.Space(4);
            GUI.backgroundColor = new Color(0.38f, 0.90f, 0.62f);
            if (GUILayout.Button("2. Mevcut Renk & Kutu Sırasını Test Et (Çözümü Çıkar)", GUILayout.Height(36)))
            {
                RunCurrentEvaluation(autoWireBlockersFirst: true);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.45f, 0.82f, 1f);
            if (GUILayout.Button("3A. Renkleri Koru -> Çözülebilir Kutu Sırası Bul", GUILayout.Height(32)))
            {
                OptimizeManualQueuePreservingColors();
            }

            GUI.backgroundColor = new Color(1f, 0.80f, 0.35f);
            if (GUILayout.Button("3B. AI ile Renkleri + Kuyruğu Optimize Et", GUILayout.Height(32)))
            {
                OptimizeFullColorsAndQueue();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            MessageType statusMsgType = _lastReport == null
                ? MessageType.Info
                : (_lastReport.IsSolvable ? MessageType.Info : MessageType.Error);
            EditorGUILayout.HelpBox(_solverAnalysisText, statusMsgType);

            DrawSolutionPlayerSection();
        }

        private void DrawSolutionPlayerSection()
        {
            if (_lastReport == null || _lastGraph == null) return;

            if (!_lastReport.IsSolvable || _lastReport.Steps.Count == 0)
            {
                DrawDeadlockDiagnostics();
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"🎬 İnteraktif Çözüm Oynatıcısı ({_lastReport.Steps.Count} Adım)", EditorStyles.boldLabel);

            // Sahne ayarı
            EditorGUILayout.BeginHorizontal();
            _showSolutionOverlayInScene = EditorGUILayout.Toggle("Scene 3B Ok & Halkalar", _showSolutionOverlayInScene);
            _focusCameraOnStep = EditorGUILayout.Toggle("Adıma Kamerayı Odakla", _focusCameraOnStep);
            EditorGUILayout.EndHorizontal();

            // Oynatma kontrol butonları
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("|◀ Başa", GUILayout.Height(28)))
            {
                _isPlayingSolution = false;
                SetPlaybackStep(0);
            }
            if (GUILayout.Button("◀ Geri", GUILayout.Height(28)))
            {
                _isPlayingSolution = false;
                SetPlaybackStep(Mathf.Max(0, _playbackStepIndex - 1));
            }

            GUI.backgroundColor = _isPlayingSolution ? new Color(1f, 0.55f, 0.35f) : new Color(0.35f, 0.92f, 0.55f);
            if (GUILayout.Button(_isPlayingSolution ? "⏸ Duraklat" : "▶ Oynat", GUILayout.Height(28), GUILayout.Width(100)))
            {
                _isPlayingSolution = !_isPlayingSolution;
                if (_isPlayingSolution)
                {
                    if (_playbackStepIndex >= _lastReport.Steps.Count) SetPlaybackStep(0);
                    _lastPlaybackTick = EditorApplication.timeSinceStartup;
                }
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("İleri ▶", GUILayout.Height(28)))
            {
                _isPlayingSolution = false;
                SetPlaybackStep(Mathf.Min(_lastReport.Steps.Count, _playbackStepIndex + 1));
            }
            if (GUILayout.Button("Sona ▶|", GUILayout.Height(28)))
            {
                _isPlayingSolution = false;
                SetPlaybackStep(_lastReport.Steps.Count);
            }
            EditorGUILayout.EndHorizontal();

            // Adım Slider + Hız
            int newStep = EditorGUILayout.IntSlider("Adım:", _playbackStepIndex, 0, _lastReport.Steps.Count);
            if (newStep != _playbackStepIndex)
            {
                _isPlayingSolution = false;
                SetPlaybackStep(newStep);
            }
            _playbackInterval = EditorGUILayout.Slider("Oynatma Hızı (sn/adım):", _playbackInterval, 0.3f, 2.0f);

            // Seçili adım özeti ve canlı 3 kutu durumu
            DrawCurrentStepAndActiveBoxes();

            // Tıklanabilir Adım Listesi
            _showStepList = EditorGUILayout.Foldout(_showStepList, $"Çözüm Adım Listesi (Tıklayarak Adıma Zıpla)", true);
            if (_showStepList)
            {
                _traceScrollPos = EditorGUILayout.BeginScrollView(_traceScrollPos, GUILayout.MaxHeight(180));
                for (int i = 0; i < _lastReport.Steps.Count; i++)
                {
                    HeadlessLevelSolver.SolutionStepInfo st = _lastReport.Steps[i];
                    bool isCurrent = (_playbackStepIndex == i + 1);
                    GUI.backgroundColor = isCurrent ? new Color(0.35f, 0.85f, 1f) : (i < _playbackStepIndex ? new Color(0.82f, 0.95f, 0.82f) : Color.white);
                    if (GUILayout.Button($"{i + 1:00}. {st.Description}", EditorStyles.miniButtonLeft, GUILayout.Height(20)))
                    {
                        _isPlayingSolution = false;
                        SetPlaybackStep(i + 1);
                    }
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCurrentStepAndActiveBoxes()
        {
            if (_lastReport == null) return;

            int activeHoles = 0;
            string stepHeadline;

            if (_playbackStepIndex == 0)
            {
                string firstMove = _lastReport.Steps.Count > 0 ? _lastReport.Steps[0].Description : "-";
                stepHeadline = $"Başlangıç Durumu • Sonraki İlk Hamle: {firstMove}";
            }
            else
            {
                HeadlessLevelSolver.SolutionStepInfo step = _lastReport.Steps[_playbackStepIndex - 1];
                activeHoles = step.ActiveEmptyHolesAfter;
                stepHeadline = $"Adım #{step.StepNumber}: {step.Description}";
            }

            EditorGUILayout.HelpBox($"{stepHeadline}\nOyun Alanındaki Aktif Boş Delik: {activeHoles}", MessageType.None);

            // Canlı 3 Aktif Kutu Önizlemesi
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Aktif Kutular:", EditorStyles.boldLabel, GUILayout.Width(95));
            for (int k = 0; k < 3; k++)
            {
                int colorId = -1;
                int need = 0;
                if (_playbackStepIndex == 0)
                {
                    if (k < _lastReport.TestedBoxQueue.Count)
                    {
                        colorId = (int)_lastReport.TestedBoxQueue[k];
                        need = 4;
                    }
                }
                else
                {
                    HeadlessLevelSolver.SolutionStepInfo st = _lastReport.Steps[_playbackStepIndex - 1];
                    colorId = st.SlotColorIdsAfter[k];
                    need = st.SlotNeedAfter[k];
                }

                if (colorId >= 0 && need > 0)
                {
                    ColorType c = (ColorType)colorId;
                    GUI.backgroundColor = c.ToUnityColor();
                    int filled = 4 - need;
                    GUILayout.Box($"Slot {k + 1}: {c}\n({filled}/4 dolu)", GUILayout.Height(36), GUILayout.ExpandWidth(true));
                }
                else
                {
                    GUI.backgroundColor = new Color(0.4f, 0.4f, 0.4f);
                    GUILayout.Box($"Slot {k + 1}\n(Boş)", GUILayout.Height(36), GUILayout.ExpandWidth(true));
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDeadlockDiagnostics()
        {
            if (_lastGraph == null) return;
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🔍 Çıkmaz Teşhisi & Öneriler", EditorStyles.boldLabel);

            if (_lastGraph.ValidationIssues.Count > 0)
            {
                foreach (string issue in _lastGraph.ValidationIssues)
                {
                    EditorGUILayout.LabelField("• " + issue, EditorStyles.wordWrappedLabel);
                }
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Graf geçerli ancak mevcut kutu iniş sırasıyla çıkmaza giriyor.\n" +
                    "İpucu: Yukarıdaki '3A. Renkleri Koru -> Çözülebilir Kutu Sırası Bul' butonunu deneyin.",
                    EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Tab 3: Manual Designer & Artist FBX Workbench

        private void DrawManualFbxStudioTab()
        {
            EditorGUILayout.HelpBox(
                "Yeni Level Tasarım Akışı:\n" +
                "1) Aşağıdaki 'Yeni Sanatçı FBX' alanına yeni 3D modelinizi atın ve 'Sahneye Çıkar' butonuna basın.\n" +
                "2) Scene ekranında boş modele kendi planınıza göre renkli vidaları yerleştirin.\n" +
                "3) En alttaki 'ONAYLA & LEVELLER DOSYASINA EKLE' tuşuna basın — sistem doğrular, sıraya ekler ve sahneyi temizler.",
                MessageType.Info);

            DrawFbxWorkbenchLauncherSection();
            EditorGUILayout.Space(6);

            DrawTargetObjectSelector();
            EditorGUILayout.Space(6);

            DrawTargetTransformControlsSection();
            EditorGUILayout.Space(6);

            // 1. Sanatçı FBX Hazırlık & Plaka Listesi
            DrawArtistModelSetupSection();
            EditorGUILayout.Space(8);

            // 2. Scene View Manuel Vida Fırçası & Paleti
            DrawManualBrushPaletteSection();
            EditorGUILayout.Space(8);

            // 3. Canlı Seviye Sağlık & Matematik Çubuğu
            DrawLiveLevelHealthSection();
            EditorGUILayout.Space(8);

            // 4. Kutu Kuyruğu Editörü & Test
            DrawManualQueueAndVerifySection();
            EditorGUILayout.Space(8);

            // 5. Prefab Kaydetme / Onayla & Sıraya Ekle
            DrawManualSaveSection();
        }

        private static GameObject FindWorkbenchInPuzzleRoot()
        {
            GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
            if (puzzleRoot == null) return null;

            for (int i = 0; i < puzzleRoot.transform.childCount; i++)
            {
                GameObject child = puzzleRoot.transform.GetChild(i).gameObject;
                if (child.name.StartsWith("__Workbench_", StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
            return null;
        }

        private void DrawFbxWorkbenchLauncherSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🚀 Yeni FBX Modeli Tasarım Tezgahına Çıkar", EditorStyles.boldLabel);

            if (IsWorkbenchActive)
            {
                GUI.backgroundColor = new Color(1f, 0.92f, 0.65f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"AKTİF TASARIM TEZGAHI: {_targetModelRoot.name}  →  Level_{_extLevelNumber:00}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Mevcut sahne seviyesi geçici gizlendi. Vidaları Scene ekranında dizip alttan onaylayın; vazgeçerseniz aşağıdan objeyi silebilirsiniz.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("🎯 Odaklan", GUILayout.Height(26)))
                {
                    Selection.activeGameObject = _targetModelRoot;
                    SceneView.FrameLastActiveSceneView();
                }
                if (GUILayout.Button("🔧 Colliderları Tam Yüzeye Yenile", GUILayout.Height(26)))
                {
                    int updated = ExternalModelLevelTool.ConvertSubMeshesToPlates(_targetModelRoot, _chassisKeywords);
                    _solverAnalysisText = $"{updated} parça ve alt mesh tam yüzey (concave MeshCollider) ile güncellendi.";
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = new Color(1f, 0.50f, 0.45f);
                if (GUILayout.Button("🗑 Bu Objeyi Sil & Sahneyi Geri Yükle", GUILayout.Height(26)))
                {
                    EditorApplication.delayCall += CancelWorkbench;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                GUI.backgroundColor = new Color(0.55f, 0.88f, 1f);
                if (GUILayout.Button("🔩 Vidaları & Siyahlıkları Yüzeye Şekillendir (Eğim/Kavis Uyumu)", GUILayout.Height(26)))
                {
                    int conformed = ExternalModelLevelTool.ConformAllScrewsInTarget(_targetModelRoot);
                    _solverAnalysisText = $"{conformed} vida ve siyah delik yuvası yüzey kavislerine göre yeniden şekillendirildi.";
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndVertical();
                return;
            }

            GameObject strayWorkbench = FindWorkbenchInPuzzleRoot();
            if (strayWorkbench != null)
            {
                GUI.backgroundColor = new Color(1f, 0.78f, 0.55f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Sahnede Açık Tasarım Objesi Var: {strayWorkbench.name}", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("🎯 Seç & Düzenlemeye Devam Et", GUILayout.Height(24)))
                {
                    _targetModelRoot = strayWorkbench;
                    _brushMode = SceneBrushMode.PlaceScrew;
                    SyncTargetMetadataAndQueueIfChanged();
                    Selection.activeGameObject = strayWorkbench;
                    SceneView.FrameLastActiveSceneView();
                }
                GUI.backgroundColor = new Color(1f, 0.45f, 0.42f);
                if (GUILayout.Button("🗑 Bu Objeyi Sil & Önceki Leveli Geri Getir", GUILayout.Height(24)))
                {
                    EditorApplication.delayCall += CancelWorkbench;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            GameObject pickedFbx = (GameObject)EditorGUILayout.ObjectField(
                "Yeni Sanatçı FBX / 3D Model:",
                _pendingFbxAsset,
                typeof(GameObject),
                allowSceneObjects: false);

            if (pickedFbx != _pendingFbxAsset)
            {
                _pendingFbxAsset = pickedFbx;
                if (_pendingFbxAsset != null)
                {
                    _extLevelNumber = ExternalModelLevelTool.GetNextAvailableLevelNumber();
                    _extTitle = _pendingFbxAsset.name.Replace('_', ' ');
                }
            }

            EditorGUILayout.BeginHorizontal();
            _extLevelNumber = EditorGUILayout.IntField("Atanacak Level No:", Mathf.Max(1, _extLevelNumber));
            if (GUILayout.Button("Sıradaki No", GUILayout.Width(96)))
            {
                _extLevelNumber = ExternalModelLevelTool.GetNextAvailableLevelNumber();
            }
            EditorGUILayout.EndHorizontal();
            _extTitle = EditorGUILayout.TextField("Yeni Bölüm Adı:", _extTitle);
            _spawnTargetExtent = EditorGUILayout.Slider("Başlangıç Boyutu:", _spawnTargetExtent, 0.7f, 3.2f);

            EditorGUILayout.Space(4);
            GUI.backgroundColor = _pendingFbxAsset != null ? new Color(0.32f, 0.78f, 0.96f) : new Color(0.75f, 0.75f, 0.75f);
            if (GUILayout.Button("✨ Yeni Level Tasarımını Sahneye Çıkar (Hazırla & Odakla)", GUILayout.Height(34)))
            {
                EditorApplication.delayCall += LaunchPendingFbxIntoWorkbench;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        private void LaunchPendingFbxIntoWorkbench()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Uyarı", "Tasarım tezgahını açmadan önce Play Mode'dan çıkın.", "Tamam");
                return;
            }

            if (_pendingFbxAsset == null)
            {
                EditorUtility.DisplayDialog(
                    "Model Seçilmedi",
                    "Lütfen Project penceresinden sanatçının getirdiği FBX / 3D model prefab'ını 'Yeni Sanatçı FBX / 3D Model' alanına sürükleyip bırakın.",
                    "Tamam");
                return;
            }

            GameObject workbenchRoot = ExternalModelLevelTool.SpawnFbxIntoDesignWorkbench(
                _pendingFbxAsset,
                _extLevelNumber,
                _chassisKeywords,
                out string msg,
                targetLocalExtent: _spawnTargetExtent);

            if (workbenchRoot == null)
            {
                _solverAnalysisText = msg;
                EditorUtility.DisplayDialog("Hata", msg, "Tamam");
                return;
            }

            _targetModelRoot = workbenchRoot;
            ClearRelocateBaseline();
            _brushMode = SceneBrushMode.PlaceScrew;
            SyncTargetMetadataAndQueueIfChanged();
            _solverAnalysisText = msg;
            SceneView.RepaintAll();
            Repaint();
        }

        private void DrawTargetObjectSelector()
        {
            EditorGUILayout.BeginHorizontal();
            GameObject picked = (GameObject)EditorGUILayout.ObjectField("Aktif Sahne Hedefi:", _targetModelRoot, typeof(GameObject), true);
            if (picked != _targetModelRoot)
            {
                if (picked != null && EditorUtility.IsPersistent(picked))
                {
                    _pendingFbxAsset = picked;
                    if (!IsWorkbenchActive)
                    {
                        _extLevelNumber = ExternalModelLevelTool.GetNextAvailableLevelNumber();
                        _extTitle = picked.name.Replace('_', ' ');
                    }
                    _solverAnalysisText = $"'{picked.name}' Project asset'i üstteki 'Yeni Sanatçı FBX' alanına alındı. Sahneye çıkarmak için 'Yeni Level Tasarımını Sahneye Çıkar' tuşuna basın.";
                }
                else
                {
                    _targetModelRoot = picked;
                    SyncTargetMetadataAndQueueIfChanged();
                }
            }

            if (GUILayout.Button("PuzzleRoot'tan Al", GUILayout.Width(122)))
            {
                _targetModelRoot = null;
                RefreshTargetFromScene();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTargetTransformControlsSection()
        {
            if (_targetModelRoot == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("📐 Prefab Boyut (Ölçek) & Konum Ayarı", EditorStyles.boldLabel);

            Vector3 ls = _targetModelRoot.transform.localScale;
            float currentScale = Mathf.Max(0.05f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);

            EditorGUI.BeginChangeCheck();
            float newScale = EditorGUILayout.Slider("Boyut (Ölçek):", currentScale, 0.20f, 3.50f);
            if (EditorGUI.EndChangeCheck())
            {
                ExternalModelLevelTool.SetTargetUniformScale(_targetModelRoot, newScale);
                SceneView.RepaintAll();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("− (%10)", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.SetTargetUniformScale(_targetModelRoot, currentScale * 0.90f);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("+ (%10)", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.SetTargetUniformScale(_targetModelRoot, currentScale * 1.10f);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("0.75x", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.SetTargetUniformScale(_targetModelRoot, 0.75f);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("1.0x", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.SetTargetUniformScale(_targetModelRoot, 1.0f);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("1.35x", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.SetTargetUniformScale(_targetModelRoot, 1.35f);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("📏 Ekrana Sığdır", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.FitTargetToExtent(_targetModelRoot, _spawnTargetExtent);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            Vector3 localPos = _targetModelRoot.transform.localPosition;
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = EditorGUILayout.Vector3Field("Konum (Yerel X, Y, Z):", localPos);
            if (EditorGUI.EndChangeCheck())
            {
                ExternalModelLevelTool.SetTargetLocalPosition(_targetModelRoot, newPos);
                SceneView.RepaintAll();
            }

            EditorGUILayout.BeginHorizontal();
            const float step = 0.10f;
            if (GUILayout.Button("⬆ Y+", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.SetTargetLocalPosition(_targetModelRoot, localPos + new Vector3(0f, step, 0f));
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("⬇ Y-", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.SetTargetLocalPosition(_targetModelRoot, localPos - new Vector3(0f, step, 0f));
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("⬅ X-", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.SetTargetLocalPosition(_targetModelRoot, localPos - new Vector3(step, 0f, 0f));
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("➡ X+", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.SetTargetLocalPosition(_targetModelRoot, localPos + new Vector3(step, 0f, 0f));
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("↺ (0,0,0)", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.SetTargetLocalPosition(_targetModelRoot, Vector3.zero);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("🎯 Merkezi Ortala", GUILayout.Height(22)))
            {
                ExternalModelLevelTool.CenterTargetGeometryAtLocalOrigin(_targetModelRoot);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("✥ Sahnede Taşı (W)", GUILayout.Height(24)))
            {
                SetBrushMode(SceneBrushMode.Off);
                Selection.activeGameObject = _targetModelRoot;
                Tools.current = Tool.Move;
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("⤢ Sahnede Büyüt (R)", GUILayout.Height(24)))
            {
                SetBrushMode(SceneBrushMode.Off);
                Selection.activeGameObject = _targetModelRoot;
                Tools.current = Tool.Scale;
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("⚖ Vida Boyunu Eşitle", GUILayout.Height(24)))
            {
                ExternalModelLevelTool.NormalizeScrewWorldScales(_targetModelRoot);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("🔩 Yüzeye Şekillendir", GUILayout.Height(24)))
            {
                int conformed = ExternalModelLevelTool.ConformAllScrewsInTarget(_targetModelRoot);
                _solverAnalysisText = $"{conformed} vida ve siyah yuvası yüzeye şekillendirildi.";
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawArtistModelSetupSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("1. Sanatçı Modeli / Plaka Kurulumu", EditorStyles.boldLabel);
            _chassisKeywords = EditorGUILayout.TextField("Sabit Gövde İsimleri:", _chassisKeywords);

            if (GUILayout.Button("Parçaları Sökülebilir Plakaya (PlateObject) Dönüştür", GUILayout.Height(30)))
            {
                if (_targetModelRoot != null)
                {
                    int plates = ExternalModelLevelTool.ConvertSubMeshesToPlates(_targetModelRoot, _chassisKeywords);
                    _solverAnalysisText = $"{plates} alt parça sökülebilir PlateObject olarak kuruldu.";
                    SyncQueueFromScrews();
                }
            }

            if (_targetModelRoot != null)
            {
                PlateObject[] plates = _targetModelRoot.GetComponentsInChildren<PlateObject>(true);
                _showPlateHierarchy = EditorGUILayout.Foldout(_showPlateHierarchy, $"Plaka Katmanları ({plates.Length} Plaka) — İç Katmana Erişmek İçin Gizle/Seç", true);
                if (_showPlateHierarchy)
                {
                    foreach (PlateObject p in plates)
                    {
                        if (p == null) continue;
                        int screwCount = p.GetComponentsInChildren<ScrewObject>(true).Length;
                        bool isHidden = SceneVisibilityManager.instance.IsHidden(p.gameObject);

                        EditorGUILayout.BeginHorizontal();
                        GUI.color = screwCount == 0 ? new Color(1f, 0.55f, 0.45f) : Color.white;
                        EditorGUILayout.LabelField($"{p.name}  [{screwCount} vida]", GUILayout.MinWidth(160));
                        GUI.color = Color.white;

                        if (GUILayout.Button("Seç", GUILayout.Width(46)))
                        {
                            Selection.activeGameObject = p.gameObject;
                            EditorGUIUtility.PingObject(p.gameObject);
                        }
                        if (GUILayout.Button(isHidden ? "Göster" : "Gizle", GUILayout.Width(58)))
                        {
                            if (isHidden) SceneVisibilityManager.instance.Show(p.gameObject, true);
                            else SceneVisibilityManager.instance.Hide(p.gameObject, true);
                            SceneView.RepaintAll();
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    if (GUILayout.Button("Tüm Gizlenen Plakaları Tekrar Göster", GUILayout.Height(22)))
                    {
                        SceneVisibilityManager.instance.ShowAll();
                        SceneView.RepaintAll();
                    }
                }
            }

            _showMarkerUtility = EditorGUILayout.Foldout(_showMarkerUtility, "Opsiyonel: İşaretçi Dönüştürücü / Ayrı Yüzey Penceresi", true);
            if (_showMarkerUtility)
            {
                _markerPrefixes = EditorGUILayout.TextField("İşaretçi Ön-Ekleri:", _markerPrefixes);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("İşaretçilerden Vida Kur"))
                {
                    if (_targetModelRoot != null)
                    {
                        int count = ExternalModelLevelTool.ConvertMarkerTransformsToScrews(_targetModelRoot, _markerPrefixes);
                        _solverAnalysisText = $"{count} işaretçiden vida oluşturuldu.";
                    }
                }
                if (GUILayout.Button("Ayrı Yüzey Aracını Aç (Ctrl+Shift+N)"))
                {
                    SurfaceScrewSnapperTool.ShowWindow();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawManualBrushPaletteSection()
        {
            Dictionary<ColorType, int> counts = CollectColorCounts(_targetModelRoot);

            // 2A. Otomatik Zorluk Bazlı Vida Dağıtımı (Kolay / Orta / Zor)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("2. Otomatik Vida Dağıtımı & Düzeltme Fırçası", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("✨ Zorluğa Göre Otomatik Vida Paylaştır (Sonrasında Düzeltme Modu Açar):", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.42f, 0.88f, 0.56f);
            if (GUILayout.Button("🟢 Kolay Dağıt\n(16 Vida • 3 Renk)", GUILayout.Height(36)))
            {
                RunAutoDistributeDifficulty(LevelDifficulty.Easy);
            }
            GUI.backgroundColor = new Color(0.98f, 0.82f, 0.38f);
            if (GUILayout.Button("🟡 Orta Dağıt\n(24 Vida • 4 Renk)", GUILayout.Height(36)))
            {
                RunAutoDistributeDifficulty(LevelDifficulty.Medium);
            }
            GUI.backgroundColor = new Color(0.96f, 0.48f, 0.45f);
            if (GUILayout.Button("🔴 Zor Dağıt\n(36 Vida • 6 Renk)", GUILayout.Height(36)))
            {
                RunAutoDistributeDifficulty(LevelDifficulty.Hard);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            _autoAdvanceSeedOnDistribute = EditorGUILayout.Toggle("Her Basışta Yeni Dizilim (Seed +1)", _autoAdvanceSeedOnDistribute);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            string[] modeNames =
            {
                "Kapalı (Normal)",
                "🔄 Sadece Döndür",
                "+ Vida Ekle",
                "🎨 Rengi Boya",
                "✕ Vida Sil",
                "✏️ Düzeltme (Kilitli)"
            };
            SceneBrushMode newMode = (SceneBrushMode)GUILayout.SelectionGrid((int)_brushMode, modeNames, 3, GUILayout.Height(50));
            if (newMode != _brushMode)
            {
                SetBrushMode(newMode);
            }

            EditorGUILayout.Space(4);

            bool inRelocate = _brushMode == SceneBrushMode.RelocateLocked;
            int totalDebt = inRelocate ? GetTotalRelocateDebt(counts) : 0;

            if (inRelocate)
            {
                if (totalDebt > 0)
                {
                    EditorGUILayout.HelpBox(
                        $"🔒 DÜZELTME MODU SINIRLANDIRMASI AKTİF:\n" +
                        $"Yerleştirilmesi gereken renk(ler): {FormatRelocateDebtSummary(counts)}\n" +
                        "Diğer renkler ve ekstra vida ekleme geçici olarak kilitlendi. Yeni yere yalnızca sökülen renkten vida koyabilirsiniz.",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "✏️ DÜZELTME MODU (SAYI & RENK KİLİTLİ):\n" +
                        "• Mevcut bir vidayı tıklayın → Sökülür ve yalnızca o renkten 1 yerleştirme hakkı açılır.\n" +
                        "• Sökmeden boş yüzeye ekstra vida ekleyemezsiniz (%4 dengesi korunur).",
                        MessageType.Info);
                }
            }

            // Aktif Renk Swatch Paleti + Canlı Sayı
            EditorGUILayout.LabelField(inRelocate ? "Aktif Vida Rengi (Düzeltme Kilidi Uygulanıyor):" : "Aktif Vida Rengi:", EditorStyles.miniBoldLabel);

            for (int row = 0; row < 2; row++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int col = 0; col < 4; col++)
                {
                    ColorType c = OrderedPaletteColors[row * 4 + col];
                    int cnt = counts.TryGetValue(c, out int n) ? n : 0;
                    int debt = inRelocate ? GetRelocateDebt(c, counts) : 0;
                    bool colorAllowed = !inRelocate || debt > 0;
                    bool selected = (_brushColor == c) && (!inRelocate || debt > 0);

                    GUI.enabled = colorAllowed;
                    GUI.backgroundColor = colorAllowed ? c.ToUnityColor() : new Color(0.45f, 0.45f, 0.45f, 0.7f);
                    string mark = selected ? "▶ " : (inRelocate && !colorAllowed ? "🔒 " : "");
                    string validMark = (cnt > 0 && cnt % 4 == 0) ? "✓" : "";
                    string debtTag = debt > 0 ? $" • +{debt} koy" : "";
                    string label = $"{mark}{c}\n({cnt}{validMark}{debtTag})";

                    GUIStyle btnStyle = selected ? new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold } : GUI.skin.button;
                    if (GUILayout.Button(label, btnStyle, GUILayout.Height(36)))
                    {
                        _brushColor = c;
                        if (_brushMode == SceneBrushMode.Off) SetBrushMode(SceneBrushMode.PlaceScrew);
                        SceneView.RepaintAll();
                    }
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(4);
            _brushSingleClickNoShift = EditorGUILayout.Toggle("Tek Tıkla Uygula (Shift'siz)", _brushSingleClickNoShift);
            _brushDragToRotateModel = EditorGUILayout.Toggle("Sürükle-Döndür (Oyundaki Gibi)", _brushDragToRotateModel);

            GUI.enabled = !inRelocate;
            _brushMirrorOpposite = EditorGUILayout.Toggle("Karşı Yüzeye Simetrik Ekle", _brushMirrorOpposite);
            GUI.enabled = true;

            _brushFineOffset = EditorGUILayout.Slider("İnce Yüzey Gömülmesi:", _brushFineOffset, -0.03f, 0.03f);
            _showBlockerLinksInScene = EditorGUILayout.Toggle("Scene'de Kilit Çizgilerini Göster", _showBlockerLinksInScene);

            if (_targetModelRoot != null && GUILayout.Button("↺ Modelin Duruşunu Sıfırla (Ön Yüz 0,0,0)", GUILayout.Height(22)))
            {
                ResetTargetModelRotation();
            }

            if (_brushMode != SceneBrushMode.Off)
            {
                string usageHint = inRelocate
                    ? "DÜZELTME MODU: Mevcut vidaya SOL TIK → Sök • Boş yüzeye SOL TIK → Sökülen rengi koy • SÜRÜKLE → 360° döndür."
                    : (_brushSingleClickNoShift
                        ? (_brushDragToRotateModel
                            ? "SOL TIK: Uygula  •  SOL TIK SÜRÜKLE: Modeli oyundaki gibi 360° çevir  •  Onayla deyince açı otomatik sıfırlanır."
                            : "Scene ekranında doğrudan SOL TIK ile uygular. Kamerayı döndürmek için Sağ Tık / Alt + Tık kullanın.")
                        : "Scene ekranında 'SHIFT + SOL TIK' ile uygular.");
                EditorGUILayout.HelpBox(usageHint, MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void SetBrushMode(SceneBrushMode newMode)
        {
            if (newMode == _brushMode) return;

            if (newMode == SceneBrushMode.RelocateLocked)
            {
                if (!_hasRelocateBaseline && _targetModelRoot != null)
                {
                    CaptureRelocateBaseline(_targetModelRoot);
                }

                Dictionary<ColorType, int> current = CollectColorCounts(_targetModelRoot);
                ColorType? pending = GetFirstPendingDebtColor(current);
                if (pending.HasValue)
                {
                    _brushColor = pending.Value;
                }
            }
            else if (newMode == SceneBrushMode.PlaceScrew ||
                     newMode == SceneBrushMode.PaintColor ||
                     newMode == SceneBrushMode.DeleteScrew)
            {
                ClearRelocateBaseline();
            }

            _brushMode = newMode;
            Repaint();
            SceneView.RepaintAll();
        }

        private void ClearRelocateBaseline()
        {
            _hasRelocateBaseline = false;
            _relocateBaselineCounts.Clear();
        }

        private void CaptureRelocateBaseline(GameObject root)
        {
            _relocateBaselineCounts.Clear();
            if (root == null)
            {
                _hasRelocateBaseline = false;
                return;
            }

            Dictionary<ColorType, int> counts = CollectColorCounts(root);
            foreach (var kv in counts)
            {
                _relocateBaselineCounts[kv.Key] = kv.Value;
            }
            _hasRelocateBaseline = true;
        }

        private int GetRelocateDebt(ColorType color, Dictionary<ColorType, int> currentCounts)
        {
            if (!_hasRelocateBaseline) return 0;
            int baseCount = _relocateBaselineCounts.TryGetValue(color, out int b) ? b : 0;
            int nowCount = currentCounts != null && currentCounts.TryGetValue(color, out int c) ? c : 0;
            return Mathf.Max(0, baseCount - nowCount);
        }

        private int GetTotalRelocateDebt(Dictionary<ColorType, int> currentCounts)
        {
            if (!_hasRelocateBaseline) return 0;
            int sum = 0;
            foreach (var kv in _relocateBaselineCounts)
            {
                int nowCount = currentCounts != null && currentCounts.TryGetValue(kv.Key, out int c) ? c : 0;
                if (kv.Value > nowCount) sum += kv.Value - nowCount;
            }
            return sum;
        }

        private ColorType? GetFirstPendingDebtColor(Dictionary<ColorType, int> currentCounts)
        {
            foreach (ColorType c in OrderedPaletteColors)
            {
                if (GetRelocateDebt(c, currentCounts) > 0) return c;
            }
            return null;
        }

        private ColorType GetEffectiveRelocateColor(Dictionary<ColorType, int> currentCounts)
        {
            if (GetRelocateDebt(_brushColor, currentCounts) > 0) return _brushColor;
            ColorType? first = GetFirstPendingDebtColor(currentCounts);
            if (first.HasValue)
            {
                _brushColor = first.Value;
                return first.Value;
            }
            return _brushColor;
        }

        private string FormatRelocateDebtSummary(Dictionary<ColorType, int> currentCounts)
        {
            List<string> parts = new();
            foreach (ColorType c in OrderedPaletteColors)
            {
                int d = GetRelocateDebt(c, currentCounts);
                if (d > 0) parts.Add($"{c} x{d}");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "Yok";
        }

        private void RunAutoDistributeDifficulty(LevelDifficulty difficulty)
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Uyarı", "Otomatik vida dağıtımı için önce Play Mode'dan çıkın.", "Tamam");
                return;
            }

            if (_targetModelRoot == null)
            {
                EditorUtility.DisplayDialog("Hedef Yok", "Lütfen önce sahneye bir model çıkarın veya hedef seçin.", "Tamam");
                return;
            }

            if (_autoAdvanceSeedOnDistribute)
            {
                _extSeed++;
            }

            _extDifficulty = difficulty;
            _extTimeLimit = difficulty switch
            {
                LevelDifficulty.Easy => 90f,
                LevelDifficulty.Medium => 120f,
                _ => 150f
            };
            _extColorCount = difficulty switch
            {
                LevelDifficulty.Easy => 3,
                LevelDifficulty.Medium => 4,
                _ => 6
            };

            ExternalModelLevelTool.AutoDistributeResult res = ExternalModelLevelTool.AutoDistributeScrewsByDifficulty(
                _targetModelRoot,
                difficulty,
                _chassisKeywords,
                _extLevelNumber,
                _extTitle,
                _extSeed);

            _solverAnalysisText = res.StatusMessage;

            if (res.Success)
            {
                _editableBoxQueue.Clear();
                if (res.OptimizedQueue != null)
                {
                    _editableBoxQueue.AddRange(res.OptimizedQueue);
                }

                _lastGraph = HeadlessLevelSolver.ExtractGraph(_targetModelRoot);
                _lastReport = _lastGraph != null
                    ? HeadlessLevelSolver.Evaluate(_lastGraph, _editableBoxQueue)
                    : null;

                CaptureRelocateBaseline(_targetModelRoot);
                _brushMode = SceneBrushMode.RelocateLocked;

                foreach (ColorType c in OrderedPaletteColors)
                {
                    if (_relocateBaselineCounts.TryGetValue(c, out int count) && count > 0)
                    {
                        _brushColor = c;
                        break;
                    }
                }
            }

            SceneView.RepaintAll();
            Repaint();
        }

        private void DrawLiveLevelHealthSection()
        {
            if (_targetModelRoot == null) return;

            ScrewObject[] screws = _targetModelRoot.GetComponentsInChildren<ScrewObject>(true);
            PlateObject[] plates = _targetModelRoot.GetComponentsInChildren<PlateObject>(true);
            Dictionary<ColorType, int> counts = CollectColorCounts(_targetModelRoot);

            int total = screws.Length;
            int remainder = total % 4;
            int emptyPlates = 0;
            foreach (PlateObject p in plates)
            {
                if (p != null && p.GetComponentsInChildren<ScrewObject>(true).Length == 0) emptyPlates++;
            }

            List<string> badColors = new();
            foreach (var kv in counts)
            {
                if (kv.Value % 4 != 0)
                {
                    int need = 4 - (kv.Value % 4);
                    badColors.Add($"{kv.Key}: {kv.Value} (+{need})");
                }
            }

            bool allHealthy = total > 0 && remainder == 0 && badColors.Count == 0 && emptyPlates == 0;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("3. Canlı Seviye Matematik & Sağlık Durumu", EditorStyles.boldLabel);

            string summary = total == 0
                ? "Henüz vida eklenmedi."
                : (remainder == 0
                    ? $"Toplam Vida: {total} ({total / 4} Kutu) ✓"
                    : $"Toplam Vida: {total} — 4'ün katı olması için +{4 - remainder} vida daha ekleyin!");

            if (badColors.Count > 0) summary += "\nEksik Renk Tamamlama: " + string.Join(" | ", badColors);
            if (emptyPlates > 0) summary += $"\nUyarı: {emptyPlates} plakada hiç vida yok (oyun başında boş düşer).";

            EditorGUILayout.HelpBox(summary, allHealthy ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.EndVertical();
        }

        private void DrawManualQueueAndVerifySection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"4. Kutu İniş Sırası ({_editableBoxQueue.Count} Kutu) & Test", EditorStyles.boldLabel);
            if (GUILayout.Button("Renklerden Yenile", GUILayout.Width(120)))
            {
                SyncQueueFromScrews();
            }
            EditorGUILayout.EndHorizontal();

            if (_editableBoxQueue.Count > 0)
            {
                EditorGUILayout.LabelField("Kutuların iniş sırasını < > butonlarıyla ayarlayabilir veya AI Solver'a buldurabilirsiniz:", EditorStyles.miniLabel);
                const int perRow = 3;
                for (int i = 0; i < _editableBoxQueue.Count; i += perRow)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int c = 0; c < perRow && (i + c) < _editableBoxQueue.Count; c++)
                    {
                        int idx = i + c;
                        ColorType col = _editableBoxQueue[idx];

                        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                        GUI.backgroundColor = col.ToUnityColor();
                        GUILayout.Box($"{idx + 1}. {col}", GUILayout.Height(22), GUILayout.ExpandWidth(true));
                        GUI.backgroundColor = Color.white;

                        if (idx > 0 && GUILayout.Button("<", GUILayout.Width(22), GUILayout.Height(22)))
                        {
                            (_editableBoxQueue[idx], _editableBoxQueue[idx - 1]) = (_editableBoxQueue[idx - 1], _editableBoxQueue[idx]);
                        }
                        if (idx < _editableBoxQueue.Count - 1 && GUILayout.Button(">", GUILayout.Width(22), GUILayout.Height(22)))
                        {
                            (_editableBoxQueue[idx], _editableBoxQueue[idx + 1]) = (_editableBoxQueue[idx + 1], _editableBoxQueue[idx]);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.45f, 0.82f, 1f);
            if (GUILayout.Button("Renkleri Koru -> Kutu Sırası Bul", GUILayout.Height(30)))
            {
                OptimizeManualQueuePreservingColors();
            }
            GUI.backgroundColor = new Color(1f, 0.80f, 0.35f);
            if (GUILayout.Button("Renkleri + Kuyruğu Optimize Et", GUILayout.Height(30)))
            {
                OptimizeFullColorsAndQueue();
            }
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = new Color(0.35f, 0.90f, 0.55f);
            if (GUILayout.Button("🔍 Engelleri Bağla, Test Et ve Çözüm Göstericisinde Aç", GUILayout.Height(36)))
            {
                RunCurrentEvaluation(autoWireBlockersFirst: true);
                _selectedTab = 2; // Solver & Çözüm Analizi sekmesine geçir!
                _brushMode = SceneBrushMode.Off;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        private void DrawManualSaveSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                IsWorkbenchActive ? "5. Tasarımı Onayla & Leveller Klasörüne Ekle" : "5. Bölümü Prefab Olarak Kaydet",
                EditorStyles.boldLabel);

            _extLevelNumber = EditorGUILayout.IntSlider("Kaydedilecek Level No:", _extLevelNumber, 1, 100);
            _extTitle = EditorGUILayout.TextField("Bölüm Adı:", _extTitle);
            _extDifficulty = (LevelDifficulty)EditorGUILayout.EnumPopup("Hedef Zorluk:", _extDifficulty);
            _extTimeLimit = EditorGUILayout.Slider("Süre Sınırı (sn):", _extTimeLimit, 45f, 300f);
            _extColorCount = EditorGUILayout.IntSlider("Oto-Optimize Renk Sayısı:", _extColorCount, 3, 6);
            _extSeed = EditorGUILayout.IntField("Tohum (Seed):", _extSeed);
            _wireSavedToLevelManager = EditorGUILayout.Toggle("LevelManager Listesine Bağla", _wireSavedToLevelManager);

            EditorGUILayout.Space(4);

            if (IsWorkbenchActive)
            {
                GUI.backgroundColor = new Color(0.22f, 0.88f, 0.48f);
                if (GUILayout.Button($"✅ ONAYLA, DOĞRULA VE LEVEL_{_extLevelNumber:00} OLARAK EKLE", GUILayout.Height(40)))
                {
                    EditorApplication.delayCall += ApproveAndSaveWorkbench;
                }

                GUI.backgroundColor = new Color(0.95f, 0.50f, 0.45f);
                if (GUILayout.Button("🗑 Bu Objeyi Sil & Önceki Sahneyi Geri Yükle", GUILayout.Height(28)))
                {
                    EditorApplication.delayCall += CancelWorkbench;
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = new Color(0.28f, 0.85f, 0.52f);
                if (GUILayout.Button("💾 Mevcut Tasarımı Koruyarak Level Prefab Kaydet", GUILayout.Height(36)) && _targetModelRoot != null)
                {
                    if (_editableBoxQueue.Count == 0) SyncQueueFromScrews();
                    GameObject prefab = ExternalModelLevelTool.SaveManualLevelPrefab(
                        _targetModelRoot,
                        _extLevelNumber,
                        _extTitle,
                        _extDifficulty,
                        _extTimeLimit,
                        _editableBoxQueue,
                        _lastReport,
                        _wireSavedToLevelManager,
                        out _solverAnalysisText);

                    if (prefab != null)
                    {
                        Selection.activeObject = prefab;
                        EditorGUIUtility.PingObject(prefab);
                        Debug.Log($"<color=#00ff88><b>[LevelStudio]</b> {_solverAnalysisText}</color>");
                    }
                }
                GUI.backgroundColor = Color.white;
            }

            if (!string.IsNullOrEmpty(_solverAnalysisText))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(_solverAnalysisText, MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }

        private void ApproveAndSaveWorkbench()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Uyarı", "Kaydetmeden önce Play Mode'dan çıkın.", "Tamam");
                return;
            }

            if (_targetModelRoot == null) return;

            SceneVisibilityManager.instance.ShowAll();

            // 0. Düzeltme Modunda sökülüp geri konmamış vida varsa engelle
            if (_brushMode == SceneBrushMode.RelocateLocked && _hasRelocateBaseline)
            {
                Dictionary<ColorType, int> nowCounts = CollectColorCounts(_targetModelRoot);
                int pending = GetTotalRelocateDebt(nowCounts);
                if (pending > 0)
                {
                    EditorUtility.DisplayDialog(
                        "Düzeltme Modunda Eksik Vida Var",
                        $"Yerini değiştirmek için söktüğünüz vidaları henüz yeni yere takmadınız ({pending} adet):\n\n" +
                        $"• {FormatRelocateDebtSummary(nowCounts)}\n\n" +
                        "Lütfen önce eksik renkteki vidaları model üzerinde uygun noktalara takın.",
                        "Tamam");
                    return;
                }
            }

            // 1. Matematiksel 4'ün katı kontrolü
            ScrewObject[] screws = _targetModelRoot.GetComponentsInChildren<ScrewObject>(true);
            if (screws.Length == 0 || screws.Length % 4 != 0)
            {
                EditorUtility.DisplayDialog(
                    "Matematik Denge Hatası",
                    $"Toplam vida sayısı 4'ün tam katı olmalıdır (şu an: {screws.Length}).\n" +
                    (screws.Length == 0 ? "Henüz hiç vida eklenmedi." : $"{4 - (screws.Length % 4)} adet daha vida ekleyin."),
                    "Tamam");
                return;
            }

            Dictionary<ColorType, int> counts = CollectColorCounts(_targetModelRoot);
            List<string> badColors = new();
            foreach (var kv in counts)
            {
                if (kv.Value % 4 != 0)
                {
                    badColors.Add($"• {kv.Key}: {kv.Value} adet ({4 - (kv.Value % 4)} eksik)");
                }
            }

            if (badColors.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "Renk Dengesi Eksik (%4 Kuralı)",
                    "Her renkteki vida sayısı 4'ün tam katı olmalıdır:\n\n" + string.Join("\n", badColors),
                    "Tamam");
                return;
            }

            // 2. Tasarım sırasındaki önizleme dönüşünü temizleyip dik pozisyona al + engelleri bağla + kuyruk testi
            if (_targetModelRoot.transform.localRotation != Quaternion.identity)
            {
                _targetModelRoot.transform.localRotation = Quaternion.identity;
                Physics.SyncTransforms();
            }

            HeadlessLevelSolver.AutoWirePhysicalDependencies(_targetModelRoot, clearExistingBlockers: true);
            _lastGraph = HeadlessLevelSolver.ExtractGraph(_targetModelRoot);

            if (_editableBoxQueue.Count * 4 != _lastGraph.TotalScrews)
            {
                SyncQueueFromScrews();
            }

            _lastReport = HeadlessLevelSolver.Evaluate(_lastGraph, _editableBoxQueue);
            if (!_lastReport.IsSolvable)
            {
                HeadlessLevelSolver.OptimizationResult opt = HeadlessLevelSolver.OptimizeQueueOnly(_lastGraph, _extDifficulty, _extSeed);
                if (opt.Success && opt.Report != null)
                {
                    _editableBoxQueue.Clear();
                    _editableBoxQueue.AddRange(opt.OptimizedBoxQueue);
                    _lastReport = opt.Report;
                }
            }

            if (!_lastReport.IsSolvable)
            {
                bool cancelToInspect = EditorUtility.DisplayDialog(
                    "Solver Çözüm Bulamadı",
                    "Bu vida ve katman dizilimi mevcut haliyle çözülemiyor veya %0 kazanma oranına sahip.\n\n" +
                    "Sorunlu kilidi görmek için 'İptal Et & Analizi Gör' seçebilirsiniz.",
                    "İptal Et & Analizi Gör",
                    "Yine de Kaydet");

                if (cancelToInspect)
                {
                    _selectedTab = 2;
                    _solverAnalysisText = HeadlessLevelSolver.FormatGraphSummary(_lastGraph, _lastReport);
                    Repaint();
                    return;
                }
            }

            // 3. Hedef Level_XX varsa üzerine yazma onayı
            string targetPrefabPath = $"{ParametricLevelArchitect.LevelsFolder}/Level_{_extLevelNumber:00}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(targetPrefabPath) != null)
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Dosya Zaten Var",
                    $"{targetPrefabPath} zaten mevcut. Üzerine yazılsın mı?",
                    "Üzerine Yaz",
                    "Vazgeç");
                if (!overwrite) return;
            }

            // 4. Prefab olarak kaydet + LevelManager'ı sayısal sırala + geçici sahne tezgahını temizle
            GameObject workbenchRef = _targetModelRoot;
            GameObject savedPrefab = ExternalModelLevelTool.SaveManualLevelPrefab(
                workbenchRef,
                _extLevelNumber,
                _extTitle,
                _extDifficulty,
                _extTimeLimit,
                _editableBoxQueue,
                _lastReport,
                _wireSavedToLevelManager,
                out string saveMsg,
                connectSceneInstance: false);

            ExternalModelLevelTool.RestorePuzzleRootFromWorkbench(workbenchRef);

            ClearRelocateBaseline();
            _brushMode = SceneBrushMode.Off;
            _targetModelRoot = null;
            _pendingFbxAsset = null;
            RefreshTargetFromScene();

            _extLevelNumber = ExternalModelLevelTool.GetNextAvailableLevelNumber();
            _solverAnalysisText = saveMsg + "\nSahne önceki haline döndürüldü ve LevelManager listesi sıralandı.";

            if (savedPrefab != null)
            {
                Selection.activeObject = savedPrefab;
                EditorGUIUtility.PingObject(savedPrefab);
                Debug.Log($"<color=#00ff88><b>[LevelWorkbench]</b> {_solverAnalysisText}</color>");
                EditorUtility.DisplayDialog("Yeni Level Eklendi!", _solverAnalysisText, "Harika!");
            }

            SceneView.RepaintAll();
            Repaint();
        }

        private void CancelWorkbench()
        {
            GameObject workbenchRef = IsWorkbenchActive ? _targetModelRoot : FindWorkbenchInPuzzleRoot();
            if (workbenchRef == null) return;

            bool confirm = EditorUtility.DisplayDialog(
                "Tasarım Objesini Sil & İptal Et",
                $"Sahnedeki geçici tasarım objesi ('{workbenchRef.name}') silinecek ve önceki sahne seviyesi geri yüklenecek. Emin misiniz?",
                "Evet, Objeyi Sil",
                "Vazgeç");

            if (!confirm) return;

            ExternalModelLevelTool.RestorePuzzleRootFromWorkbench(workbenchRef);
            ClearRelocateBaseline();
            _brushMode = SceneBrushMode.Off;
            _targetModelRoot = null;
            RefreshTargetFromScene();
            _solverAnalysisText = "Tasarım objesi silindi, önceki sahne seviyesi geri yüklendi.";
            SceneView.RepaintAll();
            Repaint();
        }

        #endregion

        #region Solver Execution & Optimization

        private void RunCurrentEvaluation(bool autoWireBlockersFirst)
        {
            if (_targetModelRoot == null)
            {
                _solverAnalysisText = "Lütfen önce hedef seviye objesini seçin.";
                return;
            }

            if (autoWireBlockersFirst)
            {
                HeadlessLevelSolver.AutoWirePhysicalDependencies(_targetModelRoot, clearExistingBlockers: true);
            }

            _lastGraph = HeadlessLevelSolver.ExtractGraph(_targetModelRoot);

            if (_editableBoxQueue.Count == 0 || _editableBoxQueue.Count * 4 != _lastGraph.TotalScrews)
            {
                LevelConfig cfg = _targetModelRoot.GetComponent<LevelConfig>();
                if (cfg != null && cfg.OptimizedBoxQueue != null && cfg.OptimizedBoxQueue.Count * 4 == _lastGraph.TotalScrews)
                {
                    _editableBoxQueue.Clear();
                    _editableBoxQueue.AddRange(cfg.OptimizedBoxQueue);
                }
                else
                {
                    SyncQueueFromScrews();
                }
            }

            _lastReport = HeadlessLevelSolver.Evaluate(_lastGraph, _editableBoxQueue);
            _solverAnalysisText = HeadlessLevelSolver.FormatGraphSummary(_lastGraph, _lastReport);
            _isPlayingSolution = false;
            SetPlaybackStep(_lastReport.IsSolvable && _lastReport.Steps.Count > 0 ? 1 : 0);
            SceneView.RepaintAll();
        }

        private void OptimizeManualQueuePreservingColors()
        {
            if (_targetModelRoot == null) return;

            HeadlessLevelSolver.AutoWirePhysicalDependencies(_targetModelRoot, clearExistingBlockers: true);
            _lastGraph = HeadlessLevelSolver.ExtractGraph(_targetModelRoot);
            HeadlessLevelSolver.OptimizationResult opt = HeadlessLevelSolver.OptimizeQueueOnly(_lastGraph, _extDifficulty, _extSeed);

            if (opt.Success && opt.Report != null)
            {
                _editableBoxQueue.Clear();
                _editableBoxQueue.AddRange(opt.OptimizedBoxQueue);
                _lastReport = opt.Report;
                _solverAnalysisText = "Mevcut vida renkleri korunarak çözülebilir kutu sırası bulundu!\n" +
                                      HeadlessLevelSolver.FormatGraphSummary(_lastGraph, _lastReport);
                SetPlaybackStep(_lastReport.Steps.Count > 0 ? 1 : 0);
            }
            else
            {
                _lastReport = new HeadlessLevelSolver.SolveReport { IsSolvable = false };
                _solverAnalysisText = "Mevcut renklerle çözülebilir kutu sırası bulunamadı. Renk sayılarını (her biri 4'ün katı) veya katman kilitlerini kontrol edin.";
            }
            SceneView.RepaintAll();
        }

        private void OptimizeFullColorsAndQueue()
        {
            if (_targetModelRoot == null) return;

            ColorType[] palette = new ColorType[_extColorCount];
            Array.Copy(_customPaletteOrder, palette, _extColorCount);

            ExternalModelLevelTool.FinalizeAndVerifyExternalModel(
                _targetModelRoot,
                _extLevelNumber,
                _extTitle,
                _extDifficulty,
                _extTimeLimit,
                palette,
                _extSeed,
                saveAsPrefab: false,
                out _);

            LevelConfig cfg = _targetModelRoot.GetComponent<LevelConfig>();
            if (cfg != null && cfg.OptimizedBoxQueue != null)
            {
                _editableBoxQueue.Clear();
                _editableBoxQueue.AddRange(cfg.OptimizedBoxQueue);
            }

            RunCurrentEvaluation(autoWireBlockersFirst: false);
        }

        private void SetPlaybackStep(int newStep)
        {
            if (_lastReport == null)
            {
                _playbackStepIndex = 0;
                return;
            }

            _playbackStepIndex = Mathf.Clamp(newStep, 0, _lastReport.Steps.Count);

            if (_focusCameraOnStep && _lastGraph != null && _lastReport.Steps.Count > 0)
            {
                int focusIdx = _playbackStepIndex > 0 ? _playbackStepIndex - 1 : 0;
                int screwIdx = _lastReport.Steps[focusIdx].ScrewIndex;
                if (screwIdx >= 0 && screwIdx < _lastGraph.Screws.Count)
                {
                    ScrewObject screw = _lastGraph.Screws[screwIdx].ScrewRef;
                    SceneView sv = SceneView.lastActiveSceneView;
                    if (screw != null && sv != null)
                    {
                        Vector3 lookDir = -screw.transform.forward;
                        if (lookDir.sqrMagnitude < 0.001f) lookDir = Vector3.back;
                        Quaternion rot = Quaternion.LookRotation(lookDir, Vector3.up);
                        sv.LookAt(screw.transform.position, rot, Mathf.Clamp(sv.size, 1.4f, 3.0f), sv.orthographic, false);
                    }
                }
            }

            SceneView.RepaintAll();
        }

        #endregion

        #region Scene View Overlays (Brush + Blockers + Solution Playback)

        private void UpdateUnityToolsHidden(bool shouldHide)
        {
            if (shouldHide && !_weHidUnityTools)
            {
                _weHidUnityTools = true;
                Tools.hidden = true;
            }
            else if (!shouldHide && _weHidUnityTools)
            {
                _weHidUnityTools = false;
                Tools.hidden = false;
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_targetModelRoot == null)
            {
                UpdateUnityToolsHidden(false);
                return;
            }

            if (_showBlockerLinksInScene && (_selectedTab == 2 || _selectedTab == 3))
            {
                DrawBlockerLinksGizmos();
            }

            if (_selectedTab == 2 && _showSolutionOverlayInScene && _lastReport != null && _lastReport.IsSolvable)
            {
                DrawSolutionPlaybackGizmos();
            }

            bool inBrushTab = _selectedTab == 3;
            bool fullOverlay = inBrushTab && (IsWorkbenchActive || _brushMode != SceneBrushMode.Off);
            bool compactOverlay = !inBrushTab && _targetModelRoot != null;

            Rect hudRect = fullOverlay
                ? GetSceneHudRect(sceneView, compact: false)
                : (compactOverlay ? GetSceneHudRect(sceneView, compact: true) : Rect.zero);

            bool pointerOverHud = (fullOverlay || compactOverlay) && Event.current != null && hudRect.Contains(Event.current.mousePosition);

            bool brushHandlesScene = _brushMode == SceneBrushMode.RotateOnly || (inBrushTab && _brushMode != SceneBrushMode.Off);
            bool hideToolsNow = brushHandlesScene && (_brushMode == SceneBrushMode.RotateOnly || _brushSingleClickNoShift);
            UpdateUnityToolsHidden(hideToolsNow);

            if (brushHandlesScene)
            {
                HandleManualBrushSceneGUI(pointerOverHud);
            }

            if (fullOverlay)
            {
                DrawSceneWorkbenchOverlay(hudRect);
            }
            else if (compactOverlay)
            {
                DrawCompactRotateOverlay(hudRect);
            }
        }

        private void DrawBlockerLinksGizmos()
        {
            ScrewObject[] screws = _targetModelRoot.GetComponentsInChildren<ScrewObject>(true);
            foreach (ScrewObject s in screws)
            {
                if (s == null || s.BlockingPlates == null || s.BlockingPlates.Count == 0) continue;
                Handles.color = new Color(1f, 0.55f, 0.1f, 0.85f);
                Handles.DrawSolidDisc(s.transform.position, s.transform.forward, 0.04f);

                foreach (PlateObject bp in s.BlockingPlates)
                {
                    if (bp == null) continue;
                    Handles.DrawDottedLine(s.transform.position, bp.transform.position, 4f);
                }
            }
        }

        private void DrawSolutionPlaybackGizmos()
        {
            if (_lastGraph == null || _lastReport == null || _lastReport.Steps.Count == 0) return;

            bool[] screwDone = new bool[_lastGraph.TotalScrews];
            bool[] plateDropped = new bool[_lastGraph.Plates.Count];

            int executedCount = Mathf.Min(_playbackStepIndex, _lastReport.Steps.Count);
            for (int i = 0; i < executedCount; i++)
            {
                var st = _lastReport.Steps[i];
                if (!st.IsTransfer && st.ScrewIndex >= 0 && st.ScrewIndex < screwDone.Length)
                {
                    screwDone[st.ScrewIndex] = true;
                }
                if (st.DroppedPlateIndex >= 0 && st.DroppedPlateIndex < plateDropped.Length)
                {
                    plateDropped[st.DroppedPlateIndex] = true;
                }
            }

            // Düşmüş plakaları kırmızı çerçeve ile işaretle
            for (int p = 0; p < _lastGraph.Plates.Count; p++)
            {
                if (!plateDropped[p]) continue;
                PlateObject plate = _lastGraph.Plates[p].PlateRef;
                if (plate == null) continue;
                Renderer rend = plate.GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    Handles.color = new Color(1f, 0.25f, 0.25f, 0.9f);
                    Handles.DrawWireCube(rend.bounds.center, rend.bounds.size * 1.03f);
                    Handles.Label(rend.bounds.center, $"DÜŞTÜ: {plate.name}", EditorStyles.whiteBoldLabel);
                }
            }

            // Çıkarılmış vidaları yeşil onay halkası ile işaretle
            int highlightStepIdx = _playbackStepIndex > 0 ? _playbackStepIndex - 1 : 0;
            int activeScrewIdx = _lastReport.Steps[highlightStepIdx].ScrewIndex;

            for (int s = 0; s < _lastGraph.TotalScrews; s++)
            {
                if (!screwDone[s] || s == activeScrewIdx) continue;
                ScrewObject sc = _lastGraph.Screws[s].ScrewRef;
                if (sc == null) continue;
                Handles.color = new Color(0.3f, 0.95f, 0.45f, 0.55f);
                Handles.DrawWireDisc(sc.transform.position + sc.transform.forward * 0.12f, sc.transform.forward, 0.085f);
            }

            // Şu anki / sıradaki hamle vidasını güçlü ok ve etiket ile vurgula
            HeadlessLevelSolver.SolutionStepInfo activeStep = _lastReport.Steps[highlightStepIdx];
            if (activeStep.ScrewIndex >= 0 && activeStep.ScrewIndex < _lastGraph.Screws.Count)
            {
                ScrewObject targetScrew = _lastGraph.Screws[activeStep.ScrewIndex].ScrewRef;
                if (targetScrew != null)
                {
                    Vector3 headPos = targetScrew.transform.position + targetScrew.transform.forward * 0.11f;
                    Vector3 dir = targetScrew.transform.forward;

                    Handles.color = _playbackStepIndex == 0 ? Color.cyan : new Color(1f, 0.88f, 0.15f);
                    Handles.DrawWireDisc(headPos, dir, 0.14f);
                    Handles.DrawWireDisc(headPos, dir, 0.17f);
                    Handles.ArrowHandleCap(0, headPos, Quaternion.LookRotation(dir), 0.42f, EventType.Repaint);

                    string prefix = _playbackStepIndex == 0 ? "SONRAKİ " : "";
                    Handles.Label(headPos + dir * 0.46f, $"{prefix}#{activeStep.StepNumber}: {activeStep.Description}", EditorStyles.whiteBoldLabel);

                    if (activeStep.IsTransfer && activeStep.TargetSlotOrPlateIndex >= 0 && activeStep.TargetSlotOrPlateIndex < _lastGraph.Plates.Count)
                    {
                        PlateObject destPlate = _lastGraph.Plates[activeStep.TargetSlotOrPlateIndex].PlateRef;
                        if (destPlate != null)
                        {
                            Vector3 start = headPos + dir * 0.15f;
                            Vector3 end = destPlate.transform.position;
                            Vector3 tangentOffset = (dir + Vector3.up * 0.5f) * 0.45f;
                            Handles.DrawBezier(start, end, start + tangentOffset, end + Vector3.up * 0.45f, Color.cyan, null, 4f);
                        }
                    }
                }
            }
        }

        private Rect GetSceneHudRect(SceneView sceneView, bool compact)
        {
            float ppp = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            float viewW = sceneView.camera != null ? sceneView.camera.pixelWidth / ppp : sceneView.position.width;
            float viewH = sceneView.camera != null ? sceneView.camera.pixelHeight / ppp : sceneView.position.height;
            float targetW = compact ? 470f : 960f;
            float barW = Mathf.Min(targetW, Mathf.Max(300f, viewW - 24f));
            float barH = compact ? 38f : (_showSceneTransformHud ? 156f : 102f);
            return new Rect((viewW - barW) * 0.5f, Mathf.Max(8f, viewH - barH - 14f), barW, barH);
        }

        private void ResetTargetModelRotation()
        {
            if (_targetModelRoot == null) return;
            Undo.RecordObject(_targetModelRoot.transform, "Reset Model Rotation");
            _targetModelRoot.transform.localRotation = Quaternion.identity;
            Physics.SyncTransforms();
            SceneView.RepaintAll();
        }

        private void DrawCompactRotateOverlay(Rect hudRect)
        {
            bool resetClicked = false;

            Handles.BeginGUI();
            EditorGUI.DrawRect(hudRect, new Color(0.10f, 0.12f, 0.16f, 0.88f));
            EditorGUI.DrawRect(new Rect(hudRect.x, hudRect.y, hudRect.width, 2f), new Color(0.32f, 0.78f, 0.96f, 1f));

            GUILayout.BeginArea(new Rect(hudRect.x + 8f, hudRect.y + 7f, hudRect.width - 16f, hudRect.height - 12f));
            EditorGUILayout.BeginHorizontal();

            string modelName = _targetModelRoot != null ? _targetModelRoot.name : "-";
            GUILayout.Label($"🎯 {modelName}", EditorStyles.whiteBoldLabel, GUILayout.Width(135f));

            bool rotateActive = _brushMode == SceneBrushMode.RotateOnly;
            GUI.backgroundColor = rotateActive ? new Color(0.28f, 0.85f, 0.52f) : Color.white;
            if (GUILayout.Button(rotateActive ? "🔄 Sürükle-Döndür: AÇIK" : "🔄 Sürükle-Döndür Aç", GUILayout.Height(22)))
            {
                _brushMode = rotateActive ? SceneBrushMode.Off : SceneBrushMode.RotateOnly;
                Repaint();
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("↺ Ön Yüz", GUILayout.Width(74), GUILayout.Height(22)))
            {
                resetClicked = true;
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
            Handles.EndGUI();

            if (resetClicked)
            {
                ResetTargetModelRotation();
            }
        }

        private void DrawSceneWorkbenchOverlay(Rect hudRect)
        {
            bool approveClicked = false;
            bool cancelClicked = false;
            bool focusClicked = false;
            bool resetRotClicked = false;
            bool toggleTransformHud = false;
            bool fitScaleClicked = false;
            bool centerPosClicked = false;
            bool resetPosClicked = false;
            bool conformScrewsClicked = false;
            float? pendingNewScale = null;
            Vector3? pendingNewPos = null;
            Tool? switchUnityTool = null;
            SceneBrushMode? requestedMode = null;
            LevelDifficulty? distributeDifficulty = null;

            ScrewObject[] screws = _targetModelRoot != null ? _targetModelRoot.GetComponentsInChildren<ScrewObject>(true) : Array.Empty<ScrewObject>();
            int total = screws.Length;
            bool mathOk = total > 0 && total % 4 == 0;
            Dictionary<ColorType, int> counts = CollectColorCounts(_targetModelRoot);
            bool inRelocate = _brushMode == SceneBrushMode.RelocateLocked;
            int totalDebt = inRelocate ? GetTotalRelocateDebt(counts) : 0;

            Vector3 curLs = _targetModelRoot != null ? _targetModelRoot.transform.localScale : Vector3.one;
            float currentScale = Mathf.Max(0.05f, (Mathf.Abs(curLs.x) + Mathf.Abs(curLs.y) + Mathf.Abs(curLs.z)) / 3f);
            Vector3 curPos = _targetModelRoot != null ? _targetModelRoot.transform.localPosition : Vector3.zero;

            Handles.BeginGUI();
            EditorGUI.DrawRect(hudRect, new Color(0.10f, 0.12f, 0.16f, 0.92f));
            Color accentColor = inRelocate
                ? (totalDebt > 0 ? new Color(1f, 0.72f, 0.20f, 1f) : new Color(0.45f, 0.85f, 1f, 1f))
                : (IsWorkbenchActive ? new Color(0.28f, 0.85f, 0.52f, 1f) : new Color(0.32f, 0.78f, 0.96f, 1f));
            EditorGUI.DrawRect(new Rect(hudRect.x, hudRect.y, hudRect.width, 2f), accentColor);

            GUILayout.BeginArea(new Rect(hudRect.x + 8f, hudRect.y + 6f, hudRect.width - 16f, hudRect.height - 10f));

            // 1. Satır: Durum + Fırça Modu + Duruş Sıfırla + Onayla / İptal
            EditorGUILayout.BeginHorizontal();
            GUI.color = totalDebt > 0 ? new Color(1f, 0.78f, 0.30f) : (mathOk ? new Color(0.45f, 1f, 0.65f) : new Color(1f, 0.80f, 0.40f));
            string titleTag = IsWorkbenchActive ? $"L{_extLevelNumber:00}" : "Fırça";
            GUILayout.Label($"{titleTag} | {total}v {(mathOk ? "✓" : $"%{total % 4}")}", EditorStyles.whiteBoldLabel, GUILayout.Width(96f));
            GUI.color = Color.white;

            string[] shortModes = { "Kapalı", "🔄 Dön", "+ Ekle", "Boya", "✕ Sil", "✏️ Düzelt" };
            SceneBrushMode nextMode = (SceneBrushMode)GUILayout.Toolbar((int)_brushMode, shortModes, GUILayout.Height(22));
            if (nextMode != _brushMode)
            {
                requestedMode = nextMode;
            }

            if (GUILayout.Button("↺ Ön", GUILayout.Width(48), GUILayout.Height(22)))
            {
                resetRotClicked = true;
            }

            if (GUILayout.Button("Göster", GUILayout.Width(48), GUILayout.Height(22)))
            {
                SceneVisibilityManager.instance.ShowAll();
                focusClicked = true;
            }

            if (IsWorkbenchActive)
            {
                GUI.backgroundColor = new Color(0.25f, 0.88f, 0.50f);
                if (GUILayout.Button("✅ Onayla", GUILayout.Width(76), GUILayout.Height(22)))
                {
                    approveClicked = true;
                }

                GUI.backgroundColor = new Color(0.95f, 0.45f, 0.45f);
                if (GUILayout.Button("🗑", GUILayout.Width(28), GUILayout.Height(22)))
                {
                    cancelClicked = true;
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(3f);

            // 2. Satır: Tek Tıkla Kolay/Orta/Zor Dağıt + Boyut/Konum Aç/Kapa + Yüzeye Şekillendir
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Oto Dağıt:", EditorStyles.whiteMiniLabel, GUILayout.Width(58f));

            GUI.backgroundColor = new Color(0.42f, 0.88f, 0.56f);
            if (GUILayout.Button("🟢 Kolay (16)", EditorStyles.miniButton, GUILayout.Width(80f), GUILayout.Height(20)))
            {
                distributeDifficulty = LevelDifficulty.Easy;
            }
            GUI.backgroundColor = new Color(0.98f, 0.82f, 0.38f);
            if (GUILayout.Button("🟡 Orta (24)", EditorStyles.miniButton, GUILayout.Width(78f), GUILayout.Height(20)))
            {
                distributeDifficulty = LevelDifficulty.Medium;
            }
            GUI.backgroundColor = new Color(0.96f, 0.48f, 0.45f);
            if (GUILayout.Button("🔴 Zor (36)", EditorStyles.miniButton, GUILayout.Width(74f), GUILayout.Height(20)))
            {
                distributeDifficulty = LevelDifficulty.Hard;
            }

            GUI.backgroundColor = _showSceneTransformHud ? new Color(0.45f, 0.82f, 1f) : new Color(0.82f, 0.86f, 0.92f);
            if (GUILayout.Button("📐 Boyut/Konum", EditorStyles.miniButton, GUILayout.Width(98f), GUILayout.Height(20)))
            {
                toggleTransformHud = true;
            }
            GUI.backgroundColor = new Color(0.55f, 0.88f, 1f);
            if (GUILayout.Button("🔩 Şekillendir", EditorStyles.miniButton, GUILayout.Width(86f), GUILayout.Height(20)))
            {
                conformScrewsClicked = true;
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(6f);
            if (inRelocate)
            {
                GUI.color = totalDebt > 0 ? new Color(1f, 0.82f, 0.35f) : new Color(0.65f, 0.92f, 1f);
                string lockText = totalDebt > 0
                    ? $"🔒 Yalnızca koyabilirsiniz: {FormatRelocateDebtSummary(counts)}"
                    : "✏️ Düzeltme (Kilitli): Taşımak için mevcut bir vidayı tıkla-sök";
                GUILayout.Label(lockText, EditorStyles.whiteMiniLabel);
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(0.80f, 0.86f, 0.94f);
                GUILayout.Label("Sürükle: 360° Döndür  •  Tık: Fırça  •  Oto Dağıt → Düzeltme açar", EditorStyles.whiteMiniLabel);
                GUI.color = Color.white;
            }
            EditorGUILayout.EndHorizontal();

            // Opsiyonel 2B Satırları: Scene içi Boyut & Konum Ayarı
            if (_showSceneTransformHud && _targetModelRoot != null)
            {
                GUILayout.Space(4f);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Boyut:", EditorStyles.whiteMiniLabel, GUILayout.Width(44f));
                float editedScale = EditorGUILayout.Slider(currentScale, 0.20f, 3.50f, GUILayout.Width(175f));
                if (Mathf.Abs(editedScale - currentScale) > 0.001f) pendingNewScale = editedScale;
                if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(24f), GUILayout.Height(18))) pendingNewScale = currentScale * 0.90f;
                if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(24f), GUILayout.Height(18))) pendingNewScale = currentScale * 1.10f;
                if (GUILayout.Button("1x", EditorStyles.miniButton, GUILayout.Width(28f), GUILayout.Height(18))) pendingNewScale = 1.0f;
                if (GUILayout.Button("Sığdır", EditorStyles.miniButton, GUILayout.Width(48f), GUILayout.Height(18))) fitScaleClicked = true;

                GUILayout.Space(8f);
                if (GUILayout.Button("✥ Taşı (W)", EditorStyles.miniButton, GUILayout.Width(72f), GUILayout.Height(18))) switchUnityTool = Tool.Move;
                if (GUILayout.Button("⤢ Ölçek (R)", EditorStyles.miniButton, GUILayout.Width(76f), GUILayout.Height(18))) switchUnityTool = Tool.Scale;
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(2f);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Konum:", EditorStyles.whiteMiniLabel, GUILayout.Width(44f));
                GUILayout.Label("X", EditorStyles.whiteMiniLabel, GUILayout.Width(12f));
                float px = EditorGUILayout.FloatField(curPos.x, GUILayout.Width(48f));
                GUILayout.Label("Y", EditorStyles.whiteMiniLabel, GUILayout.Width(12f));
                float py = EditorGUILayout.FloatField(curPos.y, GUILayout.Width(48f));
                GUILayout.Label("Z", EditorStyles.whiteMiniLabel, GUILayout.Width(12f));
                float pz = EditorGUILayout.FloatField(curPos.z, GUILayout.Width(48f));
                Vector3 editedPos = new Vector3(px, py, pz);
                if ((editedPos - curPos).sqrMagnitude > 0.00001f) pendingNewPos = editedPos;

                if (GUILayout.Button("⬆", EditorStyles.miniButton, GUILayout.Width(26f), GUILayout.Height(18))) pendingNewPos = curPos + new Vector3(0f, 0.10f, 0f);
                if (GUILayout.Button("⬇", EditorStyles.miniButton, GUILayout.Width(26f), GUILayout.Height(18))) pendingNewPos = curPos - new Vector3(0f, 0.10f, 0f);
                if (GUILayout.Button("↺ 0,0,0", EditorStyles.miniButton, GUILayout.Width(54f), GUILayout.Height(18))) resetPosClicked = true;
                if (GUILayout.Button("🎯 Ortala", EditorStyles.miniButton, GUILayout.Width(64f), GUILayout.Height(18))) centerPosClicked = true;
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(4f);

            // 3. Satır: 8 Renk Paleti + Canlı Vida Sayıları (Düzeltme modunda kilitlenir)
            EditorGUILayout.BeginHorizontal();
            foreach (ColorType c in OrderedPaletteColors)
            {
                int cnt = counts.TryGetValue(c, out int n) ? n : 0;
                int debt = inRelocate ? GetRelocateDebt(c, counts) : 0;
                bool colorAllowed = !inRelocate || debt > 0;
                bool selected = (_brushColor == c) && (!inRelocate || debt > 0);

                GUI.enabled = colorAllowed;
                GUI.backgroundColor = colorAllowed ? c.ToUnityColor() : new Color(0.38f, 0.38f, 0.38f, 0.65f);
                string prefix = selected ? "▶" : (inRelocate && !colorAllowed ? "🔒" : "");
                string okMark = cnt > 0 && cnt % 4 == 0 ? "✓" : "";
                string debtTag = debt > 0 ? $"+{debt}" : "";
                string txt = $"{prefix}{c} ({cnt}{okMark}{debtTag})";

                if (GUILayout.Button(txt, selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(24)))
                {
                    _brushColor = c;
                    if (_brushMode == SceneBrushMode.Off) requestedMode = SceneBrushMode.PlaceScrew;
                    Repaint();
                }
                GUI.enabled = true;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            GUILayout.EndArea();
            Handles.EndGUI();

            if (toggleTransformHud)
            {
                _showSceneTransformHud = !_showSceneTransformHud;
                SceneView.RepaintAll();
            }

            if (pendingNewScale.HasValue && _targetModelRoot != null)
            {
                ExternalModelLevelTool.SetTargetUniformScale(_targetModelRoot, pendingNewScale.Value);
                Repaint();
                SceneView.RepaintAll();
            }
            else if (fitScaleClicked && _targetModelRoot != null)
            {
                ExternalModelLevelTool.FitTargetToExtent(_targetModelRoot, _spawnTargetExtent);
                Repaint();
                SceneView.RepaintAll();
            }

            if (pendingNewPos.HasValue && _targetModelRoot != null)
            {
                ExternalModelLevelTool.SetTargetLocalPosition(_targetModelRoot, pendingNewPos.Value);
                Repaint();
                SceneView.RepaintAll();
            }
            else if (resetPosClicked && _targetModelRoot != null)
            {
                ExternalModelLevelTool.SetTargetLocalPosition(_targetModelRoot, Vector3.zero);
                Repaint();
                SceneView.RepaintAll();
            }
            else if (centerPosClicked && _targetModelRoot != null)
            {
                ExternalModelLevelTool.CenterTargetGeometryAtLocalOrigin(_targetModelRoot);
                Repaint();
                SceneView.RepaintAll();
            }

            if (switchUnityTool.HasValue && _targetModelRoot != null)
            {
                SetBrushMode(SceneBrushMode.Off);
                Selection.activeGameObject = _targetModelRoot;
                Tools.current = switchUnityTool.Value;
                SceneView.RepaintAll();
            }

            if (requestedMode.HasValue)
            {
                SetBrushMode(requestedMode.Value);
            }

            if (conformScrewsClicked && _targetModelRoot != null)
            {
                EditorApplication.delayCall += () =>
                {
                    if (_targetModelRoot == null) return;
                    int conformed = ExternalModelLevelTool.ConformAllScrewsInTarget(_targetModelRoot);
                    _solverAnalysisText = $"{conformed} vida ve siyah yuvası yüzeye şekillendirildi.";
                    Repaint();
                    SceneView.RepaintAll();
                };
            }

            if (resetRotClicked)
            {
                ResetTargetModelRotation();
            }

            if (focusClicked && _targetModelRoot != null)
            {
                Selection.activeGameObject = _targetModelRoot;
                SceneView.FrameLastActiveSceneView();
            }

            if (distributeDifficulty.HasValue)
            {
                LevelDifficulty picked = distributeDifficulty.Value;
                EditorApplication.delayCall += () => RunAutoDistributeDifficulty(picked);
            }
            else if (approveClicked)
            {
                EditorApplication.delayCall += ApproveAndSaveWorkbench;
            }
            else if (cancelClicked)
            {
                EditorApplication.delayCall += CancelWorkbench;
            }
        }

        private void HandleManualBrushSceneGUI(bool pointerOverHud)
        {
            Event e = Event.current;
            if (e == null) return;

            bool rotateOnly = _brushMode == SceneBrushMode.RotateOnly;
            bool triggerActive = rotateOnly || _brushSingleClickNoShift || e.shift;
            bool wantsDefaultControl = (rotateOnly || _brushSingleClickNoShift) && !e.alt;

            if (e.type == EventType.Layout)
            {
                _sceneBrushControlId = GUIUtility.GetControlID(FocusType.Passive);
                if (!pointerOverHud && wantsDefaultControl)
                {
                    HandleUtility.AddDefaultControl(_sceneBrushControlId);
                }
            }

            // MouseUp yakalama (ekran dışı dahil, HUD üstüne sürüklense bile sürüklemeyi temiz sonlandır)
            if ((e.rawType == EventType.MouseUp || e.type == EventType.MouseUp) && e.button == 0 && _sceneMouseDown)
            {
                bool wasTap = !_sceneDragStarted;
                Vector2 releasePos = e.mousePosition;
                _sceneMouseDown = false;
                _sceneDragStarted = false;
                if (GUIUtility.hotControl == _sceneBrushControlId) GUIUtility.hotControl = 0;

                if (wasTap && !rotateOnly && !pointerOverHud && !e.alt && triggerActive)
                {
                    e.Use();
                    ExecuteBrushTapAt(HandleUtility.GUIPointToWorldRay(releasePos));
                }
                else if (!wasTap || rotateOnly)
                {
                    e.Use();
                }
                return;
            }

            if (pointerOverHud && !_sceneMouseDown) return;

            // Sürükle-Döndür modu açıksa: Sol Tık + Sürükle modeli oyundaki PuzzleRotator gibi 360° çevirir
            bool dragRotateEnabled = (rotateOnly || (_brushDragToRotateModel && _brushSingleClickNoShift)) && !e.alt;
            if (dragRotateEnabled && e.type == EventType.MouseDrag && e.button == 0 && _sceneMouseDown)
            {
                float dragThreshold = rotateOnly ? 0f : 10f;
                if (!_sceneDragStarted && Vector2.Distance(e.mousePosition, _sceneMouseDownPos) > dragThreshold)
                {
                    _sceneDragStarted = true;
                    if (_targetModelRoot != null)
                    {
                        Undo.RecordObject(_targetModelRoot.transform, "Rotate Model Preview");
                    }
                }

                if (_sceneDragStarted && _targetModelRoot != null)
                {
                    Camera cam = SceneView.currentDrawingSceneView != null ? SceneView.currentDrawingSceneView.camera : null;
                    if (cam != null)
                    {
                        const float sensitivity = 0.45f;
                        _targetModelRoot.transform.Rotate(cam.transform.right, -e.delta.y * sensitivity, Space.World);
                        _targetModelRoot.transform.Rotate(cam.transform.up, -e.delta.x * sensitivity, Space.World);
                        Physics.SyncTransforms();
                        SceneView.RepaintAll();
                    }
                    e.Use();
                }
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && triggerActive)
            {
                if (dragRotateEnabled)
                {
                    _sceneMouseDown = true;
                    _sceneDragStarted = rotateOnly;
                    _sceneMouseDownPos = e.mousePosition;
                    if (rotateOnly && _targetModelRoot != null)
                    {
                        Undo.RecordObject(_targetModelRoot.transform, "Rotate Model Preview");
                    }
                    GUIUtility.hotControl = _sceneBrushControlId;
                    e.Use();
                    return;
                }

                if (!rotateOnly)
                {
                    e.Use();
                    ExecuteBrushTapAt(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                }
                return;
            }

            if (_sceneDragStarted || rotateOnly) return;

            Ray hoverRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (_brushMode == SceneBrushMode.PlaceScrew)
            {
                DrawSurfacePlacementPreview(hoverRay, _brushColor, $"+ {_brushColor}");
            }
            else if (_brushMode == SceneBrushMode.PaintColor || _brushMode == SceneBrushMode.DeleteScrew)
            {
                if (TryRaycastScrew(hoverRay, out ScrewObject hitScrew))
                {
                    bool isDelete = _brushMode == SceneBrushMode.DeleteScrew;
                    Vector3 pos = hitScrew.transform.position + hitScrew.transform.forward * 0.11f;
                    Handles.color = isDelete ? Color.red : _brushColor.ToUnityColor();
                    Handles.DrawWireDisc(pos, hitScrew.transform.forward, 0.12f);
                    Handles.Label(pos + hitScrew.transform.forward * 0.16f, isDelete ? "✕ SİL" : $"🎨 -> {_brushColor}", EditorStyles.whiteBoldLabel);
                    HandleUtility.Repaint();
                }
            }
            else if (_brushMode == SceneBrushMode.RelocateLocked)
            {
                if (TryRaycastFrontScrew(hoverRay, out ScrewObject hitScrew, out RaycastHit surfaceHit, out bool hasSurface))
                {
                    Vector3 pos = hitScrew.transform.position + hitScrew.transform.forward * 0.11f;
                    Handles.color = new Color(1f, 0.80f, 0.25f, 1f);
                    Handles.DrawWireDisc(pos, hitScrew.transform.forward, 0.125f);
                    Handles.Label(pos + hitScrew.transform.forward * 0.16f, $"✂ {hitScrew.Color} Sök & Taşı", EditorStyles.whiteBoldLabel);
                    HandleUtility.Repaint();
                }
                else if (hasSurface)
                {
                    Dictionary<ColorType, int> currentCounts = CollectColorCounts(_targetModelRoot);
                    int totalDebt = GetTotalRelocateDebt(currentCounts);
                    if (totalDebt > 0)
                    {
                        ColorType placeColor = GetEffectiveRelocateColor(currentCounts);
                        int rem = GetRelocateDebt(placeColor, currentCounts);
                        DrawSurfacePlacementPreview(surfaceHit, placeColor, $"+ {placeColor} ({rem} kaldı)");
                    }
                    else
                    {
                        Handles.color = new Color(0.95f, 0.55f, 0.25f, 0.9f);
                        Handles.DrawWireDisc(surfaceHit.point, surfaceHit.normal, 0.095f);
                        Handles.Label(surfaceHit.point + surfaceHit.normal * 0.22f, "🔒 Önce taşıyacağın vidayı tıkla-sök", EditorStyles.whiteBoldLabel);
                        HandleUtility.Repaint();
                    }
                }
            }
        }

        private void DrawSurfacePlacementPreview(Ray hoverRay, ColorType previewColor, string normalLabel)
        {
            if (TryRaycastModelSurface(hoverRay, out RaycastHit hit))
            {
                DrawSurfacePlacementPreview(hit, previewColor, normalLabel);
            }
        }

        private void DrawSurfacePlacementPreview(RaycastHit hit, ColorType previewColor, string normalLabel)
        {
            var seating = ExternalModelLevelTool.AnalyzeSurfaceSeating(hit, _targetModelRoot);
            bool edgeRisk = !seating.IsFlushEnough;
            Vector3 previewNormal = seating.SmoothedNormal;

            Handles.color = edgeRisk ? new Color(1f, 0.45f, 0.1f, 1f) : previewColor.ToUnityColor();
            Handles.DrawWireDisc(hit.point, previewNormal, 0.105f);
            Handles.color = edgeRisk ? new Color(1f, 0.35f, 0.1f, 0.28f) : new Color(0.25f, 0.95f, 0.55f, 0.18f);
            Handles.DrawSolidDisc(hit.point, previewNormal, 0.085f);

            Handles.color = edgeRisk ? Color.yellow : Color.green;
            Handles.DrawLine(hit.point, hit.point + previewNormal * 0.28f);
            string hoverLabel = edgeRisk ? $"⚠ Kenar / Havada Kalabilir ({seating.SupportedRimPoints}/4)" : normalLabel;
            Handles.Label(hit.point + previewNormal * 0.30f, hoverLabel, EditorStyles.whiteBoldLabel);
            HandleUtility.Repaint();
        }

        private void ExecuteBrushTapAt(Ray ray)
        {
            if (_brushMode == SceneBrushMode.PlaceScrew)
            {
                if (TryRaycastModelSurface(ray, out RaycastHit hit))
                {
                    ExternalModelLevelTool.PlaceScrewAtWorldSurface(
                        hit,
                        null,
                        _brushColor,
                        _brushFineOffset,
                        _brushMirrorOpposite,
                        targetRootContext: _targetModelRoot,
                        autoSeatCurvature: true);
                    if (_showBlockerLinksInScene && _targetModelRoot != null)
                    {
                        HeadlessLevelSolver.AutoWirePhysicalDependencies(_targetModelRoot, clearExistingBlockers: false);
                    }
                    Repaint();
                }
            }
            else if (_brushMode == SceneBrushMode.PaintColor || _brushMode == SceneBrushMode.DeleteScrew)
            {
                if (TryRaycastScrew(ray, out ScrewObject hitScrew))
                {
                    if (_brushMode == SceneBrushMode.DeleteScrew)
                    {
                        ExternalModelLevelTool.RemoveScrewCleanly(hitScrew);
                        if (_showBlockerLinksInScene && _targetModelRoot != null)
                        {
                            HeadlessLevelSolver.AutoWirePhysicalDependencies(_targetModelRoot, clearExistingBlockers: false);
                        }
                    }
                    else
                    {
                        ExternalModelLevelTool.PaintScrewColor(hitScrew, _brushColor);
                    }
                    Repaint();
                }
            }
            else if (_brushMode == SceneBrushMode.RelocateLocked)
            {
                if (TryRaycastFrontScrew(ray, out ScrewObject hitScrew, out RaycastHit surfaceHit, out bool hasSurface))
                {
                    ColorType removedColor = hitScrew.Color;
                    ExternalModelLevelTool.RemoveScrewCleanly(hitScrew);
                    if (_showBlockerLinksInScene && _targetModelRoot != null)
                    {
                        HeadlessLevelSolver.AutoWirePhysicalDependencies(_targetModelRoot, clearExistingBlockers: false);
                    }
                    _brushColor = removedColor;
                    Dictionary<ColorType, int> newCounts = CollectColorCounts(_targetModelRoot);
                    _solverAnalysisText = $"{removedColor} vidası yerinden söküldü. Düzeltme Modu aktif: şimdi yalnızca {FormatRelocateDebtSummary(newCounts)} yerleştirilebilir.";
                    Repaint();
                    SceneView.RepaintAll();
                }
                else if (hasSurface)
                {
                    Dictionary<ColorType, int> currentCounts = CollectColorCounts(_targetModelRoot);
                    if (GetTotalRelocateDebt(currentCounts) == 0)
                    {
                        _solverAnalysisText = "Düzeltme Modu Kilitli: Sayı ve renk dengesini (%4) korumak için yeni yere koymadan önce yerini değiştirmek istediğiniz mevcut bir vidayı tıklayıp silin.";
                        Repaint();
                        return;
                    }

                    ColorType placeColor = GetEffectiveRelocateColor(currentCounts);
                    ExternalModelLevelTool.PlaceScrewAtWorldSurface(
                        surfaceHit,
                        null,
                        placeColor,
                        _brushFineOffset,
                        mirrorOppositeSide: false,
                        targetRootContext: _targetModelRoot,
                        autoSeatCurvature: true);

                    if (_showBlockerLinksInScene && _targetModelRoot != null)
                    {
                        HeadlessLevelSolver.AutoWirePhysicalDependencies(_targetModelRoot, clearExistingBlockers: false);
                    }

                    Dictionary<ColorType, int> afterCounts = CollectColorCounts(_targetModelRoot);
                    int remainingDebt = GetTotalRelocateDebt(afterCounts);
                    if (remainingDebt > 0)
                    {
                        ColorType? nextColor = GetFirstPendingDebtColor(afterCounts);
                        if (nextColor.HasValue) _brushColor = nextColor.Value;
                        _solverAnalysisText = $"{placeColor} yerleştirildi. Bekleyen taşıma: {FormatRelocateDebtSummary(afterCounts)}.";
                    }
                    else
                    {
                        _solverAnalysisText = $"{placeColor} yeni yerine yerleştirildi — renk ve %4 dengesi eksiksiz korundu ✓";
                    }

                    Repaint();
                    SceneView.RepaintAll();
                }
            }
        }

        private bool TryRaycastModelSurface(Ray ray, out RaycastHit bestHit)
        {
            return ExternalModelLevelTool.TryRaycastVisibleModelSurface(ray, _targetModelRoot, out bestHit);
        }

        private bool TryRaycastFrontScrew(Ray ray, out ScrewObject frontScrew, out RaycastHit surfaceHit, out bool hasSurface)
        {
            bool hasScrew = TryRaycastScrew(ray, out frontScrew, out float screwDist);
            hasSurface = TryRaycastModelSurface(ray, out surfaceHit);
            if (!hasScrew) return false;
            if (hasSurface && screwDist > surfaceHit.distance + 0.08f)
            {
                frontScrew = null;
                return false;
            }
            return true;
        }

        private bool TryRaycastScrew(Ray ray, out ScrewObject bestScrew)
        {
            return TryRaycastScrew(ray, out bestScrew, out _);
        }

        private bool TryRaycastScrew(Ray ray, out ScrewObject bestScrew, out float minDist)
        {
            bestScrew = null;
            RaycastHit[] hits = Physics.RaycastAll(ray, 200f);
            minDist = float.MaxValue;

            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                ScrewObject s = h.collider.GetComponentInParent<ScrewObject>();
                if (s == null) continue;
                if (_targetModelRoot != null && !s.transform.IsChildOf(_targetModelRoot.transform)) continue;
                if (SceneVisibilityManager.instance.IsHidden(s.gameObject) ||
                    (s.ParentPlate != null && SceneVisibilityManager.instance.IsHidden(s.ParentPlate.gameObject)))
                {
                    continue;
                }

                if (h.distance < minDist)
                {
                    minDist = h.distance;
                    bestScrew = s;
                }
            }
            return bestScrew != null;
        }

        #endregion

        #region Utilities

        private static Dictionary<ColorType, int> CollectColorCounts(GameObject root)
        {
            Dictionary<ColorType, int> dict = new();
            if (root == null) return dict;
            ScrewObject[] screws = root.GetComponentsInChildren<ScrewObject>(true);
            foreach (ScrewObject s in screws)
            {
                if (s == null) continue;
                if (!dict.ContainsKey(s.Color)) dict[s.Color] = 0;
                dict[s.Color]++;
            }
            return dict;
        }

        private void EnsureSavedLevelsCache()
        {
            bool reloaded = false;
            if (_savedLevelsDirty)
            {
                _cachedSavedLevels.Clear();
                _cachedSavedLevels.AddRange(ExternalModelLevelTool.GetSortedSavedLevels(includeLevelManagerExtras: true));
                _savedLevelsDirty = false;
                reloaded = true;
            }

            if (_cachedSavedLevels.Count == 0)
            {
                _selectedSavedLevelIdx = 0;
                return;
            }

            if ((reloaded || _lastNavigatorTargetRoot != _targetModelRoot) && _targetModelRoot != null && !IsWorkbenchActive)
            {
                _lastNavigatorTargetRoot = _targetModelRoot;
                GameObject sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(_targetModelRoot);
                for (int i = 0; i < _cachedSavedLevels.Count; i++)
                {
                    if ((sourcePrefab != null && _cachedSavedLevels[i].Prefab == sourcePrefab) ||
                        string.Equals(_cachedSavedLevels[i].Prefab.name, _targetModelRoot.name, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedSavedLevelIdx = i;
                        break;
                    }
                }
            }

            _selectedSavedLevelIdx = Mathf.Clamp(_selectedSavedLevelIdx, 0, _cachedSavedLevels.Count - 1);
        }

        private void DrawActiveLevelNavigatorBar()
        {
            EnsureSavedLevelsCache();

            EditorGUI.BeginDisabledGroup(Application.isPlaying);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("📂 Sahne İnceleme:", EditorStyles.boldLabel, GUILayout.Width(118));

            ExternalModelLevelTool.SavedLevelEntry? queuedLoad = null;

            if (_cachedSavedLevels.Count == 0)
            {
                GUILayout.Label("Kayıtlı level yok", EditorStyles.miniLabel);
            }
            else
            {
                string[] labels = new string[_cachedSavedLevels.Count];
                for (int i = 0; i < _cachedSavedLevels.Count; i++)
                {
                    var e = _cachedSavedLevels[i];
                    string numTag = e.LevelNumber < 1000 ? $"Lvl {e.LevelNumber:00}" : "Özel";
                    labels[i] = $"{numTag} — {e.Title} ({e.ScrewCount}v)";
                }

                if (GUILayout.Button("◀", GUILayout.Width(24), GUILayout.Height(20)))
                {
                    _selectedSavedLevelIdx = (_selectedSavedLevelIdx - 1 + _cachedSavedLevels.Count) % _cachedSavedLevels.Count;
                    queuedLoad = _cachedSavedLevels[_selectedSavedLevelIdx];
                }

                int nextIdx = EditorGUILayout.Popup(_selectedSavedLevelIdx, labels, GUILayout.MinWidth(130));
                if (nextIdx != _selectedSavedLevelIdx)
                {
                    _selectedSavedLevelIdx = nextIdx;
                    queuedLoad = _cachedSavedLevels[_selectedSavedLevelIdx];
                }

                if (GUILayout.Button("▶", GUILayout.Width(24), GUILayout.Height(20)))
                {
                    _selectedSavedLevelIdx = (_selectedSavedLevelIdx + 1) % _cachedSavedLevels.Count;
                    queuedLoad = _cachedSavedLevels[_selectedSavedLevelIdx];
                }

                if (GUILayout.Button("Sahnede Aç", GUILayout.Width(76), GUILayout.Height(20)))
                {
                    queuedLoad = _cachedSavedLevels[_selectedSavedLevelIdx];
                }
            }

            bool rotateOn = _brushMode == SceneBrushMode.RotateOnly;
            GUI.backgroundColor = rotateOn ? new Color(0.32f, 0.78f, 0.96f) : Color.white;
            if (GUILayout.Button(rotateOn ? "🔄 Açık" : "🔄 Döndür", GUILayout.Width(72), GUILayout.Height(20)))
            {
                SetBrushMode(rotateOn ? SceneBrushMode.Off : SceneBrushMode.RotateOnly);
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("↻", GUILayout.Width(24), GUILayout.Height(20)))
            {
                _savedLevelsDirty = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            if (queuedLoad.HasValue)
            {
                var targetEntry = queuedLoad.Value;
                EditorApplication.delayCall += () =>
                {
                    LoadLevelPrefabIntoPuzzleRoot(targetEntry.Prefab, targetEntry.LevelNumber);
                    Repaint();
                };
            }
        }

        private void LoadLevelPrefabIntoPuzzleRoot(int levelNumber)
        {
            string path = $"{ParametricLevelArchitect.LevelsFolder}/Level_{levelNumber:00}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Bulunamadı", $"{path} henüz üretilmemiş. Önce üstteki yeşil butona basın.", "Tamam");
                return;
            }

            LoadLevelPrefabIntoPuzzleRoot(prefab, levelNumber);
        }

        private void LoadLevelPrefabIntoPuzzleRoot(GameObject prefab, int fallbackLevelNumber = -1)
        {
            if (Application.isPlaying || prefab == null) return;

            if (IsWorkbenchActive)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Açık Tezgah Var",
                    "Açık olan tasarım tezgahı kapatılıp seçili level sahnede açılsın mı? (Kaydedilmemiş değişiklikler silinir.)",
                    "Evet, Leveli Aç",
                    "Vazgeç");
                if (!proceed) return;
            }

            GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
            if (puzzleRoot == null) return;

            for (int i = puzzleRoot.transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(puzzleRoot.transform.GetChild(i).gameObject);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, puzzleRoot.transform);
            instance.transform.localPosition = prefab.transform.localPosition;
            instance.transform.localRotation = Quaternion.identity;
            _targetModelRoot = instance;
            SyncTargetMetadataAndQueueIfChanged();

            LevelManager lm = FindFirstObjectByType<LevelManager>();
            if (lm != null)
            {
                SerializedObject lmSo = new SerializedObject(lm);
                SerializedProperty listProp = lmSo.FindProperty("_levelPrefabs");
                int matchedIdx = -1;
                if (listProp != null)
                {
                    for (int i = 0; i < listProp.arraySize; i++)
                    {
                        if (listProp.GetArrayElementAtIndex(i).objectReferenceValue == prefab)
                        {
                            matchedIdx = i;
                            break;
                        }
                    }
                }
                if (matchedIdx < 0 && fallbackLevelNumber > 0)
                {
                    matchedIdx = fallbackLevelNumber - 1;
                }
                if (matchedIdx >= 0)
                {
                    lmSo.FindProperty("_currentLevelIndex").intValue = matchedIdx;
                    lmSo.ApplyModifiedProperties();
                    EditorUtility.SetDirty(lm);
                }
            }

            EditorSceneManager.MarkSceneDirty(puzzleRoot.scene);
            Selection.activeGameObject = instance;
            SceneView.RepaintAll();
        }

        #endregion
    }
}
