using System;
using System.Collections.Generic;
using DG.Tweening;
using ScrewSolver.Core;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ScrewSolver.Gameplay.Boxes
{
    /// <summary>
    /// Sahneye elle yerleştirilen veya Prefab olarak düzenlenen hedef kutu bileşeni.
    /// Kod kutunun veya soketlerin boyutuna/pozisyonuna ASLA müdahale etmez.
    /// Manuel olarak eklenen soket noktaları (Slot_1, Slot_2...) üzerinde
    /// Editörde gerçek zamanlı vida hayalet önizlemesi (Ghost Screw Preview) gösterir.
    /// </summary>
    [SelectionBase]
    public sealed class ColorBox : MonoBehaviour
    {
        [Header("Kutu Rengi")]
        [Tooltip("Bu kutunun kabul ettiği vida rengi")]
        [SerializeField] private ColorType _boxColor = ColorType.Yellow;

        [Header("Kapasite")]
        [Tooltip("Bu kutunun kaç vida kabul edeceği (Örn: 3 veya 4)")]
        [SerializeField] private int _capacity = 3;

        [Header("Vida Soketleri (Manuel Ayarlanır)")]
        [Tooltip("Kutudaki delik noktaları (Hiyerarşideki Slot nesneleri)")]
        [SerializeField] private List<Transform> _screwSockets = new();

        [Header("Görsel Renklendirme")]
        [SerializeField] private Renderer _boxRenderer;

        [Header("Vida Önizleme (Ghost Preview - Editör)")]
        [Tooltip("Scene ve Prefab ekranında vidaların deliklerde nasıl duracağını göster")]
        [SerializeField] private bool _showScrewPreview = true;

        [Tooltip("Kutu rengi yerine vidaların gerçekçi gri/gümüş görünmesi")]
        [SerializeField] private bool _useBoxColorForPreview = false;

        [Tooltip("Önizleme vida rengi (Şık ve net Gri/Gümüş)")]
        [SerializeField] private Color _customPreviewColor = new Color(0.78f, 0.80f, 0.84f, 0.70f);

        [Tooltip("Yıldız yarığı koyu gri tonu")]
        [SerializeField] private Color _crossSlotPreviewColor = new Color(0.28f, 0.28f, 0.32f, 0.90f);

        [Tooltip("Kutunun ölçeğine göre önizlemeyi orantılı ayarlar (Prefab ve Scene uyumu)")]
        [SerializeField] private bool _autoScaleWithBox = true;

        [Tooltip("Önizleme vida boyutu çarpanı (Gerektiğinde ince ayar)")]
        [SerializeField] private float _previewScaleMultiplier = 1f;

        [Tooltip("Vidanın delikteki duruş açısı (0, 0, 0 = Tam Dik)")]
        [SerializeField] private Vector3 _previewRotationOffset = Vector3.zero;

        [Tooltip("Önizleme için kullanılacak vida mesh'i (Otomatik yüklenir)")]
        [SerializeField] private Mesh _previewScrewMesh;

        public ColorType BoxColor => _boxColor;
        public int Capacity => _capacity;
        public int ReservedCount => _reservedCount;
        public int SettledCount => _settledCount;
        public bool HasSpace => _reservedCount < _capacity;
        public bool IsFull => _reservedCount >= _capacity;
        public bool IsComplete => _settledCount >= _capacity;
        public IReadOnlyList<Transform> ScrewSockets => _screwSockets;

        public event Action<ColorBox> OnBoxCompleted;

        private int _reservedCount = 0;
        private int _settledCount = 0;
        private bool _isDeparting = false;
        private Vector3 _initialLocalPos;

        private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            _initialLocalPos = transform.localPosition;
            if (_boxRenderer == null) _boxRenderer = GetComponentInChildren<Renderer>();
            ApplyColor();
            CleanNullSockets();
        }

        private void OnValidate()
        {
            if (_boxRenderer == null) _boxRenderer = GetComponentInChildren<Renderer>();
            ApplyColor();
            CleanNullSockets();

            // Eski -90 rotasyon veya kutu rengi kalmışsa otomatik olarak dik ve griye güncelle
            if (Mathf.Approximately(_previewRotationOffset.x, -90f))
            {
                _previewRotationOffset = Vector3.zero;
            }
            if (_useBoxColorForPreview)
            {
                _useBoxColorForPreview = false;
            }

#if UNITY_EDITOR
            if (_screwSockets.Count == 0)
            {
                AutoFindChildSockets();
            }
            if (_previewScrewMesh == null)
            {
                TryLoadPreviewMesh();
            }
#endif
        }

        private void CleanNullSockets()
        {
            if (_screwSockets != null)
            {
                _screwSockets.RemoveAll(s => s == null);
            }
        }

        [ContextMenu("Otomatik: Çocuk Soketleri Bul ve Ata")]
        public void AutoFindChildSockets()
        {
            if (_screwSockets == null) _screwSockets = new List<Transform>();

            // Sadece gerçekten Slot/Socket/Hole olan alt objeleri listeye ekle
            foreach (Transform child in transform)
            {
                if (child == transform) continue;

                string n = child.name;
                bool isSocketName = n.StartsWith("Slot", StringComparison.OrdinalIgnoreCase) ||
                                   n.StartsWith("Socket", StringComparison.OrdinalIgnoreCase) ||
                                   n.StartsWith("Hole", StringComparison.OrdinalIgnoreCase);

                if (isSocketName && !_screwSockets.Contains(child))
                {
                    _screwSockets.Add(child);
                }
            }
        }

        [ContextMenu("Refresh Visual Color")]
        public void RefreshColorVisual()
        {
            ApplyColor();
        }

        public void SetColor(ColorType newColor)
        {
            _boxColor = newColor;
            ApplyColor();
        }

        private void ApplyColor()
        {
            if (_propBlock == null)
            {
                _propBlock = new MaterialPropertyBlock();
            }

            Color c = _boxColor.ToUnityColor();

            Renderer[] rends = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] != null)
                {
                    rends[i].GetPropertyBlock(_propBlock);
                    _propBlock.SetColor(BaseColorPropId, c);
                    _propBlock.SetColor(ColorPropId, c);
                    rends[i].SetPropertyBlock(_propBlock);
                }
            }
        }

        /// <summary>
        /// Kutuya sıradaki vidayı rezerve eder ve ilgili deliğin soket Transform'unu döner.
        /// </summary>
        public Transform ReserveSocket()
        {
            if (IsFull) return null;

            Transform targetSocket = null;
            if (_screwSockets != null && _screwSockets.Count > 0)
            {
                int index = _reservedCount % _screwSockets.Count;
                targetSocket = _screwSockets[index];
            }

            _reservedCount++;
            return targetSocket != null ? targetSocket : transform;
        }

        public void OnScrewLanded()
        {
            _settledCount++;

            // Yaylanma tepkisi
            transform.DOPunchScale(Vector3.one * 0.08f, 0.2f, 8, 0.5f);

            if (_settledCount >= _capacity && !_isDeparting)
            {
                _isDeparting = true;
                PlayCompletionAnimation();
            }
        }

        private void PlayCompletionAnimation()
        {
            Sequence seq = DOTween.Sequence();
            seq.Append(transform.DOPunchScale(Vector3.one * 0.15f, 0.25f, 10, 0.6f));
            seq.Append(transform.DOLocalMoveY(_initialLocalPos.y + 3f, 0.45f).SetEase(Ease.InBack));
            seq.OnComplete(() =>
            {
                gameObject.SetActive(false);
                OnBoxCompleted?.Invoke(this);
            });
        }

        private void OnDrawGizmos()
        {
            DrawPreviewGizmos(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawPreviewGizmos(true);
        }

        private void DrawPreviewGizmos(bool isSelected)
        {
            if (!_showScrewPreview) return;
            if (_screwSockets == null || _screwSockets.Count == 0) return;

#if UNITY_EDITOR
            if (_previewScrewMesh == null)
            {
                TryLoadPreviewMesh();
            }
#endif

            // Orantılı ölçek: TargetBox sahne referans ölçeği ~9.88'dir.
            float scaleRatio = _autoScaleWithBox ? (transform.lossyScale.x / 9.88f) : 1f;
            if (scaleRatio <= 0.001f) scaleRatio = 1f;
            Vector3 finalScale = Vector3.one * (scaleRatio * _previewScaleMultiplier);

            Color baseCol = _useBoxColorForPreview ? _boxColor.ToUnityColor() : _customPreviewColor;
            Color ghostColor = isSelected 
                ? new Color(baseCol.r, baseCol.g, baseCol.b, 0.90f) 
                : new Color(baseCol.r, baseCol.g, baseCol.b, 0.70f);

            for (int i = 0; i < _screwSockets.Count; i++)
            {
                Transform socket = _screwSockets[i];
                if (socket == null) continue;

                Quaternion rot = socket.rotation * Quaternion.Euler(_previewRotationOffset);

                if (_previewScrewMesh != null)
                {
                    int subCount = _previewScrewMesh.subMeshCount;
                    for (int s = 0; s < subCount; s++)
                    {
                        // Submesh 1 yıldız yarığıdır: belirgin görünmesi için koyu gri çizeriz
                        Gizmos.color = (s == 1) ? _crossSlotPreviewColor : ghostColor;
                        Gizmos.DrawMesh(_previewScrewMesh, s, socket.position, rot, finalScale);
                    }
                }

                // Soketin tam merkezini gösteren küçük zarif işaretleyici
                Gizmos.color = isSelected ? new Color(0.2f, 1f, 0.4f, 0.8f) : new Color(0.5f, 0.5f, 0.5f, 0.4f);
                Gizmos.DrawWireSphere(socket.position, 0.005f * scaleRatio);
            }
        }

#if UNITY_EDITOR
        private void TryLoadPreviewMesh()
        {
            // 1. Önce doğrudan sahnede ve oyunda kullanılan Screw prefab'ından al
            GameObject screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Screw.prefab");
            if (screwPrefab != null)
            {
                MeshFilter mf = screwPrefab.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    _previewScrewMesh = mf.sharedMesh;
                    return;
                }
            }

            // 2. FBX dosyasından tara
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("Assets/Prefabs/FBX/Screw+Phillips.fbx");
            if (assets != null)
            {
                foreach (var a in assets)
                {
                    if (a is Mesh m)
                    {
                        _previewScrewMesh = m;
                        return;
                    }
                }
            }
        }
#endif
    }
}
