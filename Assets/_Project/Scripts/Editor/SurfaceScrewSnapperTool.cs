using ScrewSolver.Config;
using ScrewSolver.Core;
using ScrewSolver.Gameplay.Plates;
using ScrewSolver.Gameplay.Screws;
using UnityEditor;
using UnityEngine;

namespace ScrewSolver.Editor
{
    /// <summary>
    /// 3D Yüzey Normaline Otomatik Vida Oturtma Aracı (Surface Screw Snapper):
    /// Küre, silindir, kavisli/organik veya karmaşık herhangi bir 3D model yüzeyine
    /// tıklanan noktanın yüzey normalini (Surface Normal) hesaplayarak vidayı tam dik açıyla oturtur.
    /// </summary>
    public sealed class SurfaceScrewSnapperTool : EditorWindow
    {
        private const string PrefabPath = "Assets/Prefabs/Screw.prefab";
        private const string AnimConfigPath = "Assets/ScrewAnimationConfig.asset";

        [SerializeField] private ColorType _selectedColor = ColorType.Blue;
        [SerializeField] private PlateObject _targetPlate;
        [SerializeField] private bool _autoDetectPlate = true;
        [SerializeField] private float _depthOffset = 0.045f;
        [SerializeField] private bool _toolActive = false;

        private GameObject _screwPrefab;
        private ScrewAnimationConfigSO _animConfig;

        [MenuItem("ScrewSolver/Araçlar/3D Yüzey Normaline Vida Ekleme %#n")]
        public static void ShowWindow()
        {
            var window = GetWindow<SurfaceScrewSnapperTool>("Yüzey Vida Aracı");
            window.minSize = new Vector2(320, 360);
        }

        private void OnEnable()
        {
            _screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            _animConfig = AssetDatabase.LoadAssetAtPath<ScrewAnimationConfigSO>(AnimConfigPath);
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("3D Yüzey Normali Vida Ekleyici", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bu araç; küre, silindir veya kavisli tüm 3D modellerde, yüzeyin eğimini otomatik algılayıp vidayı yüzeye tam dik (90°) açıyla oturtur.\n\n" +
                "Kullanım: Aracı Aktif Et butonuna basın, Scene ekranında istediğiniz yüzeye 'Shift + Sol Tık' yapın.",
                MessageType.Info);

            EditorGUILayout.Space(10);
            _selectedColor = (ColorType)EditorGUILayout.EnumPopup("Vida Rengi:", _selectedColor);

            EditorGUILayout.Space(5);
            _autoDetectPlate = EditorGUILayout.Toggle("Plakayı Otomatik Algıla", _autoDetectPlate);
            if (!_autoDetectPlate)
            {
                _targetPlate = (PlateObject)EditorGUILayout.ObjectField("Hedef Plaka:", _targetPlate, typeof(PlateObject), true);
            }

            _depthOffset = EditorGUILayout.Slider("Derinlik Ayarı (Gömülme):", _depthOffset, 0f, 0.15f);

            EditorGUILayout.Space(15);
            GUI.backgroundColor = _toolActive ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.4f);
            if (GUILayout.Button(_toolActive ? "■ Aracı Devre Dışı Bırak" : "▶ Aracı Aktif Et (Shift + Tık)", GUILayout.Height(38)))
            {
                _toolActive = !_toolActive;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_toolActive) return;

            Event e = Event.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
            {
                // Yüzey normalini görselleştir (Cyan daire ve yeşil ok)
                Handles.color = _selectedColor.ToUnityColor();
                Handles.DrawWireDisc(hit.point, hit.normal, 0.12f);
                Handles.color = Color.green;
                Handles.DrawLine(hit.point, hit.point + hit.normal * 0.35f);

                HandleUtility.Repaint();

                // Shift + Sol Tık ile vidayı tam o noktaya ve o eğimle ekle
                if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
                {
                    e.Use();
                    PlaceScrewAtHit(hit);
                }
            }
        }

        private void PlaceScrewAtHit(RaycastHit hit)
        {
            if (_screwPrefab == null)
            {
                _screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            PlateObject plate = _targetPlate;
            if (_autoDetectPlate || plate == null)
            {
                plate = hit.collider.GetComponentInParent<PlateObject>();
            }

            Transform parentTransform = plate != null ? plate.transform : hit.collider.transform;

            GameObject screwGo = (GameObject)PrefabUtility.InstantiatePrefab(_screwPrefab, parentTransform);
            screwGo.name = $"Screw_{_selectedColor}_{System.DateTime.Now.Millisecond}";

            // Konum: Yüzeye hafif gömülü (derinlik ayarlı)
            screwGo.transform.position = hit.point - (hit.normal * _depthOffset);

            // Rotasyon: Tam yüzey normaline bakacak şekilde (Yüzeye 90° dik)
            screwGo.transform.rotation = Quaternion.LookRotation(hit.normal);

            ScrewObject screw = screwGo.GetComponent<ScrewObject>();
            if (screw != null)
            {
                SerializedObject so = new SerializedObject(screw);
                so.FindProperty("_color").enumValueIndex = (int)_selectedColor;
                if (plate != null)
                {
                    so.FindProperty("_parentPlate").objectReferenceValue = plate;
                }
                if (_animConfig != null)
                {
                    so.FindProperty("_animationConfig").objectReferenceValue = _animConfig;
                }
                so.ApplyModifiedProperties();

                screw.ApplyColorVisual();
            }

            if (plate != null)
            {
                plate.AutoFindAttachedScrews();
                EditorUtility.SetDirty(plate);
            }

            // Siyah delik soketi ekle
            ScrewHoleTool.SetupHoleForScrew(screwGo);

            Undo.RegisterCreatedObjectUndo(screwGo, "Place Screw on Curved Surface");
            Debug.Log($"<color=#{ColorUtility.ToHtmlStringRGB(_selectedColor.ToUnityColor())}><b>[SurfaceSnapper]</b> Vida {_selectedColor} tam yüzey normaline yerleştirildi! (Pozisyon: {hit.point})</color>");
        }
    }
}

