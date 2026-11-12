using System.Collections.Generic;
using ScrewSolver.Config;
using ScrewSolver.Core;
using ScrewSolver.Gameplay.Boxes;
using ScrewSolver.Gameplay.Controllers;
using ScrewSolver.Gameplay.Holes;
using ScrewSolver.Gameplay.Level;
using ScrewSolver.Gameplay.Plates;
using ScrewSolver.Gameplay.Screws;
using ScrewSolver.Gameplay.Slots;
using ScrewSolver.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ScrewSolver.Editor
{
    /// <summary>
    /// Gerçek muadillerdeki gibi (Screw Out 3D, Screw Master vb.) 360 derece serbestçe döndürülebilen,
    /// çok katmanlı, kilitli ve söküldükçe parçaları fiziksel olarak düşen karmaşık 3D seviyeyi kurar.
    /// Aynı zamanda üstteki kutu kuyruğunu (BoxQueueManager) otomatik oluşturur ve 32 vidaya 8 kutuyu bağlar.
    /// </summary>
    [InitializeOnLoad]
    public static class Puzzle3DBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/Screw.prefab";
        private const string TargetBoxPrefabPath = "Assets/Prefabs/TargetBox.prefab";
        private const string AnimConfigPath = "Assets/ScrewAnimationConfig.asset";
        private const string MaterialsFolder = "Assets/Materials";
        private const string AutoBuildKey = "Puzzle3DBuilder_AutoBuilt_v10";

        static Puzzle3DBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying) return;
                if (!SessionState.GetBool(AutoBuildKey, false))
                {
                    SessionState.SetBool(AutoBuildKey, true);
                    Build3DMechanicalPuzzle(silent: true);
                }
            };
        }

        [MenuItem("ScrewSolver/Seviye/3D Çok Yüzeyli Mekanik Obje Kur %#m")]
        public static void BuildMenuCommand()
        {
            Build3DMechanicalPuzzle(silent: false);
        }

        public static void Build3DMechanicalPuzzle(bool silent = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                if (!silent)
                {
                    EditorUtility.DisplayDialog("Uyarı", "Lütfen oyun modundan (Play Mode) çıkıp tekrar deneyin.", "Tamam");
                }
                return;
            }

            GameObject screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetBoxPrefabPath);
            ScrewAnimationConfigSO animConfig = AssetDatabase.LoadAssetAtPath<ScrewAnimationConfigSO>(AnimConfigPath);

            if (screwPrefab == null || boxPrefab == null)
            {
                if (!silent)
                {
                    EditorUtility.DisplayDialog("Hata", "Screw veya TargetBox prefab'ı bulunamadı!", "Tamam");
                }
                return;
            }

            // 1. PuzzleRoot'u bul veya oluştur
            GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
            if (puzzleRoot == null)
            {
                puzzleRoot = new GameObject("PuzzleRoot");
                Undo.RegisterCreatedObjectUndo(puzzleRoot, "Create PuzzleRoot");
            }
            puzzleRoot.transform.position = Vector3.zero;
            puzzleRoot.transform.rotation = Quaternion.identity;
            puzzleRoot.transform.localScale = new Vector3(0.70f, 0.70f, 0.70f);

            // PuzzleRotator'ı bağla
            PuzzleRotator rotator = Object.FindFirstObjectByType<PuzzleRotator>();
            if (rotator != null)
            {
                SerializedObject rotatorSo = new SerializedObject(rotator);
                rotatorSo.FindProperty("_targetPivot").objectReferenceValue = puzzleRoot.transform;
                rotatorSo.ApplyModifiedProperties();
            }

            // 2. PuzzleRoot içindeki eski geçici nesneleri temizle
            List<GameObject> childrenToDestroy = new List<GameObject>();
            for (int i = 0; i < puzzleRoot.transform.childCount; i++)
            {
                childrenToDestroy.Add(puzzleRoot.transform.GetChild(i).gameObject);
            }
            foreach (var child in childrenToDestroy)
            {
                Undo.DestroyObjectImmediate(child);
            }

            // 3. Materyalleri hazırla
            Material matChassis = GetOrCreateMaterial("Mat_ChassisCore", new Color(0.14f, 0.16f, 0.20f, 1f), 0.35f, 0.4f);
            Material matBlue = GetOrCreateMaterial("Mat_PlateBlue", new Color(0.20f, 0.60f, 1.00f, 1f), 0.55f, 0.1f);
            Material matPurple = GetOrCreateMaterial("Mat_PlatePurple", new Color(0.61f, 0.35f, 0.71f, 1f), 0.55f, 0.1f);
            Material matYellow = GetOrCreateMaterial("Mat_PlateYellow", new Color(0.95f, 0.77f, 0.06f, 1f), 0.55f, 0.1f);
            Material matGreen = GetOrCreateMaterial("Mat_PlateGreen", new Color(0.18f, 0.80f, 0.44f, 1f), 0.55f, 0.1f);
            Material matTeal = GetOrCreateMaterial("Mat_PlateTeal", new Color(0.10f, 0.74f, 0.61f, 1f), 0.55f, 0.1f);

            // 4. Merkez Gövde (Mechanical Core Chassis)
            GameObject chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chassis.name = "Chassis_Core";
            chassis.transform.SetParent(puzzleRoot.transform, false);
            chassis.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            chassis.transform.localRotation = Quaternion.identity;
            chassis.transform.localScale = new Vector3(1.50f, 1.70f, 1.50f);
            chassis.GetComponent<MeshRenderer>().sharedMaterial = matChassis;
            Undo.RegisterCreatedObjectUndo(chassis, "Create Chassis");

            // 5. Yüzeylerdeki Plakaları ve Vidaları İnşa Et (Toplam 32 Vida = 8 Mavi, 8 Sarı, 8 Mor, 8 Yeşil)
            const float t = 0.06f; // Plaka kalınlığı

            // =============================================================
            // YÜZEY 1: ÖN CEPHE (FRONT +Z) - 3 Katmanlı (8 Vida)
            // =============================================================
            // Katman 1 (Geniş Taban Plakası)
            PlateObject pFrontBase = CreatePlate(puzzleRoot.transform, "Plate_Front_Base",
                new Vector3(0f, 0.05f, 0.78f), Quaternion.identity, new Vector3(1.30f, 1.30f, t), matTeal);

            ScrewObject sFront1 = CreateScrew(screwPrefab, pFrontBase, "Screw_Front_Yellow_1", new Vector2(-0.45f, 0.45f), t, ColorType.Yellow, animConfig);
            ScrewObject sFront2 = CreateScrew(screwPrefab, pFrontBase, "Screw_Front_Yellow_2", new Vector2(0.45f, -0.45f), t, ColorType.Yellow, animConfig);
            ScrewObject sFront3Covered = CreateScrew(screwPrefab, pFrontBase, "Screw_Front_Blue_Covered_1", new Vector2(-0.45f, -0.45f), t, ColorType.Blue, animConfig);
            ScrewObject sFront4Covered = CreateScrew(screwPrefab, pFrontBase, "Screw_Front_Green_Covered_1", new Vector2(0.45f, 0.45f), t, ColorType.Green, animConfig);

            // Katman 2 (Orta - L-Braket)
            PlateObject pFrontLBracket = CreatePlate(puzzleRoot.transform, "Plate_Front_L_Bracket",
                new Vector3(0.05f, 0f, 0.84f), Quaternion.identity, new Vector3(0.60f, 1.15f, t), matPurple);

            ScrewObject sFront5 = CreateScrew(screwPrefab, pFrontLBracket, "Screw_Front_Yellow_3", new Vector2(0f, -0.35f), t, ColorType.Yellow, animConfig);
            ScrewObject sFront6Covered = CreateScrew(screwPrefab, pFrontLBracket, "Screw_Front_Green_Covered_2", new Vector2(0f, 0.35f), t, ColorType.Green, animConfig);

            // Katman 2, Katman 1'deki iki vidayı örter
            LinkBlocker(pFrontLBracket, sFront3Covered);
            LinkBlocker(pFrontLBracket, sFront4Covered);

            // Katman 3 (En Dış - Yatay Kilit Çıtası)
            PlateObject pFrontLockBar = CreatePlate(puzzleRoot.transform, "Plate_Front_Lock_Bar",
                new Vector3(0.05f, 0.35f, 0.90f), Quaternion.identity, new Vector3(0.95f, 0.30f, t), matBlue);

            ScrewObject sFront7 = CreateScrew(screwPrefab, pFrontLockBar, "Screw_Front_Blue_2", new Vector2(-0.35f, 0f), t, ColorType.Blue, animConfig);
            ScrewObject sFront8 = CreateScrew(screwPrefab, pFrontLockBar, "Screw_Front_Purple_1", new Vector2(0.35f, 0f), t, ColorType.Purple, animConfig);

            // Katman 3, Katman 2'deki sFront6Covered vidasını kilitler
            LinkBlocker(pFrontLockBar, sFront6Covered);

            // =============================================================
            // YÜZEY 2: ARKA CEPHE (BACK -Z) - 2 Katmanlı Çapraz Şerit (6 Vida)
            // =============================================================
            PlateObject pBackBase = CreatePlate(puzzleRoot.transform, "Plate_Back_Base",
                new Vector3(0f, -0.05f, -0.78f), Quaternion.Euler(0f, 180f, 0f), new Vector3(1.30f, 1.30f, t), matYellow);

            ScrewObject sBack1 = CreateScrew(screwPrefab, pBackBase, "Screw_Back_Green_1", new Vector2(-0.45f, 0.45f), t, ColorType.Green, animConfig);
            ScrewObject sBack2 = CreateScrew(screwPrefab, pBackBase, "Screw_Back_Blue_3", new Vector2(0.45f, -0.45f), t, ColorType.Blue, animConfig);
            ScrewObject sBack3Covered = CreateScrew(screwPrefab, pBackBase, "Screw_Back_Yellow_Covered_4", new Vector2(-0.45f, -0.45f), t, ColorType.Yellow, animConfig);
            ScrewObject sBack4Covered = CreateScrew(screwPrefab, pBackBase, "Screw_Back_Purple_Covered_2", new Vector2(0.45f, 0.45f), t, ColorType.Purple, animConfig);

            // Çapraz Destek Şeridi
            PlateObject pBackDiagonal = CreatePlate(puzzleRoot.transform, "Plate_Back_Diagonal_Strut",
                new Vector3(0f, -0.05f, -0.84f), Quaternion.Euler(0f, 180f, 35f), new Vector3(1.35f, 0.32f, t), matBlue);

            ScrewObject sBack5 = CreateScrew(screwPrefab, pBackDiagonal, "Screw_Back_Blue_4", new Vector2(-0.40f, 0f), t, ColorType.Blue, animConfig);
            ScrewObject sBack6 = CreateScrew(screwPrefab, pBackDiagonal, "Screw_Back_Green_2", new Vector2(0.40f, 0f), t, ColorType.Green, animConfig);

            LinkBlocker(pBackDiagonal, sBack3Covered);
            LinkBlocker(pBackDiagonal, sBack4Covered);

            // =============================================================
            // YÜZEY 3: SAĞ CEPHE (RIGHT +X) - 2 Katmanlı T-Kuşak (6 Vida)
            // =============================================================
            PlateObject pRightCol = CreatePlate(puzzleRoot.transform, "Plate_Right_Column",
                new Vector3(0.78f, -0.05f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector3(0.45f, 1.40f, t), matGreen);

            ScrewObject sRight1 = CreateScrew(screwPrefab, pRightCol, "Screw_Right_Yellow_5", new Vector2(0f, 0.50f), t, ColorType.Yellow, animConfig);
            ScrewObject sRight2 = CreateScrew(screwPrefab, pRightCol, "Screw_Right_Green_3", new Vector2(0f, -0.50f), t, ColorType.Green, animConfig);
            ScrewObject sRight3Covered = CreateScrew(screwPrefab, pRightCol, "Screw_Right_Blue_Covered_5", new Vector2(0f, 0f), t, ColorType.Blue, animConfig);

            // Yatay Kuşak
            PlateObject pRightBelt = CreatePlate(puzzleRoot.transform, "Plate_Right_Belt",
                new Vector3(0.84f, -0.05f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector3(1.25f, 0.36f, t), matYellow);

            ScrewObject sRight4 = CreateScrew(screwPrefab, pRightBelt, "Screw_Right_Purple_3", new Vector2(-0.45f, 0f), t, ColorType.Purple, animConfig);
            ScrewObject sRight5 = CreateScrew(screwPrefab, pRightBelt, "Screw_Right_Yellow_6", new Vector2(0.45f, 0f), t, ColorType.Yellow, animConfig);
            ScrewObject sRight6 = CreateScrew(screwPrefab, pRightBelt, "Screw_Right_Blue_6", new Vector2(0.18f, 0f), t, ColorType.Blue, animConfig);

            LinkBlocker(pRightBelt, sRight3Covered);

            // =============================================================
            // YÜZEY 4: SOL CEPHE (LEFT -X) - 2 Katmanlı Kelepçe (6 Vida)
            // =============================================================
            PlateObject pLeftCol = CreatePlate(puzzleRoot.transform, "Plate_Left_Column",
                new Vector3(-0.78f, -0.05f, 0f), Quaternion.Euler(0f, -90f, 0f), new Vector3(0.45f, 1.40f, t), matPurple);

            ScrewObject sLeft1 = CreateScrew(screwPrefab, pLeftCol, "Screw_Left_Green_4", new Vector2(0f, 0.50f), t, ColorType.Green, animConfig);
            ScrewObject sLeft2 = CreateScrew(screwPrefab, pLeftCol, "Screw_Left_Purple_4", new Vector2(0f, -0.50f), t, ColorType.Purple, animConfig);
            ScrewObject sLeft3Covered = CreateScrew(screwPrefab, pLeftCol, "Screw_Left_Yellow_Covered_7", new Vector2(0f, 0f), t, ColorType.Yellow, animConfig);

            // Yatay Kelepçe
            PlateObject pLeftClamp = CreatePlate(puzzleRoot.transform, "Plate_Left_Clamp",
                new Vector3(-0.84f, -0.05f, 0f), Quaternion.Euler(0f, -90f, 0f), new Vector3(1.25f, 0.36f, t), matTeal);

            ScrewObject sLeft4 = CreateScrew(screwPrefab, pLeftClamp, "Screw_Left_Green_5", new Vector2(-0.45f, 0f), t, ColorType.Green, animConfig);
            ScrewObject sLeft5 = CreateScrew(screwPrefab, pLeftClamp, "Screw_Left_Purple_5", new Vector2(0.45f, 0f), t, ColorType.Purple, animConfig);
            ScrewObject sLeft6 = CreateScrew(screwPrefab, pLeftClamp, "Screw_Left_Yellow_8", new Vector2(0.18f, 0f), t, ColorType.Yellow, animConfig);

            LinkBlocker(pLeftClamp, sLeft3Covered);

            // =============================================================
            // YÜZEY 5: ÜST CEPHE (TOP +Y) - Tavan & Köprü Çıtası (6 Vida)
            // =============================================================
            PlateObject pTopRoof = CreatePlate(puzzleRoot.transform, "Plate_Top_Roof",
                new Vector3(0f, 0.83f, 0f), Quaternion.Euler(-90f, 0f, 0f), new Vector3(1.25f, 1.25f, t), matYellow);

            ScrewObject sTop1 = CreateScrew(screwPrefab, pTopRoof, "Screw_Top_Blue_7", new Vector2(-0.45f, 0.45f), t, ColorType.Blue, animConfig);
            ScrewObject sTop2 = CreateScrew(screwPrefab, pTopRoof, "Screw_Top_Purple_6", new Vector2(0.45f, -0.45f), t, ColorType.Purple, animConfig);
            ScrewObject sTop3Covered = CreateScrew(screwPrefab, pTopRoof, "Screw_Top_Green_Covered_8", new Vector2(0f, 0.35f), t, ColorType.Green, animConfig);
            ScrewObject sTop4Covered = CreateScrew(screwPrefab, pTopRoof, "Screw_Top_Purple_Covered_7", new Vector2(0f, -0.35f), t, ColorType.Purple, animConfig);

            // Üst Köprü Çıtası
            PlateObject pTopBridge = CreatePlate(puzzleRoot.transform, "Plate_Top_Bridge",
                new Vector3(0f, 0.89f, 0f), Quaternion.Euler(-90f, 0f, 0f), new Vector3(0.38f, 1.25f, t), matBlue);

            ScrewObject sTop5 = CreateScrew(screwPrefab, pTopBridge, "Screw_Top_Blue_8", new Vector2(0f, -0.42f), t, ColorType.Blue, animConfig);
            ScrewObject sTop6 = CreateScrew(screwPrefab, pTopBridge, "Screw_Top_Purple_8", new Vector2(0f, 0.42f), t, ColorType.Purple, animConfig);

            LinkBlocker(pTopBridge, sTop3Covered);
            LinkBlocker(pTopBridge, sTop4Covered);

            // 6. Tüm Plakaların bağlı vidalarını otomatik güncelle
            PlateObject[] allPlates = Object.FindObjectsByType<PlateObject>(FindObjectsSortMode.None);
            foreach (var plate in allPlates)
            {
                plate.AutoFindAttachedScrews();
                EditorUtility.SetDirty(plate);
            }

            // 7. Siyah delik yuvalarını (HoleBezel) her vidanın altına yerleştir
            ScrewHoleTool.SetupAllHoles();

            // 8. HUD Kutu Kuyruğunu (BoxQueueManager) Otomatik Kur
            SetupHUDBoxQueue(boxPrefab);

            // 9. Harici HUD Bekleme Barını Kaldır
            RemoveHUDWaitingSlots();

            // 10. Havadaki eski fazlalık delikleri temizle
            CleanFloatingHoles(puzzleRoot.transform);

            // 11. Oyun Arayüzü ve Popup'ları (GameUIController / Canvas) Kur
            SetupGameUI();

            // 12. Modüler Seviye Yöneticisini (LevelManager) Kur
            SetupLevelManager();

            // Sahneyi kaydet
            EditorSceneManager.MarkSceneDirty(puzzleRoot.scene);
            EditorSceneManager.SaveScene(puzzleRoot.scene);

            Debug.Log("<color=#00FF88><b>[Puzzle3DBuilder]</b> Tam 32 Vidalı Çok Katmanlı 3D Mekanik Obje ve Dinamik Kutu Kuyruğu (8 Kutu) başarıyla kuruldu!</color>");
            if (!silent)
            {
                EditorUtility.DisplayDialog("Başarılı!", 
                    "3D Mekanik Bulmaca ve Dinamik Kutu Kuyruğu Kuruldu!\n\n" +
                    "• Tam 32 Vida = 8 Kutu x 4 Slot (Sıfır artık vida, %100 Uyumlu)\n" +
                    "  - 8 Mavi (2 Kutu)\n" +
                    "  - 8 Sarı (2 Kutu)\n" +
                    "  - 8 Mor (2 Kutu)\n" +
                    "  - 8 Yeşil (2 Kutu)\n" +
                    "• Çok Katmanlı Blocker Kilitleri (L-Braket, Çapraz Şerit, Köprü, Kelepçe)\n" +
                    "• Dinamik Kutu Akışı: Kutular doldukça sıradaki kutu yukarıdan düşer!\n" +
                    "• Düşen Fizik Plakaları: Vidalar söküldükçe parçalar taklayla ayrılır.\n\n" +
                    "Şimdi Play tuşuna basarak kesintisiz akışı test edebilirsiniz!", "Harika!");
            }
        }

        private static void SetupHUDBoxQueue(GameObject boxPrefab)
        {
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            Transform hudTr = mainCam.transform.Find("HUD_Boxes");
            if (hudTr == null)
            {
                GameObject hudGo = new GameObject("HUD_Boxes");
                hudGo.transform.SetParent(mainCam.transform, false);
                hudTr = hudGo.transform;
                hudTr.localPosition = Vector3.zero;
                hudTr.localRotation = Quaternion.identity;
                hudTr.localScale = Vector3.one;
            }

            // HUD altındaki eski statik TargetBox'ları temizle
            List<GameObject> toDestroy = new List<GameObject>();
            for (int i = 0; i < hudTr.childCount; i++)
            {
                Transform child = hudTr.GetChild(i);
                if (child.name.StartsWith("TargetBox") || child.GetComponent<ColorBox>() != null)
                {
                    toDestroy.Add(child.gameObject);
                }
            }
            foreach (var go in toDestroy)
            {
                Undo.DestroyObjectImmediate(go);
            }

            // 3 Sabit Yuva (Slot) Oluştur veya Bul
            Vector3[] slotPositions = new Vector3[]
            {
                new Vector3(-0.29558f, 1.883f, 5.327f),
                new Vector3(0.02400f, 1.883f, 5.327f),
                new Vector3(0.34542f, 1.883f, 5.327f)
            };
            Quaternion slotRotation = Quaternion.Euler(-180f, 0f, 0f);
            Vector3 slotScale = new Vector3(9.880922f, 9.874693f, 1.988908f);

            List<Transform> slotTransforms = new List<Transform>();
            for (int i = 0; i < 3; i++)
            {
                string slotName = $"BoxSlot_{i}";
                Transform slotTr = hudTr.Find(slotName);
                if (slotTr == null)
                {
                    GameObject slotGo = new GameObject(slotName);
                    slotGo.transform.SetParent(hudTr, false);
                    slotTr = slotGo.transform;
                }
                slotTr.localPosition = slotPositions[i];
                slotTr.localRotation = slotRotation;
                slotTr.localScale = slotScale;
                slotTransforms.Add(slotTr);
            }

            // BoxQueueManager bileşenini kur
            BoxQueueManager queueMgr = hudTr.GetComponent<BoxQueueManager>();
            if (queueMgr == null)
            {
                queueMgr = hudTr.gameObject.AddComponent<BoxQueueManager>();
            }

            SerializedObject queueSo = new SerializedObject(queueMgr);
            queueSo.FindProperty("_targetBoxPrefab").objectReferenceValue = boxPrefab;
            queueSo.FindProperty("_autoBuildQueueFromScrews").boolValue = true;
            queueSo.FindProperty("_dropDuration").floatValue = 0.40f;
            queueSo.FindProperty("_dropLocalYOffset").floatValue = 0.35f;

            SerializedProperty slotsProp = queueSo.FindProperty("_boxSlots");
            slotsProp.arraySize = 3;
            for (int i = 0; i < 3; i++)
            {
                slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slotTransforms[i];
            }
            queueSo.ApplyModifiedProperties();

            // Editör modunda kutu kuyruğunu hesapla
            queueMgr.AutoPopulateQueueFromScrews();
            EditorUtility.SetDirty(queueMgr);

            // TargetBox prefabındaki tüm renderer'larda gölge düşürmeyi kapat
            Renderer[] boxPrefabRends = boxPrefab.GetComponentsInChildren<Renderer>(true);
            bool boxDirty = false;
            foreach (var r in boxPrefabRends)
            {
                if (r != null && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    boxDirty = true;
                }
            }
            if (boxDirty)
            {
                EditorUtility.SetDirty(boxPrefab);
                AssetDatabase.SaveAssets();
            }
        }

        private static void RemoveHUDWaitingSlots()
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                Transform waitingTr = mainCam.transform.Find("HUD_WaitingSlots");
                if (waitingTr != null)
                {
                    Undo.DestroyObjectImmediate(waitingTr.gameObject);
                }
            }
            GameObject standalone = GameObject.Find("HUD_WaitingSlots");
            if (standalone != null)
            {
                Undo.DestroyObjectImmediate(standalone);
            }
        }

        private static void CleanFloatingHoles(Transform puzzleRoot)
        {
            if (puzzleRoot == null) return;

            Transform holesContainer = puzzleRoot.Find("CoreEmptyHoles");
            if (holesContainer != null)
            {
                Undo.DestroyObjectImmediate(holesContainer.gameObject);
            }

            // Sahnedeki bağımsız başıboş delikleri de temizle
            GameObject standaloneHoles = GameObject.Find("CoreEmptyHoles");
            if (standaloneHoles != null)
            {
                Undo.DestroyObjectImmediate(standaloneHoles);
            }
        }

        private static void SetupGameUI()
        {
            // EventSystem kontrolü (Yeni Input System uyumlu)
            UnityEngine.EventSystems.EventSystem es = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                es = esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Canvas bul veya oluştur
            GameObject canvasGo = GameObject.Find("UI_Canvas");
            if (canvasGo == null)
            {
                canvasGo = new GameObject("UI_Canvas");
            }

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            if (canvasGo.GetComponent<GraphicRaycaster>() == null)
            {
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            GameUIController uiController = canvasGo.GetComponent<GameUIController>();
            if (uiController == null)
            {
                uiController = canvasGo.AddComponent<GameUIController>();
            }

            SerializedObject uiSo = new SerializedObject(uiController);

            // 1. HUD Top Bar
            Transform hudBar = canvasGo.transform.Find("HUD_TopBar");
            if (hudBar == null)
            {
                GameObject barGo = new GameObject("HUD_TopBar");
                barGo.transform.SetParent(canvasGo.transform, false);
                hudBar = barGo.transform;
            }
            RectTransform hudBarRt = hudBar.GetComponent<RectTransform>() ?? hudBar.gameObject.AddComponent<RectTransform>();
            hudBarRt.anchorMin = new Vector2(0f, 1f);
            hudBarRt.anchorMax = new Vector2(1f, 1f);
            hudBarRt.pivot = new Vector2(0.5f, 1f);
            hudBarRt.anchoredPosition = new Vector2(0f, -40f);
            hudBarRt.sizeDelta = new Vector2(0f, 140f);

            // Level Text
            Transform levelTr = hudBar.Find("LevelText");
            if (levelTr == null)
            {
                GameObject lvlGo = new GameObject("LevelText");
                lvlGo.transform.SetParent(hudBar, false);
                levelTr = lvlGo.transform;
            }
            Text lvlText = levelTr.GetComponent<Text>() ?? levelTr.gameObject.AddComponent<Text>();
            lvlText.font = defaultFont;
            lvlText.fontSize = 44;
            lvlText.alignment = TextAnchor.MiddleCenter;
            lvlText.fontStyle = FontStyle.Bold;
            lvlText.color = Color.white;
            lvlText.text = "LEVEL 1";
            RectTransform lvlRt = levelTr.GetComponent<RectTransform>();
            lvlRt.anchorMin = new Vector2(0.5f, 0.5f);
            lvlRt.anchorMax = new Vector2(0.5f, 0.5f);
            lvlRt.anchoredPosition = new Vector2(0f, 25f);
            lvlRt.sizeDelta = new Vector2(400f, 60f);

            // Timer Text
            Transform timerTr = hudBar.Find("TimerText");
            if (timerTr == null)
            {
                GameObject tmrGo = new GameObject("TimerText");
                tmrGo.transform.SetParent(hudBar, false);
                timerTr = tmrGo.transform;
            }
            Text tmrText = timerTr.GetComponent<Text>() ?? timerTr.gameObject.AddComponent<Text>();
            tmrText.font = defaultFont;
            tmrText.fontSize = 36;
            tmrText.alignment = TextAnchor.MiddleCenter;
            tmrText.fontStyle = FontStyle.Bold;
            tmrText.color = new Color(1f, 0.9f, 0.3f);
            tmrText.text = "02:00";
            RectTransform tmrRt = timerTr.GetComponent<RectTransform>();
            tmrRt.anchorMin = new Vector2(0.5f, 0.5f);
            tmrRt.anchorMax = new Vector2(0.5f, 0.5f);
            tmrRt.anchoredPosition = new Vector2(0f, -25f);
            tmrRt.sizeDelta = new Vector2(300f, 50f);

            // Restart Button
            Transform restartTr = hudBar.Find("RestartButton");
            if (restartTr == null)
            {
                GameObject rstGo = new GameObject("RestartButton");
                rstGo.transform.SetParent(hudBar, false);
                restartTr = rstGo.transform;
            }
            Image rstImg = restartTr.GetComponent<Image>() ?? restartTr.gameObject.AddComponent<Image>();
            rstImg.color = new Color(0.2f, 0.25f, 0.35f, 0.85f);
            Button rstBtn = restartTr.GetComponent<Button>() ?? restartTr.gameObject.AddComponent<Button>();
            RectTransform rstRt = restartTr.GetComponent<RectTransform>();
            rstRt.anchorMin = new Vector2(1f, 0.5f);
            rstRt.anchorMax = new Vector2(1f, 0.5f);
            rstRt.anchoredPosition = new Vector2(-60f, 0f);
            rstRt.sizeDelta = new Vector2(80f, 80f);

            Transform rstIconTr = restartTr.Find("Icon");
            if (rstIconTr == null)
            {
                GameObject icGo = new GameObject("Icon");
                icGo.transform.SetParent(restartTr, false);
                rstIconTr = icGo.transform;
            }
            Text rstIcon = rstIconTr.GetComponent<Text>() ?? rstIconTr.gameObject.AddComponent<Text>();
            rstIcon.font = defaultFont;
            rstIcon.fontSize = 42;
            rstIcon.alignment = TextAnchor.MiddleCenter;
            rstIcon.color = Color.white;
            rstIcon.text = "↻";
            rstIconTr.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 80f);

            uiSo.FindProperty("_levelText").objectReferenceValue = lvlText;
            uiSo.FindProperty("_timerText").objectReferenceValue = tmrText;
            uiSo.FindProperty("_restartButton").objectReferenceValue = rstBtn;

            // 2. Victory Panel
            Transform vicTr = canvasGo.transform.Find("VictoryPanel");
            if (vicTr == null)
            {
                GameObject vicGo = new GameObject("VictoryPanel");
                vicGo.transform.SetParent(canvasGo.transform, false);
                vicTr = vicGo.transform;
            }
            Image vicBg = vicTr.GetComponent<Image>() ?? vicTr.gameObject.AddComponent<Image>();
            vicBg.color = new Color(0.02f, 0.05f, 0.12f, 0.78f);
            CanvasGroup vicCg = vicTr.GetComponent<CanvasGroup>() ?? vicTr.gameObject.AddComponent<CanvasGroup>();
            RectTransform vicRt = vicTr.GetComponent<RectTransform>();
            vicRt.anchorMin = Vector2.zero;
            vicRt.anchorMax = Vector2.one;
            vicRt.sizeDelta = Vector2.zero;

            Transform vicCardTr = vicTr.Find("VictoryCard");
            if (vicCardTr == null)
            {
                GameObject cardGo = new GameObject("VictoryCard");
                cardGo.transform.SetParent(vicTr, false);
                vicCardTr = cardGo.transform;
            }
            Image vicCardImg = vicCardTr.GetComponent<Image>() ?? vicCardTr.gameObject.AddComponent<Image>();
            vicCardImg.color = new Color(0.14f, 0.17f, 0.24f, 0.98f);
            RectTransform vicCardRt = vicCardTr.GetComponent<RectTransform>();
            vicCardRt.anchorMin = new Vector2(0.5f, 0.5f);
            vicCardRt.anchorMax = new Vector2(0.5f, 0.5f);
            vicCardRt.anchoredPosition = Vector2.zero;
            vicCardRt.sizeDelta = new Vector2(820f, 920f);

            // Victory Title
            Transform vicTitleTr = vicCardTr.Find("Title");
            if (vicTitleTr == null)
            {
                GameObject tGo = new GameObject("Title");
                tGo.transform.SetParent(vicCardTr, false);
                vicTitleTr = tGo.transform;
            }
            Text vicTitle = vicTitleTr.GetComponent<Text>() ?? vicTitleTr.gameObject.AddComponent<Text>();
            vicTitle.font = defaultFont;
            vicTitle.fontSize = 54;
            vicTitle.fontStyle = FontStyle.Bold;
            vicTitle.alignment = TextAnchor.MiddleCenter;
            vicTitle.color = new Color(1f, 0.85f, 0.2f);
            vicTitle.text = "LEVEL COMPLETED!";
            RectTransform vtRt = vicTitleTr.GetComponent<RectTransform>();
            vtRt.anchorMin = new Vector2(0.5f, 1f);
            vtRt.anchorMax = new Vector2(0.5f, 1f);
            vtRt.anchoredPosition = new Vector2(0f, -100f);
            vtRt.sizeDelta = new Vector2(760f, 80f);

            // Stars Container
            Transform starsContainer = vicCardTr.Find("StarsContainer");
            if (starsContainer == null)
            {
                GameObject sGo = new GameObject("StarsContainer");
                sGo.transform.SetParent(vicCardTr, false);
                starsContainer = sGo.transform;
            }
            RectTransform scRt = starsContainer.GetComponent<RectTransform>() ?? starsContainer.gameObject.AddComponent<RectTransform>();
            scRt.anchorMin = new Vector2(0.5f, 1f);
            scRt.anchorMax = new Vector2(0.5f, 1f);
            scRt.anchoredPosition = new Vector2(0f, -240f);
            scRt.sizeDelta = new Vector2(500f, 140f);

            List<Image> starsList = new List<Image>();
            float[] starX = new float[] { -150f, 0f, 150f };
            for (int s = 0; s < 3; s++)
            {
                string starName = $"Star_{s}";
                Transform sTr = starsContainer.Find(starName);
                if (sTr == null)
                {
                    GameObject starGo = new GameObject(starName);
                    starGo.transform.SetParent(starsContainer, false);
                    sTr = starGo.transform;
                }
                Image sImg = sTr.GetComponent<Image>() ?? sTr.gameObject.AddComponent<Image>();
                sImg.color = new Color(1f, 0.84f, 0.0f);
                RectTransform sRt = sTr.GetComponent<RectTransform>();
                sRt.anchoredPosition = new Vector2(starX[s], s == 1 ? 15f : 0f);
                sRt.sizeDelta = new Vector2(90f, 90f);

                Transform iconTr = sTr.Find("TextIcon");
                if (iconTr == null)
                {
                    GameObject iGo = new GameObject("TextIcon");
                    iGo.transform.SetParent(sTr, false);
                    iconTr = iGo.transform;
                }
                Text icTxt = iconTr.GetComponent<Text>() ?? iconTr.gameObject.AddComponent<Text>();
                icTxt.font = defaultFont;
                icTxt.fontSize = 68;
                icTxt.alignment = TextAnchor.MiddleCenter;
                icTxt.color = Color.white;
                icTxt.text = "★";
                iconTr.GetComponent<RectTransform>().sizeDelta = new Vector2(90f, 90f);

                starsList.Add(sImg);
            }

            // Next Level Button
            Transform nextTr = vicCardTr.Find("NextLevelButton");
            if (nextTr == null)
            {
                GameObject nGo = new GameObject("NextLevelButton");
                nGo.transform.SetParent(vicCardTr, false);
                nextTr = nGo.transform;
            }
            Image nextImg = nextTr.GetComponent<Image>() ?? nextTr.gameObject.AddComponent<Image>();
            nextImg.color = new Color(0.18f, 0.80f, 0.44f);
            Button nextBtn = nextTr.GetComponent<Button>() ?? nextTr.gameObject.AddComponent<Button>();
            RectTransform nextRt = nextTr.GetComponent<RectTransform>();
            nextRt.anchorMin = new Vector2(0.5f, 0f);
            nextRt.anchorMax = new Vector2(0.5f, 0f);
            nextRt.anchoredPosition = new Vector2(0f, 110f);
            nextRt.sizeDelta = new Vector2(560f, 130f);

            Transform nTxtTr = nextTr.Find("Text");
            if (nTxtTr == null)
            {
                GameObject ntGo = new GameObject("Text");
                ntGo.transform.SetParent(nextTr, false);
                nTxtTr = ntGo.transform;
            }
            Text nTxt = nTxtTr.GetComponent<Text>() ?? nTxtTr.gameObject.AddComponent<Text>();
            nTxt.font = defaultFont;
            nTxt.fontSize = 44;
            nTxt.fontStyle = FontStyle.Bold;
            nTxt.alignment = TextAnchor.MiddleCenter;
            nTxt.color = Color.white;
            nTxt.text = "NEXT LEVEL  ▶";
            nTxtTr.GetComponent<RectTransform>().sizeDelta = new Vector2(560f, 130f);

            uiSo.FindProperty("_victoryPanel").objectReferenceValue = vicTr.gameObject;
            uiSo.FindProperty("_victoryCanvasGroup").objectReferenceValue = vicCg;
            uiSo.FindProperty("_victoryCard").objectReferenceValue = vicCardTr;
            uiSo.FindProperty("_victoryTitleText").objectReferenceValue = vicTitle;
            uiSo.FindProperty("_nextLevelButton").objectReferenceValue = nextBtn;

            SerializedProperty vicStarsProp = uiSo.FindProperty("_victoryStars");
            vicStarsProp.arraySize = 3;
            for (int s = 0; s < 3; s++)
            {
                vicStarsProp.GetArrayElementAtIndex(s).objectReferenceValue = starsList[s];
            }

            // 3. Defeat Panel
            Transform defTr = canvasGo.transform.Find("DefeatPanel");
            if (defTr == null)
            {
                GameObject defGo = new GameObject("DefeatPanel");
                defGo.transform.SetParent(canvasGo.transform, false);
                defTr = defGo.transform;
            }
            Image defBg = defTr.GetComponent<Image>() ?? defTr.gameObject.AddComponent<Image>();
            defBg.color = new Color(0.12f, 0.04f, 0.04f, 0.85f);
            CanvasGroup defCg = defTr.GetComponent<CanvasGroup>() ?? defTr.gameObject.AddComponent<CanvasGroup>();
            RectTransform defRt = defTr.GetComponent<RectTransform>();
            defRt.anchorMin = Vector2.zero;
            defRt.anchorMax = Vector2.one;
            defRt.sizeDelta = Vector2.zero;

            Transform defCardTr = defTr.Find("DefeatCard");
            if (defCardTr == null)
            {
                GameObject dCardGo = new GameObject("DefeatCard");
                dCardGo.transform.SetParent(defTr, false);
                defCardTr = dCardGo.transform;
            }
            Image defCardImg = defCardTr.GetComponent<Image>() ?? defCardTr.gameObject.AddComponent<Image>();
            defCardImg.color = new Color(0.18f, 0.14f, 0.16f, 0.98f);
            RectTransform defCardRt = defCardTr.GetComponent<RectTransform>();
            defCardRt.anchorMin = new Vector2(0.5f, 0.5f);
            defCardRt.anchorMax = new Vector2(0.5f, 0.5f);
            defCardRt.anchoredPosition = Vector2.zero;
            defCardRt.sizeDelta = new Vector2(820f, 850f);

            // Defeat Title
            Transform defTitleTr = defCardTr.Find("Title");
            if (defTitleTr == null)
            {
                GameObject dtGo = new GameObject("Title");
                dtGo.transform.SetParent(defCardTr, false);
                defTitleTr = dtGo.transform;
            }
            Text defTitle = defTitleTr.GetComponent<Text>() ?? defTitleTr.gameObject.AddComponent<Text>();
            defTitle.font = defaultFont;
            defTitle.fontSize = 54;
            defTitle.fontStyle = FontStyle.Bold;
            defTitle.alignment = TextAnchor.MiddleCenter;
            defTitle.color = new Color(1f, 0.35f, 0.35f);
            defTitle.text = "LEVEL FAILED!";
            RectTransform dtRt = defTitleTr.GetComponent<RectTransform>();
            dtRt.anchorMin = new Vector2(0.5f, 1f);
            dtRt.anchorMax = new Vector2(0.5f, 1f);
            dtRt.anchoredPosition = new Vector2(0f, -100f);
            dtRt.sizeDelta = new Vector2(760f, 80f);

            // Defeat Reason
            Transform defReasonTr = defCardTr.Find("Reason");
            if (defReasonTr == null)
            {
                GameObject drGo = new GameObject("Reason");
                drGo.transform.SetParent(defCardTr, false);
                defReasonTr = drGo.transform;
            }
            Text defReason = defReasonTr.GetComponent<Text>() ?? defReasonTr.gameObject.AddComponent<Text>();
            defReason.font = defaultFont;
            defReason.fontSize = 34;
            defReason.alignment = TextAnchor.MiddleCenter;
            defReason.color = new Color(0.85f, 0.85f, 0.85f);
            defReason.text = "OUT OF SLOTS!\nNo empty slots left to park screws.";
            RectTransform drRt = defReasonTr.GetComponent<RectTransform>();
            drRt.anchorMin = new Vector2(0.5f, 1f);
            drRt.anchorMax = new Vector2(0.5f, 1f);
            drRt.anchoredPosition = new Vector2(0f, -230f);
            drRt.sizeDelta = new Vector2(700f, 120f);

            // Retry Button
            Transform retryTr = defCardTr.Find("RetryButton");
            if (retryTr == null)
            {
                GameObject retGo = new GameObject("RetryButton");
                retGo.transform.SetParent(defCardTr, false);
                retryTr = retGo.transform;
            }
            Image retryImg = retryTr.GetComponent<Image>() ?? retryTr.gameObject.AddComponent<Image>();
            retryImg.color = new Color(0.95f, 0.45f, 0.20f);
            Button retryBtn = retryTr.GetComponent<Button>() ?? retryTr.gameObject.AddComponent<Button>();
            RectTransform retryRt = retryTr.GetComponent<RectTransform>();
            retryRt.anchorMin = new Vector2(0.5f, 0f);
            retryRt.anchorMax = new Vector2(0.5f, 0f);
            retryRt.anchoredPosition = new Vector2(0f, 110f);
            retryRt.sizeDelta = new Vector2(560f, 130f);

            Transform rTxtTr = retryTr.Find("Text");
            if (rTxtTr == null)
            {
                GameObject rtGo = new GameObject("Text");
                rtGo.transform.SetParent(retryTr, false);
                rTxtTr = rtGo.transform;
            }
            Text rTxt = rTxtTr.GetComponent<Text>() ?? rTxtTr.gameObject.AddComponent<Text>();
            rTxt.font = defaultFont;
            rTxt.fontSize = 44;
            rTxt.fontStyle = FontStyle.Bold;
            rTxt.alignment = TextAnchor.MiddleCenter;
            rTxt.color = Color.white;
            rTxt.text = "TRY AGAIN  ↻";
            rTxtTr.GetComponent<RectTransform>().sizeDelta = new Vector2(560f, 130f);

            uiSo.FindProperty("_defeatPanel").objectReferenceValue = defTr.gameObject;
            uiSo.FindProperty("_defeatCanvasGroup").objectReferenceValue = defCg;
            uiSo.FindProperty("_defeatCard").objectReferenceValue = defCardTr;
            uiSo.FindProperty("_defeatTitleText").objectReferenceValue = defTitle;
            uiSo.FindProperty("_defeatReasonText").objectReferenceValue = defReason;
            uiSo.FindProperty("_retryButton").objectReferenceValue = retryBtn;

            uiSo.ApplyModifiedProperties();
            uiController.HideAllPopupsImmediately();
            EditorUtility.SetDirty(uiController);
        }

        private static void SetupLevelManager()
        {
            LevelManager lm = Object.FindFirstObjectByType<LevelManager>();
            if (lm == null)
            {
                GameObject lmGo = GameObject.Find("LevelManager");
                if (lmGo == null) lmGo = new GameObject("LevelManager");
                lm = lmGo.AddComponent<LevelManager>();
            }

            SerializedObject lmSo = new SerializedObject(lm);
            GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
            if (puzzleRoot != null)
            {
                lmSo.FindProperty("_puzzleRoot").objectReferenceValue = puzzleRoot.transform;
            }

            ScrewManager sm = Object.FindFirstObjectByType<ScrewManager>();
            if (sm != null) lmSo.FindProperty("_screwManager").objectReferenceValue = sm;

            BoxQueueManager bqm = Object.FindFirstObjectByType<BoxQueueManager>();
            if (bqm != null) lmSo.FindProperty("_boxQueueManager").objectReferenceValue = bqm;

            WaitingSlotManager wsm = Object.FindFirstObjectByType<WaitingSlotManager>();
            if (wsm != null) lmSo.FindProperty("_waitingSlotManager").objectReferenceValue = wsm;

            GameUIController gui = Object.FindFirstObjectByType<GameUIController>();
            if (gui != null) lmSo.FindProperty("_gameUI").objectReferenceValue = gui;

            lmSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(lm);
        }

        private static PlateObject CreatePlate(Transform parent, string name, Vector3 localPos, Quaternion localRot, Vector3 plateSize, Material mat)
        {
            GameObject plateGo = new GameObject(name);
            plateGo.transform.SetParent(parent, false);
            plateGo.transform.localPosition = localPos;
            plateGo.transform.localRotation = localRot;
            plateGo.transform.localScale = Vector3.one;

            GameObject visualGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visualGo.name = "Visual";
            visualGo.transform.SetParent(plateGo.transform, false);
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localRotation = Quaternion.identity;
            visualGo.transform.localScale = plateSize;
            visualGo.GetComponent<MeshRenderer>().sharedMaterial = mat;

            Object.DestroyImmediate(visualGo.GetComponent<Collider>());

            BoxCollider boxCollider = plateGo.AddComponent<BoxCollider>();
            boxCollider.size = plateSize;
            boxCollider.center = Vector3.zero;

            Rigidbody rb = plateGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            PlateObject plate = plateGo.AddComponent<PlateObject>();
            Undo.RegisterCreatedObjectUndo(plateGo, $"Create {name}");
            return plate;
        }

        private static ScrewObject CreateScrew(GameObject prefab, PlateObject parentPlate, string name, Vector2 xyPos, float plateThickness, ColorType color, ScrewAnimationConfigSO animConfig)
        {
            GameObject screwGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parentPlate.transform);
            screwGo.name = name;

            float surfaceZ = (plateThickness * 0.5f) - 0.107f;
            screwGo.transform.localPosition = new Vector3(xyPos.x, xyPos.y, surfaceZ);
            screwGo.transform.localRotation = Quaternion.identity;
            screwGo.transform.localScale = Vector3.one;

            ScrewObject screw = screwGo.GetComponent<ScrewObject>();
            SerializedObject so = new SerializedObject(screw);
            so.FindProperty("_color").enumValueIndex = (int)color;
            so.FindProperty("_parentPlate").objectReferenceValue = parentPlate;
            if (animConfig != null)
            {
                so.FindProperty("_animationConfig").objectReferenceValue = animConfig;
            }
            so.ApplyModifiedProperties();

            screw.ApplyColorVisual();

            Undo.RegisterCreatedObjectUndo(screwGo, $"Create {name}");
            return screw;
        }

        private static void LinkBlocker(PlateObject coveringPlate, ScrewObject blockedScrew)
        {
            if (coveringPlate == null || blockedScrew == null) return;

            SerializedObject plateSo = new SerializedObject(coveringPlate);
            SerializedProperty coveredProp = plateSo.FindProperty("_coveredScrews");
            coveredProp.arraySize++;
            coveredProp.GetArrayElementAtIndex(coveredProp.arraySize - 1).objectReferenceValue = blockedScrew;
            plateSo.ApplyModifiedProperties();

            SerializedObject screwSo = new SerializedObject(blockedScrew);
            SerializedProperty blockingProp = screwSo.FindProperty("_blockingPlates");
            blockingProp.arraySize++;
            blockingProp.GetArrayElementAtIndex(blockingProp.arraySize - 1).objectReferenceValue = coveringPlate;
            screwSo.ApplyModifiedProperties();
        }

        private static Material GetOrCreateMaterial(string matName, Color color, float smoothness, float metallic)
        {
            string path = $"{MaterialsFolder}/{matName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            if (mat == null)
            {
                mat = new Material(shader) { name = matName };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
