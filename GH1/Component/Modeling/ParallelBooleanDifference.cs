using CommonFunction;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace NS_Parrot
{
    /// <summary>
    /// 对每个原 Brep 一次性减去全部切削 Brep；不同原 Brep 之间并行计算。
    /// </summary>
    public class ParallelBooleanDifference : GH_Component
    {
        public ParallelBooleanDifference()
          : base("布尔差", "布尔差",
              "原物体按分支路径减去同路径的相交物体。多个原物体时并行计算，并保留数据树路径。",
              "Parrot", "Modeling")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("原物体", "原物体", "需要保留的 Brep 数据树。每个分支只使用同路径“减去的物体”分支中的 Brep。", GH_ParamAccess.tree);
            pManager.AddBrepParameter("减去的物体", "减去", "用于切削的 Brep 数据树。与“原物体”按相同分支路径一一对应；一个分支可包含多个切削 Brep。", GH_ParamAccess.tree);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("结果", "结果", "布尔差结果数据树，保留原物体的分支路径。", GH_ParamAccess.tree);
            pManager.AddTextParameter("日志", "日志", "计算日志，记录跳过的无交集物体及直接输出原物体等事件。", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            GH_Structure<GH_Brep> sourceTree;
            GH_Structure<GH_Brep> cutterTree;
            if (!DA.GetDataTree(0, out sourceTree) || sourceTree == null || sourceTree.DataCount == 0)
                return;
            if (!DA.GetDataTree(1, out cutterTree) || cutterTree == null)
                cutterTree = new GH_Structure<GH_Brep>();

            double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
            List<BooleanWorkItem> workItems = CreateWorkItems(sourceTree, cutterTree);
            List<Brep>[] resultsBySource = new List<Brep>[workItems.Count];
            List<string>[] logsBySource = new List<string>[workItems.Count];
            int workerCount = ParallelWorkerCount.ForWorkItems(workItems.Count);

            Parallel.For(0, workItems.Count, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, index =>
            {
                try
                {
                    BooleanWorkItem workItem = workItems[index];
                    Brep sourceCopy = workItem.Source.DuplicateBrep();
                    List<Brep> cutterCopies = workItem.Cutters
                        .Select(cutter => cutter.DuplicateBrep())
                        .Where(cutter => cutter != null)
                        .ToList();
                    List<string> logs = new List<string>();

                    if (cutterCopies.Count == 0)
                    {
                        resultsBySource[index] = new List<Brep> { sourceCopy };
                        logs.Add(FormatLog(workItem) + "减去物体分支为空，已直接输出原物体。");
                        logsBySource[index] = logs;
                        return;
                    }

                    List<Brep> currentResults = new List<Brep> { sourceCopy };
                    int skippedByBoundingBox = 0;
                    int appliedCutterCount = 0;
                    logs.Add(FormatLog(workItem) + "原物体诊断：" + DescribeBrep(sourceCopy));
                    for (int cutterIndex = 0; cutterIndex < cutterCopies.Count; cutterIndex++)
                    {
                        Brep cutter = cutterCopies[cutterIndex];
                        logs.Add(FormatLog(workItem) + "切削体 " + (cutterIndex + 1) + "/" + cutterCopies.Count + " 诊断：" + DescribeBrep(cutter));
                        if (!currentResults.Any(result => BoundingBoxesOverlap(result.GetBoundingBox(false), cutter.GetBoundingBox(false))))
                        {
                            skippedByBoundingBox++;
                            logs.Add(FormatLog(workItem) + "切削体 " + (cutterIndex + 1) + " 包围盒不重叠，跳过。");
                            continue;
                        }

                        List<Brep> nextResults = new List<Brep>();
                        bool applied = false;
                        for (int resultIndex = 0; resultIndex < currentResults.Count; resultIndex++)
                        {
                            Brep current = currentResults[resultIndex];
                            if (!BoundingBoxesOverlap(current.GetBoundingBox(false), cutter.GetBoundingBox(false)))
                            {
                                nextResults.Add(current);
                                continue;
                            }

                            double volumeBefore = GetVolume(current);
                            Stopwatch stopwatch = Stopwatch.StartNew();
                            Brep[] difference;
                            try
                            {
                                difference = Brep.CreateBooleanDifference(new[] { current }, new[] { cutter }, tolerance);
                            }
                            catch (Exception ex)
                            {
                                stopwatch.Stop();
                                logs.Add(FormatLog(workItem) + "切削体 " + (cutterIndex + 1) + " 对结果块 " + (resultIndex + 1) +
                                    " 的布尔调用异常，耗时 " + stopwatch.ElapsedMilliseconds + " ms：" + ex.Message + "；已保留该结果块。");
                                nextResults.Add(current);
                                continue;
                            }
                            stopwatch.Stop();

                            if (difference == null || difference.Length == 0)
                            {
                                if (IsFullyInside(current, cutter, tolerance))
                                {
                                    applied = true;
                                    logs.Add(FormatLog(workItem) + "切削体 " + (cutterIndex + 1) + " 完全包含结果块 " + (resultIndex + 1) +
                                        "，返回空结果，耗时 " + stopwatch.ElapsedMilliseconds + " ms；已移除该结果块。切削前体积=" + FormatVolume(volumeBefore) + "。");
                                    continue;
                                }

                                nextResults.Add(current);
                                logs.Add(FormatLog(workItem) + "切削体 " + (cutterIndex + 1) + " 对结果块 " + (resultIndex + 1) +
                                    " 返回空结果，耗时 " + stopwatch.ElapsedMilliseconds + " ms；已保留该结果块。切削前体积=" + FormatVolume(volumeBefore) + "。");
                                continue;
                            }

                            List<Brep> validDifference = difference.Where(brep => brep != null).ToList();
                            nextResults.AddRange(validDifference);
                            applied = true;
                            double volumeAfter = GetTotalVolume(validDifference);
                            logs.Add(FormatLog(workItem) + "切削体 " + (cutterIndex + 1) + " 对结果块 " + (resultIndex + 1) +
                                " 完成，耗时 " + stopwatch.ElapsedMilliseconds + " ms，结果 " + validDifference.Count + " 块，体积 " +
                                FormatVolume(volumeBefore) + " → " + FormatVolume(volumeAfter) + "，变化 " + FormatVolume(volumeAfter - volumeBefore) + "。");
                        }

                        currentResults = nextResults;
                        if (applied)
                            appliedCutterCount++;
                        if (currentResults.Count == 0)
                            break;
                    }

                    resultsBySource[index] = currentResults;
                    logs.Add(FormatLog(workItem) + "最终结果 " + currentResults.Count + " 块，总体积=" + FormatVolume(GetTotalVolume(currentResults)) + "。");
                    if (appliedCutterCount == 0)
                    {
                        logs.Add(FormatLog(workItem) + "没有切削体产生布尔差，已直接输出原物体。" +
                            (skippedByBoundingBox > 0 ? " 已按包围盒跳过 " + skippedByBoundingBox + " 个无交集物体。" : string.Empty));
                    }
                    else
                        logs.Add(FormatLog(workItem) + "完成逐个布尔差，成功切削 " + appliedCutterCount + " 个物体。" +
                            (skippedByBoundingBox > 0 ? " 已按包围盒跳过 " + skippedByBoundingBox + " 个无交集物体。" : string.Empty));
                    logsBySource[index] = logs;
                }
                catch (Exception ex)
                {
                    logsBySource[index] = new List<string> { FormatLog(workItems[index]) + "布尔差失败：" + ex.Message };
                    resultsBySource[index] = new List<Brep>();
                }
            });

            GH_Structure<GH_Brep> outputTree = new GH_Structure<GH_Brep>();
            GH_Structure<GH_String> logTree = new GH_Structure<GH_String>();
            foreach (GH_Path path in sourceTree.Paths)
            {
                outputTree.EnsurePath(path);
                logTree.EnsurePath(path);
            }

            for (int index = 0; index < workItems.Count; index++)
            {
                if (resultsBySource[index] == null)
                    continue;

                foreach (Brep result in resultsBySource[index])
                    outputTree.Append(new GH_Brep(result), workItems[index].Path);

                foreach (string log in logsBySource[index] ?? Enumerable.Empty<string>())
                    logTree.Append(new GH_String(log), workItems[index].Path);
            }

            DA.SetDataTree(0, outputTree);
            DA.SetDataTree(1, logTree);
        }

        private static bool BoundingBoxesOverlap(BoundingBox first, BoundingBox second)
        {
            return first.IsValid && second.IsValid &&
                   first.Min.X <= second.Max.X && first.Max.X >= second.Min.X &&
                   first.Min.Y <= second.Max.Y && first.Max.Y >= second.Min.Y &&
                   first.Min.Z <= second.Max.Z && first.Max.Z >= second.Min.Z;
        }

        private static bool IsFullyInside(Brep inner, Brep outer, double tolerance)
        {
            return inner.Vertices.Count > 0 &&
                   inner.Vertices.All(vertex => outer.IsPointInside(vertex.Location, tolerance, true));
        }

        private static string DescribeBrep(Brep brep)
        {
            if (brep == null)
                return "空 Brep。";

            return "有效=" + brep.IsValid + "，闭合=" + IsClosedBrep(brep) + "，实体=" + brep.IsSolid +
                   "，面=" + brep.Faces.Count + "，边=" + brep.Edges.Count + "，体积=" + FormatVolume(GetVolume(brep)) + "。";
        }

        private static bool IsClosedBrep(Brep brep)
        {
            try
            {
                Curve[] nakedEdges = brep?.DuplicateNakedEdgeCurves(true, true);
                return nakedEdges != null && nakedEdges.Length == 0;
            }
            catch
            {
                return false;
            }
        }

        private static double GetTotalVolume(IEnumerable<Brep> breps)
        {
            return (breps ?? Enumerable.Empty<Brep>()).Sum(GetVolume);
        }

        private static double GetVolume(Brep brep)
        {
            try
            {
                VolumeMassProperties properties = brep == null ? null : VolumeMassProperties.Compute(brep);
                return properties == null ? 0.0 : properties.Volume;
            }
            catch
            {
                return 0.0;
            }
        }

        private static string FormatVolume(double volume)
        {
            return volume.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatLog(BooleanWorkItem workItem)
        {
            return "分支 " + workItem.Path + " 的原物体 " + (workItem.SourceIndex + 1) + "：";
        }

        private static List<BooleanWorkItem> CreateWorkItems(GH_Structure<GH_Brep> sourceTree, GH_Structure<GH_Brep> cutterTree)
        {
            List<BooleanWorkItem> workItems = new List<BooleanWorkItem>();
            foreach (GH_Path path in sourceTree.Paths)
            {
                List<Brep> cutters = cutterTree.PathExists(path)
                    ? cutterTree.get_Branch(path)
                        .OfType<GH_Brep>()
                        .Where(item => item?.Value != null && item.Value.IsValid)
                        .Select(item => item.Value)
                        .ToList()
                    : new List<Brep>();

                int sourceIndex = 0;
                foreach (GH_Brep sourceItem in sourceTree.get_Branch(path).OfType<GH_Brep>())
                {
                    if (sourceItem?.Value != null && sourceItem.Value.IsValid)
                        workItems.Add(new BooleanWorkItem(path, sourceIndex, sourceItem.Value, cutters));
                    sourceIndex++;
                }
            }

            return workItems;
        }

        private sealed class BooleanWorkItem
        {
            public BooleanWorkItem(GH_Path path, int sourceIndex, Brep source, List<Brep> cutters)
            {
                Path = path;
                SourceIndex = sourceIndex;
                Source = source;
                Cutters = cutters;
            }

            public GH_Path Path { get; }
            public int SourceIndex { get; }
            public Brep Source { get; }
            public List<Brep> Cutters { get; }
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("5B5C77B1-3E5B-4DA4-9B43-23B5B225CBDC");
    }
}
