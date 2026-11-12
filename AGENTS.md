# ZORUNLU ÇALIŞMA VE ARAÇ KULLANIM KURALLARI (CRITICAL DIRECTIVES)

1. **TARAMA YASAĞI (NO BLIND SCANNING):** Kullanıcı açıkça talep etmedikçe geniş kapsamlı dosya/klasör taraması (`list_dir`, `find`, `grep`, terminal komutları vb.) YAPMA. Gerekli dosya ve sistem yolları aşağıdaki **CANLI HAFIZA & DOSYA HARİTASI**nda kayıtlıdır.
2. **DOĞRUDAN VE KISMİ ERİŞİM:** Bir işlem yaparken haritadaki doğrudan dosya yolunu kullan. Dosyaları baştan sona bütünüyle okuma; yalnızca ilgili fonksiyon ve satır aralığına (`StartLine`, `EndLine`) odaklan.
3. **OTOMATİK HAFIZA KAYDI (ZORUNLU):** Herhangi bir dosyada analiz, düzeltme veya yeni geliştirme yaptığında; konuşmayı tamamlamadan önce o dosyanın işlevini, kritik metotlarını ve çalışma mantığını 1-2 cümleyle **CANLI HAFIZA** tablosuna işle/güncelle. Böylece gelecekte aynı dosyaları tekrar tekrar okuyarak token israfı yapılmaz.
4. **YASAKLI DİZİNLER:** Hiçbir koşulda derleme ve önbellek klasörlerini (`Library/`, `Temp/`, `obj/`, `bin/`, `Build/`, `Logs/`, `.git/`, `.vs/`) okuma veya tarama.
5. **ÖZ VE TEKNİK İLETİŞİM:** Yanıtları doğrudan, net ve kod odaklı tut; gereksiz tekrarlardan ve tam dosya dökümlerinden kaçın.

---

# 1. PROJE ÖZETİ
- **Proje Adı:** Screw Solver Game (3D / Fizik Tabanlı Vida & Somun Bulmaca Oyunu)
- **Oyun Motoru:** Unity 6 (6000.3.27f2)
- **Render Pipeline:** Universal Render Pipeline (URP 17.3.0)
- **Girdi Sistemi:** Unity New Input System (com.unity.inputsystem 1.20.1)
- **Hedef Platform:** Mobile / Standalone / WebGL

---

# 2. CANLI HAFIZA & DOSYA HARİTASI (LIVING MEMORY)
*(Bu bölüm her analiz ve yeni geliştirmeden sonra yapay zekâ tarafından otomatik olarak güncellenir)*

### Proje Yapısı & Konfigürasyon
- `.gitignore` : Unity 6, URP, IDE ve sistem önbelleklerini (Library, Temp, obj vb.) hariç tutan Git kuralları.
- `TECHNICAL_PLAN.md` : Tartışma sonucu netleşen 5 temel mimari karar, SOLID sınıf yapısı ve faz planı.
- `Assets/Plugins/Demigiant/DOTween/` : DOTween animasyon kütüphanesi ve modülleri.
- `Assets/Prefabs/FBX/Screw+Phillips.fbx` : Vida 3D modeli (Phillips başlık, dişli mil).
- `Assets/Prefabs/FBX/TargetBox.fbx` : Hedef kutu / soket tabanı 3D modeli.
- `ProjectSettings/ProjectSettings.asset` : Unity proje, oynatıcı ve platform ayarları.
- `Packages/manifest.json` : Projeye dahil olan Unity paket bağımlılıkları (URP, Input System vb.).
- `Assets/Scenes/SampleScene.unity` : Varsayılan çalışma sahnesi.
- `Assets/InputSystem_Actions.inputactions` : Yeni Input System girdi haritaları ve eylemleri.

