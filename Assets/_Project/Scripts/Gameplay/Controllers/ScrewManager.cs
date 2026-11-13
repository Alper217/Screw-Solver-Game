using System;
using System.Collections.Generic;
using ScrewSolver.Core;
using ScrewSolver.Gameplay.Boxes;
using ScrewSolver.Gameplay.Holes;
using ScrewSolver.Gameplay.Screws;
using UnityEngine;

namespace ScrewSolver.Gameplay.Controllers
{
    /// <summary>
    /// Oynanışın ana yöneticisi (Kısa ve net: ScrewManager):
    /// 1. Tıklanan vidayı Raycast ile algılar.
    /// 2. Vidanın rengine uygun olan açık kutuyu bulur.
    /// 3. Vidayı delikten çıkarıp kutuya 3D yay çizerek uçurur.
    /// 4. Tüm kutular dolduğunda Bölüm Tamamlandı (Win) sonucunu üretir.
    /// </summary>
    [AddComponentMenu("ScrewSolver/Screw Manager")]
    public sealed class ScrewManager : MonoBehaviour
    {
        [Header("Girdi & Kamera")]
        [SerializeField] private PuzzleRotator _rotator;
        [SerializeField] private Camera _camera;
        [SerializeField] private LayerMask _screwLayerMask = ~0;

        [Header("Hedef Kutular (Sahnedeki ColorBox'lar)")]
        [SerializeField] private List<ColorBox> _activeBoxes = new List<ColorBox>();

        [Header("Bekleme Yuvaları")]
        [SerializeField] private ScrewSolver.Gameplay.Slots.WaitingSlotManager _waitingSlots;

        public event Action OnLevelCompleted;
        public event Action OnGameOver;

        /// <summary>
        /// BoxQueueManager gibi dinamik kuyruk yöneticileri tarafından aktif kutuları senkronize etmek için çağrılır.
        /// </summary>
        public void UpdateActiveBoxes(List<ColorBox> boxes)
        {
            _activeBoxes.Clear();
            if (boxes != null)
            {
                _activeBoxes.AddRange(boxes);
            }

            // Yeni kutu geldiğinde bekleme deliklerindeki vidaları kontrol et ve gerekirse kutuya aktar
            if (_waitingSlots != null)
            {
                _waitingSlots.CheckAndTransferParkedScrews(_activeBoxes);
            }
        }

        public bool HasMatchingActiveBox(ColorType color) => FindMatchingBox(color) != null;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];
        private int _totalBoxesCount;
        private int _completedBoxesCount;

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;

            if (_rotator == null)
            {
                _rotator = FindFirstObjectByType<PuzzleRotator>();
            }

            if (_waitingSlots == null)
            {
                _waitingSlots = FindFirstObjectByType<ScrewSolver.Gameplay.Slots.WaitingSlotManager>();
            }

            if (FindFirstObjectByType<ScrewSolver.UI.GameUIController>() == null)
            {
                GameObject canvasGo = GameObject.Find("UI_Canvas") ?? new GameObject("UI_Canvas");
                canvasGo.AddComponent<ScrewSolver.UI.GameUIController>();
            }

