using UnityEngine;

namespace ScrewSolver.Gameplay.Plates
{
    /// <summary>
    /// Plaka veya gövde üzerindeki vida deliğini temsil eder.
    /// Vida bu deliğin üzerinde oturur. Vida sökülüp kutuya uçtuğunda
    /// delik plakanın üzerinde kalır ve boş vida yuvası hissini verir.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public sealed class ScrewHole : MonoBehaviour
    {
        [Header("Görsel Ayarlar")]
        [SerializeField] private Material _holeMaterial;
        [SerializeField] private float _radius = 0.052f;
        [SerializeField] private float _depth = 0.002f;

        private void Reset()
        {
            SetupVisual();
        }

        private void OnValidate()
        {
            SetupVisual();
        }

        [ContextMenu("Setup Hole Visual")]
        public void SetupVisual()
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();

            if (mf.sharedMesh == null)
            {
                mf.sharedMesh = CreateHoleDiscMesh(_radius, _depth, 24);
            }

            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();

#if UNITY_EDITOR
            if (_holeMaterial == null)
            {
                _holeMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/HoleDark.mat");
            }
#endif
            if (_holeMaterial != null)
            {
                mr.sharedMaterial = _holeMaterial;
            }
        }

        private static Mesh CreateHoleDiscMesh(float radius, float depth, int segments)
        {
            Mesh mesh = new Mesh { name = "ScrewHole_Mesh" };

            // Hafif kalınlığı olan çift yüzeyli zarif bir pul
            int vertexCount = (segments + 1) * 2;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[segments * 6];

            // Üst merkez
            vertices[0] = new Vector3(0f, depth, 0f);
            normals[0] = Vector3.up;
            uvs[0] = new Vector2(0.5f, 0.5f);

            // Alt merkez
            vertices[segments + 1] = Vector3.zero;
            normals[segments + 1] = Vector3.down;
            uvs[segments + 1] = new Vector2(0.5f, 0.5f);

            float angleStep = 360f / segments;
            for (int i = 0; i < segments; i++)
            {
                float rad = Mathf.Deg2Rad * (i * angleStep);
                float x = Mathf.Cos(rad) * radius;
                float z = Mathf.Sin(rad) * radius;

                // Üst çember
                int topIdx = i + 1;
                vertices[topIdx] = new Vector3(x, depth, z);
                normals[topIdx] = Vector3.up;
                uvs[topIdx] = new Vector2(0.5f + (x / radius) * 0.5f, 0.5f + (z / radius) * 0.5f);

                // Alt çember
                int botIdx = segments + 2 + i;
                vertices[botIdx] = new Vector3(x, 0f, z);
                normals[botIdx] = Vector3.down;
                uvs[botIdx] = uvs[topIdx];

                int next = (i + 1) % segments;
                // Üst yüzey üçgenleri
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = topIdx;
                triangles[i * 3 + 2] = next + 1;

                // Alt yüzey üçgenleri
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
            return mesh;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.1f, 0.1f, 0.12f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}

