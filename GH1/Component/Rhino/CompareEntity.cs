using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class CompareEntity : GH_Component
    {
        internal bool ButtonRun { get; set; }
        internal enum ButtonColor { Black, Grey }
        internal ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;

        private List<Guid> _lastResult = new List<Guid>();
        private string _status = string.Empty;

        private const double PrincipalAxisCloseRatio = 0.02;
        private const int MaxValidationSamples = 10000;

        public CompareEntity()
            : base("比较实体", "比较实体",
                  "以第一个文件中的实体为基准，将其它文件中的指定实体对齐后Bake到Rhino",
                  "Parrot", "Rhino")
        {
        }

        public override void CreateAttributes()
        {
            m_attributes = new CompareEntity_Attributes(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件路径列表", "文件",
                "待比较的 .3dm 文件路径", GH_ParamAccess.list);
            pManager.AddTextParameter("图层", "图层",
                "要搜索的图层名列表，同一文件在所有指定图层中搜索到的实体总数应为1",
                GH_ParamAccess.list);
            pManager.AddBooleanParameter("旋转对齐", "旋转",
                "开启后，将后续实体旋转对齐到第一个实体的主轴方向",
                GH_ParamAccess.item, false);
            pManager.AddNumberParameter("重合容差", "容差",
                "旋转对齐的几何重合容差。不输入时使用当前Rhino文档的绝对容差",
                GH_ParamAccess.item);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("已Bake实体的GUID", "GUID",
                "Bake 后各实体在Rhino文档中的 GUID", GH_ParamAccess.list);
            pManager.AddTextParameter("状态", "状态",
                "操作日志或错误信息", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> filePaths = new List<string>();
            List<string> layerNames = new List<string>();
            bool rotateAlign = false;
            double snapTolerance = -1;

            if (!DA.GetDataList(0, filePaths)) { return; }
            DA.GetDataList(1, layerNames);
            DA.GetData(2, ref rotateAlign);
            DA.GetData(3, ref snapTolerance);

            if (!ButtonRun)
            {
                DA.SetDataList(0, _lastResult.Select(g => new GH_Guid(g)));
                DA.SetData(1, _status);
                return;
            }

            ButtonRun = false;
            _lastResult.Clear();
            _status = string.Empty;

            var logs = new List<string>();

            var geometries = new List<GeometryBase>();
            var names = new List<string>();
            try
            {
            if (filePaths.Count == 0)
                {
                    _status = "输入列表为空";
                    DA.SetDataList(0, new List<GH_Guid>());
                    DA.SetData(1, _status);
                    return;
                }

                if (layerNames.Count == 0)
                {
                    _status = "图层列表为空";
                    DA.SetDataList(0, new List<GH_Guid>());
                    DA.SetData(1, _status);
                    return;
                }

                // 读取每个文件指定图层的实体
                for (int i = 0; i < filePaths.Count; i++)
                {
                    string rawPath = filePaths[i];
                    string filePath = rawPath?.Trim();

                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        logs.Add("第" + i + "个文件路径为空，跳过");
                        continue;
                    }

                    try
                    {
                        filePath = Path.GetFullPath(filePath);
                    }
                    catch
                    {
                        logs.Add("第" + i + "个文件路径无效：" + rawPath + "，跳过");
                        continue;
                    }

                    if (!File.Exists(filePath))
                    {
                        logs.Add("文件不存在，跳过：" + filePath);
                        continue;
                    }

                    GeometryBase geo = null;
                    using (File3dm file = File3dm.Read(filePath))
                    {
                        if (file == null)
                        {
                            logs.Add("无法读取文件，跳过：" + filePath);
                            continue;
                        }

                        HashSet<int> matchedLayerIndices = FindSourceLayerIndices(
                            file, layerNames, logs, filePath);
                        if (matchedLayerIndices.Count == 0)
                            continue;

                        List<File3dmObject> layerObjects = new List<File3dmObject>();
                        foreach (File3dmObject obj in file.Objects)
                        {
                            if (obj?.Attributes == null || obj.Geometry == null)
                                continue;
                            if (matchedLayerIndices.Contains(obj.Attributes.LayerIndex))
                                layerObjects.Add(obj);
                        }

                        if (layerObjects.Count != 1)
                        {
                            logs.Add("文件 " + Path.GetFileName(filePath)
                                + " 图层列表中共有 " + layerObjects.Count
                                + " 个实体（需要恰好1个），跳过");
                            continue;
                        }

                        geo = layerObjects[0].Geometry.Duplicate();
                    }

                    if (geo == null)
                    {
                        logs.Add("无法复制文件 " + Path.GetFileName(filePath)
                            + " 指定图层的几何体，跳过");
                        continue;
                    }

                    geometries.Add(geo);
                    names.Add(GetRelativePath(filePath));
                }

                if (geometries.Count == 0)
                {
                    _status = "未获取到任何有效实体。" + string.Join("；", logs);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _status);
                    DA.SetDataList(0, new List<GH_Guid>());
                    DA.SetData(1, _status);
                    return;
                }

                // 以第一个实体为基准对齐
                BoundingBox baseBBox = geometries[0].GetBoundingBox(true);
                if (!baseBBox.IsValid)
                {
                    _status = "基准实体的包围盒无效";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
                    DA.SetDataList(0, new List<GH_Guid>());
                    DA.SetData(1, _status);
                    return;
                }

                Point3d baseCenter = baseBBox.Center;

                // 旋转对齐
                if (rotateAlign)
                {
                    if (snapTolerance <= 0)
                    {
                        var rhinoDoc = RhinoDoc.ActiveDoc;
                        snapTolerance = rhinoDoc != null
                            ? rhinoDoc.ModelAbsoluteTolerance
                            : 0.1;
                    }
                    if (snapTolerance <= 0)
                        snapTolerance = 0.1;

                    var baseResult = ExtractPrincipalAxes(geometries[0]);
                    if (baseResult == null)
                    {
                        logs.Add("基准实体的惯量主轴计算失败，跳过旋转对齐");
                    }
                    else
                    {
                        // 基准实体只采样一次
                        var (baseBrep, ownsBaseBrep) = GetBrepForSampling(geometries[0]);
                        Point3d[] baseSamples = baseBrep != null
                            ? SampleMeshPoints(baseBrep)
                            : null;

                        try
                        {
                            for (int i = 1; i < geometries.Count; i++)
                            {
                                var targetResult = ExtractPrincipalAxes(geometries[i]);
                                if (targetResult == null)
                                {
                                    logs.Add("第" + i + "个实体的惯量主轴计算失败，仅中心对齐");
                                    continue;
                                }

                                // 目标实体只采样一次
                                var (targetBrep, ownsTargetBrep) = GetBrepForSampling(geometries[i]);
                                Point3d[] targetSamples = targetBrep != null
                                    ? SampleMeshPoints(targetBrep)
                                    : null;

                                try
                                {
                                    GeometryBase rotated = TryAlignWithRotation(
                                        baseResult, targetResult,
                                        geometries[i],
                                        baseSamples, targetSamples, baseBrep,
                                        baseCenter, snapTolerance);

                                    if (rotated == null)
                                    {
                                        logs.Add("第" + i + "个实体：可能为镜像、不同件或对齐误差过大；未执行旋转，仅中心对齐");
                                    }
                                    else
                                    {
                                        GeometryBase oldGeo = geometries[i];
                                        geometries[i] = rotated;
                                        if (oldGeo != null) oldGeo.Dispose();
                                    }
                                }
                                finally
                                {
                                    if (ownsTargetBrep && targetBrep != null)
                                        targetBrep.Dispose();
                                }
                            }
                        }
                        finally
                        {
                            if (ownsBaseBrep && baseBrep != null)
                                baseBrep.Dispose();
                        }
                    }
                }

                // 平移对齐（包围盒中心重合）
                for (int i = 1; i < geometries.Count; i++)
                {
                    BoundingBox currentBBox = geometries[i].GetBoundingBox(true);
                    if (!currentBBox.IsValid)
                    {
                        logs.Add("第" + i + "个实体包围盒无效，跳过对齐");
                        continue;
                    }

                    Vector3d offset = baseCenter - currentBBox.Center;
                    geometries[i].Translate(offset);
                }

                // Bake 到当前 Rhino 文档
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                {
                    _status = "没有打开的 Rhino 文档";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
                    DA.SetDataList(0, new List<GH_Guid>());
                    DA.SetData(1, _status);
                    return;
                }

                for (int i = 0; i < geometries.Count; i++)
                {
                    try
                    {
                        ObjectAttributes attrs = doc.CreateDefaultAttributes();
                        attrs.Name = names[i];

                        Guid bakedId = doc.Objects.Add(geometries[i], attrs);
                        if (bakedId == Guid.Empty)
                        {
                            logs.Add("第" + i + "个实体 Bake 失败（" + names[i] + "）");
                        }
                        else
                        {
                            _lastResult.Add(bakedId);
                        }
                    }
                    catch (Exception ex)
                    {
                        logs.Add("第" + i + "个实体 Bake 异常：" + ex.Message);
                    }
                }

                doc.Views.Redraw();

                _status = "成功 Bake " + _lastResult.Count + " 个实体";
                if (logs.Count > 0)
                    _status += "。" + string.Join("；", logs);
                DA.SetDataList(0, _lastResult.Select(g => new GH_Guid(g)));
                DA.SetData(1, _status);
            }
            catch (Exception ex)
            {
                _status = "异常：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
                DA.SetDataList(0, new List<GH_Guid>());
                DA.SetData(1, _status);
            }
            finally
            {
                foreach (GeometryBase geo in geometries)
                {
                    if (geo != null) geo.Dispose();
                }
                geometries.Clear();
            }
        }

        /// <summary>取路径的末尾最多3级作为相对路径。</summary>
        private static string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return string.Empty;

            char[] separators = { '\\', '/' };
            string[] parts = fullPath.Split(separators, StringSplitOptions.RemoveEmptyEntries);

            int take = Math.Min(3, parts.Length);
            string[] tail = parts.Skip(parts.Length - take).ToArray();
            tail[tail.Length - 1] = Path.GetFileNameWithoutExtension(tail[tail.Length - 1]);
            return string.Join("\\", tail);
        }

        /// <summary>先匹配 FullPath，再匹配短 Name，* 后缀做前缀匹配。</summary>
        private static HashSet<int> FindSourceLayerIndices(
            File3dm file, List<string> layerNames, List<string> logs, string filePath)
        {
            var result = new HashSet<int>();

            foreach (string rawName in layerNames)
            {
                string layerName = rawName?.Trim();
                if (string.IsNullOrWhiteSpace(layerName))
                    continue;

                if (layerName.EndsWith("*", StringComparison.Ordinal))
                {
                    string prefix = layerName.Substring(0, layerName.Length - 1);
                    bool found = false;
                    foreach (Layer layer in file.AllLayers)
                    {
                        string fullPath = layer.FullPath ?? layer.Name ?? "";
                        if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(layer.Index);
                            found = true;
                        }
                    }
                    if (!found)
                        logs.Add("文件 " + Path.GetFileName(filePath)
                            + " 中找不到匹配图层：「" + layerName + "」");
                    continue;
                }

                List<Layer> fullPathMatches = new List<Layer>();
                foreach (Layer layer in file.AllLayers)
                {
                    if (string.Equals(layer.FullPath, layerName, StringComparison.OrdinalIgnoreCase))
                        fullPathMatches.Add(layer);
                }
                if (fullPathMatches.Count > 0)
                {
                    foreach (Layer layer in fullPathMatches)
                        result.Add(layer.Index);
                    continue;
                }

                List<Layer> nameMatches = new List<Layer>();
                foreach (Layer layer in file.AllLayers)
                {
                    if (string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase))
                        nameMatches.Add(layer);
                }
                if (nameMatches.Count == 0)
                {
                    logs.Add("文件 " + Path.GetFileName(filePath)
                        + " 中找不到图层：「" + layerName + "」");
                    continue;
                }
                foreach (Layer layer in nameMatches)
                    result.Add(layer.Index);
            }

            return result;
        }

        // ---- 旋转对齐相关方法 ----

        private sealed class PrincipalAxesResult
        {
            public double[] Inertias;
            public Vector3d[] Axes;
            public Point3d Centroid;
        }

        private static PrincipalAxesResult ExtractPrincipalAxes(GeometryBase geo)
        {
            if (geo == null) return null;

            VolumeMassProperties vmp = null;
            if (geo is Brep brep)
                vmp = VolumeMassProperties.Compute(brep, true, true, true, true);
            else if (geo is Mesh mesh)
                vmp = VolumeMassProperties.Compute(mesh);

            if (vmp == null)
                return ExtractPrincipalAxesViaArea(geo);

            try
            {
                if (!vmp.CentroidCoordinatesPrincipalMomentsOfInertia(
                    out double vi0, out Vector3d va0,
                    out double vi1, out Vector3d va1,
                    out double vi2, out Vector3d va2))
                    return null;

                double[] vinertias = { vi0, vi1, vi2 };
                Vector3d[] vaxes = { va0, va1, va2 };
                int[] vorder = Enumerable.Range(0, 3).OrderBy(j => vinertias[j]).ToArray();
                return new PrincipalAxesResult
                {
                    Inertias = vorder.Select(j => vinertias[j]).ToArray(),
                    Axes = vorder.Select(j => vaxes[j]).Select(a => { a.Unitize(); return a; }).ToArray(),
                    Centroid = vmp.Centroid
                };
            }
            finally
            {
                vmp.Dispose();
            }
        }

        private static PrincipalAxesResult ExtractPrincipalAxesViaArea(GeometryBase geo)
        {
            AreaMassProperties amp = null;
            Brep tempBrep = null;

            try
            {
                if (geo is Brep b)
                    amp = AreaMassProperties.Compute(b, true, true, true, true);
                else if (geo is Mesh m)
                    amp = AreaMassProperties.Compute(m);
                else if (geo is Surface s)
                {
                    tempBrep = s.ToBrep();
                    amp = AreaMassProperties.Compute(tempBrep, true, true, true, true);
                }
                else if (geo is Extrusion e)
                {
                    tempBrep = e.ToBrep();
                    amp = AreaMassProperties.Compute(tempBrep, true, true, true, true);
                }

                if (amp == null) return null;
                if (!amp.CentroidCoordinatesPrincipalMomentsOfInertia(
                    out double i0, out Vector3d a0,
                    out double i1, out Vector3d a1,
                    out double i2, out Vector3d a2))
                    return null;

                double[] inertias = { i0, i1, i2 };
                Vector3d[] axes = { a0, a1, a2 };
                int[] order = Enumerable.Range(0, 3).OrderBy(j => inertias[j]).ToArray();
                return new PrincipalAxesResult
                {
                    Inertias = order.Select(j => inertias[j]).ToArray(),
                    Axes = order.Select(j => axes[j]).Select(a => { a.Unitize(); return a; }).ToArray(),
                    Centroid = amp.Centroid
                };
            }
            finally
            {
                if (amp != null) amp.Dispose();
                if (tempBrep != null) tempBrep.Dispose();
            }
        }

        private static (Brep brep, bool owns) GetBrepForSampling(GeometryBase geo)
        {
            if (geo is Brep brep) return (brep, false);
            if (geo is Extrusion ext) return (ext.ToBrep(), true);
            if (geo is Surface s) return (s.ToBrep(), true);
            if (geo is Mesh mesh) return (Brep.CreateFromMesh(mesh, true), true);
            return (null, false);
        }

        /// <summary>网格采样顶点 + 面中心。统一使用 QualityRenderMesh。</summary>
        private static Point3d[] SampleMeshPoints(Brep brep)
        {
            MeshingParameters mp = MeshingParameters.QualityRenderMesh;
            if (mp == null) mp = MeshingParameters.Default;

            Mesh[] meshes = Mesh.CreateFromBrep(brep, mp);
            if (meshes == null || meshes.Length == 0)
                return null;

            var points = new List<Point3d>();
            try
            {
                foreach (Mesh mesh in meshes)
                {
                    foreach (Point3f v in mesh.Vertices)
                        points.Add(new Point3d(v.X, v.Y, v.Z));

                    for (int f = 0; f < mesh.Faces.Count; f++)
                    {
                        Point3d center;
                        if (mesh.Faces[f].IsTriangle)
                        {
                            center = new Point3d(
                                mesh.Vertices[mesh.Faces[f].A].X + mesh.Vertices[mesh.Faces[f].B].X + mesh.Vertices[mesh.Faces[f].C].X,
                                mesh.Vertices[mesh.Faces[f].A].Y + mesh.Vertices[mesh.Faces[f].B].Y + mesh.Vertices[mesh.Faces[f].C].Y,
                                mesh.Vertices[mesh.Faces[f].A].Z + mesh.Vertices[mesh.Faces[f].B].Z + mesh.Vertices[mesh.Faces[f].C].Z);
                            center /= 3.0;
                        }
                        else
                        {
                            center = new Point3d(
                                mesh.Vertices[mesh.Faces[f].A].X + mesh.Vertices[mesh.Faces[f].B].X + mesh.Vertices[mesh.Faces[f].C].X + mesh.Vertices[mesh.Faces[f].D].X,
                                mesh.Vertices[mesh.Faces[f].A].Y + mesh.Vertices[mesh.Faces[f].B].Y + mesh.Vertices[mesh.Faces[f].C].Y + mesh.Vertices[mesh.Faces[f].D].Y,
                                mesh.Vertices[mesh.Faces[f].A].Z + mesh.Vertices[mesh.Faces[f].B].Z + mesh.Vertices[mesh.Faces[f].C].Z + mesh.Vertices[mesh.Faces[f].D].Z);
                            center /= 4.0;
                        }
                        points.Add(center);
                    }
                }

                if (points.Count > MaxValidationSamples)
                    points = ThinPointsUniform(points, MaxValidationSamples);

                return points.ToArray();
            }
            finally
            {
                foreach (Mesh mesh in meshes)
                {
                    if (mesh != null) mesh.Dispose();
                }
            }
        }

        /// <summary>均匀间隔采样点，保留 count 个。</summary>
        private static List<Point3d> ThinPointsUniform(List<Point3d> points, int count)
        {
            if (points.Count <= count) return points;
            var result = new List<Point3d>(count);
            double step = (double)(points.Count - 1) / (count - 1);
            for (int i = 0; i < count; i++)
                result.Add(points[(int)Math.Round(i * step)]);
            return result;
        }

        /// <summary>双向最大误差：max(baseSamples→candidateBrep 最远, targetSamplesTransformed→baseBrep 最远)。超过 cutoff 立即返回。</summary>
        private static double ComputeBidirectionalMaxError(
            Point3d[] baseSamples, Brep candidateBrep,
            Point3d[] transformedSamples, Brep baseBrep,
            double cutoff)
        {
            if (baseSamples == null || baseBrep == null
                || transformedSamples == null || candidateBrep == null)
                return double.MaxValue;

            double maxDist = 0;

            foreach (Point3d p in baseSamples)
            {
                Point3d closest = candidateBrep.ClosestPoint(p);
                double d = p.DistanceTo(closest);
                if (d > maxDist) maxDist = d;
                if (maxDist > cutoff) return maxDist;
            }

            foreach (Point3d p in transformedSamples)
            {
                Point3d closest = baseBrep.ClosestPoint(p);
                double d = p.DistanceTo(closest);
                if (d > maxDist) maxDist = d;
                if (maxDist > cutoff) return maxDist;
            }

            return maxDist;
        }

        /// <summary>候选姿态枚举 + 几何重合校验。所有候选超容差时返回 null。</summary>
        private static GeometryBase TryAlignWithRotation(
            PrincipalAxesResult baseAxes,
            PrincipalAxesResult targetAxes,
            GeometryBase targetGeo,
            Point3d[] baseSamples,
            Point3d[] targetSamples,
            Brep baseBrep,
            Point3d baseCenter,
            double tolerance)
        {
            bool close01 = false, close12 = false, close02 = false;
            for (int i = 0; i < 3; i++)
            {
                double maxRef = Math.Max(baseAxes.Inertias[i], targetAxes.Inertias[i]);
                double t = PrincipalAxisCloseRatio * Math.Max(maxRef, 1e-12);
                bool bClose = i > 0
                    && Math.Abs(baseAxes.Inertias[i] - baseAxes.Inertias[i - 1]) < t;
                bool tClose = i > 0
                    && Math.Abs(targetAxes.Inertias[i] - targetAxes.Inertias[i - 1]) < t;
                if (i == 1 && (bClose || tClose)) close01 = true;
                if (i == 2 && (bClose || tClose)) close12 = true;
            }
            {
                double maxRef = Math.Max(baseAxes.Inertias[2], targetAxes.Inertias[2]);
                double t = PrincipalAxisCloseRatio * Math.Max(maxRef, 1e-12);
                bool bClose = Math.Abs(baseAxes.Inertias[2] - baseAxes.Inertias[0]) < t;
                bool tClose = Math.Abs(targetAxes.Inertias[2] - targetAxes.Inertias[0]) < t;
                close02 = bClose || tClose;
            }

            // 三轴正负翻转共 8 种，其中 4 种改变手性等同镜像，只枚举 4 种右手组合
            int[][] signCombos = new[]
            {
                new[] {  1,  1,  1 },
                new[] { -1, -1,  1 },
                new[] { -1,  1, -1 },
                new[] {  1, -1, -1 },
            };

            // 只有惯量接近的轴允许交换；三轴都接近时才枚举全部 6 种排列
            var perms = new List<int[]> { new[] { 0, 1, 2 } };
            if (close01) perms.Add(new[] { 1, 0, 2 });
            if (close12) perms.Add(new[] { 0, 2, 1 });
            if (close02) perms.Add(new[] { 2, 1, 0 });
            if (close01 && close12 && close02)
            {
                perms.Add(new[] { 1, 2, 0 });
                perms.Add(new[] { 2, 0, 1 });
            }

            BoundingBox targetBBox = targetGeo.GetBoundingBox(true);
            Point3d targetCenter = targetBBox.Center;
            Vector3d toOrigin = Point3d.Origin - targetCenter;
            Vector3d fromOrigin = baseCenter - Point3d.Origin;

            double bestError = double.MaxValue;
            GeometryBase bestGeo = null;

            try
            {
            foreach (int[] perm in perms)
            foreach (int[] signs in signCombos)
            {
                Vector3d uTarget = baseAxes.Axes[perm[0]] * signs[0];
                Vector3d vTarget = baseAxes.Axes[perm[1]] * signs[1];
                Vector3d wTarget = Vector3d.CrossProduct(uTarget, vTarget);

                double dotCheck = wTarget * (baseAxes.Axes[perm[2]] * signs[2]);
                if (dotCheck < 0) continue;

                Plane srcPlane = new Plane(Point3d.Origin,
                    targetAxes.Axes[0], targetAxes.Axes[1]);
                Plane dstPlane = new Plane(Point3d.Origin, uTarget, vTarget);

                if (!srcPlane.IsValid || !dstPlane.IsValid)
                    continue;

                Transform rotate = Transform.PlaneToPlane(srcPlane, dstPlane);

                GeometryBase candidate = targetGeo.Duplicate();
                Brep candidateBrep = null;
                bool ownsCandidateBrep = false;
                bool keepCandidate = false;

                try
                {
                    candidate.Translate(toOrigin);
                    candidate.Transform(rotate);
                    candidate.Translate(fromOrigin);

                    if (baseSamples != null && targetSamples != null
                        && baseBrep != null)
                    {
                        (candidateBrep, ownsCandidateBrep) = GetBrepForSampling(candidate);
                        if (candidateBrep == null) continue;

                        Point3d[] transformedSamples = new Point3d[targetSamples.Length];
                        for (int k = 0; k < targetSamples.Length; k++)
                        {
                            Point3d p = targetSamples[k];
                            p += toOrigin;
                            p.Transform(rotate);
                            p += fromOrigin;
                            transformedSamples[k] = p;
                        }

                        double cutoff = Math.Min(tolerance, bestError);
                        double error = ComputeBidirectionalMaxError(
                            baseSamples, candidateBrep,
                            transformedSamples, baseBrep,
                            cutoff);

                        if (error < bestError)
                        {
                            if (bestGeo != null) bestGeo.Dispose();
                            bestGeo = candidate;
                            bestError = error;
                            keepCandidate = true;
                        }
                    }
                    else
                    {
                        if (bestGeo == null)
                        {
                            bestGeo = candidate;
                            keepCandidate = true;
                        }
                    }
                }
                finally
                {
                    if (ownsCandidateBrep)
                        candidateBrep?.Dispose();

                    if (!keepCandidate)
                        candidate?.Dispose();
                }
            }

            if (bestError > tolerance)
            {
                bestGeo?.Dispose();
                bestGeo = null;
                return null;
            }

            // 所有权移交调用方，不再在 finally 中释放
            GeometryBase result = bestGeo;
            bestGeo = null;
            return result;
            }
            finally
            {
                bestGeo?.Dispose();
            }
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.GetCompareEntity(); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("7A2E5F1C-9D4B-4A81-B3E6-8F0C12D5A678"); }
        }
    }

    internal sealed class CompareEntity_Attributes : GH_ComponentAttributes
    {
        public CompareEntity_Attributes(CompareEntity owner) : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 20f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            CompareEntity owner = (CompareEntity)Owner;
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20f);
            buttonRect.Inflate(-5f, -2f);

            GH_Palette palette = owner.CurrentButtonColor == CompareEntity.ButtonColor.Black
                ? GH_Palette.Black
                : GH_Palette.Grey;

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
            {
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            }

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                graphics.DrawString("Bake", font, Brushes.White, buttonRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(
            GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(
                Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20f);

            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                CompareEntity owner = (CompareEntity)Owner;
                owner.CurrentButtonColor = CompareEntity.ButtonColor.Grey;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                CMath.Delay(50);
                owner.CurrentButtonColor = CompareEntity.ButtonColor.Black;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
