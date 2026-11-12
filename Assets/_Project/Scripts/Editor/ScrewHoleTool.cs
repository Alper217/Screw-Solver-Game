using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ScrewSolver.Gameplay.Holes;
using ScrewSolver.Gameplay.Screws;

namespace ScrewSolver.Editor
{
    /// <summary>
    /// Sahnedeki ve prefabdaki tüm vidaların altına estetik simsiyah vida delik yuvalarını (HoleBezel)
    /// vidanın yüzeyine tam paralel (düz) ve tam sıfır noktasında yerleştiren editör aracı.
    /// </summary>
    [InitializeOnLoad]
    public static class ScrewHoleTool
    {
        private const string MeshPath = "Assets/Meshes/HoleDiscMesh.asset";
        private const string MatPath = "Assets/Materials/HoleDark.mat";
        private const string PrefabPath = "Assets/Prefabs/Screw.prefab";

        // Vida modelinde vida başlığının oturduğu tam yüzey offseti (Z ekseni boyunca)
        public static readonly Vector3 HoleLocalPosition = new Vector3(0f, 0f, 0.107f);
        public static readonly Quaternion HoleLocalRotation = Quaternion.identity;

        [InitializeOnLoadMethod]
        private static void OnProjectLoaded()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying) return;
                SetupAllHoles();
            };
        }

        [MenuItem("ScrewSolver/Delikler/Tüm Vidalara Siyah Yuva (Hole) Ekle %#h")]
        public static void SetupAllHoles()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying) return;

            Mesh discMesh = GetOrCreateHoleMesh();
            Material darkMat = GetOrCreateDarkMaterial();

            CleanOrphanHolesInScene();
            UpdatePrefabHole(discMesh, darkMat);
            UpdateSceneScrewHoles(discMesh, darkMat);
        }

        /// <summary>
        /// Belirli bir vidanın altına HoleBezel yuvasını ekler veya günceller.
        /// </summary>
        public static void SetupHoleForScrew(GameObject screwGo)
        {
            if (screwGo == null) return;

            Mesh discMesh = GetOrCreateHoleMesh();
            Material darkMat = GetOrCreateDarkMaterial();

            Transform holeTr = screwGo.transform.Find("HoleBezel");
            GameObject holeObj = holeTr != null ? holeTr.gameObject : new GameObject("HoleBezel");

            if (holeTr == null)
            {
                holeObj.transform.SetParent(screwGo.transform, false);
            }

            holeObj.transform.localPosition = HoleLocalPosition;
            holeObj.transform.localRotation = HoleLocalRotation;
            holeObj.transform.localScale = Vector3.one;

            MeshFilter mf = holeObj.GetComponent<MeshFilter>() ?? holeObj.AddComponent<MeshFilter>();
            mf.sharedMesh = discMesh;

            MeshRenderer mr = holeObj.GetComponent<MeshRenderer>() ?? holeObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = darkMat;

            // PuzzleHole ve Tıklama Collider'ı
            if (!holeObj.TryGetComponent<PuzzleHole>(out var hole))
            {
                hole = holeObj.AddComponent<PuzzleHole>();
            }

            if (!holeObj.TryGetComponent<SphereCollider>(out var sc))
            {
                sc = holeObj.AddComponent<SphereCollider>();
            }
            if (sc != null)
            {
                sc.radius = 0.12f;
                sc.center = Vector3.zero;
            }

            ScrewObject screw = screwGo.GetComponent<ScrewObject>();
            if (screw != null && hole != null)
            {
                hole.Occupy(screw);
                screw.CurrentHole = hole;
            }
        }

        private static Mesh GetOrCreateHoleMesh()
        {
            string folder = "Assets/Meshes";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "Meshes");
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null)
            {
                mesh = CreateDiscMeshXY(0.052f, 0.001f, 24);
                mesh.name = "HoleDiscMesh";
                AssetDatabase.CreateAsset(mesh, MeshPath);
                AssetDatabase.SaveAssets();
            }
            else
            {
                // Mevcut mesh'i de XY düzleminde doğru normale güncelle
                UpdateDiscMeshXY(mesh, 0.052f, 0.001f, 24);
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssets();
            }
            return mesh;
        }

        private static Material GetOrCreateDarkMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            if (mat == null)
            {
                mat = new Material(shader) { name = "HoleDark" };
                AssetDatabase.CreateAsset(mat, MatPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Tamamen simsiyah, parlama ve yansıma yapmayan mat delik siyahı
            mat.color = Color.black;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", Color.black);
            }
            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", 0f);
            }
            if (mat.HasProperty("_Metallic"))
            {
                mat.SetFloat("_Metallic", 0f);
            }

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static void CleanOrphanHolesInScene()
        {
            // Sahnede sahipsiz, bir vidaya bağlı olmayan başıboş HoleBezel nesnelerini temizle
            GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var go in allObjects)
            {
                if (go != null && go.name == "HoleBezel" && go.GetComponentInParent<ScrewObject>() == null)
                {
                    Undo.DestroyObjectImmediate(go);
                }
            }
        }

        private static void UpdatePrefabHole(Mesh discMesh, Material darkMat)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) return;

            try
            {
                Transform holeTr = root.transform.Find("HoleBezel");
                GameObject holeObj = holeTr != null ? holeTr.gameObject : new GameObject("HoleBezel");

                if (holeTr == null)
                {
                    holeObj.transform.SetParent(root.transform, false);
                }

                holeObj.transform.localPosition = HoleLocalPosition;
                holeObj.transform.localRotation = HoleLocalRotation;
                holeObj.transform.localScale = Vector3.one;

                MeshFilter mf = holeObj.GetComponent<MeshFilter>() ?? holeObj.AddComponent<MeshFilter>();
                mf.sharedMesh = discMesh;

                MeshRenderer mr = holeObj.GetComponent<MeshRenderer>() ?? holeObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = darkMat;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("<color=green>[ScrewHoleTool] Screw.prefab güncellendi: HoleBezel tam yüzeye oturtuldu ve siyah yapıldı!</color>");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void UpdateSceneScrewHoles(Mesh discMesh, Material darkMat)
        {
            ScrewObject[] screws = Object.FindObjectsByType<ScrewObject>(FindObjectsSortMode.None);
            if (screws == null || screws.Length == 0) return;

            int updated = 0;
            foreach (var screw in screws)
            {
                if (screw == null) continue;

                Transform holeTr = screw.transform.Find("HoleBezel");
                GameObject holeObj = holeTr != null ? holeTr.gameObject : new GameObject("HoleBezel");

                if (holeTr == null)
                {
                    holeObj.transform.SetParent(screw.transform, false);
                    Undo.RegisterCreatedObjectUndo(holeObj, "Add HoleBezel");
                }
                else
                {
                    Undo.RecordObject(holeObj.transform, "Adjust HoleBezel Transform");
                }

                holeObj.transform.localPosition = HoleLocalPosition;
                holeObj.transform.localRotation = HoleLocalRotation;
                holeObj.transform.localScale = Vector3.one;

                MeshFilter mf = holeObj.GetComponent<MeshFilter>() ?? holeObj.AddComponent<MeshFilter>();
                mf.sharedMesh = discMesh;

                MeshRenderer mr = holeObj.GetComponent<MeshRenderer>() ?? holeObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = darkMat;

                if (!holeObj.TryGetComponent<PuzzleHole>(out var hole))
                {
                    hole = holeObj.AddComponent<PuzzleHole>();
                }

                if (!holeObj.TryGetComponent<SphereCollider>(out var sc))
                {
                    sc = holeObj.AddComponent<SphereCollider>();
                }
                if (sc != null)
                {
                    sc.radius = 0.12f;
                    sc.center = Vector3.zero;
                }

                hole.Occupy(screw);
                screw.CurrentHole = hole;

                updated++;
            }

            if (updated > 0 && !Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log($"<color=green>[ScrewHoleTool] Sahnedeki {updated} vidanın yuvası düzeltildi: Dik duruş giderildi, simsiyah yapıldı!</color>");
            }
        }

        private static Mesh CreateDiscMeshXY(float radius, float depth, int segments)
        {
            Mesh mesh = new Mesh { name = "HoleDiscMesh" };
            UpdateDiscMeshXY(mesh, radius, depth, segments);
            return mesh;
        }

        private static void UpdateDiscMeshXY(Mesh mesh, float radius, float depth, int segments)
        {
            mesh.Clear();

            // Vida modelinin yüzey düzlemi XY'dir (normal +Z yönündedir).
            // Dolayısıyla diski XY düzleminde oluşturuyoruz ki vida başlığının altına tam paralel otursun.
            int vertexCount = (segments + 1) * 2;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[segments * 6];

            // Ön merkez (+Z)
            vertices[0] = new Vector3(0f, 0f, depth);
            normals[0] = Vector3.forward;
            uvs[0] = new Vector2(0.5f, 0.5f);

            // Arka merkez (-Z)
            vertices[segments + 1] = Vector3.zero;
            normals[segments + 1] = Vector3.back;
            uvs[segments + 1] = new Vector2(0.5f, 0.5f);

            float angleStep = 360f / segments;
            for (int i = 0; i < segments; i++)
            {
                float rad = Mathf.Deg2Rad * (i * angleStep);
                float x = Mathf.Cos(rad) * radius;
                float y = Mathf.Sin(rad) * radius;

                // Ön çember (+Z)
                int topIdx = i + 1;
                vertices[topIdx] = new Vector3(x, y, depth);
                normals[topIdx] = Vector3.forward;
                uvs[topIdx] = new Vector2(0.5f + (x / radius) * 0.5f, 0.5f + (y / radius) * 0.5f);

                // Arka çember (0)
                int botIdx = segments + 2 + i;
                vertices[botIdx] = new Vector3(x, y, 0f);
                normals[botIdx] = Vector3.back;
                uvs[botIdx] = uvs[topIdx];

                int next = (i + 1) % segments;
                // Ön yüzey (CCW)
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = topIdx;
                triangles[i * 3 + 2] = next + 1;

                // Arka yüzey (CW)
                int botBase = segments * 3;
                triangles[botBase + i * 3] = segments + 1;
                triangles[botBase + i * 3 + 1] = segments + 2 + next;
                triangles[botBase + i * 3 + 2] = botIdx;
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }
    }
}
