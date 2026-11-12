using System;
using System.Collections.Generic;
using DG.Tweening;
using ScrewSolver.Core;
using ScrewSolver.Gameplay.Controllers;
using ScrewSolver.Gameplay.Screws;
using UnityEngine;

namespace ScrewSolver.Gameplay.Boxes
{
    /// <summary>
    /// Seviyedeki tüm kutuların (Örn: 32 vida için 8 kutu) sırayla ekrana gelmesini yönetir.
    /// Ekranda aynı anda sabit 3 aktif yuva (Slot) bulunur.
    /// Bir kutu dolup uçtuğunda sıradaki kutu yukarıdan tatlı bir yaylanma (Ease.OutBack) ile boş yuvaya iner.
    /// Kutu bittiğinde yeni kutuların akmasını ve tüm vidalarla %100 senkronize olmasını sağlar.
    /// </summary>
    [AddComponentMenu("ScrewSolver/Box Queue Manager")]
    public sealed class BoxQueueManager : MonoBehaviour
    {
        [Header("Ekranda Görünen Aktif Yuvalar (3 Slot)")]
        [Tooltip("Kutuların ekranda oturacağı 3 sabit yuva (Sol, Orta, Sağ)")]
        [SerializeField] private List<Transform> _boxSlots = new();

        [Header("Kutu Prefab'ı")]
        [Tooltip("Sahneye indirilecek TargetBox prefab'ı")]
        [SerializeField] private GameObject _targetBoxPrefab;

        [Header("Seviye Kutu Kuyruğu")]
        [Tooltip("Bölüm boyunca sırayla gelecek kutu renklerinin tam listesi")]
        [SerializeField] private List<ColorType> _boxColorQueue = new();

        [Header("Otomatik Kuyruk Üretimi")]
        [Tooltip("Başlangıçta sahnedeki tüm vidaları tarayıp kuyruğu otomatik üret")]
        [SerializeField] private bool _autoBuildQueueFromScrews = true;

        [Header("Geliş Animasyonu")]
        [Tooltip("Yeni kutunun yukarıdan iniş süresi")]
        [SerializeField, Range(0.2f, 1.0f)] private float _dropDuration = 0.45f;

        [Tooltip("Yeni kutunun yerel Y eksenindeki iniş yüksekliği")]
        [SerializeField] private float _dropLocalYOffset = 0.35f;

        public event Action OnAllBoxesCompleted;

        private readonly Queue<ColorType> _pendingQueue = new();
        private readonly ColorBox[] _activeBoxes = new ColorBox[3];
        private ScrewManager _screwManager;
        private int _totalBoxesCount;
        private int _completedBoxesCount;

        private void Awake()
        {
            _screwManager = FindFirstObjectByType<ScrewManager>();

            if (_autoBuildQueueFromScrews || _boxColorQueue == null || _boxColorQueue.Count == 0)
            {
                AutoPopulateQueueFromScrews();
            }

            InitializeQueue();
        }

