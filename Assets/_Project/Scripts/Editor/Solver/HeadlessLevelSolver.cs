using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrewSolver.Core;
using ScrewSolver.Gameplay.Level;
using ScrewSolver.Gameplay.Plates;
using ScrewSolver.Gameplay.Screws;
using UnityEditor;
using UnityEngine;

namespace ScrewSolver.Editor.Solver
{
    /// <summary>
    /// Unity fiziğinden bağımsız çalışan Saf C# Headless Seviye Doğrulayıcı ve Renk/Kutu Optimize Edici.
    /// Oyunun gerçek kurallarını birebir simüle eder:
    /// - 3 aktif hedef kutu slotu (her biri 4 vida kapasiteli)
    /// - Başlangıçta 0 ekstra boş delik (veya sahnedeki mevcut boş delikler)
    /// - Çok katmanlı plaka bağımlılıkları (plaka düşünce altındaki vidalar açılır ve plaka üzerindeki boş delikler yok olur)
    /// - Vidası sökülen çok vidalı plakalarda dinamik boş delik (PuzzleHole) açılması ve stratejik vida transferi
    /// </summary>
    public static class HeadlessLevelSolver
    {
        public sealed class PlateNode
        {
            public int Index;
            public string Name;
            public PlateObject PlateRef;
            public readonly List<int> InitialScrewIndices = new();
            public readonly List<int> CoveredScrewIndices = new();
        }

        public sealed class ScrewNode
        {
            public int Index;
            public string Name;
            public ScrewObject ScrewRef;
            public ColorType Color;
            public int ParentPlateIndex; // -1 ise gövde üzerinde (düşmez)
            public readonly List<int> BlockingPlateIndices = new();
            public int DepthLayer;
        }

        public sealed class LevelGraph
        {
            public readonly List<PlateNode> Plates = new();
            public readonly List<ScrewNode> Screws = new();
            public readonly Dictionary<ColorType, int> ColorCounts = new();
            public readonly List<string> ValidationIssues = new();

            public int TotalScrews => Screws.Count;
            public int RequiredBoxes => TotalScrews / 4;
            public bool HasValidMultipleOfFour => TotalScrews > 0 && (TotalScrews % 4 == 0);
        }

        public sealed class SolutionStepInfo
        {
            public int StepNumber;
            public bool IsTransfer;
            public int ScrewIndex;
            public string ScrewName;
            public ColorType ScrewColor;
            public int SourcePlateIndex;       // -1 ise sabit gövde
            public int TargetSlotOrPlateIndex; // Direct ise Kutu Slot (0..2), Transfer ise Plaka İndeksi
            public int DroppedPlateIndex = -1; // Bu adımda düşen plaka indeksi (-1 = yok)
            public string DroppedPlateName;
            public readonly int[] SlotColorIdsAfter = new int[ActiveSlotCount]; // -1 = boş
            public readonly int[] SlotNeedAfter = new int[ActiveSlotCount];
            public int ActiveEmptyHolesAfter;
            public string Description;
        }

        public sealed class SolveReport
        {
            public bool IsSolvable;
            public float MonteCarloWinRate; // 0..100
            public int SolutionSteps;
            public int TransfersUsedInSolution;
            public int MaxConcurrentHolesUsed;
            public int VisitedNodes;
            public string Summary;
            public readonly List<string> Trace = new();
            public readonly List<SolutionStepInfo> Steps = new();
            public readonly List<ColorType> TestedBoxQueue = new();
        }

        public sealed class OptimizationResult
        {
            public bool Success;
            public ColorType[] AssignedScrewColors;
            public List<ColorType> OptimizedBoxQueue = new();
            public SolveReport Report;
        }

        private const int BoxCapacity = 4;
        private const int ActiveSlotCount = 3;
        private static readonly Collider[] _overlapBuffer = new Collider[24];

        /// <summary>
        /// Sahnedeki veya prefab hiyerarşisindeki plaka, vida ve fiziksel çakışmaları analiz edip bağımlılık grafını kurar.
        /// Dışarıdan gelen FBX modellerinde veya yeni üretilen seviyelerde fiziksel engelleri otomatik tespit edip bağlayabilir.
        /// </summary>
        public static void AutoWirePhysicalDependencies(GameObject levelRoot, bool clearExistingBlockers = true)
        {
            if (levelRoot == null) return;

            PlateObject[] plates = levelRoot.GetComponentsInChildren<PlateObject>(true);
            ScrewObject[] screws = levelRoot.GetComponentsInChildren<ScrewObject>(true);

            // 1. Plaka -> Bağlı Vida referanslarını kur
            Dictionary<PlateObject, List<ScrewObject>> attachedMap = new();
            foreach (PlateObject p in plates)
            {
                if (p != null) attachedMap[p] = new List<ScrewObject>();
            }

            foreach (ScrewObject s in screws)
            {
                if (s == null) continue;
                PlateObject parentPlate = s.GetComponentInParent<PlateObject>();
                SerializedObject screwSo = new SerializedObject(s);
                screwSo.FindProperty("_parentPlate").objectReferenceValue = parentPlate;
                if (clearExistingBlockers)
                {
                    screwSo.FindProperty("_blockingPlates").arraySize = 0;
                }
                screwSo.ApplyModifiedProperties();

                if (parentPlate != null && attachedMap.TryGetValue(parentPlate, out var list))
                {
                    list.Add(s);
                }
            }

            Dictionary<PlateObject, HashSet<ScrewObject>> coveredMap = new();
            foreach (PlateObject p in plates)
            {
                if (p != null) coveredMap[p] = new HashSet<ScrewObject>();
            }

            if (!clearExistingBlockers)
            {
                foreach (ScrewObject s in screws)
                {
                    if (s == null || s.BlockingPlates == null) continue;
                    foreach (PlateObject bp in s.BlockingPlates)
                    {
                        if (bp != null && coveredMap.TryGetValue(bp, out var set))
                        {
                            set.Add(s);
                        }
                    }
                }
            }

            // 2. Fiziksel Çakışma Tespiti (ScrewObject.IsPhysicallyBlocked ile birebir aynı kapsül geometrisi)
            Physics.SyncTransforms();

            Dictionary<ScrewObject, HashSet<PlateObject>> screwBlockerSets = new();
            foreach (ScrewObject s in screws)
            {
                if (s == null) continue;
                var set = new HashSet<PlateObject>();
                if (!clearExistingBlockers && s.BlockingPlates != null)
                {
                    foreach (var existing in s.BlockingPlates)
                    {
                        if (existing != null && existing != s.ParentPlate) set.Add(existing);
                    }
                }

                float scaleZ = Mathf.Max(0.01f, s.transform.lossyScale.z);
                float scaleX = Mathf.Max(0.01f, s.transform.lossyScale.x);
                Vector3 p0 = s.transform.position + s.transform.forward * (0.145f * scaleZ);
                Vector3 p1 = s.transform.position + s.transform.forward * (0.45f * scaleZ);
                float radius = 0.025f * scaleX;

                // Sahne içinde aktifse PhysX OverlapCapsule + her durumda geometrik Collider.ClosestPoint/Bounds kontrolü
                if (s.gameObject.scene.IsValid())
                {
                    int hitCount = Physics.OverlapCapsuleNonAlloc(p0, p1, radius, _overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < hitCount; i++)
                    {
                        Collider col = _overlapBuffer[i];
                        if (col == null) continue;
                        PlateObject hitPlate = col.GetComponentInParent<PlateObject>();
                        if (hitPlate == null || hitPlate == s.ParentPlate) continue;
                        if (hitPlate.transform.IsChildOf(levelRoot.transform))
                        {
                            set.Add(hitPlate);
                        }
                    }
                }

                // Ayrıca örneklenmiş kapsül ekseni boyunca BoxCollider lokal uzay testi (prefab / pasif obje güvenliği)
                foreach (PlateObject candidatePlate in plates)
                {
                    if (candidatePlate == null || candidatePlate == s.ParentPlate) continue;
                    if (DoesSegmentIntersectPlateBox(p0, p1, radius, candidatePlate))
                    {
                        set.Add(candidatePlate);
                    }
                }

                screwBlockerSets[s] = set;
                foreach (PlateObject bp in set)
                {
                    if (coveredMap.TryGetValue(bp, out var covSet))
                    {
                        covSet.Add(s);
                    }
                }
            }

            // 3. SerializedObject ile tüm referansları temiz şekilde yaz
            foreach (ScrewObject s in screws)
            {
                if (s == null) continue;
                var set = screwBlockerSets[s];
                SerializedObject screwSo = new SerializedObject(s);
                SerializedProperty blockingProp = screwSo.FindProperty("_blockingPlates");
                blockingProp.arraySize = set.Count;
                int idx = 0;
                foreach (PlateObject bp in set)
                {
                    blockingProp.GetArrayElementAtIndex(idx++).objectReferenceValue = bp;
                }
                screwSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(s);
            }

            foreach (PlateObject p in plates)
            {
                if (p == null) continue;
                SerializedObject plateSo = new SerializedObject(p);

                List<ScrewObject> attached = attachedMap[p];
                SerializedProperty attProp = plateSo.FindProperty("_attachedScrews");
                attProp.arraySize = attached.Count;
                for (int i = 0; i < attached.Count; i++)
                {
                    attProp.GetArrayElementAtIndex(i).objectReferenceValue = attached[i];
                }

                HashSet<ScrewObject> covered = coveredMap[p];
                SerializedProperty covProp = plateSo.FindProperty("_coveredScrews");
                covProp.arraySize = covered.Count;
                int cIdx = 0;
                foreach (ScrewObject cs in covered)
                {
                    covProp.GetArrayElementAtIndex(cIdx++).objectReferenceValue = cs;
                }

                plateSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(p);
            }
        }

