namespace ScrewSolver.Core
{
    public enum ReservationType
    {
        None = 0,
        ToTargetBox = 1,
        ToBufferDock = 2,
        GameOverFullBuffer = 3
    }

    /// <summary>
    /// Represents the immutable result of an instant logical reservation when a screw is clicked.
    /// Provides zero-allocation details for both the gameplay model and the visual dispatcher.
    /// </summary>
    public readonly struct ReservationResult
    {
        public readonly ReservationType Type;
        public readonly ColorType Color;
        public readonly int TargetBoxIndex;
        public readonly int TargetSlotIndex;
        public readonly int BufferIndex;
        public readonly bool CausesBoxCompletion;
        public readonly int CompletedBoxIndex;

        public bool IsSuccessful => Type == ReservationType.ToTargetBox || Type == ReservationType.ToBufferDock;
        public bool IsGameOver => Type == ReservationType.GameOverFullBuffer;

        public ReservationResult(
            ReservationType type,
            ColorType color,
            int targetBoxIndex = -1,
            int targetSlotIndex = -1,
            int bufferIndex = -1,
            bool causesBoxCompletion = false,
            int completedBoxIndex = -1)
        {
            Type = type;
            Color = color;
            TargetBoxIndex = targetBoxIndex;
            TargetSlotIndex = targetSlotIndex;
            BufferIndex = bufferIndex;
            CausesBoxCompletion = causesBoxCompletion;
            CompletedBoxIndex = completedBoxIndex;
        }

        public static ReservationResult Box(ColorType color, int boxIndex, int slotIndex, bool isComplete)
        {
            return new ReservationResult(
                ReservationType.ToTargetBox,
                color,
                targetBoxIndex: boxIndex,
                targetSlotIndex: slotIndex,
                bufferIndex: -1,
                causesBoxCompletion: isComplete,
                completedBoxIndex: isComplete ? boxIndex : -1
            );
        }

        public static ReservationResult Buffer(ColorType color, int bufferIndex)
        {
            return new ReservationResult(
                ReservationType.ToBufferDock,
                color,
                targetBoxIndex: -1,
                targetSlotIndex: -1,
                bufferIndex: bufferIndex,
                causesBoxCompletion: false,
                completedBoxIndex: -1
            );
        }

        public static ReservationResult GameOver(ColorType color)
        {
            return new ReservationResult(
                ReservationType.GameOverFullBuffer,
                color,
                targetBoxIndex: -1,
                targetSlotIndex: -1,
                bufferIndex: -1,
                causesBoxCompletion: false,
                completedBoxIndex: -1
            );
        }
    }
}

