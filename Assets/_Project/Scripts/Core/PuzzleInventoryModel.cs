using System;
using System.Collections.Generic;

namespace ScrewSolver.Core
{
    /// <summary>
    /// Pure C# central inventory and reservation brain of the puzzle.
    /// Completely isolated from Unity visual components (MonoBehaviour) to enable 
    /// unit testing, zero-allocation simulations, and high-performance headless bots.
    /// </summary>
    public sealed class PuzzleInventoryModel
    {
        public const int DefaultActiveBoxCount = 4;
        public const int DefaultBufferCapacity = 5;

        public int ActiveBoxCount { get; private set; }
        public int BufferCapacity { get; private set; }

        public BoxData[] ActiveBoxes { get; private set; }
        public Queue<ColorType> BoxQueue { get; private set; }
        public List<ColorType> BufferDock { get; private set; }

        public bool IsGameOver { get; private set; }
        public bool IsGameWon { get; private set; }

        // Events for View / UI / Dispatcher layer
        public event Action<ReservationResult> OnScrewReserved;
        public event Action<int, ColorType> OnBoxCompleted;
        public event Action<int, ColorType> OnBoxSpawned;
        public event Action<BufferDrainResult> OnBufferDrained;
        public event Action OnGameOverTriggered;
        public event Action OnGameWonTriggered;

        public PuzzleInventoryModel(int activeBoxCount = DefaultActiveBoxCount, int bufferCapacity = DefaultBufferCapacity)
        {
            ActiveBoxCount = Math.Max(1, activeBoxCount);
            BufferCapacity = Math.Max(1, bufferCapacity);

            ActiveBoxes = new BoxData[ActiveBoxCount];
            for (int i = 0; i < ActiveBoxCount; i++)
            {
                ActiveBoxes[i] = new BoxData(ColorType.None);
            }

            BoxQueue = new Queue<ColorType>();
            BufferDock = new List<ColorType>(BufferCapacity);
        }

        /// <summary>
        /// Initializes board with active boxes and incoming box queue.
        /// </summary>
        public void Initialize(
            IReadOnlyList<ColorType> initialActiveBoxColors,
            IEnumerable<ColorType> queuedBoxColors)
        {
            IsGameOver = false;
            IsGameWon = false;
            BufferDock.Clear();
            BoxQueue.Clear();

            int initialCount = initialActiveBoxColors != null ? initialActiveBoxColors.Count : 0;
            for (int i = 0; i < ActiveBoxCount; i++)
            {
                if (i < initialCount && initialActiveBoxColors[i] != ColorType.None)
                {
                    ActiveBoxes[i].Setup(initialActiveBoxColors[i]);
                }
                else
                {
                    ActiveBoxes[i].Clear();
                }
            }

            if (queuedBoxColors != null)
            {
                foreach (ColorType color in queuedBoxColors)
                {
                    if (color != ColorType.None)
                    {
                        BoxQueue.Enqueue(color);
                    }
                }
            }
        }

        /// <summary>
        /// Instantly reserves a slot for the clicked screw.
        /// Guaranteed sub-microsecond decision with zero allocation.
        /// </summary>
        public ReservationResult TryAddScrew(ColorType color)
        {
            if (IsGameOver || IsGameWon || color == ColorType.None)
            {
                return new ReservationResult(ReservationType.None, color);
            }

            // 1. Check if any active box matches the screw color
            for (int i = 0; i < ActiveBoxCount; i++)
            {
                BoxData box = ActiveBoxes[i];
                if (box.Matches(color))
                {
                    if (box.TryReserveSlot(out int slotIndex))
                    {
                        bool isComplete = box.IsFull;
                        ReservationResult result = ReservationResult.Box(color, i, slotIndex, isComplete);
                        OnScrewReserved?.Invoke(result);

                        if (isComplete)
                        {
                            OnBoxCompleted?.Invoke(i, color);
                        }

                        return result;
                    }
                }
            }

            // 2. No matching active box: check Buffer Dock
            if (BufferDock.Count < BufferCapacity)
            {
                int bufferIndex = BufferDock.Count;
                BufferDock.Add(color);

                ReservationResult result = ReservationResult.Buffer(color, bufferIndex);
                OnScrewReserved?.Invoke(result);
                return result;
            }

            // 3. Buffer Dock is completely full -> Game Over!
            IsGameOver = true;
            ReservationResult gameOverResult = ReservationResult.GameOver(color);
            OnScrewReserved?.Invoke(gameOverResult);
            OnGameOverTriggered?.Invoke();
            return gameOverResult;
        }

        /// <summary>
        /// Completes a full box, shifts the queue to introduce a new box,
        /// and automatically drains matching screws waiting in the buffer dock.
        /// </summary>
        public bool CompleteBoxAndShift(int boxIndex, List<BufferDrainResult> outDrains = null)
        {
            if (boxIndex < 0 || boxIndex >= ActiveBoxCount)
                return false;

            BoxData box = ActiveBoxes[boxIndex];
            ColorType completedColor = box.RequiredColor;
            box.Clear();

            // Spawn next box from queue if available
            if (BoxQueue.Count > 0)
            {
                ColorType nextColor = BoxQueue.Dequeue();
                box.Setup(nextColor);
                OnBoxSpawned?.Invoke(boxIndex, nextColor);

                // Auto-drain from buffer dock into new box
                DrainBufferIntoBox(boxIndex, outDrains);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Drains matching screws from the buffer dock into the specified active box.
        /// </summary>
        public void DrainBufferIntoBox(int boxIndex, List<BufferDrainResult> outDrains = null)
        {
            if (boxIndex < 0 || boxIndex >= ActiveBoxCount)
                return;

            BoxData box = ActiveBoxes[boxIndex];
            if (!box.IsActive)
                return;

            ColorType targetColor = box.RequiredColor;

            for (int i = BufferDock.Count - 1; i >= 0 && !box.IsFull; i--)
            {
                if (BufferDock[i] == targetColor)
                {
                    if (box.TryReserveSlot(out int slotIndex))
                    {
                        BufferDock.RemoveAt(i);
                        bool isChainComplete = box.IsFull;

                        BufferDrainResult drainResult = new BufferDrainResult(i, boxIndex, slotIndex, targetColor, isChainComplete);
                        outDrains?.Add(drainResult);
                        OnBufferDrained?.Invoke(drainResult);

                        if (isChainComplete)
                        {
                            OnBoxCompleted?.Invoke(boxIndex, targetColor);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the player has won the level (no remaining screws on board and buffer is empty).
        /// </summary>
        public bool CheckWinCondition(int remainingScrewsOnBoard)
        {
            if (remainingScrewsOnBoard <= 0 && BufferDock.Count == 0)
            {
                IsGameWon = true;
                OnGameWonTriggered?.Invoke();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if the game is deadlocked (buffer dock is full and no active box can accept screws).
        /// </summary>
        public bool CheckDeadlockCondition(IReadOnlyList<ColorType> exposedScrews)
        {
            if (BufferDock.Count < BufferCapacity)
                return false;

            if (exposedScrews == null || exposedScrews.Count == 0)
                return false;

            // If any exposed screw matches any active box that has remaining space, not deadlocked
            for (int s = 0; s < exposedScrews.Count; s++)
            {
                ColorType color = exposedScrews[s];
                for (int b = 0; b < ActiveBoxCount; b++)
                {
                    if (ActiveBoxes[b].Matches(color))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}