        private static bool DoesSegmentIntersectPlateBox(Vector3 worldP0, Vector3 worldP1, float radius, PlateObject plate)
        {
            Collider col = plate.GetComponent<Collider>();
            if (col is not BoxCollider box) return false;

            Transform t = box.transform;
            Vector3 lp0 = t.InverseTransformPoint(worldP0) - box.center;
            Vector3 lp1 = t.InverseTransformPoint(worldP1) - box.center;

            Vector3 half = box.size * 0.5f;
            Vector3 scale = t.lossyScale;
            float rx = radius / Mathf.Max(0.01f, Mathf.Abs(scale.x));
            float ry = radius / Mathf.Max(0.01f, Mathf.Abs(scale.y));
            float rz = radius / Mathf.Max(0.01f, Mathf.Abs(scale.z));

            half.x += rx;
            half.y += ry;
            half.z += rz;

            // 9 adım örneklemeli segment-AABB kesişimi (hem hızlı hem güvenli)
            const int samples = 9;
            for (int i = 0; i <= samples; i++)
            {
                float u = i / (float)samples;
                Vector3 pt = Vector3.Lerp(lp0, lp1, u);
                if (Mathf.Abs(pt.x) <= half.x && Mathf.Abs(pt.y) <= half.y && Mathf.Abs(pt.z) <= half.z)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Bir seviye kök objesinden bağımlılık grafını çıkarır ve temel tutarlılık kontrollerini yapar.
        /// </summary>
        public static LevelGraph ExtractGraph(GameObject levelRoot)
        {
            LevelGraph graph = new();
            if (levelRoot == null)
            {
                graph.ValidationIssues.Add("Seviye kök objesi null.");
                return graph;
            }

            PlateObject[] plates = levelRoot.GetComponentsInChildren<PlateObject>(true);
            ScrewObject[] screws = levelRoot.GetComponentsInChildren<ScrewObject>(true);

            Dictionary<PlateObject, int> plateIndexMap = new();
            for (int i = 0; i < plates.Length; i++)
            {
                if (plates[i] == null) continue;
                int idx = graph.Plates.Count;
                plateIndexMap[plates[i]] = idx;
                graph.Plates.Add(new PlateNode
                {
                    Index = idx,
                    Name = plates[i].name,
                    PlateRef = plates[i]
                });
            }

            Dictionary<ScrewObject, int> screwIndexMap = new();
            for (int i = 0; i < screws.Length; i++)
            {
                ScrewObject s = screws[i];
                if (s == null) continue;

                int pIdx = -1;
                PlateObject parentPlate = s.ParentPlate != null ? s.ParentPlate : s.GetComponentInParent<PlateObject>();
                if (parentPlate != null && plateIndexMap.TryGetValue(parentPlate, out int foundPlateIdx))
                {
                    pIdx = foundPlateIdx;
                }

                int sIdx = graph.Screws.Count;
                screwIndexMap[s] = sIdx;

                ScrewNode node = new()
                {
                    Index = sIdx,
                    Name = s.name,
                    ScrewRef = s,
                    Color = s.Color,
                    ParentPlateIndex = pIdx
                };

                graph.Screws.Add(node);

                if (!graph.ColorCounts.ContainsKey(s.Color)) graph.ColorCounts[s.Color] = 0;
                graph.ColorCounts[s.Color]++;

                if (pIdx >= 0)
                {
                    graph.Plates[pIdx].InitialScrewIndices.Add(sIdx);
                }
            }

            // Blocker kenarlarını bağla
            for (int i = 0; i < graph.Screws.Count; i++)
            {
                ScrewNode sNode = graph.Screws[i];
                ScrewObject s = sNode.ScrewRef;
                if (s.BlockingPlates != null)
                {
                    foreach (PlateObject bp in s.BlockingPlates)
                    {
                        if (bp != null && plateIndexMap.TryGetValue(bp, out int bpIdx) && bpIdx != sNode.ParentPlateIndex)
                        {
                            if (!sNode.BlockingPlateIndices.Contains(bpIdx))
                            {
                                sNode.BlockingPlateIndices.Add(bpIdx);
                                graph.Plates[bpIdx].CoveredScrewIndices.Add(sNode.Index);
                            }
                        }
                    }
                }
            }

            // Plaka _coveredScrews listelerinden eksik varsa tamamla
            for (int pIdx = 0; pIdx < graph.Plates.Count; pIdx++)
            {
                PlateNode pNode = graph.Plates[pIdx];
                SerializedObject pSo = new SerializedObject(pNode.PlateRef);
                SerializedProperty covProp = pSo.FindProperty("_coveredScrews");
                if (covProp != null)
                {
                    for (int k = 0; k < covProp.arraySize; k++)
                    {
                        ScrewObject cs = covProp.GetArrayElementAtIndex(k).objectReferenceValue as ScrewObject;
                        if (cs != null && screwIndexMap.TryGetValue(cs, out int sIdx))
                        {
                            ScrewNode sNode = graph.Screws[sIdx];
                            if (sNode.ParentPlateIndex != pIdx && !sNode.BlockingPlateIndices.Contains(pIdx))
                            {
                                sNode.BlockingPlateIndices.Add(pIdx);
                                pNode.CoveredScrewIndices.Add(sIdx);
                            }
                        }
                    }
                }
            }

            ComputeTopologicalDepths(graph);
            ValidateGraphIntegrity(graph);
            return graph;
        }

        private static void ComputeTopologicalDepths(LevelGraph graph)
        {
            bool[] fallen = new bool[graph.Plates.Count];
            bool[] resolved = new bool[graph.Screws.Count];
            int remainingPlates = graph.Plates.Count;
            int depth = 0;

            while (depth <= graph.Plates.Count + 2)
            {
                List<int> newlyOpenedScrews = new();
                for (int s = 0; s < graph.Screws.Count; s++)
                {
                    if (resolved[s]) continue;
                    bool blocked = false;
                    foreach (int bp in graph.Screws[s].BlockingPlateIndices)
                    {
                        if (!fallen[bp])
                        {
                            blocked = true;
                            break;
                        }
                    }
                    if (!blocked)
                    {
                        newlyOpenedScrews.Add(s);
                    }
                }

                if (newlyOpenedScrews.Count == 0) break;

                foreach (int s in newlyOpenedScrews)
                {
                    resolved[s] = true;
                    graph.Screws[s].DepthLayer = depth;
                }

                // Çözülen vidaların bağlı olduğu plakaları düşür
                bool anyPlateDropped = false;
                for (int p = 0; p < graph.Plates.Count; p++)
                {
                    if (fallen[p]) continue;
                    bool allResolved = true;
                    foreach (int sIdx in graph.Plates[p].InitialScrewIndices)
                    {
                        if (!resolved[sIdx])
                        {
                            allResolved = false;
                            break;
                        }
                    }
                    if (allResolved)
                    {
                        fallen[p] = true;
                        remainingPlates--;
                        anyPlateDropped = true;
                    }
                }

                if (!anyPlateDropped || remainingPlates <= 0) break;
                depth++;
            }

            // Döngüde kalan kilitli vidalar varsa yüksek katman işaretle
            for (int s = 0; s < graph.Screws.Count; s++)
            {
                if (!resolved[s])
                {
                    graph.Screws[s].DepthLayer = 99;
                    graph.ValidationIssues.Add($"Döngüsel/Çıkmaz Kilit: '{graph.Screws[s].Name}' vidası hiçbir zaman açılamıyor!");
                }
            }
        }

        private static void ValidateGraphIntegrity(LevelGraph graph)
        {
            if (graph.TotalScrews == 0)
            {
                graph.ValidationIssues.Add("Sahnede hiç vida yok.");
                return;
            }

            if (!graph.HasValidMultipleOfFour)
            {
                graph.ValidationIssues.Add($"Toplam vida sayısı ({graph.TotalScrews}) 4'ün katı değil!");
            }

            foreach (var kv in graph.ColorCounts)
            {
                if (kv.Value % BoxCapacity != 0)
                {
                    graph.ValidationIssues.Add($"{kv.Key} renginde {kv.Value} vida var (4'ün katı olmalı!).");
                }
            }

            for (int p = 0; p < graph.Plates.Count; p++)
            {
                if (graph.Plates[p].InitialScrewIndices.Count == 0)
                {
                    graph.ValidationIssues.Add($"'{graph.Plates[p].Name}' plakasında hiç bağlı vida yok (oyun başında boş düşer).");
                }
            }
        }

        /// <summary>
        /// Verilen graf ve kutu kuyruğu için:
        /// 1. Deterministik DFS/Best-First ile %100 çözüm yolu olup olmadığını kanıtlar.
        /// 2. Monte Carlo oyuncu simülasyonları ile tahmini kazanma oranını (zorluk derecesini) hesaplar.
        /// </summary>
        public static SolveReport Evaluate(LevelGraph graph, IReadOnlyList<ColorType> boxQueue, int monteCarloRuns = 80, int randomSeed = 42)
        {
            SolveReport report = new();

            if (graph == null || graph.TotalScrews == 0 || boxQueue == null || boxQueue.Count * BoxCapacity != graph.TotalScrews)
            {
                report.IsSolvable = false;
                report.Summary = "Geçersiz graf veya vida/kutu sayısı uyumsuzluğu.";
                return report;
            }

            if (graph.ValidationIssues.Count > 0)
            {
                // Döngüsel kilit veya renk hatası varsa baştan başarısız
                foreach (string issue in graph.ValidationIssues)
                {
                    if (issue.Contains("Döngüsel") || issue.Contains("katı değil") || issue.Contains("katı olmalı"))
                    {
                        report.IsSolvable = false;
                        report.Summary = issue;
                        return report;
                    }
                }
            }

            // 1. Deterministik Çözücü (Best-First / Bounded DFS)
            SimState initialState = CreateInitialState(graph, boxQueue);
            HashSet<ulong> visited = new();
            List<SolutionStepInfo> stepTrace = new();
            int visitedCount = 0;
            int maxHolesUsed = 0;
            int transfersUsed = 0;

            bool solved = SearchSolution(
                graph,
                boxQueue,
                initialState,
                consecutiveTransfers: 0,
                maxConsecutiveTransfers: 4,
                totalTransfersSoFar: 0,
                ref visitedCount,
                maxVisitedBudget: 16000,
                visited,
                stepTrace,
                ref maxHolesUsed,
                ref transfersUsed);

            report.IsSolvable = solved;
            report.VisitedNodes = visitedCount;
            report.SolutionSteps = stepTrace.Count;
            report.TransfersUsedInSolution = transfersUsed;
            report.MaxConcurrentHolesUsed = maxHolesUsed;
            report.TestedBoxQueue.AddRange(boxQueue);
            for (int i = 0; i < stepTrace.Count; i++)
            {
                stepTrace[i].StepNumber = i + 1;
                report.Steps.Add(stepTrace[i]);
                report.Trace.Add(stepTrace[i].Description);
            }

            if (!solved)
            {
                report.MonteCarloWinRate = 0f;
                report.Summary = $"ÇÖZÜLEMEDİ (Aranan düğüm: {visitedCount}). Katman veya kutu sırası çıkmaza giriyor.";
                return report;
            }

            // 2. Monte Carlo Yarı-Akıllı Oyuncu Simülasyonu (Gerçekçi Zorluk Ölçümü)
            int wins = 0;
            System.Random rng = new(randomSeed);
            for (int run = 0; run < monteCarloRuns; run++)
            {
                if (RunNoisyPlayerAttempt(graph, boxQueue, rng))
                {
                    wins++;
                }
            }

            report.MonteCarloWinRate = monteCarloRuns > 0 ? (wins * 100f / monteCarloRuns) : 100f;
            report.Summary = $"%100 Çözülebilir ({report.SolutionSteps} adım, maks {report.MaxConcurrentHolesUsed} boş delik, " +
                             $"{report.TransfersUsedInSolution} transfer) • Simüle Kazanma: %{report.MonteCarloWinRate:0}";

            return report;
        }

        /// <summary>
        /// Verilen 3D yapıyı (plaka + vida + engel grafı) bozmadan,
        /// hedef renk paleti ve hedef zorluk için %100 çözülebilir optimal vida renklerini ve kutu kuyruğunu üretir!
        /// </summary>
        public static OptimizationResult OptimizeColorsAndQueue(
            LevelGraph baseGraph,
            ColorType[] palette,
            LevelDifficulty targetDifficulty,
            int seed = 1001)
        {
            OptimizationResult bestResult = new();

            if (baseGraph == null || !baseGraph.HasValidMultipleOfFour || palette == null || palette.Length == 0)
            {
                return bestResult;
            }

            int totalScrews = baseGraph.TotalScrews;
            int totalBoxes = totalScrews / BoxCapacity;
            int activeColorCount = Mathf.Min(palette.Length, totalBoxes);

            // Geçerli bir soyma sırası (peel order) çıkar
            List<int> basePeelOrder = BuildValidPeelOrder(baseGraph, new System.Random(seed));
            if (basePeelOrder.Count != totalScrews)
            {
                return bestResult;
            }

            (float minWin, float maxWin) = targetDifficulty switch
            {
                LevelDifficulty.Easy => (78f, 100f),
                LevelDifficulty.Medium => (50f, 85f),
                LevelDifficulty.Hard => (25f, 65f),
                LevelDifficulty.Master => (10f, 48f),
                _ => (60f, 100f)
            };

            System.Random rng = new(seed);
            OptimizationResult fallbackSolvable = null;
            float bestScore = float.NegativeInfinity;

            int candidateAttempts = targetDifficulty == LevelDifficulty.Easy ? 28 : 55;

            for (int attempt = 0; attempt < candidateAttempts; attempt++)
            {
                // Her denemede farklı bir soyma sırası ve karıştırma stratejisi dene
                List<int> peelOrder = attempt == 0
                    ? basePeelOrder
                    : BuildValidPeelOrder(baseGraph, new System.Random(seed + attempt * 37));

                ColorType[] screwColors = new ColorType[totalScrews];
                List<ColorType> candidateQueue = new(totalBoxes);

                // Kutulara renk dağılımı (her renkten en az 1 kutu = 4 vida)
                List<ColorType> boxPalettePool = new(totalBoxes);
                for (int b = 0; b < totalBoxes; b++)
                {
                    boxPalettePool.Add(palette[b % activeColorCount]);
                }

                // Kolayda ilk 3 kutu her zaman farklı renklerle başlar; zorda sıralamayı kontrollü karıştır
                List<ColorType> orderedBoxColors = ArrangeBoxOrder(boxPalettePool, targetDifficulty, rng);

                // Temel garantili atama: soyma sırasını 4'lü kutu dalgaları halinde renklendir
                // Zorluk arttıkça aynı aktif penceredeki renkleri/katmanları karıştırarak gerçek bulmaca gerilimi oluştur
                AssignColorsAlongPeelOrder(
                    baseGraph,
                    peelOrder,
                    orderedBoxColors,
                    screwColors,
                    candidateQueue,
                    targetDifficulty,
                    attempt,
                    rng);

                // Test grafı oluştur
                ApplyColorsToTempGraph(baseGraph, screwColors);
                SolveReport report = Evaluate(baseGraph, candidateQueue, monteCarloRuns: 60, randomSeed: seed + attempt);

                if (!report.IsSolvable) continue;

                float winBandScore = ScoreSolveReport(report, targetDifficulty);

                // Plakalar üzerindeki renk çeşitliliğini ödüllendir (aynı plakada tek renk sıkıcılığını azalt, Level 1 hariç)
                float plateVarietyBonus = MeasurePlateColorVariety(baseGraph, screwColors) *
                                          (targetDifficulty == LevelDifficulty.Easy && totalBoxes <= 3 ? 2f : 12f);

                float score = winBandScore + plateVarietyBonus;

                OptimizationResult candidate = new()
                {
                    Success = true,
                    AssignedScrewColors = screwColors,
                    OptimizedBoxQueue = candidateQueue,
                    Report = report
                };

                if (fallbackSolvable == null)
                {
                    fallbackSolvable = candidate;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestResult = candidate;
                }

                // Mükemmel eşleşme bulunduysa erken çık
                if (IsInsideDifficultyBand(report.MonteCarloWinRate, targetDifficulty) && attempt >= 16)
                {
                    break;
                }
            }

            if (!bestResult.Success && fallbackSolvable != null)
            {
                bestResult = fallbackSolvable;
            }

            // Orijinal graf renklerini en iyi sonuca güncelle
            if (bestResult.Success)
            {
                ApplyColorsToTempGraph(baseGraph, bestResult.AssignedScrewColors);
            }

            return bestResult;
        }

        /// <summary>
        /// Mevcut vidaların renklerini hiç değiştirmeden yalnızca kutu iniş sırasını (Queue)
        /// hedef zorluğa göre %100 çözülebilir olacak şekilde optimize eder.
        /// Manuel tasarlanmış ve boyanmış seviyeler için idealdir.
        /// </summary>
        public static OptimizationResult OptimizeQueueOnly(
            LevelGraph graph,
            LevelDifficulty targetDifficulty,
            int seed = 1001)
        {
            OptimizationResult bestResult = new();
            if (graph == null || !graph.HasValidMultipleOfFour) return bestResult;

            foreach (var kv in graph.ColorCounts)
            {
                if (kv.Value % BoxCapacity != 0) return bestResult;
            }

            List<ColorType> baseBoxPool = BuildDefaultBoxQueueFromGraph(graph);
            if (baseBoxPool.Count == 0) return bestResult;

            System.Random rng = new(seed);
            OptimizationResult fallback = null;
            float bestScore = float.NegativeInfinity;
            const int maxAttempts = 64;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                List<ColorType> candidateQueue;
                if (attempt < 24)
                {
                    // Geçerli soyma sırasına (peel order) göre ilk açılan vidaların renklerine öncelik ver
                    List<int> peelOrder = BuildValidPeelOrder(graph, new System.Random(seed + attempt * 19));
                    candidateQueue = BuildQueueGuidedByPeelOrder(graph, peelOrder, baseBoxPool);
                }
                else if (attempt == 24)
                {
                    candidateQueue = new List<ColorType>(baseBoxPool);
                }
                else
                {
                    candidateQueue = ShuffleCopy(baseBoxPool, rng);
                }

                SolveReport report = Evaluate(graph, candidateQueue, monteCarloRuns: 45, randomSeed: seed + attempt);
                if (!report.IsSolvable) continue;

                float score = ScoreSolveReport(report, targetDifficulty);
                OptimizationResult candidate = new()
                {
                    Success = true,
                    AssignedScrewColors = graph.Screws.Select(s => s.Color).ToArray(),
                    OptimizedBoxQueue = candidateQueue,
                    Report = report
                };

                if (fallback == null) fallback = candidate;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestResult = candidate;
                }

                if (IsInsideDifficultyBand(report.MonteCarloWinRate, targetDifficulty) && attempt >= 12)
                {
                    break;
                }
            }

            return bestResult.Success ? bestResult : (fallback ?? bestResult);
        }

