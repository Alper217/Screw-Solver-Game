using System;
using System.Collections.Generic;
using DG.Tweening;
using ScrewSolver.Config;
using ScrewSolver.Core;
using ScrewSolver.Gameplay.Holes;
using ScrewSolver.Gameplay.Plates;
using UnityEngine;

namespace ScrewSolver.Gameplay.Screws
{
    /// <summary>
    /// Represents an individual 3D screw.
    /// Manages color visuals, blocking dependencies, and juicy DOTween unscrewing/flight animations.
    /// </summary>
    [SelectionBase]
    [RequireComponent(typeof(Collider))]
    public sealed class ScrewObject : MonoBehaviour
    {
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

        [Header("Color & Visuals")]
        [Tooltip("Vidanın bulmaca rengi")]
        [SerializeField] private ColorType _color = ColorType.Red;

        [Tooltip("Vidanın renk atanacak MeshRenderer bileşeni")]
        [SerializeField] private MeshRenderer _meshRenderer;

        [Tooltip("Tıklamayı algılayan Collider")]
        [SerializeField] private Collider _clickCollider;

        [Tooltip("Animasyon süre ve kavis ayarları")]
        [SerializeField] private ScrewAnimationConfigSO _animationConfig;

        [Tooltip("Yıldız yarığına (phillips cross) otomatik gölge rengi uygulansın mı?")]
        [SerializeField] private bool _autoDarkenCrossSlot = true;

        [Tooltip("Yıldız yarığının hangi materyal yuvasında (slot) olduğu (genelde 1)")]
        [SerializeField] private int _crossSlotMaterialIndex = 1;

        [Tooltip("Yıldız yarığının gölge koyuluk oranı (0.45 = %45 daha koyu ton)")]
        [SerializeField, Range(0.1f, 0.9f)] private float _shadowDarkness = 0.45f;

        [Header("Plate Dependencies (Plaka Bağlantıları)")]
        [Tooltip("Bu vidanın tuttuğu ana plaka (vida sökülünce bu plakaya haber verilir)")]
        [SerializeField] private PlateObject _parentPlate;

        [Tooltip("Bu vidanın üstünü kapatan / engelleyen plakaların listesi")]
        [SerializeField] private List<PlateObject> _blockingPlates = new();

        [Header("Runtime State (Read Only)")]
        [SerializeField] private int _blockingCount;
        [SerializeField] private bool _isUnscrewed;
        [SerializeField] private bool _isAnimating;
        [SerializeField] private bool _isSelected;
        [SerializeField] private PuzzleHole _currentHole;

        private Vector3 _dockedLocalPos;
        private Quaternion _dockedLocalRot;
        private MaterialPropertyBlock _propBlock;
        private Sequence _activeSequence;
        private Tween _floatTween;

        public ColorType Color => _color;
        public bool IsBlocked => _blockingCount > 0;
        public bool IsUnscrewed => _isUnscrewed;
        public bool IsAnimating => _isAnimating;
        public bool IsSelected => _isSelected;
        public PuzzleHole CurrentHole { get => _currentHole; set => _currentHole = value; }
        public PlateObject ParentPlate => _parentPlate;
        public IReadOnlyList<PlateObject> BlockingPlates => _blockingPlates;

        private void Reset()
        {
            _meshRenderer = GetComponentInChildren<MeshRenderer>();
            _clickCollider = GetComponent<Collider>();
            _parentPlate = GetComponentInParent<PlateObject>();
        }

        private void Awake()
        {
            if (_meshRenderer == null) _meshRenderer = GetComponentInChildren<MeshRenderer>();
            if (_clickCollider == null) _clickCollider = GetComponent<Collider>();
            if (_parentPlate == null) _parentPlate = GetComponentInParent<PlateObject>();

            // Concave Mesh Collider hatasını önlemek için tüm mesh collider'ları convex yap
            MeshCollider[] mcs = GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < mcs.Length; i++)
            {
                if (mcs[i] != null) mcs[i].convex = true;
            }

