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

            // Notify dependent screws that this plate is falling (decrements their blockingCount)
            OnPlateFell?.Invoke(this);

            // Enable gravity and dynamic physics
            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = true;

                // Add slight juicy torque for natural tumbling
                Vector3 randomTorque = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-0.5f, 0.5f),
                    UnityEngine.Random.Range(-1f, 1f)
                ).normalized * _randomTorqueStrength;

                _rigidbody.AddTorque(randomTorque, ForceMode.Impulse);
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
    }
}