        /// <summary>
        /// Grafikteki mevcut vida renklerinden (her 4 vida = 1 kutu) dengeli bir varsayılan kutu kuyruğu üretir.
        /// </summary>
        public static List<ColorType> BuildDefaultBoxQueueFromGraph(LevelGraph graph)
        {
            List<ColorType> queue = new();
            if (graph == null) return queue;

            Dictionary<ColorType, int> boxCounts = new();
            List<ColorType> distinctColors = new();
            foreach (ScrewNode s in graph.Screws)
            {
                if (!boxCounts.ContainsKey(s.Color))
                {
                    boxCounts[s.Color] = 0;
                    distinctColors.Add(s.Color);
                }
                boxCounts[s.Color]++;
            }

            Dictionary<ColorType, int> boxesLeft = new();
            foreach (ColorType c in distinctColors)
            {
                boxesLeft[c] = boxCounts[c] / BoxCapacity;
            }

            bool added;
            do
            {
                added = false;
                foreach (ColorType c in distinctColors)
                {
                    if (boxesLeft[c] > 0)
                    {
                        queue.Add(c);
                        boxesLeft[c]--;
                        added = true;
                    }
                }
            }
            while (added);

            return queue;
        }

        private static List<ColorType> BuildQueueGuidedByPeelOrder(LevelGraph graph, List<int> peelOrder, List<ColorType> boxPool)
        {
            Dictionary<ColorType, int> remainingBoxes = new();
            foreach (ColorType c in boxPool)
            {
                if (!remainingBoxes.ContainsKey(c)) remainingBoxes[c] = 0;
                remainingBoxes[c]++;
            }

            List<ColorType> queue = new(boxPool.Count);
            Dictionary<ColorType, int> capacityInQueue = new();

            // Soyma sırasındaki vidalar açıldıkça ihtiyaç duyulan kutu rengini sıraya ekle
            foreach (int sIdx in peelOrder)
            {
                ColorType c = graph.Screws[sIdx].Color;
                int cap = capacityInQueue.TryGetValue(c, out int val) ? val : 0;
                if (cap > 0)
                {
                    capacityInQueue[c] = cap - 1;
                    continue;
                }

                if (remainingBoxes.TryGetValue(c, out int left) && left > 0)
                {
                    queue.Add(c);
                    remainingBoxes[c] = left - 1;
                    capacityInQueue[c] = BoxCapacity - 1;
                }
            }

            // Eksik kaldıysa havuzdan tamamla
            foreach (var kv in remainingBoxes)
            {
                for (int i = 0; i < kv.Value; i++)
                {
                    queue.Add(kv.Key);
                }
            }

            return queue;
        }