            // Vidanın altındaki HoleBezel yuvasında PuzzleHole bileşeni olduğundan emin ol ve başlangıçta doldur
            Transform holeTr = transform.Find("HoleBezel") ?? transform.Find("ScrewHole");
            if (holeTr != null)
            {
                holeTr.localScale = Vector3.one;
                if (!holeTr.TryGetComponent<PuzzleHole>(out var hole))
                {
                    hole = holeTr.gameObject.AddComponent<PuzzleHole>();
                }
                _currentHole = hole;
                _currentHole.Occupy(this);
            }

            _propBlock = new MaterialPropertyBlock();
            ApplyColorVisual();
            InitializeDependencies();
        }

        private void OnValidate()
        {
            if (_meshRenderer == null) _meshRenderer = GetComponentInChildren<MeshRenderer>();
            ApplyColorVisual();
        }

        public void InitializeDependencies()
        {
            _isUnscrewed = false;
            _isAnimating = false;
            _blockingCount = _blockingPlates.Count;

            // Listen to each covering plate: when it falls, decrement our blockingCount
            foreach (PlateObject plate in _blockingPlates)
            {
                if (plate != null)
                {
                    plate.OnPlateFell += HandleCoveringPlateFell;
                }
            }
        }

        private void HandleCoveringPlateFell(PlateObject fallenPlate)
        {
            if (fallenPlate != null)
            {
                fallenPlate.OnPlateFell -= HandleCoveringPlateFell;
            }

            _blockingCount = Mathf.Max(0, _blockingCount - 1);
        }

        /// <summary>
        /// Üstünü örten bir plaka düştüğünde bu vidanın engel sayacını düşürür ve kilidini açar.
        /// </summary>
        public void Unblock(PlateObject plate)
        {
            if (plate != null && _blockingPlates.Contains(plate))
            {
                _blockingPlates.Remove(plate);
            }

            _blockingCount = Mathf.Max(0, _blockingCount - 1);
        }

        /// <summary>
        /// Plays a fast, juicy wiggle/shake animation when the player taps a blocked screw.
        /// </summary>
        public void PlayWiggleLockedAnimation()
        {
            if (_isUnscrewed || _isAnimating) return;

            float duration = _animationConfig != null ? _animationConfig.WiggleDuration : 0.20f;
            float strength = _animationConfig != null ? _animationConfig.WiggleStrength : 12f;

            _isAnimating = true;
            transform.DOComplete();

            transform.DOShakeRotation(duration, new Vector3(0f, 0f, strength), vibrato: 10, randomness: 90f)
                .SetEase(Ease.OutQuad)
                .OnComplete(() => _isAnimating = false);
        }

        /// <summary>
        /// Sequences the screw loosening from the hole, lifting up, and flying parabolically to the target.
        /// </summary>
        public void PlayUnscrewAndFlyAnimation(Vector3 targetWorldPosition, Action onComplete = null)
        {
            PlayUnscrewAndFlyAnimation(targetWorldPosition, transform.rotation, onComplete);
        }