        /// <summary>
        /// Sahnedeki veya verilen listedeki vidaları analiz eder, her rengin toplam vida sayısını ve unblocked (açık) durumunu
        /// tespit ederek çözülebilir, kilitlenmeyen akıllı bir kutu kuyruğu üretir.
        /// </summary>
        [ContextMenu("Sahnedeki Vidalardan Kutu Kuyruğu Üret")]
        public void AutoPopulateQueueFromScrews(ScrewObject[] targetScrews = null)
        {
            ScrewObject[] allScrews = targetScrews;
            if (allScrews == null || allScrews.Length == 0)
            {
                allScrews = FindObjectsByType<ScrewObject>(FindObjectsSortMode.None);
            }
            if (allScrews == null || allScrews.Length == 0) return;

            Dictionary<ColorType, int> totalCount = new();
            Dictionary<ColorType, int> unblockedCount = new();

            foreach (var screw in allScrews)
            {
                if (screw == null || screw.IsUnscrewed) continue;
                if (!totalCount.ContainsKey(screw.Color))
                {
                    totalCount[screw.Color] = 0;
                    unblockedCount[screw.Color] = 0;
                }
                totalCount[screw.Color]++;
                if (!screw.IsBlocked)
                {
                    unblockedCount[screw.Color]++;
                }
            }

            const int boxCapacity = 4;
            List<ColorType> generatedQueue = new();

            // İlk 3 kutu için başlangıçta en çok açık vidası olan renkleri öne al
            List<ColorType> sortedColors = new(totalCount.Keys);
            sortedColors.Sort((a, b) => unblockedCount[b].CompareTo(unblockedCount[a]));

            // Her renk için 1 kutuyu başlangıç önceliğine ekle
            List<ColorType> remainingBoxes = new();
            foreach (var color in sortedColors)
            {
                int count = totalCount[color];
                int boxesNeeded = Mathf.CeilToInt((float)count / boxCapacity);

                if (boxesNeeded > 0)
                {
                    generatedQueue.Add(color); // 1. kutu öncelikli
                    for (int i = 1; i < boxesNeeded; i++)
                    {
                        remainingBoxes.Add(color); // Sonraki kutular kuyruğa
                    }
                }
            }

            // Geri kalan kutuları da ekle
            generatedQueue.AddRange(remainingBoxes);
            _boxColorQueue = generatedQueue;

            Debug.Log($"<color=#00e5ff><b>[BoxQueueManager]</b> {allScrews.Length} vida için {_boxColorQueue.Count} kutuluk dinamik kuyruk oluşturuldu: " +
                      $"{string.Join(" ➔ ", _boxColorQueue)}</color>");
        }

        /// <summary>
        /// Kutu kuyruğunu başlatır ve ilk 3 kutuyu yuvalara indirir.
        /// </summary>
        public void InitializeQueue(List<ColorType> customQueue = null)
        {
            if (customQueue != null)
            {
                _boxColorQueue = new List<ColorType>(customQueue);
            }

            _pendingQueue.Clear();
            _completedBoxesCount = 0;
            _totalBoxesCount = _boxColorQueue.Count;

            for (int i = 0; i < _boxColorQueue.Count; i++)
            {
                _pendingQueue.Enqueue(_boxColorQueue[i]);
            }

            // İlk 3 yuvayı temizle ve doldur
            for (int slotIdx = 0; slotIdx < _boxSlots.Count; slotIdx++)
            {
                CleanSlot(slotIdx);
                if (_pendingQueue.Count > 0)
                {
                    SpawnBoxAtSlot(slotIdx, isImmediate: true);
                }
            }

            SyncWithScrewManager();
        }

        private void CleanSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _boxSlots.Count) return;
            Transform slot = _boxSlots[slotIndex];
            if (slot == null) return;

            // Slot altındaki eski kutuları ve editör önizlemelerini temizle
            for (int i = slot.childCount - 1; i >= 0; i--)
            {
                Transform child = slot.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
            _activeBoxes[slotIndex] = null;
        }

        private void SpawnBoxAtSlot(int slotIndex, bool isImmediate)
        {
            if (slotIndex < 0 || slotIndex >= _boxSlots.Count) return;
            if (_pendingQueue.Count == 0) return;

            Transform slot = _boxSlots[slotIndex];
            if (slot == null || _targetBoxPrefab == null) return;

            ColorType nextColor = _pendingQueue.Dequeue();

            GameObject boxObj = Instantiate(_targetBoxPrefab, slot);
            boxObj.transform.localPosition = Vector3.zero;
            boxObj.transform.localRotation = Quaternion.identity;
            boxObj.transform.localScale = Vector3.one;

            ColorBox box = boxObj.GetComponent<ColorBox>();
            if (box != null)
            {
                box.SetColor(nextColor);
                box.OnBoxCompleted += (completedBox) => HandleBoxCompleted(slotIndex, completedBox);
                _activeBoxes[slotIndex] = box;
            }

            // Geliş animasyonu (İlk açılış değilse kameraya göre yukarıdan tatlı bir iniş)
            if (!isImmediate)
            {
                Vector3 targetWorldPos = slot.position;
                Vector3 dropDir = Camera.main != null ? Camera.main.transform.up : Vector3.up;
                boxObj.transform.position = targetWorldPos + dropDir * 1.5f;
                boxObj.transform.localScale = Vector3.zero;

                Sequence seq = DOTween.Sequence();
                seq.Append(boxObj.transform.DOMove(targetWorldPos, _dropDuration).SetEase(Ease.OutBack));
                seq.Join(boxObj.transform.DOScale(Vector3.one, _dropDuration * 0.8f).SetEase(Ease.OutBack));
            }

            SyncWithScrewManager();
        }

