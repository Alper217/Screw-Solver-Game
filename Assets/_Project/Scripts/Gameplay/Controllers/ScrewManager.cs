using System;
using System.Collections.Generic;
using ScrewSolver.Core;
using ScrewSolver.Gameplay.Boxes;
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

        public event Action OnLevelCompleted;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[4];
        private int _totalBoxesCount;
        private int _completedBoxesCount;

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;

            if (_rotator == null)
            {
                _rotator = FindFirstObjectByType<PuzzleRotator>();
            }

            // Sahnedeki kutuları otomatik bul
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

        private void HandleScreenTap(Vector2 screenPos)
        {
            if (_camera == null) return;

            Ray ray = _camera.ScreenPointToRay(screenPos);
            int hitCount = Physics.RaycastNonAlloc(ray, _hitBuffer, 100f, _screwLayerMask);

            if (hitCount <= 0) return;

            float closestDist = float.MaxValue;
            ScrewObject tappedScrew = null;

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
            }

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

            // Vidanın rengine uygun ve boş yeri olan açık kutuyu bul
            ColorBox targetBox = FindMatchingBox(screw.Color);

            if (targetBox != null)
            {
                Transform socket = targetBox.ReserveSocket();
                Vector3 destination = socket != null ? socket.position : targetBox.transform.position;
                Quaternion targetRotation = socket != null ? socket.rotation : targetBox.transform.rotation;

                // Vidayı küpten çöz ve kutuya uçur (uçarken dikleşir, kutuya tam oturur)
                screw.PlayUnscrewAndFlyAnimation(destination, targetRotation, () =>
                {
                    screw.transform.SetParent(socket != null ? socket : targetBox.transform, true);
                    screw.transform.position = destination;
                    screw.transform.rotation = targetRotation;
                    targetBox.OnScrewLanded();
                });

                Debug.Log($"<color=#{ColorUtility.ToHtmlStringRGB(screw.Color.ToUnityColor())}><b>[{screw.Color}]</b> Vida havalandı ve kendi renk kutusuna uçuyor!</color>");
            }
            else
            {
                // Uygun kutu yoksa veya kutu dolduysa uyarı titremesi
                screw.PlayWiggleLockedAnimation();
                Debug.LogWarning($"[ScrewManager] {screw.Color} rengi için ekranda açık veya boş yeri olan bir kutu bulunamadı!");
            }
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