### Oynanış Çekirdeği (Gameplay)
- `Assets/_Project/Scripts/Core/ColorType.cs` : Renk tanımları ve URP RGBA renk dönüştürücüleri (`ToUnityColor()`).
- `Assets/_Project/Scripts/Config/ScrewAnimationConfigSO.cs` : DOTween süreleri, kavis yüksekliği ve hız çarpanını yöneten ayar ScriptableObject'i.
- `Assets/_Project/Scripts/Gameplay/Camera/MobileCameraFitter.cs` : 1080x1920 (9:16) mobil dikey ekran koruyucusu; farklı ekran oranlarında FOV'u dinamik hesaplar.
- `Assets/_Project/Scripts/Gameplay/Plates/PlateObject.cs` : Sökülebilir parça/plaka mantığı; bağlı vidalar sökülünce isKinematic=false, `AttachScrew` ile dinamik vida takibini sağlama, altındaki tüm collider'ları kapatarak Concave Mesh Collider hatasını önleme, PuzzleRoot'tan ayrılarak doğal torkla serbest düşüş ve altındaki vidaları (`_coveredScrews`) otomatik açma.
- `Assets/_Project/Scripts/Gameplay/Holes/PuzzleHole.cs` : Model veya plakalar üzerindeki interaktif boş vida delikleri; vidanın başlığının delik puluna (HoleBezel) kusursuz oturması ve asla siyah diskin arkasında kaybolmaması için modelin dünya ölçeğini (`lossyScale.z`) hesaba katan milimetrik `SnapPosition` (0.105f local offset) hesaplar, `_originalScale = Vector3.one` güvencesiyle siyah çerçevelerin yok olmasını önler, boştaki vidaları kabul eder, nabız atarak hedef gösterir (`SetHighlight`), vida takıldığında tatlı yaylanma (`PlaySnapPunch`) oynatır.
- `Assets/_Project/Scripts/Gameplay/Plates/ScrewHole.cs` : Plaka/gövde üzerindeki vida delik soketi; procedural çift yüzlü pul mesh ve `HoleDark.mat` kullanımı.
- `Assets/_Project/Scripts/Gameplay/Screws/ScrewObject.cs` : 3D vida nesnesi; kilit engel kontrolü (`_blockingCount`), vida delikten kalktığı an bağlı plakaya haber verip otomatik düşüşü tetikleme (`OnScrewRemoved`), PuzzleRoot'a bağlanıp local süzülerek küple 360° senkron dönme, eski deliği Vacate ederek boş deliğe taşınma (`PlayMoveToHoleAnimation`).
- `Assets/_Project/Scripts/Gameplay/Controllers/PuzzleRotator.cs` : Ortadaki bulmaca modelini (`PuzzleRoot`) parmak veya fare sürüklemesiyle kameraya göre 360° serbestçe döndüren, tatlı ataletli (inertia) hafif kontrolcü.
- `Assets/_Project/Scripts/Gameplay/Boxes/ColorBox.cs` : Manuel soket (Slot_1, Slot_2...) yönetimi; gölge düşürmeyi otomatik kapatan (ShadowCastingMode.Off), kapasiteyi soket sayısına eşitleyen ve deliklerin tam konumlandırılması için hayalet vida önizlemesi (Ghost Screw Preview) çizen hedef kutu bileşeni.
- `Assets/_Project/Scripts/Gameplay/Boxes/BoxQueueManager.cs` : Seviye kutu kuyruğunu (örn: 32 vida için 8 kutu) ekrandaki 3 sabit slota sırayla akıtan, `AutoPopulateQueueFromScrews` ile sahnedeki tüm vidaları tarayıp kilitlenmeyen akıllı kuyruk üreten, `OnDrawGizmos` ve `SpawnEditorPreviews` ile Edit Mode'da 3 kutuyu Scene ekranında görünür/taşınabilir kılan, `CleanSlot` ile önizlemeleri runtime'da temizleyen ve `UpdateActiveBoxes` senkronizasyonlu yöneticisi.
- `Assets/_Project/Scripts/Gameplay/Slots/WaitingSlotManager.cs` : Ekranda uygun renk kutu yokken vidaların geçici park ettiği 5 adet bekleme yuvasını yönetir; yeni uygun kutu indiğinde vidaları otomatik aktarır, delikler dolup geçerli hamle kalmadığında Game Over tetikler.
- `Assets/_Project/Scripts/Gameplay/Controllers/ScrewManager.cs` : Dokunulan vidayı veya boş deliği yakalayan ana akış köprüsü; seçili bir vida varken dolu bir deliğe veya başka bir vidaya tıklandığında seçimi iptal edip vidayı kendi orijinal deliğine geri döndürür, sadece geçerli boş deliğe yerleştirmeye izin verir, Deadlock durumunda Game Over tetikler.
- `Assets/_Project/Scripts/Editor/ScrewHoleTool.cs` : `Screw.prefab` ve sahnedeki tüm vidalara otomatik olarak URP Unlit simsiyah ve vidanın başlığına/yüzeyine tam paralel (localPosition = (0, 0, 0.107f)) vida yuvalarını (`HoleBezel`) yerleştiren editör aracı (`SetupAllHoles()`, `SetupHoleForScrew()`).
- `Assets/_Project/Scripts/Editor/SurfaceScrewSnapperTool.cs` : Küre, silindir, kavisli ve organik tüm 3D modellerde tıklanan noktanın yüzey normalini (`hit.normal`) hesaplayarak vidayı yüzeye tam 90° dik açıyla oturtan editör aracı.
- `Assets/_Project/Scripts/Editor/LevelsGeneratorTool.cs` : 8 farklı eşsiz 3D seviyeyi (Küp, Silindir, Haç, Altıgen Kule, Piramit, Türbin, L-Blok, Zırhlı Kasa) tam 4'ün katı vida sayılarıyla (%100 kusursuz kutu akışı) üreten ve `LevelManager`'a bağlayan seviye üreticisi (`Ctrl+Shift+L`).
- `Assets/_Project/Scripts/Editor/Puzzle3DBuilder.cs` : Gerçek muadillerdeki gibi (Screw Out 3D) 360° her yöne döndürülebilen, 5 cepheli, çok katmanlı kilitli (L-Braket, Çapraz Şerit, Köprü, Kelepçe), düşen fizik plakalı, 32 vidaya 8 kutu akıtan dinamik kuyruğu (`HUD_Boxes`), 5 bekleme yuvasını (`HUD_WaitingSlots`) ve tam teşekküllü arayüzü (`UI_Canvas`) tek tıkla kuran editör aracı (`Ctrl+Shift+M`).