        public void PlayUnscrewAndFlyAnimation(Vector3 targetWorldPosition, Quaternion targetWorldRotation, Action onComplete = null)
        {
            if (_isUnscrewed) return;

            _isUnscrewed = true;
            _isAnimating = true;

            // Disable collider immediately to prevent double-click race conditions
            if (_clickCollider != null)
            {
                _clickCollider.enabled = false;
            }

            float unscrewTime = _animationConfig != null ? _animationConfig.UnscrewDuration : 0.20f;
            float liftDist = _animationConfig != null ? _animationConfig.UnscrewLiftDistance : 0.20f;
            float rotDegrees = _animationConfig != null ? _animationConfig.UnscrewRotationDegrees : 720f;
            float flyTime = _animationConfig != null ? _animationConfig.FlyDuration : 0.35f;
            float rawArc = _animationConfig != null ? _animationConfig.ArcHeight : 0.35f;

            _activeSequence?.Kill();
            _activeSequence = DOTween.Sequence();

            // Vidanın altındaki siyah delik yuvasını (HoleBezel / ScrewHole) gövde veya plaka üzerinde bırak!
            // Böylece vida uçup kutuya gittiğinde delik yüzeyde kalır ve boş delik (PuzzleHole) olarak kullanılabilir.
            Transform holeTransform = transform.Find("HoleBezel") ?? transform.Find("ScrewHole");
            if (holeTransform != null)
            {
                holeTransform.SetParent(transform.parent, true);
                if (_currentHole == null)
                {
                    _currentHole = holeTransform.GetComponent<PuzzleHole>();
                }
            }

            if (_currentHole != null)
            {
                _currentHole.Vacate();
                _currentHole = null;
            }

            // Detach from parent plate so the plate falling doesn't drag the flying screw with it
            transform.SetParent(null, true);

            // 1. Unscrew phase: Vidanın kendi şaft ekseni (forward) boyunca dönerek delikten dışarı çıkması
            Vector3 surfaceNormal = transform.forward;
            Vector3 liftPosition = transform.position + surfaceNormal * liftDist;
            _activeSequence.Append(transform.DOMove(liftPosition, unscrewTime).SetEase(Ease.OutCubic));
            _activeSequence.Join(transform.DORotate(new Vector3(0f, 0f, -rotDegrees), unscrewTime, RotateMode.LocalAxisAdd).SetEase(Ease.OutCubic));

            // Notify parent plate right when the screw exits the hole
            _activeSequence.AppendCallback(() =>
            {
                if (_parentPlate != null)
                {
                    _parentPlate.OnScrewRemoved(this);
                }
            });

            // 2. Flight phase: Objenin içinden geçmeden, yüzeyden dışarı ve yukarı doğru tatlı bir kavisle hedefe uçuş
            float yDiff = targetWorldPosition.y - liftPosition.y;
            float arcHeight = rawArc;
            if (yDiff > 0f)
            {
                arcHeight = Mathf.Min(rawArc, Mathf.Max(0.12f, yDiff * 0.30f));
            }

            Vector3 midPoint = Vector3.Lerp(liftPosition, targetWorldPosition, 0.40f);
            midPoint += surfaceNormal * 0.25f + Vector3.up * arcHeight;

            Vector3[] flightPath = new Vector3[] { midPoint, targetWorldPosition };
            _activeSequence.Append(transform.DOPath(flightPath, flyTime, PathType.CatmullRom).SetEase(Ease.InOutQuad));
            _activeSequence.Join(transform.DORotateQuaternion(targetWorldRotation, flyTime).SetEase(Ease.OutQuad));

            // 3. Arrival
            _activeSequence.OnComplete(() =>
            {
                _isAnimating = false;
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// Bekleme yuvasında park etmiş bir vidanın yeni gelen renk kutusuna yumuşak yayla uçuşu.
        /// </summary>
        public void PlayFlyFromWaitingSlot(Vector3 targetWorldPosition, Quaternion targetWorldRotation, Action onComplete = null)
        {
            _isAnimating = true;
            _activeSequence?.Kill();
            _activeSequence = DOTween.Sequence();

            float flyTime = _animationConfig != null ? _animationConfig.FlyDuration : 0.35f;
            Vector3 startPos = transform.position;
            Camera cam = Camera.main;
            Vector3 upDir = cam != null ? cam.transform.up : Vector3.up;
            Vector3 midPoint = Vector3.Lerp(startPos, targetWorldPosition, 0.5f) + upDir * 0.15f;

            Vector3[] path = new Vector3[] { startPos, midPoint, targetWorldPosition };
            _activeSequence.Append(transform.DOPath(path, flyTime, PathType.CatmullRom).SetEase(Ease.InOutQuad));
            _activeSequence.Join(transform.DORotate(targetWorldRotation.eulerAngles, flyTime).SetEase(Ease.InOutQuad));
            _activeSequence.OnComplete(() =>
            {
                _isAnimating = false;
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// Bekleme barından (Buffer) yeni açılan hedef kutuya pürüzsüz yay çizerek uçar.
        /// </summary>
        public void PlayFlyToSocketAnimation(Vector3 targetWorldPosition, Action onComplete = null)
        {
            _activeSequence?.Kill();
            _activeSequence = DOTween.Sequence();

            float flyTime = _animationConfig != null ? _animationConfig.FlyDuration : 0.35f;
            float arcHeight = 0.5f;

            _activeSequence.Append(transform.DOJump(targetWorldPosition, arcHeight, 1, flyTime).SetEase(Ease.OutQuad));
            _activeSequence.OnComplete(() =>
            {
                _isAnimating = false;
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// Vidayı delikten hafifçe yukarı kaldırıp oyuncunun model üzerinde boş bir delik seçmesini bekler.
        /// Küp döndürüldüğünde vidanın küple beraber dönmesi için PuzzleRoot'a bağlanır ve local koordinatlarda çalışır.
        /// Ayrıca plakadan söküldüğü için plakanın otomatik serbest düşüşünü anında tetikler.
        /// </summary>
        public void PlayLiftAndSelectAnimation()
        {
            if (_isAnimating || _isUnscrewed) return;

            _isSelected = true;
            _dockedLocalPos = transform.localPosition;
            _dockedLocalRot = transform.localRotation;

            _activeSequence?.Kill();
            _floatTween?.Kill();
            _activeSequence = DOTween.Sequence();

            // 1. Vidanın altındaki siyah deliği (HoleBezel) yerinde (plaka/gövde üzerinde) bırak!
            Transform holeTransform = transform.Find("HoleBezel") ?? transform.Find("ScrewHole");
            if (holeTransform != null)
            {
                holeTransform.SetParent(transform.parent, true);
                if (_currentHole == null)
                {
                    _currentHole = holeTransform.GetComponent<PuzzleHole>();
                }
            }

            if (_currentHole != null)
            {
                _currentHole.Vacate();
            }

            // 2. Plakadan vida söküldü -> Plakaya hemen bildir ki bağlı başka vida kalmadıysa anında düşsün!
            if (_parentPlate != null)
            {
                PlateObject oldPlate = _parentPlate;
                _parentPlate = null;
                oldPlate.OnScrewRemoved(this);
            }

            // 3. Vidayı dönen modelin kök objesine (PuzzleRoot) bağla!
            // Böylece oyuncu küpü 360 derece döndürdüğünde vida da küple beraber döner, havada tek başına kalmaz!
            var rotator = UnityEngine.Object.FindFirstObjectByType<ScrewSolver.Gameplay.Controllers.PuzzleRotator>();
            Transform puzzleRoot = rotator != null ? rotator.transform : GameObject.Find("PuzzleRoot")?.transform;
            if (puzzleRoot != null)
            {
                transform.SetParent(puzzleRoot, true);
            }

            // 4. Local koordinatlarda delikten yukarı çıkış ve süzülme animasyonu
            Vector3 localSurfaceNormal = transform.parent != null 
                ? transform.parent.InverseTransformDirection(transform.forward) 
                : transform.forward;
            Vector3 targetLocalLiftPos = transform.localPosition + localSurfaceNormal * 0.20f;

            _activeSequence.Append(transform.DOLocalMove(targetLocalLiftPos, 0.22f).SetEase(Ease.OutCubic));
            _activeSequence.Join(transform.DORotate(new Vector3(0f, 0f, -360f), 0.22f, RotateMode.LocalAxisAdd).SetEase(Ease.OutCubic));
            _activeSequence.OnComplete(() =>
            {
                // Local uzayda hafif havada süzülme animasyonu (Selected Idle)
                // Küp dönerken local offset korunduğu için vida küple birebir birlikte döner!
                _floatTween = transform.DOLocalMove(targetLocalLiftPos + localSurfaceNormal * 0.04f, 0.5f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine);
            });
        }

        /// <summary>
        /// Seçimi iptal edip vidayı tekrar orijinal yuvasına vidalar.
        /// </summary>
        public void PlayCancelSelectAnimation()
        {
            _isSelected = false;
            _floatTween?.Kill();
            _activeSequence?.Kill();
            _activeSequence = DOTween.Sequence();

            if (_currentHole != null)
            {
                _currentHole.Occupy(this);
                _parentPlate = _currentHole.ParentPlate;
                if (_parentPlate != null)
                {
                    _parentPlate.AttachScrew(this);
                }
                transform.SetParent(_currentHole.transform.parent != null ? _currentHole.transform.parent : _currentHole.transform, true);
                transform.localScale = Vector3.one;

                _activeSequence.Append(transform.DOMove(_currentHole.SnapPosition, 0.20f).SetEase(Ease.InCubic));
                _activeSequence.Join(transform.DORotate(_currentHole.SnapRotation.eulerAngles, 0.20f).SetEase(Ease.InCubic));
                _activeSequence.OnComplete(() =>
                {
                    if (_currentHole != null)
                    {
                        transform.localScale = Vector3.one;
                        transform.position = _currentHole.SnapPosition;
                        transform.rotation = _currentHole.SnapRotation;
                    }
                });
            }
            else
            {
                _activeSequence.Append(transform.DOLocalMove(_dockedLocalPos, 0.20f).SetEase(Ease.InCubic));
                _activeSequence.Join(transform.DOLocalRotate(_dockedLocalRot.eulerAngles, 0.20f).SetEase(Ease.InCubic));
            }
        }

        /// <summary>
        /// Seçili vidayı model veya plaka üzerindeki boş bir PuzzleHole yuvasına uçurup vidalar.
        /// </summary>
        public void PlayMoveToHoleAnimation(PuzzleHole targetHole, Action onComplete = null)
        {
            if (targetHole == null) return;

            _isSelected = false;
            _floatTween?.Kill();
            _activeSequence?.Kill();
            _activeSequence = DOTween.Sequence();
            _isAnimating = true;

            // Eski delik zaten boşaltılmıştı
            if (_currentHole != null)
            {
                _currentHole.Vacate();
            }

            // Yeni yuvayı rezerve et (Scale'i hemen orijinal boyutuna sıfırlar)
            targetHole.Occupy(this);
            _currentHole = targetHole;
            _parentPlate = targetHole.ParentPlate;
            if (_parentPlate != null)
            {
                _parentPlate.AttachScrew(this);
            }

            Vector3 startPos = transform.position;
            Vector3 targetPos = targetHole.SnapPosition;
            Quaternion targetRot = targetHole.SnapRotation;

            Vector3 midPoint = Vector3.Lerp(startPos, targetPos, 0.5f) + (transform.forward + targetRot * Vector3.forward) * 0.20f;
            Vector3[] path = new Vector3[] { startPos, midPoint, targetPos };

            _activeSequence.Append(transform.DOPath(path, 0.35f, PathType.CatmullRom).SetEase(Ease.InOutQuad));
            _activeSequence.Join(transform.DORotate(targetRot.eulerAngles, 0.35f).SetEase(Ease.InOutQuad));
            _activeSequence.OnComplete(() =>
            {
                if (targetHole != null)
                {
                    transform.SetParent(targetHole.transform.parent != null ? targetHole.transform.parent : targetHole.transform, true);
                    transform.localScale = Vector3.one;
                    transform.position = targetHole.SnapPosition;
                    transform.rotation = targetHole.SnapRotation;

                    targetHole.PlaySnapPunch();
                }
                _isAnimating = false;
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// Updates the visual tint of the screw according to ColorType.
        /// Supports multi-material models with automatic cross-slot shadow contrast.
        /// </summary>
        [ContextMenu("Refresh Visual Color")]
        public void ApplyColorVisual()
        {
            if (_meshRenderer == null) return;

            if (_propBlock == null)
            {
                _propBlock = new MaterialPropertyBlock();
            }

            UnityEngine.Color mainColor = _color.ToUnityColor();
            UnityEngine.Color shadowColor = UnityEngine.Color.Lerp(mainColor, UnityEngine.Color.black, _shadowDarkness);

            int matCount = _meshRenderer.sharedMaterials != null ? _meshRenderer.sharedMaterials.Length : 1;

            if (_autoDarkenCrossSlot && matCount > 1)
            {
                for (int i = 0; i < matCount; i++)
                {
                    _meshRenderer.GetPropertyBlock(_propBlock, i);
                    UnityEngine.Color targetColor = (i == _crossSlotMaterialIndex) ? shadowColor : mainColor;
                    _propBlock.SetColor(BaseColorPropertyId, targetColor);
                    _meshRenderer.SetPropertyBlock(_propBlock, i);
                }
            }
            else
            {
                _meshRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(BaseColorPropertyId, mainColor);
                _meshRenderer.SetPropertyBlock(_propBlock);
            }
        }

        private void OnDestroy()
        {
            _activeSequence?.Kill();
            _floatTween?.Kill();
            foreach (PlateObject plate in _blockingPlates)
            {
                if (plate != null)
                {
                    plate.OnPlateFell -= HandleCoveringPlateFell;
                }
            }
        }
    }
}