        private static List<ColorType> ShuffleCopy(List<ColorType> source, System.Random rng)
        {
            List<ColorType> copy = new(source);
            for (int i = copy.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (copy[i], copy[j]) = (copy[j], copy[i]);
            }
            return copy;
        }

        private static (float minWin, float maxWin) GetDifficultyWinBand(LevelDifficulty difficulty)
        {
            return difficulty switch
            {
                LevelDifficulty.Easy => (78f, 100f),
                LevelDifficulty.Medium => (50f, 85f),
                LevelDifficulty.Hard => (25f, 65f),
                LevelDifficulty.Master => (10f, 48f),
                _ => (60f, 100f)
            };
        }

        private static bool IsInsideDifficultyBand(float winRate, LevelDifficulty difficulty)
        {
            var (minWin, maxWin) = GetDifficultyWinBand(difficulty);
            return winRate >= minWin && winRate <= maxWin;
        }

        private static float ScoreSolveReport(SolveReport report, LevelDifficulty difficulty)
        {
            var (minWin, maxWin) = GetDifficultyWinBand(difficulty);
            float targetMidWin = (minWin + maxWin) * 0.5f;
            float winPenalty = Mathf.Abs(report.MonteCarloWinRate - targetMidWin);
            bool insideBand = report.MonteCarloWinRate >= minWin && report.MonteCarloWinRate <= maxWin;

            float transferBonus = difficulty switch
            {
                LevelDifficulty.Easy => report.TransfersUsedInSolution == 0 ? 15f : -20f,
                LevelDifficulty.Medium => report.TransfersUsedInSolution <= 2 ? 10f : 0f,
                LevelDifficulty.Hard => report.TransfersUsedInSolution >= 1 ? 12f : 0f,
                LevelDifficulty.Master => report.TransfersUsedInSolution >= 1 ? 15f : 0f,
                _ => 0f
            };

            return (insideBand ? 100f : 0f) - winPenalty + transferBonus;
        }