### UI & Seviye Yönetimi (UI & Level Management)
- `Assets/_Project/Scripts/UI/GameUIController.cs` : Oyun içi HUD (Seviye No, süre, yeniden başlat butonu), `EnsureRuntimePanelsExist()` + `AutoBindUI()`, `BoxQueueManager` & `ScrewManager` çift emniyetli dinleyicisi, 3 yıldız animasyonlu Zafer Popup'ı (Victory) ve Yenilgi Popup'ını (Defeat) DOTween ile yönetir. Kullanıcının manuel UI tasarımına müdahale etmez.
- `Assets/_Project/Scripts/Gameplay/Level/LevelManager.cs` : Seviye Prefab'larını (`Level_X.prefab`) sırayla `PuzzleRoot` içerisine yükleyen, kuyruğu ve yuvaları otomatik sıfırlayan, PageUp (Sonraki Seviye) ve PageDown (Önceki Seviye) kısayolları ile anında seviye atlamayı sağlayan modüler seviye yöneticisi.

---

# 3. ÖĞRENİLEN MİMARİ NOTLAR (QUICK CACHE)
- Proje Unity 6 ve URP ile çalışmaktadır.
- Blocker Detection: Runtime PhysX raycast yerine integer bağımlılık sayacı (`blockingCount`) kullanılır (0 GC, 0 PhysX maliyeti).
- Fizik Stratejisi: Plakalar düşene kadar `isKinematic = true` uyku modundadır; yalnızca tüm vidaları sökülen plaka anlık serbest düşer.
- Akış & Rezervasyon: Tıklama anında mantıksal slot rezervasyonu yapılır (0 gecikme), görsel animasyonlar maks 3 eşzamanlı kuyrukla yürütülür.
- Animasyonlar: DOTween altyapısı ve `ScrewAnimationConfigSO` ile dinamik hız ayarı.
- Çözülebilirlik: Color Sort Game benzeri headless simulator bot ile seviye optimizasyonu.

