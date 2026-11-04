using System;
using ScrewSolver.Gameplay.Screws;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScrewSolver.Gameplay.Input
{
    /// <summary>
    /// Detects taps/clicks on screws in the 3D scene using Camera raycasts.
    /// Supports both Touch and Mouse input with zero garbage collection.
    /// </summary>
    public sealed class ScrewInputController : MonoBehaviour
    {
        [Header("Camera & Layers")]
        [Tooltip("Sahneyi gören ana kamera (boş bırakılırsa Camera.main otomatik alınır)")]
        [SerializeField] private Camera _targetCamera;

        [Tooltip("Vidaların bulunduğu LayerMask")]
        [SerializeField] private LayerMask _screwLayerMask = ~0;

        [Tooltip("Girdi etkileşimi açık mı?")]
        [SerializeField] private bool _inputEnabled = true;

        public event Action<ScrewObject> OnScrewSelected;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[4];

        private void Awake()
        {
            if (_targetCamera == null)
            {
                _targetCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (!_inputEnabled || _targetCamera == null) return;

            Vector2 screenPosition;
            bool wasPressedThisFrame = false;

            // 1. Touchscreen input (Mobile)
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
                wasPressedThisFrame = true;
            }
            // 2. Mouse input (Desktop / Editor)
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                wasPressedThisFrame = true;
            }
            else
            {
                return;
            }

            if (wasPressedThisFrame)
            {
                ProcessScreenTap(screenPosition);
            }
        }

        public void SetInputEnabled(bool enabled)
        {
            _inputEnabled = enabled;
        }

        private void ProcessScreenTap(Vector2 screenPos)
        {
            Ray ray = _targetCamera.ScreenPointToRay(screenPos);
            int hitCount = Physics.RaycastNonAlloc(ray, _hitBuffer, 100f, _screwLayerMask);

            if (hitCount <= 0) return;

            // Find closest hit screw
            float closestDistance = float.MaxValue;
            ScrewObject closestScrew = null;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _hitBuffer[i];
                if (hit.collider == null) continue;

                ScrewObject screw = hit.collider.GetComponentInParent<ScrewObject>();
                if (screw != null && !screw.IsUnscrewed && hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    closestScrew = screw;
                }
            }

            if (closestScrew != null)
            {
                // If blocked by outer plate: juicy wiggle feedback
                if (closestScrew.IsBlocked)
                {
                    closestScrew.PlayWiggleLockedAnimation();
                }
                else
                {
                    OnScrewSelected?.Invoke(closestScrew);
                }
            }
        }
    }
}