        private static List<ColorType> ArrangeBoxOrder(List<ColorType> boxPool, LevelDifficulty difficulty, System.Random rng)
        {
            List<ColorType> result = new();
            List<ColorType> remaining = new(boxPool);

            // İlk 3 kutuyu mümkün olduğunca farklı renklerden seç (oyuncu hemen akıcı başlasın)
            HashSet<ColorType> firstThreeSeen = new();
            for (int i = remaining.Count - 1; i >= 0 && result.Count < 3; i--)
            {
                if (firstThreeSeen.Add(remaining[i]))
                {
                    result.Add(remaining[i]);
                    remaining.RemoveAt(i);
                }
            }

            while (result.Count < 3 && remaining.Count > 0)
            {
                result.Add(remaining[0]);
                remaining.RemoveAt(0);
            }

            // Kalan kutuları karıştır; orta/zor seviyede arka arkaya aynı rengin gelmesini dengeli dağıt
            for (int i = remaining.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
            }

            result.AddRange(remaining);
            return result;
        }

        private static void AssignColorsAlongPeelOrder(
            LevelGraph graph,
            List<int> peelOrder,
            List<ColorType> boxOrder,
            ColorType[] outScrewColors,
            List<ColorType> outQueue,
            LevelDifficulty difficulty,
            int attemptIndex,
            System.Random rng)
        {
            outQueue.Clear();
            outQueue.AddRange(boxOrder);

            // Her kutuya 4 slot sırası tahsis et:
            // Kutuların aktif 3'lü pencerede kesişim genişliği zorluğa göre ayarlanır.
            int totalBoxes = boxOrder.Count;
            List<ColorType> flatAssignments = new(peelOrder.Count);

            // Aktif 3 kutu simülasyonuyla her vidaya mevcut aktif pencereden bir kutu rengi eşle
            int[] slotBoxIdx = new int[ActiveSlotCount];
            int[] slotFillLeft = new int[ActiveSlotCount];
            int nextBoxPtr = 0;

            for (int s = 0; s < ActiveSlotCount; s++)
            {
                if (nextBoxPtr < totalBoxes)
                {
                    slotBoxIdx[s] = nextBoxPtr;
                    slotFillLeft[s] = BoxCapacity;
                    nextBoxPtr++;
                }
                else
                {
                    slotBoxIdx[s] = -1;
                    slotFillLeft[s] = 0;
                }
            }

            // Soyma sırasındaki vidaları aktif kutu slotlarına dağıt (böylece soyma sırası her zaman çözülebilirdir!)
            for (int step = 0; step < peelOrder.Count; step++)
            {
                List<int> availableSlots = new(3);
                for (int s = 0; s < ActiveSlotCount; s++)
                {
                    if (slotFillLeft[s] > 0) availableSlots.Add(s);
                }

                // Kolayda kutuları dengeli / biraz daha tahmin edilebilir doldur, zorda daha iç içe karıştır
                int chosenSlot;
                if (difficulty == LevelDifficulty.Easy && attemptIndex % 2 == 0)
                {
                    // Mevcut vidanın plakasıyla uyumlu çeşitlilik tercih et
                    chosenSlot = availableSlots[step % availableSlots.Count];
                }
                else
                {
                    chosenSlot = availableSlots[rng.Next(availableSlots.Count)];
                }

                ColorType c = boxOrder[slotBoxIdx[chosenSlot]];
                flatAssignments.Add(c);
                slotFillLeft[chosenSlot]--;

                if (slotFillLeft[chosenSlot] == 0 && nextBoxPtr < totalBoxes)
                {
                    slotBoxIdx[chosenSlot] = nextBoxPtr;
                    slotFillLeft[chosenSlot] = BoxCapacity;
                    nextBoxPtr++;
                }
            }

            for (int i = 0; i < peelOrder.Count; i++)
            {
                outScrewColors[peelOrder[i]] = flatAssignments[i];
            }

            // Orta / Zor / Master seviyelerde ek kontrollü çapraz katman takası (sadece üst katmandaki bazı vidaları
            // derin katmandakilerle değiştirerek "boş delik açma" ve "kuyruk bekleme" bulmacası yarat!)
            int swapCount = difficulty switch
            {
                LevelDifficulty.Easy => totalBoxes <= 3 ? 0 : 1,
                LevelDifficulty.Medium => Mathf.Max(2, totalBoxes / 2),
                LevelDifficulty.Hard => Mathf.Max(3, totalBoxes),
                LevelDifficulty.Master => Mathf.Max(4, totalBoxes + 2),
                _ => 0
            };

            for (int k = 0; k < swapCount; k++)
            {
                int a = rng.Next(peelOrder.Count);
                int windowMax = Mathf.Min(peelOrder.Count - 1, a + (difficulty >= LevelDifficulty.Hard ? 10 : 6));
                int b = rng.Next(a, windowMax + 1);
                (outScrewColors[peelOrder[a]], outScrewColors[peelOrder[b]]) =
                    (outScrewColors[peelOrder[b]], outScrewColors[peelOrder[a]]);
            }
        }

