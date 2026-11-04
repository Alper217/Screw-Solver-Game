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
            if (_isUnscrewed) return;

            _isUnscrewed = true;
            _isAnimating = true;

            // Disable collider immediately to prevent double-click race conditions
            if (_clickCollider != null)
            {
                _clickCollider.enabled = false;
            }

            float unscrewTime = _animationConfig != null ? _animationConfig.UnscrewDuration : 0.22f;
            float liftDist = _animationConfig != null ? _animationConfig.UnscrewLiftDistance : 0.5f;
            float rotDegrees = _animationConfig != null ? _animationConfig.UnscrewRotationDegrees : 720f;
            float flyTime = _animationConfig != null ? _animationConfig.FlyDuration : 0.38f;
            float arcHeight = _animationConfig != null ? _animationConfig.ArcHeight : 1.8f;

            _activeSequence?.Kill();
            _activeSequence = DOTween.Sequence();

            // Detach from parent plate so the plate falling doesn't drag the flying screw with it
            transform.SetParent(null, true);

            // 1. Unscrew phase: rotate and lift along local up axis
            Vector3 liftPosition = transform.position + transform.up * liftDist;
            _activeSequence.Append(transform.DOMove(liftPosition, unscrewTime).SetEase(Ease.OutCubic));
            _activeSequence.Join(transform.DORotate(new Vector3(0f, rotDegrees, 0f), unscrewTime, RotateMode.LocalAxisAdd).SetEase(Ease.Linear));

            // Notify parent plate right when the screw exits the hole
            _activeSequence.AppendCallback(() =>
            {
                if (_parentPlate != null)
                {
                    _parentPlate.OnScrewRemoved(this);
                }
            });

            // 2. Flight phase: parabolic jump to destination box/dock slot
            _activeSequence.Append(transform.DOJump(targetWorldPosition, arcHeight, 1, flyTime).SetEase(Ease.InQuad));
            _activeSequence.Join(transform.DOScale(Vector3.one * 0.9f, flyTime));

            // 3. Arrival
            _activeSequence.OnComplete(() =>
            {
                _isAnimating = false;
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// Updates the visual tint of the screw according to ColorType.
        /// </summary>
        [ContextMenu("Refresh Visual Color")]
        public void ApplyColorVisual()
        {
            if (_meshRenderer == null) return;

            if (_propBlock == null)
            {
                _propBlock = new MaterialPropertyBlock();
            }

            _meshRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorPropertyId, _color.ToUnityColor());
            _meshRenderer.SetPropertyBlock(_propBlock);
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
