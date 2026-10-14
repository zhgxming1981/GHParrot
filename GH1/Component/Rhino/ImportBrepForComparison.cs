using CommonFunction;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NS_Parrot
{
    /// <summary>导入多个 3dm 文件中的 Brep，并可将各文件整体刚体对齐到第一个文件。</summary>
    public sealed class ImportBrepForComparison : GH_Component
    {
        private const string SourceUserStringKey = "来源";
        private bool _lastRun;
        private readonly List<Guid> _lastImportedIds = new List<Guid>();
        private string _status = "等待执行。";

        public ImportBrepForComparison()
            : base("导入Brep比较", "导入比较",
                "从 .3dm 文件列表导入 Brep；可将后续文件整体平移、旋转到第一个文件以便比较，并写入来源 UserString。",
                "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("路径列表", "Path", "待导入的 .3dm 文件完整路径列表。", GH_ParamAccess.list);
            p.AddBooleanParameter("执行", "Run", "False 变为 True 时导入。", GH_ParamAccess.item, false);
            p.AddBooleanParameter("重叠", "Overlap", "True 时，以第一个有效文件为基准，将后续文件的全部 Brep 整体平移、旋转至重叠位置。", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("GUID", "GUID", "本次导入 Brep 的 Rhino 对象 GUID。", GH_ParamAccess.list);
            p.AddTextParameter("状态", "Status", "导入结果、跳过项及对齐提示。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (!CHardware.CheckLegality()) return;

            var paths = new List<string>();
            bool run = false;
            bool overlap = false;
            if (!da.GetDataList(0, paths)) return;
            da.GetData(1, ref run);
            da.GetData(2, ref overlap);

            bool execute = run && !_lastRun;
            _lastRun = run;
            if (!execute)
            {
                da.SetDataList(0, _lastImportedIds.Select(id => new GH_Guid(id)));
                da.SetData(1, _status);
                return;
            }

            _lastImportedIds.Clear();
            var logs = new List<string>();
            var batches = new List<ImportBatch>();
            try
            {
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null) throw new InvalidOperationException("当前没有打开 Rhino 文档。" );

                foreach (string rawPath in paths)
                {
                    string path = rawPath == null ? string.Empty : rawPath.Trim();
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        logs.Add("存在空路径，已跳过。");
                        continue;
                    }

                    try { path = Path.GetFullPath(path); }
                    catch
                    {
                        logs.Add("路径无效，已跳过：" + rawPath);
                        continue;
                    }
                    if (!File.Exists(path))
                    {
                        logs.Add("文件不存在，已跳过：" + path);
                        continue;
                    }

                    ImportBatch batch = ReadBrepBatch(path);
                    if (batch.Breps.Count == 0)
                    {
                        batch.Dispose();
                        logs.Add("文件没有顶层 Brep，已跳过：" + Path.GetFileName(path));
                        continue;
                    }
                    batches.Add(batch);
                }

                if (batches.Count == 0)
                    throw new InvalidOperationException("没有可导入的 Brep。" );

                if (overlap && batches.Count > 1)
                    AlignBatchesToFirst(batches, doc.ModelAbsoluteTolerance, logs);

                foreach (ImportBatch batch in batches)
                {
                    foreach (Brep brep in batch.Breps)
                    {
                        ObjectAttributes attributes = doc.CreateDefaultAttributes();
                        attributes.SetUserString(SourceUserStringKey, batch.SourcePath);
                        Guid id = doc.Objects.AddBrep(brep, attributes);
                        if (id == Guid.Empty)
                            logs.Add("导入失败：" + Path.GetFileName(batch.SourcePath));
                        else
                            _lastImportedIds.Add(id);
                    }
                }
                doc.Views.Redraw();
                _status = "成功导入 " + _lastImportedIds.Count + " 个 Brep。";
            }
            catch (Exception ex)
            {
                _status = "导入失败：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
            }
            finally
            {
                foreach (ImportBatch batch in batches) batch.Dispose();
            }

            if (logs.Count > 0) _status += " " + string.Join("；", logs);
            da.SetDataList(0, _lastImportedIds.Select(id => new GH_Guid(id)));
            da.SetData(1, _status);
            Message = "导入 " + _lastImportedIds.Count;
        }

        private static ImportBatch ReadBrepBatch(string sourcePath)
        {
            var batch = new ImportBatch(sourcePath);
            using (File3dm file = File3dm.Read(sourcePath))
            {
                if (file == null) throw new InvalidOperationException("无法读取文件：" + sourcePath);
                foreach (File3dmObject item in file.Objects)
                {
                    Brep brep = item?.Geometry as Brep;
                    if (brep == null) continue;
                    Brep copy = brep.DuplicateBrep();
                    if (copy != null) batch.Breps.Add(copy);
                }
            }
            return batch;
        }

        private static void AlignBatchesToFirst(List<ImportBatch> batches, double tolerance, List<string> logs)
        {
            AlignmentData reference = CreateAlignmentData(batches[0].Breps);
            if (reference == null)
            {
                logs.Add("基准文件无法计算整体方向，后续文件保留原坐标。");
                return;
            }

            try
            {
                for (int i = 1; i < batches.Count; i++)
                {
                    AlignmentData candidate = CreateAlignmentData(batches[i].Breps);
                    if (candidate == null)
                    {
                        logs.Add("无法对齐：" + Path.GetFileName(batches[i].SourcePath));
                        continue;
                    }
                    try
                    {
                        Transform transform = FindBestRigidTransform(reference, candidate, batches[0].Breps, batches[i].Breps);
                        foreach (Brep brep in batches[i].Breps) brep.Transform(transform);
                        double error = AverageSurfaceDistance(batches[i].Breps, batches[0].Breps);
                        if (error > Math.Max(tolerance, 1e-6) * 10)
                            logs.Add("对齐后仍有明显差异：" + Path.GetFileName(batches[i].SourcePath));
                    }
                    finally { candidate.Dispose(); }
                }
            }
            finally { reference.Dispose(); }
        }

        private static AlignmentData CreateAlignmentData(IList<Brep> breps)
        {
            var points = new List<Point3d>();
            foreach (Brep brep in breps)
            {
                Point3d[] samples = SamplePoints(brep, 300);
                if (samples != null) points.AddRange(samples);
            }
            if (points.Count < 4) return null;

            Point3d centroid = new Point3d(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
            double[,] covariance = new double[3, 3];
            foreach (Point3d point in points)
            {
                double[] v = { point.X - centroid.X, point.Y - centroid.Y, point.Z - centroid.Z };
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3; col++) covariance[row, col] += v[row] * v[col];
            }
            PrincipalAxisResult axisResult = PrincipalAxes(covariance);
            if (axisResult == null) return null;
            return new AlignmentData(centroid, axisResult.Axes, axisResult.Values, points.ToArray());
        }

        private static Transform FindBestRigidTransform(AlignmentData reference, AlignmentData candidate,
            IList<Brep> referenceBreps, IList<Brep> candidateBreps)
        {
            int[][] signs =
            {
                new[] { 1, 1, 1 }, new[] { -1, -1, 1 },
                new[] { -1, 1, -1 }, new[] { 1, -1, -1 }
            };
            int[][] permutations =
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
            };
            Transform best = Transform.Identity;
            double bestError = double.MaxValue;
            foreach (int[] permutation in permutations)
            foreach (int[] sign in signs)
            {
                Vector3d targetX = reference.Axes[permutation[0]] * sign[0];
                Vector3d targetY = reference.Axes[permutation[1]] * sign[1];
                Plane source = new Plane(candidate.Centroid, candidate.Axes[0], candidate.Axes[1]);
                Plane destination = new Plane(reference.Centroid, targetX, targetY);
                if (!source.IsValid || !destination.IsValid) continue;
                Transform transform = Transform.PlaneToPlane(source, destination);
                TryKeepBetterTransform(candidate.SamplePoints, referenceBreps, transform, ref best, ref bestError);

                // 两个主值接近时代表可能存在圆柱、球等旋转对称性。
                // 主轴只能确定对称轴，无法确定绕轴角度，故再扫描一周的离散姿态。
                if (HasRotationalSymmetry(reference.Values))
                {
                    Vector3d spinAxis = reference.Axes[permutation[2]];
                    for (int step = 1; step < 24; step++)
                    {
                        Transform spin = Transform.Rotation(Math.PI * 2.0 * step / 24.0, spinAxis, reference.Centroid);
                        TryKeepBetterTransform(candidate.SamplePoints, referenceBreps, spin * transform, ref best, ref bestError);
                    }
                }
            }
            return best;
        }

        private static void TryKeepBetterTransform(Point3d[] source, IList<Brep> targetBreps,
            Transform transform, ref Transform best, ref double bestError)
        {
            double total = 0;
            foreach (Point3d rawPoint in source)
            {
                Point3d point = rawPoint;
                point.Transform(transform);
                total += NearestSurfaceDistance(point, targetBreps);
            }
            double error = total / source.Length;
            if (error < bestError) { bestError = error; best = transform; }
        }

        private static bool HasRotationalSymmetry(double[] values)
        {
            if (values == null || values.Length != 3) return false;
            double largest = Math.Max(values[0], Math.Max(values[1], values[2]));
            if (largest <= 1e-12) return false;
            return Math.Abs(values[0] - values[1]) / largest < 0.02
                || Math.Abs(values[1] - values[2]) / largest < 0.02
                || Math.Abs(values[0] - values[2]) / largest < 0.02;
        }

        private static double AverageSurfaceDistance(IList<Brep> sourceBreps, IList<Brep> targetBreps)
        {
            var points = new List<Point3d>();
            foreach (Brep brep in sourceBreps) points.AddRange(SamplePoints(brep, 50));
            return points.Count == 0 ? 0 : points.Average(point => NearestSurfaceDistance(point, targetBreps));
        }

        private static double NearestSurfaceDistance(Point3d point, IList<Brep> breps)
        {
            double result = double.MaxValue;
            foreach (Brep brep in breps)
            {
                Point3d closest = brep.ClosestPoint(point);
                double distance = point.DistanceTo(closest);
                if (distance < result) result = distance;
            }
            return result;
        }

        private static Point3d[] SamplePoints(Brep brep, int maximum)
        {
            Mesh[] meshes = Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh);
            if (meshes == null) return new Point3d[0];
            try
            {
                var result = new List<Point3d>();
                foreach (Mesh mesh in meshes)
                    foreach (Point3f point in mesh.Vertices)
                        result.Add(new Point3d(point.X, point.Y, point.Z));
                if (result.Count <= maximum) return result.ToArray();
                double step = (double)(result.Count - 1) / (maximum - 1);
                return Enumerable.Range(0, maximum).Select(i => result[(int)Math.Round(i * step)]).ToArray();
            }
            finally
            {
                foreach (Mesh mesh in meshes) mesh?.Dispose();
            }
        }

        // Jacobi 特征分解：协方差矩阵为实对称矩阵，结果按最大特征值对应的主方向排序。
        private static PrincipalAxisResult PrincipalAxes(double[,] matrix)
        {
            double[,] a = (double[,])matrix.Clone();
            double[,] v = { { 1d, 0d, 0d }, { 0d, 1d, 0d }, { 0d, 0d, 1d } };
            for (int iteration = 0; iteration < 32; iteration++)
            {
                int p = 0, q = 1;
                if (Math.Abs(a[0, 2]) > Math.Abs(a[p, q])) { p = 0; q = 2; }
                if (Math.Abs(a[1, 2]) > Math.Abs(a[p, q])) { p = 1; q = 2; }
                if (Math.Abs(a[p, q]) < 1e-12) break;
                double angle = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
                double c = Math.Cos(angle), s = Math.Sin(angle);
                for (int index = 0; index < 3; index++)
                {
                    double api = a[p, index], aqi = a[q, index];
                    a[p, index] = c * api - s * aqi;
                    a[q, index] = s * api + c * aqi;
                }
                for (int index = 0; index < 3; index++)
                {
                    double aip = a[index, p], aiq = a[index, q];
                    a[index, p] = c * aip - s * aiq;
                    a[index, q] = s * aip + c * aiq;
                    double vip = v[index, p], viq = v[index, q];
                    v[index, p] = c * vip - s * viq;
                    v[index, q] = s * vip + c * viq;
                }
            }
            int[] order = Enumerable.Range(0, 3).OrderByDescending(i => a[i, i]).ToArray();
            var axes = order.Select(i => new Vector3d(v[0, i], v[1, i], v[2, i])).ToArray();
            if (axes.Any(axis => !axis.Unitize())) return null;
            if (Vector3d.CrossProduct(axes[0], axes[1]) * axes[2] < 0) axes[2] = -axes[2];
            return new PrincipalAxisResult { Axes = axes, Values = order.Select(i => a[i, i]).ToArray() };
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_SelectRhinoObjectByLayerRun");
        public override Guid ComponentGuid => new Guid("A884D2B2-993D-4ED5-8DCF-CC08741A6B19");

        private sealed class ImportBatch : IDisposable
        {
            internal readonly string SourcePath;
            internal readonly List<Brep> Breps = new List<Brep>();
            internal ImportBatch(string sourcePath) { SourcePath = sourcePath; }
            public void Dispose() { foreach (Brep brep in Breps) brep?.Dispose(); Breps.Clear(); }
        }

        private sealed class AlignmentData : IDisposable
        {
            internal readonly Point3d Centroid;
            internal readonly Vector3d[] Axes;
            internal readonly double[] Values;
            internal readonly Point3d[] SamplePoints;
            internal AlignmentData(Point3d centroid, Vector3d[] axes, double[] values, Point3d[] samplePoints)
            { Centroid = centroid; Axes = axes; Values = values; SamplePoints = samplePoints; }
            public void Dispose() { }
        }

        private sealed class PrincipalAxisResult
        {
            internal Vector3d[] Axes;
            internal double[] Values;
        }
    }
}