        private static List<int> BuildValidPeelOrder(LevelGraph graph, System.Random rng)
        {
            List<int> order = new(graph.TotalScrews);
            bool[] removed = new bool[graph.TotalScrews];
            int[] plateRemaining = new int[graph.Plates.Count];
            for (int p = 0; p < graph.Plates.Count; p++)
            {
                plateRemaining[p] = graph.Plates[p].InitialScrewIndices.Count;
            }

            while (order.Count < graph.TotalScrews)
            {
                List<int> unblocked = new();
                for (int s = 0; s < graph.Screws.Count; s++)
                {
                    if (removed[s]) continue;
                    bool blocked = false;
                    foreach (int bp in graph.Screws[s].BlockingPlateIndices)
                    {
                        if (plateRemaining[bp] > 0)
                        {
                            blocked = true;
                            break;
                        }
                    }
                    if (!blocked) unblocked.Add(s);
                }

                if (unblocked.Count == 0) break;

                // Plakaları tamamen boşaltma ile yüzleri dolaşma arasında dengeli seçim
                int pickIdx = unblocked[rng.Next(unblocked.Count)];
                removed[pickIdx] = true;
                order.Add(pickIdx);

                int pIdx = graph.Screws[pickIdx].ParentPlateIndex;
                if (pIdx >= 0) plateRemaining[pIdx]--;
            }

            return order;
        }

        private static float MeasurePlateColorVariety(LevelGraph graph, ColorType[] screwColors)
        {
            if (graph.Plates.Count == 0) return 0f;
            float sum = 0f;
            foreach (PlateNode p in graph.Plates)
            {
                if (p.InitialScrewIndices.Count <= 1) continue;
                HashSet<ColorType> unique = new();
                foreach (int sIdx in p.InitialScrewIndices)
                {
                    unique.Add(screwColors[sIdx]);
                }
                sum += (float)unique.Count / p.InitialScrewIndices.Count;
            }
            return sum / graph.Plates.Count;
        }

        private static void ApplyColorsToTempGraph(LevelGraph graph, ColorType[] screwColors)
        {
            graph.ColorCounts.Clear();
            for (int i = 0; i < graph.Screws.Count; i++)
            {
                ColorType c = screwColors[i];
                graph.Screws[i].Color = c;
                if (!graph.ColorCounts.ContainsKey(c)) graph.ColorCounts[c] = 0;
                graph.ColorCounts[c]++;
            }
        }

        #region Exact Headless Simulation Engine

        private struct SimState
        {
            public const int SentToBoxPlateId = -99;
            public int[] ScrewPlate;       // -99 = kutuya gitti, -1 = gövde üstünde, >=0 = plaka indeksi
            public int[] PlateScrewsLeft;  // Plakadaki aktif vida sayısı (<=0 ise düştü)
            public int[] PlateEmptyHoles;  // Plakada açılmış kullanılabilir boş delik sayısı
            public int[] SlotColor;        // -1 = boş slot
            public int[] SlotNeed;         // 0..4
            public int NextQueueIndex;
            public int RemainingScrews;

            public SimState Clone()
            {
                return new SimState
                {
                    ScrewPlate = (int[])ScrewPlate.Clone(),
                    PlateScrewsLeft = (int[])PlateScrewsLeft.Clone(),
                    PlateEmptyHoles = (int[])PlateEmptyHoles.Clone(),
                    SlotColor = (int[])SlotColor.Clone(),
                    SlotNeed = (int[])SlotNeed.Clone(),
                    NextQueueIndex = NextQueueIndex,
                    RemainingScrews = RemainingScrews
                };
            }
        }

        private static SimState CreateInitialState(LevelGraph graph, IReadOnlyList<ColorType> boxQueue)
        {
            SimState state = new()
            {
                ScrewPlate = new int[graph.TotalScrews],
                PlateScrewsLeft = new int[graph.Plates.Count],
                PlateEmptyHoles = new int[graph.Plates.Count],
                SlotColor = new int[ActiveSlotCount],
                SlotNeed = new int[ActiveSlotCount],
                NextQueueIndex = 0,
                RemainingScrews = graph.TotalScrews
            };

            for (int i = 0; i < graph.TotalScrews; i++)
            {
                state.ScrewPlate[i] = graph.Screws[i].ParentPlateIndex;
            }

            for (int p = 0; p < graph.Plates.Count; p++)
            {
                state.PlateScrewsLeft[p] = graph.Plates[p].InitialScrewIndices.Count;
                state.PlateEmptyHoles[p] = 0;
            }

            for (int s = 0; s < ActiveSlotCount; s++)
            {
                if (state.NextQueueIndex < boxQueue.Count)
                {
                    state.SlotColor[s] = (int)boxQueue[state.NextQueueIndex++];
                    state.SlotNeed[s] = BoxCapacity;
                }
                else
                {
                    state.SlotColor[s] = -1;
                    state.SlotNeed[s] = 0;
                }
            }

            return state;
        }

