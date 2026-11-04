using System;

namespace ScrewSolver.Core
{
    /// <summary>
    /// Pure C# state of an active target box on the board.
    /// Tracks color requirement, capacity, and current reserved count.
    /// </summary>
    public sealed class BoxData
    {
        public const int DefaultCapacity = 3;

        public ColorType RequiredColor { get; private set; }
        public int Capacity { get; private set; }
        public int CurrentCount { get; private set; }

        public bool IsActive => RequiredColor != ColorType.None;
        public bool IsFull => CurrentCount >= Capacity;
        public int RemainingSlots => Math.Max(0, Capacity - CurrentCount);

        public BoxData(ColorType requiredColor, int capacity = DefaultCapacity)
        {
            RequiredColor = requiredColor;
            Capacity = Math.Max(1, capacity);
            CurrentCount = 0;
        }

        public void Setup(ColorType color, int capacity = DefaultCapacity)
        {
            RequiredColor = color;
            Capacity = Math.Max(1, capacity);
            CurrentCount = 0;
        }

        public bool Matches(ColorType color)
        {
            return IsActive && RequiredColor == color && !IsFull;
        }

        public bool TryReserveSlot(out int slotIndex)
        {
            if (!IsFull && IsActive)
            {
                slotIndex = CurrentCount;
                CurrentCount++;
                return true;
            }

            slotIndex = -1;
            return false;
        }

        public void Clear()
        {
            RequiredColor = ColorType.None;
            CurrentCount = 0;
        }
    }
}
