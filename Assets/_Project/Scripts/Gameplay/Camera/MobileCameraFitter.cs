using UnityEngine;

namespace ScrewSolver.Gameplay.CameraControllers
{
    /// <summary>
    /// Mobil dikey ekran (1080x1920 / 9:16) formatında oyun alanının her zaman
    /// kusursuz çerçevelenmesini ve yatayda hiçbir objenin kesilmemesini garanti eder.
    /// Farklı mobil ekran oranlarında (16:9, 19.5:9, 20:9, tablet 4:3) FOV'u dinamik dengeler.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class MobileCameraFitter : MonoBehaviour
    {
        [Header("Target Reference Resolution (1080x1920)")]
        [Tooltip("Hedef mobil tasarım genişliği (piksel)")]
        [SerializeField] private float _referenceWidth = 1080f;

        [Tooltip("Hedef mobil tasarım yüksekliği (piksel)")]
        [SerializeField] private float _referenceHeight = 1920f;

        [Header("Framing Settings")]
        [Tooltip("9:16 referans oranındaki dikey FOV açısı")]
        [SerializeField] private float _designFieldOfView = 45f;

        [Tooltip("Yatayda ekranda kesinlikle görünür kalması gereken dünya genişliği")]
        [SerializeField] private float _targetVisibleWidth = 2.2f;

        [SerializeField] private Camera _targetCamera;

        public float ReferenceAspect => _referenceWidth / Mathf.Max(1f, _referenceHeight);

        private int _lastScreenWidth = 0;
        private int _lastScreenHeight = 0;

        private void Awake()
        {
            if (_targetCamera == null)
            {
                _targetCamera = GetComponent<Camera>();
            }
            UpdateCameraFraming();
        }

        private void Update()
        {
            // Sadece ekran çözünürlüğü değiştiğinde hesapla
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                UpdateCameraFraming();
            }
        }

        public void UpdateCameraFraming()
        {
            if (_targetCamera == null)
            {
                _targetCamera = GetComponent<Camera>();
                if (_targetCamera == null) return;
            }

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            float currentAspect = (float)Screen.width / Mathf.Max(1f, Screen.height);
            float refAspect = ReferenceAspect;

            if (_targetCamera.orthographic)
            {
                float desiredHalfWidth = _targetVisibleWidth * 0.5f;
                _targetCamera.orthographicSize = desiredHalfWidth / currentAspect;
            }
            else
            {
                // Perspektif Kamerada Yatay Alan Koruma:
                // Ekran 9:16'dan daha dar bir telefon ise (örn. iPhone 19.5:9, 20:9),
                // yatay genişliğin kesilmemesi için dikey FOV genişletilir.
                if (currentAspect < refAspect)
                {
                    float hFovRad = 2f * Mathf.Atan(Mathf.Tan(_designFieldOfView * 0.5f * Mathf.Deg2Rad) * refAspect);
                    float newVertFovRad = 2f * Mathf.Atan(Mathf.Tan(hFovRad * 0.5f) / currentAspect);
                    _targetCamera.fieldOfView = newVertFovRad * Mathf.Rad2Deg;
                }
                else
                {
                    // Tablet veya yatay ekranda dikey görüş alanı sabit tutulur, taşma yaşanmaz
                    _targetCamera.fieldOfView = _designFieldOfView;
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            UpdateCameraFraming();
        }
#endif
    }
}