        private static bool IsScrewUnblocked(LevelGraph graph, in SimState state, int screwIdx)
        {
            if (state.ScrewPlate[screwIdx] == SimState.SentToBoxPlateId) return false; // Zaten kutuya gitti
            List<int> blockers = graph.Screws[screwIdx].BlockingPlateIndices;
            for (int i = 0; i < blockers.Count; i++)
            {
                if (state.PlateScrewsLeft[blockers[i]] > 0) return false;
            }
            return true;
        }

        private static bool SearchSolution(
            LevelGraph graph,
            IReadOnlyList<ColorType> boxQueue,
            SimState state,
            int consecutiveTransfers,
            int maxConsecutiveTransfers,
            int totalTransfersSoFar,
            ref int visitedCount,
            int maxVisitedBudget,
            HashSet<ulong> visited,
            List<SolutionStepInfo> stepTrace,
            ref int maxHolesUsed,
            ref int transfersUsed)
        {
            if (state.RemainingScrews == 0)
            {
                transfersUsed = totalTransfersSoFar;
                return true;
            }

            if (visitedCount >= maxVisitedBudget) return false;
            visitedCount++;

            ulong hash = ComputeStateHash(state);
            if (!visited.Add(hash)) return false;

            int currentEmptyHoles = CountActiveEmptyHoles(state);
            if (currentEmptyHoles > maxHolesUsed) maxHolesUsed = currentEmptyHoles;

            // A) Doğrudan aktif kutulara giden hamleleri skorla ve dene
            List<(int screwIdx, int slotIdx, float score)> directMoves = new();
            for (int s = 0; s < graph.TotalScrews; s++)
            {
                if (!IsScrewUnblocked(graph, state, s)) continue;
                int c = (int)graph.Screws[s].Color;
                int matchingSlot = -1;
                for (int k = 0; k < ActiveSlotCount; k++)
                {
                    if (state.SlotColor[k] == c && state.SlotNeed[k] > 0)
                    {
                        matchingSlot = k;
                        break;
                    }
                }
                if (matchingSlot < 0) continue;

                float moveScore = 0f;
                if (state.SlotNeed[matchingSlot] == 1) moveScore += 30f; // Kutu tamamlanır, yeni kutu açılır!
                int pIdx = state.ScrewPlate[s];
                if (pIdx >= 0)
                {
                    if (state.PlateScrewsLeft[pIdx] == 1)
                    {
                        moveScore += graph.Plates[pIdx].CoveredScrewIndices.Count * 15f; // Plaka düşer ve kilit açar!
                    }
                    else
                    {
                        moveScore += 8f; // Boş delik açar (güvenli!)
                    }
                }
                else
                {
                    moveScore += 10f; // Gövde vidası: çıkınca kalıcı boş delik açar
                }
                directMoves.Add((s, matchingSlot, moveScore));
            }

            directMoves.Sort((a, b) => b.score.CompareTo(a.score));

            for (int i = 0; i < directMoves.Count; i++)
            {
                var mv = directMoves[i];
                SimState next = state.Clone();
                ApplyDirectBoxMove(boxQueue, ref next, mv.screwIdx, mv.slotIdx);

                SolutionStepInfo stepInfo = BuildStepInfo(graph, state, next, isTransfer: false, mv.screwIdx, mv.slotIdx);
                stepTrace.Add(stepInfo);

                if (SearchSolution(graph, boxQueue, next, 0, maxConsecutiveTransfers, totalTransfersSoFar,
                    ref visitedCount, maxVisitedBudget, visited, stepTrace, ref maxHolesUsed, ref transfersUsed))
                {
                    return true;
                }

                stepTrace.RemoveAt(stepTrace.Count - 1);
                if (visitedCount >= maxVisitedBudget) return false;
            }

            // B) Boş deliğe vida transferi hamleleri (kilitli plakayı düşürmek için kutusu olmayan vidayı taşı)
            if (consecutiveTransfers < maxConsecutiveTransfers)
            {
                List<(int screwIdx, int targetPlateIdx, float score)> transferMoves = new();
                for (int s = 0; s < graph.TotalScrews; s++)
                {
                    if (!IsScrewUnblocked(graph, state, s)) continue;

                    // Kutuya gidebilen vidayı boşuna deliğe transfer etme (genelde gereksiz dallanma)
                    int c = (int)graph.Screws[s].Color;
                    bool hasBox = false;
                    for (int k = 0; k < ActiveSlotCount; k++)
                    {
                        if (state.SlotColor[k] == c && state.SlotNeed[k] > 0) { hasBox = true; break; }
                    }
                    if (hasBox) continue;

                    int fromPlate = state.ScrewPlate[s];
                    if (fromPlate < 0) continue; // Gövde vidasını boşuna taşıma
                    // Sadece kilit açacak bir plakayı azaltıyorsa veya o plakada son vidalardansa anlamlıdır
                    int coveredCount = graph.Plates[fromPlate].CoveredScrewIndices.Count;
                    if (coveredCount == 0 && state.PlateScrewsLeft[fromPlate] > 1) continue;

                    for (int toPlate = 0; toPlate < graph.Plates.Count; toPlate++)
                    {
                        if (toPlate == fromPlate) continue;
                        if (state.PlateScrewsLeft[toPlate] <= 0 || state.PlateEmptyHoles[toPlate] <= 0) continue;

                        float score = (state.PlateScrewsLeft[fromPlate] == 1 ? 40f + coveredCount * 20f : 5f)
                                      + state.PlateScrewsLeft[toPlate] * 2f; // Büyük/sağlam plakaya park etmek daha güvenli!
                        transferMoves.Add((s, toPlate, score));
                    }
                }

                transferMoves.Sort((a, b) => b.score.CompareTo(a.score));

                // Dallanma patlamasını önlemek için en mantıklı ilk 4 transfer adayını dene
                int limit = Mathf.Min(4, transferMoves.Count);
                for (int i = 0; i < limit; i++)
                {
                    var mv = transferMoves[i];
                    SimState next = state.Clone();
                    ApplyHoleTransferMove(ref next, mv.screwIdx, mv.targetPlateIdx);

                    SolutionStepInfo stepInfo = BuildStepInfo(graph, state, next, isTransfer: true, mv.screwIdx, mv.targetPlateIdx);
                    stepTrace.Add(stepInfo);

                    if (SearchSolution(graph, boxQueue, next, consecutiveTransfers + 1, maxConsecutiveTransfers, totalTransfersSoFar + 1,
                        ref visitedCount, maxVisitedBudget, visited, stepTrace, ref maxHolesUsed, ref transfersUsed))
                    {
                        return true;
                    }

                    stepTrace.RemoveAt(stepTrace.Count - 1);
                    if (visitedCount >= maxVisitedBudget) return false;
                }
            }

            return false;
        }

        private static int CountActiveEmptyHoles(in SimState state)
        {
            int count = 0;
            for (int p = 0; p < state.PlateEmptyHoles.Length; p++)
            {
                if (state.PlateScrewsLeft[p] > 0) count += state.PlateEmptyHoles[p];
            }
            return count;
        }

