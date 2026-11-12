using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using ScrewSolver.Core;
using ScrewSolver.Gameplay.Boxes;
using ScrewSolver.Gameplay.Controllers;
using ScrewSolver.Gameplay.Screws;
using UnityEngine;

namespace ScrewSolver.Gameplay.Slots
{
    /// <summary>
    /// Ekranda uygun renk kutu yokken vidaların geçici olarak park ettiği bekleme deliklerini yönetir.
    /// 1. Uygun kutusu olmayan açık vidalar buraya uçar ve altındaki parçayı serbest bırakır.
    /// 2. Yeni bir kutu geldiğinde, park eden vidalar otomatik olarak havalanıp kutuya uçar.
    /// 3. Tüm delikler dolduğunda ve hamle kalmadığında Game Over (Yenilgi) tetikler.
    /// </summary>
    [AddComponentMenu("ScrewSolver/Waiting Slot Manager")]
    public sealed class WaitingSlotManager : MonoBehaviour
    {
        [Header("Bekleme Yuvaları (Slot Transforms)")]
        [Tooltip("Vidaların park edeceği 5 adet bekleme yuvası")]
        [SerializeField] private List<Transform> _slots = new();

        [Header("Görsel Ayarlar")]
        [SerializeField] private float _slotScale = 1f;

        public event Action OnSlotsFull;
        public event Action OnGameOver;

        private ScrewObject[] _parkedScrews;
        private ScrewManager _screwManager;
        private BoxQueueManager _boxQueueManager;
        private bool _isGameOver = false;

        public int TotalSlotsCount => _slots.Count;
        public bool HasFreeSlot
        {
            get
            {
                if (_parkedScrews == null) return false;
                for (int i = 0; i < _parkedScrews.Length; i++)
                {
                    if (_parkedScrews[i] == null) return true;
                }
                return false;
            }
        }

        public int FreeSlotCount
        {
            get
            {
                if (_parkedScrews == null) return 0;
                int free = 0;
                for (int i = 0; i < _parkedScrews.Length; i++)
                {
                    if (_parkedScrews[i] == null) free++;
                }
                return free;
            }
        }

        private void Awake()
        {
            _screwManager = FindFirstObjectByType<ScrewManager>();
            _boxQueueManager = FindFirstObjectByType<BoxQueueManager>();

            InitializeSlots();
        }

        public void InitializeSlots()
        {
            _isGameOver = false;
            _parkedScrews = new ScrewObject[_slots.Count];
        }

        /// <summary>
        /// İlk boş yuvayı bu vida için rezerve eder.
        /// </summary>
        public Transform ReserveFreeSlot(ScrewObject screw, out int slotIndex)
        {
            slotIndex = -1;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_parkedScrews[i] == null)
                {
                    slotIndex = i;
                    _parkedScrews[i] = screw; // Rezerve et
                    return _slots[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Vida yuvaya inişini tamamladığında çağrılır.
        /// </summary>
        public void ConfirmParkedScrew(int slotIndex, ScrewObject screw)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return;

            _parkedScrews[slotIndex] = screw;

            // Yuva hafifçe yaylansın (tatmin edici iniş efekti)
            if (_slots[slotIndex] != null)
            {
                _slots[slotIndex].DOPunchScale(Vector3.one * 0.12f, 0.2f, 6, 0.5f);
            }

            // Eğer tüm yuvalar dolduysa hamle kontrolü yap
            if (!HasFreeSlot)
            {
                OnSlotsFull?.Invoke();
                CheckForGameOver();
            }
        }

        /// <summary>
        /// Yeni bir kutu açıldığında veya kutular güncellendiğinde çağrılır.
        /// Park etmiş vidaları tarar; uygun kutusu olan vidaları havalandırıp kutuya uçurur.
        /// </summary>
        public void CheckAndTransferParkedScrews(List<ColorBox> activeBoxes)
        {
            if (activeBoxes == null || activeBoxes.Count == 0 || _isGameOver) return;

            for (int i = 0; i < _slots.Count; i++)
            {
                ScrewObject screw = _parkedScrews[i];
                if (screw == null) continue;

                // Bu vidanın rengine uygun kutu var mı?
                ColorBox targetBox = null;
                for (int b = 0; b < activeBoxes.Count; b++)
                {
                    ColorBox box = activeBoxes[b];
                    if (box != null && box.gameObject.activeInHierarchy && !box.IsFull && box.BoxColor == screw.Color)
                    {
                        targetBox = box;
                        break;
                    }
                }

                if (targetBox != null)
                {
                    int slotIdx = i;
                    _parkedScrews[slotIdx] = null; // Yuva boşaldı!

                    Transform socket = targetBox.ReserveSocket();
                    Vector3 dest = socket != null ? socket.position : targetBox.transform.position;
                    Quaternion targetRot = socket != null ? socket.rotation : targetBox.transform.rotation;

                    // Bekleme yuvasından havalanıp kutuya uçuş
                    screw.PlayFlyFromWaitingSlot(dest, targetRot, () =>
                    {
                        screw.transform.SetParent(socket != null ? socket : targetBox.transform, true);
                        screw.transform.position = dest;
                        screw.transform.rotation = targetRot;

                        Renderer[] rends = screw.GetComponentsInChildren<Renderer>(true);
                        for (int r = 0; r < rends.Length; r++)
                        {
                            if (rends[r] != null) rends[r].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }

                        targetBox.OnScrewLanded();
                    });

                    Debug.Log($"<color=#00ffc4><b>[WaitingSlots]</b> {screw.Color} renkli park etmiş vida yeni kutuya aktarılıyor!</color>");
                    break; // Birer birer akıcı transfer
                }
            }
        }

        /// <summary>
        /// Bekleme delikleri dolduğunda oyunda geçerli bir hamle kalıp kalmadığını kontrol eder.
        /// </summary>
        public void CheckForGameOver()
        {
            if (_isGameOver || HasFreeSlot) return;

            // 1. Park eden vidalardan herhangi biri mevcut kutulara gidebiliyor mu?
            if (_screwManager != null)
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    ScrewObject s = _parkedScrews[i];
                    if (s != null && _screwManager.HasMatchingActiveBox(s.Color))
                    {
                        return; // Hala gidebilecek vida var, kaybetme yok
                    }
                }

                // 2. Model üzerinde açık (unblocked) vidalardan herhangi biri mevcut kutulara uyuyor mu?
                ScrewObject[] allScrews = FindObjectsByType<ScrewObject>(FindObjectsSortMode.None);
                foreach (var screw in allScrews)
                {
                    if (screw != null && !screw.IsUnscrewed && !screw.IsBlocked)
                    {
                        if (_screwManager.HasMatchingActiveBox(screw.Color))
                        {
                            return; // Hala doğrudan kutuya gidebilecek açık bir vida var!
                        }
                    }
                }
            }

            // Hiçbir bekleme deliği yok, hiçbir açık vida mevcut kutulara uymuyor: GAME OVER!
            _isGameOver = true;
            Debug.LogError("<color=#ff3838><b>🚨 [GAME OVER] Tüm bekleme delikleri doldu ve geçerli hamle kalmadı!</b></color>");
            OnGameOver?.Invoke();
        }
    }
}
