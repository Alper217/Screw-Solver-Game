# 3D Katmanlı Vida Bulmacası ("Screw Jam" Tarzı) - Kesinleşmiş Teknik Plan

Bu belge; Dream Games, Peak ve Rollic seviyesinde yüksek mobil performans, sıfır GC tahsisatı (0 GC Alloc), modüler SOLID mimari ve garantili çözülebilirlik standartlarıyla hazırlanmış ana teknik şartnamedir.

---

## 1. Mimari Genel Bakış ve Oyun Döngüsü

Oyun birbirine kenetlenmiş 3 temel katmandan oluşur:

1. **3D Uzaysal ve Plaka Katmanı:**
   - Renkli vidalarla birbirine tutturulmuş iç içe/katmanlı 3D mekanik modeller.
   - Bir plakanın üzerindeki tüm vidalar söküldüğünde plaka yerçekimiyle serbest düşüşe geçer.
2. **Hedef Kutu ve Kuyruk Sistemi:**
   - Üst kısımda bulunan ve her biri belirli renkten 3 vida alabilen 4 aktif hedef kutusu.
   - Arkada bekleyen ve kutu doldukça öne kayan kutu kuyruğu.
3. **Bekleme Barı (Buffer Dock):**
   - 4 aktif kutunun rengine uymayan vidaların toplandığı 5 slotlu geçici tutma barı.
   - 5 slot tamamen dolarsa ve eşleşme yapılamazsa seviye kaybedilir (`Game Over`).
   - Yeni bir kutu açıldığında, bekleme barındaki eşleşen vidalar otomatik olarak kutuya çekilir.

```
[ 3D Model: Katmanlar ve Vidalar ]
                 │ (Oyuncu Dokunuşu / Tıklama)
                 ▼
     [ blockingCount == 0 mı? ] ──► HAYIR ──► Wiggle/Shake Animasyonu (Kilitli!)
                 │ EVET
                 ▼
     [ Mantıksal Rezervasyon (0 Gecikme) ]
                 ├── Aktif Kutuya Rezerve Et
                 └── Bekleme Barına Rezerve Et (Doluysa -> Game Over)
                 │
                 ▼
     [ Görsel Sıralayıcı Kuyruğu (Maksimum 3 Eşzamanlı) ]
                 │ (DOTween Sequence: Gevşeme -> Yukarı Çıkış -> Parabolik Uçuş)
                 ▼
    [ Kutu 3/3 Doldu mu? ] ──► EVET ──► Kutu Kapanır, Kuyruk Kayar
                                          └── Bekleme Barından Otomatik Çekim Yapılır
```

---

## 2. Kesinleşen 5 Temel Tasarım Kararı

### Karar 1: Engel Algılama (Blocker Detection) -> Integer Bağımlılık Sayacı
* **Mekanizma:** Oyun anında PhysX Raycast/SphereCast **kullanılmaz**. Vidanın üzerinde sadece bir tamsayı tutulur: `int blockingCount`.
* **Hazırlık:** Seviye Editörde kaydedilirken veya sahne ilk açılışında vidanın üstünde kalan plakalar tespit edilip sayaç belirlenir.
* **Oyun Anı (Runtime):** 
  - Oyuncu vidaya dokunduğunda: `if (screw.blockingCount == 0)` kontrol edilir (nanosaniye hızında, 0 GC, 0 Fizik yükü).
  - Eğer `blockingCount > 0` ise vida gevşemez; oyuncuya kilitli olduğunu hissettiren hafif bir **Wiggle/Shake** animasyonu oynatılır.
  - Bir plaka düştüğünde: Kendi altındaki vidalara haber verir (`screw.blockingCount--`) ve kilidi açılan vidalar anında tıklanabilir hale gelir.

### Karar 2: Mobil Performans & Fizik Uyku Stratejisi (Kinematic Physics)
* **Mekanizma:** Sahnede 50 plaka olsa dahi fizik motoru (PhysX) boşta çalıştırılmaz.
* **Fizik Uyku:** Tüm plakalar varsayılan olarak **`isKinematic = true`** durumundadır.
* **Anlık Düşüş:** Yalnızca üzerindeki tüm vidalar sökülen (`assignedScrewsCount == 0`) plaka anlık olarak **`isKinematic = false`** yapılır ve diğer plakalarla gereksiz temas hesabı yapmaması için düşüş katmanına (`FallingPlatesLayer`) alınır.
* **Temizlik:** Ekran altına ulaştığı anda plakanın fiziği tamamen kapatılır veya obje havuza gönderilir. Sahnede aynı anda en fazla 1-2 düşen plaka fizik tüketir.

