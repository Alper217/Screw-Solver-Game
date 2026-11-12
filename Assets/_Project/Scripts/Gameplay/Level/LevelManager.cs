using System;
using System.Collections.Generic;
using ScrewSolver.Gameplay.Boxes;
using ScrewSolver.Gameplay.Controllers;
using ScrewSolver.Gameplay.Screws;
using ScrewSolver.Gameplay.Slots;
using ScrewSolver.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScrewSolver.Gameplay.Level
{
    /// <summary>
    /// Modüler Seviye Yöneticisi (LevelManager):
    /// 1. Seviye Prefab'larını (Level_1, Level_2 vb.) sırayla PuzzleRoot içerisine yükler.
    /// 2. Yeni seviye yüklendiğinde BoxQueueManager'ı otomatik tetikler (vidaları sayıp tam uyumlu kuyruk oluşturur).
    /// 3. Bekleme yuvalarını (WaitingSlotManager) sıfırlar.
    /// 4. UI ile konuşarak Next Level ve Retry işlemlerini kesintisiz yürütür.
    /// </summary>
    [AddComponentMenu("ScrewSolver/Level Manager")]
    public sealed class LevelManager : MonoBehaviour
    {
        [Header("Bulmaca Kökü (PuzzleRoot)")]
        [SerializeField] private Transform _puzzleRoot;

        [Header("Seviye Prefab Listesi")]
        [Tooltip("Sırayla oynatılacak seviye prefabları. Boşsa mevcut sahneyi yeniden yükler.")]
        [SerializeField] private List<GameObject> _levelPrefabs = new();

        [Header("Mevcut Seviye İndeksi")]
        [SerializeField] private int _currentLevelIndex = 0;

        [Header("Seviye Başına Süre (Saniye)")]
        [SerializeField] private float _defaultLevelDuration = 120f;

        [Header("Bağlantılar")]
        [SerializeField] private ScrewManager _screwManager;
        [SerializeField] private BoxQueueManager _boxQueueManager;
        [SerializeField] private WaitingSlotManager _waitingSlotManager;
        [SerializeField] private GameUIController _gameUI;

        public int CurrentLevelIndex => _currentLevelIndex;
        public int TotalLevelsCount => _levelPrefabs.Count;

        private GameObject _activeLevelInstance;

        private void Awake()
        {
            if (_puzzleRoot == null)
            {
                GameObject rootGo = GameObject.Find("PuzzleRoot");
                if (rootGo != null) _puzzleRoot = rootGo.transform;
            }

            if (_screwManager == null) _screwManager = FindFirstObjectByType<ScrewManager>();
            if (_boxQueueManager == null) _boxQueueManager = FindFirstObjectByType<BoxQueueManager>();
            if (_waitingSlotManager == null) _waitingSlotManager = FindFirstObjectByType<WaitingSlotManager>();
            if (_gameUI == null) _gameUI = FindFirstObjectByType<GameUIController>();

#if UNITY_EDITOR
            if (_levelPrefabs == null || _levelPrefabs.Count == 0)
            {
                _levelPrefabs = new List<GameObject>();
                for (int i = 1; i <= 8; i++)
                {
                    string path = $"Assets/Prefabs/Levels/Level_{i:00}.prefab";
                    GameObject p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (p != null) _levelPrefabs.Add(p);
                }
            }
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeInstance()
        {
            if (FindFirstObjectByType<LevelManager>() == null)
            {
                GameObject go = new GameObject("LevelManager");
                go.AddComponent<LevelManager>();
            }
        }

        private void Update()
        {
            HandleShortcuts();
        }

        private void HandleShortcuts()
        {
            bool nextPressed = false;
            bool prevPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                nextPressed = UnityEngine.InputSystem.Keyboard.current.pageUpKey.wasPressedThisFrame;
                prevPressed = UnityEngine.InputSystem.Keyboard.current.pageDownKey.wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.PageUp)) nextPressed = true;
            if (Input.GetKeyDown(KeyCode.PageDown)) prevPressed = true;
#endif

            if (nextPressed)
            {
                Debug.Log("<color=#00e5ff><b>[LevelManager]</b> [PageUp] tuşlandı -> Sonraki bölüme geçiliyor!</color>");
                LoadNextLevel();
            }
            else if (prevPressed)
            {
                Debug.Log("<color=#00e5ff><b>[LevelManager]</b> [PageDown] tuşlandı -> Önceki bölüme geçiliyor!</color>");
                LoadPreviousLevel();
            }
        }

        private void OnEnable()
        {
            if (_gameUI != null)
            {
                _gameUI.OnNextLevelRequested += LoadNextLevel;
                _gameUI.OnRetryRequested += RestartCurrentLevel;
            }
        }

        private void OnDisable()
        {
            if (_gameUI != null)
            {
                _gameUI.OnNextLevelRequested -= LoadNextLevel;
                _gameUI.OnRetryRequested -= RestartCurrentLevel;
            }
        }

        private void Start()
        {
            if (_levelPrefabs.Count > 0)
            {
                LoadLevel(_currentLevelIndex);
            }
            else
            {
                // Sahnedeki mevcut seviyeyi başlat
                if (_gameUI != null)
                {
                    _gameUI.SetLevelNumber(_currentLevelIndex + 1);
                    _gameUI.StartLevelTimer(_defaultLevelDuration);
                }
            }
        }

        /// <summary>
        /// Belirtilen indeksteki seviyeyi yükler.
        /// </summary>
        public void LoadLevel(int levelIndex)
        {
            if (_levelPrefabs == null || _levelPrefabs.Count == 0)
            {
                Debug.LogWarning("[LevelManager] Yüklenecek seviye prefabı tanımlı değil! Mevcut sahne yeniden başlatılıyor.");
                RestartCurrentLevel();
                return;
            }

            _currentLevelIndex = Mathf.Clamp(levelIndex, 0, _levelPrefabs.Count - 1);

            // 0. Seçili vida varsa temizle
            if (_screwManager != null)
            {
                _screwManager.DeselectCurrentScrew();
            }

            // 1. Önceki seviyeyi temizle
            if (_puzzleRoot != null)
            {
                for (int i = _puzzleRoot.childCount - 1; i >= 0; i--)
                {
                    Destroy(_puzzleRoot.GetChild(i).gameObject);
                }
            }
            _activeLevelInstance = null;

            // 2. Yeni seviyeyi PuzzleRoot içine instantiate et
            GameObject prefab = _levelPrefabs[_currentLevelIndex];
            if (prefab != null && _puzzleRoot != null)
            {
                _activeLevelInstance = Instantiate(prefab, _puzzleRoot);
                _activeLevelInstance.transform.localPosition = Vector3.zero;
                _activeLevelInstance.transform.localRotation = Quaternion.identity;
                _activeLevelInstance.transform.localScale = Vector3.one;
            }

            // 3. Bekleme yuvalarını sıfırla
            if (_waitingSlotManager != null)
            {
                _waitingSlotManager.InitializeSlots();
            }

            // 4. BoxQueueManager'ı yeni seviyenin vidalarına göre güncelle
            if (_boxQueueManager != null && _activeLevelInstance != null)
            {
                ScrewObject[] levelScrews = _activeLevelInstance.GetComponentsInChildren<ScrewObject>(true);
                _boxQueueManager.AutoPopulateQueueFromScrews(levelScrews);
                _boxQueueManager.InitializeQueue();
            }

            // 5. UI durumunu güncelle
            if (_gameUI != null)
            {
                _gameUI.HideAllPopupsImmediately();
                _gameUI.SetLevelNumber(_currentLevelIndex + 1);
                _gameUI.StartLevelTimer(_defaultLevelDuration);
            }

            Debug.Log($"<color=#00e5ff><b>[LevelManager]</b> Seviye {_currentLevelIndex + 1} başarıyla yüklendi!</color>");
        }

        /// <summary>
        /// Mevcut seviyeyi baştan başlatır.
        /// </summary>
        public void RestartCurrentLevel()
        {
            if (_levelPrefabs.Count > 0)
            {
                LoadLevel(_currentLevelIndex);
            }
            else
            {
                // Prefab yoksa sahneyi temiz yükle
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        /// <summary>
        /// Sonraki seviyeye geçer (Döngüsel veya son seviyede kalır).
        /// </summary>
        public void LoadNextLevel()
        {
            int nextIndex = _currentLevelIndex + 1;
            if (_levelPrefabs != null && nextIndex < _levelPrefabs.Count)
            {
                LoadLevel(nextIndex);
            }
            else
            {
                Debug.Log("<color=#ffff00><b>[LevelManager]</b> Tüm seviyeler tamamlandı! Başa sarılıyor veya tekrar yükleniyor.</color>");
                LoadLevel(0);
            }
        }

        /// <summary>
        /// Önceki seviyeye geçer (Döngüsel olarak ilk seviyedeyken son seviyeye sarar).
        /// </summary>
        public void LoadPreviousLevel()
        {
            if (_levelPrefabs == null || _levelPrefabs.Count == 0) return;

            int prevIndex = _currentLevelIndex - 1;
            if (prevIndex < 0)
            {
                prevIndex = _levelPrefabs.Count - 1;
            }
            LoadLevel(prevIndex);
        }
    }
}