        private static SolutionStepInfo BuildStepInfo(
            LevelGraph graph,
            in SimState before,
            in SimState after,
            bool isTransfer,
            int screwIdx,
            int targetIdx)
        {
            int sourcePlate = before.ScrewPlate[screwIdx];
            int droppedPlate = -1;
            string droppedName = null;
            if (sourcePlate >= 0 && before.PlateScrewsLeft[sourcePlate] > 0 && after.PlateScrewsLeft[sourcePlate] <= 0)
            {
                droppedPlate = sourcePlate;
                droppedName = graph.Plates[sourcePlate].Name;
            }

            ScrewNode screw = graph.Screws[screwIdx];
            string desc = isTransfer
                ? $"Transfer: {screw.Name} ({screw.Color}) -> {graph.Plates[targetIdx].Name} Deliği"
                : $"Vida: {screw.Name} ({screw.Color}) -> Kutu[{targetIdx + 1}]";

            if (droppedPlate >= 0)
            {
                desc += $" [💥 {droppedName} Düştü!]";
            }

            SolutionStepInfo step = new()
            {
                IsTransfer = isTransfer,
                ScrewIndex = screwIdx,
                ScrewName = screw.Name,
                ScrewColor = screw.Color,
                SourcePlateIndex = sourcePlate,
                TargetSlotOrPlateIndex = targetIdx,
                DroppedPlateIndex = droppedPlate,
                DroppedPlateName = droppedName,
                ActiveEmptyHolesAfter = CountActiveEmptyHoles(after),
                Description = desc
            };

            for (int k = 0; k < ActiveSlotCount; k++)
            {
                step.SlotColorIdsAfter[k] = after.SlotColor[k];
                step.SlotNeedAfter[k] = after.SlotNeed[k];
            }

            return step;
        }

        private static bool RunNoisyPlayerAttempt(LevelGraph graph, IReadOnlyList<ColorType> boxQueue, System.Random rng)
        {
            SimState state = CreateInitialState(graph, boxQueue);
            int maxIterations = graph.TotalScrews * 4 + 20;
            int consecutiveTransfers = 0;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                if (state.RemainingScrews == 0) return true;

                List<(int s, int slot)> direct = new();
                for (int s = 0; s < graph.TotalScrews; s++)
                {
                    if (!IsScrewUnblocked(graph, state, s)) continue;
                    int c = (int)graph.Screws[s].Color;
                    for (int k = 0; k < ActiveSlotCount; k++)
                    {
                        if (state.SlotColor[k] == c && state.SlotNeed[k] > 0)
                        {
                            direct.Add((s, k));
                            break;
                        }
                    }
                }

                if (direct.Count > 0)
                {
                    var pick = direct[rng.Next(direct.Count)];
                    ApplyDirectBoxMove(boxQueue, ref state, pick.s, pick.slot);
                    consecutiveTransfers = 0;
                    continue;
                }

                // Doğrudan kutu yoksa rastgele bir boş delik transferi dene
                if (consecutiveTransfers >= 3) return false;

                List<(int s, int toPlate)> transfers = new();
                for (int s = 0; s < graph.TotalScrews; s++)
                {
                    if (!IsScrewUnblocked(graph, state, s)) continue;
                    int fromPlate = state.ScrewPlate[s];
                    if (fromPlate < 0) continue;
                    if (graph.Plates[fromPlate].CoveredScrewIndices.Count == 0 && state.PlateScrewsLeft[fromPlate] > 1) continue;

                    for (int p = 0; p < graph.Plates.Count; p++)
                    {
                        if (p == fromPlate || state.PlateScrewsLeft[p] <= 0 || state.PlateEmptyHoles[p] <= 0) continue;
                        transfers.Add((s, p));
                    }
                }

                if (transfers.Count == 0) return false;

                var tr = transfers[rng.Next(transfers.Count)];
                ApplyHoleTransferMove(ref state, tr.s, tr.toPlate);
                consecutiveTransfers++;
            }

            return false;
        }

        private static void ApplyDirectBoxMove(IReadOnlyList<ColorType> boxQueue, ref SimState state, int screwIdx, int slotIdx)
        {
            int plateIdx = state.ScrewPlate[screwIdx];
            state.ScrewPlate[screwIdx] = SimState.SentToBoxPlateId;
            state.RemainingScrews--;

            if (plateIdx >= 0)
            {
                state.PlateScrewsLeft[plateIdx]--;
                if (state.PlateScrewsLeft[plateIdx] <= 0)
                {
                    state.PlateEmptyHoles[plateIdx] = 0; // Plaka düştü, delikler kayboldu
                }
                else
                {
                    state.PlateEmptyHoles[plateIdx]++; // Çıkan vidanın altında yeni boş delik açıldı
                }
            }

            state.SlotNeed[slotIdx]--;
            if (state.SlotNeed[slotIdx] <= 0)
            {
                if (state.NextQueueIndex < boxQueue.Count)
                {
                    state.SlotColor[slotIdx] = (int)boxQueue[state.NextQueueIndex++];
                    state.SlotNeed[slotIdx] = BoxCapacity;
                }
                else
                {
                    state.SlotColor[slotIdx] = -1;
                    state.SlotNeed[slotIdx] = 0;
                }
            }
        }

        private static void ApplyHoleTransferMove(ref SimState state, int screwIdx, int targetPlateIdx)
        {
            int fromPlate = state.ScrewPlate[screwIdx];
            state.ScrewPlate[screwIdx] = targetPlateIdx;

            if (fromPlate >= 0)
            {
                state.PlateScrewsLeft[fromPlate]--;
                if (state.PlateScrewsLeft[fromPlate] <= 0)
                {
                    state.PlateEmptyHoles[fromPlate] = 0;
                }
                else
                {
                    state.PlateEmptyHoles[fromPlate]++;
                }
            }

            state.PlateScrewsLeft[targetPlateIdx]++;
            state.PlateEmptyHoles[targetPlateIdx]--;
        }

        private static ulong ComputeStateHash(in SimState state)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (int i = 0; i < state.ScrewPlate.Length; i++)
                {
                    h = (h ^ (ulong)(state.ScrewPlate[i] + 2)) * 1099511628211UL;
                }
                for (int i = 0; i < state.PlateEmptyHoles.Length; i++)
                {
                    h = (h ^ (ulong)(state.PlateEmptyHoles[i] + 17)) * 1099511628211UL;
                }
                for (int s = 0; s < ActiveSlotCount; s++)
                {
                    h = (h ^ (ulong)((state.SlotColor[s] + 1) * 13 + state.SlotNeed[s])) * 1099511628211UL;
                }
                h = (h ^ (ulong)state.NextQueueIndex) * 1099511628211UL;
                return h;
            }
        }

        /// <summary>
        /// Editörde mevcut grafik raporu okunabilir özet olarak formatlar.
        /// </summary>
        public static string FormatGraphSummary(LevelGraph graph, SolveReport report)
        {
            StringBuilder sb = new();
            sb.AppendLine($"Toplam Vida: {graph.TotalScrews} ({graph.RequiredBoxes} Kutu) • Plaka: {graph.Plates.Count}");
            if (graph.ColorCounts.Count > 0)
            {
                sb.AppendLine("Renkler: " + string.Join(", ", graph.ColorCounts.Select(kv => $"{kv.Key}:{kv.Value}")));
            }
            if (graph.ValidationIssues.Count > 0)
            {
                sb.AppendLine("Uyarılar: " + string.Join(" | ", graph.ValidationIssues));
            }
            if (report != null)
            {
                sb.AppendLine("Solver: " + report.Summary);
            }
            return sb.ToString().TrimEnd();
        }

        #endregion
    }
}
