using System.Collections.Generic;
using ScrewSolver.Config;
using ScrewSolver.Core;
using ScrewSolver.Gameplay.Boxes;
using ScrewSolver.Gameplay.Controllers;
using ScrewSolver.Gameplay.Holes;
using ScrewSolver.Gameplay.Level;
using ScrewSolver.Gameplay.Plates;
using ScrewSolver.Gameplay.Screws;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScrewSolver.Editor
{
    /// <summary>
    /// 8 Adet Tam Teşekküllü, Birbirinden Farklı 3D Mekanik Bulmaca Seviyesini
    /// (Küp, Silindir, Haç, Altıgen Kule, Piramit, Türbin, L-Blok, Master Kasa)
    /// Prefab olarak üreten ve LevelManager'a bağlayan otomatik seviye mimarı.
    /// NOT: Şimdilik kullanıcı talebiyle devre dışı bırakıldı.
    /// </summary>
    // [InitializeOnLoad] // İPTAL EDİLDİ: Otomatik seviye üretimi durduruldu
    public static class LevelsGeneratorTool
    {
        private const string LevelsFolder = "Assets/Prefabs/Levels";
        private const string PrefabPath = "Assets/Prefabs/Screw.prefab";
        private const string AnimConfigPath = "Assets/ScrewAnimationConfig.asset";
        private const string MaterialsFolder = "Assets/Materials";
        private const string AutoGenKey = "LevelsGenerator_AutoBuilt_v6";

        /* İPTAL EDİLDİ: Editör açılışında otomatik seviye üretimi kapalı
        static LevelsGeneratorTool()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying) return;
                if (!SessionState.GetBool(AutoGenKey, false))
                {
                    SessionState.SetBool(AutoGenKey, true);
                    GenerateAllLevels(silent: true);
                }
            };
        }
        */

        [MenuItem("ScrewSolver/Seviye/Eski Seviye Üretici (Yeni Pencereyi Aç)")]
        public static void GenerateMenuCommand()
        {
            Generator.LevelGeneratorWindow.ShowWindow();
        }

        public static void GenerateAllLevels(bool silent = false)
        {
            Debug.Log("[LevelsGeneratorTool] Level Generator şimdilik devre dışı bırakıldığı için çalıştırılmadı.");
            return;
#pragma warning disable CS0162
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                if (!silent) EditorUtility.DisplayDialog("Uyarı", "Lütfen oyun modundan çıkıp tekrar deneyin.", "Tamam");
                return;
            }

            if (!AssetDatabase.IsValidFolder(LevelsFolder))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs", "Levels");
            }

            GameObject screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            ScrewAnimationConfigSO animConfig = AssetDatabase.LoadAssetAtPath<ScrewAnimationConfigSO>(AnimConfigPath);

            if (screwPrefab == null)
            {
                Debug.LogError("[LevelsGenerator] Screw.prefab bulunamadı!");
                return;
            }

            // Ortak Materyaller
            Material coreMat = GetOrCreateMaterial("CoreDark_Mat", new Color(0.16f, 0.17f, 0.20f), 0.7f, 0.2f);
            Material plateMatBlue = GetOrCreateMaterial("PlateBlue_Mat", new Color(0.18f, 0.58f, 0.92f), 0.6f, 0.1f);
            Material plateMatYellow = GetOrCreateMaterial("PlateYellow_Mat", new Color(0.96f, 0.78f, 0.18f), 0.6f, 0.1f);
            Material plateMatPurple = GetOrCreateMaterial("PlatePurple_Mat", new Color(0.72f, 0.38f, 0.88f), 0.6f, 0.1f);
            Material plateMatGreen = GetOrCreateMaterial("PlateGreen_Mat", new Color(0.24f, 0.78f, 0.52f), 0.6f, 0.1f);
            Material plateMatMetal = GetOrCreateMaterial("PlateMetal_Mat", new Color(0.40f, 0.44f, 0.48f), 0.9f, 0.6f);

            List<GameObject> generatedPrefabs = new List<GameObject>();

            // 1. Seviyeleri Geçici Olarak Sahne Kökünde Oluştur, Prefab Yap ve Sahneden Sil
            GameObject tempRoot = new GameObject("Temp_Level_Generator");

            try
            {
                // Level 01: Core Cube (32 Vida: 8 Mavi, 8 Sarı, 8 Mor, 8 Yeşil)
                generatedPrefabs.Add(BuildAndSaveLevel(tempRoot, "Level_01", (root) =>
                {
                    BuildLevel01_Cube(root, screwPrefab, animConfig, coreMat, plateMatBlue, plateMatYellow, plateMatPurple, plateMatGreen);
                }));

                // Level 02: Cylinder Barrel (16 Vida: 4 Mavi, 4 Sarı, 4 Mor, 4 Yeşil)
                generatedPrefabs.Add(BuildAndSaveLevel(tempRoot, "Level_02", (root) =>
                {
                    BuildLevel02_Cylinder(root, screwPrefab, animConfig, coreMat, plateMatBlue, plateMatYellow, plateMatPurple, plateMatGreen);
                }));

                // Level 03: Industrial Cross (20 Vida: 8 Mavi, 4 Sarı, 4 Mor, 4 Yeşil)
                generatedPrefabs.Add(BuildAndSaveLevel(tempRoot, "Level_03", (root) =>
                {
                    BuildLevel03_Cross(root, screwPrefab, animConfig, coreMat, plateMatBlue, plateMatYellow, plateMatPurple, plateMatGreen);
                }));

                // Level 04: Hexagonal Prism Tower (24 Vida: 8 Mavi, 8 Sarı, 4 Mor, 4 Yeşil)
                generatedPrefabs.Add(BuildAndSaveLevel(tempRoot, "Level_04", (root) =>
                {
                    BuildLevel04_HexPrism(root, screwPrefab, animConfig, coreMat, plateMatBlue, plateMatYellow, plateMatPurple, plateMatGreen);
                }));

                // Level 05: Pyramid Obelisk (24 Vida: 4 Mavi, 8 Sarı, 8 Mor, 4 Yeşil)
                generatedPrefabs.Add(BuildAndSaveLevel(tempRoot, "Level_05", (root) =>
                {
                    BuildLevel05_Pyramid(root, screwPrefab, animConfig, coreMat, plateMatBlue, plateMatYellow, plateMatPurple, plateMatGreen);
                }));

                // Level 06: Turbine & Blades (28 Vida: 8 Mavi, 8 Sarı, 8 Mor, 4 Yeşil)
                generatedPrefabs.Add(BuildAndSaveLevel(tempRoot, "Level_06", (root) =>
                {
                    BuildLevel06_Turbine(root, screwPrefab, animConfig, coreMat, plateMatBlue, plateMatYellow, plateMatPurple, plateMatGreen);
                }));

                // Level 07: L-Block Dual Cube (32 Vida: 8 Mavi, 8 Sarı, 8 Mor, 8 Yeşil)
                generatedPrefabs.Add(BuildAndSaveLevel(tempRoot, "Level_07", (root) =>
                {
                    BuildLevel07_LBlock(root, screwPrefab, animConfig, coreMat, plateMatBlue, plateMatYellow, plateMatPurple, plateMatGreen);
                }));

                // Level 08: Master Armored Vault (36 Vida: 12 Mavi, 8 Sarı, 8 Mor, 8 Yeşil)
                generatedPrefabs.Add(BuildAndSaveLevel(tempRoot, "Level_08", (root) =>
                {
                    BuildLevel08_Vault(root, screwPrefab, animConfig, coreMat, plateMatBlue, plateMatYellow, plateMatPurple, plateMatGreen, plateMatMetal);
                }));
            }
            finally
            {
                Object.DestroyImmediate(tempRoot);
            }

            // 2. LevelManager'a bu 8 Prefab'ı Bağla
            LevelManager lm = Object.FindFirstObjectByType<LevelManager>();
            if (lm != null)
            {
                SerializedObject lmSo = new SerializedObject(lm);
                SerializedProperty listProp = lmSo.FindProperty("_levelPrefabs");
                listProp.arraySize = generatedPrefabs.Count;
                for (int i = 0; i < generatedPrefabs.Count; i++)
                {
                    listProp.GetArrayElementAtIndex(i).objectReferenceValue = generatedPrefabs[i];
                }
                lmSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(lm);
            }

            // 3. PuzzleRoot içine varsayılan olarak Level_01'i yerleştir
            GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
            if (puzzleRoot != null && generatedPrefabs.Count > 0)
            {
                for (int i = puzzleRoot.transform.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(puzzleRoot.transform.GetChild(i).gameObject);
                }

                GameObject activeLevel = (GameObject)PrefabUtility.InstantiatePrefab(generatedPrefabs[0], puzzleRoot.transform);
                activeLevel.transform.localPosition = Vector3.zero;
                activeLevel.transform.localRotation = Quaternion.identity;
                activeLevel.transform.localScale = Vector3.one;

                // Delik yuvalarını yenile
                ScrewHoleTool.SetupAllHoles();

                // Kutu kuyruğunu güncelle
                BoxQueueManager bqm = Object.FindFirstObjectByType<BoxQueueManager>();
                if (bqm != null)
                {
                    bqm.AutoPopulateQueueFromScrews();
                    EditorUtility.SetDirty(bqm);
                }

                EditorSceneManager.MarkSceneDirty(puzzleRoot.scene);
                EditorSceneManager.SaveScene(puzzleRoot.scene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("<color=#00ff88><b>[LevelsGenerator]</b> 8 Adet Eşsiz 3D Seviye Prefab'ı başarıyla üretildi ve LevelManager'a bağlandı!</color>");
            if (!silent)
            {
                EditorUtility.DisplayDialog("8 Seviye Hazır!",
                    "Tebrikler! Toplam 8 seviye başarıyla üretildi:\n\n" +
                    "• Level 1: Core Cube (32 Vida / 8 Kutu)\n" +
                    "• Level 2: Cylinder Barrel (16 Vida / 4 Kutu)\n" +
                    "• Level 3: Industrial Cross (20 Vida / 5 Kutu)\n" +
                    "• Level 4: Hexagonal Prism Tower (24 Vida / 6 Kutu)\n" +
                    "• Level 5: Pyramid Obelisk (24 Vida / 6 Kutu)\n" +
                    "• Level 6: Turbine & Blades (28 Vida / 7 Kutu)\n" +
                    "• Level 7: L-Block Dual Cube (32 Vida / 8 Kutu)\n" +
                    "• Level 8: Master Armored Vault (36 Vida / 9 Kutu)\n\n" +
                    "Her seviyedeki vida renkleri tam 4'ün katıdır (0 artık vida, %100 kusursuz kutu akışı). " +
                    "Kalan +2 seviyeyi de istediğiniz gibi ekleyebilirsiniz!", "Harika!");
            }
#pragma warning restore CS0162
        }

        private static GameObject BuildAndSaveLevel(GameObject tempRoot, string levelName, System.Action<GameObject> buildAction)
        {
            GameObject levelGo = new GameObject(levelName);
            levelGo.transform.SetParent(tempRoot.transform, false);
            levelGo.transform.localPosition = Vector3.zero;
            levelGo.transform.localRotation = Quaternion.identity;
            levelGo.transform.localScale = Vector3.one;

            buildAction.Invoke(levelGo);

            // Her vidanın altına siyah soket deliği ekle
            ScrewObject[] screws = levelGo.GetComponentsInChildren<ScrewObject>(true);
            foreach (var s in screws)
            {
                ScrewHoleTool.SetupHoleForScrew(s.gameObject);
            }

            // Plaka referanslarını bağla
            PlateObject[] plates = levelGo.GetComponentsInChildren<PlateObject>(true);
            foreach (var p in plates)
            {
                p.AutoFindAttachedScrews();
            }

            // Prefab olarak kaydet
            string prefabPath = $"{LevelsFolder}/{levelName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(levelGo, prefabPath);
            Object.DestroyImmediate(levelGo);

            return prefab;
        }

        // ==========================================
        // LEVEL 1: Core Cube (32 Vida)
        // ==========================================
        private static void BuildLevel01_Cube(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, Material coreMat, Material bMat, Material yMat, Material pMat, Material gMat)
        {
            Material matChassis = GetOrCreateMaterial("Mat_ChassisCore", new Color(0.14f, 0.16f, 0.20f, 1f), 0.35f, 0.4f);
            Material matBlue = GetOrCreateMaterial("Mat_PlateBlue", new Color(0.20f, 0.60f, 1.00f, 1f), 0.55f, 0.1f);
            Material matPurple = GetOrCreateMaterial("Mat_PlatePurple", new Color(0.61f, 0.35f, 0.71f, 1f), 0.55f, 0.1f);
            Material matYellow = GetOrCreateMaterial("Mat_PlateYellow", new Color(0.95f, 0.77f, 0.06f, 1f), 0.55f, 0.1f);
            Material matGreen = GetOrCreateMaterial("Mat_PlateGreen", new Color(0.18f, 0.80f, 0.44f, 1f), 0.55f, 0.1f);
            Material matTeal = GetOrCreateMaterial("Mat_PlateTeal", new Color(0.10f, 0.74f, 0.61f, 1f), 0.55f, 0.1f);

            // Merkez Gövde (1.50 x 1.70 x 1.50)
            GameObject chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chassis.name = "Chassis_Core";
            chassis.transform.SetParent(root.transform, false);
            chassis.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            chassis.transform.localRotation = Quaternion.identity;
            chassis.transform.localScale = new Vector3(1.50f, 1.70f, 1.50f);
            chassis.GetComponent<MeshRenderer>().sharedMaterial = matChassis;

            const float t = 0.06f; // İnce ve geniş orijinal plaka kalınlığı

            // YÜZEY 1: ÖN CEPHE (FRONT +Z) - 3 Katmanlı
            PlateObject pFrontBase = CreatePlate(root.transform, "Plate_Front_Base",
                new Vector3(0f, 0.05f, 0.78f), Quaternion.identity, new Vector3(1.30f, 1.30f, t), matTeal);

            ScrewObject sFront1 = CreateScrew(screwPrefab, pFrontBase, "Screw_Front_Yellow_1", new Vector2(-0.45f, 0.45f), t, ColorType.Yellow, anim);
            ScrewObject sFront2 = CreateScrew(screwPrefab, pFrontBase, "Screw_Front_Yellow_2", new Vector2(0.45f, -0.45f), t, ColorType.Yellow, anim);
            ScrewObject sFront3Covered = CreateScrew(screwPrefab, pFrontBase, "Screw_Front_Blue_Covered_1", new Vector2(-0.45f, -0.45f), t, ColorType.Blue, anim);
            ScrewObject sFront4Covered = CreateScrew(screwPrefab, pFrontBase, "Screw_Front_Green_Covered_1", new Vector2(0.45f, 0.45f), t, ColorType.Green, anim);

            PlateObject pFrontLBracket = CreatePlate(root.transform, "Plate_Front_L_Bracket",
                new Vector3(0.05f, 0f, 0.84f), Quaternion.identity, new Vector3(0.60f, 1.15f, t), matPurple);

            ScrewObject sFront5 = CreateScrew(screwPrefab, pFrontLBracket, "Screw_Front_Yellow_3", new Vector2(0f, -0.35f), t, ColorType.Yellow, anim);
            ScrewObject sFront6Covered = CreateScrew(screwPrefab, pFrontLBracket, "Screw_Front_Green_Covered_2", new Vector2(0f, 0.35f), t, ColorType.Green, anim);

            LinkBlocker(pFrontLBracket, sFront3Covered);
            LinkBlocker(pFrontLBracket, sFront4Covered);

            PlateObject pFrontLockBar = CreatePlate(root.transform, "Plate_Front_Lock_Bar",
                new Vector3(0.05f, 0.35f, 0.90f), Quaternion.identity, new Vector3(0.95f, 0.30f, t), matBlue);

            ScrewObject sFront7 = CreateScrew(screwPrefab, pFrontLockBar, "Screw_Front_Blue_2", new Vector2(-0.35f, 0f), t, ColorType.Blue, anim);
            ScrewObject sFront8 = CreateScrew(screwPrefab, pFrontLockBar, "Screw_Front_Purple_1", new Vector2(0.35f, 0f), t, ColorType.Purple, anim);

            LinkBlocker(pFrontLockBar, sFront6Covered);

            // YÜZEY 2: ARKA CEPHE (BACK -Z)
            PlateObject pBackBase = CreatePlate(root.transform, "Plate_Back_Base",
                new Vector3(0f, -0.05f, -0.78f), Quaternion.Euler(0f, 180f, 0f), new Vector3(1.30f, 1.30f, t), matYellow);

            ScrewObject sBack1 = CreateScrew(screwPrefab, pBackBase, "Screw_Back_Green_1", new Vector2(-0.45f, 0.45f), t, ColorType.Green, anim);
            ScrewObject sBack2 = CreateScrew(screwPrefab, pBackBase, "Screw_Back_Blue_3", new Vector2(0.45f, -0.45f), t, ColorType.Blue, anim);
            ScrewObject sBack3Covered = CreateScrew(screwPrefab, pBackBase, "Screw_Back_Yellow_Covered_4", new Vector2(-0.45f, -0.45f), t, ColorType.Yellow, anim);
            ScrewObject sBack4Covered = CreateScrew(screwPrefab, pBackBase, "Screw_Back_Purple_Covered_2", new Vector2(0.45f, 0.45f), t, ColorType.Purple, anim);

            PlateObject pBackDiagonal = CreatePlate(root.transform, "Plate_Back_Diagonal_Strut",
                new Vector3(0f, -0.05f, -0.84f), Quaternion.Euler(0f, 180f, 35f), new Vector3(1.35f, 0.32f, t), matBlue);

            ScrewObject sBack5 = CreateScrew(screwPrefab, pBackDiagonal, "Screw_Back_Blue_4", new Vector2(-0.40f, 0f), t, ColorType.Blue, anim);
            ScrewObject sBack6 = CreateScrew(screwPrefab, pBackDiagonal, "Screw_Back_Green_2", new Vector2(0.40f, 0f), t, ColorType.Green, anim);

            LinkBlocker(pBackDiagonal, sBack3Covered);
            LinkBlocker(pBackDiagonal, sBack4Covered);

            // YÜZEY 3: SAĞ CEPHE (RIGHT +X)
            PlateObject pRightCol = CreatePlate(root.transform, "Plate_Right_Column",
                new Vector3(0.78f, -0.05f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector3(0.45f, 1.40f, t), matGreen);

            ScrewObject sRight1 = CreateScrew(screwPrefab, pRightCol, "Screw_Right_Yellow_5", new Vector2(0f, 0.50f), t, ColorType.Yellow, anim);
            ScrewObject sRight2 = CreateScrew(screwPrefab, pRightCol, "Screw_Right_Green_3", new Vector2(0f, -0.50f), t, ColorType.Green, anim);
            ScrewObject sRight3Covered = CreateScrew(screwPrefab, pRightCol, "Screw_Right_Blue_Covered_5", new Vector2(0f, 0f), t, ColorType.Blue, anim);

            PlateObject pRightBelt = CreatePlate(root.transform, "Plate_Right_Belt",
                new Vector3(0.84f, -0.05f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector3(1.25f, 0.36f, t), matYellow);

            ScrewObject sRight4 = CreateScrew(screwPrefab, pRightBelt, "Screw_Right_Purple_3", new Vector2(-0.45f, 0f), t, ColorType.Purple, anim);
            ScrewObject sRight5 = CreateScrew(screwPrefab, pRightBelt, "Screw_Right_Yellow_6", new Vector2(0.45f, 0f), t, ColorType.Yellow, anim);
            ScrewObject sRight6 = CreateScrew(screwPrefab, pRightBelt, "Screw_Right_Blue_6", new Vector2(0.18f, 0f), t, ColorType.Blue, anim);

            LinkBlocker(pRightBelt, sRight3Covered);

            // YÜZEY 4: SOL CEPHE (LEFT -X)
            PlateObject pLeftCol = CreatePlate(root.transform, "Plate_Left_Column",
                new Vector3(-0.78f, -0.05f, 0f), Quaternion.Euler(0f, -90f, 0f), new Vector3(0.45f, 1.40f, t), matPurple);

            ScrewObject sLeft1 = CreateScrew(screwPrefab, pLeftCol, "Screw_Left_Green_4", new Vector2(0f, 0.50f), t, ColorType.Green, anim);
            ScrewObject sLeft2 = CreateScrew(screwPrefab, pLeftCol, "Screw_Left_Purple_4", new Vector2(0f, -0.50f), t, ColorType.Purple, anim);
            ScrewObject sLeft3Covered = CreateScrew(screwPrefab, pLeftCol, "Screw_Left_Yellow_Covered_7", new Vector2(0f, 0f), t, ColorType.Yellow, anim);

            PlateObject pLeftClamp = CreatePlate(root.transform, "Plate_Left_Clamp",
                new Vector3(-0.84f, -0.05f, 0f), Quaternion.Euler(0f, -90f, 0f), new Vector3(1.25f, 0.36f, t), matTeal);

            ScrewObject sLeft4 = CreateScrew(screwPrefab, pLeftClamp, "Screw_Left_Green_5", new Vector2(-0.45f, 0f), t, ColorType.Green, anim);
            ScrewObject sLeft5 = CreateScrew(screwPrefab, pLeftClamp, "Screw_Left_Purple_5", new Vector2(0.45f, 0f), t, ColorType.Purple, anim);
            ScrewObject sLeft6 = CreateScrew(screwPrefab, pLeftClamp, "Screw_Left_Yellow_8", new Vector2(0.18f, 0f), t, ColorType.Yellow, anim);

            LinkBlocker(pLeftClamp, sLeft3Covered);

            // YÜZEY 5: ÜST CEPHE (TOP +Y)
            PlateObject pTopRoof = CreatePlate(root.transform, "Plate_Top_Roof",
                new Vector3(0f, 0.83f, 0f), Quaternion.Euler(-90f, 0f, 0f), new Vector3(1.25f, 1.25f, t), matYellow);

            ScrewObject sTop1 = CreateScrew(screwPrefab, pTopRoof, "Screw_Top_Blue_7", new Vector2(-0.45f, 0.45f), t, ColorType.Blue, anim);
            ScrewObject sTop2 = CreateScrew(screwPrefab, pTopRoof, "Screw_Top_Purple_6", new Vector2(0.45f, -0.45f), t, ColorType.Purple, anim);
            ScrewObject sTop3Covered = CreateScrew(screwPrefab, pTopRoof, "Screw_Top_Green_Covered_8", new Vector2(0f, 0.35f), t, ColorType.Green, anim);
            ScrewObject sTop4Covered = CreateScrew(screwPrefab, pTopRoof, "Screw_Top_Purple_Covered_7", new Vector2(0f, -0.35f), t, ColorType.Purple, anim);

            PlateObject pTopBridge = CreatePlate(root.transform, "Plate_Top_Bridge",
                new Vector3(0f, 0.89f, 0f), Quaternion.Euler(-90f, 0f, 0f), new Vector3(0.38f, 1.25f, t), matBlue);

            ScrewObject sTop5 = CreateScrew(screwPrefab, pTopBridge, "Screw_Top_Blue_8", new Vector2(0f, -0.42f), t, ColorType.Blue, anim);
            ScrewObject sTop6 = CreateScrew(screwPrefab, pTopBridge, "Screw_Top_Purple_8", new Vector2(0f, 0.42f), t, ColorType.Purple, anim);

            LinkBlocker(pTopBridge, sTop3Covered);
            LinkBlocker(pTopBridge, sTop4Covered);
        }

        // ==========================================
        // LEVEL 2: Cylinder Barrel (16 Vida: 4 Mavi, 4 Sarı, 4 Mor, 4 Yeşil)
        // ==========================================
        private static void BuildLevel02_Cylinder(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, Material coreMat, Material bMat, Material yMat, Material pMat, Material gMat)
        {
            GameObject coreGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coreGo.name = "CylinderCore";
            coreGo.transform.SetParent(root.transform, false);
            coreGo.transform.localScale = new Vector3(1.8f, 1.3f, 1.8f);
            coreGo.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

            // 4 Adet Çevreleyici Plaka (Kuzey, Güney, Doğu, Batı)
            // Kuzey Plakası (Mavi - 4 Vida)
            PlateObject pNorth = CreatePlate(root.transform, "Plate_North_Blue", new Vector3(0f, 0.2f, 1.02f), Quaternion.identity, new Vector3(1.1f, 1.2f, 0.10f), bMat);
            CreateScrew(screwPrefab, pNorth, "S_Blu_1", new Vector2(-0.35f, 0.35f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pNorth, "S_Blu_2", new Vector2(0.35f, 0.35f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pNorth, "S_Blu_3", new Vector2(-0.35f, -0.35f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pNorth, "S_Blu_4", new Vector2(0.35f, -0.35f), 0.10f, ColorType.Blue, anim);

            // Güney Plakası (Sarı - 4 Vida)
            PlateObject pSouth = CreatePlate(root.transform, "Plate_South_Yellow", new Vector3(0f, -0.2f, -1.02f), Quaternion.Euler(0, 180, 0), new Vector3(1.1f, 1.2f, 0.10f), yMat);
            CreateScrew(screwPrefab, pSouth, "S_Yel_1", new Vector2(-0.35f, 0.35f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pSouth, "S_Yel_2", new Vector2(0.35f, 0.35f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pSouth, "S_Yel_3", new Vector2(-0.35f, -0.35f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pSouth, "S_Yel_4", new Vector2(0.35f, -0.35f), 0.10f, ColorType.Yellow, anim);

            // Doğu Plakası (Mor - 4 Vida)
            PlateObject pEast = CreatePlate(root.transform, "Plate_East_Purple", new Vector3(1.02f, 0f, 0f), Quaternion.Euler(0, 90, 0), new Vector3(1.1f, 1.2f, 0.10f), pMat);
            CreateScrew(screwPrefab, pEast, "S_Pur_1", new Vector2(-0.35f, 0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pEast, "S_Pur_2", new Vector2(0.35f, 0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pEast, "S_Pur_3", new Vector2(-0.35f, -0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pEast, "S_Pur_4", new Vector2(0.35f, -0.35f), 0.10f, ColorType.Purple, anim);

            // Batı Plakası (Yeşil - 4 Vida)
            PlateObject pWest = CreatePlate(root.transform, "Plate_West_Green", new Vector3(-1.02f, 0f, 0f), Quaternion.Euler(0, -90, 0), new Vector3(1.1f, 1.2f, 0.10f), gMat);
            CreateScrew(screwPrefab, pWest, "S_Grn_1", new Vector2(-0.35f, 0.35f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pWest, "S_Grn_2", new Vector2(0.35f, 0.35f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pWest, "S_Grn_3", new Vector2(-0.35f, -0.35f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pWest, "S_Grn_4", new Vector2(0.35f, -0.35f), 0.10f, ColorType.Green, anim);
        }

        // ==========================================
        // LEVEL 3: Industrial Cross (20 Vida: 8 Mavi, 4 Sarı, 4 Mor, 4 Yeşil)
        // ==========================================
        private static void BuildLevel03_Cross(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, Material coreMat, Material bMat, Material yMat, Material pMat, Material gMat)
        {
            // Gövde: İki kesişen prizma (Haç biçimi)
            GameObject barH = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barH.name = "Cross_Bar_H";
            barH.transform.SetParent(root.transform, false);
            barH.transform.localScale = new Vector3(2.6f, 0.8f, 0.8f);
            barH.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

            GameObject barV = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barV.name = "Cross_Bar_V";
            barV.transform.SetParent(root.transform, false);
            barV.transform.localScale = new Vector3(0.8f, 2.6f, 0.8f);
            barV.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

            // 4 Kol Plakaları
            PlateObject pLeft = CreatePlate(root.transform, "Plate_Left_Blue", new Vector3(-1.0f, 0f, 0.48f), Quaternion.identity, new Vector3(0.7f, 0.7f, 0.10f), bMat);
            CreateScrew(screwPrefab, pLeft, "S_Blu_1", new Vector2(-0.2f, 0.2f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pLeft, "S_Blu_2", new Vector2(0.2f, -0.2f), 0.10f, ColorType.Blue, anim);

            PlateObject pRight = CreatePlate(root.transform, "Plate_Right_Blue", new Vector3(1.0f, 0f, 0.48f), Quaternion.identity, new Vector3(0.7f, 0.7f, 0.10f), bMat);
            CreateScrew(screwPrefab, pRight, "S_Blu_3", new Vector2(-0.2f, -0.2f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pRight, "S_Blu_4", new Vector2(0.2f, 0.2f), 0.10f, ColorType.Blue, anim);

            PlateObject pTop = CreatePlate(root.transform, "Plate_Top_Yellow", new Vector3(0f, 1.0f, 0.48f), Quaternion.identity, new Vector3(0.7f, 0.7f, 0.10f), yMat);
            CreateScrew(screwPrefab, pTop, "S_Yel_1", new Vector2(-0.2f, 0.2f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pTop, "S_Yel_2", new Vector2(0.2f, -0.2f), 0.10f, ColorType.Yellow, anim);

            PlateObject pBottom = CreatePlate(root.transform, "Plate_Bottom_Yellow", new Vector3(0f, -1.0f, 0.48f), Quaternion.identity, new Vector3(0.7f, 0.7f, 0.10f), yMat);
            CreateScrew(screwPrefab, pBottom, "S_Yel_3", new Vector2(-0.2f, -0.2f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pBottom, "S_Yel_4", new Vector2(0.2f, 0.2f), 0.10f, ColorType.Yellow, anim);

            // Arka Kollar (Mor & Yeşil)
            PlateObject pBackH = CreatePlate(root.transform, "Plate_Back_Purple", new Vector3(0f, 0f, -0.48f), Quaternion.Euler(0, 180, 0), new Vector3(2.2f, 0.7f, 0.10f), pMat);
            CreateScrew(screwPrefab, pBackH, "S_Pur_1", new Vector2(-0.8f, 0f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pBackH, "S_Pur_2", new Vector2(-0.3f, 0f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pBackH, "S_Pur_3", new Vector2(0.3f, 0f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pBackH, "S_Pur_4", new Vector2(0.8f, 0f), 0.10f, ColorType.Purple, anim);

            PlateObject pBackV = CreatePlate(root.transform, "Plate_Back_Green", new Vector3(0f, 0f, -0.58f), Quaternion.Euler(0, 180, 90), new Vector3(2.2f, 0.7f, 0.10f), gMat);
            CreateScrew(screwPrefab, pBackV, "S_Grn_1", new Vector2(-0.8f, 0f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pBackV, "S_Grn_2", new Vector2(-0.3f, 0f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pBackV, "S_Grn_3", new Vector2(0.3f, 0f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pBackV, "S_Grn_4", new Vector2(0.8f, 0f), 0.10f, ColorType.Green, anim);

            // Merkez Ön Kilit Plakası (Mavi - 4 Vida, kolları kilitler!)
            PlateObject pCenterLock = CreatePlate(root.transform, "Plate_Center_Lock", new Vector3(0f, 0f, 0.58f), Quaternion.Euler(0, 0, 45), new Vector3(1.1f, 1.1f, 0.10f), bMat);
            CreateScrew(screwPrefab, pCenterLock, "S_Blu_5", new Vector2(-0.35f, 0.35f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pCenterLock, "S_Blu_6", new Vector2(0.35f, 0.35f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pCenterLock, "S_Blu_7", new Vector2(-0.35f, -0.35f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pCenterLock, "S_Blu_8", new Vector2(0.35f, -0.35f), 0.10f, ColorType.Blue, anim);
        }

        // ==========================================
        // LEVEL 4: Hexagonal Prism Tower (24 Vida: 8 Mavi, 8 Sarı, 4 Mor, 4 Yeşil)
        // ==========================================
        private static void BuildLevel04_HexPrism(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, Material coreMat, Material bMat, Material yMat, Material pMat, Material gMat)
        {
            GameObject coreGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coreGo.name = "HexCore";
            coreGo.transform.SetParent(root.transform, false);
            coreGo.transform.localScale = new Vector3(1.9f, 1.5f, 1.9f);
            coreGo.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

            // 6 Cephe Etrafında 6 Plaka (6 x 60 derece)
            Material[] mats = new Material[] { bMat, yMat, pMat, gMat, bMat, yMat };
            ColorType[] colors = new ColorType[] { ColorType.Blue, ColorType.Yellow, ColorType.Purple, ColorType.Green, ColorType.Blue, ColorType.Yellow };

            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f;
                float rad = angle * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Sin(rad) * 1.05f, 0f, Mathf.Cos(rad) * 1.05f);
                Quaternion rot = Quaternion.Euler(0f, angle, 0f);

                PlateObject plate = CreatePlate(root.transform, $"Plate_Hex_{i}", pos, rot, new Vector3(0.9f, 1.4f, 0.10f), mats[i]);
                CreateScrew(screwPrefab, plate, $"S_{colors[i]}_{i}_1", new Vector2(-0.25f, 0.45f), 0.10f, colors[i], anim);
                CreateScrew(screwPrefab, plate, $"S_{colors[i]}_{i}_2", new Vector2(0.25f, 0.45f), 0.10f, colors[i], anim);
                CreateScrew(screwPrefab, plate, $"S_{colors[i]}_{i}_3", new Vector2(-0.25f, -0.45f), 0.10f, colors[i], anim);
                CreateScrew(screwPrefab, plate, $"S_{colors[i]}_{i}_4", new Vector2(0.25f, -0.45f), 0.10f, colors[i], anim);
            }
        }

        // ==========================================
        // LEVEL 5: Pyramid Obelisk (24 Vida: 4 Mavi, 8 Sarı, 8 Mor, 4 Yeşil)
        // ==========================================
        private static void BuildLevel05_Pyramid(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, Material coreMat, Material bMat, Material yMat, Material pMat, Material gMat)
        {
            GameObject coreGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            coreGo.name = "PyramidCore";
            coreGo.transform.SetParent(root.transform, false);
            coreGo.transform.localScale = new Vector3(1.8f, 1.8f, 1.8f);
            coreGo.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

            // 4 Eğimli Yüzey Plakası (20 derece içe eğimli)
            PlateObject pFront = CreatePlate(root.transform, "Plate_Pyr_Front", new Vector3(0f, 0f, 1.0f), Quaternion.Euler(-18, 0, 0), new Vector3(1.3f, 1.3f, 0.10f), yMat);
            CreateScrew(screwPrefab, pFront, "S_Yel_1", new Vector2(-0.35f, 0.35f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pFront, "S_Yel_2", new Vector2(0.35f, 0.35f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pFront, "S_Yel_3", new Vector2(-0.35f, -0.35f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pFront, "S_Yel_4", new Vector2(0.35f, -0.35f), 0.10f, ColorType.Yellow, anim);

            PlateObject pBack = CreatePlate(root.transform, "Plate_Pyr_Back", new Vector3(0f, 0f, -1.0f), Quaternion.Euler(18, 180, 0), new Vector3(1.3f, 1.3f, 0.10f), yMat);
            CreateScrew(screwPrefab, pBack, "S_Yel_5", new Vector2(-0.35f, 0.35f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pBack, "S_Yel_6", new Vector2(0.35f, 0.35f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pBack, "S_Yel_7", new Vector2(-0.35f, -0.35f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pBack, "S_Yel_8", new Vector2(0.35f, -0.35f), 0.10f, ColorType.Yellow, anim);

            PlateObject pRight = CreatePlate(root.transform, "Plate_Pyr_Right", new Vector3(1.0f, 0f, 0f), Quaternion.Euler(0, 90, 18), new Vector3(1.3f, 1.3f, 0.10f), pMat);
            CreateScrew(screwPrefab, pRight, "S_Pur_1", new Vector2(-0.35f, 0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pRight, "S_Pur_2", new Vector2(0.35f, 0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pRight, "S_Pur_3", new Vector2(-0.35f, -0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pRight, "S_Pur_4", new Vector2(0.35f, -0.35f), 0.10f, ColorType.Purple, anim);

            PlateObject pLeft = CreatePlate(root.transform, "Plate_Pyr_Left", new Vector3(-1.0f, 0f, 0f), Quaternion.Euler(0, -90, -18), new Vector3(1.3f, 1.3f, 0.10f), pMat);
            CreateScrew(screwPrefab, pLeft, "S_Pur_5", new Vector2(-0.35f, 0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pLeft, "S_Pur_6", new Vector2(0.35f, 0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pLeft, "S_Pur_7", new Vector2(-0.35f, -0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pLeft, "S_Pur_8", new Vector2(0.35f, -0.35f), 0.10f, ColorType.Purple, anim);

            // Tepe Kilit Plakası (Mavi - 4 Vida)
            PlateObject pTopCap = CreatePlate(root.transform, "Plate_Pyr_TopCap", new Vector3(0f, 1.05f, 0f), Quaternion.Euler(90, 0, 0), new Vector3(1.4f, 1.4f, 0.10f), bMat);
            CreateScrew(screwPrefab, pTopCap, "S_Blu_1", new Vector2(-0.4f, 0.4f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pTopCap, "S_Blu_2", new Vector2(0.4f, 0.4f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pTopCap, "S_Blu_3", new Vector2(-0.4f, -0.4f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pTopCap, "S_Blu_4", new Vector2(0.4f, -0.4f), 0.10f, ColorType.Blue, anim);

            // Taban Zırh Plakası (Yeşil - 4 Vida)
            PlateObject pBottomCap = CreatePlate(root.transform, "Plate_Pyr_Bottom", new Vector3(0f, -1.05f, 0f), Quaternion.Euler(-90, 0, 0), new Vector3(1.4f, 1.4f, 0.10f), gMat);
            CreateScrew(screwPrefab, pBottomCap, "S_Grn_1", new Vector2(-0.4f, 0.4f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pBottomCap, "S_Grn_2", new Vector2(0.4f, 0.4f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pBottomCap, "S_Grn_3", new Vector2(-0.4f, -0.4f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pBottomCap, "S_Grn_4", new Vector2(0.4f, -0.4f), 0.10f, ColorType.Green, anim);
        }

        // ==========================================
        // LEVEL 6: Turbine & Blades (28 Vida: 8 Mavi, 8 Sarı, 8 Mor, 4 Yeşil)
        // ==========================================
        private static void BuildLevel06_Turbine(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, Material coreMat, Material bMat, Material yMat, Material pMat, Material gMat)
        {
            GameObject hubGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hubGo.name = "TurbineHub";
            hubGo.transform.SetParent(root.transform, false);
            hubGo.transform.localScale = new Vector3(1.6f, 1.6f, 1.6f);
            hubGo.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

            // 4 Adet Kanat Plakası (Mavi, Sarı, Mor, Yeşil)
            PlateObject blade1 = CreatePlate(root.transform, "Blade_1_Blue", new Vector3(0f, 0.95f, 0.9f), Quaternion.Euler(25, 0, 0), new Vector3(1.2f, 0.6f, 0.10f), bMat);
            CreateScrew(screwPrefab, blade1, "S_Blu_1", new Vector2(-0.35f, 0f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, blade1, "S_Blu_2", new Vector2(0.35f, 0f), 0.10f, ColorType.Blue, anim);

            PlateObject blade2 = CreatePlate(root.transform, "Blade_2_Blue", new Vector3(0f, -0.95f, 0.9f), Quaternion.Euler(-25, 0, 0), new Vector3(1.2f, 0.6f, 0.10f), bMat);
            CreateScrew(screwPrefab, blade2, "S_Blu_3", new Vector2(-0.35f, 0f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, blade2, "S_Blu_4", new Vector2(0.35f, 0f), 0.10f, ColorType.Blue, anim);

            PlateObject blade3 = CreatePlate(root.transform, "Blade_3_Yellow", new Vector3(0.95f, 0f, 0.9f), Quaternion.Euler(0, 25, 90), new Vector3(1.2f, 0.6f, 0.10f), yMat);
            CreateScrew(screwPrefab, blade3, "S_Yel_1", new Vector2(-0.35f, 0f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, blade3, "S_Yel_2", new Vector2(0.35f, 0f), 0.10f, ColorType.Yellow, anim);

            PlateObject blade4 = CreatePlate(root.transform, "Blade_4_Yellow", new Vector3(-0.95f, 0f, 0.9f), Quaternion.Euler(0, -25, 90), new Vector3(1.2f, 0.6f, 0.10f), yMat);
            CreateScrew(screwPrefab, blade4, "S_Yel_3", new Vector2(-0.35f, 0f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, blade4, "S_Yel_4", new Vector2(0.35f, 0f), 0.10f, ColorType.Yellow, anim);

            // Ön Merkez Döner Kilit (Mor - 4 Vida)
            PlateObject centerFront = CreatePlate(root.transform, "Plate_CenterFront_Purple", new Vector3(0f, 0f, 1.05f), Quaternion.identity, new Vector3(1.1f, 1.1f, 0.10f), pMat);
            CreateScrew(screwPrefab, centerFront, "S_Pur_1", new Vector2(-0.3f, 0.3f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, centerFront, "S_Pur_2", new Vector2(0.3f, 0.3f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, centerFront, "S_Pur_3", new Vector2(-0.3f, -0.3f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, centerFront, "S_Pur_4", new Vector2(0.3f, -0.3f), 0.10f, ColorType.Purple, anim);

            // Arka Kanatlar (4 Sarı, 4 Mor)
            PlateObject backBlade1 = CreatePlate(root.transform, "Blade_Back1_Yellow", new Vector3(0f, 0.95f, -0.9f), Quaternion.Euler(-25, 180, 0), new Vector3(1.2f, 0.6f, 0.10f), yMat);
            CreateScrew(screwPrefab, backBlade1, "S_Yel_5", new Vector2(-0.35f, 0f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, backBlade1, "S_Yel_6", new Vector2(0.35f, 0f), 0.10f, ColorType.Yellow, anim);

            PlateObject backBlade2 = CreatePlate(root.transform, "Blade_Back2_Yellow", new Vector3(0f, -0.95f, -0.9f), Quaternion.Euler(25, 180, 0), new Vector3(1.2f, 0.6f, 0.10f), yMat);
            CreateScrew(screwPrefab, backBlade2, "S_Yel_7", new Vector2(-0.35f, 0f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, backBlade2, "S_Yel_8", new Vector2(0.35f, 0f), 0.10f, ColorType.Yellow, anim);

            PlateObject backCenter = CreatePlate(root.transform, "Plate_BackCenter_Purple", new Vector3(0f, 0f, -1.05f), Quaternion.Euler(0, 180, 0), new Vector3(1.1f, 1.1f, 0.10f), pMat);
            CreateScrew(screwPrefab, backCenter, "S_Pur_5", new Vector2(-0.3f, 0.3f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, backCenter, "S_Pur_6", new Vector2(0.3f, 0.3f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, backCenter, "S_Pur_7", new Vector2(-0.3f, -0.3f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, backCenter, "S_Pur_8", new Vector2(0.3f, -0.3f), 0.10f, ColorType.Purple, anim);

            // Yan Kilit Bantları (4 Mavi, 4 Yeşil)
            PlateObject sideBandL = CreatePlate(root.transform, "Plate_SideL_Blue", new Vector3(-1.05f, 0f, 0f), Quaternion.Euler(0, -90, 0), new Vector3(1.3f, 0.6f, 0.10f), bMat);
            CreateScrew(screwPrefab, sideBandL, "S_Blu_5", new Vector2(-0.4f, 0f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, sideBandL, "S_Blu_6", new Vector2(0f, 0f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, sideBandL, "S_Blu_7", new Vector2(0.4f, 0f), 0.10f, ColorType.Blue, anim);

            PlateObject sideBandR = CreatePlate(root.transform, "Plate_SideR_Blue", new Vector3(1.05f, 0f, 0f), Quaternion.Euler(0, 90, 0), new Vector3(1.3f, 0.6f, 0.10f), bMat);
            CreateScrew(screwPrefab, sideBandR, "S_Blu_8", new Vector2(0f, 0f), 0.10f, ColorType.Blue, anim);

            PlateObject topFlange = CreatePlate(root.transform, "Plate_Top_Green", new Vector3(0f, 1.05f, 0f), Quaternion.Euler(90, 0, 0), new Vector3(1.1f, 1.1f, 0.10f), gMat);
            CreateScrew(screwPrefab, topFlange, "S_Grn_1", new Vector2(-0.3f, 0.3f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, topFlange, "S_Grn_2", new Vector2(0.3f, 0.3f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, topFlange, "S_Grn_3", new Vector2(-0.3f, -0.3f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, topFlange, "S_Grn_4", new Vector2(0.3f, -0.3f), 0.10f, ColorType.Green, anim);
        }

        // ==========================================
        // LEVEL 7: L-Block Dual Cube (32 Vida: 8 Mavi, 8 Sarı, 8 Mor, 8 Yeşil)
        // ==========================================
        private static void BuildLevel07_LBlock(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, Material coreMat, Material bMat, Material yMat, Material pMat, Material gMat)
        {
            // İki küp birleşimi L-Blok
            GameObject cube1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube1.name = "L_Base";
            cube1.transform.SetParent(root.transform, false);
            cube1.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            cube1.transform.localScale = new Vector3(2.0f, 1.0f, 1.4f);
            cube1.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

            GameObject cube2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube2.name = "L_Upright";
            cube2.transform.SetParent(root.transform, false);
            cube2.transform.localPosition = new Vector3(-0.5f, 0.5f, 0f);
            cube2.transform.localScale = new Vector3(1.0f, 1.0f, 1.4f);
            cube2.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

            // Taban Ön Plaka (Mavi - 4 Vida)
            PlateObject pBaseFront = CreatePlate(root.transform, "Plate_BaseFront_Blue", new Vector3(0f, -0.5f, 0.78f), Quaternion.identity, new Vector3(1.8f, 0.8f, 0.10f), bMat);
            CreateScrew(screwPrefab, pBaseFront, "S_Blu_1", new Vector2(-0.6f, 0f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pBaseFront, "S_Blu_2", new Vector2(-0.2f, 0f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pBaseFront, "S_Blu_3", new Vector2(0.2f, 0f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pBaseFront, "S_Blu_4", new Vector2(0.6f, 0f), 0.10f, ColorType.Blue, anim);

            // Dikey Ön Plaka (Mavi - 4 Vida)
            PlateObject pUpFront = CreatePlate(root.transform, "Plate_UpFront_Blue", new Vector3(-0.5f, 0.5f, 0.78f), Quaternion.identity, new Vector3(0.8f, 0.8f, 0.10f), bMat);
            CreateScrew(screwPrefab, pUpFront, "S_Blu_5", new Vector2(-0.25f, 0.25f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pUpFront, "S_Blu_6", new Vector2(0.25f, 0.25f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pUpFront, "S_Blu_7", new Vector2(-0.25f, -0.25f), 0.10f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, pUpFront, "S_Blu_8", new Vector2(0.25f, -0.25f), 0.10f, ColorType.Blue, anim);

            // Arka Plakalar (Sarı - 8 Vida)
            PlateObject pBack1 = CreatePlate(root.transform, "Plate_Back1_Yellow", new Vector3(0f, -0.5f, -0.78f), Quaternion.Euler(0, 180, 0), new Vector3(1.8f, 0.8f, 0.10f), yMat);
            CreateScrew(screwPrefab, pBack1, "S_Yel_1", new Vector2(-0.6f, 0f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pBack1, "S_Yel_2", new Vector2(-0.2f, 0f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pBack1, "S_Yel_3", new Vector2(0.2f, 0f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pBack1, "S_Yel_4", new Vector2(0.6f, 0f), 0.10f, ColorType.Yellow, anim);

            PlateObject pBack2 = CreatePlate(root.transform, "Plate_Back2_Yellow", new Vector3(-0.5f, 0.5f, -0.78f), Quaternion.Euler(0, 180, 0), new Vector3(0.8f, 0.8f, 0.10f), yMat);
            CreateScrew(screwPrefab, pBack2, "S_Yel_5", new Vector2(-0.25f, 0.25f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pBack2, "S_Yel_6", new Vector2(0.25f, 0.25f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pBack2, "S_Yel_7", new Vector2(-0.25f, -0.25f), 0.10f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, pBack2, "S_Yel_8", new Vector2(0.25f, -0.25f), 0.10f, ColorType.Yellow, anim);

            // Üst ve Köşe Dirsek Plakaları (Mor - 8 Vida)
            PlateObject pCornerStep = CreatePlate(root.transform, "Plate_CornerStep_Purple", new Vector3(0.5f, 0.08f, 0f), Quaternion.Euler(90, 0, 0), new Vector3(0.8f, 1.2f, 0.10f), pMat);
            CreateScrew(screwPrefab, pCornerStep, "S_Pur_1", new Vector2(-0.25f, 0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pCornerStep, "S_Pur_2", new Vector2(0.25f, 0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pCornerStep, "S_Pur_3", new Vector2(-0.25f, -0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pCornerStep, "S_Pur_4", new Vector2(0.25f, -0.35f), 0.10f, ColorType.Purple, anim);

            PlateObject pTopPeak = CreatePlate(root.transform, "Plate_TopPeak_Purple", new Vector3(-0.5f, 1.08f, 0f), Quaternion.Euler(90, 0, 0), new Vector3(0.8f, 1.2f, 0.10f), pMat);
            CreateScrew(screwPrefab, pTopPeak, "S_Pur_5", new Vector2(-0.25f, 0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pTopPeak, "S_Pur_6", new Vector2(0.25f, 0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pTopPeak, "S_Pur_7", new Vector2(-0.25f, -0.35f), 0.10f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, pTopPeak, "S_Pur_8", new Vector2(0.25f, -0.35f), 0.10f, ColorType.Purple, anim);

            // Yan ve Taban Plakaları (Yeşil - 8 Vida)
            PlateObject pRightSide = CreatePlate(root.transform, "Plate_RightSide_Green", new Vector3(1.08f, -0.5f, 0f), Quaternion.Euler(0, 90, 0), new Vector3(1.2f, 0.8f, 0.10f), gMat);
            CreateScrew(screwPrefab, pRightSide, "S_Grn_1", new Vector2(-0.35f, 0f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pRightSide, "S_Grn_2", new Vector2(0.35f, 0f), 0.10f, ColorType.Green, anim);

            PlateObject pLeftSide = CreatePlate(root.transform, "Plate_LeftSide_Green", new Vector3(-1.08f, 0f, 0f), Quaternion.Euler(0, -90, 0), new Vector3(1.2f, 1.6f, 0.10f), gMat);
            CreateScrew(screwPrefab, pLeftSide, "S_Grn_3", new Vector2(-0.35f, 0.45f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pLeftSide, "S_Grn_4", new Vector2(0.35f, 0.45f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pLeftSide, "S_Grn_5", new Vector2(-0.35f, -0.45f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pLeftSide, "S_Grn_6", new Vector2(0.35f, -0.45f), 0.10f, ColorType.Green, anim);

            PlateObject pBottom = CreatePlate(root.transform, "Plate_Bottom_Green", new Vector3(0f, -1.08f, 0f), Quaternion.Euler(-90, 0, 0), new Vector3(1.8f, 1.2f, 0.10f), gMat);
            CreateScrew(screwPrefab, pBottom, "S_Grn_7", new Vector2(-0.5f, 0f), 0.10f, ColorType.Green, anim);
            CreateScrew(screwPrefab, pBottom, "S_Grn_8", new Vector2(0.5f, 0f), 0.10f, ColorType.Green, anim);
        }

        // ==========================================
        // LEVEL 8: Master Armored Vault (36 Vida: 12 Mavi, 8 Sarı, 8 Mor, 8 Yeşil)
        // ==========================================
        private static void BuildLevel08_Vault(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, Material coreMat, Material bMat, Material yMat, Material pMat, Material gMat, Material mMat)
        {
            GameObject vaultGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vaultGo.name = "VaultCore";
            vaultGo.transform.SetParent(root.transform, false);
            vaultGo.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);
            vaultGo.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

            // 1. Katman: Ana Çelik Kapı (Ön - Mavi, 8 Vida)
            PlateObject mainDoor = CreatePlate(root.transform, "Vault_MainDoor_Blue", new Vector3(0f, 0f, 1.08f), Quaternion.identity, new Vector3(1.6f, 1.6f, 0.12f), bMat);
            CreateScrew(screwPrefab, mainDoor, "S_Blu_1", new Vector2(-0.55f, 0.55f), 0.12f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, mainDoor, "S_Blu_2", new Vector2(0.55f, 0.55f), 0.12f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, mainDoor, "S_Blu_3", new Vector2(-0.55f, -0.55f), 0.12f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, mainDoor, "S_Blu_4", new Vector2(0.55f, -0.55f), 0.12f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, mainDoor, "S_Blu_5", new Vector2(0f, 0.55f), 0.12f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, mainDoor, "S_Blu_6", new Vector2(0f, -0.55f), 0.12f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, mainDoor, "S_Blu_7", new Vector2(-0.55f, 0f), 0.12f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, mainDoor, "S_Blu_8", new Vector2(0.55f, 0f), 0.12f, ColorType.Blue, anim);

            // 2. Katman: Kapı Üstü Çapraz Güvenlik Mandalları (Mavi - 4 Vida, Sarı - 4 Vida)
            PlateObject latchH = CreatePlate(root.transform, "Vault_LatchH_Blue", new Vector3(0f, 0f, 1.22f), Quaternion.identity, new Vector3(1.9f, 0.40f, 0.12f), bMat);
            CreateScrew(screwPrefab, latchH, "S_Blu_9", new Vector2(-0.75f, 0f), 0.12f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, latchH, "S_Blu_10", new Vector2(-0.30f, 0f), 0.12f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, latchH, "S_Blu_11", new Vector2(0.30f, 0f), 0.12f, ColorType.Blue, anim);
            CreateScrew(screwPrefab, latchH, "S_Blu_12", new Vector2(0.75f, 0f), 0.12f, ColorType.Blue, anim);

            PlateObject latchV = CreatePlate(root.transform, "Vault_LatchV_Yellow", new Vector3(0f, 0f, 1.34f), Quaternion.Euler(0, 0, 90), new Vector3(1.9f, 0.40f, 0.12f), yMat);
            CreateScrew(screwPrefab, latchV, "S_Yel_1", new Vector2(-0.75f, 0f), 0.12f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, latchV, "S_Yel_2", new Vector2(-0.30f, 0f), 0.12f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, latchV, "S_Yel_3", new Vector2(0.30f, 0f), 0.12f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, latchV, "S_Yel_4", new Vector2(0.75f, 0f), 0.12f, ColorType.Yellow, anim);
            LinkBlocker(latchV, latchH.GetComponentInChildren<ScrewObject>());

            // Arka Zırh Paneli (Sarı - 4 Vida, Yeşil - 4 Vida)
            PlateObject backArmor = CreatePlate(root.transform, "Vault_BackArmor_Yellow", new Vector3(0f, 0f, -1.08f), Quaternion.Euler(0, 180, 0), new Vector3(1.6f, 1.6f, 0.12f), yMat);
            CreateScrew(screwPrefab, backArmor, "S_Yel_5", new Vector2(-0.55f, 0.55f), 0.12f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, backArmor, "S_Yel_6", new Vector2(0.55f, 0.55f), 0.12f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, backArmor, "S_Yel_7", new Vector2(-0.55f, -0.55f), 0.12f, ColorType.Yellow, anim);
            CreateScrew(screwPrefab, backArmor, "S_Yel_8", new Vector2(0.55f, -0.55f), 0.12f, ColorType.Yellow, anim);

            PlateObject backBrace = CreatePlate(root.transform, "Vault_BackBrace_Green", new Vector3(0f, 0f, -1.22f), Quaternion.Euler(0, 180, 45), new Vector3(1.8f, 0.40f, 0.12f), gMat);
            CreateScrew(screwPrefab, backBrace, "S_Grn_1", new Vector2(-0.65f, 0f), 0.12f, ColorType.Green, anim);
            CreateScrew(screwPrefab, backBrace, "S_Grn_2", new Vector2(-0.25f, 0f), 0.12f, ColorType.Green, anim);
            CreateScrew(screwPrefab, backBrace, "S_Grn_3", new Vector2(0.25f, 0f), 0.12f, ColorType.Green, anim);
            CreateScrew(screwPrefab, backBrace, "S_Grn_4", new Vector2(0.65f, 0f), 0.12f, ColorType.Green, anim);

            // Sağ ve Sol Yan Zırhlar (Mor - 8 Vida)
            PlateObject rArmor = CreatePlate(root.transform, "Vault_Right_Purple", new Vector3(1.08f, 0f, 0f), Quaternion.Euler(0, 90, 0), new Vector3(1.5f, 1.5f, 0.12f), pMat);
            CreateScrew(screwPrefab, rArmor, "S_Pur_1", new Vector2(-0.45f, 0.45f), 0.12f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, rArmor, "S_Pur_2", new Vector2(0.45f, 0.45f), 0.12f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, rArmor, "S_Pur_3", new Vector2(-0.45f, -0.45f), 0.12f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, rArmor, "S_Pur_4", new Vector2(0.45f, -0.45f), 0.12f, ColorType.Purple, anim);

            PlateObject lArmor = CreatePlate(root.transform, "Vault_Left_Purple", new Vector3(-1.08f, 0f, 0f), Quaternion.Euler(0, -90, 0), new Vector3(1.5f, 1.5f, 0.12f), pMat);
            CreateScrew(screwPrefab, lArmor, "S_Pur_5", new Vector2(-0.45f, 0.45f), 0.12f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, lArmor, "S_Pur_6", new Vector2(0.45f, 0.45f), 0.12f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, lArmor, "S_Pur_7", new Vector2(-0.45f, -0.45f), 0.12f, ColorType.Purple, anim);
            CreateScrew(screwPrefab, lArmor, "S_Pur_8", new Vector2(0.45f, -0.45f), 0.12f, ColorType.Purple, anim);

            // Üst ve Alt Kasa Menteşeleri (Yeşil - 4 Vida)
            PlateObject tArmor = CreatePlate(root.transform, "Vault_Top_Green", new Vector3(0f, 1.08f, 0f), Quaternion.Euler(90, 0, 0), new Vector3(1.5f, 1.5f, 0.12f), gMat);
            CreateScrew(screwPrefab, tArmor, "S_Grn_5", new Vector2(-0.45f, 0f), 0.12f, ColorType.Green, anim);
            CreateScrew(screwPrefab, tArmor, "S_Grn_6", new Vector2(0.45f, 0f), 0.12f, ColorType.Green, anim);

            PlateObject bArmor = CreatePlate(root.transform, "Vault_Bottom_Green", new Vector3(0f, -1.08f, 0f), Quaternion.Euler(-90, 0, 0), new Vector3(1.5f, 1.5f, 0.12f), gMat);
            CreateScrew(screwPrefab, bArmor, "S_Grn_7", new Vector2(-0.45f, 0f), 0.12f, ColorType.Green, anim);
            CreateScrew(screwPrefab, bArmor, "S_Grn_8", new Vector2(0.45f, 0f), 0.12f, ColorType.Green, anim);
        }

        // ==========================================
        // YARDIMCI FABRİKA METOTLARI
        // ==========================================
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