        private void HandleBoxCompleted(int slotIndex, ColorBox box)
        {
            _completedBoxesCount++;
            _activeBoxes[slotIndex] = null;

            if (box != null)
            {
                Destroy(box.gameObject);
            }

            SyncWithScrewManager();

            // Sırada bekleyen başka kutu var mı?
            if (_pendingQueue.Count > 0)
            {
                // Boşalan yuvaya yeni kutuyu indir
                DOVirtual.DelayedCall(0.12f, () =>
                {
                    SpawnBoxAtSlot(slotIndex, isImmediate: false);
                });
            }
            else
            {
                // Kuyruk bitti, sahnedeki vidalar ve kutular kontrol edilsin
                CheckAllCompleted();
            }
        }

        private void CheckAllCompleted()
        {
            // 1. Sahnede hala sökülmemiş vida var mı?
            ScrewObject[] remainingScrews = FindObjectsByType<ScrewObject>(FindObjectsSortMode.None);
            List<ScrewObject> activeScrews = new();
            foreach (var s in remainingScrews)
            {
                if (s != null && !s.IsUnscrewed)
                {
                    activeScrews.Add(s);
                }
            }

            // Eğer sökülmemiş vida kaldıysa ama kutu yoksa, kalan vidalar için anında yeni kutuları indir!
            if (activeScrews.Count > 0)
            {
                Debug.LogWarning($"<color=yellow>[BoxQueueManager] Sahnede hala {activeScrews.Count} adet sökülmemiş vida var! Kalanlar için kutular kuyruğa ekleniyor...</color>");
                AutoPopulateQueueFromScrews();
                for (int slotIdx = 0; slotIdx < _boxSlots.Count; slotIdx++)
                {
                    if (_activeBoxes[slotIdx] == null && _pendingQueue.Count > 0)
                    {
                        SpawnBoxAtSlot(slotIdx, isImmediate: false);
                    }
                }
                return;
            }

            bool anyActive = false;
            for (int i = 0; i < _activeBoxes.Length; i++)
            {
                if (_activeBoxes[i] != null && !_activeBoxes[i].IsComplete)
                {
                    anyActive = true;
                    break;
                }
            }

            if (!anyActive && _pendingQueue.Count == 0 && activeScrews.Count == 0)
            {
                Debug.Log($"<color=#00e5ff><b>🎉 [BoxQueueManager] BÖLÜM TAMAMLANDI! Tüm kutular ve vidalar ({_totalBoxesCount}/{_totalBoxesCount}) doldu ve temizlendi!</b></color>");
                OnAllBoxesCompleted?.Invoke();
            }
        }

        private void SyncWithScrewManager()
        {
            if (_screwManager == null)
            {
                _screwManager = FindFirstObjectByType<ScrewManager>();
            }

            if (_screwManager != null)
            {
                List<ColorBox> activeList = new List<ColorBox>();
                for (int i = 0; i < _activeBoxes.Length; i++)
                {
                    if (_activeBoxes[i] != null && !_activeBoxes[i].IsFull)
                    {
                        activeList.Add(_activeBoxes[i]);
                    }
                }
                _screwManager.UpdateActiveBoxes(activeList);
            }
        }

