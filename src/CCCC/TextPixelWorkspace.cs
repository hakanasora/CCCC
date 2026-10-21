using System;

namespace CCCC
{
    // One workspace per toast: text is rendered serially on its dispatcher.
    // No text history, global pool, timer or forced garbage collection.
    internal sealed class TextPixelWorkspace
    {
        internal byte[] HighPixels, FinalPixels;
        internal int AllocationCount { get; private set; }
        internal long AllocatedBytes { get; private set; }
        internal int RetainedBytes
        { get { return (HighPixels == null ? 0 : HighPixels.Length) + (FinalPixels == null ? 0 : FinalPixels.Length); } }
        internal const int IdleBudget = 2 * 1024 * 1024;

        internal void Ensure(int highBytes, int finalBytes)
        {
            HighPixels = Grow(HighPixels, highBytes);
            FinalPixels = Grow(FinalPixels, finalBytes);
        }
        private byte[] Grow(byte[] current, int needed)
        {
            if (current != null && current.Length >= needed) return current;
            int capacity = checked((needed + 4095) / 4096 * 4096);
            AllocationCount++; AllocatedBytes += capacity;
            return new byte[capacity];
        }
        internal void Idle()
        {
            // Erase pixels from the last clipboard preview, retaining only a
            // small reusable allocation. Oversized/high-DPI buffers are dropped.
            if (RetainedBytes > IdleBudget) { Release(); return; }
            if (HighPixels != null) Array.Clear(HighPixels, 0, HighPixels.Length);
            if (FinalPixels != null) Array.Clear(FinalPixels, 0, FinalPixels.Length);
        }
        internal void Release() { HighPixels = FinalPixels = null; }
    }
}
