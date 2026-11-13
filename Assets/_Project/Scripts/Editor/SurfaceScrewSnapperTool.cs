using ScrewSolver.Core;
using ScrewSolver.Editor.Generator;
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
        [SerializeField] private ColorType _selectedColor = ColorType.Blue;
        [SerializeField] private PlateObject _targetPlate;
        [SerializeField] private bool _autoDetectPlate = true;
        [SerializeField] private float _depthOffset = 0f;
        [SerializeField] private bool _mirrorOpposite = false;
        [SerializeField] private bool _toolActive = false;

        [MenuItem("ScrewSolver/Araçlar/3D Yüzey Normaline Vida Ekleme %#n")]
        public static void ShowWindow()
        {
            var window = GetWindow<SurfaceScrewSnapperTool>("Yüzey Vida Aracı");
            window.minSize = new Vector2(320, 360);
        }

        private void OnEnable()
        {
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
                "Küre, silindir veya kavisli tüm 3D modellerde yüzey eğimini algılayıp vidayı HoleBezel yuvası yüzeye sıfır oturacak şekilde 90° dik yerleştirir.\n\n" +
                "Kullanım: Aracı Aktif Et butonuna basın, Scene ekranında 'Shift + Sol Tık' yapın.",
                MessageType.Info);

            EditorGUILayout.Space(10);
            _selectedColor = (ColorType)EditorGUILayout.EnumPopup("Vida Rengi:", _selectedColor);

            EditorGUILayout.Space(5);
            _autoDetectPlate = EditorGUILayout.Toggle("Plakayı Otomatik Algıla", _autoDetectPlate);
            if (!_autoDetectPlate)
            {
                _targetPlate = (PlateObject)EditorGUILayout.ObjectField("Hedef Plaka:", _targetPlate, typeof(PlateObject), true);
            }

            _depthOffset = EditorGUILayout.Slider("İnce Derinlik Ofseti:", _depthOffset, -0.04f, 0.04f);
            _mirrorOpposite = EditorGUILayout.Toggle("Karşı Yüzeye Simetrik Ekle", _mirrorOpposite);

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
                Handles.color = _selectedColor.ToUnityColor();
                Handles.DrawWireDisc(hit.point, hit.normal, 0.12f);
                Handles.color = Color.green;
                Handles.DrawLine(hit.point, hit.point + hit.normal * 0.35f);

                HandleUtility.Repaint();

                if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
                {
                    e.Use();
                    PlaceScrewAtHit(hit);
                }
            }
        }

        private void PlaceScrewAtHit(RaycastHit hit)
        {
            PlateObject manualPlate = _autoDetectPlate ? null : _targetPlate;
            ScrewObject placed = ExternalModelLevelTool.PlaceScrewAtWorldSurface(
                hit, manualPlate, _selectedColor, _depthOffset, _mirrorOpposite);

            if (placed != null)
            {
                Debug.Log($"<color=#{ColorUtility.ToHtmlStringRGB(_selectedColor.ToUnityColor())}><b>[SurfaceSnapper]</b> Vida {_selectedColor} yüzey normaline yerleştirildi!</color>");
            }
        }
    }
}

