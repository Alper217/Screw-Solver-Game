using System;
using System.Collections.Generic;
using ScrewSolver.Config;
using ScrewSolver.Core;
using ScrewSolver.Editor.Solver;
using ScrewSolver.Gameplay.Level;
using ScrewSolver.Gameplay.Plates;
using ScrewSolver.Gameplay.Screws;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScrewSolver.Editor.Generator
{
    /// <summary>
    /// Blender/Maya'dan veya dış Asset paketlerinden gelen parçalı 3D modelleri (FBX)
    /// tek tıkla sökülebilir plaka (PlateObject) + vida + fiziksel engel + HeadlessLevelSolver
    /// onaylı seviye prefab'ına dönüştüren stüdyo boru hattı aracı.
    /// </summary>
    public static class ExternalModelLevelTool
    {
        private const string ScrewPrefabPath = "Assets/Prefabs/Screw.prefab";
        private const string AnimConfigPath = "Assets/ScrewAnimationConfig.asset";

        /// <summary>
        /// Dış 3D modeldeki alt parçaları tarar: çekirdek/gövde anahtar kelimeleriyle eşleşenleri
        /// düşmeyen ana iskelet olarak bırakır; diğer tüm parçalara Convex Collider + Kinematic Rigidbody + PlateObject kurar.
        /// </summary>
        public static int ConvertSubMeshesToPlates(GameObject root, string chassisKeywordsCsv = "Core,Chassis,Body,Frame,Skeleton,Base")
        {
            if (root == null) return 0;

            // Dış FBX doğrudan Model Prefab olarak sahneye sürüklendiyse marker silme ve prefab kaydetme için unpack et
            if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(root);
                if (instanceRoot == root && PrefabUtility.GetPrefabAssetType(root) == PrefabAssetType.Model)
                {
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.UserAction);
                }
            }

            string[] keywords = (chassisKeywordsCsv ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < keywords.Length; i++)
            {
                keywords[i] = keywords[i].Trim();
            }

            int convertedCount = 0;

            // 0. Dış Asset Store prefab'larından kalma kaba Box/Sphere/Capsule proxy collider'ları pasifleştir
            //    (24 vertex'li standart Unity Cube plakaları korur, organik FBX proxy küre/kapsüllerini susturur)
            Collider[] allCols = root.GetComponentsInChildren<Collider>(true);
            foreach (Collider col in allCols)
            {
                if (col == null) continue;
                if (col.GetComponentInParent<ScrewObject>() != null) continue;
                if (col.GetComponentInParent<ScrewSolver.Gameplay.Holes.PuzzleHole>() != null) continue;
                if (col.gameObject.name.Equals("HoleBezel", StringComparison.OrdinalIgnoreCase)) continue;

                if (col is SphereCollider || col is CapsuleCollider)
                {
                    Undo.RecordObject(col, "Disable Proxy Collider");
                    col.enabled = false;
                    continue;
                }

                if (col is BoxCollider)
                {
                    MeshFilter boxMf = col.GetComponent<MeshFilter>();
                    bool isUnityCube = boxMf != null && boxMf.sharedMesh != null &&
                                       boxMf.sharedMesh.name == "Cube" &&
                                       boxMf.sharedMesh.vertexCount == 24;
                    if (!isUnityCube)
                    {
                        Undo.RecordObject(col, "Disable Proxy BoxCollider");
                        col.enabled = false;
                    }
                }
            }

            // 1. Hem MeshRenderer hem SkinnedMeshRenderer (namlu, hareketli parça vb.) taraması
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                if (r.GetComponentInParent<ScrewObject>() != null) continue;
                if (r.name.Equals("HoleBezel", StringComparison.OrdinalIgnoreCase)) continue;

                Mesh mesh = null;
                if (r is MeshRenderer)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                }
                else if (r is SkinnedMeshRenderer smr)
                {
                    mesh = smr.sharedMesh;
                }

                if (mesh == null) continue;

                // Alt parça bile olsa yüzeyine tıklayıp vida koyabilmek için mutlaka tam MeshCollider kur
                EnsureExactMeshCollider(r.gameObject, mesh);

                if (r.gameObject == root) continue;

                // Zaten başka bir PlateObject'in görsel alt parçasıysa (ör. ana gövdenin namlusu)
                // collider kuruldu, ancak ikinci kez PlateObject bileşeni ekleme
                PlateObject parentPlate = r.transform.parent != null ? r.transform.parent.GetComponentInParent<PlateObject>() : null;
                if (parentPlate != null && parentPlate.gameObject != r.gameObject) continue;

                bool isChassis = IsNameMatchingAny(r.name, keywords);
                if (isChassis) continue;

                EnsurePlatePhysicsAndComponent(r.gameObject, mesh);
                convertedCount++;
            }

            Physics.SyncTransforms();
            return convertedCount;
        }

        private static void EnsureExactMeshCollider(GameObject go, Mesh mesh)
        {
            if (go == null || mesh == null) return;

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            // Eğer sahnede halihazırda etkin bir 24-vertex Cube BoxCollider'ı varsa koru
            Collider existing = go.GetComponent<Collider>();
            if (existing != null && existing.enabled && !(existing is MeshCollider))
            {
                return;
            }

            MeshCollider mc = go.GetComponent<MeshCollider>();
            if (mc == null)
            {
                mc = Undo.AddComponent<MeshCollider>(go);
            }

            if (mc != null)
            {
                if (mc.sharedMesh != mesh) mc.sharedMesh = mesh;
                // convex = false: girintili çıkıntılı modellerde streç-film boşluğu oluşturmaz, tam yüzeye vurur!
                mc.convex = false;
                mc.enabled = true;
            }
        }

        /// <summary>
        /// Sanatçının 3D model içine bıraktığı konum işaretleyicilerini (ör. Pin_01, Screw_01, Locator_*)
        /// otomatik olarak gerçek Screw.prefab + HoleBezel yuvasına dönüştürür.
        /// </summary>
        public static int ConvertMarkerTransformsToScrews(GameObject root, string markerPrefixesCsv = "Screw_,Pin_,Bolt_,Socket_")
        {
            if (root == null) return 0;

            GameObject screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScrewPrefabPath);
            ScrewAnimationConfigSO animConfig = AssetDatabase.LoadAssetAtPath<ScrewAnimationConfigSO>(AnimConfigPath);
            if (screwPrefab == null) return 0;

            string[] prefixes = (markerPrefixesCsv ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < prefixes.Length; i++)
            {
                prefixes[i] = prefixes[i].Trim();
            }

            List<Transform> markers = new();
            Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in allTransforms)
            {
                if (t == null || t == root.transform) continue;
                if (t.GetComponent<ScrewObject>() != null || t.GetComponentInParent<ScrewObject>() != null) continue;
                if (IsNameStartingWithAny(t.name, prefixes))
                {
                    markers.Add(t);
                }
            }

            int created = 0;
            foreach (Transform marker in markers)
            {
                PlateObject markerPlate = marker.GetComponentInParent<PlateObject>();
                Transform parentPlateTr = markerPlate != null ? markerPlate.transform : marker.parent;
                GameObject screwGo = (GameObject)PrefabUtility.InstantiatePrefab(screwPrefab, parentPlateTr);
                Undo.RegisterCreatedObjectUndo(screwGo, "Create Screw From Marker");
                screwGo.name = marker.name;
                screwGo.transform.position = marker.position;
                screwGo.transform.rotation = marker.rotation;
                screwGo.transform.localScale = Vector3.one;

                ScrewObject screw = screwGo.GetComponent<ScrewObject>();
                if (screw != null && animConfig != null)
                {
                    SerializedObject so = new SerializedObject(screw);
                    so.FindProperty("_animationConfig").objectReferenceValue = animConfig;
                    so.FindProperty("_parentPlate").objectReferenceValue = parentPlateTr.GetComponent<PlateObject>();
                    so.ApplyModifiedProperties();
                }

                ScrewHoleTool.SetupHoleForScrew(screwGo);
                Undo.DestroyObjectImmediate(marker.gameObject);
                created++;
            }

            return created;
        }

        /// <summary>
        /// Seçili modeldeki plakaların dış yüzeylerine otomatik ışın atarak (Raycast Normal)
        /// henüz vidası olmayan plakalara 2 veya 4 adet vida yerleştirir.
        /// </summary>
        public static int AutoPopulateMissingPlateScrews(GameObject root, int defaultScrewsPerPlate = 2)
        {
            if (root == null) return 0;

            GameObject screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScrewPrefabPath);
            ScrewAnimationConfigSO animConfig = AssetDatabase.LoadAssetAtPath<ScrewAnimationConfigSO>(AnimConfigPath);
            if (screwPrefab == null) return 0;

            Physics.SyncTransforms();
            PlateObject[] plates = root.GetComponentsInChildren<PlateObject>(true);
            int added = 0;

            foreach (PlateObject plate in plates)
            {
                if (plate == null) continue;
                if (plate.GetComponentsInChildren<ScrewObject>(true).Length > 0) continue;

                Collider col = plate.GetComponent<Collider>();
                if (col == null) continue;

                Bounds b = col.bounds;
                Vector3 center = b.center;
                Vector3 ext = b.extents;

                // En ince ekseni yüzey normali kabul et (genelde levha/plaka kalınlık ekseni)
                Vector3[] candidateDirs =
                {
                    plate.transform.forward, -plate.transform.forward,
                    plate.transform.right, -plate.transform.right,
                    plate.transform.up, -plate.transform.up
                };

                // Merkeze göre dışa bakan en iyi yüzeyi seç
                Vector3 fromRoot = (center - root.transform.position);
                Vector3 bestNormal = plate.transform.forward;
                float bestDot = float.NegativeInfinity;
                foreach (Vector3 dir in candidateDirs)
                {
                    float d = fromRoot.sqrMagnitude > 0.001f ? Vector3.Dot(dir.normalized, fromRoot.normalized) : Vector3.Dot(dir.normalized, Vector3.forward);
                    if (d > bestDot)
                    {
                        bestDot = d;
                        bestNormal = dir.normalized;
                    }
                }

                Vector3 tangentU = Vector3.Cross(bestNormal, Mathf.Abs(Vector3.Dot(bestNormal, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 tangentV = Vector3.Cross(bestNormal, tangentU).normalized;

                float spanU = Mathf.Abs(Vector3.Dot(ext, tangentU)) * 0.48f;
                float spanV = Mathf.Abs(Vector3.Dot(ext, tangentV)) * 0.48f;
                int count = Mathf.Clamp(defaultScrewsPerPlate, 1, 4);

                Vector2[] offsets = count switch
                {
                    1 => new[] { Vector2.zero },
                    2 => spanU >= spanV
                        ? new[] { new Vector2(-spanU, 0f), new Vector2(spanU, 0f) }
                        : new[] { new Vector2(0f, -spanV), new Vector2(0f, spanV) },
                    _ => new[]
                    {
                        new Vector2(-spanU, spanV), new Vector2(spanU, spanV),
                        new Vector2(-spanU, -spanV), new Vector2(spanU, -spanV)
                    }
                };

                float castDist = b.size.magnitude + 1f;
                for (int i = 0; i < offsets.Length; i++)
                {
                    Vector3 sampleOrigin = center + tangentU * offsets[i].x + tangentV * offsets[i].y + bestNormal * castDist;
                    Ray ray = new Ray(sampleOrigin, -bestNormal);

                    if (col.Raycast(ray, out RaycastHit hit, castDist * 2f))
                    {
                        InstantiateConfiguredScrew(
                            screwPrefab, animConfig, hit.point, hit.normal,
                            plate, plate.transform, ColorType.Blue, 0f, $"Screw_{plate.name}_{i + 1}");
                        added++;
                    }
                }
            }

            return added;
        }

        public struct SurfaceSeatingInfo
        {
            public RaycastHit CenterHit;
            public Vector3 SmoothedNormal;
            public float RecommendedSinkOffset;
            public int SupportedRimPoints;
            public bool IsFlushEnough => SupportedRimPoints >= 3;
        }

        /// <summary>
        /// Görünür model yüzeyine (gizlenmiş plakaları ve vidaları atlayarak) raycast atar.
        /// </summary>
        public static bool TryRaycastVisibleModelSurface(Ray ray, GameObject targetRoot, out RaycastHit bestHit)
        {
            bestHit = default;
            RaycastHit[] hits = Physics.RaycastAll(ray, 200f);
            float minDist = float.MaxValue;
            bool found = false;

            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || !h.collider.enabled) continue;
                if (h.collider.GetComponentInParent<ScrewObject>() != null) continue;
                if (h.collider.GetComponentInParent<ScrewSolver.Gameplay.Holes.PuzzleHole>() != null) continue;
                if (targetRoot != null && !h.collider.transform.IsChildOf(targetRoot.transform)) continue;

                PlateObject plate = h.collider.GetComponentInParent<PlateObject>();
                if (SceneVisibilityManager.instance.IsHidden(h.collider.gameObject) ||
                    (plate != null && SceneVisibilityManager.instance.IsHidden(plate.gameObject)))
                {
                    continue;
                }

                if (h.distance < minDist)
                {
                    minDist = h.distance;
                    bestHit = h;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Tıklanan noktanın çevresinde 4 yönlü pul yarıçapı (rim) örneklemesi yaparak eğimli yüzeylerde
        /// vidanın havada/açıkta kalmasını önleyen yumuşatılmış normal ve gömme payını hesaplar.
        /// </summary>
        public static SurfaceSeatingInfo AnalyzeSurfaceSeating(RaycastHit centerHit, GameObject targetRoot, float probeRadius = 0.055f)
        {
            Vector3 n = centerHit.normal.sqrMagnitude > 0.0001f ? centerHit.normal.normalized : Vector3.forward;
            Vector3 upHint = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.92f ? Vector3.forward : Vector3.up;
            Vector3 tangentU = Vector3.Cross(n, upHint).normalized;
            Vector3 tangentV = Vector3.Cross(n, tangentU).normalized;

            Vector3[] offsets = { tangentU, -tangentU, tangentV, -tangentV };
            Vector3 normalSum = n;
            int supported = 0;
            float positiveRecessSum = 0f;
            int positiveRecessCount = 0;

            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 origin = centerHit.point + offsets[i] * probeRadius + n * 0.06f;
                Ray r = new Ray(origin, -n);
                if (TryRaycastVisibleModelSurface(r, targetRoot, out RaycastHit rimHit) && rimHit.distance <= 0.16f)
                {
                    supported++;
                    Vector3 rimN = rimHit.normal.sqrMagnitude > 0.0001f ? rimHit.normal.normalized : n;
                    if (Vector3.Dot(rimN, n) > 0.55f)
                    {
                        normalSum += rimN;
                        float recess = Vector3.Dot(centerHit.point - rimHit.point, n);
                        if (recess > 0.0005f)
                        {
                            positiveRecessSum += recess;
                            positiveRecessCount++;
                        }
                    }
                }
            }

            Vector3 smoothed = normalSum.sqrMagnitude > 0.0001f ? normalSum.normalized : n;

            // Dışbükey (convex) yüzeylerde vidayı içeri gömmek siyah yuvayı ve başlığı mesh'in altına iter;
            // bunun yerine sıfır derinlikten oturup ConformScrewAndHoleToSurface ile yüzeye şekillendirilir.
            return new SurfaceSeatingInfo
            {
                CenterHit = centerHit,
                SmoothedNormal = smoothed,
                RecommendedSinkOffset = 0f,
                SupportedRimPoints = supported
            };
        }

        /// <summary>
        /// Sahne raycast vuruş noktasında yüzeye tam dik (90°) bir vida oluşturur;
        /// eğimli/yuvarlak yüzeylerde hem siyah yuvayı (HoleBezel) hem vida başlığını yüzeye göre şekillendirir.
        /// </summary>
        public static ScrewObject PlaceScrewAtWorldSurface(
            RaycastHit hit,
            PlateObject targetPlate,
            ColorType color,
            float fineDepthOffset = 0f,
            bool mirrorOppositeSide = false,
            GameObject targetRootContext = null,
            bool autoSeatCurvature = true)
        {
            GameObject screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScrewPrefabPath);
            ScrewAnimationConfigSO animConfig = AssetDatabase.LoadAssetAtPath<ScrewAnimationConfigSO>(AnimConfigPath);
            if (screwPrefab == null || hit.collider == null) return null;

            PlateObject plate = targetPlate != null ? targetPlate : hit.collider.GetComponentInParent<PlateObject>();
            Transform parentTr = plate != null ? plate.transform : hit.collider.transform;

            Vector3 placeNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.forward;
            Vector3 exactHitPoint = hit.point;
            GameObject searchRoot = ResolveSearchRoot(hit.collider.transform, targetRootContext);

            if (autoSeatCurvature)
            {
                SurfaceSeatingInfo seating = AnalyzeSurfaceSeating(hit, searchRoot);
                placeNormal = seating.SmoothedNormal;

                // Yumuşatılmış eksen boyunca merkez temas noktasını tekrar yakala
                Ray alignRay = new Ray(hit.point + placeNormal * 0.14f, -placeNormal);
                if (TryRaycastVisibleModelSurface(alignRay, searchRoot, out RaycastHit alignedHit) && alignedHit.distance <= 0.30f)
                {
                    exactHitPoint = alignedHit.point;
                    if (targetPlate == null)
                    {
                        PlateObject alignedPlate = alignedHit.collider.GetComponentInParent<PlateObject>();
                        if (alignedPlate != null)
                        {
                            plate = alignedPlate;
                            parentTr = alignedPlate.transform;
                        }
                    }
                }
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Place Manual Screw");

            ScrewObject mainScrew = InstantiateConfiguredScrew(
                screwPrefab, animConfig, exactHitPoint, placeNormal,
                plate, parentTr, color, fineDepthOffset, $"Screw_{color}_{DateTime.Now:HHmmssfff}");

            if (mainScrew != null && autoSeatCurvature)
            {
                ConformScrewAndHoleToSurface(mainScrew, searchRoot);
            }

            if (mirrorOppositeSide)
            {
                Ray oppositeRay = new Ray(exactHitPoint - placeNormal * 6f, placeNormal);
                if (hit.collider.Raycast(oppositeRay, out RaycastHit oppHit, 6f) &&
                    Vector3.Distance(oppHit.point, exactHitPoint) > 0.04f)
                {
                    ScrewObject oppScrew = InstantiateConfiguredScrew(
                        screwPrefab, animConfig, oppHit.point, oppHit.normal,
                        plate, parentTr, color, fineDepthOffset, $"Screw_{color}_Sym_{DateTime.Now:fff}");
                    if (oppScrew != null && autoSeatCurvature)
                    {
                        ConformScrewAndHoleToSurface(oppScrew, searchRoot);
                    }
                }
            }

            if (plate != null)
            {
                Undo.RecordObject(plate, "Update Plate Attached Screws");
                plate.AutoFindAttachedScrews();
                EditorUtility.SetDirty(plate);
            }

            Undo.CollapseUndoOperations(undoGroup);
            return mainScrew;
        }

        private static GameObject ResolveSearchRoot(Transform hitTransform, GameObject targetRootContext)
        {
            if (targetRootContext != null) return targetRootContext;
            if (hitTransform == null) return null;

            GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
            if (puzzleRoot != null && hitTransform.IsChildOf(puzzleRoot.transform))
            {
                Transform t = hitTransform;
                while (t.parent != null && t.parent != puzzleRoot.transform) t = t.parent;
                return t.gameObject;
            }
            return hitTransform.root.gameObject;
        }

        /// <summary>
        /// Mevcut bir vidanın rengini Undo destekli olarak anında değiştirir.
        /// </summary>
        public static void PaintScrewColor(ScrewObject screw, ColorType newColor)
        {
            if (screw == null) return;

            Undo.RecordObject(screw, "Paint Screw Color");
            SerializedObject so = new SerializedObject(screw);
            so.FindProperty("_color").enumValueIndex = (int)newColor;
            so.ApplyModifiedProperties();

            screw.ApplyColorVisual();
            EditorUtility.SetDirty(screw);
        }

        /// <summary>
        /// Bir vidayı siler ve bağlı olduğu plakanın vida listesini temizler.
        /// </summary>
        public static void RemoveScrewCleanly(ScrewObject screw)
        {
            if (screw == null) return;
            PlateObject parentPlate = screw.ParentPlate != null ? screw.ParentPlate : screw.GetComponentInParent<PlateObject>();

            Undo.DestroyObjectImmediate(screw.gameObject);
            if (parentPlate != null)
            {
                Undo.RecordObject(parentPlate, "Refresh Plate Screws After Delete");
                parentPlate.AutoFindAttachedScrews();
                EditorUtility.SetDirty(parentPlate);
            }
        }

        /// <summary>
        /// Assets/Prefabs/Levels içindeki mevcut Level_XX prefab'larını tarayıp bir sonraki boş seviye numarasını döndürür.
        /// </summary>
        public static int GetNextAvailableLevelNumber()
        {
            if (!AssetDatabase.IsValidFolder(ParametricLevelArchitect.LevelsFolder))
            {
                return 1;
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ParametricLevelArchitect.LevelsFolder });
            int maxNum = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (fileName.StartsWith("Level_", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(fileName.Substring(6), out int num) &&
                    num > maxNum)
                {
                    maxNum = num;
                }
            }
            return maxNum + 1;
        }

        /// <summary>
        /// Dış FBX / Model asset'ini sahnedeki PuzzleRoot içine geçici tasarım tezgahı olarak çıkarır:
        /// mevcut sahne levellerini geçici gizler, modeli merkezler, boyutunu otomatik normalize eder ve
        /// alt parçaları PlateObject yapıp Scene kamerasını odaklar.
        /// </summary>
        public static GameObject SpawnFbxIntoDesignWorkbench(
            GameObject sourceModelAsset,
            int targetLevelNumber,
            string chassisKeywordsCsv,
            out string statusMessage,
            float targetLocalExtent = 1.65f)
        {
            if (sourceModelAsset == null)
            {
                statusMessage = "Lütfen önce bir FBX veya 3D model dosyası sürükleyin.";
                return null;
            }

            GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
            if (puzzleRoot == null)
            {
                puzzleRoot = new GameObject("PuzzleRoot");
                puzzleRoot.transform.localScale = Vector3.one * 0.7f;
            }

            // Varsa eski yarım kalmış tezgahı temizle, mevcut levelleri geçici pasife al
            for (int i = puzzleRoot.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = puzzleRoot.transform.GetChild(i);
                if (child.name.StartsWith("__Workbench_", StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
                else
                {
                    child.gameObject.SetActive(false);
                }
            }

            GameObject workbenchRoot = new GameObject($"__Workbench_Level_{targetLevelNumber:00}");
            workbenchRoot.transform.SetParent(puzzleRoot.transform, false);
            workbenchRoot.transform.localPosition = Vector3.zero;
            workbenchRoot.transform.localRotation = Quaternion.identity;
            workbenchRoot.transform.localScale = Vector3.one;

            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(sourceModelAsset, workbenchRoot.transform);
            if (modelInstance == null)
            {
                modelInstance = UnityEngine.Object.Instantiate(sourceModelAsset, workbenchRoot.transform);
            }
            modelInstance.name = sourceModelAsset.name;

            if (PrefabUtility.IsPartOfPrefabInstance(modelInstance))
            {
                PrefabUtility.UnpackPrefabInstance(modelInstance, PrefabUnpackMode.Completely, InteractionMode.UserAction);
            }

            // Modeli kendi ebeveyni içinde otomatik boyutlandır (~targetLocalExtent) ve geometrik merkeze oturt
            CenterAndNormalizeModel(workbenchRoot, modelInstance, Mathf.Clamp(targetLocalExtent, 0.4f, 4.5f));

            int plateCount = ConvertSubMeshesToPlates(workbenchRoot, chassisKeywordsCsv);

            Physics.SyncTransforms();
            if (SceneView.lastActiveSceneView != null)
            {
                Bounds bounds = CalculateHierarchyBounds(workbenchRoot);
                SceneView.lastActiveSceneView.Frame(bounds, false);
            }

            EditorSceneManager.MarkSceneDirty(puzzleRoot.scene);
            Selection.activeGameObject = workbenchRoot;

            statusMessage = $"'{sourceModelAsset.name}' sahneye çıkarıldı ({plateCount} parça plakaya dönüştürüldü). Şimdi sahne üzerinden vidaları ekleyin ve alttan Onaylayın.";
            return workbenchRoot;
        }

        /// <summary>
        /// Geçici tasarım tezgahını temizleyip PuzzleRoot içindeki asıl sahne seviyesini (Level_01 vb.) geri yükler.
        /// </summary>
        public static void RestorePuzzleRootFromWorkbench(GameObject workbenchRoot)
        {
            SceneVisibilityManager.instance.ShowAll();

            if (workbenchRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(workbenchRoot);
            }

            GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
            if (puzzleRoot == null) return;

            for (int i = puzzleRoot.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = puzzleRoot.transform.GetChild(i);
                if (child.name.StartsWith("__Workbench_", StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            for (int i = 0; i < puzzleRoot.transform.childCount; i++)
            {
                puzzleRoot.transform.GetChild(i).gameObject.SetActive(true);
            }

            EditorSceneManager.MarkSceneDirty(puzzleRoot.scene);
            EditorSceneManager.SaveScene(puzzleRoot.scene);
        }

        private static void CenterAndNormalizeModel(GameObject workbenchRoot, GameObject modelInstance, float targetLocalExtent)
        {
            Bounds initialBounds = CalculateHierarchyBounds(modelInstance);
            float scaleRef = Mathf.Max(0.01f, Mathf.Abs(workbenchRoot.transform.lossyScale.x));
            float worldMaxDim = Mathf.Max(initialBounds.size.x, Mathf.Max(initialBounds.size.y, initialBounds.size.z));

            if (worldMaxDim > 0.0001f)
            {
                float currentLocalDim = worldMaxDim / scaleRef;
                if (Mathf.Abs(currentLocalDim - targetLocalExtent) > 0.1f)
                {
                    float multiplier = targetLocalExtent / currentLocalDim;
                    modelInstance.transform.localScale *= multiplier;
                }
            }

            Bounds scaledBounds = CalculateHierarchyBounds(modelInstance);
            Vector3 worldOffset = workbenchRoot.transform.position - scaledBounds.center;
            modelInstance.transform.position += worldOffset;
        }

        private static Bounds CalculateHierarchyBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one * 1.5f);
            }

            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                combined.Encapsulate(renderers[i].bounds);
            }
            return combined;
        }

        /// <summary>
        /// Tasarımcının elle yerleştirdiği vida renklerini bozmadan LevelConfig + Prefab olarak kaydeder.
        /// </summary>
        public static GameObject SaveManualLevelPrefab(
            GameObject targetRoot,
            int levelNumber,
            string title,
            LevelDifficulty difficulty,
            float timeLimitSeconds,
            IReadOnlyList<ColorType> boxQueue,
            HeadlessLevelSolver.SolveReport report,
            bool wireToLevelManager,
            out string statusMessage,
            bool connectSceneInstance = true)
        {
            if (targetRoot == null)
            {
                statusMessage = "Hedef sahne objesi seçili değil!";
                return null;
            }

            EnsureScrewsWiredAndReady(targetRoot);

            LevelConfig config = targetRoot.GetComponent<LevelConfig>();
            if (config == null) config = Undo.AddComponent<LevelConfig>(targetRoot);
            bool verified = report != null && report.IsSolvable;
            List<ColorType> queueCopy = boxQueue != null ? new List<ColorType>(boxQueue) : new List<ColorType>();
            config.Configure(
                levelNumber,
                title,
                difficulty,
                timeLimitSeconds,
                queueCopy,
                verified,
                report != null ? report.MonteCarloWinRate : 0f,
                report != null ? report.SolutionSteps : 0,
                report != null ? report.MaxConcurrentHolesUsed : 0,
                report != null ? report.Summary : "Manuel kaydedildi (Solver testi çalıştırılmadı)");
            EditorUtility.SetDirty(config);

            if (!AssetDatabase.IsValidFolder(ParametricLevelArchitect.LevelsFolder))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs", "Levels");
            }

            string levelName = $"Level_{levelNumber:00}";
            targetRoot.name = levelName;
            string prefabPath = $"{ParametricLevelArchitect.LevelsFolder}/{levelName}.prefab";

            PersistConformedMeshesForLevel(targetRoot, levelNumber);

            GameObject savedPrefab = connectSceneInstance
                ? PrefabUtility.SaveAsPrefabAssetAndConnect(targetRoot, prefabPath, InteractionMode.UserAction)
                : PrefabUtility.SaveAsPrefabAsset(targetRoot, prefabPath);

            if (wireToLevelManager && savedPrefab != null)
            {
                RebuildSortedLevelManagerList();
            }

            AssetDatabase.SaveAssets();
            statusMessage = $"Kaydedildi: {prefabPath} ({(verified ? "Solver Onaylı ✓" : "Test Edilmedi / Onaysız")})";
            return savedPrefab;
        }

        public readonly struct SavedLevelEntry
        {
            public readonly int LevelNumber;
            public readonly string Title;
            public readonly GameObject Prefab;
            public readonly int ScrewCount;

            public SavedLevelEntry(int levelNumber, string title, GameObject prefab, int screwCount)
            {
                LevelNumber = levelNumber;
                Title = title;
                Prefab = prefab;
                ScrewCount = screwCount;
            }
        }

        /// <summary>
        /// Kayıtlı tüm seviye prefab'larını (Assets/Prefabs/Levels ve opsiyonel olarak LevelManager) sayısal sırayla döndürür.
        /// </summary>
        public static List<SavedLevelEntry> GetSortedSavedLevels(bool includeLevelManagerExtras = true)
        {
            List<SavedLevelEntry> entries = new();
            HashSet<GameObject> seen = new();

            void TryAddPrefab(GameObject prefab, int fallbackIndex = int.MaxValue)
            {
                if (prefab == null || !seen.Add(prefab)) return;

                LevelConfig cfg = prefab.GetComponent<LevelConfig>();
                int num = cfg != null ? cfg.LevelNumber : int.MaxValue;
                if (num == int.MaxValue)
                {
                    string fileName = prefab.name;
                    if (fileName.StartsWith("Level_", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(fileName.Substring(6), out int parsed))
                    {
                        num = parsed;
                    }
                    else if (fallbackIndex != int.MaxValue)
                    {
                        num = fallbackIndex;
                    }
                }

                string title = cfg != null && !string.IsNullOrEmpty(cfg.LevelTitle)
                    ? cfg.LevelTitle
                    : prefab.name;
                int screwCount = prefab.GetComponentsInChildren<ScrewObject>(true).Length;
                entries.Add(new SavedLevelEntry(num, title, prefab, screwCount));
            }

            if (AssetDatabase.IsValidFolder(ParametricLevelArchitect.LevelsFolder))
            {
                string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ParametricLevelArchitect.LevelsFolder });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    TryAddPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                }
            }

            if (includeLevelManagerExtras)
            {
                LevelManager lm = UnityEngine.Object.FindFirstObjectByType<LevelManager>();
                if (lm != null)
                {
                    SerializedObject lmSo = new SerializedObject(lm);
                    SerializedProperty listProp = lmSo.FindProperty("_levelPrefabs");
                    if (listProp != null)
                    {
                        for (int i = 0; i < listProp.arraySize; i++)
                        {
                            GameObject p = listProp.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                            TryAddPrefab(p, 1000 + i);
                        }
                    }
                }
            }

            entries.Sort((a, b) => a.LevelNumber != b.LevelNumber
                ? a.LevelNumber.CompareTo(b.LevelNumber)
                : string.CompareOrdinal(a.Prefab.name, b.Prefab.name));
            return entries;
        }

        /// <summary>
        /// Assets/Prefabs/Levels içindeki tüm Level_XX prefab'larını sayısal sıraya göre okuyup
        /// sahnedeki LevelManager._levelPrefabs listesini eksiksiz ve sıralı günceller.
        /// </summary>
        public static int RebuildSortedLevelManagerList()
        {
            LevelManager lm = UnityEngine.Object.FindFirstObjectByType<LevelManager>();
            if (lm == null || !AssetDatabase.IsValidFolder(ParametricLevelArchitect.LevelsFolder)) return 0;

            List<SavedLevelEntry> entries = GetSortedSavedLevels(includeLevelManagerExtras: false);

            SerializedObject so = new SerializedObject(lm);
            SerializedProperty listProp = so.FindProperty("_levelPrefabs");
            if (listProp != null)
            {
                listProp.arraySize = entries.Count;
                for (int i = 0; i < entries.Count; i++)
                {
                    listProp.GetArrayElementAtIndex(i).objectReferenceValue = entries[i].Prefab;
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(lm);
            }

            return entries.Count;
        }

        private static void EnsureScrewsWiredAndReady(GameObject targetRoot)
        {
            ScrewObject[] screws = targetRoot.GetComponentsInChildren<ScrewObject>(true);
            foreach (ScrewObject s in screws)
            {
                ScrewHoleTool.SetupHoleForScrew(s.gameObject);
            }
            HeadlessLevelSolver.AutoWirePhysicalDependencies(targetRoot, clearExistingBlockers: true);
        }

        private static ScrewObject InstantiateConfiguredScrew(
            GameObject screwPrefab,
            ScrewAnimationConfigSO animConfig,
            Vector3 worldPoint,
            Vector3 worldNormal,
            PlateObject plate,
            Transform parentTransform,
            ColorType color,
            float fineDepthOffset,
            string screwName)
        {
            Vector3 normal = worldNormal.sqrMagnitude > 0.0001f ? worldNormal.normalized : Vector3.forward;
            Vector3 upHint = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.92f ? Vector3.forward : Vector3.up;

            GameObject screwGo = (GameObject)PrefabUtility.InstantiatePrefab(screwPrefab, parentTransform);
            Undo.RegisterCreatedObjectUndo(screwGo, "Create Manual Screw");
            screwGo.name = screwName;
            screwGo.transform.rotation = Quaternion.LookRotation(normal, upHint);

            // Blender/FBX kaynaklı farklı ölçekli ebeveynlerde vidanın oyun içi tutarlı boyutunu koru
            screwGo.transform.localScale = ComputeNormalizedScrewLocalScale(parentTransform);

            float worldZScale = Mathf.Max(0.01f, Mathf.Abs(screwGo.transform.lossyScale.z));
            screwGo.transform.position = worldPoint - normal * ((0.107f * worldZScale) + fineDepthOffset);

            ScrewObject screw = screwGo.GetComponent<ScrewObject>();
            if (screw != null)
            {
                SerializedObject so = new SerializedObject(screw);
                so.FindProperty("_color").enumValueIndex = (int)color;
                so.FindProperty("_parentPlate").objectReferenceValue = plate;
                if (animConfig != null)
                {
                    so.FindProperty("_animationConfig").objectReferenceValue = animConfig;
                }
                so.ApplyModifiedProperties();
                screw.ApplyColorVisual();
                EditorUtility.SetDirty(screw);
            }

            ScrewHoleTool.SetupHoleForScrew(screwGo);
            return screw;
        }

        /// <summary>
        /// Herhangi bir sahne modelini veya dış FBX'i alır:
        /// Delikleri kurar -> Fiziksel engelleri tarar -> Solver ile boyayıp kuyruk çıkarır -> LevelConfig + Prefab üretir!
        /// </summary>
        public static GameObject FinalizeAndVerifyExternalModel(
            GameObject targetRoot,
            int levelNumber,
            string title,
            LevelDifficulty difficulty,
            float timeLimitSeconds,
            ColorType[] palette,
            int seed,
            bool saveAsPrefab,
            out string reportMessage)
        {
            if (targetRoot == null)
            {
                reportMessage = "Hedef obje seçili değil!";
                return null;
            }

            ScrewObject[] screws = targetRoot.GetComponentsInChildren<ScrewObject>(true);
            foreach (ScrewObject s in screws)
            {
                ScrewHoleTool.SetupHoleForScrew(s.gameObject);
            }

            HeadlessLevelSolver.AutoWirePhysicalDependencies(targetRoot, clearExistingBlockers: true);
            HeadlessLevelSolver.LevelGraph graph = HeadlessLevelSolver.ExtractGraph(targetRoot);

            if (!graph.HasValidMultipleOfFour)
            {
                reportMessage = $"UYARI: Toplam vida sayısı ({graph.TotalScrews}) 4'ün katı olmalı! Eksik/fazla: {4 - (graph.TotalScrews % 4)} vida ayarlayın.";
                return null;
            }

            HeadlessLevelSolver.OptimizationResult opt = HeadlessLevelSolver.OptimizeColorsAndQueue(
                graph,
                palette,
                difficulty,
                seed);

            if (opt.Success && opt.AssignedScrewColors != null)
            {
                for (int i = 0; i < graph.Screws.Count; i++)
                {
                    ScrewObject screw = graph.Screws[i].ScrewRef;
                    ColorType color = opt.AssignedScrewColors[i];

                    SerializedObject sSo = new SerializedObject(screw);
                    sSo.FindProperty("_color").enumValueIndex = (int)color;
                    sSo.ApplyModifiedProperties();
                    screw.ApplyColorVisual();
                    EditorUtility.SetDirty(screw);
                }
            }

            LevelConfig config = targetRoot.GetComponent<LevelConfig>();
            if (config == null) config = targetRoot.AddComponent<LevelConfig>();
            bool verified = opt.Success && opt.Report != null && opt.Report.IsSolvable;
            config.Configure(
                levelNumber,
                title,
                difficulty,
                timeLimitSeconds,
                opt.OptimizedBoxQueue,
                verified,
                opt.Report != null ? opt.Report.MonteCarloWinRate : 0f,
                opt.Report != null ? opt.Report.SolutionSteps : 0,
                opt.Report != null ? opt.Report.MaxConcurrentHolesUsed : 0,
                opt.Report != null ? opt.Report.Summary : "Optimize edilemedi");
            EditorUtility.SetDirty(config);

            reportMessage = HeadlessLevelSolver.FormatGraphSummary(graph, opt.Report);

            if (saveAsPrefab)
            {
                if (!AssetDatabase.IsValidFolder(ParametricLevelArchitect.LevelsFolder))
                {
                    AssetDatabase.CreateFolder("Assets/Prefabs", "Levels");
                }
                string prefabPath = $"{ParametricLevelArchitect.LevelsFolder}/Level_{levelNumber:00}.prefab";
                PersistConformedMeshesForLevel(targetRoot, levelNumber);
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(targetRoot, prefabPath, InteractionMode.UserAction);
                AssetDatabase.SaveAssets();
                return savedPrefab;
            }

            return targetRoot;
        }

        private static void EnsurePlatePhysicsAndComponent(GameObject go, Mesh knownMesh = null)
        {
            // Unity Editor'da GetComponent<Rigidbody>() yoksa "fake-null" obje döndürdüğünden `??` operatörü atlar!
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = Undo.AddComponent<Rigidbody>(go);
            }

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            Mesh mesh = knownMesh;
            if (mesh == null)
            {
                MeshFilter mf = go.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                else if (go.TryGetComponent<SkinnedMeshRenderer>(out var smr)) mesh = smr.sharedMesh;
            }

            if (mesh != null)
            {
                EnsureExactMeshCollider(go, mesh);
            }

            if (go.GetComponent<PlateObject>() == null)
            {
                Undo.AddComponent<PlateObject>(go);
            }
        }

        public struct AutoDistributeResult
        {
            public bool Success;
            public int PlacedScrews;
            public int ColorCount;
            public bool SolverVerified;
            public float WinRate;
            public List<ColorType> OptimizedQueue;
            public string ReportMessage;
            public string StatusMessage;
        }

        /// <summary>
        /// Seçili zorluğa (Kolay=16/3 renk, Orta=24/4 renk, Zor=36/6 renk) göre modelin görünür ve düz
        /// yüzeylerine otomatik vida dağıtır, engelleri bağlar ve HeadlessLevelSolver ile %4 dengeli boyayıp kuyruk çıkarır.
        /// </summary>
        public static AutoDistributeResult AutoDistributeScrewsByDifficulty(
            GameObject targetRoot,
            LevelDifficulty difficulty,
            string chassisKeywordsCsv,
            int levelNumber,
            string title,
            int seed = 101)
        {
            AutoDistributeResult result = new AutoDistributeResult
            {
                OptimizedQueue = new List<ColorType>()
            };

            if (targetRoot == null)
            {
                result.StatusMessage = "Hedef model seçili değil.";
                return result;
            }

            int desiredTotal;
            ColorType[] palette;
            float timeLimitSeconds;
            switch (difficulty)
            {
                case LevelDifficulty.Easy:
                    desiredTotal = 16;
                    palette = new[] { ColorType.Blue, ColorType.Yellow, ColorType.Green };
                    timeLimitSeconds = 90f;
                    break;
                case LevelDifficulty.Medium:
                    desiredTotal = 24;
                    palette = new[] { ColorType.Blue, ColorType.Yellow, ColorType.Green, ColorType.Purple };
                    timeLimitSeconds = 120f;
                    break;
                case LevelDifficulty.Hard:
                    desiredTotal = 36;
                    palette = new[] { ColorType.Blue, ColorType.Yellow, ColorType.Green, ColorType.Purple, ColorType.Red, ColorType.Orange };
                    timeLimitSeconds = 150f;
                    break;
                default:
                    desiredTotal = 40;
                    palette = new[] { ColorType.Blue, ColorType.Yellow, ColorType.Green, ColorType.Purple, ColorType.Red, ColorType.Orange };
                    timeLimitSeconds = 180f;
                    break;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Auto Distribute Screws ({difficulty})");

            // Plakalar/collider'lar henüz kurulmadıysa modeli hazırla
            if (targetRoot.GetComponentsInChildren<PlateObject>(true).Length == 0 &&
                targetRoot.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                ConvertSubMeshesToPlates(targetRoot, chassisKeywordsCsv);
            }

            // Eski vidaları temizle
            ScrewObject[] existingScrews = targetRoot.GetComponentsInChildren<ScrewObject>(true);
            for (int i = existingScrews.Length - 1; i >= 0; i--)
            {
                if (existingScrews[i] != null)
                {
                    Undo.DestroyObjectImmediate(existingScrews[i].gameObject);
                }
            }

            PlateObject[] allPlates = targetRoot.GetComponentsInChildren<PlateObject>(true);
            foreach (PlateObject plate in allPlates)
            {
                if (plate != null)
                {
                    Undo.RecordObject(plate, "Clear Plate Screws");
                    plate.AutoFindAttachedScrews();
                }
            }

            Physics.SyncTransforms();

            Bounds modelBounds = CalculateHierarchyBounds(targetRoot);
            float maxExtent = Mathf.Max(0.4f, Mathf.Max(modelBounds.size.x, Mathf.Max(modelBounds.size.y, modelBounds.size.z)));
            float rayDistance = Mathf.Max(3f, modelBounds.size.magnitude + 2f);
            float baseSpacing = Mathf.Clamp(maxExtent / Mathf.Sqrt(desiredTotal * 0.8f), 0.14f, 0.34f);
            float minHardSpacing = Mathf.Clamp(maxExtent * 0.09f, 0.085f, 0.14f);

            // Alt parçaların collider merkezlerini topla (namlu, kabza vb. uzantılar için)
            List<Bounds> subColliderBounds = new List<Bounds>();
            Collider[] modelColliders = targetRoot.GetComponentsInChildren<Collider>(true);
            foreach (Collider col in modelColliders)
            {
                if (col == null || !col.enabled) continue;
                if (col.GetComponentInParent<ScrewObject>() != null) continue;
                if (col.GetComponentInParent<ScrewSolver.Gameplay.Holes.PuzzleHole>() != null) continue;
                if (SceneVisibilityManager.instance.IsHidden(col.gameObject)) continue;
                subColliderBounds.Add(col.bounds);
            }

            List<(RaycastHit hit, PlateObject plate, Vector3 pos)> accepted = new List<(RaycastHit, PlateObject, Vector3)>();
            System.Random rng = new System.Random(seed);

            bool IsFarEnough(Vector3 point, float minDist)
            {
                float sqrMin = minDist * minDist;
                for (int i = 0; i < accepted.Count; i++)
                {
                    if ((accepted[i].pos - point).sqrMagnitude < sqrMin)
                        return false;
                }
                return true;
            }

            // Aşama 1: Her sökülebilir plaka üzerinde en az 1-2 sağlam yüzeye vida garantisi
            List<PlateObject> shuffledPlates = new List<PlateObject>();
            foreach (PlateObject p in allPlates)
            {
                if (p != null && !SceneVisibilityManager.instance.IsHidden(p.gameObject))
                    shuffledPlates.Add(p);
            }
            for (int i = shuffledPlates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffledPlates[i], shuffledPlates[j]) = (shuffledPlates[j], shuffledPlates[i]);
            }

            int maxPerPlateInitial = difficulty == LevelDifficulty.Easy ? 2 : 3;
            Vector3[] plateDirs =
            {
                Vector3.forward, Vector3.back,
                Vector3.right, Vector3.left,
                Vector3.up, Vector3.down,
                new Vector3(1f, 1f, 1f).normalized,
                new Vector3(-1f, 1f, -1f).normalized
            };

            foreach (PlateObject plate in shuffledPlates)
            {
                if (accepted.Count >= desiredTotal) break;

                Bounds pBounds = CalculateHierarchyBounds(plate.gameObject);
                int addedOnPlate = 0;
                for (int d = 0; d < plateDirs.Length && addedOnPlate < maxPerPlateInitial && accepted.Count < desiredTotal; d++)
                {
                    Vector3 dir = (plateDirs[d] + new Vector3(
                        (float)(rng.NextDouble() - 0.5) * 0.25f,
                        (float)(rng.NextDouble() - 0.5) * 0.25f,
                        (float)(rng.NextDouble() - 0.5) * 0.25f)).normalized;
                    Ray ray = new Ray(pBounds.center + dir * rayDistance, -dir);

                    if (!TryRaycastVisibleModelSurface(ray, targetRoot, out RaycastHit hit)) continue;
                    if (Vector3.Dot(hit.normal.normalized, dir) < 0.25f) continue;

                    PlateObject hitPlate = hit.collider.GetComponentInParent<PlateObject>();
                    if (hitPlate != plate) continue;
                    if (!IsFarEnough(hit.point, Mathf.Max(minHardSpacing, baseSpacing * 0.85f))) continue;

                    SurfaceSeatingInfo seating = AnalyzeSurfaceSeating(hit, targetRoot);
                    if (!seating.IsFlushEnough || Vector3.Dot(seating.SmoothedNormal, dir) < 0.2f) continue;

                    accepted.Add((hit, hitPlate, hit.point));
                    addedOnPlate++;
                }
            }

            // Aşama 2: Fibonacci küresi + parça merkezlerine ışın taramasıyla hedef sayıya ulaş
            float[] spacingFactors = { 1.25f, 0.95f, 0.75f, 0.58f };
            const int fibSamples = 72;
            float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
            Quaternion seedSpin = Quaternion.Euler(
                (float)(rng.NextDouble() * 360.0),
                (float)(rng.NextDouble() * 360.0),
                (float)(rng.NextDouble() * 360.0));

            for (int pass = 0; pass < spacingFactors.Length && accepted.Count < desiredTotal; pass++)
            {
                float minDist = Mathf.Max(minHardSpacing, baseSpacing * spacingFactors[pass]);
                int minRimReq = pass == 0 ? 4 : 3;

                for (int i = 0; i < fibSamples && accepted.Count < desiredTotal; i++)
                {
                    float y = 1f - (i / (float)(fibSamples - 1)) * 2f;
                    float radiusAtY = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                    float theta = goldenAngle * i;
                    Vector3 fibDir = new Vector3(Mathf.Cos(theta) * radiusAtY, y, Mathf.Sin(theta) * radiusAtY);
                    Vector3 dir = (seedSpin * fibDir).normalized;

                    Vector3 aimPoint = modelBounds.center;
                    if (subColliderBounds.Count > 0 && (i % 2 == 1))
                    {
                        Bounds subB = subColliderBounds[(i + pass * 7) % subColliderBounds.Count];
                        aimPoint = subB.center;
                    }

                    aimPoint += new Vector3(
                        (float)(rng.NextDouble() - 0.5) * modelBounds.extents.x * 0.35f,
                        (float)(rng.NextDouble() - 0.5) * modelBounds.extents.y * 0.35f,
                        (float)(rng.NextDouble() - 0.5) * modelBounds.extents.z * 0.35f);

                    Ray ray = new Ray(aimPoint + dir * rayDistance, -dir);
                    if (!TryRaycastVisibleModelSurface(ray, targetRoot, out RaycastHit hit)) continue;
                    if (Vector3.Dot(hit.normal.normalized, dir) < 0.25f) continue;
                    if (!IsFarEnough(hit.point, minDist)) continue;

                    SurfaceSeatingInfo seating = AnalyzeSurfaceSeating(hit, targetRoot);
                    if (seating.SupportedRimPoints < minRimReq || !seating.IsFlushEnough) continue;
                    if (Vector3.Dot(seating.SmoothedNormal, dir) < 0.2f) continue;

                    PlateObject hitPlate = hit.collider.GetComponentInParent<PlateObject>();
                    accepted.Add((hit, hitPlate, hit.point));
                }
            }

            // Her zaman 4'ün tam katı kadar vida bırak
            int usableCount = (accepted.Count / 4) * 4;
            if (usableCount < 4)
            {
                Undo.RevertAllInCurrentGroup();
                result.StatusMessage = "Model yüzeyinde 4'lü denge oluşturacak kadar düz/oturaklı alan bulunamadı. Lütfen Collider'ları yenileyin veya ölçeği büyütün.";
                return result;
            }

            List<ScrewObject> spawnedScrews = new List<ScrewObject>(usableCount);
            for (int i = 0; i < usableCount; i++)
            {
                var item = accepted[i];
                ScrewObject s = PlaceScrewAtWorldSurface(
                    item.hit,
                    item.plate,
                    palette[i % palette.Length],
                    0f,
                    false,
                    targetRoot,
                    autoSeatCurvature: true);
                if (s != null) spawnedScrews.Add(s);
            }

            // Eğer sahnedeki oluşturma sonrası eksilme olursa son fazlaları kırpıp 4'ün katını koru
            int finalMultiple = (spawnedScrews.Count / 4) * 4;
            while (spawnedScrews.Count > finalMultiple)
            {
                int last = spawnedScrews.Count - 1;
                if (spawnedScrews[last] != null)
                    Undo.DestroyObjectImmediate(spawnedScrews[last].gameObject);
                spawnedScrews.RemoveAt(last);
            }

            if (spawnedScrews.Count < 4)
            {
                Undo.RevertAllInCurrentGroup();
                result.StatusMessage = "Vidalar oluşturulurken geçerli 4'ün katı sayıya ulaşılamadı.";
                return result;
            }

            Physics.SyncTransforms();
            foreach (PlateObject plate in allPlates)
            {
                if (plate != null)
                {
                    Undo.RecordObject(plate, "Sync Plate Screws");
                    plate.AutoFindAttachedScrews();
                    EditorUtility.SetDirty(plate);
                }
            }

            HeadlessLevelSolver.AutoWirePhysicalDependencies(targetRoot, clearExistingBlockers: true);
            HeadlessLevelSolver.LevelGraph graph = HeadlessLevelSolver.ExtractGraph(targetRoot);
            HeadlessLevelSolver.OptimizationResult opt = HeadlessLevelSolver.OptimizeColorsAndQueue(graph, palette, difficulty, seed);

            int activeColorCount = Mathf.Min(palette.Length, spawnedScrews.Count / 4);
            if (opt.Success && opt.AssignedScrewColors != null && opt.AssignedScrewColors.Length == graph.Screws.Count)
            {
                for (int i = 0; i < graph.Screws.Count; i++)
                {
                    ScrewObject screw = graph.Screws[i].ScrewRef;
                    ColorType assignedColor = opt.AssignedScrewColors[i];
                    PaintScrewColor(screw, assignedColor);
                    screw.gameObject.name = $"Screw_{i + 1:00}_{assignedColor}";
                }
            }
            else
            {
                // Yedek dengeli dağıtım (her renge tam 4'ün katı)
                int totalBoxes = graph.Screws.Count / 4;
                int screwIdx = 0;
                for (int b = 0; b < totalBoxes; b++)
                {
                    ColorType c = palette[b % activeColorCount];
                    for (int k = 0; k < 4 && screwIdx < graph.Screws.Count; k++)
                    {
                        ScrewObject screw = graph.Screws[screwIdx].ScrewRef;
                        PaintScrewColor(screw, c);
                        screw.gameObject.name = $"Screw_{screwIdx + 1:00}_{c}";
                        screwIdx++;
                    }
                }
                opt = HeadlessLevelSolver.OptimizeQueueOnly(graph, difficulty, seed);
            }

            List<ColorType> finalQueue = opt.OptimizedBoxQueue != null && opt.OptimizedBoxQueue.Count > 0
                ? new List<ColorType>(opt.OptimizedBoxQueue)
                : HeadlessLevelSolver.BuildDefaultBoxQueueFromGraph(graph);

            LevelConfig config = targetRoot.GetComponent<LevelConfig>();
            if (config == null) config = Undo.AddComponent<LevelConfig>(targetRoot);
            Undo.RecordObject(config, "Update Auto-Distributed LevelConfig");

            bool verified = opt.Report != null && opt.Report.IsSolvable;
            float winRate = opt.Report != null ? opt.Report.MonteCarloWinRate : 0f;
            int minSteps = opt.Report != null ? opt.Report.SolutionSteps : 0;
            int maxHoles = opt.Report != null ? opt.Report.MaxConcurrentHolesUsed : 0;
            string summary = opt.Report != null ? opt.Report.Summary : "Otomatik dağıtıldı";

            config.Configure(
                levelNumber,
                string.IsNullOrWhiteSpace(title) ? targetRoot.name : title,
                difficulty,
                timeLimitSeconds,
                finalQueue,
                verified,
                winRate,
                minSteps,
                maxHoles,
                summary);
            EditorUtility.SetDirty(config);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(targetRoot.scene);

            HashSet<ColorType> usedColors = new HashSet<ColorType>();
            foreach (ScrewObject s in targetRoot.GetComponentsInChildren<ScrewObject>(true))
            {
                if (s != null) usedColors.Add(s.Color);
            }

            result.Success = true;
            result.PlacedScrews = graph.TotalScrews;
            result.ColorCount = usedColors.Count;
            result.SolverVerified = verified;
            result.WinRate = winRate;
            result.OptimizedQueue = finalQueue;
            result.ReportMessage = HeadlessLevelSolver.FormatGraphSummary(graph, opt.Report);
            result.StatusMessage = $"{difficulty} modda {graph.TotalScrews} sağlam vida ({usedColors.Count} renk, {finalQueue.Count} kutu) dağıtıldı! " +
                                   $"{(verified ? $"Solver Onaylı (%{winRate:F0})" : "Sıra düzenlemesi gerekebilir")}. " +
                                   "Düzeltme Modu aktif: yerini değiştireceğin vidayı silip aynı renkte yeni yere koyabilirsin.";
            return result;
        }

        private static Vector3 ComputeNormalizedScrewLocalScale(Transform parentTransform)
        {
            GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
            bool insidePuzzleRoot = puzzleRoot != null && parentTransform != null && parentTransform.IsChildOf(puzzleRoot.transform);
            float targetWorldScale = insidePuzzleRoot
                ? Mathf.Max(0.1f, Mathf.Abs(puzzleRoot.transform.lossyScale.x))
                : 1f;
            Vector3 pLossy = parentTransform != null ? parentTransform.lossyScale : Vector3.one;
            float pScale = Mathf.Max(0.001f, (Mathf.Abs(pLossy.x) + Mathf.Abs(pLossy.y) + Mathf.Abs(pLossy.z)) / 3f);
            bool isParentUniform = Mathf.Abs(pLossy.x - pLossy.y) < 0.05f * pScale && Mathf.Abs(pLossy.y - pLossy.z) < 0.05f * pScale;
            if (isParentUniform && Mathf.Abs(pScale - targetWorldScale) / targetWorldScale > 0.02f)
            {
                return Vector3.one * (targetWorldScale / pScale);
            }
            return Vector3.one;
        }

        /// <summary>
        /// Model yeniden ölçeklendiğinde üzerindeki vidaların oyun içi boyutunu sabit tutar ve yüzeye sıfır oturtur.
        /// </summary>
        public static void NormalizeScrewWorldScales(GameObject targetRoot)
        {
            if (targetRoot == null) return;

            ScrewObject[] screws = targetRoot.GetComponentsInChildren<ScrewObject>(true);
            foreach (ScrewObject s in screws)
            {
                if (s == null) continue;

                float currentWorldZ = Mathf.Max(0.01f, Mathf.Abs(s.transform.lossyScale.z));
                Vector3 surfacePoint = s.transform.position + s.transform.forward * (0.107f * currentWorldZ);
                Vector3 targetLocalScale = ComputeNormalizedScrewLocalScale(s.transform.parent);

                if ((s.transform.localScale - targetLocalScale).sqrMagnitude > 0.00001f)
                {
                    Undo.RecordObject(s.transform, "Normalize Screw Scale");
                    s.transform.localScale = targetLocalScale;
                    float normalizedWorldZ = Mathf.Max(0.01f, Mathf.Abs(s.transform.lossyScale.z));
                    s.transform.position = surfacePoint - s.transform.forward * (0.107f * normalizedWorldZ);
                    EditorUtility.SetDirty(s);
                }
            }
        }

        /// <summary>
        /// Hedef model/prefab kökünün boyutunu (uniform localScale) değiştirir, mevcut vidaların dünya ölçeğini korur.
        /// </summary>
        public static void SetTargetUniformScale(GameObject targetRoot, float newUniformScale, bool normalizeScrews = true)
        {
            if (targetRoot == null) return;

            float clamped = Mathf.Clamp(newUniformScale, 0.1f, 8f);
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Resize Target Model");

            Undo.RecordObject(targetRoot.transform, "Resize Target Model");
            targetRoot.transform.localScale = Vector3.one * clamped;

            if (normalizeScrews)
            {
                NormalizeScrewWorldScales(targetRoot);
            }

            Physics.SyncTransforms();
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(targetRoot.scene);
        }

        /// <summary>
        /// Hedef modelin görünen sınırlarını ekrana ideal boyuta (varsayılan 1.65 yerel birim) sığdırır.
        /// </summary>
        public static void FitTargetToExtent(GameObject targetRoot, float targetLocalExtent = 1.65f, bool normalizeScrews = true)
        {
            if (targetRoot == null) return;

            Bounds bounds = CalculateHierarchyBounds(targetRoot);
            float parentScale = targetRoot.transform.parent != null
                ? Mathf.Max(0.01f, Mathf.Abs(targetRoot.transform.parent.lossyScale.x))
                : 1f;
            float worldMaxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float currentLocalDim = worldMaxDim / parentScale;

            if (currentLocalDim > 0.001f)
            {
                Vector3 ls = targetRoot.transform.localScale;
                float avgScale = Mathf.Max(0.01f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
                SetTargetUniformScale(targetRoot, avgScale * (targetLocalExtent / currentLocalDim), normalizeScrews);
            }
        }

        /// <summary>
        /// Hedef modelin yerel konumunu (PuzzleRoot içindeki pozisyonu) Undo destekli günceller.
        /// </summary>
        public static void SetTargetLocalPosition(GameObject targetRoot, Vector3 newLocalPosition)
        {
            if (targetRoot == null) return;
            Undo.RecordObject(targetRoot.transform, "Move Target Model");
            targetRoot.transform.localPosition = newLocalPosition;
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(targetRoot.scene);
        }

        /// <summary>
        /// Modelin görsel sınır merkezini ebeveyn (PuzzleRoot) merkezine (0,0,0) hizalar.
        /// </summary>
        public static void CenterTargetGeometryAtLocalOrigin(GameObject targetRoot)
        {
            if (targetRoot == null) return;

            Bounds bounds = CalculateHierarchyBounds(targetRoot);
            Vector3 anchorWorld = targetRoot.transform.parent != null
                ? targetRoot.transform.parent.position
                : Vector3.zero;
            Vector3 worldDelta = anchorWorld - bounds.center;

            Undo.RecordObject(targetRoot.transform, "Center Target Model");
            targetRoot.transform.position += worldDelta;
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(targetRoot.scene);
        }

        private const string ConformedMeshesFolder = "Assets/Meshes/Conformed";

        /// <summary>
        /// Hedef modeldeki tüm mevcut vidaları tekrar gerçek yüzeye hizalar ve
        /// eğimli/kavisli yüzeylere göre hem vidayı hem siyah delik halkasını (HoleBezel) şekillendirir.
        /// </summary>
        public static int ConformAllScrewsInTarget(GameObject targetRoot)
        {
            if (targetRoot == null) return 0;

            Physics.SyncTransforms();
            ScrewObject[] screws = targetRoot.GetComponentsInChildren<ScrewObject>(true);
            if (screws == null || screws.Length == 0) return 0;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Conform All Screws To Surface");

            int updated = 0;
            foreach (ScrewObject screw in screws)
            {
                if (screw == null) continue;

                if (screw.transform.Find("HoleBezel") == null)
                {
                    ScrewHoleTool.SetupHoleForScrew(screw.gameObject);
                }

                float worldZScale = Mathf.Max(0.01f, Mathf.Abs(screw.transform.lossyScale.z));
                Vector3 normal = screw.transform.forward.sqrMagnitude > 0.0001f
                    ? screw.transform.forward.normalized
                    : Vector3.forward;
                Vector3 holeCenterWorld = screw.transform.TransformPoint(ScrewHoleTool.HoleLocalPosition);

                Ray alignRay = new Ray(holeCenterWorld + normal * (0.20f * worldZScale), -normal);
                if (TryRaycastVisibleModelSurface(alignRay, targetRoot, out RaycastHit hit) && hit.distance <= 0.40f * worldZScale)
                {
                    SurfaceSeatingInfo seating = AnalyzeSurfaceSeating(hit, targetRoot);
                    Vector3 smoothed = seating.SmoothedNormal;

                    Ray refinedRay = new Ray(hit.point + smoothed * (0.14f * worldZScale), -smoothed);
                    Vector3 exactPoint = hit.point;
                    if (TryRaycastVisibleModelSurface(refinedRay, targetRoot, out RaycastHit refinedHit) && refinedHit.distance <= 0.30f * worldZScale)
                    {
                        exactPoint = refinedHit.point;
                    }

                    Vector3 upHint = Mathf.Abs(Vector3.Dot(smoothed, Vector3.up)) > 0.92f ? Vector3.forward : Vector3.up;
                    Undo.RecordObject(screw.transform, "Align Screw To Surface");
                    screw.transform.rotation = Quaternion.LookRotation(smoothed, upHint);
                    screw.transform.position = exactPoint - smoothed * (0.107f * worldZScale);
                }

                ConformScrewAndHoleToSurface(screw, targetRoot);
                updated++;
            }

            Physics.SyncTransforms();
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(targetRoot.scene);
            return updated;
        }

        /// <summary>
        /// Tek bir vidanın siyah yuvasını (HoleBezel) ve vida başlığını altındaki 3D yüzeyin eğriliğine göre şekillendirir.
        /// Düz yüzeylerde orijinal keskin prefab mesh'lerini korur.
        /// </summary>
        public static void ConformScrewAndHoleToSurface(ScrewObject screw, GameObject targetRoot)
        {
            if (screw == null || targetRoot == null) return;

            Transform holeTr = screw.transform.Find("HoleBezel");
            if (holeTr == null)
            {
                ScrewHoleTool.SetupHoleForScrew(screw.gameObject);
                holeTr = screw.transform.Find("HoleBezel");
                if (holeTr == null) return;
            }

            Undo.RecordObject(holeTr, "Reset HoleBezel Local Transform");
            holeTr.localPosition = ScrewHoleTool.HoleLocalPosition;
            holeTr.localRotation = ScrewHoleTool.HoleLocalRotation;
            holeTr.localScale = Vector3.one;

            const int rings = 4;
            const int segments = 24;
            float[] ringRadii = { 0.014f, 0.028f, 0.042f, 0.056f };

            Vector3 hLossy = holeTr.lossyScale;
            float avgHoleScale = Mathf.Max(0.05f, (Mathf.Abs(hLossy.x) + Mathf.Abs(hLossy.y) + Mathf.Abs(hLossy.z)) / 3f);
            Vector3 holeNormal = holeTr.forward.sqrMagnitude > 0.0001f ? holeTr.forward.normalized : Vector3.forward;

            // Merkez yüksekliğini örnekleyip Referans Z olarak al
            float centerZ = 0f;
            Ray centerRay = new Ray(holeTr.position + holeNormal * (0.07f * avgHoleScale), -holeNormal);
            if (TryRaycastVisibleModelSurface(centerRay, targetRoot, out RaycastHit centerHit) && centerHit.distance <= 0.18f * avgHoleScale)
            {
                float cz = holeTr.InverseTransformPoint(centerHit.point).z;
                if (cz >= -0.045f && cz <= 0.035f) centerZ = cz;
            }

            float[,] gridZ = new float[rings, segments];
            float minZ = centerZ;
            float maxZ = centerZ;

            for (int r = 0; r < rings; r++)
            {
                float radius = ringRadii[r];
                for (int s = 0; s < segments; s++)
                {
                    float rad = (s / (float)segments) * Mathf.PI * 2f;
                    float lx = Mathf.Cos(rad) * radius;
                    float ly = Mathf.Sin(rad) * radius;
                    Vector3 sampleWorld = holeTr.TransformPoint(new Vector3(lx, ly, 0f));

                    Ray ray = new Ray(sampleWorld + holeNormal * (0.07f * avgHoleScale), -holeNormal);
                    bool hitValid = false;
                    float sampledZ = centerZ;

                    if (TryRaycastVisibleModelSurface(ray, targetRoot, out RaycastHit rimHit) && rimHit.distance <= 0.18f * avgHoleScale)
                    {
                        float lz = holeTr.InverseTransformPoint(rimHit.point).z;
                        if (lz >= -0.045f && lz <= 0.035f)
                        {
                            sampledZ = lz;
                            hitValid = true;
                        }
                    }

                    if (!hitValid)
                    {
                        sampledZ = r == 0 ? centerZ : gridZ[r - 1, s];
                    }

                    gridZ[r, s] = sampledZ;
                    if (sampledZ < minZ) minZ = sampledZ;
                    if (sampledZ > maxZ) maxZ = sampledZ;
                }
            }

            MeshFilter holeMf = holeTr.GetComponent<MeshFilter>();
            MeshFilter screwMf = FindScrewHeadMeshFilter(screw);
            Mesh pristineScrewMesh = GetPristineScrewMesh();

            float variation = maxZ - minZ;
            if (variation < 0.0012f && Mathf.Abs(centerZ) < 0.0012f)
            {
                // Yüzey yeterince düz: temiz orijinal prefab mesh'lerini kullan
                Mesh flatHoleMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Meshes/HoleDiscMesh.asset");
                if (holeMf != null && flatHoleMesh != null)
                {
                    Undo.RecordObject(holeMf, "Use Flat Hole Mesh");
                    holeMf.sharedMesh = flatHoleMesh;
                }
                if (screwMf != null && pristineScrewMesh != null)
                {
                    Undo.RecordObject(screwMf, "Use Pristine Screw Mesh");
                    screwMf.sharedMesh = pristineScrewMesh;
                }
                return;
            }

            if (holeMf != null)
            {
                Mesh conformedHole = BuildConformedHoleMesh(
                    $"ConformedHole_{screw.GetInstanceID()}",
                    ringRadii,
                    segments,
                    centerZ,
                    gridZ);
                Undo.RecordObject(holeMf, "Apply Conformed Hole Mesh");
                holeMf.sharedMesh = conformedHole;
                EditorUtility.SetDirty(holeMf);
            }

            if (screwMf != null && pristineScrewMesh != null)
            {
                Mesh conformedScrew = BuildConformedScrewMesh(
                    $"ConformedScrew_{screw.GetInstanceID()}",
                    pristineScrewMesh,
                    screw.transform,
                    screwMf.transform,
                    ringRadii,
                    segments,
                    centerZ,
                    gridZ);
                Undo.RecordObject(screwMf, "Apply Conformed Screw Mesh");
                screwMf.sharedMesh = conformedScrew;
                EditorUtility.SetDirty(screwMf);
            }
        }

        private static MeshFilter FindScrewHeadMeshFilter(ScrewObject screw)
        {
            if (screw == null) return null;
            MeshFilter[] filters = screw.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter mf in filters)
            {
                if (mf != null && !mf.gameObject.name.Equals("HoleBezel", StringComparison.OrdinalIgnoreCase))
                {
                    return mf;
                }
            }
            return null;
        }

        private static Mesh GetPristineScrewMesh()
        {
            GameObject screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScrewPrefabPath);
            if (screwPrefab == null) return null;
            MeshFilter[] filters = screwPrefab.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter mf in filters)
            {
                if (mf != null && !mf.gameObject.name.Equals("HoleBezel", StringComparison.OrdinalIgnoreCase) && mf.sharedMesh != null)
                {
                    return mf.sharedMesh;
                }
            }
            return null;
        }

        private static float SampleConformedSurfaceGrid(
            float lx,
            float ly,
            float[] ringRadii,
            int segments,
            float centerZ,
            float[,] gridZ)
        {
            float r = Mathf.Sqrt(lx * lx + ly * ly);
            float maxR = ringRadii[ringRadii.Length - 1];
            float clampedR = Mathf.Clamp(r, 0f, maxR);

            float angle = Mathf.Atan2(ly, lx);
            if (angle < 0f) angle += Mathf.PI * 2f;

            float fSeg = (angle / (Mathf.PI * 2f)) * segments;
            int s0 = Mathf.FloorToInt(fSeg) % segments;
            int s1 = (s0 + 1) % segments;
            float ts = fSeg - Mathf.Floor(fSeg);

            if (clampedR <= ringRadii[0])
            {
                float t = clampedR / Mathf.Max(0.0001f, ringRadii[0]);
                float outerVal = Mathf.Lerp(gridZ[0, s0], gridZ[0, s1], ts);
                return Mathf.Lerp(centerZ, outerVal, t);
            }

            for (int ringIdx = 0; ringIdx < ringRadii.Length - 1; ringIdx++)
            {
                float r0 = ringRadii[ringIdx];
                float r1 = ringRadii[ringIdx + 1];
                if (clampedR <= r1 || ringIdx == ringRadii.Length - 2)
                {
                    float tr = Mathf.Clamp01((clampedR - r0) / Mathf.Max(0.0001f, r1 - r0));
                    float vInner = Mathf.Lerp(gridZ[ringIdx, s0], gridZ[ringIdx, s1], ts);
                    float vOuter = Mathf.Lerp(gridZ[ringIdx + 1, s0], gridZ[ringIdx + 1, s1], ts);
                    return Mathf.Lerp(vInner, vOuter, tr);
                }
            }

            return centerZ;
        }

        private static Mesh BuildConformedHoleMesh(
            string meshName,
            float[] ringRadii,
            int segments,
            float centerZ,
            float[,] gridZ)
        {
            const float topLift = 0.0024f;
            const float botSink = -0.0032f;

            int rings = ringRadii.Length;
            int ringStride = segments;
            int capVertCount = 1 + rings * ringStride;
            int sideVertCount = segments * 2;
            int totalVerts = capVertCount * 2 + sideVertCount * 2;

            Vector3[] verts = new Vector3[totalVerts];
            Vector3[] norms = new Vector3[totalVerts];
            Vector2[] uvs = new Vector2[totalVerts];

            int topBase = 0;
            int botBase = capVertCount;
            int sideTopBase = capVertCount * 2;
            int sideBotBase = capVertCount * 2 + segments;

            float maxR = ringRadii[rings - 1];

            // Merkez üst & alt tepe noktaları
            verts[topBase] = new Vector3(0f, 0f, centerZ + topLift);
            norms[topBase] = Vector3.forward;
            uvs[topBase] = new Vector2(0.5f, 0.5f);

            verts[botBase] = new Vector3(0f, 0f, centerZ + botSink);
            norms[botBase] = Vector3.back;
            uvs[botBase] = new Vector2(0.5f, 0.5f);

            for (int r = 0; r < rings; r++)
            {
                float radius = ringRadii[r];
                for (int s = 0; s < segments; s++)
                {
                    float rad = (s / (float)segments) * Mathf.PI * 2f;
                    float cos = Mathf.Cos(rad);
                    float sin = Mathf.Sin(rad);
                    float x = cos * radius;
                    float y = sin * radius;
                    float surfZ = gridZ[r, s];
                    Vector2 uv = new Vector2(0.5f + (x / maxR) * 0.5f, 0.5f + (y / maxR) * 0.5f);

                    int tIdx = topBase + 1 + r * ringStride + s;
                    verts[tIdx] = new Vector3(x, y, surfZ + topLift);
                    norms[tIdx] = Vector3.forward;
                    uvs[tIdx] = uv;

                    int bIdx = botBase + 1 + r * ringStride + s;
                    verts[bIdx] = new Vector3(x, y, surfZ + botSink);
                    norms[bIdx] = Vector3.back;
                    uvs[bIdx] = uv;

                    if (r == rings - 1)
                    {
                        Vector3 outward = new Vector3(cos, sin, 0f);
                        verts[sideTopBase + s] = verts[tIdx];
                        norms[sideTopBase + s] = outward;
                        uvs[sideTopBase + s] = uv;

                        verts[sideBotBase + s] = verts[bIdx];
                        norms[sideBotBase + s] = outward;
                        uvs[sideBotBase + s] = uv;
                    }
                }
            }

            List<int> tris = new List<int>(segments * (6 * rings + 6));

            // Üst yüz + Alt yüz merkez yelpazesi
            for (int s = 0; s < segments; s++)
            {
                int next = (s + 1) % segments;
                tris.Add(topBase);
                tris.Add(topBase + 1 + s);
                tris.Add(topBase + 1 + next);

                tris.Add(botBase);
                tris.Add(botBase + 1 + next);
                tris.Add(botBase + 1 + s);
            }

            // Konsantrik halkalar (Üst & Alt)
            for (int r = 0; r < rings - 1; r++)
            {
                int row0 = 1 + r * ringStride;
                int row1 = 1 + (r + 1) * ringStride;
                for (int s = 0; s < segments; s++)
                {
                    int next = (s + 1) % segments;

                    int t00 = topBase + row0 + s;
                    int t01 = topBase + row0 + next;
                    int t10 = topBase + row1 + s;
                    int t11 = topBase + row1 + next;
                    tris.Add(t00); tris.Add(t10); tris.Add(t11);
                    tris.Add(t00); tris.Add(t11); tris.Add(t01);

                    int b00 = botBase + row0 + s;
                    int b01 = botBase + row0 + next;
                    int b10 = botBase + row1 + s;
                    int b11 = botBase + row1 + next;
                    tris.Add(b00); tris.Add(b11); tris.Add(b10);
                    tris.Add(b00); tris.Add(b01); tris.Add(b11);
                }
            }

            // Dış çevre eteği (yan açıdan bile boşluksuz görünüm)
            for (int s = 0; s < segments; s++)
            {
                int next = (s + 1) % segments;
                int st0 = sideTopBase + s;
                int st1 = sideTopBase + next;
                int sb0 = sideBotBase + s;
                int sb1 = sideBotBase + next;

                tris.Add(st0); tris.Add(sb0); tris.Add(sb1);
                tris.Add(st0); tris.Add(sb1); tris.Add(st1);
            }

            Mesh mesh = new Mesh { name = meshName };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildConformedScrewMesh(
            string meshName,
            Mesh sourceMesh,
            Transform screwTransform,
            Transform meshTransform,
            float[] ringRadii,
            int segments,
            float centerZ,
            float[,] gridZ)
        {
            Mesh copy = UnityEngine.Object.Instantiate(sourceMesh);
            copy.name = meshName;

            Vector3[] srcVerts = copy.vertices;
            Matrix4x4 meshToScrewLocal = screwTransform.worldToLocalMatrix * meshTransform.localToWorldMatrix;
            Matrix4x4 screwToMeshLocal = meshToScrewLocal.inverse;

            for (int i = 0; i < srcVerts.Length; i++)
            {
                Vector3 pScrew = meshToScrewLocal.MultiplyPoint3x4(srcVerts[i]);

                // Vida başlığı ~0.107+ bölgesinde, gövde/mil ise <0.095'tedir.
                float headWeight = Mathf.InverseLerp(0.035f, 0.095f, pScrew.z);
                if (headWeight > 0.001f)
                {
                    float surfZ = SampleConformedSurfaceGrid(pScrew.x, pScrew.y, ringRadii, segments, centerZ, gridZ);
                    float clampedOffset = Mathf.Clamp(surfZ, -0.024f, 0.024f) + 0.0026f;
                    pScrew.z += clampedOffset * headWeight;
                    srcVerts[i] = screwToMeshLocal.MultiplyPoint3x4(pScrew);
                }
            }

            copy.vertices = srcVerts;
            copy.RecalculateNormals();
            copy.RecalculateBounds();
            return copy;
        }

        /// <summary>
        /// Seviye kaydedilirken üretilen yüzey uyumlu mesh'leri prefab ile birlikte kaybolmaması için
        /// tek bir düzenli asset dosyası altında (Assets/Meshes/Conformed/Level_XX_Conformed.asset) kalıcı hale getirir.
        /// </summary>
        public static void PersistConformedMeshesForLevel(GameObject targetRoot, int levelNumber)
        {
            if (targetRoot == null) return;

            MeshFilter[] filters = targetRoot.GetComponentsInChildren<MeshFilter>(true);
            Dictionary<Mesh, Mesh> uniqueToClone = new Dictionary<Mesh, Mesh>();
            List<(MeshFilter mf, Mesh orig)> conformedEntries = new List<(MeshFilter, Mesh)>();

            foreach (MeshFilter mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                string mName = mf.sharedMesh.name;
                if (mName.StartsWith("ConformedHole_", StringComparison.Ordinal) ||
                    mName.StartsWith("ConformedScrew_", StringComparison.Ordinal))
                {
                    conformedEntries.Add((mf, mf.sharedMesh));
                }
            }

            string assetPath = $"{ConformedMeshesFolder}/Level_{levelNumber:00}_Conformed.asset";
            if (conformedEntries.Count == 0)
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Meshes"))
            {
                AssetDatabase.CreateFolder("Assets", "Meshes");
            }
            if (!AssetDatabase.IsValidFolder(ConformedMeshesFolder))
            {
                AssetDatabase.CreateFolder("Assets/Meshes", "Conformed");
            }

            // Mevcut eski asset silinmeden önce sahnedeki referansları güvenli bellek kopyalarına geçir
            List<Mesh> newMeshes = new List<Mesh>();
            foreach (var entry in conformedEntries)
            {
                if (!uniqueToClone.TryGetValue(entry.orig, out Mesh cloned))
                {
                    cloned = UnityEngine.Object.Instantiate(entry.orig);
                    cloned.name = entry.orig.name;
                    uniqueToClone[entry.orig] = cloned;
                    newMeshes.Add(cloned);
                }
                entry.mf.sharedMesh = cloned;
                EditorUtility.SetDirty(entry.mf);
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            AssetDatabase.CreateAsset(newMeshes[0], assetPath);
            for (int i = 1; i < newMeshes.Count; i++)
            {
                AssetDatabase.AddObjectToAsset(newMeshes[i], assetPath);
            }
            AssetDatabase.SaveAssets();
        }

        private static bool IsNameMatchingAny(string name, string[] keywords)
        {
            foreach (string kw in keywords)
            {
                if (!string.IsNullOrEmpty(kw) && name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool IsNameStartingWithAny(string name, string[] prefixes)
        {
            foreach (string p in prefixes)
            {
                if (!string.IsNullOrEmpty(p) && name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