            BoxQueueManager queueMgr = FindFirstObjectByType<BoxQueueManager>();
            if (queueMgr != null)
            {
                queueMgr.OnAllBoxesCompleted += () =>
                {
                    Debug.Log("<color=#00e5ff><b>🎉 TEBRİKLER! BÖLÜMDEKİ TÜM KUTULAR VE VİDALAR TEMİZLENDİ! (WIN)</b></color>");
                    OnLevelCompleted?.Invoke();
                    var ui = FindFirstObjectByType<ScrewSolver.UI.GameUIController>();
                    if (ui != null) ui.ShowVictoryPopup();
                };
            }
            else
            {
                // Statik kutular varsa onları bul
                if (_activeBoxes.Count == 0)
                {
                    _activeBoxes.AddRange(FindObjectsByType<ColorBox>(FindObjectsSortMode.None));
                }

                _totalBoxesCount = _activeBoxes.Count;
                _completedBoxesCount = 0;

                foreach (var box in _activeBoxes)
                {
                    if (box != null)
                    {
                        box.OnBoxCompleted += HandleBoxCompleted;
                    }
                }
            }
        }

        private void OnEnable()
        {
            if (_rotator != null)
            {
                _rotator.OnTap += HandleScreenTap;
            }
        }

        private void OnDisable()
        {
            if (_rotator != null)
            {
                _rotator.OnTap -= HandleScreenTap;
            }

            foreach (var box in _activeBoxes)
            {
                if (box != null)
                {
                    box.OnBoxCompleted -= HandleBoxCompleted;
                }
            }
        }

        private ScrewObject _selectedScrew;

        private void HandleScreenTap(Vector2 screenPos)
        {
            if (_camera == null) return;

            Ray ray = _camera.ScreenPointToRay(screenPos);
            int hitCount = Physics.RaycastNonAlloc(ray, _hitBuffer, 100f, _screwLayerMask);

            if (hitCount <= 0)
            {
                // Boşluğa tıklandı -> Seçili vida varsa iptal et
                if (_selectedScrew != null)
                {
                    DeselectCurrentScrew();
                }
                return;
            }

            float closestDist = float.MaxValue;
            ScrewObject tappedScrew = null;
            PuzzleHole tappedEmptyHole = null;
            PuzzleHole tappedOccupiedHole = null;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _hitBuffer[i];
                if (hit.collider == null) continue;

                ScrewObject screw = hit.collider.GetComponentInParent<ScrewObject>();
                if (screw != null && !screw.IsUnscrewed && hit.distance < closestDist)
                {
                    closestDist = hit.distance;
                    tappedScrew = screw;
                }

                PuzzleHole hole = hit.collider.GetComponentInParent<PuzzleHole>();
                if (hole != null && hit.distance < closestDist)
                {
                    closestDist = hit.distance;
                    if (hole.IsOccupied)
                    {
                        tappedOccupiedHole = hole;
                    }
                    else
                    {
                        tappedEmptyHole = hole;
                    }
                }
            }

            // DURUM 1: Elimizde seçili bir vida var (_selectedScrew != null)
            if (_selectedScrew != null)
            {
                // A) Eğer oyuncu geçerli boş bir deliğe tıkladıysa -> Vidayı o boş deliğe aktar
                if (tappedEmptyHole != null && !tappedEmptyHole.IsOccupied)
                {
                    MoveSelectedScrewToHole(tappedEmptyHole);
                    return;
                }

                // B) Eğer oyuncu DOLU bir deliğe veya başka bir vidaya tıkladıysa (veya boş yüzeye):
                // Kullanıcı talebi: Tıklanan delik doluysa vida ilk boş deliğe sapmasın, kendi yuvasına geri dönsün!
                Debug.Log("<color=yellow>[ScrewManager] Tıklanan delik dolu veya geçersiz! Seçili vida kendi orijinal yuvasına geri dönüyor.</color>");
                DeselectCurrentScrew();
                return;
            }

            // DURUM 2: Seçili vida yokken bir vidaya tıklandı
            if (tappedScrew != null)
            {
                ProcessScrewSelection(tappedScrew);
            }
        }

        private void ProcessScrewSelection(ScrewObject screw)
        {
            // Eğer vida başka bir plaka tarafından kilitliyse titreme animasyonu oynat
            if (screw.IsBlocked)
            {
                screw.PlayWiggleLockedAnimation();
                return;
            }

            // Eğer tıklanan vida zaten seçiliyse iptal et (yerine geri vidala)
            if (_selectedScrew == screw)
            {
                DeselectCurrentScrew();
                return;
            }

            // 1. Vidanın rengine uygun ve boş yeri olan açık kutu var mı?
            ColorBox targetBox = FindMatchingBox(screw.Color);

            if (targetBox != null)
            {
                if (_selectedScrew != null) DeselectCurrentScrew();
                FlyScrewToBox(screw, targetBox);
                return;
            }

            // 2. Açık kutu yoksa: Vidayı kaldır ve model üzerinde boş delik seçmesini bekle!
            if (_selectedScrew != null)
            {
                _selectedScrew.PlayCancelSelectAnimation();
            }

            _selectedScrew = screw;
            _selectedScrew.PlayLiftAndSelectAnimation();

            // Sahnedeki tüm boş delikleri hafif parlat/nabız ver
            HighlightAllEmptyHoles(true);
            Debug.Log($"<color=#{ColorUtility.ToHtmlStringRGB(screw.Color.ToUnityColor())}><b>[{screw.Color}]</b> Vida seçildi! Modeldeki boş bir deliğe tıklayarak vidalayabilirsiniz.</color>");

            // Vida söküldükten ve plaka düşüşü tetiklendikten sonra geçerli hamle kaldı mı kontrol et!
            CancelInvoke(nameof(CheckDeadlock));
            Invoke(nameof(CheckDeadlock), 0.35f);
        }

        private void CheckDeadlock()
        {
            if (_selectedScrew == null) return;

            // 1. Vidanın gidebileceği uygun kutu var mı?
            ColorBox targetBox = FindMatchingBox(_selectedScrew.Color);
            if (targetBox != null) return;

            // 2. Modelde hala sağlam ve boş olan herhangi bir delik var mı?
            PuzzleHole[] allHoles = FindObjectsByType<PuzzleHole>(FindObjectsSortMode.None);
            bool hasAnyValidHole = false;

            foreach (var h in allHoles)
            {
                if (h != null && !h.IsOccupied)
                {
                    // Eğer delik bir plakaya bağlıysa ve o plaka düştüyse bu delik kullanılamaz
                    if (h.ParentPlate != null && h.ParentPlate.HasFallen) continue;

                    hasAnyValidHole = true;
                    break;
                }
            }

            // Eğer ne kutu var ne de koyulabilecek sağlam bir delik kaldı -> OYUN BİTTİ (KAYBETTİN)
            if (!hasAnyValidHole)
            {
                Debug.LogWarning("<color=#ff3366><b>[ScrewManager] ÇIKMAZ (DEADLOCK)!</b> Vidanın gidebileceği uygun kutu yok ve koyulabilecek hiçbir delik kalmadı! OYUN BİTTİ.</color>");
                OnGameOver?.Invoke();

                var ui = FindFirstObjectByType<ScrewSolver.UI.GameUIController>();
                if (ui != null)
                {
                    ui.ShowDefeatPopup("NO VALID MOVES!", "The screw cannot go to any box, and no empty holes left to place!");
                }
            }
        }

        private void MoveSelectedScrewToHole(PuzzleHole targetHole)
        {
            if (_selectedScrew == null || targetHole == null || targetHole.IsOccupied) return;

            ScrewObject movingScrew = _selectedScrew;
            _selectedScrew = null;
            HighlightAllEmptyHoles(false);

            movingScrew.PlayMoveToHoleAnimation(targetHole, () =>
            {
                Debug.Log($"<color=#00ffc4><b>[PuzzleHole]</b> Vida başarıyla modeldeki boş deliğe vidalandı!</color>");
            });
        }

        public void DeselectCurrentScrew()
        {
            if (_selectedScrew != null)
            {
                _selectedScrew.PlayCancelSelectAnimation();
                _selectedScrew = null;
            }
            HighlightAllEmptyHoles(false);
        }

        private void HighlightAllEmptyHoles(bool highlight)
        {
            PuzzleHole[] holes = FindObjectsByType<PuzzleHole>(FindObjectsSortMode.None);
            foreach (var h in holes)
            {
                if (h != null && !h.IsOccupied)
                {
                    h.SetHighlight(highlight);
                }
            }
        }

        private void FlyScrewToBox(ScrewObject screw, ColorBox targetBox)
        {
            Transform socket = targetBox.ReserveSocket();
            Vector3 destination = socket != null ? socket.position : targetBox.transform.position;
            Quaternion targetRotation = socket != null ? socket.rotation : targetBox.transform.rotation;
            Vector3 targetWorldScale = Vector3.one * targetBox.GetTargetScrewWorldScale();

            screw.PlayUnscrewAndFlyAnimation(destination, targetRotation, () =>
            {
                screw.transform.SetParent(socket != null ? socket : targetBox.transform, true);
                screw.transform.position = destination;
                screw.transform.rotation = targetRotation;

                Renderer[] screwRends = screw.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < screwRends.Length; r++)
                {
                    if (screwRends[r] != null)
                    {
                        screwRends[r].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                }

                targetBox.OnScrewLanded();
            }, targetWorldScale);

            Debug.Log($"<color=#{ColorUtility.ToHtmlStringRGB(screw.Color.ToUnityColor())}><b>[{screw.Color}]</b> Vida havalandı ve kendi renk kutusuna uçuyor!</color>");
        }

        private ColorBox FindMatchingBox(ColorType color)
        {
            for (int i = 0; i < _activeBoxes.Count; i++)
            {
                ColorBox box = _activeBoxes[i];
                if (box != null && box.gameObject.activeInHierarchy && !box.IsFull && box.BoxColor == color)
                {
                    return box;
                }
            }
            return null;
        }

        private void HandleBoxCompleted(ColorBox box)
        {
            _completedBoxesCount++;
            Debug.Log($"<color=#{ColorUtility.ToHtmlStringRGB(box.BoxColor.ToUnityColor())}><b>[{box.BoxColor} Kutu]</b> Tamamlandı ve ayrıldı! ({_completedBoxesCount}/{_totalBoxesCount})</color>");

            if (_completedBoxesCount >= _totalBoxesCount)
            {
                Debug.Log("<color=#00e5ff><b>🎉 TEBRİKLER! BÖLÜMDEKİ TÜM KUTULAR VE VİDALAR TEMİZLENDİ! (WIN)</b></color>");
                OnLevelCompleted?.Invoke();
            }
        }
    }
}

