using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ScrewSolver.Gameplay.Controllers
{
    /// <summary>
    /// Ortadaki 3D bulmaca modelini (PuzzleRoot) parmak veya fare sürüklemesiyle
    /// her yöne 360° serbestçe (Virtual Trackball) ve pürüzsüz ataletle döndürür.
    /// Dokunma (Tap) ve Sürükleme (Drag) ayrımını yaparak yanlışlıkla vida sökülmesini önler.
    /// </summary>
    public sealed class PuzzleRotator : MonoBehaviour
    {
        [Header("Döndürülecek Merkez Pivot")]
        [Tooltip("Plakaların ve vidaların bağlı olduğu merkez boş obje (PuzzleRoot)")]
        [SerializeField] private Transform _targetPivot;

        [Header("Kamera & Hassasiyet")]
        [SerializeField] private Camera _camera;
        [SerializeField] private float _sensitivity = 0.35f;
        [SerializeField] private float _inertiaDamping = 6f;
        [SerializeField] private float _dragThreshold = 14f;

        public event Action<Vector2> OnTap;
        public event Action OnDragStarted;

        public Transform TargetPivot => _targetPivot;

        private bool _isPointerDown;
        private bool _isDragging;
        private Vector2 _pointerDownPos;
        private Vector2 _lastPointerPos;
        private Vector2 _velocity;

        private void Awake()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_targetPivot == null)
            {
                GameObject root = GameObject.Find("PuzzleRoot");
                if (root != null) _targetPivot = root.transform;
            }
        }

        public void SetTargetPivot(Transform pivot)
        {
            _targetPivot = pivot;
        }

        private void Update()
        {
            HandleInput();
            ApplyInertia();
        }

        private void HandleInput()
        {
            Vector2 pointerPos;
            bool isPressed = false;
            bool wasPressedThisFrame = false;
            bool wasReleasedThisFrame = false;

            // 1. Mobil Dokunmatik (Touchscreen)
            if (Touchscreen.current != null)
            {
                var touch = Touchscreen.current.primaryTouch;
                pointerPos = touch.position.ReadValue();
                isPressed = touch.press.isPressed;
                wasPressedThisFrame = touch.press.wasPressedThisFrame;
                wasReleasedThisFrame = touch.press.wasReleasedThisFrame;
            }
            // 2. Masaüstü / Editör Faresi (Mouse)
            else if (Mouse.current != null)
            {
                pointerPos = Mouse.current.position.ReadValue();
                isPressed = Mouse.current.leftButton.isPressed;
                wasPressedThisFrame = Mouse.current.leftButton.wasPressedThisFrame;
                wasReleasedThisFrame = Mouse.current.leftButton.wasReleasedThisFrame;
            }
            else
            {
                return;
            }

            // Basma Başlangıcı
            if (wasPressedThisFrame)
            {
                if (IsPointerOverUI()) return;
                _isPointerDown = true;
                _isDragging = false;
                _pointerDownPos = pointerPos;
                _lastPointerPos = pointerPos;
                _velocity = Vector2.zero;
            }

            // Basılı Tutup Sürükleme
            if (_isPointerDown && isPressed)
            {
                Vector2 totalDelta = pointerPos - _pointerDownPos;
                if (!_isDragging && totalDelta.sqrMagnitude > (_dragThreshold * _dragThreshold))
                {
                    _isDragging = true;
                    OnDragStarted?.Invoke();
                }

                if (_isDragging)
                {
                    Vector2 delta = pointerPos - _lastPointerPos;
                    RotatePivot(delta);

                    _velocity = delta / Mathf.Max(0.001f, Time.deltaTime);
                }

                _lastPointerPos = pointerPos;
            }

            // Bırakma
            if (wasReleasedThisFrame && _isPointerDown)
            {
                _isPointerDown = false;

                if (!_isDragging)
                {
                    // Oyuncu sürüklemedi, temiz bir dokunuş yaptı (Tap)
                    OnTap?.Invoke(pointerPos);
                }

                _isDragging = false;
            }
        }

        private void RotatePivot(Vector2 deltaPixels)
        {
            if (_targetPivot == null || _camera == null) return;

            float rotX = deltaPixels.y * _sensitivity;
            float rotY = -deltaPixels.x * _sensitivity;

            Transform camT = _camera.transform;
            _targetPivot.Rotate(camT.right, rotX, Space.World);
            _targetPivot.Rotate(camT.up, rotY, Space.World);
        }

        private static bool IsPointerOverUI()
        {
            if (EventSystem.current == null) return false;
            if (Touchscreen.current != null)
            {
                int touchId = Touchscreen.current.primaryTouch.touchId.ReadValue();
                if (EventSystem.current.IsPointerOverGameObject(touchId)) return true;
            }
            return EventSystem.current.IsPointerOverGameObject();
        }

        private void ApplyInertia()
        {
            if (!_isPointerDown && _velocity.sqrMagnitude > 0.01f && _targetPivot != null && _camera != null)
            {
                Vector2 deltaThisFrame = _velocity * Time.deltaTime;
                RotatePivot(deltaThisFrame);

                _velocity = Vector2.Lerp(_velocity, Vector2.zero, Time.deltaTime * _inertiaDamping);
            }
        }
    }
}
