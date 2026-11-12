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
        [SerializeField, Range(1f, 10f)] private float _randomTorqueStrength = 3.5f;

        [Tooltip("Düşerken modelden dışarı doğru çok hafif itilme/fırlama gücü")]
        [SerializeField, Range(0.5f, 6f)] private float _outwardPopForce = 1.6f;

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

            // Eğer liste boşsa veya eksikse, altındaki vidaları otomatik tara
            if (_attachedScrews == null || _attachedScrews.Count == 0)
            {
                AutoFindAttachedScrews();
            }

            // Null referansları temizle
            _attachedScrews.RemoveAll(s => s == null);
            _remainingScrewsCount = _attachedScrews.Count;
        }

        /// <summary>
        /// Bir vida bu plakaya taşındığında veya geri takıldığında plakaya bağlanır.
        /// </summary>
        public void AttachScrew(ScrewObject screw)
        {
            if (screw == null || _hasFallen) return;

            if (!_attachedScrews.Contains(screw))
            {
                _attachedScrews.Add(screw);
            }

            _remainingScrewsCount = _attachedScrews.Count;
        }

        /// <summary>
        /// Bu plakaya bağlı bir vida söküldüğünde çağrılır.
        /// </summary>
        public void OnScrewRemoved(ScrewObject screw)
        {
            if (_hasFallen) return;

            if (screw != null && _attachedScrews.Contains(screw))
            {
                _attachedScrews.Remove(screw);
            }

            _remainingScrewsCount = _attachedScrews.Count;

            // Ancak ve ancak bu plakayı tutan HİÇBİR vida kalmadığında plaka düşer!
            if (_remainingScrewsCount <= 0)
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

            // 1. Plakanın baktığı dış yönü (Normal) kaydet
            Vector3 outwardDir = transform.forward;

            // 2. PuzzleRoot'tan ayır (oyuncu modeli döndürürken düşen parça havada garip dönmesin)
            transform.SetParent(null, true);

            // 3. Düşen parçanın ve altındaki nesnelerin dönen bulmaca modeliyle çarpışmasını önle
            Collider[] allColliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < allColliders.Length; i++)
            {
                if (allColliders[i] != null)
                {
                    allColliders[i].enabled = false;
                }
            }

            // 4. Altında kilitlediği vidalara haber ver ve kilitlerini aç
            OnPlateFell?.Invoke(this);
            foreach (var screw in _coveredScrews)
            {
                if (screw != null)
                {
                    screw.Unblock(this);
                }
            }

            // 5. Dinamik fiziği aktifleştir: Dışarı doğru fırlama (Pop) + Yerçekimi + Takla
            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = true;

                // Doğal bir takla hissi için rastgele tork
                Vector3 randomTorque = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-0.5f, 0.5f),
                    UnityEngine.Random.Range(-1f, 1f)
                ).normalized * _randomTorqueStrength;

                // Plakayı gövdeden dışarı doğru hafifçe it (yay gibi dışarı atılma hissi) ve doğal yerçekimiyle düşür
                Vector3 popImpulse = (outwardDir * _outwardPopForce) + (Vector3.down * 0.4f);
                _rigidbody.AddForce(popImpulse, ForceMode.Impulse);
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

