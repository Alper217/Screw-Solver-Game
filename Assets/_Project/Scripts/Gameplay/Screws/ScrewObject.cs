using System;
using System.Collections.Generic;
using DG.Tweening;
using ScrewSolver.Config;
using ScrewSolver.Core;
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

        private MaterialPropertyBlock _propBlock;
        private Sequence _activeSequence;

        public ColorType Color => _color;
        public bool IsBlocked => _blockingCount > 0;
        public bool IsUnscrewed => _isUnscrewed;
        public bool IsAnimating => _isAnimating;
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
            // Böylece vida uçup kutuya gittiğinde delik yüzeyde kalır ve vidanın arkasından uçmaz.
            Transform holeTransform = transform.Find("HoleBezel") ?? transform.Find("ScrewHole");
            if (holeTransform != null)
            {
                holeTransform.SetParent(transform.parent, true);
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

