using System.Collections.Generic;
using System.IO;
using ScrewSolver.Gameplay.Boxes;
using ScrewSolver.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ScrewSolver.Editor
{
    /// <summary>
    /// Ana oyun (In-Game) HUD ve arka plan görsel tasarımını Belevich'in turkuaz-çerçeveli
    /// 2.5D krem kart ailesi ve HardArtcore ikonlarıyla kurar.
    /// Tüm üst/alt göstergeler tek bir tutarlı renk dilindedir ve ortadaki 3 TargetBox
    /// arkasında raf yuvaları (BoxDockSlot) oluşturarak 3D kutuları 2.5D HUD ile bütünleştirir.
    /// </summary>
    [InitializeOnLoad]
    public static class InGameHUDDesignerTool
    {
        private const string AutoBuildKey = "InGameHUDDesigner_Built_v2";

        private const string PopupsPath = "Assets/Belevich/Hyper casual mobile GUI/Sprites/popups.png";
        private const string AdditionalControlsPath = "Assets/Belevich/Hyper casual mobile GUI/Sprites/additional controls.png";

        private const string RefreshIconPath = "Assets/HardArtcore/UI/SimpleIcons/Sprites/Icons/Filled/ic_refresh_fill.png";
        private const string StopwatchIconPath = "Assets/HardArtcore/UI/SimpleIcons/Sprites/Icons/Filled/ic_stopwatch_fill.png";
        private const string TargetIconPath = "Assets/HardArtcore/UI/SimpleIcons/Sprites/Icons/Filled/ic_target_fill.png";
        private const string CursorIconPath = "Assets/HardArtcore/UI/SimpleIcons/Sprites/Icons/Filled/ic_cursor_fill.png";
        private const string BgGradientAssetPath = "Assets/_Project/Art/UI/BgSkyGradient.png";

        private static readonly Color InkDarkColor = new Color(0.08f, 0.24f, 0.34f, 1f);
        private static readonly Color TealIconColor = new Color(0.05f, 0.45f, 0.54f, 1f);

        static InGameHUDDesignerTool()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying) return;
                if (EditorPrefs.GetBool(AutoBuildKey, false)) return;

                GameUIController uiController = Object.FindFirstObjectByType<GameUIController>();
                if (uiController != null)
                {
                    EditorPrefs.SetBool(AutoBuildKey, true);
                    BuildInGameHUD(silent: true);
                }
            };
        }

        [MenuItem("ScrewSolver/UI/Ana Oyun HUD Tasarımını Kur")]
        public static void BuildMenuCommand()
        {
            BuildInGameHUD(silent: false);
        }

        public static void BuildInGameHUD(bool silent = false)
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

            Transform canvasTr = uiController.transform;

            Dictionary<string, Sprite> popups = LoadSpriteSheet(PopupsPath);
            Dictionary<string, Sprite> controls = LoadSpriteSheet(AdditionalControlsPath);

            Sprite cardFrameSprite = GetSprite(popups, "popups_3");          // Turkuaz çerçeveli 2.5D krem panel
            Sprite squareBtnSprite = GetSprite(controls, "additional controls_14"); // Turkuaz çerçeveli 2.5D krem kare buton
            Sprite barBgSprite = GetSprite(controls, "additional controls_8");
            Sprite barFillSprite = GetSprite(controls, "additional controls_9");

            Sprite refreshSprite = EnsureSpriteAsset(RefreshIconPath);
            Sprite stopwatchSprite = EnsureSpriteAsset(StopwatchIconPath);
            Sprite targetSprite = EnsureSpriteAsset(TargetIconPath);
            Sprite cursorSprite = EnsureSpriteAsset(CursorIconPath);
            Sprite bgGradientSprite = EnsureBgGradientSprite();

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Eski HUD öğelerini temizle (Victory/Defeat panellerine dokunma!)
            DestroyExistingChild(canvasTr, "TopBar");
            DestroyExistingChild(canvasTr, "HUD_TopBar");
            DestroyExistingChild(canvasTr, "HUD_Bottom");
            DestroyExistingChild(canvasTr, "BackgroundGradient");

            // =========================================================================
            // 1. ARKA PLAN GRADIENT (3D Küp & Kutuların Arkasında, Raycast Kapalı)
            // =========================================================================
            GameObject bgGo = new GameObject("BackgroundGradient", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(canvasTr, false);
            bgGo.transform.SetSiblingIndex(0);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            Image bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = bgGradientSprite;
            bgImg.color = Color.white;
            bgImg.raycastTarget = false;

            // =========================================================================
            // 2. ÜST HUD BAR (Tek Renk Ailesi: Derin Okyanus Dock + Turkuaz-Krem 2.5D Kartlar)
            // =========================================================================
            GameObject topBarGo = new GameObject("HUD_TopBar", typeof(RectTransform), typeof(Image));
            topBarGo.transform.SetParent(canvasTr, false);
            topBarGo.transform.SetSiblingIndex(1);

            RectTransform topRt = topBarGo.GetComponent<RectTransform>();
            topRt.anchorMin = new Vector2(0f, 1f);
            topRt.anchorMax = new Vector2(1f, 1f);
            topRt.pivot = new Vector2(0.5f, 1f);
            topRt.anchoredPosition = Vector2.zero;
            topRt.sizeDelta = new Vector2(0f, 274f);

            Image topBgImg = topBarGo.GetComponent<Image>();
            topBgImg.color = new Color(0.09f, 0.21f, 0.37f, 0.96f);
            topBgImg.raycastTarget = false;

            // Alt Vurgu Çizgisi & Yumuşak 2.5D Gölge
            GameObject shadowLine = new GameObject("BottomShadow", typeof(RectTransform), typeof(Image));
            shadowLine.transform.SetParent(topBarGo.transform, false);
            RectTransform shRt = shadowLine.GetComponent<RectTransform>();
            shRt.anchorMin = new Vector2(0f, 0f);
            shRt.anchorMax = new Vector2(1f, 0f);
            shRt.pivot = new Vector2(0.5f, 1f);
            shRt.anchoredPosition = Vector2.zero;
            shRt.sizeDelta = new Vector2(0f, 10f);
            Image shImg = shadowLine.GetComponent<Image>();
            shImg.color = new Color(0.03f, 0.08f, 0.18f, 0.45f);
            shImg.raycastTarget = false;

            GameObject accentLine = new GameObject("BottomAccent", typeof(RectTransform), typeof(Image));
            accentLine.transform.SetParent(topBarGo.transform, false);
            RectTransform acRt = accentLine.GetComponent<RectTransform>();
            acRt.anchorMin = new Vector2(0f, 0f);
            acRt.anchorMax = new Vector2(1f, 0f);
            acRt.pivot = new Vector2(0.5f, 0f);
            acRt.anchoredPosition = Vector2.zero;
            acRt.sizeDelta = new Vector2(0f, 6f);
            Image acImg = accentLine.GetComponent<Image>();
            acImg.color = new Color(0.14f, 0.72f, 0.80f, 0.95f);
            acImg.raycastTarget = false;

            // --- ORTA 3 KUTU RAF YUVALARI (3D Kutuları 2.5D Panelin İçine Oturtur) ---
            CreateBoxDockSlots(topBarGo.transform, cardFrameSprite);

            // --- SOL KANAT: Restart Butonu + Level Rozeti (Turkuaz-Krem 2.5D Stil) ---
            GameObject restartGo = CreateImageObject(topBarGo.transform, "RestartButton", squareBtnSprite,
                Color.white, new Vector2(76f, -66f), new Vector2(88f, 88f), preserveAspect: true, raycastTarget: true);
            RectTransform rstRt = restartGo.GetComponent<RectTransform>();
            rstRt.anchorMin = new Vector2(0f, 1f);
            rstRt.anchorMax = new Vector2(0f, 1f);
            AddCardDropShadow(restartGo, new Vector2(0f, -5f));
            Button restartBtn = restartGo.AddComponent<Button>();
            restartBtn.targetGraphic = restartGo.GetComponent<Image>();

            if (refreshSprite != null)
            {
                GameObject rIcon = CreateImageObject(restartGo.transform, "Icon", refreshSprite,
                    TealIconColor, new Vector2(0f, 1f), new Vector2(44f, 44f), preserveAspect: true, raycastTarget: false);
                Shadow rSh = rIcon.AddComponent<Shadow>();
                rSh.effectColor = new Color(1f, 1f, 1f, 0.65f);
                rSh.effectDistance = new Vector2(0f, -2f);
            }

            GameObject levelBadge = CreateImageObject(topBarGo.transform, "LevelBadge", cardFrameSprite,
                Color.white, new Vector2(126f, -158f), new Vector2(198f, 74f), preserveAspect: false, raycastTarget: false);
            RectTransform lvlBadgeRt = levelBadge.GetComponent<RectTransform>();
            lvlBadgeRt.anchorMin = new Vector2(0f, 1f);
            lvlBadgeRt.anchorMax = new Vector2(0f, 1f);
            AddCardDropShadow(levelBadge, new Vector2(0f, -4f));

            Text levelText = CreateStyledText(levelBadge.transform, "LevelText", "LEVEL 1", font, 28,
                InkDarkColor, Vector2.zero, new Vector2(168f, 46f),
                new Color(1f, 1f, 1f, 0.85f), new Vector2(1.5f, -1.5f),
                new Color(0f, 0f, 0f, 0.14f), new Vector2(0f, -2f),
                bestFit: true, minSize: 18, maxSize: 28);

            // --- SAĞ KANAT: Süre Kapsülü + Kalan Kutu Rozeti (Aynı Turkuaz-Krem 2.5D Stil) ---
            GameObject timerBadge = CreateImageObject(topBarGo.transform, "TimerBadge", cardFrameSprite,
                Color.white, new Vector2(-126f, -66f), new Vector2(198f, 76f), preserveAspect: false, raycastTarget: false);
            RectTransform tmrBadgeRt = timerBadge.GetComponent<RectTransform>();
            tmrBadgeRt.anchorMin = new Vector2(1f, 1f);
            tmrBadgeRt.anchorMax = new Vector2(1f, 1f);
            AddCardDropShadow(timerBadge, new Vector2(0f, -4f));

            if (stopwatchSprite != null)
            {
                CreateImageObject(timerBadge.transform, "Icon", stopwatchSprite,
                    TealIconColor, new Vector2(-56f, 0f), new Vector2(32f, 32f), preserveAspect: true, raycastTarget: false);
            }

            Text timerText = CreateStyledText(timerBadge.transform, "TimerText", "02:00", font, 28,
                InkDarkColor, new Vector2(stopwatchSprite != null ? 18f : 0f, 0f), new Vector2(116f, 44f),
                new Color(1f, 1f, 1f, 0.85f), new Vector2(1.5f, -1.5f),
                new Color(0f, 0f, 0f, 0.14f), new Vector2(0f, -2f),
                bestFit: true, minSize: 18, maxSize: 28);

            GameObject boxesBadge = CreateImageObject(topBarGo.transform, "BoxesLeftBadge", cardFrameSprite,
                Color.white, new Vector2(-126f, -158f), new Vector2(198f, 72f), preserveAspect: false, raycastTarget: false);
            RectTransform bxBadgeRt = boxesBadge.GetComponent<RectTransform>();
            bxBadgeRt.anchorMin = new Vector2(1f, 1f);
            bxBadgeRt.anchorMax = new Vector2(1f, 1f);
            AddCardDropShadow(boxesBadge, new Vector2(0f, -4f));

            Text boxesLeftText = CreateStyledText(boxesBadge.transform, "BoxesLeftText", "KUTU: 8/8", font, 24,
                InkDarkColor, Vector2.zero, new Vector2(168f, 44f),
                new Color(1f, 1f, 1f, 0.85f), new Vector2(1.5f, -1.5f),
                new Color(0f, 0f, 0f, 0.14f), new Vector2(0f, -2f),
                bestFit: true, minSize: 16, maxSize: 24);

            // --- ORTA ALT ŞERİT: RectMask2D Tabanlı Kesin Çalışan İlerleme Barı ---
            const float barWidth = 410f;
            const float barHeight = 40f;
            GameObject progGo = CreateImageObject(topBarGo.transform, "ProgressBar", barBgSprite,
                Color.white, new Vector2(0f, -236f), new Vector2(barWidth, barHeight), preserveAspect: false, raycastTarget: false);
            RectTransform progRt = progGo.GetComponent<RectTransform>();
            progRt.anchorMin = new Vector2(0.5f, 1f);
            progRt.anchorMax = new Vector2(0.5f, 1f);
            AddCardDropShadow(progGo, new Vector2(0f, -3f));

            // İç Dolum Alanı (Padding ile çerçeve içinde kalır)
            GameObject trackArea = new GameObject("FillTrack", typeof(RectTransform));
            trackArea.transform.SetParent(progGo.transform, false);
            RectTransform trackRt = trackArea.GetComponent<RectTransform>();
            trackRt.anchorMin = Vector2.zero;
            trackRt.anchorMax = Vector2.one;
            trackRt.offsetMin = new Vector2(11f, 7f);
            trackRt.offsetMax = new Vector2(-11f, -7f);

            // RectMask2D ile %0'dan %100'e kusursuz kırpılan Viewport
            GameObject fillViewport = new GameObject("FillViewport", typeof(RectTransform), typeof(RectMask2D));
            fillViewport.transform.SetParent(trackArea.transform, false);
            RectTransform vpRt = fillViewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = new Vector2(0f, 1f); // Başlangıçta %0 boş
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;

            // Sabit genişlikteki dolum görseli (Viewport daralıp genişledikçe asla ezilmez)
            float fullTrackWidth = barWidth - 22f;
            float fullTrackHeight = barHeight - 14f;
            GameObject fillGo = CreateImageObject(fillViewport.transform, "Fill", barFillSprite,
                new Color(0.45f, 0.95f, 0.85f, 1f), Vector2.zero, new Vector2(fullTrackWidth, fullTrackHeight), preserveAspect: false, raycastTarget: false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0.5f);
            fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;

            Text progText = CreateStyledText(progGo.transform, "ProgressText", "0 / 32", font, 21,
                Color.white, Vector2.zero, new Vector2(220f, 34f),
                new Color(0.03f, 0.22f, 0.28f, 1f), new Vector2(2f, -2f),
                new Color(0f, 0f, 0f, 0.60f), new Vector2(0f, -2f),
                bestFit: true, minSize: 16, maxSize: 22);

            // =========================================================================
            // 3. ALT HUD (Aynı Turkuaz-Krem 2.5D Durum Kapsülü + İlk Açılış İpucu)
            // =========================================================================
            GameObject bottomGo = new GameObject("HUD_Bottom", typeof(RectTransform));
            bottomGo.transform.SetParent(canvasTr, false);
            bottomGo.transform.SetSiblingIndex(2);
            RectTransform botRt = bottomGo.GetComponent<RectTransform>();
            botRt.anchorMin = new Vector2(0f, 0f);
            botRt.anchorMax = new Vector2(1f, 0f);
            botRt.pivot = new Vector2(0.5f, 0f);
            botRt.anchoredPosition = Vector2.zero;
            botRt.sizeDelta = new Vector2(0f, 250f);

            GameObject statusPill = CreateImageObject(bottomGo.transform, "BottomStatusPill", cardFrameSprite,
                Color.white, new Vector2(0f, 76f), new Vector2(610f, 88f), preserveAspect: false, raycastTarget: false);
            RectTransform stRt = statusPill.GetComponent<RectTransform>();
            stRt.anchorMin = new Vector2(0.5f, 0f);
            stRt.anchorMax = new Vector2(0.5f, 0f);
            AddCardDropShadow(statusPill, new Vector2(0f, -5f));

            if (targetSprite != null)
            {
                CreateImageObject(statusPill.transform, "Icon", targetSprite,
                    TealIconColor, new Vector2(-240f, 0f), new Vector2(36f, 36f), preserveAspect: true, raycastTarget: false);
            }

            Text statusText = CreateStyledText(statusPill.transform, "StatusText", "Boş Delik: 2   •   Kalan Vida: 32", font, 26,
                InkDarkColor, new Vector2(targetSprite != null ? 18f : 0f, 0f), new Vector2(480f, 56f),
                new Color(1f, 1f, 1f, 0.75f), new Vector2(1.5f, -1.5f),
                new Color(0f, 0f, 0f, 0.12f), new Vector2(0f, -2f),
                bestFit: true, minSize: 18, maxSize: 27);

            GameObject hintPill = CreateImageObject(bottomGo.transform, "OnboardingHint", GetSprite(popups, "popups_4"),
                new Color(0.07f, 0.17f, 0.32f, 0.88f), new Vector2(0f, 178f), new Vector2(540f, 62f), preserveAspect: false, raycastTarget: false);
            RectTransform hintRt = hintPill.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0.5f, 0f);
            hintRt.anchorMax = new Vector2(0.5f, 0f);
            CanvasGroup hintCg = hintPill.AddComponent<CanvasGroup>();
            hintCg.alpha = 1f;

            if (cursorSprite != null)
            {
                CreateImageObject(hintPill.transform, "Icon", cursorSprite,
                    new Color(0.45f, 0.90f, 1f, 1f), new Vector2(-216f, 0f), new Vector2(30f, 30f), preserveAspect: true, raycastTarget: false);
            }

            CreateStyledText(hintPill.transform, "HintText", "Sürükle: Döndür   •   Tıkla: Vida Sök", font, 23,
                Color.white, new Vector2(cursorSprite != null ? 16f : 0f, 0f), new Vector2(440f, 46f),
                new Color(0.03f, 0.08f, 0.18f, 0.9f), new Vector2(2f, -2f),
                new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, -2f),
                bestFit: true, minSize: 17, maxSize: 24);

            // VictoryPanel ve DefeatPanel her zaman en önde (son kardeşler) kalsın
            Transform vicTr = canvasTr.Find("VictoryPanel");
            Transform defTr = canvasTr.Find("DefeatPanel");
            if (vicTr != null) vicTr.SetAsLastSibling();
            if (defTr != null) defTr.SetAsLastSibling();

            // =========================================================================
            // 4. GAME UI CONTROLLER REFERANSLARINI BAĞLA
            // =========================================================================
            SerializedObject uiSo = new SerializedObject(uiController);
            uiSo.FindProperty("_levelText").objectReferenceValue = levelText;
            uiSo.FindProperty("_timerText").objectReferenceValue = timerText;
            uiSo.FindProperty("_restartButton").objectReferenceValue = restartBtn;
            uiSo.FindProperty("_progressFillViewport").objectReferenceValue = vpRt;
            uiSo.FindProperty("_progressFillImage").objectReferenceValue = fillGo.GetComponent<Image>();
            uiSo.FindProperty("_progressText").objectReferenceValue = progText;
            uiSo.FindProperty("_boxesLeftText").objectReferenceValue = boxesLeftText;
            uiSo.FindProperty("_bottomStatusBg").objectReferenceValue = statusPill.GetComponent<Image>();
            uiSo.FindProperty("_bottomStatusText").objectReferenceValue = statusText;
            uiSo.FindProperty("_onboardingHintGroup").objectReferenceValue = hintCg;
            uiSo.ApplyModifiedProperties();

            EditorUtility.SetDirty(uiController);
            EditorSceneManager.MarkSceneDirty(uiController.gameObject.scene);
            EditorSceneManager.SaveScene(uiController.gameObject.scene);

            Debug.Log("<color=#00FF88><b>[InGameHUDDesigner]</b> Uyumlu 2.5D Turkuaz-Krem HUD tasarımı, kutu raf yuvaları ve RectMask2D ilerleme barı kuruldu!</color>");
            if (!silent)
            {
                EditorUtility.DisplayDialog("Başarılı!",
                    "Ana Oyun HUD tasarımı tek renk ailesinde (Turkuaz-Krem 2.5D) yenilendi!\n\n" +
                    "• 3 kutu arkasına raf yuvaları eklendi\n" +
                    "• İlerleme barı RectMask2D ile %0'dan %100'e pürüzsüz çalışacak şekilde kuruldu\n" +
                    "• Restart, Level, Süre ve Kalan Kutu göstergeleri aynı 2.5D stilde birleştirildi", "Harika!");
            }
        }

        private static void CreateBoxDockSlots(Transform topBarParent, Sprite slotSprite)
        {
            // Varsayılan pozisyonlar (1080x1920 üst orta bölge)
            Vector2[] dockCoords = new Vector2[]
            {
                new Vector2(-144f, -116f),
                new Vector2(0f, -116f),
                new Vector2(144f, -116f)
            };

            // Eğer sahnede BoxQueueManager ve kamera varsa kutuların 1080x1920 referans ekran X/Y merkezlerini hesapla
            Camera cam = Camera.main;
            BoxQueueManager queueMgr = Object.FindFirstObjectByType<BoxQueueManager>();
            if (cam != null && queueMgr != null)
            {
                float halfTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                for (int i = 0; i < 3; i++)
                {
                    Transform slot = queueMgr.transform.Find($"BoxSlot_{i}");
                    if (slot != null)
                    {
                        Vector3 rel = slot.position - cam.transform.position;
                        float z = Vector3.Dot(rel, cam.transform.forward);
                        if (z > 0.1f)
                        {
                            float halfH = z * halfTan;
                            float halfW = halfH * (1080f / 1920f);
                            float vx = Vector3.Dot(rel, cam.transform.right) / halfW;
                            float vy = Vector3.Dot(rel, cam.transform.up) / halfH;
                            dockCoords[i] = new Vector2(vx * 540f, (vy - 1f) * 960f);
                        }
                    }
                }
            }

            for (int i = 0; i < 3; i++)
            {
                GameObject pocket = CreateImageObject(topBarParent, $"BoxDockSlot_{i}", slotSprite,
                    new Color(0.08f, 0.22f, 0.34f, 0.72f), dockCoords[i], new Vector2(138f, 138f), preserveAspect: false, raycastTarget: false);
                RectTransform pRt = pocket.GetComponent<RectTransform>();
                pRt.anchorMin = new Vector2(0.5f, 1f);
                pRt.anchorMax = new Vector2(0.5f, 1f);
            }
        }

        private static void AddCardDropShadow(GameObject go, Vector2 offset)
        {
            Shadow sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0.03f, 0.10f, 0.20f, 0.45f);
            sh.effectDistance = offset;
        }

        private static Sprite EnsureBgGradientSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(BgGradientAssetPath);
            if (existing != null) return existing;

            string fullDir = Path.GetDirectoryName(Path.GetFullPath(BgGradientAssetPath));
            if (!string.IsNullOrEmpty(fullDir) && !Directory.Exists(fullDir))
            {
                Directory.CreateDirectory(fullDir);
            }

            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color centerColor = new Color(0.39f, 0.69f, 1.00f, 1f);
            Color midColor = new Color(0.24f, 0.50f, 0.90f, 1f);
            Color edgeColor = new Color(0.13f, 0.31f, 0.68f, 1f);

            Vector2 center = new Vector2(0.5f, 0.55f);
            for (int y = 0; y < size; y++)
            {
                float ny = (float)y / (size - 1);
                for (int x = 0; x < size; x++)
                {
                    float nx = (float)x / (size - 1);
                    float dist = Vector2.Distance(new Vector2(nx, ny), center) / 0.78f;
                    dist = Mathf.Clamp01(dist);

                    Color c = dist < 0.5f
                        ? Color.Lerp(centerColor, midColor, dist * 2f)
                        : Color.Lerp(midColor, edgeColor, (dist - 0.5f) * 2f);

                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();

            File.WriteAllBytes(BgGradientAssetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(BgGradientAssetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(BgGradientAssetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(BgGradientAssetPath);
        }

        private static GameObject CreateImageObject(Transform parent, string name, Sprite sprite, Color tint, Vector2 anchoredPos, Vector2 sizeDelta, bool preserveAspect, bool raycastTarget)
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
            img.raycastTarget = raycastTarget;
            return go;
        }

        private static Text CreateStyledText(
            Transform parent, string name, string content, Font font, int fontSize, Color color,
            Vector2 anchoredPos, Vector2 sizeDelta,
            Color outlineColor, Vector2 outlineDist,
            Color shadowColor, Vector2 shadowDist,
            bool bestFit = false, int minSize = 16, int maxSize = 40)
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
            txt.raycastTarget = false;
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
