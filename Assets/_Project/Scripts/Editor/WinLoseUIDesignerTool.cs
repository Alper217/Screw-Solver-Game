using System.Collections.Generic;
using ScrewSolver.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ScrewSolver.Editor
{
    /// <summary>
    /// Peak Games / Dream Games / Good Job Games tarzında parlak, katmanlı,
    /// kurdele pankartlı Win (Victory) ve Lose (Defeat) UI panellerini
    /// indirilen GUI ve Cartoon FX Remaster Free paketleriyle kurar.
    /// Mevcut HUD_TopBar ve Canvas ayarlarına dokunmaz.
    /// </summary>
    [InitializeOnLoad]
    public static class WinLoseUIDesignerTool
    {
        private const string AutoBuildKey = "WinLoseUIDesigner_Built_v2";

        private const string PopupsPath = "Assets/Belevich/Hyper casual mobile GUI/Sprites/popups.png";
        private const string ButtonsPath = "Assets/Belevich/Hyper casual mobile GUI/Sprites/buttons.png";
        private const string IconsPath = "Assets/Belevich/Hyper casual mobile GUI/Sprites/icons.png";

        private const string TrophyIconPath = "Assets/Layer Lab/2D Icons-CasualIconPack/Icons/256/Icon_Collectibles_Trophy01_Gold.png";
        private const string HeartIconPath = "Assets/Layer Lab/2D Icons-CasualIconPack/Icons/256/Icon_Resources_Heart01_Red.png";
        private const string RefreshIconPath = "Assets/HardArtcore/UI/SimpleIcons/Sprites/Icons/Filled/ic_refresh_fill.png";

        private const string VfxFireworkPath = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Explosions/CFXR4 Firework 1 Cyan-Purple (HDR).prefab";
        private const string VfxFallingStarsPath = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Magic Misc/CFXR4 Falling Stars.prefab";
        private const string VfxSparklePath = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Misc/CFXR2 Shiny Item (Loop).prefab";
        private const string VfxBrokenHeartPath = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Misc/CFXR2 Broken Heart.prefab";
        private const string VfxSmokePath = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Misc/CFXR3 Hit Misc F Smoke.prefab";

        static WinLoseUIDesignerTool()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying) return;
                if (EditorPrefs.GetBool(AutoBuildKey, false)) return;

                GameUIController uiController = Object.FindFirstObjectByType<GameUIController>();
                if (uiController != null)
                {
                    EditorPrefs.SetBool(AutoBuildKey, true);
                    BuildWinLoseUI(silent: true);
                }
            };
        }

        [MenuItem("ScrewSolver/UI/Peak-Dream Win & Lose UI Kur")]
        public static void BuildMenuCommand()
        {
            BuildWinLoseUI(silent: false);
        }

        [MenuItem("ScrewSolver/UI/Önizle: Victory Paneli Göster")]
        public static void PreviewVictory()
        {
            TogglePreviewPanel("VictoryPanel", "DefeatPanel");
        }

        [MenuItem("ScrewSolver/UI/Önizle: Defeat Paneli Göster")]
        public static void PreviewDefeat()
        {
            TogglePreviewPanel("DefeatPanel", "VictoryPanel");
        }

        private const string SavedPlaneDistKey = "WinLoseUIDesigner_SavedPlaneDist";
        private const string HasSavedPlaneDistKey = "WinLoseUIDesigner_HasSavedPlaneDist";

        [MenuItem("ScrewSolver/UI/Önizle: Popupları Gizle")]
        public static void HidePreviews()
        {
            GameUIController ui = Object.FindFirstObjectByType<GameUIController>();
            if (ui == null) return;
            ui.HideAllPopupsImmediately();

            Canvas canvas = ui.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera && SessionState.GetBool(HasSavedPlaneDistKey, false))
            {
                canvas.planeDistance = SessionState.GetFloat(SavedPlaneDistKey, 100f);
                SessionState.SetBool(HasSavedPlaneDistKey, false);
            }
            EditorUtility.SetDirty(ui.gameObject);
        }

        private static void TogglePreviewPanel(string showName, string hideName)
        {
            GameUIController ui = Object.FindFirstObjectByType<GameUIController>();
            if (ui == null) return;

            Canvas canvas = ui.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.planeDistance > 2.2f)
            {
                SessionState.SetFloat(SavedPlaneDistKey, canvas.planeDistance);
                SessionState.SetBool(HasSavedPlaneDistKey, true);
                canvas.planeDistance = 2.0f;
            }

            Transform showTr = ui.transform.Find(showName);
            Transform hideTr = ui.transform.Find(hideName);
            if (showTr != null) showTr.gameObject.SetActive(true);
            if (hideTr != null) hideTr.gameObject.SetActive(false);
            Selection.activeGameObject = showTr != null ? showTr.gameObject : ui.gameObject;
        }

        public static void BuildWinLoseUI(bool silent = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                if (!silent) EditorUtility.DisplayDialog("Uyarı", "Lütfen Play modundan çıkıp tekrar deneyin.", "Tamam");
                return;
            }

            GameUIController uiController = Object.FindFirstObjectByType<GameUIController>();
            if (uiController == null)
            {
                GameObject canvasGo = GameObject.Find("UI_Canvas");
                if (canvasGo == null)
                {
                    if (!silent) EditorUtility.DisplayDialog("Hata", "Sahnede UI_Canvas / GameUIController bulunamadı.", "Tamam");
                    return;
                }
                uiController = canvasGo.AddComponent<GameUIController>();
            }

            // Sprite sözlüklerini yükle
            Dictionary<string, Sprite> popups = LoadSpriteSheet(PopupsPath);
            Dictionary<string, Sprite> buttons = LoadSpriteSheet(ButtonsPath);
            Dictionary<string, Sprite> icons = LoadSpriteSheet(IconsPath);

            Sprite trophySprite = EnsureSpriteAsset(TrophyIconPath);
            Sprite heartSprite = EnsureSpriteAsset(HeartIconPath);
            Sprite refreshSprite = EnsureSpriteAsset(RefreshIconPath);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Eski panelleri temizle (sadece VictoryPanel ve DefeatPanel; HUD_TopBar'a dokunma!)
            DestroyExistingChild(uiController.transform, "VictoryPanel");
            DestroyExistingChild(uiController.transform, "DefeatPanel");

            SerializedObject uiSo = new SerializedObject(uiController);

            // =========================================================================
            // 1. VICTORY PANEL (Peak / Dream Games Tarzı)
            // =========================================================================
            GameObject vicPanel = CreateFullScreenOverlay(uiController.transform, "VictoryPanel", new Color(0.04f, 0.08f, 0.17f, 0.78f), out CanvasGroup vicCg);

            // Ana Krem Kart (popups_12: 716x793 -> 860x952 orantılı)
            GameObject vicCard = CreateImageObject(vicPanel.transform, "VictoryCard", GetSprite(popups, "popups_12"),
                Color.white, new Vector2(0f, -20f), new Vector2(860f, 952f), preserveAspect: true);

            // Üst Turkuaz Kurdele Pankart (popups_18: 855x367 -> 820x352 orantılı)
            GameObject vicRibbon = CreateImageObject(vicCard.transform, "RibbonBanner", GetSprite(popups, "popups_18"),
                Color.white, new Vector2(0f, 435f), new Vector2(820f, 352f), preserveAspect: true);

            Text vicTitle = CreateStyledText(vicRibbon.transform, "Title", "LEVEL CLEARED!", font, 50,
                Color.white, new Vector2(0f, 32f), new Vector2(560f, 120f),
                outlineColor: new Color(0.02f, 0.34f, 0.40f, 1f), outlineDist: new Vector2(4f, -4f),
                shadowColor: new Color(0.01f, 0.20f, 0.25f, 0.85f), shadowDist: new Vector2(0f, -6f),
                bestFit: true, minSize: 28, maxSize: 50);

            // 3 Yıldız Kemeri (Koyu yuva icons_7 + Altın yıldız icons_6)
            GameObject starsContainer = new GameObject("StarsContainer", typeof(RectTransform));
            starsContainer.transform.SetParent(vicCard.transform, false);
            RectTransform starsRt = starsContainer.GetComponent<RectTransform>();
            starsRt.anchoredPosition = new Vector2(0f, 175f);
            starsRt.sizeDelta = new Vector2(680f, 230f);

            float[] starX = { -205f, 0f, 205f };
            float[] starY = { -18f, 22f, -18f };
            float[] starTilt = { 10f, 0f, -10f };
            SerializedProperty starsProp = uiSo.FindProperty("_victoryStars");
            starsProp.arraySize = 3;

            Sprite starSocketSprite = GetSprite(icons, "icons_7");
            Sprite starGoldSprite = GetSprite(icons, "icons_6");

            for (int i = 0; i < 3; i++)
            {
                GameObject slotGo = CreateImageObject(starsContainer.transform, $"StarSlot_{i + 1}", starSocketSprite,
                    new Color(1f, 1f, 1f, 0.92f), new Vector2(starX[i], starY[i]), new Vector2(175f, 168f), preserveAspect: true);
                slotGo.transform.localRotation = Quaternion.Euler(0f, 0f, starTilt[i]);

                GameObject starGo = CreateImageObject(slotGo.transform, $"Star_{i + 1}", starGoldSprite,
                    Color.white, Vector2.zero, new Vector2(175f, 168f), preserveAspect: true);
                starsProp.GetArrayElementAtIndex(i).objectReferenceValue = starGo.GetComponent<Image>();
            }

            // İç Ödül / Kupa Kutusu (popups_4: 1329x666 -> 720x300)
            GameObject rewardInset = CreateImageObject(vicCard.transform, "RewardInset", GetSprite(popups, "popups_4"),
                new Color(1f, 0.98f, 0.94f, 1f), new Vector2(0f, -70f), new Vector2(720f, 300f), preserveAspect: true);

            GameObject trophyGo = CreateImageObject(rewardInset.transform, "TrophyIcon", trophySprite,
                Color.white, new Vector2(-185f, 6f), new Vector2(168f, 168f), preserveAspect: true);

            CreateStyledText(rewardInset.transform, "GreatJobLabel", "GREAT JOB!", font, 36,
                new Color(0.36f, 0.24f, 0.15f, 1f), new Vector2(90f, 44f), new Vector2(330f, 60f),
                outlineColor: Color.white, outlineDist: new Vector2(2f, -2f),
                shadowColor: new Color(0f, 0f, 0f, 0.18f), shadowDist: new Vector2(0f, -3f),
                bestFit: true, minSize: 24, maxSize: 38);

            Text vicScore = CreateStyledText(rewardInset.transform, "ScoreText", "TIME BONUS +120", font, 36,
                new Color(0.92f, 0.48f, 0.08f, 1f), new Vector2(90f, -26f), new Vector2(330f, 65f),
                outlineColor: Color.white, outlineDist: new Vector2(2.5f, -2.5f),
                shadowColor: new Color(0.35f, 0.15f, 0f, 0.35f), shadowDist: new Vector2(0f, -4f),
                bestFit: true, minSize: 24, maxSize: 38);

            // Parlak Yeşil Devam Butonu (buttons_12: 738x235 -> 580x185 orantılı)
            GameObject nextBtnGo = CreateImageObject(vicCard.transform, "NextLevelButton", GetSprite(buttons, "buttons_12"),
                Color.white, new Vector2(0f, -335f), new Vector2(580f, 185f), preserveAspect: true);
            Button nextBtn = nextBtnGo.AddComponent<Button>();
            nextBtn.targetGraphic = nextBtnGo.GetComponent<Image>();

            CreateStyledText(nextBtnGo.transform, "Text", "NEXT LEVEL", font, 50,
                Color.white, new Vector2(0f, 8f), new Vector2(500f, 110f),
                outlineColor: new Color(0.07f, 0.37f, 0.09f, 1f), outlineDist: new Vector2(3.5f, -3.5f),
                shadowColor: new Color(0.04f, 0.24f, 0.06f, 0.85f), shadowDist: new Vector2(0f, -5f));

            // =========================================================================
            // 2. DEFEAT PANEL (Peak / Dream Games Tarzı)
            // =========================================================================
            GameObject defPanel = CreateFullScreenOverlay(uiController.transform, "DefeatPanel", new Color(0.11f, 0.04f, 0.06f, 0.80f), out CanvasGroup defCg);

            // Ana Kart (popups_12)
            GameObject defCard = CreateImageObject(defPanel.transform, "DefeatCard", GetSprite(popups, "popups_12"),
                new Color(1f, 0.96f, 0.94f, 1f), new Vector2(0f, -20f), new Vector2(860f, 900f), preserveAspect: true);

            // Üst Turuncu-Kırmızı Kurdele Pankart (popups_19: 854x368 -> 820x353 orantılı)
            GameObject defRibbon = CreateImageObject(defCard.transform, "RibbonBanner", GetSprite(popups, "popups_19"),
                Color.white, new Vector2(0f, 410f), new Vector2(820f, 353f), preserveAspect: true);

            Text defTitle = CreateStyledText(defRibbon.transform, "Title", "TRY AGAIN!", font, 50,
                Color.white, new Vector2(0f, 32f), new Vector2(560f, 120f),
                outlineColor: new Color(0.48f, 0.12f, 0.03f, 1f), outlineDist: new Vector2(4f, -4f),
                shadowColor: new Color(0.29f, 0.06f, 0.01f, 0.85f), shadowDist: new Vector2(0f, -6f),
                bestFit: true, minSize: 28, maxSize: 50);

            // Merkez Kalp + Çarpı Rozeti
            GameObject failBadge = new GameObject("FailBadge", typeof(RectTransform));
            failBadge.transform.SetParent(defCard.transform, false);
            RectTransform badgeRt = failBadge.GetComponent<RectTransform>();
            badgeRt.anchoredPosition = new Vector2(0f, 155f);
            badgeRt.sizeDelta = new Vector2(210f, 210f);

            CreateImageObject(failBadge.transform, "HeartIcon", heartSprite != null ? heartSprite : GetSprite(icons, "icons_3"),
                Color.white, Vector2.zero, new Vector2(185f, 185f), preserveAspect: true);

            CreateImageObject(failBadge.transform, "CrossBadge", GetSprite(buttons, "buttons_10"),
                Color.white, new Vector2(64f, -56f), new Vector2(82f, 82f), preserveAspect: true);

            // Sebep Kutusu (popups_4) - Kenarlarda bol boşluk bırak
            GameObject reasonInset = CreateImageObject(defCard.transform, "ReasonInset", GetSprite(popups, "popups_4"),
                new Color(1f, 0.98f, 0.95f, 1f), new Vector2(0f, -85f), new Vector2(720f, 260f), preserveAspect: true);

            Text defReason = CreateStyledText(reasonInset.transform, "Reason", "No valid moves left!\nTry a different order next time.", font, 30,
                new Color(0.31f, 0.22f, 0.16f, 1f), Vector2.zero, new Vector2(530f, 185f),
                outlineColor: new Color(1f, 1f, 1f, 0.65f), outlineDist: new Vector2(1.5f, -1.5f),
                shadowColor: new Color(0f, 0f, 0f, 0.12f), shadowDist: new Vector2(0f, -2f),
                bestFit: true, minSize: 20, maxSize: 30);
            defReason.lineSpacing = 1.15f;
            defReason.horizontalOverflow = HorizontalWrapMode.Wrap;
            defReason.verticalOverflow = VerticalWrapMode.Truncate;

            // Turuncu-Kırmızı Tekrar Dene Butonu (buttons_38: 738x235 -> 580x185 orantılı)
            GameObject retryBtnGo = CreateImageObject(defCard.transform, "RetryButton", GetSprite(buttons, "buttons_38"),
                Color.white, new Vector2(0f, -325f), new Vector2(580f, 185f), preserveAspect: true);
            Button retryBtn = retryBtnGo.AddComponent<Button>();
            retryBtn.targetGraphic = retryBtnGo.GetComponent<Image>();

            if (refreshSprite != null)
            {
                GameObject rIcon = CreateImageObject(retryBtnGo.transform, "Icon", refreshSprite,
                    Color.white, new Vector2(-170f, 8f), new Vector2(64f, 64f), preserveAspect: true);
                Shadow rShadow = rIcon.AddComponent<Shadow>();
                rShadow.effectColor = new Color(0.45f, 0.10f, 0.02f, 0.8f);
                rShadow.effectDistance = new Vector2(0f, -4f);
            }

            CreateStyledText(retryBtnGo.transform, "Text", "TRY AGAIN", font, 48,
                Color.white, new Vector2(refreshSprite != null ? 30f : 0f, 8f), new Vector2(390f, 110f),
                outlineColor: new Color(0.48f, 0.12f, 0.03f, 1f), outlineDist: new Vector2(3.5f, -3.5f),
                shadowColor: new Color(0.29f, 0.06f, 0.01f, 0.85f), shadowDist: new Vector2(0f, -5f));

            // =========================================================================
            // 3. REFERANSLARI & CARTOON FX PREFABLARINI BAĞLA
            // =========================================================================
            uiSo.FindProperty("_victoryPanel").objectReferenceValue = vicPanel;
            uiSo.FindProperty("_victoryCanvasGroup").objectReferenceValue = vicCg;
            uiSo.FindProperty("_victoryCard").objectReferenceValue = vicCard.transform;
            uiSo.FindProperty("_victoryRibbon").objectReferenceValue = vicRibbon.transform;
            uiSo.FindProperty("_victoryTrophyBadge").objectReferenceValue = trophyGo.transform;
            uiSo.FindProperty("_victoryTitleText").objectReferenceValue = vicTitle;
            uiSo.FindProperty("_victoryScoreText").objectReferenceValue = vicScore;
            uiSo.FindProperty("_nextLevelButton").objectReferenceValue = nextBtn;

            uiSo.FindProperty("_defeatPanel").objectReferenceValue = defPanel;
            uiSo.FindProperty("_defeatCanvasGroup").objectReferenceValue = defCg;
            uiSo.FindProperty("_defeatCard").objectReferenceValue = defCard.transform;
            uiSo.FindProperty("_defeatRibbon").objectReferenceValue = defRibbon.transform;
            uiSo.FindProperty("_defeatCenterBadge").objectReferenceValue = failBadge.transform;
            uiSo.FindProperty("_defeatTitleText").objectReferenceValue = defTitle;
            uiSo.FindProperty("_defeatReasonText").objectReferenceValue = defReason;
            uiSo.FindProperty("_retryButton").objectReferenceValue = retryBtn;

            uiSo.FindProperty("_victoryFireworkVfxPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(VfxFireworkPath);
            uiSo.FindProperty("_victoryStarsVfxPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(VfxFallingStarsPath);
            uiSo.FindProperty("_victorySparkleVfxPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(VfxSparklePath);
            uiSo.FindProperty("_defeatBrokenHeartVfxPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(VfxBrokenHeartPath);
            uiSo.FindProperty("_defeatSmokeVfxPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(VfxSmokePath);

            uiSo.ApplyModifiedProperties();

            vicPanel.SetActive(false);
            defPanel.SetActive(false);

            EditorUtility.SetDirty(uiController);
            EditorSceneManager.MarkSceneDirty(uiController.gameObject.scene);
            EditorSceneManager.SaveScene(uiController.gameObject.scene);

            Debug.Log("<color=#00FF88><b>[WinLoseUIDesigner]</b> Peak/Dream Games tarzı Win & Lose UI ve Cartoon FX Remaster efektleri kuruldu!</color>");
            if (!silent)
            {
                EditorUtility.DisplayDialog("Başarılı!",
                    "Peak / Dream / Good Job Games tarzı Victory & Defeat UI kuruldu ve Cartoon FX Remaster efektleri bağlandı!\n\n" +
                    "• Üst menüden 'ScrewSolver > UI > Önizle' ile Editörde görebilirsiniz.\n" +
                    "• Mevcut HUD ve Canvas ayarlarınıza dokunulmadı.", "Harika!");
            }
        }

        private static GameObject CreateFullScreenOverlay(Transform parent, string name, Color bgColor, out CanvasGroup cg)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            go.GetComponent<Image>().color = bgColor;
            cg = go.GetComponent<CanvasGroup>();
            return go;
        }

        private static GameObject CreateImageObject(Transform parent, string name, Sprite sprite, Color tint, Vector2 anchoredPos, Vector2 sizeDelta, bool preserveAspect)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = tint;
            img.preserveAspect = preserveAspect;
            return go;
        }

        private static Text CreateStyledText(
            Transform parent, string name, string content, Font font, int fontSize, Color color,
            Vector2 anchoredPos, Vector2 sizeDelta,
            Color outlineColor, Vector2 outlineDist,
            Color shadowColor, Vector2 shadowDist,
            bool bestFit = false, int minSize = 24, int maxSize = 60)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Text txt = go.GetComponent<Text>();
            txt.font = font;
            txt.fontSize = fontSize;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = color;
            txt.text = content;
            txt.resizeTextForBestFit = bestFit;
            txt.resizeTextMinSize = minSize;
            txt.resizeTextMaxSize = maxSize;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = outlineDist;

            Shadow shadow = go.AddComponent<Shadow>();
            shadow.effectColor = shadowColor;
            shadow.effectDistance = shadowDist;

            return txt;
        }

        private static void DestroyExistingChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }
        }

        private static Dictionary<string, Sprite> LoadSpriteSheet(string assetPath)
        {
            Dictionary<string, Sprite> dict = new Dictionary<string, Sprite>();
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets == null) return dict;

            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite sp && !dict.ContainsKey(sp.name))
                {
                    dict[sp.name] = sp;
                }
            }
            return dict;
        }

        private static Sprite GetSprite(Dictionary<string, Sprite> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out Sprite sp) ? sp : null;
        }

        private static Sprite EnsureSpriteAsset(string assetPath)
        {
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sp != null) return sp;

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
                sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            }
            return sp;
        }
    }
}
