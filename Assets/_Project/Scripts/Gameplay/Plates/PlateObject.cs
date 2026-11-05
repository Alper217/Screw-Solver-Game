using System;
using System.Collections;
using System.Collections.Generic;
using ScrewSolver.Gameplay.Screws;
using UnityEngine;

namespace ScrewSolver.Gameplay.Plates
{
    /// <summary>
    /// Represents a 3D plate held by screws.
    /// Manages reference counting and activates kinematic physics only when all screws are removed.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PlateObject : MonoBehaviour
    {
        [Header("Components")]
        [Tooltip("Plakanın Rigidbody bileşeni (düşene kadar isKinematic=true kalır)")]
        [SerializeField] private Rigidbody _rigidbody;

        [Tooltip("Plakanın Collider bileşeni")]
        [SerializeField] private Collider _collider;

        [Header("Attached Screws (Bu Plakayı Tutan Vidalar)")]
        [Tooltip("Bu plakayı sabitleyen vidaların listesi")]
        [SerializeField] private List<ScrewObject> _attachedScrews = new();

        [Header("Covered Screws (Bu Plakanın Üstünü Örttüğü Vidalar)")]
        [Tooltip("Bu plakanın altında kalan ve plaka düşünce açılacak vidalar")]
        [SerializeField] private List<ScrewObject> _coveredScrews = new();

        [Header("Fall & Physics Settings")]
        [Tooltip("Son vida söküldükten sonra düşüş başlamadan önceki ufak gecikme")]
        [SerializeField, Range(0f, 0.5f)] private float _fallDelay = 0.04f;

        [Tooltip("Bu Y koordinatının altına düştüğünde obje yok edilir / havuza gider")]
        [SerializeField] private float _destroyBelowY = -15f;

        [Tooltip("Düşerken hafif rastgele dönme torku")]
        [SerializeField] private float _randomTorqueStrength = 5f;

        [Header("Runtime State (Read Only)")]
        [SerializeField] private int _remainingScrewsCount;
        [SerializeField] private bool _hasFallen;

        public event Action<PlateObject> OnPlateFell;
        public bool HasFallen => _hasFallen;
        public int RemainingScrewsCount => _remainingScrewsCount;

        private void Reset()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _collider = GetComponent<Collider>();
            AutoFindAttachedScrews();
        }

        private void Awake()
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
            if (_collider == null) _collider = GetComponent<Collider>();

            // Ensure physics sleep at start (0 CPU physics overhead)
            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
            }

            InitializePlate();
        }

        public void InitializePlate()
        {
            _hasFallen = false;
            _remainingScrewsCount = _attachedScrews.Count;

            // In case some screws were pre-removed or list was empty
            if (_remainingScrewsCount == 0 && _attachedScrews.Count > 0)
            {
                _remainingScrewsCount = _attachedScrews.Count;
            }
        }

        /// <summary>
        /// Called when an attached screw is successfully unscrewed.
        /// </summary>
        public void OnScrewRemoved(ScrewObject screw)
        {
            if (_hasFallen) return;

            _remainingScrewsCount = Mathf.Max(0, _remainingScrewsCount - 1);

            if (_remainingScrewsCount == 0)
            {
                StartCoroutine(FallRoutine());
            }
        }

        private IEnumerator FallRoutine()
        {
            _hasFallen = true;

            if (_fallDelay > 0f)
            {
                yield return new WaitForSeconds(_fallDelay);
            }

            // 1. PuzzleRoot'tan ayır (oyuncu modeli döndürürken düşen parça havada garip dönmesin)
            transform.SetParent(null, true);

            // 2. MeshCollider kullanılıyorsa PhysX hatasını önlemek için convex yap
            if (_collider is MeshCollider mc && !mc.convex)
            {
                mc.convex = true;
            }

            // 3. Altında kilitlediği vidalara haber ver ve kilitlerini aç
            OnPlateFell?.Invoke(this);
            foreach (var screw in _coveredScrews)
            {
                if (screw != null)
                {
                    screw.Unblock(this);
                }
            }

            // 4. Dinamik fiziği aktifleştir (yerçekimi ve serbest düşüş)
            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = true;

                // Doğal bir takla hissi için hafif rastgele tork ve aşağı itiş
                Vector3 randomTorque = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-0.5f, 0.5f),
                    UnityEngine.Random.Range(-1f, 1f)
                ).normalized * _randomTorqueStrength;

                _rigidbody.AddTorque(randomTorque, ForceMode.Impulse);
                _rigidbody.AddForce(Vector3.down * 1.5f, ForceMode.Impulse);
            }

            // Monitor fall until below threshold
            while (transform.position.y > _destroyBelowY)
            {
                yield return null;
            }

            // Clean up when out of screen
            Destroy(gameObject);
        }

        [ContextMenu("Auto Find Attached Screws in Children")]
        public void AutoFindAttachedScrews()
        {
            _attachedScrews.Clear();
            ScrewObject[] childScrews = GetComponentsInChildren<ScrewObject>(includeInactive: true);
            foreach (var screw in childScrews)
            {
                if (!_attachedScrews.Contains(screw))
                {
                    _attachedScrews.Add(screw);
                }
            }
        }

        [ContextMenu("Vidaların Altına Otomatik Delik (Hole) Yerleştir")]
        public void GenerateHolesUnderScrews()
        {
            AutoFindAttachedScrews();

            for (int i = 0; i < _attachedScrews.Count; i++)
            {
                ScrewObject screw = _attachedScrews[i];
                if (screw == null) continue;

                string holeName = $"Hole_{screw.name}";
                Transform existingHole = transform.Find(holeName);
                if (existingHole == null)
                {
                    GameObject holeObj = new GameObject(holeName);
                    holeObj.transform.SetParent(transform, true);
                    holeObj.transform.position = screw.transform.position;
                    holeObj.transform.rotation = screw.transform.rotation;
                    holeObj.transform.localScale = Vector3.one;

                    ScrewHole hole = holeObj.AddComponent<ScrewHole>();
                    hole.SetupVisual();
                }
            }
        }
    }
}