        /// <summary>
        /// Editörde (Scene View) kutuların tam nerede duracağını canlı görmek ve elle yerlerini ayarlamak için
        /// 3 yuvaya gerçek TargetBox modellerini yerleştirir.
        /// </summary>
        [ContextMenu("Scene'de Canlı Önizleme Kutularını Göster (Show Previews)")]
        public void SpawnEditorPreviews()
        {
#if UNITY_EDITOR
            if (_targetBoxPrefab == null || _boxSlots == null) return;
            ClearEditorPreviews();

            ColorType[] previewColors = new ColorType[] { ColorType.Yellow, ColorType.Blue, ColorType.Pink };

            for (int i = 0; i < _boxSlots.Count; i++)
            {
                Transform slot = _boxSlots[i];
                if (slot == null) continue;

                GameObject previewObj = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(_targetBoxPrefab, slot);
                previewObj.name = $"TargetBox_Preview_{i}";
                previewObj.transform.localPosition = Vector3.zero;
                previewObj.transform.localRotation = Quaternion.identity;
                previewObj.transform.localScale = Vector3.one;

                ColorBox cb = previewObj.GetComponent<ColorBox>();
                if (cb != null)
                {
                    ColorType c = i < previewColors.Length ? previewColors[i] : ColorType.Yellow;
                    cb.SetColor(c);
                }
            }
            UnityEditor.EditorUtility.SetDirty(gameObject);
            Debug.Log("<color=#00ff88><b>[BoxQueueManager] 3 Kutu önizlemesi Scene ekranında açıldı! Artık BoxSlot_0, 1, 2 veya HUD_Boxes nesnelerini W tuşuyla serbestçe taşıyabilirsiniz.</b></color>");
#endif
        }

        /// <summary>
        /// Scene ekranındaki geçici önizleme kutularını siler.
        /// </summary>
        [ContextMenu("Scene'deki Önizleme Kutularını Temizle (Clear Previews)")]
        public void ClearEditorPreviews()
        {
            if (_boxSlots == null) return;
            for (int i = 0; i < _boxSlots.Count; i++)
            {
                Transform slot = _boxSlots[i];
                if (slot == null) continue;

                for (int j = slot.childCount - 1; j >= 0; j--)
                {
                    Transform child = slot.GetChild(j);
                    if (child.name.Contains("Preview") || child.name.Contains("TargetBox"))
                    {
                        if (Application.isPlaying)
                            Destroy(child.gameObject);
                        else
                            DestroyImmediate(child.gameObject);
                    }
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_boxSlots == null) return;

            Color[] slotColors = new Color[]
            {
                new Color(1f, 0.85f, 0.2f, 0.7f), // Sarı
                new Color(0.2f, 0.75f, 1f, 0.7f), // Mavi
                new Color(0.95f, 0.4f, 0.8f, 0.7f) // Pembe
            };

            Vector3 boxDimensions = new Vector3(0.26f, 0.12f, 0.26f);

            for (int i = 0; i < _boxSlots.Count; i++)
            {
                Transform slot = _boxSlots[i];
                if (slot == null) continue;

                Gizmos.color = i < slotColors.Length ? slotColors[i] : Color.cyan;

                Matrix4x4 oldMatrix = Gizmos.matrix;
                Gizmos.matrix = slot.localToWorldMatrix;
                Gizmos.DrawWireCube(Vector3.zero, boxDimensions);
                Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.25f);
                Gizmos.DrawCube(Vector3.zero, boxDimensions);
                Gizmos.matrix = oldMatrix;

                UnityEditor.Handles.color = Color.white;
                GUIStyle style = new GUIStyle();
                style.normal.textColor = Color.white;
                style.fontSize = 11;
                style.fontStyle = FontStyle.Bold;
                style.alignment = TextAnchor.MiddleCenter;
                UnityEditor.Handles.Label(slot.position + slot.up * 0.18f, $"[KUTU {i + 1}]", style);
            }
        }
#endif
    }
}
