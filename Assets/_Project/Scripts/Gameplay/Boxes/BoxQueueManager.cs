using System;
using System.Collections.Generic;
using DG.Tweening;
using ScrewSolver.Core;
using ScrewSolver.Gameplay.Controllers;
using UnityEngine;

namespace ScrewSolver.Gameplay.Boxes
{
    /// <summary>
    /// Seviyedeki tüm kutuların (Örn: 44 vida için 11 kutu) sırayla ekrana gelmesini yönetir.
    /// Ekranda aynı anda sabit 3 aktif yuva (Slot) bulunur.
    /// Bir kutu dolup uçtuğunda sıradaki kutu yukarıdan tatlı bir yaylanma (Ease.OutBack) ile boş yuvaya iner.
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

        [Header("Seviye Kutu Kuyruğu (Örn: 11 Kutu)")]
        [Tooltip("Bölüm boyunca sırayla gelecek kutu renklerinin tam listesi")]
        [SerializeField] private List<ColorType> _boxColorQueue = new();

        [Header("Geliş Animasyonu")]
        [Tooltip("Yeni kutunun yukarıdan iniş süresi")]
        [SerializeField, Range(0.2f, 1.0f)] private float _dropDuration = 0.45f;

        [Tooltip("Yeni kutunun yukarıdan iniş yüksekliği")]
        [SerializeField] private float _dropHeightOffset = 3f;

        public event Action OnAllBoxesCompleted;

        private readonly Queue<ColorType> _pendingQueue = new();
        private readonly ColorBox[] _activeBoxes = new ColorBox[3];
        private ScrewManager _screwManager;
        private int _totalBoxesCount;
        private int _completedBoxesCount;

        private void Awake()
        {
            _screwManager = FindFirstObjectByType<ScrewManager>();
            InitializeQueue();
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

            // İlk 3 yuvayı doldur
            int initialCount = Mathf.Min(_boxSlots.Count, _pendingQueue.Count);
            for (int slotIdx = 0; slotIdx < initialCount; slotIdx++)
            {
                SpawnBoxAtSlot(slotIdx, isImmediate: true);
            }

            SyncWithScrewManager();
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

            ColorBox box = boxObj.GetComponent<ColorBox>();
            if (box != null)
            {
                box.SetColor(nextColor);
                box.OnBoxCompleted += (completedBox) => HandleBoxCompleted(slotIndex, completedBox);
                _activeBoxes[slotIndex] = box;
            }

            // Geliş animasyonu (İlk açılış değilse yukarıdan tatlı bir iniş)
            if (!isImmediate)
            {
                Vector3 targetLocalPos = boxObj.transform.localPosition;
                boxObj.transform.localPosition = targetLocalPos + Vector3.up * _dropHeightOffset;
                boxObj.transform.localScale = Vector3.zero;

                Sequence seq = DOTween.Sequence();
                seq.Append(boxObj.transform.DOLocalMove(targetLocalPos, _dropDuration).SetEase(Ease.OutBack));
                seq.Join(boxObj.transform.DOScale(Vector3.one, _dropDuration * 0.8f).SetEase(Ease.OutBack));
            }

            SyncWithScrewManager();
        }

        private void HandleBoxCompleted(int slotIndex, ColorBox box)
        {
            _completedBoxesCount++;
            _activeBoxes[slotIndex] = null;

            SyncWithScrewManager();

            // Sırada bekleyen başka kutu var mı?
            if (_pendingQueue.Count > 0)
            {
                // Boşalan yuvaya yeni kutuyu indir
                DOVirtual.DelayedCall(0.15f, () =>
                {
                    SpawnBoxAtSlot(slotIndex, isImmediate: false);
                });
            }
            else
            {
                // Kuyruk bitti, sahnedeki diğer kutular da bitti mi kontrol et
                CheckAllCompleted();
            }
        }

        private void CheckAllCompleted()
        {
            bool anyActive = false;
            for (int i = 0; i < _activeBoxes.Length; i++)
            {
                if (_activeBoxes[i] != null && !_activeBoxes[i].IsComplete)
                {
                    anyActive = true;
                    break;
                }
            }

            if (!anyActive && _pendingQueue.Count == 0)
            {
                Debug.Log($"<color=green><b>[BoxQueueManager]</b> Seviyedeki tüm kutular ({_totalBoxesCount}/{_totalBoxesCount}) tamamlandı! Bölüm Zaferi!</color>");
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
    }
}

