using System;
using DG.Tweening;
using ScrewSolver.Gameplay.Plates;
using ScrewSolver.Gameplay.Screws;
using UnityEngine;

namespace ScrewSolver.Gameplay.Holes
{
    /// <summary>
    /// Bulmaca modeli veya plakalar üzerindeki vida yuvası deliği (PuzzleHole):
    /// 1. Boşken oyuncu tarafından tıklanabilir (boştaki vida buraya takılabilir).
    /// 2. Doluyken içindeki vidayı tutar.
    /// 3. Vida sökülüp kutuya veya başka deliğe gittiğinde bu delik tekrar boşa çıkar.
    /// </summary>
    [AddComponentMenu("ScrewSolver/Puzzle Hole")]
    public sealed class PuzzleHole : MonoBehaviour
    {
        [Header("Bağlı Plaka / Gövde")]
        [Tooltip("Bu delik bir plakanın üzerindeyse o plaka, gövdedeyse null")]
        [SerializeField] private PlateObject _parentPlate;

        [Header("Durum")]
        [SerializeField] private bool _isOccupied;
        [SerializeField] private ScrewObject _currentScrew;

        [Header("Görsel Elemanlar")]
        [SerializeField] private Renderer _holeRenderer;
        [SerializeField] private Transform _highlightRing;
        [SerializeField, Range(1.05f, 1.4f)] private float _highlightPulseScale = 1.20f;

        public bool IsOccupied => _isOccupied;
        public ScrewObject CurrentScrew => _currentScrew;
        public PlateObject ParentPlate => _parentPlate;

        /// <summary>
        /// Vidanın başlığının delik puluna (HoleBezel) tam milimetrik oturması için vidanın Z ekseni offseti.
        /// PuzzleRoot ve modelin dünya ölçeğini (lossyScale) hesaba katarak vidanın deliğin içine batmasını veya
        /// siyah yuvanın arkasında kaybolmasını önler.
        /// </summary>
        public Vector3 SnapPosition
        {
            get
            {
                Vector3 normal = transform.forward;
                float scaleZ = transform.lossyScale.z;
                if (Mathf.Approximately(scaleZ, 0f)) scaleZ = 1f;

                // 0.105f local derinlik: Vidanın başlığı delik pulunun tam üzerine (0.002f emniyet payı ile)
                // kusursuz oturur, asla siyah yuvanın içinde kaybolmaz veya küçük görünmez.
                return transform.position - (normal * (0.105f * scaleZ));
            }
        }
        public Quaternion SnapRotation => transform.rotation;

        private Vector3 _originalScale = Vector3.one;
        private Transform _scaleParent;

        private void Awake()
        {
            _scaleParent = transform.parent;
            if (transform.localScale != Vector3.zero)
            {
                _originalScale = transform.localScale;
            }
            else
            {
                _originalScale = Vector3.one;
                transform.localScale = Vector3.one;
            }

            if (_parentPlate == null)
            {
                _parentPlate = GetComponentInParent<PlateObject>();
            }

            // Collider yoksa tıklama için ekle
            if (GetComponent<Collider>() == null)
            {
                SphereCollider sc = gameObject.AddComponent<SphereCollider>();
                sc.radius = 0.12f;
                sc.center = Vector3.zero;
            }
        }

        /// <summary>
        /// Delik vidadan ayrılıp farklı ölçekli plaka/gövdeye bağlandığında yeni temel yerel ölçeğini senkronize eder.
        /// </summary>
        public void SyncScaleReference()
        {
            transform.DOKill();
            _scaleParent = transform.parent;
            if (transform.localScale != Vector3.zero)
            {
                _originalScale = transform.localScale;
            }
        }

        private void EnsureScaleReferenceForParent()
        {
            if (transform.parent == _scaleParent) return;

            if (!DOTween.IsTweening(transform) && transform.localScale != Vector3.zero)
            {
                _originalScale = transform.localScale;
            }
            _scaleParent = transform.parent;
        }

        /// <summary>
        /// Deliği belirli bir vida ile doldurur.
        /// </summary>
        public void Occupy(ScrewObject screw)
        {
            EnsureScaleReferenceForParent();
            _isOccupied = true;
            _currentScrew = screw;
            transform.DOKill();
            if (_originalScale == Vector3.zero) _originalScale = Vector3.one;
            transform.localScale = _originalScale;
        }

        /// <summary>
        /// İçindeki vida ayrıldığında deliği boşa çıkarır.
        /// </summary>
        public void Vacate()
        {
            EnsureScaleReferenceForParent();
            _isOccupied = false;
            _currentScrew = null;
        }

        /// <summary>
        /// Bir vida seçildiğinde boş deliklerin hafif nabız atarak dikkat çekmesi.
        /// </summary>
        public void SetHighlight(bool highlight)
        {
            EnsureScaleReferenceForParent();
            if (_originalScale == Vector3.zero) _originalScale = Vector3.one;
            transform.DOKill();
            if (!highlight || _isOccupied || (_parentPlate != null && _parentPlate.HasFallen))
            {
                transform.localScale = _originalScale;
                return;
            }

            transform.DOScale(_originalScale * _highlightPulseScale, 0.4f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
        }

        private void OnDestroy()
        {
            transform.DOKill();
        }

        /// <summary>
        /// Vida deliğe oturduğunda tatlı yaylanma efekti.
        /// </summary>
        public void PlaySnapPunch()
        {
            EnsureScaleReferenceForParent();
            if (_originalScale == Vector3.zero) _originalScale = Vector3.one;
            transform.DOKill();
            transform.localScale = _originalScale;
            transform.DOPunchScale(_originalScale * 0.15f, 0.25f, 6, 0.5f);
        }
    }
}

