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
*(Geliştirildikçe buraya işlenecektir: Vida çıkarma mekaniği, delik/yuva eşleştirme, renk ve katman kontrolü)*

### UI & Seviye Yönetimi (UI & Level Management)
*(Geliştirildikçe buraya işlenecektir: Seviye yükleyici, can/hamle sayacı, başarı popupları)*

---

# 3. ÖĞRENİLEN MİMARİ NOTLAR (QUICK CACHE)
- Proje Unity 6 ve URP ile çalışmaktadır.
- Blocker Detection: Runtime PhysX raycast yerine integer bağımlılık sayacı (`blockingCount`) kullanılır (0 GC, 0 PhysX maliyeti).
- Fizik Stratejisi: Plakalar düşene kadar `isKinematic = true` uyku modundadır; yalnızca tüm vidaları sökülen plaka anlık serbest düşer.
- Akış & Rezervasyon: Tıklama anında mantıksal slot rezervasyonu yapılır (0 gecikme), görsel animasyonlar maks 3 eşzamanlı kuyrukla yürütülür.
- Animasyonlar: DOTween altyapısı ve `ScrewAnimationConfigSO` ile dinamik hız ayarı.
- Çözülebilirlik: Color Sort Game benzeri headless simulator bot ile seviye optimizasyonu.

