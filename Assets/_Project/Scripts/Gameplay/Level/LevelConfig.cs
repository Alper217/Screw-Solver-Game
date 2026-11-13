using System.Collections.Generic;
using ScrewSolver.Core;
using UnityEngine;

namespace ScrewSolver.Gameplay.Level
{
    public enum LevelDifficulty
    {
        Easy = 0,
        Medium = 1,
        Hard = 2,
        Master = 3
    }

    /// <summary>
    /// Seviye prefab'ı üzerine kaydedilen, HeadlessLevelSolver tarafından doğrulanmış
    /// özel kutu kuyruğu, süre sınırı ve zorluk metaverisi.
    /// </summary>
    [AddComponentMenu("ScrewSolver/Level Config")]
    public sealed class LevelConfig : MonoBehaviour
    {
        [Header("Seviye Kimliği")]
        [SerializeField] private int _levelNumber = 1;
        [SerializeField] private string _levelTitle = "Level";
        [SerializeField] private LevelDifficulty _difficulty = LevelDifficulty.Easy;

        [Header("Oynanış Dengesi")]
        [SerializeField] private float _timeLimitSeconds = 90f;
        [SerializeField] private List<ColorType> _optimizedBoxQueue = new();

        [Header("Solver Doğrulama Raporu (Salt Okunur)")]
        [SerializeField] private bool _isSolverVerified;
        [SerializeField, Range(0f, 100f)] private float _simulatedWinRate;
        [SerializeField] private int _minSolutionSteps;
        [SerializeField] private int _maxHolesUsedInSolution;
        [SerializeField] private string _solverSummary = string.Empty;

        public int LevelNumber => _levelNumber;
        public string LevelTitle => _levelTitle;
        public LevelDifficulty Difficulty => _difficulty;
        public float TimeLimitSeconds => _timeLimitSeconds;
        public IReadOnlyList<ColorType> OptimizedBoxQueue => _optimizedBoxQueue;
        public bool IsSolverVerified => _isSolverVerified;
        public float SimulatedWinRate => _simulatedWinRate;
        public int MinSolutionSteps => _minSolutionSteps;
        public int MaxHolesUsedInSolution => _maxHolesUsedInSolution;
        public string SolverSummary => _solverSummary;

        /// <summary>
        /// Editör üreticisi veya Solver tarafından seviye verisini günceller.
        /// </summary>
        public void Configure(
            int levelNumber,
            string levelTitle,
            LevelDifficulty difficulty,
            float timeLimitSeconds,
            List<ColorType> boxQueue,
            bool isSolverVerified,
            float simulatedWinRate,
            int minSolutionSteps,
            int maxHolesUsedInSolution,
            string solverSummary)
        {
            _levelNumber = levelNumber;
            _levelTitle = levelTitle;
            _difficulty = difficulty;
            _timeLimitSeconds = timeLimitSeconds;
            _optimizedBoxQueue = boxQueue != null ? new List<ColorType>(boxQueue) : new List<ColorType>();
            _isSolverVerified = isSolverVerified;
            _simulatedWinRate = simulatedWinRate;
            _minSolutionSteps = minSolutionSteps;
            _maxHolesUsedInSolution = maxHolesUsedInSolution;
            _solverSummary = solverSummary ?? string.Empty;
        }
    }
}