### Karar 3: Akıcı Oynanış & Mantıksal Rezervasyon (Maks 3 Eşzamanlı İşlem)
* **Mekanizma:** Hızlı dokunuşlarda oyun akıcılığını korumak ve yarış durumlarını (Race Condition) önlemek için mantık ile görsel tamamen ayrılır.
* **Mantıksal Rezervasyon:** Oyuncu vidaya bastığı anda envanter modelinde slot anında rezerve edilir (Kutuya mı gidecek, bekleme barına mı, yoksa doluluktan yenilgi mi?). Vidanın collider'ı anında kapatılır.
* **Görsel Kuyruk:** Maksimum 3 vida aynı anda uçuş/sökülme sekansında olabilir. Oyuncu seri şekilde vidalara dokunsa bile vidalar sırayla, birbirini ezmeden ve taşma olmadan hedeflerine ulaşır.

### Karar 4: Animasyon Sistemi & Dinamik Hız Ayarı (DOTween + Config)
* **Kütüphane:** DOTween `Sequence` mimarisi.
* **Akış:** Delikten dönerek gevşeme (`DORotate`), hafif yukarı çekilme (`DOMove` local Y) ve parabolik kavisli uçuş (`DOJump` / `DOPath`).
* **Dinamik Ayar (`ScrewAnimationConfigSO`):**
  - `UnscrewDuration` (Vida delikten çıkış süresi)
  - `FlyToTargetDuration` (Uçuş süresi)
  - `ParabolicArcHeight` (Uçuş kavis yüksekliği)
  - `QueueShiftDuration` (Kutuların öne kayma süresi)
  - `GlobalSpeedMultiplier` (Kullanıcı geri bildirimlerine göre Inspector'dan anında tüm oyunu hızlandırıp yavaşlatabilen master çarpan)

### Karar 5: Seviye Çözülebilirlik Doğrulayıcısı (Headless AI Solver)
* **Mekanizma:** `Color Sort Game` projesindeki `HeadlessLevelBot` prensibi baz alınır.
* **İleri Yönlü Bot:** Tasarımcı 3D modeli özgürce kurar. Editör penceresindeki bot, vidaların bağımlılık grafını ve bekleme barını saniyeler içinde binlerce kez simüle eder.
* **Otomatik Dengeleme (Auto-Sort Queue):** Seviyede kilitlenme (deadlock) ihtimali varsa, bot kutu renklerinin sırasını optimize ederek seviyeyi tek tıkla **%100 Çözülebilir** hale getirir.

---

## 3. SOLID Bileşen Mimarisi

* **`GameManager`:** Seviye durumunu (`Loading`, `Playing`, `Win`, `Lose`, `Paused`) yöneten durum makinesi.
* **`LevelController`:** Seviye verisinden (`LevelDataSO`) 3D plaka ve vida hiyerarşisini oluşturan, bağımlılıkları bağlayan yönetici.
* **`ScrewObject` (`MonoBehaviour`):** Tek bir vidayı temsil eder. Rengi (`ColorType`), `blockingCount` sayacı, tıklama girdi dinleyicisi ve DOTween animasyon kancalarını barındırır.
* **`PlateObject` (`MonoBehaviour`):** 3D plakayı temsil eder. `assignedScrewsCount` sayacını tutar. Sayı 0 olduğunda `isKinematic = false` yaparak serbest düşüşü başlatır.
* **`PuzzleInventoryModel` (Pure C#):** Görselden bağımsız beyin. 4 aktif kutuyu, 5'li bekleme barını, rezervasyonları ve otomatik birleşmeleri yönetir. Birim testlerine (Unit Test) tamamen uygundur.
* **`ScrewAnimationDispatcher`:** Maksimum 3 eşzamanlı görsel hareketi yöneten FIFO animasyon kuyruğu.
* **`ScrewAnimationConfigSO` (`ScriptableObject`):** Tüm animasyon sürelerini ve çarpanlarını içeren merkezi ayar dosyası.

---

## 4. Uygulama Yol Haritası (Fazlar)

* **Faz 1: Çekirdek Mantık ve Veri Yapıları (Saf C# Envanter & Model)**
* **Faz 2: 3D Görsel Nesneler, Girdi ve DOTween Entegrasyonu**
* **Faz 3: Kinematic Plaka Fiziği ve Görsel Sıralayıcı (3 Eşzamanlı İşlem)**
* **Faz 4: UI/HUD (Kutular, Bekleme Barı, İlerleme) ve Juicy Geri Bildirimler**
* **Faz 5: Seviye Editör Aracı ve Headless Çözülebilirlik Simülatörü**

