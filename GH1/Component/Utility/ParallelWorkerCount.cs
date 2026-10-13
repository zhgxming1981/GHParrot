using System;

namespace NS_Parrot
{
    /// <summary>
    /// 为并行几何电池提供统一的工作线程数。基础值在程序集运行期间只初始化一次。
    /// </summary>
    internal static class ParallelWorkerCount
    {
        private const int MaximumWorkerCount = 14;
        private static readonly int BaseWorkerCount = Math.Max(1, Math.Min(MaximumWorkerCount, Environment.ProcessorCount - 1));

        internal static int ForWorkItems(int workItemCount)
        {
            return Math.Max(1, Math.Min(BaseWorkerCount, workItemCount));
        }
    }
}
