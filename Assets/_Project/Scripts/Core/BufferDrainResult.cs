namespace ScrewSolver.Core
{
    /// <summary>
    /// Details of a screw automatically pulled from the buffer dock into an active box.
    /// </summary>
    public readonly struct BufferDrainResult
    {
        public readonly int BufferIndex;
        public readonly int TargetBoxIndex;
        public readonly int TargetSlotIndex;
        public readonly ColorType Color;
        public readonly bool CausesChainCompletion;

        public BufferDrainResult(int bufferIndex, int targetBoxIndex, int targetSlotIndex, ColorType color, bool causesChainCompletion = false)
        {
            BufferIndex = bufferIndex;
            TargetBoxIndex = targetBoxIndex;
            TargetSlotIndex = targetSlotIndex;
            Color = color;
            CausesChainCompletion = causesChainCompletion;
        }
    }
}
