using System;
using System.Collections.Generic;
using ScrewSolver.Config;
using ScrewSolver.Core;
using ScrewSolver.Editor.Solver;
using ScrewSolver.Gameplay.Boxes;
using ScrewSolver.Gameplay.Level;
using ScrewSolver.Gameplay.Plates;
using ScrewSolver.Gameplay.Screws;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScrewSolver.Editor.Generator
{
    public enum LevelShapeKind
    {
        OnboardingCube = 0,
        LayeredCube = 1,
        ColorCylinder = 2,
        IndustrialCross = 3,
        HexagonTower = 4,
        PyramidZiggurat = 5,
        TurboTurbine = 6,
        LBlockDual = 7,
        ArmoredVault = 8,
        MasterCoreCube = 9
    }

    public readonly struct LevelBlueprintSpec
    {
        public readonly int LevelNumber;
        public readonly string Title;
        public readonly LevelShapeKind Shape;
        public readonly LevelDifficulty Difficulty;
        public readonly float TimeLimitSeconds;
        public readonly ColorType[] Palette;
        public readonly int Seed;

        public LevelBlueprintSpec(
            int levelNumber,
            string title,
            LevelShapeKind shape,
            LevelDifficulty difficulty,
            float timeLimitSeconds,
            ColorType[] palette,
            int seed)
        {
            LevelNumber = levelNumber;
            Title = title;
            Shape = shape;
            Difficulty = difficulty;
            TimeLimitSeconds = timeLimitSeconds;
            Palette = palette;
            Seed = seed;
        }
    }

    /// <summary>
    /// 10 farklı 3D mekanik bulmaca seviyesini (kolaydan zora: 12 vidadan 40 vidaya, 3 renkten 6 renge)
    /// temiz geometri, sıfır Euler yön hatası ve HeadlessLevelSolver garantisiyle üreten mimar.
    /// </summary>
    public static class ParametricLevelArchitect
    {
        public const string LevelsFolder = "Assets/Prefabs/Levels";
        private const string ScrewPrefabPath = "Assets/Prefabs/Screw.prefab";
        private const string AnimConfigPath = "Assets/ScrewAnimationConfig.asset";
        private const string MaterialsFolder = "Assets/Materials";
        private const string MeshesFolder = "Assets/Meshes";

        private static readonly LevelBlueprintSpec[] FirstTenCurriculum = new[]
        {
            new LevelBlueprintSpec(1, "Başlangıç Küpü", LevelShapeKind.OnboardingCube, LevelDifficulty.Easy, 75f,
                new[] { ColorType.Blue, ColorType.Yellow, ColorType.Green }, 101),

            new LevelBlueprintSpec(2, "İlk Kilitli Kapak", LevelShapeKind.LayeredCube, LevelDifficulty.Easy, 85f,
                new[] { ColorType.Blue, ColorType.Yellow, ColorType.Purple }, 202),

            new LevelBlueprintSpec(3, "Renkli Silindir", LevelShapeKind.ColorCylinder, LevelDifficulty.Easy, 95f,
                new[] { ColorType.Blue, ColorType.Yellow, ColorType.Green }, 303),

            new LevelBlueprintSpec(4, "Dört Renk Haçı", LevelShapeKind.IndustrialCross, LevelDifficulty.Medium, 105f,
                new[] { ColorType.Blue, ColorType.Yellow, ColorType.Purple, ColorType.Green }, 404),

            new LevelBlueprintSpec(5, "Altıgen Kule", LevelShapeKind.HexagonTower, LevelDifficulty.Medium, 115f,
                new[] { ColorType.Blue, ColorType.Yellow, ColorType.Purple, ColorType.Green }, 505),

            new LevelBlueprintSpec(6, "Piramit Dikilitaş", LevelShapeKind.PyramidZiggurat, LevelDifficulty.Medium, 120f,
                new[] { ColorType.Red, ColorType.Blue, ColorType.Yellow, ColorType.Green }, 606),

            new LevelBlueprintSpec(7, "Turbo Türbin", LevelShapeKind.TurboTurbine, LevelDifficulty.Hard, 135f,
                new[] { ColorType.Blue, ColorType.Yellow, ColorType.Purple, ColorType.Orange }, 707),

            new LevelBlueprintSpec(8, "İkili L-Blok", LevelShapeKind.LBlockDual, LevelDifficulty.Hard, 150f,
                new[] { ColorType.Blue, ColorType.Yellow, ColorType.Purple, ColorType.Green, ColorType.Red }, 808),

            new LevelBlueprintSpec(9, "Zırhlı Kasa", LevelShapeKind.ArmoredVault, LevelDifficulty.Hard, 165f,
                new[] { ColorType.Blue, ColorType.Yellow, ColorType.Purple, ColorType.Green, ColorType.Orange }, 909),

            new LevelBlueprintSpec(10, "Master Kuantum Küp", LevelShapeKind.MasterCoreCube, LevelDifficulty.Master, 180f,
                new[] { ColorType.Blue, ColorType.Yellow, ColorType.Purple, ColorType.Green, ColorType.Red, ColorType.Orange }, 1010)
        };

        public static IReadOnlyList<LevelBlueprintSpec> GetCurriculum() => FirstTenCurriculum;

        private sealed class PlateStyleSet
        {
            public Material Core;
            public Material AccentDark;
            public Material Blue;
            public Material Yellow;
            public Material Teal;
            public Material Coral;
            public Material Purple;
            public Material Mint;
            public Material Steel;
        }

        /// <summary>
        /// İlk 10 seviyeyi üretir, her birini HeadlessLevelSolver ile %100 çözülebilir hale getirip
        /// Prefab olarak kaydeder ve sahnedeki LevelManager + PuzzleRoot'u otomatik günceller.
        /// </summary>
        public static List<string> GenerateAndVerifyFirstTenLevels(bool loadLevelOneIntoScene = true)
        {
            List<string> logs = new();

            EnsureFolder("Assets/Prefabs", "Levels");
            EnsureFolder("Assets", "Materials");
            EnsureFolder("Assets", "Meshes");

            GameObject screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScrewPrefabPath);
            ScrewAnimationConfigSO animConfig = AssetDatabase.LoadAssetAtPath<ScrewAnimationConfigSO>(AnimConfigPath);
            if (screwPrefab == null)
            {
                logs.Add("HATA: Assets/Prefabs/Screw.prefab bulunamadı!");
                return logs;
            }

            PlateStyleSet styles = LoadOrCreateStyles();
            List<GameObject> savedPrefabs = new();
            GameObject tempRoot = new GameObject("__TempLevelBuilderRoot");
            tempRoot.transform.position = new Vector3(500f, 0f, 0f);

            try
            {
                for (int i = 0; i < FirstTenCurriculum.Length; i++)
                {
                    LevelBlueprintSpec spec = FirstTenCurriculum[i];
                    GameObject prefab = BuildSingleLevelPrefab(tempRoot, spec, screwPrefab, animConfig, styles, out string reportLine);
                    if (prefab != null)
                    {
                        savedPrefabs.Add(prefab);
                    }
                    logs.Add(reportLine);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempRoot);
            }

            if (savedPrefabs.Count > 0)
            {
                WireLevelsToScene(savedPrefabs, loadLevelOneIntoScene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return logs;
        }

        /// <summary>
        /// Verilen parametrelerle tek bir yeni seviye prefab'ı üretir ve Solver ile doğrular.
        /// </summary>
        public static GameObject GenerateCustomLevel(
            int levelNumber,
            string title,
            LevelShapeKind shape,
            LevelDifficulty difficulty,
            float timeLimitSeconds,
            ColorType[] palette,
            int seed,
            out string reportLine)
        {
            EnsureFolder("Assets/Prefabs", "Levels");
            EnsureFolder("Assets", "Materials");
            EnsureFolder("Assets", "Meshes");

            GameObject screwPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScrewPrefabPath);
            ScrewAnimationConfigSO animConfig = AssetDatabase.LoadAssetAtPath<ScrewAnimationConfigSO>(AnimConfigPath);
            if (screwPrefab == null)
            {
                reportLine = "HATA: Screw.prefab bulunamadı!";
                return null;
            }

            PlateStyleSet styles = LoadOrCreateStyles();
            GameObject tempRoot = new GameObject("__TempCustomLevelBuilder");
            tempRoot.transform.position = new Vector3(500f, 0f, 0f);
            try
            {
                LevelBlueprintSpec spec = new(levelNumber, title, shape, difficulty, timeLimitSeconds, palette, seed);
                GameObject prefab = BuildSingleLevelPrefab(tempRoot, spec, screwPrefab, animConfig, styles, out reportLine);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempRoot);
            }
        }

        /// <summary>
        /// Mevcut sahnedeki LevelManager referansını koruyarak yalnızca Level 5 (Altıgen Kule) prefab'ını ve mesh asset'ini yeniler.
        /// </summary>
        public static GameObject RebuildLevelFiveHexagon(out string reportLine)
        {
            LevelBlueprintSpec spec = FirstTenCurriculum[4];
            GameObject prefab = GenerateCustomLevel(
                spec.LevelNumber,
                spec.Title,
                spec.Shape,
                spec.Difficulty,
                spec.TimeLimitSeconds,
                spec.Palette,
                spec.Seed,
                out reportLine);

            if (prefab != null)
            {
                LevelManager lm = UnityEngine.Object.FindFirstObjectByType<LevelManager>();
                if (lm != null)
                {
                    SerializedObject lmSo = new SerializedObject(lm);
                    SerializedProperty listProp = lmSo.FindProperty("_levelPrefabs");
                    if (listProp != null && listProp.arraySize >= 5)
                    {
                        listProp.GetArrayElementAtIndex(4).objectReferenceValue = prefab;
                        lmSo.ApplyModifiedProperties();
                        EditorUtility.SetDirty(lm);
                    }
                }

                GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
                if (puzzleRoot != null && puzzleRoot.transform.childCount > 0)
                {
                    Transform existing = puzzleRoot.transform.GetChild(0);
                    if (existing.name.StartsWith("Level_05", StringComparison.OrdinalIgnoreCase))
                    {
                        UnityEngine.Object.DestroyImmediate(existing.gameObject);
                        GameObject activeInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, puzzleRoot.transform);
                        activeInstance.transform.localPosition = Vector3.zero;
                        activeInstance.transform.localRotation = Quaternion.identity;
                        activeInstance.transform.localScale = Vector3.one;
                        EditorSceneManager.MarkSceneDirty(puzzleRoot.scene);
                        EditorSceneManager.SaveScene(puzzleRoot.scene);
                    }
                }
            }

            return prefab;
        }

        private static GameObject BuildSingleLevelPrefab(
            GameObject tempRoot,
            LevelBlueprintSpec spec,
            GameObject screwPrefab,
            ScrewAnimationConfigSO animConfig,
            PlateStyleSet styles,
            out string reportLine)
        {
            string levelName = $"Level_{spec.LevelNumber:00}";
            GameObject levelGo = new GameObject(levelName);
            levelGo.transform.SetParent(tempRoot.transform, false);
            levelGo.transform.localPosition = Vector3.zero;
            levelGo.transform.localRotation = Quaternion.identity;
            levelGo.transform.localScale = Vector3.one;

            BuildGeometry(spec.Shape, levelGo, screwPrefab, animConfig, styles);

            // 1. Her vidanın altına siyah delik yuvası yerleştir
            ScrewObject[] screws = levelGo.GetComponentsInChildren<ScrewObject>(true);
            foreach (ScrewObject s in screws)
            {
                ScrewHoleTool.SetupHoleForScrew(s.gameObject);
            }

            // 2. Fiziksel engelleri (plakalar -> vidalar) otomatik tespit edip bağla
            HeadlessLevelSolver.AutoWirePhysicalDependencies(levelGo, clearExistingBlockers: true);

            // 3. Grafı çıkar ve Solver ile optimal renk + kutu kuyruğunu hesapla
            HeadlessLevelSolver.LevelGraph graph = HeadlessLevelSolver.ExtractGraph(levelGo);
            HeadlessLevelSolver.OptimizationResult opt = HeadlessLevelSolver.OptimizeColorsAndQueue(
                graph,
                spec.Palette,
                spec.Difficulty,
                spec.Seed);

            // 4. Optimize edilmiş renkleri vidalara uygula
            if (opt.Success && opt.AssignedScrewColors != null)
            {
                for (int i = 0; i < graph.Screws.Count; i++)
                {
                    ScrewObject screw = graph.Screws[i].ScrewRef;
                    ColorType assignedColor = opt.AssignedScrewColors[i];

                    SerializedObject sSo = new SerializedObject(screw);
                    sSo.FindProperty("_color").enumValueIndex = (int)assignedColor;
                    sSo.ApplyModifiedProperties();

                    screw.name = $"Screw_{i + 1:00}_{assignedColor}";
                    screw.ApplyColorVisual();
                    EditorUtility.SetDirty(screw);
                }
            }

            // 5. LevelConfig bileşenine doğrulanmış kuyruğu, süreyi ve zorluk raporunu kaydet
            LevelConfig config = levelGo.GetComponent<LevelConfig>() ?? levelGo.AddComponent<LevelConfig>();
            bool verified = opt.Success && opt.Report != null && opt.Report.IsSolvable;
            float winRate = opt.Report != null ? opt.Report.MonteCarloWinRate : 0f;
            int minSteps = opt.Report != null ? opt.Report.SolutionSteps : 0;
            int maxHoles = opt.Report != null ? opt.Report.MaxConcurrentHolesUsed : 0;
            string summary = opt.Report != null ? opt.Report.Summary : "Optimize edilemedi";

            config.Configure(
                spec.LevelNumber,
                spec.Title,
                spec.Difficulty,
                spec.TimeLimitSeconds,
                opt.OptimizedBoxQueue,
                verified,
                winRate,
                minSteps,
                maxHoles,
                summary);
            EditorUtility.SetDirty(config);

            string prefabPath = $"{LevelsFolder}/{levelName}.prefab";
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(levelGo, prefabPath);
            UnityEngine.Object.DestroyImmediate(levelGo);

            string statusIcon = verified ? "✓" : "✗";
            reportLine = $"[{statusIcon}] {levelName} ({spec.Title} | {spec.Difficulty}): " +
                         $"{graph.TotalScrews} Vida / {opt.OptimizedBoxQueue.Count} Kutu / {spec.Palette.Length} Renk — {summary}";

            return savedPrefab;
        }

        private static void WireLevelsToScene(List<GameObject> levelPrefabs, bool loadLevelOneIntoScene)
        {
            LevelManager lm = UnityEngine.Object.FindFirstObjectByType<LevelManager>();
            if (lm != null)
            {
                SerializedObject lmSo = new SerializedObject(lm);
                SerializedProperty listProp = lmSo.FindProperty("_levelPrefabs");
                listProp.arraySize = levelPrefabs.Count;
                for (int i = 0; i < levelPrefabs.Count; i++)
                {
                    listProp.GetArrayElementAtIndex(i).objectReferenceValue = levelPrefabs[i];
                }
                lmSo.FindProperty("_currentLevelIndex").intValue = 0;
                lmSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(lm);
            }

            GameObject puzzleRoot = GameObject.Find("PuzzleRoot");
            if (loadLevelOneIntoScene && puzzleRoot != null && levelPrefabs.Count > 0)
            {
                for (int i = puzzleRoot.transform.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(puzzleRoot.transform.GetChild(i).gameObject);
                }

                GameObject activeInstance = (GameObject)PrefabUtility.InstantiatePrefab(levelPrefabs[0], puzzleRoot.transform);
                activeInstance.transform.localPosition = Vector3.zero;
                activeInstance.transform.localRotation = Quaternion.identity;
                activeInstance.transform.localScale = Vector3.one;

                LevelConfig cfg = activeInstance.GetComponent<LevelConfig>();
                BoxQueueManager bqm = UnityEngine.Object.FindFirstObjectByType<BoxQueueManager>();
                if (bqm != null && cfg != null && cfg.OptimizedBoxQueue != null && cfg.OptimizedBoxQueue.Count > 0)
                {
                    SerializedObject bqmSo = new SerializedObject(bqm);
                    SerializedProperty qProp = bqmSo.FindProperty("_boxColorQueue");
                    qProp.arraySize = cfg.OptimizedBoxQueue.Count;
                    for (int i = 0; i < cfg.OptimizedBoxQueue.Count; i++)
                    {
                        qProp.GetArrayElementAtIndex(i).enumValueIndex = (int)cfg.OptimizedBoxQueue[i];
                    }
                    bqmSo.ApplyModifiedProperties();
                    EditorUtility.SetDirty(bqm);
                }

                EditorSceneManager.MarkSceneDirty(puzzleRoot.scene);
                EditorSceneManager.SaveScene(puzzleRoot.scene);
            }
        }

        #region 10 Distinct 3D Mechanical Blueprints

        private static void BuildGeometry(LevelShapeKind shape, GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            switch (shape)
            {
                case LevelShapeKind.OnboardingCube:
                    BuildLevel01_OnboardingCube(root, screwPrefab, anim, s);
                    break;
                case LevelShapeKind.LayeredCube:
                    BuildLevel02_LayeredCube(root, screwPrefab, anim, s);
                    break;
                case LevelShapeKind.ColorCylinder:
                    BuildLevel03_ColorCylinder(root, screwPrefab, anim, s);
                    break;
                case LevelShapeKind.IndustrialCross:
                    BuildLevel04_IndustrialCross(root, screwPrefab, anim, s);
                    break;
                case LevelShapeKind.HexagonTower:
                    BuildLevel05_HexagonTower(root, screwPrefab, anim, s);
                    break;
                case LevelShapeKind.PyramidZiggurat:
                    BuildLevel06_PyramidZiggurat(root, screwPrefab, anim, s);
                    break;
                case LevelShapeKind.TurboTurbine:
                    BuildLevel07_TurboTurbine(root, screwPrefab, anim, s);
                    break;
                case LevelShapeKind.LBlockDual:
                    BuildLevel08_LBlockDual(root, screwPrefab, anim, s);
                    break;
                case LevelShapeKind.ArmoredVault:
                    BuildLevel09_ArmoredVault(root, screwPrefab, anim, s);
                    break;
                case LevelShapeKind.MasterCoreCube:
                    BuildLevel10_MasterCoreCube(root, screwPrefab, anim, s);
                    break;
            }
        }

        // LEVEL 01: 12 Vida (3 Kutu, 3 Renk, 0 Kilit - İlk Seviye)
        private static void BuildLevel01_OnboardingCube(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            CreateCoreCube(root.transform, "Chassis_Core", Vector3.zero, new Vector3(1.36f, 1.44f, 1.36f), s.Core);
            const float t = 0.06f;

            // Ön Üst + Ön Alt (3 + 3 = 6 vida)
            PlateObject fTop = CreatePlateWithNormal(root.transform, "Plate_Front_Top", new Vector3(0f, 0.34f, 0.715f), Vector3.forward, Vector3.up, new Vector3(1.10f, 0.50f, t), s.Teal);
            CreateScrews(screwPrefab, fTop, anim, t, (-0.36f, 0f), (0f, 0f), (0.36f, 0f));

            PlateObject fBot = CreatePlateWithNormal(root.transform, "Plate_Front_Bottom", new Vector3(0f, -0.34f, 0.715f), Vector3.forward, Vector3.up, new Vector3(1.10f, 0.50f, t), s.Blue);
            CreateScrews(screwPrefab, fBot, anim, t, (-0.36f, 0f), (0f, 0f), (0.36f, 0f));

            // Sağ, Sol, Arka (2 + 2 + 2 = 6 vida) -> toplam 12 vida
            PlateObject right = CreatePlateWithNormal(root.transform, "Plate_Right", new Vector3(0.715f, 0f, 0f), Vector3.right, Vector3.up, new Vector3(1.00f, 1.06f, t), s.Yellow);
            CreateScrews(screwPrefab, right, anim, t, (0f, 0.30f), (0f, -0.30f));

            PlateObject left = CreatePlateWithNormal(root.transform, "Plate_Left", new Vector3(-0.715f, 0f, 0f), Vector3.left, Vector3.up, new Vector3(1.00f, 1.06f, t), s.Coral);
            CreateScrews(screwPrefab, left, anim, t, (0f, 0.30f), (0f, -0.30f));

            PlateObject back = CreatePlateWithNormal(root.transform, "Plate_Back", new Vector3(0f, 0f, -0.715f), Vector3.back, Vector3.up, new Vector3(1.04f, 1.04f, t), s.Mint);
            CreateScrews(screwPrefab, back, anim, t, (-0.28f, 0f), (0.28f, 0f));
        }

        // LEVEL 02: 16 Vida (4 Kutu, 3 Renk, İlk Kilitli Kapak)
        private static void BuildLevel02_LayeredCube(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            CreateCoreCube(root.transform, "Chassis_Core", Vector3.zero, new Vector3(1.42f, 1.48f, 1.42f), s.Core);
            const float t = 0.06f;

            // Ön Katman 1 (4 vida, alttaki 2'si LockBar altında)
            PlateObject fBase = CreatePlateWithNormal(root.transform, "Plate_Front_Base", new Vector3(0f, 0f, 0.745f), Vector3.forward, Vector3.up, new Vector3(1.18f, 1.18f, t), s.Teal);
            CreateScrews(screwPrefab, fBase, anim, t, (-0.36f, 0.38f), (0.36f, 0.38f), (-0.22f, -0.18f), (0.22f, -0.18f));

            // Ön Katman 2 (LockBar - alttaki (-0.22,-0.18) ve (+0.22,-0.18) vidalarını örter)
            PlateObject fBar = CreatePlateWithNormal(root.transform, "Plate_Front_LockBar", new Vector3(0f, -0.18f, 0.81f), Vector3.forward, Vector3.up, new Vector3(1.24f, 0.34f, t), s.Coral);
            CreateScrews(screwPrefab, fBar, anim, t, (-0.46f, 0f), (0.46f, 0f));

            // Sağ (3), Sol (3), Arka (2), Üst (2) -> toplam 16 vida
            PlateObject right = CreatePlateWithNormal(root.transform, "Plate_Right", new Vector3(0.745f, 0f, 0f), Vector3.right, Vector3.up, new Vector3(1.08f, 1.12f, t), s.Yellow);
            CreateScrews(screwPrefab, right, anim, t, (0f, 0.36f), (-0.28f, -0.28f), (0.28f, -0.28f));

            PlateObject left = CreatePlateWithNormal(root.transform, "Plate_Left", new Vector3(-0.745f, 0f, 0f), Vector3.left, Vector3.up, new Vector3(1.08f, 1.12f, t), s.Purple);
            CreateScrews(screwPrefab, left, anim, t, (0f, 0.36f), (-0.28f, -0.28f), (0.28f, -0.28f));

            PlateObject back = CreatePlateWithNormal(root.transform, "Plate_Back", new Vector3(0f, 0f, -0.745f), Vector3.back, Vector3.up, new Vector3(1.08f, 1.08f, t), s.Blue);
            CreateScrews(screwPrefab, back, anim, t, (-0.30f, 0.25f), (0.30f, -0.25f));

            PlateObject top = CreatePlateWithNormal(root.transform, "Plate_Top", new Vector3(0f, 0.775f, 0f), Vector3.up, Vector3.back, new Vector3(1.02f, 1.02f, t), s.Mint);
            CreateScrews(screwPrefab, top, anim, t, (-0.28f, 0f), (0.28f, 0f));
        }

        // LEVEL 03: 20 Vida (5 Kutu, 3 Renk, Renkli Silindir)
        private static void BuildLevel03_ColorCylinder(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            core.name = "Cylinder_Core";
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = new Vector3(1.46f, 0.78f, 1.46f);
            core.GetComponent<MeshRenderer>().sharedMaterial = s.Core;

            const float t = 0.06f;
            const float radius = 0.765f;
            Material[] ringMats = { s.Blue, s.Yellow, s.Teal, s.Coral };

            for (int i = 0; i < 4; i++)
            {
                float rad = i * 90f * Mathf.Deg2Rad;
                Vector3 normal = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                PlateObject panel = CreatePlateWithNormal(root.transform, $"Plate_Cyl_Side_{i}", normal * radius, normal, Vector3.up, new Vector3(0.84f, 1.18f, t), ringMats[i]);
                CreateScrews(screwPrefab, panel, anim, t, (0f, 0.38f), (0f, 0f), (0f, -0.38f));

                // Ön (0°) ve Arka (180°) orta vidasını kilitleyen şerit bantlar
                if (i == 0 || i == 2)
                {
                    PlateObject strap = CreatePlateWithNormal(root.transform, $"Plate_Cyl_LockStrap_{i}", normal * (radius + 0.065f), normal, Vector3.up, new Vector3(0.96f, 0.34f, t), s.Steel);
                    CreateScrews(screwPrefab, strap, anim, t, (-0.32f, 0f), (0.32f, 0f));
                }
            }

            PlateObject topCap = CreatePlateWithNormal(root.transform, "Plate_Cyl_Top", new Vector3(0f, 0.815f, 0f), Vector3.up, Vector3.back, new Vector3(0.96f, 0.96f, t), s.Mint);
            CreateScrews(screwPrefab, topCap, anim, t, (-0.28f, 0f), (0.28f, 0f));

            PlateObject botCap = CreatePlateWithNormal(root.transform, "Plate_Cyl_Bottom", new Vector3(0f, -0.815f, 0f), Vector3.down, Vector3.forward, new Vector3(0.96f, 0.96f, t), s.Purple);
            CreateScrews(screwPrefab, botCap, anim, t, (-0.28f, 0f), (0.28f, 0f));
        }

        // LEVEL 04: 20 Vida (5 Kutu, 4 Renk, Artı/Haç Gövde + Merkez Baklava Kilidi)
        private static void BuildLevel04_IndustrialCross(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            CreateCoreCube(root.transform, "Cross_Bar_H", Vector3.zero, new Vector3(2.14f, 0.70f, 0.70f), s.Core);
            CreateCoreCube(root.transform, "Cross_Bar_V", Vector3.zero, new Vector3(0.70f, 2.14f, 0.70f), s.Core);

            const float t = 0.06f;

            // 4 Ön Kol (Her birinde 1 açık dış vida, 1 merkez baklavanın altında kalan iç vida -> 8 vida)
            PlateObject armL = CreatePlateWithNormal(root.transform, "Plate_Cross_Left", new Vector3(-0.74f, 0f, 0.385f), Vector3.forward, Vector3.up, new Vector3(0.62f, 0.56f, t), s.Blue);
            CreateScrews(screwPrefab, armL, anim, t, (-0.16f, 0f), (0.22f, 0f));

            PlateObject armR = CreatePlateWithNormal(root.transform, "Plate_Cross_Right", new Vector3(0.74f, 0f, 0.385f), Vector3.forward, Vector3.up, new Vector3(0.62f, 0.56f, t), s.Yellow);
            CreateScrews(screwPrefab, armR, anim, t, (0.16f, 0f), (-0.22f, 0f));

            PlateObject armT = CreatePlateWithNormal(root.transform, "Plate_Cross_Top", new Vector3(0f, 0.74f, 0.385f), Vector3.forward, Vector3.up, new Vector3(0.56f, 0.62f, t), s.Teal);
            CreateScrews(screwPrefab, armT, anim, t, (0f, 0.16f), (0f, -0.22f));

            PlateObject armB = CreatePlateWithNormal(root.transform, "Plate_Cross_Bottom", new Vector3(0f, -0.74f, 0.385f), Vector3.forward, Vector3.up, new Vector3(0.56f, 0.62f, t), s.Mint);
            CreateScrews(screwPrefab, armB, anim, t, (0f, -0.16f), (0f, 0.22f));

            // Ön Merkez Baklava Kilit Plakası (4 kolun iç vidalarını kilitler -> 4 vida)
            PlateObject centerDiamond = CreatePlate(root.transform, "Plate_Cross_CenterDiamond", new Vector3(0f, 0f, 0.45f), Quaternion.Euler(0f, 0f, 45f), new Vector3(0.86f, 0.86f, t), s.Coral);
            CreateScrews(screwPrefab, centerDiamond, anim, t, (-0.22f, 0.22f), (0.22f, 0.22f), (-0.22f, -0.22f), (0.22f, -0.22f));

            // Arka 2 Katmanlı Haç Kirişleri (4 + 4 = 8 vida -> toplam 20 vida)
            PlateObject backH = CreatePlateWithNormal(root.transform, "Plate_Back_CrossH", new Vector3(0f, 0f, -0.385f), Vector3.back, Vector3.up, new Vector3(1.88f, 0.52f, t), s.Purple);
            CreateScrews(screwPrefab, backH, anim, t, (-0.74f, 0f), (-0.16f, 0f), (0.16f, 0f), (0.74f, 0f));

            PlateObject backV = CreatePlateWithNormal(root.transform, "Plate_Back_CrossV", new Vector3(0f, 0f, -0.45f), Vector3.back, Vector3.up, new Vector3(0.56f, 1.88f, t), s.Steel);
            CreateScrews(screwPrefab, backV, anim, t, (0f, 0.74f), (0f, 0.32f), (0f, -0.32f), (0f, -0.74f));
        }

        // LEVEL 05: 24 Vida (6 Kutu, 4 Renk, Altıgen Kule + Kelepçeler)
        private static void BuildLevel05_HexagonTower(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            Mesh hexMesh = GetOrCreateHexPrismMesh(0.74f, 1.62f);
            GameObject core = new GameObject("HexTower_Core");
            core.transform.SetParent(root.transform, false);
            core.AddComponent<MeshFilter>().sharedMesh = hexMesh;
            core.AddComponent<MeshRenderer>().sharedMaterial = s.Core;
            MeshCollider coreCollider = core.AddComponent<MeshCollider>();
            coreCollider.sharedMesh = hexMesh;
            coreCollider.convex = true;

            const float t = 0.06f;
            float apothem = 0.74f * Mathf.Cos(30f * Mathf.Deg2Rad); // ~0.641f
            float plateDist = apothem + t * 0.5f + 0.006f;
            Material[] mats = { s.Blue, s.Yellow, s.Teal, s.Purple, s.Mint, s.Coral };

            for (int i = 0; i < 6; i++)
            {
                float rad = i * 60f * Mathf.Deg2Rad;
                Vector3 normal = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                PlateObject panel = CreatePlateWithNormal(root.transform, $"Plate_Hex_Face_{i}", normal * plateDist, normal, Vector3.up, new Vector3(0.68f, 1.36f, t), mats[i]);
                CreateScrews(screwPrefab, panel, anim, t, (0f, 0.44f), (0f, 0f), (0f, -0.44f));

                // 3 farklı yüzeyde orta vidayı kilitleyen zırh şeritleri (0°, 120°, 240° -> 3x2=6 vida -> 18+6=24 vida)
                if (i % 2 == 0)
                {
                    PlateObject belt = CreatePlateWithNormal(root.transform, $"Plate_Hex_Clamp_{i}", normal * (plateDist + 0.065f), normal, Vector3.up, new Vector3(0.66f, 0.34f, t), s.Steel);
                    CreateScrews(screwPrefab, belt, anim, t, (-0.21f, 0f), (0.21f, 0f));
                }
            }
        }

        // LEVEL 06: 24 Vida (6 Kutu, 4 Renk, Basamaklı Piramit Dikilitaş)
        private static void BuildLevel06_PyramidZiggurat(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            CreateCoreCube(root.transform, "Pyramid_Tier1_Bot", new Vector3(0f, -0.52f, 0f), new Vector3(1.66f, 0.56f, 1.66f), s.Core);
            CreateCoreCube(root.transform, "Pyramid_Tier2_Mid", new Vector3(0f, 0.04f, 0f), new Vector3(1.26f, 0.56f, 1.26f), s.Core);
            CreateCoreCube(root.transform, "Pyramid_Tier3_Top", new Vector3(0f, 0.58f, 0f), new Vector3(0.84f, 0.52f, 0.84f), s.Core);

            const float t = 0.06f;
            Vector3[] dirs = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            Material[] botMats = { s.Coral, s.Yellow, s.Blue, s.Mint };
            Material[] midMats = { s.Teal, s.Purple, s.Yellow, s.Blue };

            // Alt Kat: 4 plaka x 2 vida = 8 vida
            for (int i = 0; i < 4; i++)
            {
                Vector3 n = dirs[i];
                PlateObject pBot = CreatePlateWithNormal(root.transform, $"Plate_Bot_{i}", n * 0.865f + new Vector3(0f, -0.52f, 0f), n, Vector3.up, new Vector3(1.24f, 0.42f, t), botMats[i]);
                CreateScrews(screwPrefab, pBot, anim, t, (-0.38f, 0f), (0.38f, 0f));
            }

            // Orta Kat: 4 plaka x 3 vida = 12 vida (Ön ve Arka orta vidaları kilit şeritleriyle örtülü)
            for (int i = 0; i < 4; i++)
            {
                Vector3 n = dirs[i];
                PlateObject pMid = CreatePlateWithNormal(root.transform, $"Plate_Mid_{i}", n * 0.665f + new Vector3(0f, 0.04f, 0f), n, Vector3.up, new Vector3(0.98f, 0.42f, t), midMats[i]);
                CreateScrews(screwPrefab, pMid, anim, t, (-0.30f, 0f), (0f, 0f), (0.30f, 0f));

                if (i == 0 || i == 2)
                {
                    // Orta + Üst katı dikey bağlayan piramit kilidi (2 vida x 2 = 4 vida -> toplam 24 vida)
                    PlateObject lockCol = CreatePlateWithNormal(root.transform, $"Plate_MidTop_Lock_{i}", n * 0.73f + new Vector3(0f, 0.28f, 0f), n, Vector3.up, new Vector3(0.32f, 0.82f, t), s.Steel);
                    CreateScrews(screwPrefab, lockCol, anim, t, (0f, 0.24f), (0f, -0.06f));
                }
            }
        }

        // LEVEL 07: 28 Vida (7 Kutu, 4 Renk, Turbo Türbin & Pervane Kanatları)
        private static void BuildLevel07_TurboTurbine(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            core.name = "Turbine_RotorCore";
            core.transform.SetParent(root.transform, false);
            core.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            core.transform.localScale = new Vector3(1.16f, 0.40f, 1.16f);
            core.GetComponent<MeshRenderer>().sharedMaterial = s.Core;

            const float t = 0.06f;
            Material[] bladeMats = { s.Blue, s.Yellow, s.Teal, s.Coral };

            // 4 Pervane Kanadı (z = +0.435, her kanatta 3 vida -> 12 vida; içteki vida Merkez Göbek altında)
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f + 22f;
                float rad = angle * Mathf.Deg2Rad;
                Vector3 radialDir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
                PlateObject blade = CreatePlate(root.transform, $"Plate_Blade_{i}",
                    radialDir * 0.72f + new Vector3(0f, 0f, 0.435f),
                    Quaternion.Euler(0f, 0f, angle),
                    new Vector3(0.96f, 0.40f, t),
                    bladeMats[i]);
                CreateScrews(screwPrefab, blade, anim, t, (-0.34f, 0f), (0.04f, 0f), (0.34f, 0f));
            }

            // Ön Merkez Göbek Plakası (4 pervane kanadının iç vidasını örter -> 4 vida)
            PlateObject frontHub = CreatePlateWithNormal(root.transform, "Plate_Front_Hub", new Vector3(0f, 0f, 0.50f), Vector3.forward, Vector3.up, new Vector3(0.94f, 0.94f, t), s.Purple);
            CreateScrews(screwPrefab, frontHub, anim, t, (-0.30f, 0.30f), (0.30f, 0.30f), (-0.16f, 0f), (0.16f, 0f));

            // Ön Göbek Kilitleme Mandalı (4 vida -> ortadaki (-0.16, 0) ve (0.16, 0) vidalarını örter!)
            PlateObject frontLatch = CreatePlateWithNormal(root.transform, "Plate_Front_SealBar", new Vector3(0f, 0f, 0.565f), Vector3.forward, Vector3.up, new Vector3(1.06f, 0.32f, t), s.Steel);
            CreateScrews(screwPrefab, frontLatch, anim, t, (-0.38f, 0f), (-0.08f, 0f), (0.08f, 0f), (0.38f, 0f));

            // Arka Rotor Kapağı (4) + Arka Çapraz Destek (4) -> toplam 12 + 4 + 4 + 4 + 4 = 28 vida
            PlateObject rearCap = CreatePlateWithNormal(root.transform, "Plate_Rear_Cap", new Vector3(0f, 0f, -0.435f), Vector3.back, Vector3.up, new Vector3(0.96f, 0.96f, t), s.Mint);
            CreateScrews(screwPrefab, rearCap, anim, t, (-0.32f, 0.32f), (0.32f, 0.32f), (-0.18f, -0.18f), (0.18f, -0.18f));

            PlateObject rearBrace = CreatePlate(root.transform, "Plate_Rear_Brace", new Vector3(0f, -0.18f, -0.50f), Quaternion.Euler(0f, 180f, 0f), new Vector3(1.10f, 0.32f, t), s.Yellow);
            CreateScrews(screwPrefab, rearBrace, anim, t, (-0.42f, 0f), (-0.08f, 0f), (0.08f, 0f), (0.42f, 0f));
        }

        // LEVEL 08: 32 Vida (8 Kutu, 5 Renk, Asimetrik L-Blok İkili Küp)
        private static void BuildLevel08_LBlockDual(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            CreateCoreCube(root.transform, "LBlock_Core_Tower", new Vector3(-0.35f, 0f, 0f), new Vector3(1.18f, 1.88f, 1.14f), s.Core);
            CreateCoreCube(root.transform, "LBlock_Core_Foot", new Vector3(0.50f, -0.47f, 0f), new Vector3(1.08f, 0.94f, 1.14f), s.Core);

            const float t = 0.06f;

            // Sol Uzun Yüz (4 + 2 kilit = 6)
            PlateObject leftBase = CreatePlateWithNormal(root.transform, "Plate_L_LeftBase", new Vector3(-0.975f, 0f, 0f), Vector3.left, Vector3.up, new Vector3(0.94f, 1.56f, t), s.Teal);
            CreateScrews(screwPrefab, leftBase, anim, t, (-0.26f, 0.52f), (0.26f, 0.52f), (0f, 0f), (0f, -0.52f));

            PlateObject leftClamp = CreatePlateWithNormal(root.transform, "Plate_L_LeftClamp", new Vector3(-1.04f, 0f, 0f), Vector3.left, Vector3.up, new Vector3(1.02f, 0.34f, t), s.Coral);
            CreateScrews(screwPrefab, leftClamp, anim, t, (-0.34f, 0f), (0.34f, 0f));

            // Üst (4) + Sağ Ayak (4) + Alt (4) = 12
            PlateObject top = CreatePlateWithNormal(root.transform, "Plate_L_Top", new Vector3(-0.35f, 0.975f, 0f), Vector3.up, Vector3.back, new Vector3(0.96f, 0.96f, t), s.Yellow);
            CreateScrews(screwPrefab, top, anim, t, (-0.28f, 0.28f), (0.28f, 0.28f), (-0.28f, -0.28f), (0.28f, -0.28f));

            PlateObject rightFoot = CreatePlateWithNormal(root.transform, "Plate_L_RightFoot", new Vector3(1.075f, -0.47f, 0f), Vector3.right, Vector3.up, new Vector3(0.92f, 0.78f, t), s.Blue);
            CreateScrews(screwPrefab, rightFoot, anim, t, (-0.26f, 0.20f), (0.26f, 0.20f), (-0.26f, -0.20f), (0.26f, -0.20f));

            PlateObject bottom = CreatePlateWithNormal(root.transform, "Plate_L_Bottom", new Vector3(0.05f, -0.975f, 0f), Vector3.down, Vector3.forward, new Vector3(1.76f, 0.92f, t), s.Purple);
            CreateScrews(screwPrefab, bottom, anim, t, (-0.60f, 0.22f), (-0.20f, -0.22f), (0.20f, 0.22f), (0.60f, -0.22f));

            // Ön Alt + Ön Üst + Ön Dikey Köprü Kilidi (4 + 2 + 2 = 8)
            PlateObject fLower = CreatePlateWithNormal(root.transform, "Plate_L_FrontLower", new Vector3(0.05f, -0.47f, 0.605f), Vector3.forward, Vector3.up, new Vector3(1.68f, 0.76f, t), s.Mint);
            CreateScrews(screwPrefab, fLower, anim, t, (-0.40f, 0f), (0.14f, 0.20f), (0.58f, 0.20f), (0.58f, -0.20f));

            PlateObject fUpper = CreatePlateWithNormal(root.transform, "Plate_L_FrontUpper", new Vector3(-0.35f, 0.44f, 0.605f), Vector3.forward, Vector3.up, new Vector3(0.94f, 0.74f, t), s.Blue);
            CreateScrews(screwPrefab, fUpper, anim, t, (-0.26f, 0.16f), (0f, -0.05f));

            PlateObject fBridge = CreatePlateWithNormal(root.transform, "Plate_L_FrontLockCol", new Vector3(-0.35f, -0.02f, 0.67f), Vector3.forward, Vector3.up, new Vector3(0.36f, 1.38f, t), s.Steel);
            CreateScrews(screwPrefab, fBridge, anim, t, (0f, 0.52f), (0f, -0.52f));

            // Arka Alt + Arka Kilit (4 + 2 = 6 -> toplam 6 + 12 + 8 + 6 = 32 vida)
            PlateObject bLower = CreatePlateWithNormal(root.transform, "Plate_L_BackLower", new Vector3(0.05f, -0.47f, -0.605f), Vector3.back, Vector3.up, new Vector3(1.68f, 0.76f, t), s.Yellow);
            CreateScrews(screwPrefab, bLower, anim, t, (-0.56f, 0.20f), (0f, 0f), (0.56f, -0.20f), (0f, -0.22f));

            PlateObject bLock = CreatePlateWithNormal(root.transform, "Plate_L_BackLock", new Vector3(0.05f, -0.47f, -0.67f), Vector3.back, Vector3.up, new Vector3(0.96f, 0.34f, t), s.Coral);
            CreateScrews(screwPrefab, bLock, anim, t, (-0.34f, 0f), (0.34f, 0f));
        }

        // LEVEL 09: 36 Vida (9 Kutu, 5 Renk, 3 Katmanlı Zırhlı Kasa)
        private static void BuildLevel09_ArmoredVault(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            CreateCoreCube(root.transform, "Vault_Core", Vector3.zero, new Vector3(1.56f, 1.72f, 1.56f), s.Core);
            const float t = 0.06f;

            // ÖN 3 KATMANLI KASA KAPISI (6 + 6 + 4 = 16 vida)
            PlateObject doorBase = CreatePlateWithNormal(root.transform, "Vault_Front_DoorBase", new Vector3(0f, 0f, 0.815f), Vector3.forward, Vector3.up, new Vector3(1.36f, 1.44f, t), s.Teal);
            CreateScrews(screwPrefab, doorBase, anim, t,
                (-0.36f, 0.44f), (0.36f, 0.44f),
                (-0.36f, -0.44f), (0.36f, -0.44f),
                (0f, 0.44f), (0f, -0.44f));

            PlateObject barL = CreatePlateWithNormal(root.transform, "Vault_Front_BarL", new Vector3(-0.36f, 0f, 0.88f), Vector3.forward, Vector3.up, new Vector3(0.36f, 1.38f, t), s.Blue);
            CreateScrews(screwPrefab, barL, anim, t, (0f, 0.52f), (0f, 0f), (0f, -0.52f));

            PlateObject barR = CreatePlateWithNormal(root.transform, "Vault_Front_BarR", new Vector3(0.36f, 0f, 0.88f), Vector3.forward, Vector3.up, new Vector3(0.36f, 1.38f, t), s.Yellow);
            CreateScrews(screwPrefab, barR, anim, t, (0f, 0.52f), (0f, 0f), (0f, -0.52f));

            PlateObject masterSeal = CreatePlateWithNormal(root.transform, "Vault_Front_MasterSeal", new Vector3(0f, 0f, 0.945f), Vector3.forward, Vector3.up, new Vector3(1.24f, 0.36f, t), s.Steel);
            CreateScrews(screwPrefab, masterSeal, anim, t, (-0.48f, 0f), (-0.16f, 0f), (0.16f, 0f), (0.48f, 0f));

            // ARKA 2 KATMANLI ZIRH (4 + 4 = 8 vida)
            PlateObject backBase = CreatePlateWithNormal(root.transform, "Vault_Back_Armor", new Vector3(0f, 0f, -0.815f), Vector3.back, Vector3.up, new Vector3(1.32f, 1.32f, t), s.Purple);
            CreateScrews(screwPrefab, backBase, anim, t, (-0.42f, 0.42f), (0.42f, -0.42f), (-0.18f, 0f), (0.18f, 0f));

            PlateObject backLock = CreatePlateWithNormal(root.transform, "Vault_Back_LockBar", new Vector3(0f, 0f, -0.88f), Vector3.back, Vector3.up, new Vector3(1.28f, 0.36f, t), s.Coral);
            CreateScrews(screwPrefab, backLock, anim, t, (-0.48f, 0f), (-0.12f, 0.08f), (0.12f, -0.08f), (0.48f, 0f));

            // SAĞ + SOL MENTEŞELER (4+2 + 4+2 = 12 vida -> toplam 16 + 8 + 12 = 36 vida)
            PlateObject rightBase = CreatePlateWithNormal(root.transform, "Vault_Right_Armor", new Vector3(0.815f, 0f, 0f), Vector3.right, Vector3.up, new Vector3(1.24f, 1.36f, t), s.Mint);
            CreateScrews(screwPrefab, rightBase, anim, t, (-0.36f, 0.38f), (0.36f, 0.38f), (0f, 0f), (0f, -0.38f));

            PlateObject rightHinge = CreatePlateWithNormal(root.transform, "Vault_Right_Hinge", new Vector3(0.88f, 0f, 0f), Vector3.right, Vector3.up, new Vector3(1.22f, 0.34f, t), s.Yellow);
            CreateScrews(screwPrefab, rightHinge, anim, t, (-0.42f, 0f), (0.42f, 0f));

            PlateObject leftBase = CreatePlateWithNormal(root.transform, "Vault_Left_Armor", new Vector3(-0.815f, 0f, 0f), Vector3.left, Vector3.up, new Vector3(1.24f, 1.36f, t), s.Blue);
            CreateScrews(screwPrefab, leftBase, anim, t, (-0.36f, 0.38f), (0.36f, 0.38f), (0f, 0f), (0f, -0.38f));

            PlateObject leftHinge = CreatePlateWithNormal(root.transform, "Vault_Left_Hinge", new Vector3(-0.88f, 0f, 0f), Vector3.left, Vector3.up, new Vector3(1.22f, 0.34f, t), s.Coral);
            CreateScrews(screwPrefab, leftHinge, anim, t, (-0.42f, 0f), (0.42f, 0f));
        }

        // LEVEL 10: 40 Vida (10 Kutu, 6 Renk, 6 Yüzlü Master Kuantum Çekirdek)
        private static void BuildLevel10_MasterCoreCube(GameObject root, GameObject screwPrefab, ScrewAnimationConfigSO anim, PlateStyleSet s)
        {
            CreateCoreCube(root.transform, "Quantum_Core", Vector3.zero, new Vector3(1.58f, 1.58f, 1.58f), s.Core);
            const float t = 0.06f;

            // ÖN 3 KATMAN (4 + 3 + 3 = 10 vida)
            PlateObject fBase = CreatePlateWithNormal(root.transform, "Q_Front_Base", new Vector3(0f, 0f, 0.825f), Vector3.forward, Vector3.up, new Vector3(1.34f, 1.34f, t), s.Teal);
            CreateScrews(screwPrefab, fBase, anim, t, (-0.42f, 0.42f), (0.42f, -0.42f), (0f, -0.25f), (0f, 0.25f));

            PlateObject fMid = CreatePlateWithNormal(root.transform, "Q_Front_Col", new Vector3(0f, 0f, 0.89f), Vector3.forward, Vector3.up, new Vector3(0.46f, 1.38f, t), s.Purple);
            CreateScrews(screwPrefab, fMid, anim, t, (0f, 0.50f), (0f, 0f), (0f, -0.50f));

            PlateObject fTopBar = CreatePlateWithNormal(root.transform, "Q_Front_CrossBar", new Vector3(0f, 0f, 0.955f), Vector3.forward, Vector3.up, new Vector3(1.26f, 0.34f, t), s.Coral);
            CreateScrews(screwPrefab, fTopBar, anim, t, (-0.46f, 0f), (0f, 0f), (0.46f, 0f));

            // ARKA 2 KATMAN (4 + 4 = 8 vida)
            PlateObject bBase = CreatePlateWithNormal(root.transform, "Q_Back_Base", new Vector3(0f, 0f, -0.825f), Vector3.back, Vector3.up, new Vector3(1.34f, 1.34f, t), s.Yellow);
            CreateScrews(screwPrefab, bBase, anim, t, (-0.44f, 0.44f), (0.44f, -0.44f), (-0.22f, -0.18f), (0.22f, 0.18f));

            PlateObject bStrut = CreatePlate(root.transform, "Q_Back_Diagonal", new Vector3(0f, 0f, -0.89f), Quaternion.Euler(0f, 180f, 38f), new Vector3(1.38f, 0.34f, t), s.Blue);
            CreateScrews(screwPrefab, bStrut, anim, t, (-0.50f, 0f), (-0.16f, 0f), (0.16f, 0f), (0.50f, 0f));

            // SAĞ 2 KATMAN (4 + 3 = 7 vida)
            PlateObject rBase = CreatePlateWithNormal(root.transform, "Q_Right_Col", new Vector3(0.825f, 0f, 0f), Vector3.right, Vector3.up, new Vector3(0.54f, 1.34f, t), s.Mint);
            CreateScrews(screwPrefab, rBase, anim, t, (0f, 0.48f), (0f, 0f), (-0.16f, -0.48f), (0.16f, -0.48f));

            PlateObject rBelt = CreatePlateWithNormal(root.transform, "Q_Right_Belt", new Vector3(0.89f, 0f, 0f), Vector3.right, Vector3.up, new Vector3(1.28f, 0.34f, t), s.Steel);
            CreateScrews(screwPrefab, rBelt, anim, t, (-0.46f, 0f), (0f, 0f), (0.46f, 0f));

            // SOL 2 KATMAN (4 + 3 = 7 vida)
            PlateObject lBase = CreatePlateWithNormal(root.transform, "Q_Left_Col", new Vector3(-0.825f, 0f, 0f), Vector3.left, Vector3.up, new Vector3(0.54f, 1.34f, t), s.Blue);
            CreateScrews(screwPrefab, lBase, anim, t, (0f, 0.48f), (0f, 0f), (-0.16f, -0.48f), (0.16f, -0.48f));

            PlateObject lClamp = CreatePlateWithNormal(root.transform, "Q_Left_Clamp", new Vector3(-0.89f, 0f, 0f), Vector3.left, Vector3.up, new Vector3(1.28f, 0.34f, t), s.Yellow);
            CreateScrews(screwPrefab, lClamp, anim, t, (-0.46f, 0f), (0f, 0f), (0.46f, 0f));

            // ÜST & ALT KAPAĞI (4 + 4 = 8 vida -> toplam 10 + 8 + 7 + 7 + 4 + 4 = 40 vida)
            PlateObject top = CreatePlateWithNormal(root.transform, "Q_Top_Plate", new Vector3(0f, 0.825f, 0f), Vector3.up, Vector3.back, new Vector3(1.24f, 1.24f, t), s.Coral);
            CreateScrews(screwPrefab, top, anim, t, (-0.36f, 0.36f), (0.36f, 0.36f), (-0.36f, -0.36f), (0.36f, -0.36f));

            PlateObject bot = CreatePlateWithNormal(root.transform, "Q_Bottom_Plate", new Vector3(0f, -0.825f, 0f), Vector3.down, Vector3.forward, new Vector3(1.24f, 1.24f, t), s.Purple);
            CreateScrews(screwPrefab, bot, anim, t, (-0.36f, 0.36f), (0.36f, 0.36f), (-0.36f, -0.36f), (0.36f, -0.36f));
        }

        #endregion

        #region Geometry, Mesh & Material Helpers

        private static void CreateCoreCube(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPos;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static PlateObject CreatePlateWithNormal(
            Transform parent,
            string name,
            Vector3 localPos,
            Vector3 outwardNormal,
            Vector3 upHint,
            Vector3 plateSize,
            Material mat)
        {
            Quaternion rot = Quaternion.LookRotation(outwardNormal.normalized, upHint.normalized);
            return CreatePlate(parent, name, localPos, rot, plateSize, mat);
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
            UnityEngine.Object.DestroyImmediate(visualGo.GetComponent<Collider>());

            BoxCollider col = plateGo.AddComponent<BoxCollider>();
            col.size = plateSize;
            col.center = Vector3.zero;

            Rigidbody rb = plateGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            return plateGo.AddComponent<PlateObject>();
        }

        private static void CreateScrews(GameObject screwPrefab, PlateObject parentPlate, ScrewAnimationConfigSO animConfig, float plateThickness, params (float x, float y)[] coords)
        {
            for (int i = 0; i < coords.Length; i++)
            {
                GameObject screwGo = (GameObject)PrefabUtility.InstantiatePrefab(screwPrefab, parentPlate.transform);
                screwGo.name = $"Screw_{parentPlate.name}_{i + 1}";

                float surfaceZ = (plateThickness * 0.5f) - 0.107f;
                screwGo.transform.localPosition = new Vector3(coords[i].x, coords[i].y, surfaceZ);
                screwGo.transform.localRotation = Quaternion.identity;
                screwGo.transform.localScale = Vector3.one;

                ScrewObject screw = screwGo.GetComponent<ScrewObject>();
                SerializedObject so = new SerializedObject(screw);
                so.FindProperty("_parentPlate").objectReferenceValue = parentPlate;
                if (animConfig != null)
                {
                    so.FindProperty("_animationConfig").objectReferenceValue = animConfig;
                }
                so.ApplyModifiedProperties();
            }
        }

        private static Mesh GetOrCreateHexPrismMesh(float radius, float height)
        {
            string path = $"{MeshesFolder}/HexPrismCore.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = "HexPrismCore" };
                AssetDatabase.CreateAsset(mesh, path);
            }

            List<Vector3> verts = new();
            List<Vector2> uvs = new();
            List<int> tris = new();
            float halfH = height * 0.5f;
            const int sides = 6;

            // 6 düz yan yüzey (Unity sol-el saat yönü winding -> normal dışarı bakar)
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i * 60f - 30f) * Mathf.Deg2Rad;
                float a1 = ((i + 1) * 60f - 30f) * Mathf.Deg2Rad;

                Vector3 b0 = new Vector3(Mathf.Sin(a0) * radius, -halfH, Mathf.Cos(a0) * radius);
                Vector3 b1 = new Vector3(Mathf.Sin(a1) * radius, -halfH, Mathf.Cos(a1) * radius);
                Vector3 t0 = new Vector3(b0.x, halfH, b0.z);
                Vector3 t1 = new Vector3(b1.x, halfH, b1.z);

                int idx = verts.Count;
                verts.Add(b0);
                verts.Add(t0);
                verts.Add(t1);
                verts.Add(b1);

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));

                tris.Add(idx + 0); tris.Add(idx + 2); tris.Add(idx + 1);
                tris.Add(idx + 0); tris.Add(idx + 3); tris.Add(idx + 2);
            }

            // Üst ve Alt kapak
            int topCenter = verts.Count;
            verts.Add(new Vector3(0f, halfH, 0f));
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i * 60f - 30f) * Mathf.Deg2Rad;
                float a1 = ((i + 1) * 60f - 30f) * Mathf.Deg2Rad;
                int vIdx = verts.Count;
                verts.Add(new Vector3(Mathf.Sin(a0) * radius, halfH, Mathf.Cos(a0) * radius));
                verts.Add(new Vector3(Mathf.Sin(a1) * radius, halfH, Mathf.Cos(a1) * radius));
                uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Sin(a0), 0.5f + 0.5f * Mathf.Cos(a0)));
                uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Sin(a1), 0.5f + 0.5f * Mathf.Cos(a1)));
                tris.Add(topCenter); tris.Add(vIdx); tris.Add(vIdx + 1);
            }

            int botCenter = verts.Count;
            verts.Add(new Vector3(0f, -halfH, 0f));
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i * 60f - 30f) * Mathf.Deg2Rad;
                float a1 = ((i + 1) * 60f - 30f) * Mathf.Deg2Rad;
                int vIdx = verts.Count;
                verts.Add(new Vector3(Mathf.Sin(a0) * radius, -halfH, Mathf.Cos(a0) * radius));
                verts.Add(new Vector3(Mathf.Sin(a1) * radius, -halfH, Mathf.Cos(a1) * radius));
                uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Sin(a0), 0.5f + 0.5f * Mathf.Cos(a0)));
                uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Sin(a1), 0.5f + 0.5f * Mathf.Cos(a1)));
                tris.Add(botCenter); tris.Add(vIdx + 1); tris.Add(vIdx);
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static PlateStyleSet LoadOrCreateStyles()
        {
            return new PlateStyleSet
            {
                Core = GetOrCreateMaterial("Mat_ChassisCore", new Color(0.15f, 0.17f, 0.22f, 1f), 0.28f, 0.25f),
                AccentDark = GetOrCreateMaterial("Mat_PlateCharcoal", new Color(0.24f, 0.27f, 0.34f, 1f), 0.30f, 0.18f),
                Blue = GetOrCreateMaterial("Mat_PlateSkyBlue", new Color(0.27f, 0.62f, 0.94f, 1f), 0.32f, 0.05f),
                Yellow = GetOrCreateMaterial("Mat_PlateWarmGold", new Color(0.95f, 0.78f, 0.24f, 1f), 0.32f, 0.05f),
                Teal = GetOrCreateMaterial("Mat_PlateTeal", new Color(0.14f, 0.72f, 0.65f, 1f), 0.32f, 0.05f),
                Coral = GetOrCreateMaterial("Mat_PlateCoral", new Color(0.93f, 0.44f, 0.38f, 1f), 0.32f, 0.05f),
                Purple = GetOrCreateMaterial("Mat_PlateLavender", new Color(0.62f, 0.45f, 0.85f, 1f), 0.32f, 0.05f),
                Mint = GetOrCreateMaterial("Mat_PlateMint", new Color(0.32f, 0.82f, 0.56f, 1f), 0.32f, 0.05f),
                Steel = GetOrCreateMaterial("Mat_PlateCreamSteel", new Color(0.86f, 0.85f, 0.80f, 1f), 0.28f, 0.15f)
            };
        }

        private static Material GetOrCreateMaterial(string matName, Color color, float smoothness, float metallic)
        {
            string path = $"{MaterialsFolder}/{matName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
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

        private static void EnsureFolder(string parent, string childName)
        {
            string full = $"{parent}/{childName}";
            if (!AssetDatabase.IsValidFolder(full))
            {
                AssetDatabase.CreateFolder(parent, childName);
            }
        }

        #endregion
    }
}
