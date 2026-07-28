using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NS_Parrot
{
    // ========================================================================
    // 共享工具
    // ========================================================================
    internal static class WbUtil
    {
        /// <summary>容差百分比比较：|a-b| &lt;= max(|a|,|b|,1e-9) * |p| / 100</summary>
        internal static bool Percent(double a, double b, double p)
        {
            double s = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-9);
            return Math.Abs(a - b) <= s * Math.Abs(p) / 100.0;
        }

        /// <summary>根据相对/绝对模式选择比较方式</summary>
        internal static bool WithinTolerance(double a, double b, double tol, bool isRelative) =>
            isRelative ? Percent(a, b, tol) : Math.Abs(a - b) <= Math.Abs(tol);

        internal static double Round(double x, int d) =>
            Math.Round(x, Math.Max(0, Math.Min(12, d)), MidpointRounding.AwayFromZero);

        /// <summary>全精度格式，用于序列化</summary>
        internal static string FR(double x) => x.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>可读格式，用于诊断</summary>
        internal static string FD(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);

        internal static string PointText(Point3d p) =>
            "(" + FD(p.X) + "," + FD(p.Y) + "," + FD(p.Z) + ")";

        internal static string VectorText(Vector3d v) =>
            "(" + FD(v.X) + "," + FD(v.Y) + "," + FD(v.Z) + ")";

        internal static string B64(string x) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(x ?? string.Empty));

        /// <summary>材质/颜色匹配。严格模式：双方均非空且相等，或双方均为空才通过；宽松模式：任一方为空即通过。</summary>
        internal static bool SameOptionalText(string a, string b, bool strict)
        {
            bool aEmpty = string.IsNullOrWhiteSpace(a);
            bool bEmpty = string.IsNullOrWhiteSpace(b);
            if (strict) return (aEmpty && bEmpty) || (!aEmpty && !bEmpty && string.Equals(a, b, StringComparison.Ordinal));
            return aEmpty || bEmpty || string.Equals(a, b, StringComparison.Ordinal);
        }

        /// <summary>
        /// 二分图完美匹配（DFS 增广路径）。
        /// 要求两侧元素数量相等，每个左侧元素必须找到互不冲突的右侧元素。
        /// 所有采样参数均为硬编码常量，保证跨文件、跨时间的绝对稳定。
        /// </summary>
        internal static bool TryBipartiteMatch<T>(
            List<T> left, List<T> right,
            Func<T, T, bool> compatible)
        {
            int n = left.Count;
            if (n == 0) return true;
            int[] assigned = Enumerable.Repeat(-1, n).ToArray();
            for (int i = 0; i < n; i++)
            {
                if (!DfsAssign(i, left, right, compatible, assigned, new bool[n]))
                    return false;
            }
            return true;
        }

        private static bool DfsAssign<T>(
            int leftIdx, List<T> left, List<T> right,
            Func<T, T, bool> compatible,
            int[] assigned, bool[] visited)
        {
            for (int j = 0; j < right.Count; j++)
            {
                if (visited[j] || !compatible(left[leftIdx], right[j]))
                    continue;
                visited[j] = true;
                if (assigned[j] < 0 ||
                    DfsAssign(assigned[j], left, right, compatible, assigned, visited))
                {
                    assigned[j] = leftIdx;
                    return true;
                }
            }
            return false;
        }
    }

    // ========================================================================
    // 数据结构
    // ========================================================================
    internal sealed class WbLoopBoundary
    {
        public List<Curve> Curves;
        public Curve ClosedCurve;
        public double Perimeter;
        public WbLoopBoundary(List<Curve> curves, Curve closedCurve)
        {
            Curves = curves;
            ClosedCurve = closedCurve;
            Perimeter = curves.Sum(c => c.GetLength());
        }
    }

    internal sealed class WbOrientedBox
    {
        public Point3d Center;
        public double[] Sizes; // 3 边，降序
    }

    internal sealed class WbHole
    {
        public double Perimeter, Area;
        public double U, V, W;       // 局部坐标（主轴系）
        public double NU, NV, NW;    // 局部法向
        public double X, Y, Z;       // 世界坐标（仅诊断）
        public string CenterMode;    // 质心模式（仅诊断）
        public bool IsDegenerate;    // 非平面内环（无法获得面积质心和稳定法向）
        public string DegenerateType = string.Empty; // 退化原因
        // 退化孔辅助特征（用于低置信度匹配）
        public double DegenerateProjectedArea;  // 拟合平面投影面积
        public double DegenerateCircularity;    // 圆形度：4π×面积/周长²
        public double DegenerateAspectRatio;    // 二维包围盒纵横比
        public double DegenerateNormalU, DegenerateNormalV, DegenerateNormalW; // 拟合法向（局部坐标）

        public WbHole(double perimeter, double area,
            double u, double v, double w,
            double nu, double nv, double nw,
            double x = 0, double y = 0, double z = 0,
            string centerMode = "",
            bool isDegenerate = false, string degenerateType = "")
        {
            Perimeter = perimeter; Area = area;
            U = u; V = v; W = w;
            NU = nu; NV = nv; NW = nw;
            X = x; Y = y; Z = z;
            CenterMode = centerMode ?? string.Empty;
            IsDegenerate = isDegenerate;
            DegenerateType = degenerateType ?? string.Empty;
        }
    }

    internal sealed class WbOuterProfile
    {
        public double Area, Perimeter;
        public double NU, NV, NW;
        public bool IsAvailable =>
            Area > RhinoMath.ZeroTolerance &&
            Perimeter > RhinoMath.ZeroTolerance;
    }

    // ========================================================================
    // 特征向量
    // ========================================================================
    internal sealed class WbFeature
    {
        internal const string Header = "ENTITY_FEATURE_V9";

        // --- 参与匹配 ---
        public string Material = string.Empty;
        public string Color = string.Empty;
        public double[] BoundingBox = new double[3];  // 降序三边（内部优化：不再存 CSV 字符串）
        public double Area, Volume;
        public double[] Inertia = new double[3];
        public double MirrorScore, HoleMirrorScore;
        public bool IsDegenerate;          // 惯量退化：任意相邻主惯量比 > 0.99
        public bool UsedFallbackTolerance; // 曲线拼接时回退容差被使用（ActiveDoc==null）
        public bool HasDegenerateHoles      // 含非平面内环（孔面积=0、法向为零向量）
        {
            get
            {
                foreach (var h in Holes)
                    if (h.IsDegenerate) return true;
                return false;
            }
        }
        public List<WbHole> Holes = new List<WbHole>();
        public List<WbOuterProfile> OuterProfiles = new List<WbOuterProfile>();

        // --- 仅诊断 ---
        public Point3d Centroid = Point3d.Unset;
        public Point3d LocalOrigin = Point3d.Unset;
        public Vector3d[] Axes = new Vector3d[3];

        // ================================================================
        // 特征提取（一次 Brep 遍历）
        // ================================================================
        public static WbFeature Create(
            string material, string color, Brep brep,
            Dictionary<V5Criterion, int> decimals, double inertiaDegenerateRatio,
            double fallbackTolerance, int index)
        {
            if (brep == null)
                throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) +
                    " 个 Brep 为空。");

            AreaMassProperties area = AreaMassProperties.Compute(brep, true, true, true, true);
            if (area == null)
                throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) +
                    " 个 Brep 无法计算面积属性。");

            VolumeMassProperties volume = brep.IsSolid
                ? VolumeMassProperties.Compute(brep, true, true, true, true)
                : null;

            if (!TryPrincipal(volume, out Point3d center, out double[] inertia, out Vector3d[] axes) &&
                !TryPrincipal(area, out center, out inertia, out axes))
                throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) +
                    " 个 Brep 无法计算主惯量轴。");

            // 面质心：确定性来源，跨文件绝对稳定
            List<Point3d> faceCentroids = CollectFaceCentroids(brep);
            if (faceCentroids.Count > 0)
                OrientAxesByMirrorPoints(faceCentroids, center, axes);
            MakeRightHanded(axes);

            WbOrientedBox box = CalculateOrientedBoundingBox(brep, center, axes);

            var f = new WbFeature
            {
                Material = material ?? string.Empty,
                Color = color ?? string.Empty,
                BoundingBox = box.Sizes,
                Area = WbUtil.Round(area.Area, decimals[V5Criterion.Area]),
                Volume = WbUtil.Round(volume == null ? 0 : volume.Volume, decimals[V5Criterion.Volume]),
                Inertia = inertia.Select(x => WbUtil.Round(x, decimals[V5Criterion.Inertia])).ToArray(),
                Centroid = center,
                LocalOrigin = box.Center,
                Axes = axes.Select(x => x).ToArray()
            };

            // 一次遍历提取孔 + 外轮廓
            ExtractFeatures(brep, center, axes, decimals, index, fallbackTolerance,
                out f.Holes, out f.OuterProfiles, out f.UsedFallbackTolerance);

            f.MirrorScore = ComputeMirrorScore(faceCentroids, center, axes);
            f.HoleMirrorScore = ComputeHoleMirrorScore(f.Holes);

            // 惯量退化检测：相邻主惯量比值 > 99% 则标记，后续匹配将遍历全部轴排列
            double[] rawInertia = inertia;  // 舍入前原始值，避免舍入精度干扰退化判断
            f.IsDegenerate =
                (rawInertia[0] > 1e-10 && rawInertia[1] / rawInertia[0] > inertiaDegenerateRatio) ||
                (rawInertia[1] > 1e-10 && rawInertia[2] / rawInertia[1] > inertiaDegenerateRatio);

            return f;
        }

        // ================================================================
        // 串行化（向后兼容 V8 格式）
        // ================================================================
        public string Serialize()
        {
            string holes = string.Join("/", Holes.Select(h =>
            {
                var baseFields = new[] {
                    WbUtil.FR(h.Perimeter), WbUtil.FR(h.Area),
                    WbUtil.FR(h.U), WbUtil.FR(h.V), WbUtil.FR(h.W),
                    WbUtil.FR(h.NU), WbUtil.FR(h.NV), WbUtil.FR(h.NW)
                };
                if (!h.IsDegenerate)
                    return string.Join(",", baseFields);
                // 退化孔：8 基本字段 + "D" 标记 + 1 退化类型 + 6 退化数值（共 16 逗号分隔值）
                return string.Join(",", baseFields) + ",D," +
                    WbUtil.B64(h.DegenerateType) + "," +
                    WbUtil.FR(h.DegenerateProjectedArea) + "," +
                    WbUtil.FR(h.DegenerateCircularity) + "," +
                    WbUtil.FR(h.DegenerateAspectRatio) + "," +
                    WbUtil.FR(h.DegenerateNormalU) + "," +
                    WbUtil.FR(h.DegenerateNormalV) + "," +
                    WbUtil.FR(h.DegenerateNormalW);
            }));

            string profiles = string.Join("|", OuterProfiles.Select(p =>
                WbUtil.FR(p.Area) + "," + WbUtil.FR(p.Perimeter) + "," +
                WbUtil.FR(p.NU) + "," + WbUtil.FR(p.NV) + "," + WbUtil.FR(p.NW)));

            string bbox = string.Join(",", BoundingBox.Select(WbUtil.FR));

            return string.Join(";", new[] {
                Header,
                WbUtil.B64(Material), WbUtil.B64(Color),
                WbUtil.B64(bbox),
                WbUtil.FR(Area), WbUtil.FR(Volume),
                WbUtil.FR(Inertia[0]), WbUtil.FR(Inertia[1]), WbUtil.FR(Inertia[2]),
                holes,
                WbUtil.FR(MirrorScore), WbUtil.FR(HoleMirrorScore),
                profiles,
                IsDegenerate ? "1" : "0"
            });
        }

        public string ToSummary()
        {
            string bbox = string.Join(",", BoundingBox.Select(WbUtil.FD));
            return string.Format(CultureInfo.InvariantCulture,
                "材质={0}; 颜色={1}; 包围盒=({2}); 惯量=({3},{4},{5}); 表面积={6}; 体积={7}; 孔数={8}; 孔周长和={9}; 孔面积和={10}",
                Material, Color, bbox,
                WbUtil.FD(Inertia[0]), WbUtil.FD(Inertia[1]), WbUtil.FD(Inertia[2]),
                WbUtil.FD(Area), WbUtil.FD(Volume),
                Holes.Count,
                WbUtil.FD(Holes.Sum(h => h.Perimeter)),
                WbUtil.FD(Holes.Sum(h => h.Area)));
        }

        /// <summary>从 V9 格式反序列化特征（用于检查点恢复，兼容不含退化孔字段的旧版本 V9 数据）</summary>
        public static WbFeature Deserialize(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                throw new ArgumentException("反序列化输入为空。");
            string[] parts = line.Split(';');
            if (parts.Length < 13 || !string.Equals(parts[0], Header, StringComparison.Ordinal))
                throw new ArgumentException("V9 特征格式不正确。");

            var f = new WbFeature
            {
                Material = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])),
                Color = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]))
            };

            // 包围盒
            string bboxStr = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
            string[] bboxParts = bboxStr.Split(',');
            f.BoundingBox = new double[3];
            for (int i = 0; i < 3; i++)
                f.BoundingBox[i] = double.Parse(bboxParts[i], CultureInfo.InvariantCulture);

            f.Area = double.Parse(parts[4], CultureInfo.InvariantCulture);
            f.Volume = double.Parse(parts[5], CultureInfo.InvariantCulture);
            f.Inertia = new double[]
            {
                double.Parse(parts[6], CultureInfo.InvariantCulture),
                double.Parse(parts[7], CultureInfo.InvariantCulture),
                double.Parse(parts[8], CultureInfo.InvariantCulture)
            };

            // 惯量退化标记：新格式字段13 直接恢复，旧格式 parts.Length==13 则拒绝（阈值未知）
            if (parts.Length > 13)
            {
                f.IsDegenerate = parts[13] == "1";
            }
            else
            {
                throw new InvalidDataException(
                    "检查点使用旧版 V9 格式（缺少 IsDegenerate 字段），惯量退化阈值可能" +
                    "与保存时不一致，静默恢复会改变分类结果。建议重新提取特征后重跑迁移。");
            }

            // 孔
            string holesPart = parts[9];
            if (!string.IsNullOrEmpty(holesPart))
            {
                string[] holeLines = holesPart.Split('/');
                foreach (string hl in holeLines)
                {
                    string[] hv = hl.Split(',');
                    if (hv.Length < 8) continue;
                    double perim = double.Parse(hv[0], CultureInfo.InvariantCulture);
                    double area = double.Parse(hv[1], CultureInfo.InvariantCulture);
                    double u = double.Parse(hv[2], CultureInfo.InvariantCulture);
                    double v = double.Parse(hv[3], CultureInfo.InvariantCulture);
                    double w = double.Parse(hv[4], CultureInfo.InvariantCulture);
                    double nu = double.Parse(hv[5], CultureInfo.InvariantCulture);
                    double nv = double.Parse(hv[6], CultureInfo.InvariantCulture);
                    double nw = double.Parse(hv[7], CultureInfo.InvariantCulture);

                    // 检测退化孔标记：hv[8] == "D" 且至少 16 个字段（8 基本 + 标记 + 7 退化值）
                    bool isDegenerate = hv.Length >= 16 && hv[8] == "D";
                    string degenerateType = string.Empty;
                    double projArea = 0, circularity = 0, aspectRatio = 0;
                    double dnu = 0, dnv = 0, dnw = 0;
                    if (isDegenerate)
                    {
                        degenerateType = Encoding.UTF8.GetString(Convert.FromBase64String(hv[9]));
                        projArea = double.Parse(hv[10], CultureInfo.InvariantCulture);
                        circularity = double.Parse(hv[11], CultureInfo.InvariantCulture);
                        aspectRatio = double.Parse(hv[12], CultureInfo.InvariantCulture);
                        dnu = double.Parse(hv[13], CultureInfo.InvariantCulture);
                        dnv = double.Parse(hv[14], CultureInfo.InvariantCulture);
                        dnw = double.Parse(hv[15], CultureInfo.InvariantCulture);
                    }

                    var hole = new WbHole(perim, area, u, v, w, nu, nv, nw,
                        isDegenerate: isDegenerate, degenerateType: degenerateType);
                    if (isDegenerate)
                    {
                        hole.DegenerateProjectedArea = projArea;
                        hole.DegenerateCircularity = circularity;
                        hole.DegenerateAspectRatio = aspectRatio;
                        hole.DegenerateNormalU = dnu;
                        hole.DegenerateNormalV = dnv;
                        hole.DegenerateNormalW = dnw;
                    }
                    f.Holes.Add(hole);
                }
            }

            f.MirrorScore = double.Parse(parts[10], CultureInfo.InvariantCulture);
            f.HoleMirrorScore = double.Parse(parts[11], CultureInfo.InvariantCulture);

            // 外轮廓
            string profilesPart = parts.Length > 12 ? parts[12] : string.Empty;
            if (!string.IsNullOrEmpty(profilesPart))
            {
                string[] profileLines = profilesPart.Split('|');
                foreach (string pl in profileLines)
                {
                    string[] pv = pl.Split(',');
                    if (pv.Length >= 5)
                        f.OuterProfiles.Add(new WbOuterProfile
                        {
                            Area = double.Parse(pv[0], CultureInfo.InvariantCulture),
                            Perimeter = double.Parse(pv[1], CultureInfo.InvariantCulture),
                            NU = double.Parse(pv[2], CultureInfo.InvariantCulture),
                            NV = double.Parse(pv[3], CultureInfo.InvariantCulture),
                            NW = double.Parse(pv[4], CultureInfo.InvariantCulture)
                        });
                }
            }

            return f;
        }

        public IEnumerable<string> DiagnosticLines(string number, string sourceFile)
        {
            yield return "===== 实体诊断：" + number + " =====";
            yield return "来源文件=" + sourceFile;
            yield return "质量质心(世界坐标)=" + WbUtil.PointText(Centroid) +
                "；局部原点(定向包围盒中心)=" + WbUtil.PointText(LocalOrigin);
            yield return "主轴U=" + WbUtil.VectorText(Axes[0]) +
                "；主轴V=" + WbUtil.VectorText(Axes[1]) +
                "；主轴W=" + WbUtil.VectorText(Axes[2]);
            yield return "惯量=" + string.Join(",", Inertia.Select(WbUtil.FR)) +
                "；表面积=" + WbUtil.FR(Area) +
                "；体积=" + WbUtil.FR(Volume) +
                "；包围盒=" + string.Join(",", BoundingBox.Select(WbUtil.FR));
            yield return "孔数=" + Holes.Count.ToString(CultureInfo.InvariantCulture) +
                "；外轮廓数=" + OuterProfiles.Count.ToString(CultureInfo.InvariantCulture) +
                "；镜像值=" + WbUtil.FR(MirrorScore) +
                "；孔镜像值=" + WbUtil.FR(HoleMirrorScore);
            if (UsedFallbackTolerance)
                yield return "警告：未检测到 Rhino 文档，曲线拼接使用了回退容差。";
            for (int i = 0; i < Holes.Count; i++)
            {
                WbHole h = Holes[i];
                yield return "孔[" + i.ToString(CultureInfo.InvariantCulture) +
                    "] 模式=" + h.CenterMode +
                    (h.IsDegenerate ? " [退化:" + h.DegenerateType + "]" : "") +
                    "；世界坐标=" + WbUtil.PointText(new Point3d(h.X, h.Y, h.Z)) +
                    "；局部=(" + WbUtil.FR(h.U) + "," + WbUtil.FR(h.V) + "," + WbUtil.FR(h.W) + ")" +
                    "；周长=" + WbUtil.FR(h.Perimeter) +
                    "；面积=" + WbUtil.FR(h.Area) +
                    "；法向=(" + WbUtil.FR(h.NU) + "," + WbUtil.FR(h.NV) + "," + WbUtil.FR(h.NW) + ")";
            }
        }

        // ================================================================
        // 主轴计算
        // ================================================================
        private static bool TryPrincipal(
            VolumeMassProperties p,
            out Point3d center, out double[] inertia, out Vector3d[] axes)
        {
            center = Point3d.Unset; inertia = null; axes = null;
            if (p == null) return false;
            center = p.Centroid;
            return p.CentroidCoordinatesPrincipalMomentsOfInertia(
                out double a, out Vector3d av,
                out double b, out Vector3d bv,
                out double c, out Vector3d cv) &&
                Order(new[] { a, b, c }, new[] { av, bv, cv }, out inertia, out axes);
        }

        private static bool TryPrincipal(
            AreaMassProperties p,
            out Point3d center, out double[] inertia, out Vector3d[] axes)
        {
            center = Point3d.Unset; inertia = null; axes = null;
            if (p == null) return false;
            center = p.Centroid;
            return p.CentroidCoordinatesPrincipalMomentsOfInertia(
                out double a, out Vector3d av,
                out double b, out Vector3d bv,
                out double c, out Vector3d cv) &&
                Order(new[] { a, b, c }, new[] { av, bv, cv }, out inertia, out axes);
        }

        private static bool Order(
            double[] values, Vector3d[] raw,
            out double[] inertia, out Vector3d[] axes)
        {
            int[] order = Enumerable.Range(0, 3)
                .OrderByDescending(i => values[i]).ToArray();
            inertia = order.Select(i => values[i]).ToArray();
            axes = order.Select(i =>
            {
                Vector3d x = raw[i];
                return x.Unitize() ? x : Vector3d.Unset;
            }).ToArray();
            return axes.All(x => x.IsValid);
        }

        private static void MakeRightHanded(Vector3d[] axes)
        {
            if (Vector3d.CrossProduct(axes[0], axes[1]) * axes[2] < 0)
                axes[2] = -axes[2];
        }

        // ================================================================
        // 面质心采样：对每个平面取面积质心，跨文件绝对稳定
        // ================================================================
        private static List<Point3d> CollectFaceCentroids(Brep brep)
        {
            var points = new List<Point3d>();
            foreach (BrepFace face in brep.Faces)
            {
                AreaMassProperties props = AreaMassProperties.Compute(face);
                if (props != null && props.Area > RhinoMath.ZeroTolerance)
                    points.Add(props.Centroid);
            }
            // 加上所有顶点作为回退（如果面质心太少）
            if (points.Count < 6)
            {
                foreach (BrepVertex v in brep.Vertices)
                    points.Add(v.Location);
            }
            return points;
        }

        private static void OrientAxesByMirrorPoints(
            List<Point3d> points, Point3d center, Vector3d[] axes)
        {
            for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
            {
                double thirdMoment = 0;
                foreach (Point3d p in points)
                {
                    double coord = (p - center) * axes[axisIndex];
                    thirdMoment += coord * coord * coord;
                }
                if (thirdMoment < 0)
                    axes[axisIndex] = -axes[axisIndex];
            }
        }

        private static double ComputeMirrorScore(
            List<Point3d> points, Point3d center, Vector3d[] axes)
        {
            double score = 0, scale = 0;
            foreach (Point3d p in points)
            {
                Vector3d v = p - center;
                double product = (v * axes[0]) * (v * axes[1]) * (v * axes[2]);
                score += product;
                scale += Math.Abs(product);
            }
            return scale <= RhinoMath.ZeroTolerance ? 0 : score / scale;
        }

        private static double ComputeHoleMirrorScore(List<WbHole> holes)
        {
            double signedV = 0, signedW = 0, signedUv = 0, signedUw = 0,
                   scale = 0, maxU = 0;
            foreach (WbHole h in holes)
            {
                double p = h.Perimeter;
                signedV += p * h.V;
                signedW += p * h.W;
                signedUv += p * h.U * h.V;
                signedUw += p * h.U * h.W;
                scale += p * Math.Sqrt(h.V * h.V + h.W * h.W);
                maxU = Math.Max(maxU, Math.Abs(h.U));
            }
            if (scale <= RhinoMath.ZeroTolerance) return 0;
            double[] candidates =
            {
                signedV / scale,
                signedW / scale,
                maxU <= RhinoMath.ZeroTolerance ? 0 : signedUv / (scale * maxU),
                maxU <= RhinoMath.ZeroTolerance ? 0 : signedUw / (scale * maxU)
            };
            return candidates.OrderByDescending(Math.Abs).First();
        }

        // ================================================================
        // 定向包围盒
        // ================================================================
        private static WbOrientedBox CalculateOrientedBoundingBox(
            Brep brep, Point3d center, Vector3d[] axes)
        {
            Plane plane = new Plane(center, axes[0], axes[1]);
            BoundingBox box = brep.GetBoundingBox(plane);
            if (!box.IsValid)
                throw new ArgumentException("实体无法计算主轴定向包围盒。");

            double[] sizes =
            {
                Math.Max(0, box.Max.X - box.Min.X),
                Math.Max(0, box.Max.Y - box.Min.Y),
                Math.Max(0, box.Max.Z - box.Min.Z)
            };
            // 降序排序
            Array.Sort(sizes);
            Array.Reverse(sizes);

            Point3d localCenter = new Point3d(
                (box.Min.X + box.Max.X) * 0.5,
                (box.Min.Y + box.Max.Y) * 0.5,
                (box.Min.Z + box.Max.Z) * 0.5);

            return new WbOrientedBox
            {
                Center = plane.PointAt(localCenter.X, localCenter.Y, localCenter.Z),
                Sizes = sizes
            };
        }

        // ================================================================
        // 一次遍历提取孔 + 外轮廓
        //
        // 孔特征语义约定（最终决策）：
        // hole_count 定义为 Brep Inner loop（内环）的计数，不是物理孔数。
        // ───────────────────────────────────────────────────────────────
        //  贯穿孔 → 实体两面各一个内环，hole_count = 2
        //  盲  孔 → 一个内环，hole_count = 1
        // ───────────────────────────────────────────────────────────────
        // 此约定是故意的：BreP 拓扑无法可靠区分贯穿孔底面的内环和底面附近
        // 的独立盲孔内环（同轴同径是必要不充分条件）。不对内环做合并，
        // 保证 hole_count 始终是纯拓扑计数值，不依赖几何启发式。
        // ================================================================
        private static void ExtractFeatures(
            Brep brep, Point3d center, Vector3d[] axes,
            Dictionary<V5Criterion, int> decimals, int index,
            double fallbackTolerance,
            out List<WbHole> holes, out List<WbOuterProfile> profiles,
            out bool usedFallbackTolerance)
        {
            usedFallbackTolerance = false;
            holes = new List<WbHole>();
            profiles = new List<WbOuterProfile>();

            foreach (BrepFace face in brep.Faces)
            foreach (BrepLoop loop in face.Loops)
            {
                WbLoopBoundary boundary = ReadLoopBoundary(loop, fallbackTolerance,
                    out bool loopUsedFallback);
                if (loopUsedFallback) usedFallbackTolerance = true;
                if (boundary.Perimeter <= RhinoMath.ZeroTolerance)
                    continue;

                if (loop.LoopType == BrepLoopType.Inner)
                {
                    // 孔
                    Point3d holeCenter;
                    double holeArea = 0;
                    Vector3d normal = Vector3d.Zero;
                    string centerMode;

                    AreaMassProperties props = boundary.ClosedCurve == null
                        ? null
                        : AreaMassProperties.Compute(boundary.ClosedCurve);
                    Plane plane;
                    if (props != null && props.Area > RhinoMath.ZeroTolerance &&
                        boundary.ClosedCurve.TryGetPlane(out plane))
                    {
                        holeCenter = props.Centroid;
                        holeArea = props.Area;
                        normal = plane.Normal;
                        normal.Unitize();
                        centerMode = "平面面积质心";
                    }
                    else
                    {
                        holeCenter = DeterministicLengthCentroid(
                            boundary.Curves, boundary.Perimeter);
                        centerMode = "确定性长度质心";
                    }

                    bool isDeg = holeArea <= 0 || normal.IsZero;
                    string degType = isDeg ? "非平面内环-" + centerMode : string.Empty;

                    Vector3d position = holeCenter - center;
                    holes.Add(new WbHole(
                        WbUtil.Round(boundary.Perimeter, decimals[V5Criterion.EdgeSum]),
                        WbUtil.Round(holeArea, decimals[V5Criterion.HoleArea]),
                        WbUtil.Round(position * axes[0], decimals[V5Criterion.HolePosition]),
                        WbUtil.Round(position * axes[1], decimals[V5Criterion.HolePosition]),
                        WbUtil.Round(position * axes[2], decimals[V5Criterion.HolePosition]),
                        WbUtil.Round(normal * axes[0], 6),
                        WbUtil.Round(normal * axes[1], 6),
                        WbUtil.Round(normal * axes[2], 6),
                        holeCenter.X, holeCenter.Y, holeCenter.Z,
                        centerMode, isDeg, degType));

                    // 退化孔：计算拟合平面特征用于低置信度匹配
                    if (isDeg)
                    {
                        WbHole lastHole = holes[holes.Count - 1];
                        ComputeDegenerateFeatures(boundary.Curves, boundary.Perimeter,
                            out lastHole.DegenerateProjectedArea,
                            out lastHole.DegenerateCircularity,
                            out lastHole.DegenerateAspectRatio,
                            out lastHole.DegenerateNormalU,
                            out lastHole.DegenerateNormalV,
                            out lastHole.DegenerateNormalW,
                            axes);
                    }
                }
                else if (loop.LoopType == BrepLoopType.Outer)
                {
                    // 外轮廓 — 仅存描述符（面积、周长、法向），不再存顶点
                    AreaMassProperties props = boundary.ClosedCurve == null
                        ? null
                        : AreaMassProperties.Compute(boundary.ClosedCurve);
                    Plane ignored;
                    if (props == null || props.Area <= RhinoMath.ZeroTolerance ||
                        !boundary.ClosedCurve.TryGetPlane(out ignored))
                        continue;

                    Vector3d normal = ignored.Normal;
                    normal.Unitize();
                    profiles.Add(new WbOuterProfile
                    {
                        Area = props.Area,
                        Perimeter = boundary.Perimeter,
                        NU = normal * axes[0],
                        NV = normal * axes[1],
                        NW = normal * axes[2]
                    });
                }
            }

            // 确定性排序
            holes = holes.OrderBy(h => h.Area)
                .ThenBy(h => h.Perimeter)
                .ThenBy(h => h.U)
                .ThenBy(h => h.V)
                .ThenBy(h => h.W).ToList();

            profiles = profiles.OrderBy(p => p.Area)
                .ThenBy(p => p.Perimeter).ToList();
        }

        private static WbLoopBoundary ReadLoopBoundary(BrepLoop loop, double fallbackTolerance,
            out bool usedFallbackTolerance)
        {
            usedFallbackTolerance = false;
            var edges = new List<Curve>();
            foreach (BrepTrim trim in loop.Trims)
            {
                if (trim.Edge == null) continue;
                Curve edge = trim.Edge.DuplicateCurve();
                if (trim.IsReversed()) edge.Reverse();
                edges.Add(edge);
            }
            if (edges.Count == 0)
                throw new ArgumentException("孔轮廓不包含有效边。");

            double tolerance;
            if (RhinoDoc.ActiveDoc == null)
            {
                tolerance = fallbackTolerance;
                usedFallbackTolerance = true;
            }
            else
            {
                tolerance = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            }
            Curve[] joined = Curve.JoinCurves(edges, tolerance);
            Curve closed = joined != null && joined.Length == 1 && joined[0].IsClosed
                ? joined[0] : null;
            return new WbLoopBoundary(edges, closed);
        }

        /// <summary>
        /// 确定性长度质心：128 个等弧长采样点平均。
        /// 固定采样数和固定起始偏移，保证跨文件稳定。
        /// </summary>
        private static Point3d DeterministicLengthCentroid(
            List<Curve> curves, double totalLength)
        {
            const int sampleCount = 128;
            double[] lengths = curves.Select(c => c.GetLength()).ToArray();
            double sumX = 0, sumY = 0, sumZ = 0;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                double target = totalLength * (sample + 0.5) / sampleCount;
                double accumulated = 0;
                int curveIndex = lengths.Length - 1;
                for (int i = 0; i < lengths.Length; i++)
                {
                    if (target <= accumulated + lengths[i])
                    { curveIndex = i; break; }
                    accumulated += lengths[i];
                }
                double local = lengths[curveIndex] <= RhinoMath.ZeroTolerance
                    ? 0.5
                    : (target - accumulated) / lengths[curveIndex];
                local = Math.Max(0, Math.Min(1, local));
                Point3d p = curves[curveIndex].PointAtNormalizedLength(local);
                sumX += p.X; sumY += p.Y; sumZ += p.Z;
            }
            return new Point3d(sumX / sampleCount, sumY / sampleCount, sumZ / sampleCount);
        }

        /// <summary>为非平面内环拟合平面并提取稳定特征（用于低置信度匹配）</summary>
        private static void ComputeDegenerateFeatures(
            List<Curve> curves, double totalLength,
            out double projectedArea, out double circularity, out double aspectRatio,
            out double normalU, out double normalV, out double normalW,
            Vector3d[] axes)
        {
            projectedArea = 0; circularity = 0; aspectRatio = 0;
            normalU = 0; normalV = 0; normalW = 0;
            if (curves == null || curves.Count == 0 || totalLength <= RhinoMath.ZeroTolerance)
                return;

            // 采样 64 个点
            const int samples = 64;
            var points = new List<Point3d>();
            double[] clen = curves.Select(c => c.GetLength()).ToArray();
            for (int s = 0; s < samples; s++)
            {
                double target = totalLength * (s + 0.5) / samples;
                double acc = 0;
                int ci = clen.Length - 1;
                for (int i = 0; i < clen.Length; i++)
                { if (target <= acc + clen[i]) { ci = i; break; } acc += clen[i]; }
                double local = clen[ci] <= RhinoMath.ZeroTolerance ? 0.5
                    : (target - acc) / clen[ci];
                local = Math.Max(0, Math.Min(1, local));
                points.Add(curves[ci].PointAtNormalizedLength(local));
            }

            // 拟合平面：协方差矩阵最小特征值对应法向
            double cx = 0, cy = 0, cz = 0;
            foreach (var p in points) { cx += p.X; cy += p.Y; cz += p.Z; }
            cx /= points.Count; cy /= points.Count; cz /= points.Count;

            double cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
            foreach (var p in points)
            {
                double dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz;
                cxx += dx * dx; cxy += dx * dy; cxz += dx * dz;
                cyy += dy * dy; cyz += dy * dz; czz += dz * dz;
            }
            double trace = cxx + cyy + czz;
            if (trace <= 1e-20) return;

            // 幂迭代 (trace*I - C) → 最小特征值对应法向
            double vx = 0, vy = 0, vz = 1;
            for (int iter = 0; iter < 20; iter++)
            {
                double nx = trace * vx - (cxx * vx + cxy * vy + cxz * vz);
                double ny = trace * vy - (cxy * vx + cyy * vy + cyz * vz);
                double nz = trace * vz - (cxz * vx + cyz * vy + czz * vz);
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-15) break;
                vx = nx / len; vy = ny / len; vz = nz / len;
            }
            Vector3d normal = new Vector3d(vx, vy, vz);

            // 建立平面二维基
            Vector3d uBasis = Math.Abs(vz) < 0.9
                ? Vector3d.CrossProduct(normal, Vector3d.ZAxis)
                : Vector3d.CrossProduct(normal, Vector3d.XAxis);
            if (!uBasis.Unitize()) return;
            Vector3d vBasis = Vector3d.CrossProduct(normal, uBasis);
            if (!vBasis.Unitize()) return;

            // 投影点 → 2D
            var pts2d = new List<Point2d>();
            foreach (var p in points)
            {
                Vector3d d = p - new Point3d(cx, cy, cz);
                pts2d.Add(new Point2d(d * uBasis, d * vBasis));
            }

            // 投影多边形面积（鞋带公式）
            double area2d = 0;
            for (int i = 0; i < pts2d.Count; i++)
            {
                int j = (i + 1) % pts2d.Count;
                area2d += pts2d[i].X * pts2d[j].Y - pts2d[j].X * pts2d[i].Y;
            }
            projectedArea = Math.Abs(area2d) * 0.5;

            // 圆形度 & 纵横比
            if (totalLength > RhinoMath.ZeroTolerance)
                circularity = 4.0 * Math.PI * projectedArea / (totalLength * totalLength);

            double minX = pts2d.Min(p2 => p2.X), maxX = pts2d.Max(p2 => p2.X);
            double minY = pts2d.Min(p2 => p2.Y), maxY = pts2d.Max(p2 => p2.Y);
            double w = maxX - minX, h = maxY - minY;
            aspectRatio = Math.Min(w, h) > 1e-10
                ? Math.Max(w, h) / Math.Min(w, h) : 1.0;

            // 法向转换到局部坐标
            normalU = normal * axes[0];
            normalV = normal * axes[1];
            normalW = normal * axes[2];
        }

    }

    // ========================================================================
    // 迁移记录
    // ========================================================================
    internal sealed class WbMigrationRecord
    {
        public int Index;
        public string Number;
        public WbFeature Feature;
        public string SourceFile;

        public WbMigrationRecord(int index, string number, WbFeature feature, string sourceFile)
        {
            Index = index;
            Number = number;
            Feature = feature;
            SourceFile = sourceFile ?? string.Empty;
        }
    }

    // ========================================================================
    // 校核与聚类
    // ========================================================================
    internal sealed class WbMigrationAudit
    {
        // 6 种轴排列：惯量退化零件的主轴顺序可能在不同文件间交换
        // 每个排列是 { U 来自原轴[perm[0]], V 来自原轴[perm[1]], W 来自原轴[perm[2]] }
        private static readonly int[][] AxisPermutations =
        {
            new[] { 0, 1, 2 },  // U,V,W → 不变
            new[] { 0, 2, 1 },  // U,V,W → U,W,V
            new[] { 1, 0, 2 },  // U,V,W → V,U,W
            new[] { 1, 2, 0 },  // U,V,W → V,W,U
            new[] { 2, 0, 1 },  // U,V,W → W,U,V
            new[] { 2, 1, 0 }   // U,V,W → W,V,U
        };

        // 主轴反射符号的 4 种组合：保证主轴方向歧义全覆盖
        private static readonly double[][] NonReflectionSigns =
        {
            new[] {  1.0,  1.0,  1.0 },
            new[] { -1.0, -1.0,  1.0 },
            new[] { -1.0,  1.0, -1.0 },
            new[] {  1.0, -1.0, -1.0 }
        };

        private static readonly double[][] ReflectionSigns =
        {
            new[] { -1.0,  1.0,  1.0 },
            new[] {  1.0, -1.0,  1.0 },
            new[] {  1.0,  1.0, -1.0 },
            new[] { -1.0, -1.0, -1.0 }
        };

        // 恒等符号：退化路径已将排列+符号烤进重映射坐标，用此数组避免二次乘符号
        private static readonly double[] IdentitySign = { 1.0, 1.0, 1.0 };

        public List<List<WbMigrationRecord>> Classes = new List<List<WbMigrationRecord>>();
        public List<List<WbMigrationRecord>> AmbiguityGroups = new List<List<WbMigrationRecord>>();
        public List<WbMigrationRecord> DegenerateHoleRecords = new List<WbMigrationRecord>();
        public List<string> SamePartDifferentNumbers = new List<string>();
        public List<string> DifferentPartsSameNumber = new List<string>();
        public List<string> MirrorParts = new List<string>();
        public List<string> ExcludedNumberConflicts = new List<string>();

        private Dictionary<V5Criterion, bool> _enabled;
        private Dictionary<V5Criterion, double> _tolerances;
        private Dictionary<V5Criterion, bool> _isRel;
        private double _mirrorTolerance;
        private bool _strictMaterialColor;
        private double _holeNormalTolerance;
        private double _outerProfileAreaRatio;
        private double _outerProfilePerimeterMin;
        private double _degeneratePositionMultiplier;
        private bool _enableDegenerateMatching;

        public static WbMigrationAudit Create(
            List<WbMigrationRecord> records,
            Dictionary<V5Criterion, bool> enabled,
            Dictionary<V5Criterion, double> tolerances,
            Dictionary<V5Criterion, bool> isRel,
            double mirrorTolerance,
            bool strictMaterialColor,
            double holeNormalTolerance,
            double outerProfileAreaRatio = 0.0001,
            double outerProfilePerimeterMin = 0.01,
            double degeneratePositionMultiplier = 2.0,
            bool enableDegenerateMatching = false)
        {
            var audit = new WbMigrationAudit
            {
                _enabled = enabled,
                _tolerances = tolerances,
                _isRel = isRel,
                _mirrorTolerance = mirrorTolerance,
                _strictMaterialColor = strictMaterialColor,
                _holeNormalTolerance = holeNormalTolerance,
                _outerProfileAreaRatio = outerProfileAreaRatio,
                _outerProfilePerimeterMin = outerProfilePerimeterMin,
                _degeneratePositionMultiplier = degeneratePositionMultiplier,
                _enableDegenerateMatching = enableDegenerateMatching
            };

            // 按来源文件+编号+索引排序，确保可复现性
            records = records.OrderBy(r => r.SourceFile ?? string.Empty)
                             .ThenBy(r => r.Number ?? string.Empty)
                             .ThenBy(r => r.Index).ToList();

            // 分离含退化孔记录——各自独立归类，不参与自动聚类
            var normalRecords = new List<WbMigrationRecord>();
            foreach (var r in records)
            {
                if (r.Feature.HasDegenerateHoles)
                    audit.DegenerateHoleRecords.Add(r);
                else
                    normalRecords.Add(r);
            }

            // 构建候选桶：严格模式下按材质+颜色+孔数分组，宽松模式下仅按孔数分组
            // 桶间无需匹配（材质/颜色不同或孔数不同 → GlobalMatches/GeometryMatches 必返回 false）
            int n = normalRecords.Count;
            bool[,] matchMatrix = new bool[n, n];
            for (int i = 0; i < n; i++) matchMatrix[i, i] = true;

            var buckets = new Dictionary<string, List<int>>();
            for (int i = 0; i < n; i++)
            {
                WbFeature f = normalRecords[i].Feature;
                // 桶键：孔数（必需相同）+ 材质/颜色（严格模式下才作为桶键）
                string bucketKey = f.Holes.Count.ToString(CultureInfo.InvariantCulture);
                if (strictMaterialColor)
                    bucketKey += "|" + (f.Material ?? string.Empty) + "|" + (f.Color ?? string.Empty);

                if (!buckets.TryGetValue(bucketKey, out var list))
                {
                    list = new List<int>();
                    buckets[bucketKey] = list;
                }
                list.Add(i);
            }

            // 仅在桶内构建匹配矩阵
            foreach (var kvp in buckets)
            {
                var indices = kvp.Value;
                for (int a = 0; a < indices.Count; a++)
                for (int b = a + 1; b < indices.Count; b++)
                {
                    int i = indices[a], j = indices[b];
                    bool m = Matches(normalRecords[i].Feature, normalRecords[j].Feature,
                        enabled, tolerances, isRel, mirrorTolerance, strictMaterialColor, holeNormalTolerance,
                        outerProfileAreaRatio, outerProfilePerimeterMin);
                    matchMatrix[i, j] = matchMatrix[j, i] = m;
                }
            }

            // BFS 提取连通分量
            bool[] visited = new bool[n];
            var components = new List<List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (visited[i]) continue;
                var comp = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;
                while (queue.Count > 0)
                {
                    int v = queue.Dequeue();
                    comp.Add(v);
                    for (int w = 0; w < n; w++)
                    {
                        if (!visited[w] && matchMatrix[v, w])
                        {
                            visited[w] = true;
                            queue.Enqueue(w);
                        }
                    }
                }
                components.Add(comp);
            }

            // 检测每个连通分量是否为团（全连接），非团标记为歧义
            foreach (var comp in components)
            {
                bool isClique = true;
                for (int i = 0; i < comp.Count && isClique; i++)
                for (int j = i + 1; j < comp.Count && isClique; j++)
                    if (!matchMatrix[comp[i], comp[j]])
                        isClique = false;

                var group = comp.Select(i => normalRecords[i]).ToList();
                if (isClique)
                    audit.Classes.Add(group);
                else
                    audit.AmbiguityGroups.Add(group);
            }

            // 退化孔记录：关闭时各自独立成类（隔离），开启时内部低置信度匹配
            if (audit._enableDegenerateMatching && audit.DegenerateHoleRecords.Count > 1)
            {
                int dn = audit.DegenerateHoleRecords.Count;
                bool[,] degenMatrix = new bool[dn, dn];
                for (int i = 0; i < dn; i++) degenMatrix[i, i] = true;

                var degenBuckets = new Dictionary<string, List<int>>();
                for (int i = 0; i < dn; i++)
                {
                    string key = audit.DegenerateHoleRecords[i].Feature.Holes.Count
                        .ToString(CultureInfo.InvariantCulture);
                    if (!degenBuckets.TryGetValue(key, out var list))
                    {
                        list = new List<int>();
                        degenBuckets[key] = list;
                    }
                    list.Add(i);
                }

                foreach (var kvp in degenBuckets)
                {
                    var indices = kvp.Value;
                    for (int a = 0; a < indices.Count; a++)
                    for (int b = a + 1; b < indices.Count; b++)
                    {
                        int i = indices[a], j = indices[b];
                        bool m = Matches(
                            audit.DegenerateHoleRecords[i].Feature,
                            audit.DegenerateHoleRecords[j].Feature,
                            enabled, tolerances, isRel, mirrorTolerance,
                            strictMaterialColor, holeNormalTolerance,
                            outerProfileAreaRatio, outerProfilePerimeterMin,
                            degeneratePositionMultiplier, true);
                        degenMatrix[i, j] = degenMatrix[j, i] = m;
                    }
                }

                bool[] degenVisited = new bool[dn];
                for (int i = 0; i < dn; i++)
                {
                    if (degenVisited[i]) continue;
                    var compIndices = new List<int>();
                    var queue = new Queue<int>();
                    queue.Enqueue(i);
                    degenVisited[i] = true;
                    while (queue.Count > 0)
                    {
                        int v = queue.Dequeue();
                        compIndices.Add(v);
                        for (int w = 0; w < dn; w++)
                        {
                            if (!degenVisited[w] && degenMatrix[v, w])
                            {
                                degenVisited[w] = true;
                                queue.Enqueue(w);
                            }
                        }
                    }

                    // 检测连通分量是否为团（全连接），非团标记为歧义
                    bool isClique = true;
                    for (int a = 0; a < compIndices.Count && isClique; a++)
                    for (int b = a + 1; b < compIndices.Count && isClique; b++)
                        if (!degenMatrix[compIndices[a], compIndices[b]])
                            isClique = false;

                    var group = compIndices.Select(idx => audit.DegenerateHoleRecords[idx]).ToList();
                    if (isClique)
                        audit.Classes.Add(group);
                    else
                        audit.AmbiguityGroups.Add(group);
                }
            }
            else
            {
                // 关闭：退化孔记录各自作为单元素类加入 Classes，确保入库且参与编号冲突隔离
                foreach (var dr in audit.DegenerateHoleRecords)
                    audit.Classes.Add(new List<WbMigrationRecord> { dr });
            }

            // 检测同件异号
            foreach (var group in audit.Classes)
            {
                var numbers = group.Select(r => r.Number)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(num => num).ToList();
                if (numbers.Count > 1)
                    audit.SamePartDifferentNumbers.Add(
                        "实际相同、编号不同：" + string.Join("、", numbers) +
                        "；输入序号：" + string.Join("、",
                            group.Select(r => (r.Index + 1).ToString(CultureInfo.InvariantCulture))));
            }

            // 检测异件同号
            foreach (var group in records.GroupBy(
                r => r.Number, StringComparer.OrdinalIgnoreCase))
            {
                int classCount = audit.Classes.Count(
                    c => c.Any(r => string.Equals(r.Number, group.Key,
                        StringComparison.OrdinalIgnoreCase)));
                if (classCount > 1)
                    audit.DifferentPartsSameNumber.Add(
                        "实际不同、编号相同：" + group.Key +
                        "；分为 " + classCount.ToString(CultureInfo.InvariantCulture) + " 类" +
                        "；输入序号：" + string.Join("、",
                            group.Select(r => (r.Index + 1).ToString(CultureInfo.InvariantCulture))));
            }

            // 检测镜像零件
            for (int left = 0; left < audit.Classes.Count; left++)
            for (int right = left + 1; right < audit.Classes.Count; right++)
            {
                WbMigrationRecord a = audit.Classes[left].OrderBy(r => r.Index).First();
                WbMigrationRecord b = audit.Classes[right].OrderBy(r => r.Index).First();
                if (MirrorMatches(a.Feature, b.Feature, enabled, tolerances, isRel, mirrorTolerance, strictMaterialColor, holeNormalTolerance,
                    outerProfileAreaRatio, outerProfilePerimeterMin))
                    audit.MirrorParts.Add(
                        "镜像零件：" + a.Number +
                        "（序号 " + (a.Index + 1).ToString(CultureInfo.InvariantCulture) + "）与 " +
                        b.Number +
                        "（序号 " + (b.Index + 1).ToString(CultureInfo.InvariantCulture) + "）。");
            }

            // 检测歧义聚类：容差不传递导致连通分量非团
            foreach (var group in audit.AmbiguityGroups)
            {
                var numbers = group.Select(r => r.Number)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                audit.ExcludedNumberConflicts.Add(
                    "聚类歧义：容差不传递，以下记录相互连通但非全匹配，"
                    + "未自动归类——" + string.Join("、", numbers)
                    + "；输入序号：" + string.Join("、",
                        group.Select(r => (r.Index + 1).ToString(CultureInfo.InvariantCulture))));
            }

            return audit;
        }

        /// <summary>入库去重：异件同号时保留首次遇到、隔离后续。</summary>
        public List<WbMigrationRecord> RecordsToInsert()
        {
            var result = new List<WbMigrationRecord>();
            foreach (WbMigrationRecord r in Classes
                .Select(c => c.OrderBy(x => x.Index).First())
                .OrderBy(x => x.Index))
            {
                WbMigrationRecord kept = result.FirstOrDefault(x =>
                    string.Equals(x.Number, r.Number, StringComparison.OrdinalIgnoreCase));
                if (kept == null)
                    result.Add(r);
                else
                    ExcludedNumberConflicts.Add(
                        "实际不同、编号相同，未写入正式库：" + r.Number +
                        "；保留来源=" + kept.SourceFile +
                        "；隔离来源=" + r.SourceFile);
            }
            return result;
        }

        public IEnumerable<string> ToLogs()
        {
            if (!_strictMaterialColor)
                yield return "注意：材质/颜色宽松模式——任一方为空即视为匹配，未参与严格确认。";
            if (_enableDegenerateMatching)
                yield return "注意：退化孔低置信度匹配已开启——使用拟合平面特征参与匹配，可能增加误匹配风险。";
            yield return "共识别 " + Classes.Count.ToString(CultureInfo.InvariantCulture) +
                " 个实际零件类。";
            if (DegenerateHoleRecords.Count > 0)
            {
                string degenSuffix = _enableDegenerateMatching
                    ? " 个含退化孔记录（退化匹配已开启，已参与低置信度聚类）——"
                    : " 个含退化孔（非平面内环）记录，各作为独立类别入库、未参与自动聚类——";
                yield return "发现 " + DegenerateHoleRecords.Count.ToString(CultureInfo.InvariantCulture) +
                    degenSuffix +
                    string.Join("、", DegenerateHoleRecords.Select(r =>
                        (r.Number ?? "?") + "（序号 " + (r.Index + 1).ToString(CultureInfo.InvariantCulture) + "）"));
            }
            if (AmbiguityGroups.Count > 0)
                yield return "发现 " + AmbiguityGroups.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个聚类歧义组（容差不传递，未自动归类）。";
            if (SamePartDifferentNumbers.Count == 0)
                yield return "未发现实际相同但编号不同。";
            else foreach (string x in SamePartDifferentNumbers) yield return x;
            if (DifferentPartsSameNumber.Count == 0)
                yield return "未发现实际不同但编号相同。";
            else foreach (string x in DifferentPartsSameNumber) yield return x;
            foreach (string x in MirrorParts) yield return x;
            foreach (string x in ExcludedNumberConflicts) yield return x;
        }

        public IEnumerable<KeyValuePair<string, string>> AuditRows()
        {
            if (!_strictMaterialColor)
                yield return new KeyValuePair<string, string>(
                    "宽松模式", "材质/颜色任一方为空即视为匹配，未参与严格确认。");
            foreach (string x in SamePartDifferentNumbers)
                yield return new KeyValuePair<string, string>("同件异号", x);
            foreach (string x in DifferentPartsSameNumber)
                yield return new KeyValuePair<string, string>("异件同号", x);
            foreach (string x in MirrorParts)
                yield return new KeyValuePair<string, string>("镜像零件", x);
            foreach (string x in ExcludedNumberConflicts)
                yield return new KeyValuePair<string, string>("隔离异件同号", x);
        }

        /// <summary>导出聚类摘要为结构化 JSON（供外部复核和后续升级）</summary>
        public string ToClassificationJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"class_count\": " + Classes.Count.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"ambiguity_group_count\": " + AmbiguityGroups.Count.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"degenerate_count\": " + DegenerateHoleRecords.Count.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"strict_material_color\": " + _strictMaterialColor.ToString().ToLowerInvariant() + ",");
            sb.AppendLine("  \"classes\": [");
            for (int i = 0; i < Classes.Count; i++)
            {
                var cls = Classes[i];
                sb.Append("    { \"size\": " + cls.Count.ToString(CultureInfo.InvariantCulture) +
                    ", \"numbers\": [" +
                    string.Join(",", cls.Select(r => "\"" + (r.Number ?? "?") + "\"")) +
                    "], \"files\": [" +
                    string.Join(",", cls.Select(r => "\"" + (r.SourceFile ?? "").Replace("\\", "\\\\") + "\"")) +
                    "] }" + (i < Classes.Count - 1 ? "," : "") + "\n");
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"ambiguity_groups\": [");
            for (int i = 0; i < AmbiguityGroups.Count; i++)
            {
                var grp = AmbiguityGroups[i];
                sb.Append("    { \"size\": " + grp.Count.ToString(CultureInfo.InvariantCulture) +
                    ", \"numbers\": [" +
                    string.Join(",", grp.Select(r => "\"" + (r.Number ?? "?") + "\"")) +
                    "] }" + (i < AmbiguityGroups.Count - 1 ? "," : "") + "\n");
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"degenerate_records\": [");
            for (int i = 0; i < DegenerateHoleRecords.Count; i++)
            {
                var dr = DegenerateHoleRecords[i];
                sb.Append("    { \"number\": \"" + (dr.Number ?? "?") +
                    "\", \"source_file\": \"" + (dr.SourceFile ?? "").Replace("\\", "\\\\") +
                    "\", \"index\": " + (dr.Index + 1).ToString(CultureInfo.InvariantCulture) +
                    " }" + (i < DegenerateHoleRecords.Count - 1 ? "," : "") + "\n");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public IEnumerable<string> DiagnosticLines()
        {
            if (_enabled == null || _tolerances == null) yield break;
            // 导出歧义组诊断
            if (AmbiguityGroups.Count > 0)
            {
                yield return "===== 歧义组诊断（共 " +
                    AmbiguityGroups.Count.ToString(CultureInfo.InvariantCulture) + " 组） =====";
                foreach (var group in AmbiguityGroups)
                {
                    yield return "歧义组：" + string.Join(", ",
                        group.Select(r => r.Number + "(#" + (r.Index + 1).ToString(CultureInfo.InvariantCulture) + ")")) +
                        "；来源文件：" + string.Join(", ", group.Select(r => r.SourceFile).Distinct());
                }
            }
            int clsIdx = 0;
            foreach (List<WbMigrationRecord> group in Classes.Where(c => c.Count > 1))
            {
                for (int left = 0; left < group.Count; left++)
                for (int right = left + 1; right < group.Count; right++)
                {
                    WbMigrationRecord a = group[left], b = group[right];
                    yield return "===== 同件判定诊断 [类" +
                        (clsIdx + 1).ToString(CultureInfo.InvariantCulture) + "]：" +
                        a.Number + " \u2194 " + b.Number + " =====";
                    yield return "质心A=" + WbUtil.PointText(a.Feature.Centroid) +
                        "；质心B=" + WbUtil.PointText(b.Feature.Centroid) +
                        "；世界距离=" + WbUtil.FD(
                            a.Feature.Centroid.DistanceTo(b.Feature.Centroid));
                    yield return "孔数A/B=" +
                        a.Feature.Holes.Count.ToString(CultureInfo.InvariantCulture) +
                        "/" + b.Feature.Holes.Count.ToString(CultureInfo.InvariantCulture);

                    double[] bestSign = NonReflectionSigns[0];
                    double bestMax = double.PositiveInfinity, bestAvg = double.PositiveInfinity;
                    foreach (double[] sign in NonReflectionSigns)
                    {
                        var minima = a.Feature.Holes.Select(h =>
                            b.Feature.Holes.Count == 0
                                ? double.PositiveInfinity
                                : b.Feature.Holes.Min(x => Distance(h, x, sign))).ToList();
                        double maximum = minima.Count == 0 ? 0 : minima.Max();
                        double average = minima.Count == 0 ? 0 : minima.Average();
                        bool assignment = PerfectAssignment(
                            a.Feature.Holes, b.Feature.Holes, sign, _enabled, _tolerances, _isRel, _holeNormalTolerance);
                        yield return "变换(" + string.Join(",", sign.Select(WbUtil.FD)) +
                            ")：逐孔最近距离 最大=" + WbUtil.FD(maximum) +
                            "，平均=" + WbUtil.FD(average) +
                            "，完整匹配=" + assignment.ToString();
                        if (maximum < bestMax ||
                            (Math.Abs(maximum - bestMax) <= 1e-12 && average < bestAvg))
                        { bestMax = maximum; bestAvg = average; bestSign = sign; }
                    }
                    yield return "最佳变换=(" + string.Join(",", bestSign.Select(WbUtil.FD)) + ")";
                    for (int h = 0; h < a.Feature.Holes.Count; h++)
                    {
                        if (b.Feature.Holes.Count == 0)
                        { yield return "A孔[" + h.ToString(CultureInfo.InvariantCulture) + "]：B 中无孔。"; continue; }
                        int target = Enumerable.Range(0, b.Feature.Holes.Count)
                            .OrderBy(x => Distance(a.Feature.Holes[h], b.Feature.Holes[x], bestSign))
                            .First();
                        yield return "A孔[" + h.ToString(CultureInfo.InvariantCulture) +
                            "] \u2192 B孔[" + target.ToString(CultureInfo.InvariantCulture) +
                            "]：合成距离=" + WbUtil.FD(Distance(
                                a.Feature.Holes[h], b.Feature.Holes[target], bestSign)) +
                            "；A局部=" + HolePoint(a.Feature.Holes[h], new[] { 1.0, 1.0, 1.0 }) +
                            "；B变换后=" + HolePoint(b.Feature.Holes[target], bestSign);
                    }
                }
                clsIdx++;
            }
        }

        // ================================================================
        // 匹配逻辑
        // ================================================================
        private static bool Matches(
            WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, double mirrorTolerance, bool strictMaterialColor, double normalTol,
            double outerAreaRatio, double outerPerimMin,
            double degenMult = 2.0, bool enableDegenMatch = false)
        {
            return GlobalMatches(a, b, e, t, isRel, strictMaterialColor) &&
                   SameMirrorCode(a, b, mirrorTolerance) &&
                   GeometryMatches(a, b, e, t, isRel, false, normalTol, outerAreaRatio, outerPerimMin, degenMult, enableDegenMatch);
        }

        private static bool MirrorMatches(
            WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, double mirrorTolerance, bool strictMaterialColor, double normalTol,
            double outerAreaRatio, double outerPerimMin,
            double degenMult = 2.0, bool enableDegenMatch = false)
        {
            return GlobalMatches(a, b, e, t, isRel, strictMaterialColor) &&
                   !SameMirrorCode(a, b, mirrorTolerance) &&
                   GeometryMatches(a, b, e, t, isRel, true, normalTol, outerAreaRatio, outerPerimMin, degenMult, enableDegenMatch);
        }

        private static bool GlobalMatches(
            WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, bool strictMaterialColor)
        {
            if (!WbUtil.SameOptionalText(a.Material, b.Material, strictMaterialColor) ||
                !WbUtil.SameOptionalText(a.Color, b.Color, strictMaterialColor))
                return false;
            if (!BoundingBoxMatches(a.BoundingBox, b.BoundingBox, t[V5Criterion.Length], isRel[V5Criterion.Length]))
                return false;
            if (e[V5Criterion.Inertia] &&
                !a.Inertia.Zip(b.Inertia, (x, y) => WbUtil.WithinTolerance(x, y, t[V5Criterion.Inertia], isRel[V5Criterion.Inertia])).All(x => x))
                return false;
            return (!e[V5Criterion.Volume] || WbUtil.WithinTolerance(a.Volume, b.Volume, t[V5Criterion.Volume], isRel[V5Criterion.Volume]));
        }

        /// <summary>直接比较 double[3]，不再解析 CSV。</summary>
        private static bool BoundingBoxMatches(double[] a, double[] b, double tolerance, bool isRelative)
        {
            if (a == null || b == null) return true;
            if (a.Length != 3 || b.Length != 3) return false;
            if (!WbUtil.WithinTolerance(a[0], b[0], tolerance, isRelative)) return false;
            return WbUtil.WithinTolerance(a[1], b[1], tolerance, isRelative) &&
                   WbUtil.WithinTolerance(a[2], b[2], tolerance, isRelative);
        }

        private static string MirrorCode(WbFeature f, double tolerance)
        {
            double value = Math.Abs(f.HoleMirrorScore) > tolerance
                ? f.HoleMirrorScore : f.MirrorScore;
            return Math.Abs(value) <= tolerance ? string.Empty : (value >= 0 ? "A" : "B");
        }

        private static bool SameMirrorCode(WbFeature a, WbFeature b, double tolerance) =>
            string.Equals(MirrorCode(a, tolerance), MirrorCode(b, tolerance), StringComparison.Ordinal);

        /// <summary>将 b 侧孔坐标按轴排列+符号重新映射（退化路径用）。</summary>
        private static List<WbHole> RemapHoles(
            List<WbHole> holes, int[] perm, double[] sign)
        {
            return holes.Select(h =>
            {
                double[] coords = { h.U, h.V, h.W };
                double[] norms = { h.NU, h.NV, h.NW };
                return new WbHole(
                    h.Perimeter, h.Area,
                    coords[perm[0]] * sign[0],
                    coords[perm[1]] * sign[1],
                    coords[perm[2]] * sign[2],
                    norms[perm[0]] * sign[0],
                    norms[perm[1]] * sign[1],
                    norms[perm[2]] * sign[2]);
            }).ToList();
        }

        /// <summary>将 b 侧外轮廓法向按轴排列+符号重新映射（退化路径用）。</summary>
        private static List<WbOuterProfile> RemapProfiles(
            List<WbOuterProfile> profiles, int[] perm, double[] sign)
        {
            return profiles.Select(p =>
            {
                double[] norms = { p.NU, p.NV, p.NW };
                return new WbOuterProfile
                {
                    Area = p.Area,
                    Perimeter = p.Perimeter,
                    NU = norms[perm[0]] * sign[0],
                    NV = norms[perm[1]] * sign[1],
                    NW = norms[perm[2]] * sign[2]
                };
            }).ToList();
        }

        internal static bool GeometryMatches(
            WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, bool reflection, double normalTol,
            double outerAreaRatio, double outerPerimMin,
            double degenMult = 2.0, bool enableDegenMatch = false)
        {
            if (a.Holes.Count != b.Holes.Count) return false;
            double[][] signs = reflection ? ReflectionSigns : NonReflectionSigns;
            double holeTol = t[V5Criterion.HolePosition];

            // 退化路径：遍历全部 6 种轴排列 × N 种符号
            if (a.IsDegenerate || b.IsDegenerate)
            {
                foreach (int[] perm in AxisPermutations)
                foreach (double[] sign in signs)
                {
                    var bHolesRemapped = RemapHoles(b.Holes, perm, sign);
                    var bProfilesRemapped = RemapProfiles(b.OuterProfiles, perm, sign);

                    if (!PerfectAssignment(a.Holes, bHolesRemapped, IdentitySign, e, t, isRel, normalTol, degenMult, enableDegenMatch))
                        continue;
                    if (OuterProfilesMatch(a.OuterProfiles, bProfilesRemapped, IdentitySign, holeTol, normalTol, outerAreaRatio, outerPerimMin))
                        return true;
                }
                return false;
            }

            // 正常路径：非退化零件只遍历符号
            foreach (double[] sign in signs)
            {
                if (!PerfectAssignment(a.Holes, b.Holes, sign, e, t, isRel, normalTol, degenMult, enableDegenMatch))
                    continue;
                if (OuterProfilesMatch(a.OuterProfiles, b.OuterProfiles, sign, holeTol, normalTol, outerAreaRatio, outerPerimMin))
                    return true;
            }
            return false;
        }

        private static bool PerfectAssignment(
            List<WbHole> a, List<WbHole> b, double[] s,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, double normalTol,
            double degenMult = 2.0, bool enableDegenMatch = false)
        {
            if (a.Count != b.Count) return false;
            return WbUtil.TryBipartiteMatch(a, b,
                (left, right) => HoleCompatible(left, right, s, e, t, isRel, normalTol, degenMult, enableDegenMatch));
        }

        private static bool OuterProfilesMatch(
            List<WbOuterProfile> a, List<WbOuterProfile> b,
            double[] sign, double tolerance, double normalTol,
            double outerAreaRatio, double outerPerimMin)
        {
            if (a.Count == 0 && b.Count == 0) return true;
            if (a.Count != b.Count) return false;
            return WbUtil.TryBipartiteMatch(a, b,
                (left, right) => OuterProfileCompatible(left, right, sign, tolerance, normalTol, outerAreaRatio, outerPerimMin));
        }

        private static bool OuterProfileCompatible(
            WbOuterProfile a, WbOuterProfile b, double[] sign, double tolerance, double normalTol,
            double outerAreaRatio, double outerPerimMin)
        {
            if (!a.IsAvailable || !b.IsAvailable) return false;
            // 面积 + 周长 + 法向一致性判别
            if (Math.Abs(a.Area - b.Area) >
                Math.Max(Math.Max(a.Area, b.Area), 1) * outerAreaRatio ||
                Math.Abs(a.Perimeter - b.Perimeter) > Math.Max(tolerance, outerPerimMin))
                return false;
            return Math.Abs(a.NU * b.NU * sign[0] + a.NV * b.NV * sign[1] + a.NW * b.NW * sign[2]) >= normalTol;
        }

        private static bool HoleCompatible(
            WbHole a, WbHole b, double[] s,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, double normalTol,
            double degenMult = 2.0, bool enableDegenMatch = false)
        {
            // 退化孔匹配时使用拟合平面特征
            bool bothDegenerate = enableDegenMatch && a.IsDegenerate && b.IsDegenerate;

            if (e[V5Criterion.EdgeSum])
            {
                double edgeTol = bothDegenerate
                    ? t[V5Criterion.EdgeSum] * degenMult  // 退化孔放宽周长容差
                    : t[V5Criterion.EdgeSum];
                if (!WbUtil.WithinTolerance(a.Perimeter, b.Perimeter, edgeTol,
                    isRel[V5Criterion.EdgeSum]))
                    return false;
            }
            if (e[V5Criterion.HoleArea])
            {
                double areaA = bothDegenerate ? a.DegenerateProjectedArea : a.Area;
                double areaB = bothDegenerate ? b.DegenerateProjectedArea : b.Area;
                double areaTol = bothDegenerate
                    ? Math.Max(t[V5Criterion.HoleArea] * degenMult, 5.0)  // 退化孔最低面积容差 5%
                    : t[V5Criterion.HoleArea];
                if (!WbUtil.WithinTolerance(areaA, areaB, areaTol,
                    isRel[V5Criterion.HoleArea]))
                    return false;
            }

            if (e[V5Criterion.HolePosition])
            {
                double tol = t[V5Criterion.HolePosition];
                if (a.IsDegenerate || b.IsDegenerate) tol *= degenMult;
                double du = a.U - b.U * s[0];
                double dv = a.V - b.V * s[1];
                double dw = a.W - b.W * s[2];
                if (Math.Sqrt(du * du + dv * dv + dw * dw) >= tol)
                    return false;
            }

            // 退化孔匹配：使用拟合平面法向
            if (bothDegenerate)
            {
                double aLen = Math.Sqrt(a.DegenerateNormalU * a.DegenerateNormalU +
                    a.DegenerateNormalV * a.DegenerateNormalV +
                    a.DegenerateNormalW * a.DegenerateNormalW);
                double bLen = Math.Sqrt(b.DegenerateNormalU * b.DegenerateNormalU +
                    b.DegenerateNormalV * b.DegenerateNormalV +
                    b.DegenerateNormalW * b.DegenerateNormalW);
                if (aLen <= RhinoMath.ZeroTolerance || bLen <= RhinoMath.ZeroTolerance)
                    return true;
                double dot = Math.Abs(
                    a.DegenerateNormalU * b.DegenerateNormalU * s[0] +
                    a.DegenerateNormalV * b.DegenerateNormalV * s[1] +
                    a.DegenerateNormalW * b.DegenerateNormalW * s[2]);
                // 退化孔法向容差放宽
                return dot >= normalTol * 0.95;
            }

            double aLen2 = Math.Sqrt(a.NU * a.NU + a.NV * a.NV + a.NW * a.NW);
            double bLen2 = Math.Sqrt(b.NU * b.NU + b.NV * b.NV + b.NW * b.NW);
            if (aLen2 <= RhinoMath.ZeroTolerance || bLen2 <= RhinoMath.ZeroTolerance)
                return true; // 退化孔无法比较法向，跳过

            double dot2 = Math.Abs(a.NU * b.NU * s[0] + a.NV * b.NV * s[1] + a.NW * b.NW * s[2]);
            return dot2 >= normalTol;
        }

        private static double Distance(WbHole a, WbHole b, double[] sign)
        {
            double du = a.U - b.U * sign[0];
            double dv = a.V - b.V * sign[1];
            double dw = a.W - b.W * sign[2];
            return Math.Sqrt(du * du + dv * dv + dw * dw);
        }

        private static string HolePoint(WbHole h, double[] sign) =>
            "(" + WbUtil.FD(h.U * sign[0]) + "," +
            WbUtil.FD(h.V * sign[1]) + "," +
            WbUtil.FD(h.W * sign[2]) + ")";

        /// <summary>孔面积/周长谱快速比较：排序后逐项最大相对差异不超过 ratio。</summary>
        private static bool FastHoleSpectrumMatches(List<WbHole> a, List<WbHole> b, double ratio)
        {
            if (a.Count == 0 && b.Count == 0) return true;
            if (a.Count == 0 || b.Count == 0) return false;

            var aAreas = a.Select(h => h.Area).OrderBy(x => x).ToList();
            var bAreas = b.Select(h => h.Area).OrderBy(x => x).ToList();
            var aPerims = a.Select(h => h.Perimeter).OrderBy(x => x).ToList();
            var bPerims = b.Select(h => h.Perimeter).OrderBy(x => x).ToList();

            for (int i = 0; i < a.Count; i++)
            {
                double maxA = Math.Max(Math.Max(aAreas[i], bAreas[i]), 1e-10);
                if (Math.Abs(aAreas[i] - bAreas[i]) / maxA > ratio) return false;
                double maxP = Math.Max(Math.Max(aPerims[i], bPerims[i]), 1e-10);
                if (Math.Abs(aPerims[i] - bPerims[i]) / maxP > ratio) return false;
            }
            return true;
        }

        /// <summary>外轮廓面积谱快速比较：排序后逐项最大相对差异不超过 ratio。</summary>
        private static bool FastProfileSpectrumMatches(List<WbOuterProfile> a, List<WbOuterProfile> b, double ratio)
        {
            if (a.Count == 0 && b.Count == 0) return true;
            if (a.Count == 0 || b.Count == 0) return false;

            var aAreas = a.Select(p => p.Area).OrderBy(x => x).ToList();
            var bAreas = b.Select(p => p.Area).OrderBy(x => x).ToList();

            for (int i = 0; i < a.Count; i++)
            {
                double maxA = Math.Max(Math.Max(aAreas[i], bAreas[i]), 1e-10);
                if (Math.Abs(aAreas[i] - bAreas[i]) / maxA > ratio) return false;
            }
            return true;
        }
    }

    // ========================================================================
    // 检查点：断点续跑支持
    // ========================================================================
    internal sealed class WbMigrationCheckpoint
    {
        public string DatabasePath;
        public List<string> PendingFiles;
        public int FileIndex;
        public List<CheckpointRecord> CollectedRecords = new List<CheckpointRecord>();
        public List<string> Diagnostics = new List<string>();
        public List<string> FailedFiles = new List<string>();
        public int TotalErrorCount;

        internal sealed class CheckpointRecord
        {
            public int Index;
            public string Number;
            public string FeatureText;
            public string SourceFile;
        }

        public static WbMigrationCheckpoint Load(string checkpointPath)
        {
            if (!File.Exists(checkpointPath)) return null;
            try
            {
                string text = File.ReadAllText(checkpointPath, Encoding.UTF8);
                return Deserialize(text);
            }
            catch (Exception ex)
            {
                // 解析失败时不要静默回退——输出诊断以便排查
                throw new InvalidDataException(
                    "检查点文件解析失败（" + checkpointPath + "）：" + ex.Message, ex);
            }
        }

        public void Save(string checkpointPath)
        {
            string data = Serialize();
            File.WriteAllText(checkpointPath, data, Encoding.UTF8);
        }

        private string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine(DatabasePath ?? "");
            sb.AppendLine(PendingFiles != null ? string.Join("\u0002", PendingFiles) : "");
            sb.AppendLine(FileIndex.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(TotalErrorCount.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(FailedFiles != null ? string.Join("\u0002", FailedFiles) : "");
            sb.AppendLine(CollectedRecords != null ? CollectedRecords.Count.ToString(CultureInfo.InvariantCulture) : "0");
            if (CollectedRecords != null)
                foreach (var cr in CollectedRecords)
                {
                    sb.AppendLine((cr.Index.ToString(CultureInfo.InvariantCulture)) +
                        "\u0001" + Escape(cr.Number) +
                        "\u0001" + Escape(cr.SourceFile) +
                        "\u0001" + Escape(cr.FeatureText));
                }
            sb.AppendLine(Diagnostics != null ? Diagnostics.Count.ToString(CultureInfo.InvariantCulture) : "0");
            if (Diagnostics != null)
                foreach (var d in Diagnostics)
                    sb.AppendLine(Escape(d));
            return sb.ToString();
        }

        private static WbMigrationCheckpoint Deserialize(string data)
        {
            // 使用 StringReader.ReadLine() 读取每一行，保留空行，避免 RemoveEmptyEntries 导致字段位偏移
            var lines = new List<string>();
            using (var sr = new StringReader(data))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                    lines.Add(line);
            }
            if (lines.Count < 7)
                throw new InvalidDataException(
                    "检查点文件结构不完整：期望至少 7 行，实际 " + lines.Count + " 行。");
            var cp = new WbMigrationCheckpoint();
            int idx = 0;
            cp.DatabasePath = lines[idx++];
            cp.PendingFiles = string.IsNullOrEmpty(lines[idx]) ? new List<string>()
                : new List<string>(lines[idx].Split('\u0002'));
            idx++;
            cp.FileIndex = int.Parse(lines[idx++], CultureInfo.InvariantCulture);
            cp.TotalErrorCount = int.Parse(lines[idx++], CultureInfo.InvariantCulture);
            cp.FailedFiles = string.IsNullOrEmpty(lines[idx]) ? new List<string>()
                : new List<string>(lines[idx].Split('\u0002'));
            idx++;
            int recCount = int.Parse(lines[idx++], CultureInfo.InvariantCulture);
            cp.CollectedRecords = new List<CheckpointRecord>();
            for (int i = 0; i < recCount && idx < lines.Count; i++)
            {
                string[] parts = lines[idx++].Split('\u0001');
                if (parts.Length >= 4)
                    cp.CollectedRecords.Add(new CheckpointRecord
                    {
                        Index = int.Parse(parts[0], CultureInfo.InvariantCulture),
                        Number = Unescape(parts[1]),
                        SourceFile = Unescape(parts[2]),
                        FeatureText = Unescape(parts[3])
                    });
            }
            int diagCount = idx < lines.Count ? int.Parse(lines[idx++], CultureInfo.InvariantCulture) : 0;
            cp.Diagnostics = new List<string>();
            for (int i = 0; i < diagCount && idx < lines.Count; i++)
                cp.Diagnostics.Add(Unescape(lines[idx++]));
            return cp;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\\\", "\\");
        }

        public static string GetCheckpointPath(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) return string.Empty;
            return Path.GetFullPath(databasePath) + ".migration_checkpoint.json";
        }
    }

    // ========================================================================
    // 数据库存储（V9 schema 兼容）
    // ========================================================================
    internal static class WbMigrationStore
    {
        public static void CreateEmpty(string path)
        {
            var enabled = new Dictionary<V5Criterion, bool>
            {
                { V5Criterion.Length, true }, { V5Criterion.Inertia, true },
                { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true },
                { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, true }
            };
            var tolerances = new Dictionary<V5Criterion, double>
            {
                { V5Criterion.Length, 0.5 }, { V5Criterion.Inertia, 0.2 },
                { V5Criterion.Volume, 0.5 }, { V5Criterion.EdgeSum, 1 },
                { V5Criterion.HoleArea, 1 }, { V5Criterion.HolePosition, 0.5 }
            };
            var isRel = new Dictionary<V5Criterion, bool>
            {
                { V5Criterion.Length, true }, { V5Criterion.Inertia, true },
                { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true },
                { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, false }
            };
            var decimals = new Dictionary<V5Criterion, int>
            {
                { V5Criterion.Length, 2 }, { V5Criterion.Inertia, 2 },
                { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 },
                { V5Criterion.EdgeSum, 2 }, { V5Criterion.HoleArea, 2 },
                { V5Criterion.HolePosition, 4 }
            };
            // 不抛异常的重建
            Rebuild(path, new List<WbMigrationRecord>(), new WbMigrationAudit(),
                enabled, tolerances, decimals, isRel, 0.001, 0.99, 0.9986, true, creatingEmpty: true);
        }

        public static void Rebuild(
            string path, List<WbMigrationRecord> records, WbMigrationAudit audit,
            Dictionary<V5Criterion, bool> enabled,
            Dictionary<V5Criterion, double> tolerances,
            Dictionary<V5Criterion, int> decimals,
            Dictionary<V5Criterion, bool> isRel,
            double mirrorTolerance,
            double inertiaDegenerateRatio = 0.99,
            double holeNormalTolerance = 0.9986,
            bool strictMaterialColor = true,
            double outerProfileAreaRatio = 0.0001,
            double outerProfilePerimeterMin = 0.01,
            double degeneratePositionMultiplier = 2.0,
            bool enableDegenerateMatching = false,
            double fallbackTolerance = 0.001,
            bool creatingEmpty = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("数据库地址不能为空。");

            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!creatingEmpty && File.Exists(full))
                File.Copy(full,
                    full + ".v8backup_" +
                    DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
                    true);

            using (var c = new SQLiteConnection("Data Source=" + full + ";Version=3;"))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    // V8 兼容 schema
                    cmd.CommandText =
                        "PRAGMA foreign_keys=ON;" +
                        "DROP TABLE IF EXISTS outer_vertices;" +
                        "DROP TABLE IF EXISTS outer_profiles;" +
                        "DROP TABLE IF EXISTS holes;" +
                        "DROP TABLE IF EXISTS parts;" +
                        "DROP TABLE IF EXISTS classification_profile;" +
                        "DROP TABLE IF EXISTS migration_audit;" +
                        "DROP TABLE IF EXISTS migration_runs;" +
                        "DROP TABLE IF EXISTS database_info;" +
                        "CREATE TABLE database_info (" +
                            "id INTEGER PRIMARY KEY CHECK(id=1)," +
                            "schema_version INTEGER NOT NULL," +
                            "algorithm_version TEXT NOT NULL," +
                            "created_time TEXT NOT NULL);" +
                        "CREATE TABLE classification_profile (" +
                            "profile_id INTEGER PRIMARY KEY," +
                            "settings_text TEXT NOT NULL);" +
                        "CREATE TABLE parts (" +
                            "part_id INTEGER PRIMARY KEY AUTOINCREMENT," +
                            "profile_id INTEGER NOT NULL," +
                            "part_number TEXT NOT NULL UNIQUE COLLATE NOCASE," +
                            "material TEXT, color TEXT," +
                            "entity_type INTEGER NOT NULL," +
                            "is_solid INTEGER NOT NULL," +
                            "volume REAL NOT NULL," +
                            "surface_area REAL NOT NULL," +
                            "bbox_max REAL NOT NULL," +
                            "bbox_mid REAL NOT NULL," +
                            "bbox_min REAL NOT NULL," +
                            "bbox_count INTEGER NOT NULL CHECK(bbox_count=3)," +
                            "inertia_1 REAL NOT NULL," +
                            "inertia_2 REAL NOT NULL," +
                            "inertia_3 REAL NOT NULL," +
                            "hole_count INTEGER NOT NULL," +  // Brep 内环计数（非物理孔数，贯穿孔计为 2）
                            "hole_perimeter_sum REAL NOT NULL," +
                            "hole_area_sum REAL NOT NULL," +
                            "source_file TEXT NOT NULL," +
                            "source_index INTEGER NOT NULL," +
                            "created_time TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP," +
                            "FOREIGN KEY(profile_id) REFERENCES classification_profile(profile_id));" +
                        "CREATE TABLE holes (" +
                            "hole_id INTEGER PRIMARY KEY AUTOINCREMENT," +
                            "part_id INTEGER NOT NULL," +
                            "hole_index INTEGER NOT NULL," +
                            "center_u REAL NOT NULL," +
                            "center_v REAL NOT NULL," +
                            "center_w REAL NOT NULL," +
                            "perimeter REAL NOT NULL," +
                            "enclosed_area REAL NOT NULL," +
                            "normal_u REAL NOT NULL," +
                            "normal_v REAL NOT NULL," +
                            "normal_w REAL NOT NULL," +
                            "FOREIGN KEY(part_id) REFERENCES parts(part_id) ON DELETE CASCADE," +
                            "UNIQUE(part_id,hole_index));" +
                        "CREATE TABLE outer_profiles (" +
                            "part_id INTEGER NOT NULL," +
                            "profile_index INTEGER NOT NULL," +
                            "enclosed_area REAL NOT NULL," +
                            "perimeter REAL NOT NULL," +
                            "normal_u REAL NOT NULL," +
                            "normal_v REAL NOT NULL," +
                            "normal_w REAL NOT NULL," +
                            "FOREIGN KEY(part_id) REFERENCES parts(part_id) ON DELETE CASCADE," +
                            "UNIQUE(part_id,profile_index));" +
                        "CREATE TABLE migration_runs (" +
                            "run_id INTEGER PRIMARY KEY AUTOINCREMENT," +
                            "start_time TEXT NOT NULL," +
                            "source_count INTEGER NOT NULL," +
                            "inserted_count INTEGER NOT NULL," +
                            "completed INTEGER NOT NULL);" +
                        "CREATE TABLE migration_audit (" +
                            "audit_id INTEGER PRIMARY KEY AUTOINCREMENT," +
                            "issue_type TEXT NOT NULL," +
                            "detail TEXT NOT NULL);";
                    cmd.ExecuteNonQuery();

                    using (var tx = c.BeginTransaction())
                    {
                        string now = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                        string settings = SettingsText(enabled, tolerances, decimals, isRel,
                            mirrorTolerance, inertiaDegenerateRatio, holeNormalTolerance, strictMaterialColor,
                            outerProfileAreaRatio, outerProfilePerimeterMin, degeneratePositionMultiplier,
                            enableDegenerateMatching, fallbackTolerance);

                        cmd.CommandText =
                            "INSERT INTO database_info (id,schema_version,algorithm_version,created_time) " +
                            "VALUES (1,9,'WbClassificationMigration_v9',@time);" +
                            "INSERT INTO classification_profile (profile_id,settings_text) " +
                            "VALUES (1,@settings);" +
                            "INSERT INTO migration_runs (start_time,source_count,inserted_count,completed) " +
                            "VALUES (@time,@source,@inserted,1);";
                        cmd.Parameters.AddWithValue("@time", now);
                        cmd.Parameters.AddWithValue("@settings", settings);
                        cmd.Parameters.AddWithValue("@source",
                            audit.Classes.Sum(cls => cls.Count));
                        cmd.Parameters.AddWithValue("@inserted", records.Count);
                        cmd.ExecuteNonQuery();

                        // 插入零件
                        foreach (WbMigrationRecord r in records)
                            InsertPart(c, cmd, r);

                        // 写入审计
                        foreach (var row in audit.AuditRows())
                        {
                            cmd.CommandText =
                                "INSERT INTO migration_audit (issue_type,detail) VALUES (@t,@d);";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@t", row.Key);
                            cmd.Parameters.AddWithValue("@d", row.Value);
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }
                }
            }
        }

        private static void InsertPart(
            SQLiteConnection c, SQLiteCommand cmd, WbMigrationRecord r)
        {
            double[] box = r.Feature.BoundingBox;
            if (box == null || box.Length != 3)
                throw new InvalidOperationException("包围盒必须包含三条边。");

            cmd.CommandText =
                "INSERT INTO parts (profile_id,part_number,material,color,entity_type,is_solid," +
                "volume,surface_area,bbox_max,bbox_mid,bbox_min,bbox_count," +
                "inertia_1,inertia_2,inertia_3,hole_count,hole_perimeter_sum,hole_area_sum," +
                "source_file,source_index) " +
                "VALUES (1,@n,@mat,@col,@ety,@sol,@vol,@area,@max,@mid,@min,@cnt," +
                "@i1,@i2,@i3,@hc,@hps,@has,@file,@idx);";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@n", r.Number);
            cmd.Parameters.AddWithValue("@mat", r.Feature.Material);
            cmd.Parameters.AddWithValue("@col", r.Feature.Color);
            cmd.Parameters.AddWithValue("@ety", 1);
            cmd.Parameters.AddWithValue("@sol", r.Feature.Volume > 0 ? 1 : 0);
            cmd.Parameters.AddWithValue("@vol", r.Feature.Volume);
            cmd.Parameters.AddWithValue("@area", r.Feature.Area);
            cmd.Parameters.AddWithValue("@max", box[0]);
            cmd.Parameters.AddWithValue("@mid", box[1]);
            cmd.Parameters.AddWithValue("@min", box[2]);
            cmd.Parameters.AddWithValue("@cnt", 3);
            cmd.Parameters.AddWithValue("@i1", r.Feature.Inertia[0]);
            cmd.Parameters.AddWithValue("@i2", r.Feature.Inertia[1]);
            cmd.Parameters.AddWithValue("@i3", r.Feature.Inertia[2]);
            cmd.Parameters.AddWithValue("@hc", r.Feature.Holes.Count);
            cmd.Parameters.AddWithValue("@hps", r.Feature.Holes.Sum(h => h.Perimeter));
            cmd.Parameters.AddWithValue("@has", r.Feature.Holes.Sum(h => h.Area));
            cmd.Parameters.AddWithValue("@file", r.SourceFile);
            cmd.Parameters.AddWithValue("@idx", r.Index + 1);
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            cmd.Parameters.Clear();
            long id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);

            // 孔
            for (int i = 0; i < r.Feature.Holes.Count; i++)
            {
                WbHole h = r.Feature.Holes[i];
                cmd.CommandText =
                    "INSERT INTO holes (part_id,hole_index,center_u,center_v,center_w," +
                    "perimeter,enclosed_area,normal_u,normal_v,normal_w) " +
                    "VALUES (@id,@idx,@u,@v,@w,@p,@a,@nu,@nv,@nw);";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@idx", i);
                cmd.Parameters.AddWithValue("@u", h.U);
                cmd.Parameters.AddWithValue("@v", h.V);
                cmd.Parameters.AddWithValue("@w", h.W);
                cmd.Parameters.AddWithValue("@p", h.Perimeter);
                cmd.Parameters.AddWithValue("@a", h.Area);
                cmd.Parameters.AddWithValue("@nu", h.NU);
                cmd.Parameters.AddWithValue("@nv", h.NV);
                cmd.Parameters.AddWithValue("@nw", h.NW);
                cmd.ExecuteNonQuery();
            }

            // 外轮廓
            for (int pi = 0; pi < r.Feature.OuterProfiles.Count; pi++)
            {
                WbOuterProfile p = r.Feature.OuterProfiles[pi];
                cmd.CommandText =
                    "INSERT INTO outer_profiles (part_id,profile_index,enclosed_area,perimeter," +
                    "normal_u,normal_v,normal_w) " +
                    "VALUES (@id,@pi,@area,@per,@nu,@nv,@nw);";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@pi", pi);
                cmd.Parameters.AddWithValue("@area", p.Area);
                cmd.Parameters.AddWithValue("@per", p.Perimeter);
                cmd.Parameters.AddWithValue("@nu", p.NU);
                cmd.Parameters.AddWithValue("@nv", p.NV);
                cmd.Parameters.AddWithValue("@nw", p.NW);
                cmd.ExecuteNonQuery();
            }
        }

        public static void Verify(string path, List<WbMigrationRecord> records)
        {
            using (var c = new SQLiteConnection(
                "Data Source=" + Path.GetFullPath(path) + ";Version=3;"))
            {
                c.Open();
                var ids = new List<long>();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT part_id,part_number,source_index,source_file," +
                        "volume,surface_area,bbox_max,bbox_mid,bbox_min,bbox_count," +
                        "inertia_1,inertia_2,inertia_3,hole_count,hole_perimeter_sum,hole_area_sum " +
                        "FROM parts ORDER BY source_index;";
                    using (var reader = cmd.ExecuteReader())
                    {
                        int row = 0;
                        while (reader.Read())
                        {
                            if (row >= records.Count)
                                throw new InvalidOperationException(
                                    "数据库回读记录数多于写入记录数。");
                            WbMigrationRecord r = records[row];
                            double[] box = r.Feature.BoundingBox;
                            bool valid =
                                string.Equals(
                                    Convert.ToString(reader["part_number"]),
                                    r.Number, StringComparison.Ordinal) &&
                                Convert.ToInt32(reader["source_index"]) == r.Index + 1 &&
                                string.Equals(
                                    Path.GetFullPath(Convert.ToString(reader["source_file"])),
                                    Path.GetFullPath(r.SourceFile),
                                    StringComparison.OrdinalIgnoreCase) &&
                                Eql(reader["volume"], r.Feature.Volume) &&
                                Eql(reader["surface_area"], r.Feature.Area) &&
                                Eql(reader["bbox_max"], box[0]) &&
                                Eql(reader["bbox_mid"], box[1]) &&
                                Eql(reader["bbox_min"], box[2]) &&
                                Convert.ToInt32(reader["bbox_count"]) == 3 &&
                                Eql(reader["inertia_1"], r.Feature.Inertia[0]) &&
                                Eql(reader["inertia_2"], r.Feature.Inertia[1]) &&
                                Eql(reader["inertia_3"], r.Feature.Inertia[2]) &&
                                Convert.ToInt32(reader["hole_count"]) == r.Feature.Holes.Count &&
                                Eql(reader["hole_perimeter_sum"],
                                    r.Feature.Holes.Sum(h => h.Perimeter)) &&
                                Eql(reader["hole_area_sum"],
                                    r.Feature.Holes.Sum(h => h.Area));
                            if (!valid)
                                throw new InvalidOperationException(
                                    "数据库回读校核失败，记录序号：" +
                                    (row + 1).ToString(CultureInfo.InvariantCulture));
                            ids.Add(Convert.ToInt64(reader["part_id"]));
                            row++;
                        }
                        if (row != records.Count)
                            throw new InvalidOperationException(
                                "数据库回读记录数与写入记录数不一致。");
                    }
                }

                // 校核孔
                for (int i = 0; i < records.Count; i++)
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT center_u,center_v,center_w,perimeter,enclosed_area," +
                        "normal_u,normal_v,normal_w " +
                        "FROM holes WHERE part_id=@id ORDER BY hole_index;";
                    cmd.Parameters.AddWithValue("@id", ids[i]);
                    using (var reader = cmd.ExecuteReader())
                    {
                        int hi = 0;
                        while (reader.Read())
                        {
                            if (hi >= records[i].Feature.Holes.Count)
                                throw new InvalidOperationException(
                                    "孔位回读记录数多于写入记录数。");
                            WbHole h = records[i].Feature.Holes[hi];
                            if (!Eql(reader["center_u"], h.U) ||
                                !Eql(reader["center_v"], h.V) ||
                                !Eql(reader["center_w"], h.W) ||
                                !Eql(reader["perimeter"], h.Perimeter) ||
                                !Eql(reader["enclosed_area"], h.Area) ||
                                !Eql(reader["normal_u"], h.NU) ||
                                !Eql(reader["normal_v"], h.NV) ||
                                !Eql(reader["normal_w"], h.NW))
                                throw new InvalidOperationException(
                                    "孔位回读校核失败，记录序号：" +
                                    (i + 1).ToString(CultureInfo.InvariantCulture));
                            hi++;
                        }
                        if (hi != records[i].Feature.Holes.Count)
                            throw new InvalidOperationException(
                                "孔位回读记录数不一致，记录序号：" +
                                (i + 1).ToString(CultureInfo.InvariantCulture));
                    }
                }

                // 校核外轮廓
                for (int i = 0; i < records.Count; i++)
                using (var cmd = c.CreateCommand())
                {
                    var profiles = records[i].Feature.OuterProfiles;
                    cmd.CommandText =
                        "SELECT profile_index,enclosed_area,perimeter," +
                        "normal_u,normal_v,normal_w,vertex_count " +
                        "FROM outer_profiles WHERE part_id=@id ORDER BY profile_index;";
                    cmd.Parameters.AddWithValue("@id", ids[i]);
                    using (var reader = cmd.ExecuteReader())
                    {
                        int pi = 0;
                        while (reader.Read())
                        {
                            if (pi >= profiles.Count)
                                throw new InvalidOperationException("外轮廓回读数过多。");
                            WbOuterProfile profile = profiles[pi];
                            if (Convert.ToInt32(reader["profile_index"]) != pi ||
                                !Eql(reader["enclosed_area"], profile.Area) ||
                                !Eql(reader["perimeter"], profile.Perimeter) ||
                                !Eql(reader["normal_u"], profile.NU) ||
                                !Eql(reader["normal_v"], profile.NV) ||
                                !Eql(reader["normal_w"], profile.NW))
                                throw new InvalidOperationException(
                                    "外轮廓回读校核失败，记录序号：" +
                                    (i + 1).ToString(CultureInfo.InvariantCulture));
                            pi++;
                        }
                        if (pi != profiles.Count)
                            throw new InvalidOperationException("外轮廓回读数不一致。");
                    }

                }
            }
        }

        private static bool Eql(object dbValue, double expected) =>
            !Convert.IsDBNull(dbValue) &&
            Math.Abs(Convert.ToDouble(dbValue, CultureInfo.InvariantCulture) - expected) <= 1e-10;

        private static string SettingsText(
            Dictionary<V5Criterion, bool> enabled,
            Dictionary<V5Criterion, double> tolerances,
            Dictionary<V5Criterion, int> decimals,
            Dictionary<V5Criterion, bool> isRel,
            double mirrorTolerance,
            double inertiaDegenerateRatio,
            double holeNormalTolerance,
            bool strictMaterialColor,
            double outerProfileAreaRatio,
            double outerProfilePerimeterMin,
            double degeneratePositionMultiplier,
            bool enableDegenerateMatching,
            double fallbackTolerance)
        {
            return string.Join(";",
                enabled.Keys.OrderBy(x => x).Select(x =>
                    x + "=" + enabled[x] +
                    ",tol=" + tolerances[x].ToString("R", CultureInfo.InvariantCulture) +
                    (isRel[x] ? "%" : "") +
                    ",precision=" + decimals[x].ToString(CultureInfo.InvariantCulture))) +
                ";mirror_tol=" + mirrorTolerance.ToString("R", CultureInfo.InvariantCulture) +
                ";inertia_degen=" + inertiaDegenerateRatio.ToString("R", CultureInfo.InvariantCulture) +
                ";normal_tol=" + holeNormalTolerance.ToString("R", CultureInfo.InvariantCulture) +
                ";strict_mat=" + strictMaterialColor.ToString(CultureInfo.InvariantCulture) +
                ";outer_area=" + outerProfileAreaRatio.ToString("R", CultureInfo.InvariantCulture) +
                ";outer_perim=" + outerProfilePerimeterMin.ToString("R", CultureInfo.InvariantCulture) +
                ";degen_mult=" + degeneratePositionMultiplier.ToString("R", CultureInfo.InvariantCulture) +
                ";degen_match=" + enableDegenerateMatching.ToString(CultureInfo.InvariantCulture) +
                ";fallback_tol=" + fallbackTolerance.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    // ========================================================================
    // 镜像判定回归测试
    //
    // 执行方式（发布前执行）：
    //   1. Visual Studio 即时窗口 → WbMirrorTest.RunAllAndLog()
    //   2. 或在单元测试项目中调用 WbMirrorTest.RunAll()
    //   3. 或在 Rhino Python 编辑器中：
    //      import clr; clr.AddReference("GH1")
    //      from GH1.Component.Library import WbMirrorTest
    //      WbMirrorTest.RunAllAndLog()
    // ========================================================================
    internal static class WbMirrorTest
    {
        public sealed class TestResult
        {
            public string TestName;
            public bool Passed;
            public string Detail;
        }

        /// <summary>
        /// 运行全部回归测试并输出结果到 Rhino 命令行。
        /// 发布前必须调用，全部通过才能发布。
        /// </summary>
        public static void RunAllAndLog()
        {
            var result = RunAll();
            RhinoApp.WriteLine("=== WbClassificationMigration 回归测试 ===");
            foreach (var r in result.Item2)
                RhinoApp.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  [{0}] {1}: {2}", r.Passed ? "PASS" : "FAIL", r.TestName, r.Detail));
            RhinoApp.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "=== 总计: {0}/{1} 通过 ===",
                result.Item2.Count(x => x.Passed), result.Item2.Count));
        }

        public static Tuple<bool, List<TestResult>> RunAll()
        {
            var results = new List<TestResult>();
            double mirrorTol = 0.001;
            var e = new Dictionary<V5Criterion, bool> {
                { V5Criterion.Length, true }, { V5Criterion.Inertia, true },
                { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true },
                { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, true }
            };
            var t = new Dictionary<V5Criterion, double> {
                { V5Criterion.Length, 0.5 }, { V5Criterion.Inertia, 0.2 },
                { V5Criterion.Volume, 0.5 }, { V5Criterion.EdgeSum, 1 },
                { V5Criterion.HoleArea, 1 }, { V5Criterion.HolePosition, 0.5 }
            };
            var r = new Dictionary<V5Criterion, bool> {
                { V5Criterion.Length, true }, { V5Criterion.Inertia, true },
                { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true },
                { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, false }
            };

            results.Add(TestIdenticalNotMirror(mirrorTol, e, t, r));
            results.Add(TestBMeansMirror(mirrorTol, e, t, r));
            results.Add(TestABDifferentNotSame(mirrorTol, e, t, r));
            results.Add(TestZeroMirrorScore(mirrorTol, e, t, r));
            results.Add(TestDegenerateMirror(mirrorTol, e, t, r));

            // 真实几何回归测试（Brep → 特征提取 → 镜像判定 完整管线）
            results.Add(TestRealGeometryMirrorDetection(mirrorTol, e, t, r));
            results.Add(TestRealGeometrySameNotMirror(mirrorTol, e, t, r));
            results.Add(TestRealGeometryDegenerateNotCrash(mirrorTol, e, t, r));
            results.Add(TestRealGeometryNearSymmetricNotMirror(mirrorTol, e, t, r));
            results.Add(TestRealGeometryBatchStatistics(mirrorTol, e, t, r));

            // 检查点解析回归测试
            results.AddRange(WbCheckpointTest.RunAll());

            // 退化孔匹配回归测试
            results.AddRange(WbDegenerateMatchingTest.RunAll().Select(tc => new TestResult
            {
                TestName = tc.TestName,
                Passed = tc.Passed,
                Detail = tc.Detail
            }));

            bool allPassed = results.All(x => x.Passed);
            return Tuple.Create(allPassed, results);
        }

        private static WbFeature MakeFeature(double mirrorScore, double holeMirrorScore,
            string material = "", string color = "",
            double[] bbox = null, double[] inertia = null,
            double area = 1000, double volume = 500,
            int holeCount = 2, bool isDegenerate = false)
        {
            bbox = bbox ?? new[] { 100.0, 50.0, 30.0 };
            inertia = inertia ?? new[] { 2000.0, 1000.0, 500.0 };
            var f = new WbFeature
            {
                Material = material, Color = color,
                BoundingBox = bbox, Area = area, Volume = volume,
                Inertia = inertia,
                MirrorScore = mirrorScore,
                HoleMirrorScore = holeMirrorScore,
                IsDegenerate = isDegenerate
            };
            for (int i = 0; i < holeCount; i++)
                f.Holes.Add(new WbHole(10, 5, i * 20.0, i * 10.0, 0, 0, 0, 1));
            return f;
        }

        private static TestResult TestIdenticalNotMirror(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            var a = MakeFeature(0.5, 0.3);
            var b = MakeFeature(0.5, 0.3);
            string mirrorA = MirrorCode(a, mirrorTol);
            string mirrorB = MirrorCode(b, mirrorTol);
            bool sameCode = SameMirrorCode(a, b, mirrorTol);
            bool isMirror = MirrorMatches(a, b, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
            bool passed = sameCode && !isMirror;
            return new TestResult
            {
                TestName = "完全相同件不判定为镜像",
                Passed = passed,
                Detail = passed ? "OK" :
                    "SameCode=" + sameCode + " IsMirror=" + isMirror
            };
        }

        private static TestResult TestBMeansMirror(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            var a = MakeFeature(0.5, 0.3);
            var b = MakeFeature(-0.5, -0.3);
            string mirrorA = MirrorCode(a, mirrorTol);
            string mirrorB = MirrorCode(b, mirrorTol);
            bool passed = mirrorA == "A" && mirrorB == "B";
            return new TestResult
            {
                TestName = "正负MirrorScore分别对应A/B",
                Passed = passed,
                Detail = passed ? "OK" : "Got " + mirrorA + "/" + mirrorB
            };
        }

        private static TestResult TestABDifferentNotSame(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            var a = MakeFeature(0.5, 0.3);
            var b = MakeFeature(-0.5, -0.3);
            bool sameCode = SameMirrorCode(a, b, mirrorTol);
            bool matches = Matches(a, b, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
            bool mirror = MirrorMatches(a, b, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
            bool passed = !sameCode && !matches && mirror;
            return new TestResult
            {
                TestName = "A/B不同代码不是同件但互为镜像",
                Passed = passed,
                Detail = passed ? "OK" :
                    "SameCode=" + sameCode + " Matches=" + matches + " Mirror=" + mirror
            };
        }

        private static TestResult TestZeroMirrorScore(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            var a = MakeFeature(0.0, 0.0);
            var b = MakeFeature(0.0, 0.0);
            string mirrorA = MirrorCode(a, mirrorTol);
            string mirrorB = MirrorCode(b, mirrorTol);
            bool sameCode = SameMirrorCode(a, b, mirrorTol);
            bool passed = mirrorA == string.Empty && mirrorB == string.Empty && sameCode;
            return new TestResult
            {
                TestName = "零MirrorScore返回空代码",
                Passed = passed,
                Detail = passed ? "OK" :
                    "A='" + mirrorA + "' B='" + mirrorB + "' SameCode=" + sameCode
            };
        }

        private static TestResult TestDegenerateMirror(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            var a = MakeFeature(0.6, 0.4, isDegenerate: true);
            var b = MakeFeature(-0.6, -0.4, isDegenerate: true);
            bool mirror = MirrorMatches(a, b, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
            return new TestResult
            {
                TestName = "退化件镜像检测不崩溃",
                Passed = true,
                Detail = "退化件镜像检测=" + mirror
            };
        }

        // ================================================================
        // 真实几何回归测试 — 验证 Brep → WbFeature → 镜像判定 完整管线
        // ================================================================

        /// <summary>
        /// 创建非对称测试 Brep：10×8×6 的盒子，在 (2,0,0) 处挖一个偏置圆柱孔（直径 2）。
        /// </summary>
        private static Brep CreateAsymmetricTestBrep()
        {
            return CreateBoxWithHole(5, 4, 3, new Point3d(2, 0, -5), 1.0, 10.0);
        }

        /// <summary>将 Brep 沿 YZ 平面镜像（X 坐标取反），产生左右对称件。</summary>
        private static Brep CreateMirrorBrep(Brep original)
        {
            Brep mirror = original.DuplicateBrep();
            mirror.Transform(Transform.Mirror(Plane.WorldYZ));
            return mirror;
        }

        /// <summary>
        /// 创建带偏置圆柱孔的盒体 Brep。用于构造非对称测试样本。
        /// 若 BooleanDifference 失败则抛出 InvalidOperationException。
        /// </summary>
        /// <param name="boxX">盒体 X 半长</param>
        /// <param name="boxY">盒体 Y 半长</param>
        /// <param name="boxZ">盒体 Z 半长</param>
        /// <param name="holeCenter">圆柱孔中心坐标（含 Z 方向穿透起点）</param>
        /// <param name="holeRadius">圆柱孔半径</param>
        /// <param name="holeHeight">圆柱孔高度（Z 方向穿透长度）</param>
        private static Brep CreateBoxWithHole(double boxX, double boxY, double boxZ,
            Point3d holeCenter, double holeRadius, double holeHeight)
        {
            var box = new Box(Plane.WorldXY,
                new Interval(-boxX, boxX), new Interval(-boxY, boxY), new Interval(-boxZ, boxZ));
            Brep boxBrep = box.ToBrep();
            if (boxBrep == null)
                throw new InvalidOperationException("盒体创建失败。");

            var cylPlane = new Plane(holeCenter, Vector3d.ZAxis);
            var cyl = new Cylinder(new Circle(cylPlane, holeRadius), holeHeight);
            Brep cylBrep = cyl.ToBrep(true, true);
            if (cylBrep == null)
                throw new InvalidOperationException("圆柱体创建失败。");

            Brep[] diff = Brep.CreateBooleanDifference(
                new List<Brep> { boxBrep }, new List<Brep> { cylBrep }, 0.001);
            if (diff == null || diff.Length == 0)
                throw new InvalidOperationException("BooleanDifference 失败。");
            return diff[0];
        }

        /// <summary>场景 1：非对称 Brep 与 其镜像 应被判定为镜像对。</summary>
        private static TestResult TestRealGeometryMirrorDetection(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            try
            {
                Brep brep = CreateAsymmetricTestBrep();
                Brep brepMirror = CreateMirrorBrep(brep);

                var decimals = new Dictionary<V5Criterion, int>
                {
                    { V5Criterion.Length, 4 }, { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 },
                    { V5Criterion.Inertia, 2 }, { V5Criterion.EdgeSum, 4 },
                    { V5Criterion.HoleArea, 2 }, { V5Criterion.HolePosition, 2 }
                };

                WbFeature f1 = WbFeature.Create("steel", "red", brep, decimals, 0.9986, 0.001, 0);
                WbFeature f2 = WbFeature.Create("steel", "red", brepMirror, decimals, 0.9986, 0.001, 1);

                bool sameCode = SameMirrorCode(f1, f2, mirrorTol);
                bool isMirror = MirrorMatches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
                bool isMatch = Matches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);

                bool passed = !sameCode && isMirror && !isMatch;
                return new TestResult
                {
                    TestName = "真实几何:非对称件+镜像判定为镜像对",
                    Passed = passed,
                    Detail = passed ? "OK"
                        : string.Format(CultureInfo.InvariantCulture,
                            "SameCode={0} Mirror={1} Match={2} MS={3} HMS={4} / MMS={5} MHMS={6}",
                            sameCode, isMirror, isMatch,
                            WbUtil.FR(f1.MirrorScore), WbUtil.FR(f1.HoleMirrorScore),
                            WbUtil.FR(f2.MirrorScore), WbUtil.FR(f2.HoleMirrorScore))
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "真实几何:非对称件+镜像判定为镜像对",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        /// <summary>场景 2：相同 Brep 重复提取 → SameMirrorCode + Matches，不判为镜像。</summary>
        private static TestResult TestRealGeometrySameNotMirror(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            try
            {
                Brep brep = CreateAsymmetricTestBrep();

                var decimals = new Dictionary<V5Criterion, int>
                {
                    { V5Criterion.Length, 4 }, { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 },
                    { V5Criterion.Inertia, 2 }, { V5Criterion.EdgeSum, 4 },
                    { V5Criterion.HoleArea, 2 }, { V5Criterion.HolePosition, 2 }
                };

                WbFeature f1 = WbFeature.Create("steel", "red", brep, decimals, 0.9986, 0.001, 0);
                WbFeature f2 = WbFeature.Create("steel", "red", brep, decimals, 0.9986, 0.001, 1);

                bool sameCode = SameMirrorCode(f1, f2, mirrorTol);
                bool isMirror = MirrorMatches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
                bool isMatch = Matches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);

                // 同一 Brep 提取两次：镜像码相同、能匹配为同件、不判为镜像
                bool passed = sameCode && isMatch && !isMirror;
                return new TestResult
                {
                    TestName = "真实几何:同Brep判为同件非镜像",
                    Passed = passed,
                    Detail = passed ? "OK"
                        : string.Format(CultureInfo.InvariantCulture,
                            "SameCode={0} Match={1} Mirror={2}", sameCode, isMatch, isMirror)
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "真实几何:同Brep判为同件非镜像",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        /// <summary>场景 3：退化几何（立方体，惯量退化）走完整管线不崩溃且不判为镜像。</summary>
        private static TestResult TestRealGeometryDegenerateNotCrash(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            try
            {
                // 立方体 1×1×1：三轴惯量相等 → IsDegenerate=true
                var box = new Box(Plane.WorldXY,
                    new Interval(-0.5, 0.5), new Interval(-0.5, 0.5), new Interval(-0.5, 0.5));
                Brep cube = box.ToBrep();
                if (cube == null) throw new InvalidOperationException("立方体创建失败。");

                var decimals = new Dictionary<V5Criterion, int>
                {
                    { V5Criterion.Length, 4 }, { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 },
                    { V5Criterion.Inertia, 2 }, { V5Criterion.EdgeSum, 4 },
                    { V5Criterion.HoleArea, 2 }, { V5Criterion.HolePosition, 2 }
                };

                WbFeature f1 = WbFeature.Create("steel", "", cube, decimals, 0.99, 0.001, 0);
                WbFeature f2 = WbFeature.Create("steel", "", cube, decimals, 0.99, 0.001, 1);

                // 立方体应被检测为惯量退化
                bool degenOK = f1.IsDegenerate && f2.IsDegenerate;
                bool sameCode = SameMirrorCode(f1, f2, mirrorTol);
                bool isMirror = MirrorMatches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);

                // 退化+同镜像码+不判镜像（相同立方体不可能互为镜像）
                bool passed = degenOK && sameCode && !isMirror;
                return new TestResult
                {
                    TestName = "真实几何:退化立方体不崩且非镜像",
                    Passed = passed,
                    Detail = passed ? "OK"
                        : string.Format(CultureInfo.InvariantCulture,
                            "Degen=({0},{1}) SameCode={2} Mirror={3}",
                            f1.IsDegenerate, f2.IsDegenerate, sameCode, isMirror)
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "真实几何:退化立方体不崩且非镜像",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        /// <summary>
        /// 场景 4：近对称但非镜像 — 两个 Brep 外形相似、镜像码相反，
        /// 但几何细节不同（孔位/孔径差异），应不被误判为镜像。
        /// 此项验证误合并风险控制。
        /// </summary>
        private static TestResult TestRealGeometryNearSymmetricNotMirror(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            try
            {
                // Brep A：10×8×6 盒子，孔在 (+2, 0)，半径 1.0
                Brep brepA = CreateAsymmetricTestBrep();

                // Brep B：10×8×6 盒子，孔在 (-1.5, 0)，半径 1.2
                // 外形与 A 相似但孔位置/半径不同，不是 A 的镜像（镜像应在 -2, r=1.0）
                var boxB = new Box(Plane.WorldXY,
                    new Interval(-5, 5), new Interval(-4, 4), new Interval(-3, 3));
                Brep boxBrepB = boxB.ToBrep();
                if (boxBrepB == null) throw new InvalidOperationException("盒体 B 创建失败。");

                var cylB = new Cylinder(
                    new Circle(new Plane(new Point3d(-1.5, 0, -5), Vector3d.ZAxis), 1.2), 10.0);
                Brep cylBrepB = cylB.ToBrep(true, true);
                if (cylBrepB == null) throw new InvalidOperationException("圆柱 B 创建失败。");

                Brep[] diffB = Brep.CreateBooleanDifference(
                    new List<Brep> { boxBrepB },
                    new List<Brep> { cylBrepB }, 0.001);
                if (diffB == null || diffB.Length == 0)
                    throw new InvalidOperationException("近对称件 B 的 BooleanDifference 失败。");
                Brep brepB = diffB[0];

                var decimals = new Dictionary<V5Criterion, int>
                {
                    { V5Criterion.Length, 4 }, { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 },
                    { V5Criterion.Inertia, 2 }, { V5Criterion.EdgeSum, 4 },
                    { V5Criterion.HoleArea, 2 }, { V5Criterion.HolePosition, 2 }
                };

                WbFeature fA = WbFeature.Create("steel", "red", brepA, decimals, 0.9986, 0.001, 0);
                WbFeature fB = WbFeature.Create("steel", "red", brepB, decimals, 0.9986, 0.001, 1);

                bool sameCode = SameMirrorCode(fA, fB, mirrorTol);
                bool isMirror = MirrorMatches(fA, fB, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
                bool isMatch = Matches(fA, fB, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);

                // 近对称但非镜像：外观相似但不应被判为镜像对或同件
                bool passed = !isMirror && !isMatch;
                return new TestResult
                {
                    TestName = "真实几何:近对称非镜像不误判",
                    Passed = passed,
                    Detail = passed ? "OK"
                        : string.Format(CultureInfo.InvariantCulture,
                            "SameCode={0} Mirror={1} Match={2}",
                            sameCode, isMirror, isMatch)
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "真实几何:近对称非镜像不误判",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        // ================================================================
        // 样本集统计回归测试 — 多对正/负样本批量验证误合并率与漏检率
        // ================================================================

        /// <summary>几何样本定义：两个 Brep 和预期关系。</summary>
        private sealed class GeometrySample
        {
            public string Name;
            public Brep BrepA;
            public Brep BrepB;
            /// <summary>预期 SameMirrorCode</summary>
            public bool ExpectedSameCode;
            /// <summary>预期 MirrorMatches</summary>
            public bool ExpectedMirror;
        }

        /// <summary>
        /// 构建正、负样本集（3 正 6 负）。
        /// 正样本 = 真实镜像对，应 hit；负样本 = 非镜像/非对称，不应误判。
        /// </summary>
        private static List<GeometrySample> BuildGeometrySamples()
        {
            var samples = new List<GeometrySample>();

            // === 正样本：真实镜像对 ===

            // P1：标准非对称件 → 镜像
            Brep asym = CreateAsymmetricTestBrep();         // 10×8×6, 孔(+2,0,r=1)
            Brep asymMirror = CreateMirrorBrep(asym);       // 孔(-2,0,r=1)
            samples.Add(new GeometrySample
            {
                Name = "正#1:10×8×6单孔偏移",
                BrepA = asym, BrepB = asymMirror,
                ExpectedSameCode = false, ExpectedMirror = true
            });

            // P2：宽盒双孔 → 镜像（多孔场景）
            Brep dualHole = CreateBoxWithHole(10, 5, 4,
                new Point3d(3, 0, -5), 1.0, 10.0);
            if (dualHole != null)
            {
                // 补第二个孔：在已打孔的 brep 上再减一个圆柱
                var cyl2 = new Cylinder(
                    new Circle(new Plane(new Point3d(3, 3, -5), Vector3d.ZAxis), 0.8), 10.0);
                Brep cylBrep2 = cyl2.ToBrep(true, true);
                Brep[] dual2 = Brep.CreateBooleanDifference(
                    new List<Brep> { dualHole }, new List<Brep> { cylBrep2 }, 0.001);
                if (dual2 != null && dual2.Length > 0)
                {
                    Brep dualHoleMirror = CreateMirrorBrep(dual2[0]);
                    samples.Add(new GeometrySample
                    {
                        Name = "正#2:20×10×8双孔偏移→镜像",
                        BrepA = dual2[0], BrepB = dualHoleMirror,
                        ExpectedSameCode = false, ExpectedMirror = true
                    });
                }
            }

            // P3：扁盒（不同纵横比）→ 镜像
            Brep flatBox = CreateBoxWithHole(6, 2, 6,
                new Point3d(1.5, 0, -7), 0.8, 14.0);
            Brep flatBoxMirror = CreateMirrorBrep(flatBox);
            samples.Add(new GeometrySample
            {
                Name = "正#3:12×4×12扁盒单孔→镜像",
                BrepA = flatBox, BrepB = flatBoxMirror,
                ExpectedSameCode = false, ExpectedMirror = true
            });

            // === 负样本：不应被判为镜像 ===

            // N1：近对称（孔位不同、孔径不同）
            Brep negNear = CreateBoxWithHole(5, 4, 3,
                new Point3d(-1.5, 0, -5), 1.2, 10.0);
            samples.Add(new GeometrySample
            {
                Name = "负#1:近对称不同孔位/孔径",
                BrepA = asym, BrepB = negNear,
                ExpectedSameCode = false, ExpectedMirror = false
            });

            // N2：退化立方体自身（三轴惯量相等）
            var cube = new Box(Plane.WorldXY,
                new Interval(-0.5, 0.5), new Interval(-0.5, 0.5), new Interval(-0.5, 0.5))
                .ToBrep();
            samples.Add(new GeometrySample
            {
                Name = "负#2:退化立方体自身",
                BrepA = cube, BrepB = cube,
                ExpectedSameCode = true, ExpectedMirror = false
            });

            // N3：非对称件自身
            samples.Add(new GeometrySample
            {
                Name = "负#3:非对称件自身",
                BrepA = asym, BrepB = asym,
                ExpectedSameCode = true, ExpectedMirror = false
            });

            // N4：不同尺寸盒体（10×8×6 vs 8×6×4），完全不同的零件
            Brep smallerBox = CreateBoxWithHole(4, 3, 2,
                new Point3d(1, 0, -4), 0.6, 8.0);
            samples.Add(new GeometrySample
            {
                Name = "负#4:不同尺寸(10×8×6 vs 8×6×4)",
                BrepA = asym, BrepB = smallerBox,
                ExpectedSameCode = false, ExpectedMirror = false
            });

            // N5：等比缩放（同形不同尺 → isometric 相似但不为镜像）
            Brep scaledBox = CreateBoxWithHole(10, 8, 6,
                new Point3d(4, 0, -10), 2.0, 20.0);
            samples.Add(new GeometrySample
            {
                Name = "负#5:等比2×缩放(同形不同尺)",
                BrepA = asym, BrepB = scaledBox,
                ExpectedSameCode = false, ExpectedMirror = false
            });

            // N6：同盒体、同孔位、不同孔径（r=1 vs r=1.5）
            Brep largerHole = CreateBoxWithHole(5, 4, 3,
                new Point3d(2, 0, -5), 1.5, 10.0);
            samples.Add(new GeometrySample
            {
                Name = "负#6:同盒同孔位不同孔径(r=1 vs 1.5)",
                BrepA = asym, BrepB = largerHole,
                ExpectedSameCode = false, ExpectedMirror = false
            });

            return samples;
        }

        /// <summary>场景 5：批量样本集统计 — 计算误合并率（负→正）与漏检率（正→负）。</summary>
        private static TestResult TestRealGeometryBatchStatistics(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            try
            {
                var decimals = new Dictionary<V5Criterion, int>
                {
                    { V5Criterion.Length, 4 }, { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 },
                    { V5Criterion.Inertia, 2 }, { V5Criterion.EdgeSum, 4 },
                    { V5Criterion.HoleArea, 2 }, { V5Criterion.HolePosition, 2 }
                };

                List<GeometrySample> samples = BuildGeometrySamples();

                int positiveTotal = 0, positiveHit = 0;   // 正样本 = 应判镜像
                int negativeTotal = 0, negativeFalse = 0; // 负样本 = 不应判镜像
                bool sameCodeAllOk = true;
                var details = new List<string>();

                foreach (var s in samples)
                {
                    WbFeature fA = WbFeature.Create("steel", "red", s.BrepA, decimals, 0.9986, 0.001, 0);
                    WbFeature fB = WbFeature.Create("steel", "red", s.BrepB, decimals, 0.9986, 0.001, 1);

                    bool sameCode = SameMirrorCode(fA, fB, mirrorTol);
                    bool isMirror = MirrorMatches(fA, fB, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);

                    bool sameCodeOk = sameCode == s.ExpectedSameCode;
                    bool mirrorOk = isMirror == s.ExpectedMirror;

                    if (!sameCodeOk) sameCodeAllOk = false;

                    if (s.ExpectedMirror)
                    {
                        positiveTotal++;
                        if (isMirror) positiveHit++;
                    }
                    else
                    {
                        negativeTotal++;
                        if (isMirror) negativeFalse++;
                    }

                    details.Add(s.Name + ":" + (mirrorOk && sameCodeOk ? "OK" : "FAIL") +
                        " SameCode=" + sameCode + "(exp " + s.ExpectedSameCode + ")" +
                        " Mirror=" + isMirror + "(exp " + s.ExpectedMirror + ")");
                }

                bool allPassed = sameCodeAllOk && positiveHit == positiveTotal && negativeFalse == 0;
                double recall = positiveTotal > 0 ? 100.0 * positiveHit / positiveTotal : 100.0;
                double falsePosRate = negativeTotal > 0 ? 100.0 * negativeFalse / negativeTotal : 0;

                return new TestResult
                {
                    TestName = string.Format(CultureInfo.InvariantCulture,
                        "真实几何:样本集统计({0}正{1}负)", positiveTotal, negativeTotal),
                    Passed = allPassed,
                    Detail = (allPassed ? "OK" : "有失败")
                        + (sameCodeAllOk ? "" : " [SameCode预期有误]")
                        + string.Format(CultureInfo.InvariantCulture,
                            " | 正样本命中 {0}/{1} ({2:F0}%), 负样本误判 {3}/{4} ({5:F0}%)",
                            positiveHit, positiveTotal, recall,
                            negativeFalse, negativeTotal, falsePosRate)
                        + (allPassed ? "" : " | " + string.Join("; ", details.Where(x => x.Contains("FAIL"))))
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "真实几何:样本集统计验证",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        // ================================================================
        // 检查点解析回归测试（WbMigrationCheckpoint）
        // ================================================================
        internal static class WbCheckpointTest
        {
            public static List<TestResult> RunAll()
            {
                var results = new List<TestResult>();
                results.Add(TestAllEmpty());
                results.Add(TestOnlyFailedFilesEmpty());
                results.Add(TestZeroRecordsZeroDiagnostics());
                results.Add(TestDiagnosticsWithSpecials());
                results.Add(TestDegenerateHoleRoundTrip());
                results.Add(TestDegenerateNonDefaultThreshold());
                results.Add(TestOldV9FormatRejected());
                results.Add(TestCorruptedCheckpoint());
                results.Add(TestFileListSameFilesDifferentOrder());
                return results;
            }

            /// <summary>
            /// 场景1：FailedFiles、CollectedRecords、Diagnostics 全部为空
            /// 验证空字段不导致字段位偏移
            /// </summary>
            private static TestResult TestAllEmpty()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = "",
                        PendingFiles = new List<string> { "a.3dm", "b.3dm" },
                        FileIndex = 1,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>()
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.FileIndex == 1
                        && loaded.FailedFiles.Count == 0
                        && loaded.CollectedRecords.Count == 0
                        && loaded.Diagnostics.Count == 0
                        && loaded.PendingFiles.Count == 2
                        && loaded.TotalErrorCount == 0;
                    return new TestResult
                    {
                        TestName = "检查点:全空字段无位偏移",
                        Passed = passed,
                        Detail = passed ? "OK" : "FIdx=" + (loaded?.FileIndex ?? -1)
                            + " FF=" + (loaded?.FailedFiles?.Count ?? -1)
                            + " R=" + (loaded?.CollectedRecords?.Count ?? -1)
                            + " D=" + (loaded?.Diagnostics?.Count ?? -1)
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:全空字段无位偏移",
                        Passed = false,
                        Detail = ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景2：仅 FailedFiles 为空，但已有正常记录
            /// 验证 FailedFiles 空行不导致记录计数错位
            /// </summary>
            private static TestResult TestOnlyFailedFilesEmpty()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "f1.3dm", "f2.3dm", "f3.3dm" },
                        FileIndex = 2,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(), // 空！
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>
                        {
                            new WbMigrationCheckpoint.CheckpointRecord { Index = 0, Number = "N001", SourceFile = "f1.3dm", FeatureText = "FEAT_A" },
                            new WbMigrationCheckpoint.CheckpointRecord { Index = 1, Number = "N002", SourceFile = "f1.3dm", FeatureText = "FEAT_B" }
                        },
                        Diagnostics = new List<string> { "diag1" }
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.FailedFiles.Count == 0
                        && loaded.CollectedRecords.Count == 2
                        && loaded.CollectedRecords[0].Number == "N001"
                        && loaded.CollectedRecords[1].Number == "N002"
                        && loaded.Diagnostics.Count == 1
                        && loaded.Diagnostics[0] == "diag1";
                    return new TestResult
                    {
                        TestName = "检查点:仅FailedFiles空+有记录",
                        Passed = passed,
                        Detail = passed ? "OK" : "FF=" + (loaded?.FailedFiles?.Count ?? -1)
                            + " R=" + (loaded?.CollectedRecords?.Count ?? -1)
                            + " D=" + (loaded?.Diagnostics?.Count ?? -1)
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:仅FailedFiles空+有记录",
                        Passed = false,
                        Detail = ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景3：记录数为 0、诊断数为 0（有 FailedFiles）
            /// 验证零计数边界正确解析为无数据块
            /// </summary>
            private static TestResult TestZeroRecordsZeroDiagnostics()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "only.3dm" },
                        FileIndex = 0,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string> { "bad.3dm" },
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>()
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.FailedFiles.Count == 1
                        && loaded.FailedFiles[0] == "bad.3dm"
                        && loaded.CollectedRecords.Count == 0
                        && loaded.Diagnostics.Count == 0;
                    return new TestResult
                    {
                        TestName = "检查点:记录0诊断0（有FailedFiles）",
                        Passed = passed,
                        Detail = passed ? "OK" : "FF=" + (loaded?.FailedFiles?.Count ?? -1)
                            + " R=" + (loaded?.CollectedRecords?.Count ?? -1)
                            + " D=" + (loaded?.Diagnostics?.Count ?? -1)
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:记录0诊断0（有FailedFiles）",
                        Passed = false,
                        Detail = ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景4：诊断文本含换行符和反斜杠
            /// 验证 Escape/Unescape round-trip 不丢失/不破坏内容
            /// </summary>
            private static TestResult TestDiagnosticsWithSpecials()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "a.3dm" },
                        FileIndex = 0,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>
                        {
                            "line1\nline2",
                            "path C:\\data\\files",
                            "mixed\nslash\\and\nnewline",
                            "全中文诊断：包含\\n和\\r转义测试"
                        }
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.Diagnostics.Count == 4
                        && loaded.Diagnostics[0] == "line1\nline2"
                        && loaded.Diagnostics[1] == "path C:\\data\\files"
                        && loaded.Diagnostics[2] == "mixed\nslash\\and\nnewline"
                        && loaded.Diagnostics[3] == "全中文诊断：包含\\n和\\r转义测试";
                    return new TestResult
                    {
                        TestName = "检查点:诊断含换行和反斜杠",
                        Passed = passed,
                        Detail = passed ? "OK"
                            : "D0=" + (loaded?.Diagnostics?.ElementAtOrDefault(0) ?? "(null)")
                            + " D1=" + (loaded?.Diagnostics?.ElementAtOrDefault(1) ?? "(null)")
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:诊断含换行和反斜杠",
                        Passed = false,
                        Detail = ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景5：退化孔特征往返 —— 含退化孔的 WbFeature 经检查点保存/加载后
            /// 验证 WbFeature.IsDegenerate、HasDegenerateHoles 及孔退化辅助字段不丢失
            /// </summary>
            private static TestResult TestDegenerateHoleRoundTrip()
            {
                string tmp = null;
                try
                {
                    // 构造含退化孔的特征（惯量退化 + 孔退化）
                    var feature = new WbFeature
                    {
                        Material = "Steel", Color = "Red",
                        BoundingBox = new[] { 100.0, 50.0, 30.0 },
                        Area = 1000, Volume = 500,
                        Inertia = new[] { 2000.0, 1990.0, 500.0 },  // 惯量退化：1990/2000 > 0.99
                        MirrorScore = 0.5, HoleMirrorScore = 0.3,
                        IsDegenerate = true
                    };
                    // 正常孔
                    feature.Holes.Add(new WbHole(10, 5, 20, 10, 0, 0, 0, 1));
                    // 退化孔：各辅助字段填非零值
                    var degHole = new WbHole(8, 2, 30, 15, 0, 0, 0, 0,
                        isDegenerate: true, degenerateType: "非平面内环");
                    degHole.DegenerateProjectedArea = 3.14;
                    degHole.DegenerateCircularity = 0.85;
                    degHole.DegenerateAspectRatio = 1.5;
                    degHole.DegenerateNormalU = 0.577;
                    degHole.DegenerateNormalV = 0.577;
                    degHole.DegenerateNormalW = 0.577;
                    feature.Holes.Add(degHole);

                    // 序列化 → 保存检查点 → 加载 → 反序列化
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "a.3dm" },
                        FileIndex = 0, TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>
                        {
                            new WbMigrationCheckpoint.CheckpointRecord
                            {
                                Index = 0, Number = "001",
                                SourceFile = "test.3dm",
                                FeatureText = feature.Serialize()
                            }
                        },
                        Diagnostics = new List<string>()
                    };
                    tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    if (loaded == null || loaded.CollectedRecords.Count != 1)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "检查点加载失败或记录数为 " + (loaded?.CollectedRecords?.Count ?? -1)
                        };

                    var restored = WbFeature.Deserialize(loaded.CollectedRecords[0].FeatureText);

                    // 验证 feature 级退化标记
                    if (!restored.IsDegenerate)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "WbFeature.IsDegenerate 未恢复（应为 true）"
                        };
                    if (!restored.HasDegenerateHoles)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "HasDegenerateHoles 未恢复（应为 true）"
                        };
                    if (restored.Holes.Count != 2)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "孔数不匹配：" + restored.Holes.Count + "（应为 2）"
                        };

                    // 验证退化孔各字段
                    var rh = restored.Holes[1];  // 第二个孔是退化孔
                    if (!rh.IsDegenerate)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "WbHole.IsDegenerate 未恢复"
                        };
                    if (rh.DegenerateType != "非平面内环")
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "DegenerateType 不匹配：" + rh.DegenerateType
                        };
                    double eps = 1e-12;
                    if (Math.Abs(rh.DegenerateProjectedArea - 3.14) > eps)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "DegenerateProjectedArea 不匹配：" + rh.DegenerateProjectedArea
                        };
                    if (Math.Abs(rh.DegenerateCircularity - 0.85) > eps)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "DegenerateCircularity 不匹配：" + rh.DegenerateCircularity
                        };
                    if (Math.Abs(rh.DegenerateAspectRatio - 1.5) > eps)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "DegenerateAspectRatio 不匹配：" + rh.DegenerateAspectRatio
                        };
                    if (Math.Abs(rh.DegenerateNormalU - 0.577) > 1e-6
                        || Math.Abs(rh.DegenerateNormalV - 0.577) > 1e-6
                        || Math.Abs(rh.DegenerateNormalW - 0.577) > 1e-6)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "拟合法向不匹配：("
                                + rh.DegenerateNormalU + "," + rh.DegenerateNormalV + "," + rh.DegenerateNormalW + ")"
                        };

                    return new TestResult
                    {
                        TestName = "检查点:退化孔特征往返",
                        Passed = true,
                        Detail = "OK — 惯量退化标记=" + restored.IsDegenerate
                            + " 孔退化标记=" + rh.IsDegenerate
                            + " 类型=" + rh.DegenerateType
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:退化孔特征往返",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
                finally
                {
                    if (tmp != null) CleanTemp(tmp);
                }
            }

            /// <summary>
            /// 场景5b：非默认惯量退化阈值往返 —— 验证 IsDegenerate 使用序列化值
            /// 而非 Deserialize 中的固定 0.99 重算。惯量 1950/2000=0.975：
            /// 0.99 阈值下非退化，0.95 阈值下退化。确认序列化 true 不被 0.99 覆盖。
            /// </summary>
            private static TestResult TestDegenerateNonDefaultThreshold()
            {
                try
                {
                    // 惯量比 0.975：0.99 阈值非退化，0.95 阈值退化
                    var feature = new WbFeature
                    {
                        Material = "Al", Color = "",
                        BoundingBox = new[] { 80.0, 40.0, 20.0 },
                        Area = 500, Volume = 200,
                        Inertia = new[] { 2000.0, 1950.0, 500.0 },
                        MirrorScore = 0.0, HoleMirrorScore = 0.0,
                        IsDegenerate = true   // 模拟 _inertiaDegenerateRatio=0.95 的计算结果
                    };
                    feature.Holes.Add(new WbHole(5, 2, 10, 5, 0, 0, 0, 1));

                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "x.3dm" },
                        FileIndex = 0, TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>
                        {
                            new WbMigrationCheckpoint.CheckpointRecord
                            {
                                Index = 0, Number = "001",
                                SourceFile = "x.3dm",
                                FeatureText = feature.Serialize()
                            }
                        },
                        Diagnostics = new List<string>()
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    if (loaded == null || loaded.CollectedRecords.Count != 1)
                        return new TestResult
                        {
                            TestName = "检查点:非默认阈值IsDegenerate往返",
                            Passed = false,
                            Detail = "检查点加载失败"
                        };
                    var restored = WbFeature.Deserialize(loaded.CollectedRecords[0].FeatureText);
                    // 核心断言：序列化的 true 优先于 0.99 重算出的 false
                    if (!restored.IsDegenerate)
                        return new TestResult
                        {
                            TestName = "检查点:非默认阈值IsDegenerate往返",
                            Passed = false,
                            Detail = "IsDegenerate 被 0.99 重算覆盖为 false（应为序列化值 true）"
                        };
                    return new TestResult
                    {
                        TestName = "检查点:非默认阈值IsDegenerate往返",
                        Passed = true,
                        Detail = "OK — 非默认阈值标记 true 未被 0.99 兜底覆盖"
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:非默认阈值IsDegenerate往返",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景5c：旧版 V9 格式（13字段，缺少 IsDegenerate）拒绝恢复
            /// 反序列化必须抛出 InvalidDataException，消息包含"重新提取"
            /// </summary>
            private static TestResult TestOldV9FormatRejected()
            {
                // 手动构造旧版 13 字段 V9 特征串（缺少 IsDegenerate）
                string material = "Al";
                string color = "Red";
                double[] bbox = { 100.0, 50.0, 30.0 };
                string oldLine = "ENTITY_FEATURE_V9;" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(material)) + ";" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(color)) + ";" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        string.Join(",", bbox.Select(v => WbUtil.FR(v))))) + ";" +
                    WbUtil.FR(500.0) + ";" +    // Area
                    WbUtil.FR(200.0) + ";" +    // Volume
                    WbUtil.FR(2000.0) + ";" +   // Inertia[0]
                    WbUtil.FR(1990.0) + ";" +   // Inertia[1]
                    WbUtil.FR(500.0) + ";" +    // Inertia[2]
                    "" + ";" +                   // holes (none)
                    WbUtil.FR(0.5) + ";" +       // MirrorScore
                    WbUtil.FR(0.3) + ";" +       // HoleMirrorScore
                    "";                          // profiles (none)
                // 共 13 个分号分隔段

                try
                {
                    WbFeature.Deserialize(oldLine);
                    return new TestResult
                    {
                        TestName = "检查点:旧V9格式拒绝恢复",
                        Passed = false,
                        Detail = "应抛 InvalidDataException 但未抛异常"
                    };
                }
                catch (InvalidDataException ex)
                {
                    if (!ex.Message.Contains("重新提取"))
                        return new TestResult
                        {
                            TestName = "检查点:旧V9格式拒绝恢复",
                            Passed = false,
                            Detail = "异常消息缺少'重新提取'：\"" + ex.Message + "\""
                        };
                    return new TestResult
                    {
                        TestName = "检查点:旧V9格式拒绝恢复",
                        Passed = true,
                        Detail = "OK — 旧格式被正确拒绝：\"" + ex.Message + "\""
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:旧V9格式拒绝恢复",
                        Passed = false,
                        Detail = "异常类型不符合预期（应为 InvalidDataException）：" +
                            ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景6：损坏检查点文件 —— 应抛出 InvalidDataException（非静默 null）
            /// 验证不静默失败且有明确诊断信息
            /// </summary>
            private static TestResult TestCorruptedCheckpoint()
            {
                string tmp = null;
                try
                {
                    tmp = Path.GetTempFileName();
                    // 写入只有2行的损坏数据（最少需要7行）
                    File.WriteAllText(tmp, "garbage\nline2", Encoding.UTF8);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    // 如果没抛异常，说明静默返回了 — 这是失败
                    return new TestResult
                    {
                        TestName = "检查点:损坏文件抛异常不静默",
                        Passed = false,
                        Detail = "应抛异常但未抛异常；loaded=" + (loaded != null)
                    };
                }
                catch (InvalidDataException)
                {
                    return new TestResult
                    {
                        TestName = "检查点:损坏文件抛异常不静默",
                        Passed = true,
                        Detail = "OK — 正确抛出 InvalidDataException"
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:损坏文件抛异常不静默",
                        Passed = false,
                        Detail = "异常类型不符合预期：" + ex.GetType().Name + ": " + ex.Message
                    };
                }
                finally
                {
                    if (tmp != null) CleanTemp(tmp);
                }
            }

            /// <summary>
            /// 场景7：同文件集合、不同顺序必须拒绝恢复。
            /// FileIndex 依赖原列表顺序；重排同一批文件时仍恢复旧顺序会指向错误文件。
            /// </summary>
            private static TestResult TestFileListSameFilesDifferentOrder()
            {
                try
                {
                    string basePath = @"C:\test";
                    // 构造检查点：顺序 A, B, C
                    var cpFiles = new List<string>
                    {
                        Path.Combine(basePath, "a.3dm"),
                        Path.Combine(basePath, "b.3dm"),
                        Path.Combine(basePath, "c.3dm")
                    };
                    // 当前输入：同一批文件，但顺序 B, A, C
                    var curFiles = new List<string>
                    {
                        Path.Combine(basePath, "b.3dm"),
                        Path.Combine(basePath, "a.3dm"),
                        Path.Combine(basePath, "c.3dm")
                    };

                    bool passed = WbClassificationMigration.ValidateFileListConsistency(cpFiles, curFiles, out string reason);
                    if (passed)
                        return new TestResult
                        {
                            TestName = "检查点:同文件不同顺序拒绝恢复",
                            Passed = false,
                            Detail = "应拒绝但通过了——集合相同、顺序不同时 FileIndex 不可靠"
                        };
                    if (!reason.Contains("顺序不一致"))
                        return new TestResult
                        {
                            TestName = "检查点:同文件不同顺序拒绝恢复",
                            Passed = false,
                            Detail = "拒绝原因应为'顺序不一致'，实际：" + reason
                        };
                    return new TestResult
                    {
                        TestName = "检查点:同文件不同顺序拒绝恢复",
                        Passed = true,
                        Detail = "OK — 正确拒绝：\"" + reason + "\""
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:同文件不同顺序拒绝恢复",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            private static string WriteTemp(WbMigrationCheckpoint cp)
            {
                string path = Path.GetTempFileName();
                cp.Save(path);
                return path;
            }

            private static void CleanTemp(string path)
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch { /* 尽力清理 */ }
            }
        }

        // ====================================================================
        // 退化匹配测试：验证 enableDegenerateMatching 开/关行为
        // ====================================================================
        internal static class WbDegenerateMatchingTest
        {
            public class TestResult
            {
                public string TestName;
                public bool Passed;
                public string Detail;
            }

            public static List<TestResult> RunAll()
            {
                var results = new List<TestResult>();
                results.Add(TestOffIsolation());
                results.Add(TestOnMatching());
                results.Add(TestChainNotClique());
                return results;
            }

            public static void RunAllAndLog()
            {
                var results = RunAll();
                foreach (var r in results)
                {
                    string tag = r.Passed ? "[PASS] " : "[FAIL] ";
                    RhinoApp.WriteLine(tag + r.TestName + " — " + r.Detail);
                }
                int pass = results.Count(r => r.Passed);
                int fail = results.Count(r => !r.Passed);
                RhinoApp.WriteLine("退化匹配测试结果：" + pass.ToString() + " 通过，" +
                    fail.ToString() + " 失败（共 " + results.Count.ToString() + "）。");
            }

            /// <summary>
            /// 场景 D1：关闭退化匹配 → 退化孔记录各自隔离成单类
            /// </summary>
            private static TestResult TestOffIsolation()
            {
                // 构造两个相似的退化特征
                var holesA = new List<WbHole> { CreateDegenerateHole(10, 1, 1, 0, 0) };
                var holesB = new List<WbHole> { CreateDegenerateHole(10, 1, 1, 0, 0) };

                var fA = CreateDegenerateFeature("A", holesA);
                var fB = CreateDegenerateFeature("B", holesB);

                var records = new List<WbMigrationRecord>
                {
                    new WbMigrationRecord(0, "P1", fA, "test"),
                    new WbMigrationRecord(1, "P2", fB, "test")
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                var audit = WbMigrationAudit.Create(records, enabled, tolerances, isRel,
                    mirrorTolerance: 1e-8, strictMaterialColor: false, holeNormalTolerance: 0.99,
                    enableDegenerateMatching: false);

                // 关闭：每个退化记录各成一类 → Classes 应有 2 个
                bool isolated = audit.Classes.Count == 2;
                return new TestResult
                {
                    TestName = "退化匹配:关闭→隔离",
                    Passed = isolated,
                    Detail = isolated
                        ? "OK — 2 退化记录各成一类（Classes=" +
                          audit.Classes.Count.ToString(CultureInfo.InvariantCulture) + "）"
                        : "期望 2 类但实际为 " +
                          audit.Classes.Count.ToString(CultureInfo.InvariantCulture) +
                          "（部分记录被合并）"
                };
            }

            /// <summary>
            /// 场景 D2：开启退化匹配 → 相似的退化记录可匹配成组
            /// </summary>
            private static TestResult TestOnMatching()
            {
                // 构造两个几乎相同的退化特征（在同一位置，仅颜色不同但宽松模式忽略）
                var holesA = new List<WbHole> { CreateDegenerateHole(10, 1, 1, 0, 0) };
                var holesB = new List<WbHole> { CreateDegenerateHole(10, 1, 1, 0, 0) };

                var fA = CreateDegenerateFeature("A", holesA);
                var fB = CreateDegenerateFeature("B", holesB);

                var records = new List<WbMigrationRecord>
                {
                    new WbMigrationRecord(0, "P1", fA, "test"),
                    new WbMigrationRecord(1, "P2", fB, "test")
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                var audit = WbMigrationAudit.Create(records, enabled, tolerances, isRel,
                    mirrorTolerance: 1e-8, strictMaterialColor: false, holeNormalTolerance: 0.99,
                    enableDegenerateMatching: true, degeneratePositionMultiplier: 2.0);

                // 开启：两条完全一致的退化记录应匹配成同组 → Classes 应有 1 个
                bool matched = audit.Classes.Count == 1;
                return new TestResult
                {
                    TestName = "退化匹配:开启→匹配",
                    Passed = matched,
                    Detail = matched
                        ? "OK — 2 条退化记录匹配为 1 类"
                        : "期望 1 类但实际为 " +
                          audit.Classes.Count.ToString(CultureInfo.InvariantCulture) +
                          "（退化记录未成功匹配）"
                };
            }

            /// <summary>
            /// 场景 D3（链式反例）：A↔B、B↔C 但 A≠C。
            /// HolePosition 有效容差 10.0（5.0 × degenMult=2），
            /// 三孔分别位于 (10,1,1) (4,1,1) (0,1,1)：
            ///   A↔B 距离 6 → 匹配成功
            ///   B↔C 距离 4 → 匹配成功
            ///   A↔C 距离 10 ≥ 10 → 匹配失败
            /// 若缺少 clique 检测，BFS 连通分量 {A,B,C} 会被直接合并成一类；
            /// 正确行为：{A,B,C} 非团 → 归入 AmbiguityGroups，不被自动合并。
            /// </summary>
            private static TestResult TestChainNotClique()
            {
                // 三孔沿 X 轴等距排列，间距 6 和 4，使两端孔间距=10（≥容差）
                var holesA = new List<WbHole> { CreateDegenerateHole(10, 10, 1, 1, 0.5) };
                var holesB = new List<WbHole> { CreateDegenerateHole(10,  4, 1, 1, 0.5) };
                var holesC = new List<WbHole> { CreateDegenerateHole(10,  0, 1, 1, 0.5) };

                var fA = CreateDegenerateFeature("A", holesA);
                var fB = CreateDegenerateFeature("B", holesB);
                var fC = CreateDegenerateFeature("C", holesC);

                var records = new List<WbMigrationRecord>
                {
                    new WbMigrationRecord(0, "P1", fA, "test"),
                    new WbMigrationRecord(1, "P2", fB, "test"),
                    new WbMigrationRecord(2, "P3", fC, "test")
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                var audit = WbMigrationAudit.Create(records, enabled, tolerances, isRel,
                    mirrorTolerance: 1e-8, strictMaterialColor: false, holeNormalTolerance: 0.99,
                    enableDegenerateMatching: true, degeneratePositionMultiplier: 2.0);

                // 正确行为：3 条记录不应被合并为 1 类
                bool noSingleClassWithAll = !audit.Classes.Any(
                    c => c.Count == 3);

                // 应出现在歧义组中
                bool hasAmbiguity = audit.AmbiguityGroups.Count > 0;
                int ambiguityRecordCount = audit.AmbiguityGroups.Sum(g => g.Count);

                bool passed = noSingleClassWithAll && hasAmbiguity && ambiguityRecordCount == 3;

                return new TestResult
                {
                    TestName = "退化匹配:链式A↔B↔C但A≠C→非团隔离",
                    Passed = passed,
                    Detail = passed
                        ? "OK — 未合并为单类，歧义组含 3 记录"
                        : string.Format(CultureInfo.InvariantCulture,
                            "Classes={0} AmbGroups={1} AmbRecs={2}",
                            audit.Classes.Count, audit.AmbiguityGroups.Count, ambiguityRecordCount)
                };
            }

            private static WbHole CreateDegenerateHole(
                double perimeter, double u, double v, double w,
                double degenerateArea = 0.5)
            {
                return new WbHole(perimeter, 0, u, v, w, 0, 0, 0,
                    isDegenerate: true, degenerateType: "Test")
                {
                    DegenerateProjectedArea = degenerateArea,
                    DegenerateCircularity = 0.8,
                    DegenerateAspectRatio = 1.5,
                    DegenerateNormalU = 0, DegenerateNormalV = 0, DegenerateNormalW = 1
                };
            }

            private static WbFeature CreateDegenerateFeature(
                string color, List<WbHole> holes)
            {
                return new WbFeature
                {
                    Material = "Steel",
                    Color = color,
                    BoundingBox = new[] { 100.0, 80.0, 60.0 },
                    Area = 5000,
                    Volume = 1000,
                    Inertia = new[] { 2000.0, 1990.0, 500.0 },
                    IsDegenerate = true,
                    Holes = holes,
                    MirrorScore = 0.5,
                    HoleMirrorScore = 0.3
                };
            }

            private static Dictionary<V5Criterion, bool> AllEnabled()
            {
                return new Dictionary<V5Criterion, bool>
                {
                    { V5Criterion.Length, true },
                    { V5Criterion.Area, true }, { V5Criterion.Volume, true },
                    { V5Criterion.Inertia, true }, { V5Criterion.EdgeSum, true },
                    { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, true }
                };
            }

            private static Dictionary<V5Criterion, double> DefaultTolerances()
            {
                return new Dictionary<V5Criterion, double>
                {
                    { V5Criterion.Length, 0.10 },
                    { V5Criterion.Area, 0.05 }, { V5Criterion.Volume, 0.05 },
                    { V5Criterion.Inertia, 0.05 }, { V5Criterion.EdgeSum, 0.10 },
                    { V5Criterion.HoleArea, 0.10 }, { V5Criterion.HolePosition, 5.0 }
                };
            }

            private static Dictionary<V5Criterion, bool> DefaultIsRel()
            {
                return new Dictionary<V5Criterion, bool>
                {
                    { V5Criterion.Length, true },
                    { V5Criterion.Area, true }, { V5Criterion.Volume, true },
                    { V5Criterion.Inertia, true }, { V5Criterion.EdgeSum, true },
                    { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, false }
                };
            }
        }

        private static string MirrorCode(WbFeature f, double tol)
        {
            double v = Math.Abs(f.HoleMirrorScore) > tol ? f.HoleMirrorScore : f.MirrorScore;
            return Math.Abs(v) <= tol ? string.Empty : (v >= 0 ? "A" : "B");
        }

        private static bool SameMirrorCode(WbFeature a, WbFeature b, double tol) =>
            string.Equals(MirrorCode(a, tol), MirrorCode(b, tol), StringComparison.Ordinal);

        private static bool Matches(WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, double mirrorTol, bool strictMat,
            double normalTol, double outerArea, double outerPerim)
        {
            return WbMigrationAudit.GeometryMatches(a, b, e, t, isRel, false, normalTol,
                outerArea, outerPerim) &&
                SameMirrorCode(a, b, mirrorTol);
        }

        private static bool MirrorMatches(WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, double mirrorTol, bool strictMat,
            double normalTol, double outerArea, double outerPerim)
        {
            return WbMigrationAudit.GeometryMatches(a, b, e, t, isRel, true, normalTol,
                outerArea, outerPerim) &&
                !SameMirrorCode(a, b, mirrorTol);
        }
    }

    // ========================================================================
    // 主电池：分类库迁移_WB
    // ========================================================================
    public sealed class WbClassificationMigration : GH_Component
    {
        private enum State { Idle, OpeningFile, AwaitingData }

        private const string SettingsChunk = "WbClassificationMigrationSettings";

        private readonly Dictionary<V5Criterion, int> _decimals = new Dictionary<V5Criterion, int>
        {
            { V5Criterion.Length, 2 }, { V5Criterion.Inertia, 2 },
            { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 },
            { V5Criterion.EdgeSum, 2 }, { V5Criterion.HoleArea, 2 },
            { V5Criterion.HolePosition, 4 }
        };

        // 注意：表面积(Area) 已从匹配中剔除，不参与校核
        // 容差语义：_isRelativeTolerance[c]=true → % 相对误差；false → 绝对误差
        // 孔位(HolePosition)固定为绝对误差，不可切换（见 IsAbsoluteOnlyCriterion 注释）
        private readonly Dictionary<V5Criterion, bool> _isRelativeTolerance = new Dictionary<V5Criterion, bool>
        {
            { V5Criterion.Length, true }, { V5Criterion.Inertia, true },
            { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true },
            { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, false }
        };

        private readonly Dictionary<V5Criterion, bool> _enabled = new Dictionary<V5Criterion, bool>
        {
            { V5Criterion.Length, true }, { V5Criterion.Inertia, true },
            { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true },
            { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, true }
        };

        private readonly Dictionary<V5Criterion, double> _tolerances = new Dictionary<V5Criterion, double>
        {
            { V5Criterion.Length, 0.5 }, { V5Criterion.Inertia, 0.2 },
            { V5Criterion.Volume, 0.5 }, { V5Criterion.EdgeSum, 1 },
            { V5Criterion.HoleArea, 1 }, { V5Criterion.HolePosition, 0.5 }
        };

        // 镜像区分容差：HoleMirrorScore/MirrorScore 绝对值 <= 此值时不区分 A/B
        private double _mirrorTolerance = 0.001;
        // 非交互/headless 模式下 RhinoDoc.ActiveDoc==null 时的回退容差
        private double _fallbackTolerance = 0.001;
        // 惯量退化判定阈值：相邻主惯量比值超过此值时标记为退化件
        private double _inertiaDegenerateRatio = 0.99;
        // 孔法向兼容阈值：|cos| 低于此值不通过法向比较，默认 0.9986 (约 3°)
        private double _holeNormalTolerance = 0.9986;
        // 材质/颜色严格模式：true=空值不通配（双方均为空才匹配）
        private bool _strictMaterialColor = true;
        // 外轮廓面积相对容差：|A-B|/max(A,B,1) <= ratio 时通过
        private double _outerProfileAreaRatio = 0.0001;
        // 外轮廓周长最小绝对容差：max(tolerance, min) 的 min 值
        private double _outerProfilePerimeterMin = 0.01;
        // 退化孔孔位容差倍数：退化孔匹配时几何容差乘以此倍数
        private double _degeneratePositionMultiplier = 2.0;
        // 退化孔匹配开关：开启后使用拟合平面特征参与低置信度匹配（默认关）
        private bool _enableDegenerateMatching;
        // 迁移状态
        private bool _runRequested;
        private bool _paused;
        private bool _lastRunInput;
        private State _state = State.Idle;
        private readonly List<string> _pendingFiles = new List<string>();
        private readonly List<WbMigrationRecord> _collectedRecords = new List<WbMigrationRecord>();
        private readonly List<string> _diagnostics = new List<string>();
        private int _fileIndex;
        private int _waitingAttempts;
        private int _failedFileCount;  // 迁移过程中失败跳过的文件数

        // 数据稳定性检测
        private string _lastDataSignature = string.Empty;
        private int _stableDataCount;
        private int _dataCheckCount;

        // 定时器
        private int _refreshToken;
        private Timer _refreshTimer;
        private Timer _fileOpenTimer;
        private Timer _finalCloseTimer;

        private string _statusText = string.Empty;
        private string _pendingDatabasePath = string.Empty;

        // ================================================================
        // 公开控制接口
        // ================================================================
        public void StartMigration()
        {
            CancelRefresh();
            StopFileOpenTimer();
            StopFinalCloseTimer();
            _paused = false;
            _state = State.Idle;
            _pendingFiles.Clear();
            _collectedRecords.Clear();
            _diagnostics.Clear();
            _fileIndex = 0;
            _waitingAttempts = 0;
            _failedFileCount = 0;
            _pendingDatabasePath = string.Empty;
            _runRequested = true;
            ScheduleRefresh(1500);
        }

        public void PauseMigration()
        {
            if (_state != State.AwaitingData) return;
            _paused = true;
            CancelRefresh();
        }

        public void ResumeMigration()
        {
            if (!_paused || _state != State.AwaitingData) return;
            _paused = false;
            ScheduleRefresh(1500);
        }

        public bool IsPaused => _paused;

        // ================================================================
        // GH 组件定义
        // ================================================================
        public WbClassificationMigration()
            : base("分类数据库迁移（WB）", "分类库迁移_WB",
                  "从 3dm 文件提取实体、计算稳定特征、校核并写入 V8 分类数据库。",
                  "Parrot", "Library") { }

        public override void CreateAttributes()
        {
            m_attributes = new WbClassificationMigrationAttributes(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("新版数据库地址", "数据库",
                "迁移专用 SQLite 数据库文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("旧编号", "旧编号",
                "与文件路径和实体 GUID 一一对应的完整旧编号。", GH_ParamAccess.list);
            p.AddTextParameter("材质", "材质",
                "与实体一一对应的材质，可为空。", GH_ParamAccess.list);
            p.AddTextParameter("颜色", "颜色",
                "与实体一一对应的颜色，可为空。", GH_ParamAccess.list);
            p.AddTextParameter("文件路径", "文件",
                "与实体 GUID 一一对应的 .3dm 文件路径。", GH_ParamAccess.list);
            p.AddGenericParameter("实体 GUID", "GUID",
                "文件中待迁移 Brep 实体的 GUID。", GH_ParamAccess.list);
            p.AddBooleanParameter("启动", "启动",
                "上升沿触发；清空进度并从第 0 个文件重新开始。",
                GH_ParamAccess.item, false);
            p.AddBooleanParameter("暂停", "暂停",
                "True 时暂停；恢复为 False 后从当前文件继续。",
                GH_ParamAccess.item, false);
            p[1].Optional = true; p[2].Optional = true; p[3].Optional = true;
            p[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("V8分类特征", "特征",
                "本次计算并写入迁移库的标准 V8 特征。", GH_ParamAccess.list);
            p.AddTextParameter("日志", "日志",
                "校核和迁移日志。", GH_ParamAccess.list);
            p.AddTextParameter("状态", "状态",
                "迁移状态。", GH_ParamAccess.item);
            p.AddBooleanParameter("重建完成", "完成",
                "成功重建数据库时为 True。", GH_ParamAccess.item);
            p.AddTextParameter("详细诊断", "诊断",
                "质心、主轴、逐孔坐标和匹配距离等诊断参数。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string database = string.Empty;
            var numbers = new List<string>();
            var materials = new List<string>();
            var colors = new List<string>();
            var files = new List<string>();
            var ids = new List<GH_Guid>();
            var logs = new List<string>();
            var featureTexts = new List<string>();
            bool saved = false;
            bool run = false, pause = false;

            da.GetData(0, ref database);
            da.GetDataList(4, files);
            da.GetData(6, ref run);
            da.GetData(7, ref pause);
            da.SetDataList(4, _diagnostics);

            // 上升沿 → 重置
            if (run && !_lastRunInput)
            {
                CancelRefresh();
                StopFileOpenTimer();
                StopFinalCloseTimer();
                _paused = false;
                _state = State.Idle;
                _pendingFiles.Clear();
                _collectedRecords.Clear();
                _diagnostics.Clear();
                _fileIndex = 0;
                _waitingAttempts = 0;
                _pendingDatabasePath = string.Empty;
                _runRequested = true;
            }
            _lastRunInput = run;

            // 暂停
            if (pause)
            {
                if (!_paused) { CancelRefresh(); StopFileOpenTimer(); }
                _paused = true;
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, new[] {
                    "已暂停：将\"暂停\"恢复为 False 后继续；再次触发\"启动\"则重新开始。" });
                da.SetData(2, "已暂停。");
                da.SetData(3, false);
                return;
            }

            bool wasPaused = _paused;
            _paused = false;
            if (wasPaused && _state == State.OpeningFile)
                ScheduleOpenCurrentFile();

            // 启动第一轮
            if (_runRequested)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(database))
                        throw new ArgumentException("数据库地址不能为空。");
                    if (files.Count == 0)
                        throw new ArgumentException("文件路径不能为空。");

                    // 尝试从检查点恢复
                    if (TryResumeCheckpoint(database, files))
                    {
                        _state = State.OpeningFile;
                        ResetDataStability();
                        _statusText = "从检查点恢复：打开第 " +
                            (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                            " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。";
                        ScheduleOpenCurrentFile();
                        _runRequested = false;
                        logs.Add("已从检查点恢复迁移，继续处理第 " +
                            (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                            " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。");
                        da.SetDataList(0, new List<string>());
                        da.SetDataList(1, logs);
                        da.SetData(2, logs[0]);
                        da.SetData(3, false);
                        da.SetDataList(4, _diagnostics);
                        return;
                    }

                    if (files.Any(string.IsNullOrWhiteSpace) ||
                        files.Any(pth => !File.Exists(pth)))
                        throw new FileNotFoundException("文件路径中包含不存在的文件。");

                    _pendingFiles.Clear();
                    _pendingFiles.AddRange(files.Select(Path.GetFullPath));
                    _pendingDatabasePath = database;
                    _collectedRecords.Clear();
                    _fileIndex = 0;
                    _waitingAttempts = 0;
                    _state = State.OpeningFile;
                    ResetDataStability();
                    _statusText = "准备打开第 1 / " +
                        _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。";
                    ScheduleOpenCurrentFile();
                    logs.Add("已启动迁移；本轮求解结束后打开第 1 / " +
                        _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。");
                }
                catch (Exception ex)
                {
                    logs.Add("错误：" + ex.Message);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    ResetMigration();
                }
                finally { _runRequested = false; }
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, logs);
                da.SetData(2, logs[0]);
                da.SetData(3, false);
                return;
            }

            // 正在打开文件
            if (_state == State.OpeningFile)
            {
                logs.Add(string.IsNullOrWhiteSpace(_statusText)
                    ? "等待在求解结束后打开当前文件。" : _statusText);
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, logs);
                da.SetData(2, logs[0]);
                da.SetData(3, false);
                return;
            }

            // 等待数据
            if (_state != State.AwaitingData)
            {
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, new[] {
                    string.IsNullOrWhiteSpace(_statusText)
                        ? "等待启动；触发\"启动\"后自动处理。"
                        : _statusText });
                da.SetData(2, string.IsNullOrWhiteSpace(_statusText)
                    ? "等待启动。" : _statusText);
                da.SetData(3, false);
                return;
            }

            // 文件已打开，收集上游数据
            da.GetDataList(1, numbers);
            da.GetDataList(2, materials);
            da.GetDataList(3, colors);
            da.GetDataList(5, ids);

            if (!IsDataStable(numbers, ids))
            {
                if (_dataCheckCount >= 20)
                {
                    logs.Add("跳过当前文件：旧编号和 GUID 未稳定；最后数量=" +
                        numbers.Count.ToString(CultureInfo.InvariantCulture) + "/" +
                        ids.Count.ToString(CultureInfo.InvariantCulture) + "。");
                    saved = AdvanceFile(logs);
                    da.SetDataList(0, featureTexts);
                    da.SetDataList(1, logs);
                    da.SetData(2, logs[0]);
                    da.SetData(3, saved);
                    return;
                }
                logs.Add("等待旧编号和 GUID 稳定：数量=" +
                    numbers.Count.ToString(CultureInfo.InvariantCulture) + "/" +
                    ids.Count.ToString(CultureInfo.InvariantCulture) +
                    "，稳定=" + _stableDataCount.ToString(CultureInfo.InvariantCulture) + "/3。");
                ScheduleRefresh(500);
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, logs);
                da.SetData(2, logs[0]);
                da.SetData(3, false);
                return;
            }

            if (numbers.Count > 1 || ids.Count > 1)
            {
                logs.Add("跳过当前文件：旧编号数量=" +
                    numbers.Count.ToString(CultureInfo.InvariantCulture) +
                    "，GUID 数量=" + ids.Count.ToString(CultureInfo.InvariantCulture) +
                    "；每个文件必须各有且只有 1 个。");
                saved = AdvanceFile(logs);
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, logs);
                da.SetData(2, logs[0]);
                da.SetData(3, saved);
                return;
            }

            if (numbers.Count == 0 || ids.Count == 0)
            {
                _waitingAttempts++;
                if (_waitingAttempts <= 50)
                {
                    logs.Add("等待自动生成旧编号和 GUID（" +
                        _waitingAttempts.ToString(CultureInfo.InvariantCulture) + " / 50）。");
                    ScheduleRefresh(1500);
                    da.SetDataList(0, featureTexts);
                    da.SetDataList(1, logs);
                    da.SetData(2, logs[0]);
                    da.SetData(3, false);
                    return;
                }
                logs.Add("跳过当前文件：等待超时。");
                saved = AdvanceFile(logs);
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, logs);
                da.SetData(2, logs[0]);
                da.SetData(3, saved);
                return;
            }

            // 处理当前文件
            try
            {
                int count = ids.Count;
                ValidateCount(numbers, count, "旧编号", false);
                ValidateCount(materials, count, "材质", true);
                ValidateCount(colors, count, "颜色", true);

                for (int i = 0; i < count; i++)
                {
                    if (string.IsNullOrWhiteSpace(numbers[i]))
                        throw new ArgumentException("第 " +
                            (i + 1).ToString(CultureInfo.InvariantCulture) + " 个旧编号为空。");
                    if (ids[i] == null || ids[i].Value == Guid.Empty)
                        throw new ArgumentException("第 " +
                            (i + 1).ToString(CultureInfo.InvariantCulture) + " 个 GUID 无效。");

                    Brep brep = ReadBrepFromActiveDocument(ids[i].Value, i);
                    WbFeature feature = WbFeature.Create(
                        TextAt(materials, i), TextAt(colors, i),
                        brep, _decimals, _inertiaDegenerateRatio, _fallbackTolerance, _collectedRecords.Count);
                    _collectedRecords.Add(new WbMigrationRecord(
                        _collectedRecords.Count, numbers[i].Trim(),
                        feature, _pendingFiles[_fileIndex]));
                    featureTexts.Add(feature.Serialize());
                    _diagnostics.AddRange(feature.DiagnosticLines(
                        numbers[i].Trim(), _pendingFiles[_fileIndex]));
                }

                saved = AdvanceFile(logs);
            }
            catch (Exception ex)
            {
                _failedFileCount++;
                logs.Add("跳过当前文件（" + _pendingFiles[_fileIndex] + "）：" + ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
                _diagnostics.Add("文件处理失败：" + _pendingFiles[_fileIndex] + " - " + ex.Message);
                saved = AdvanceFile(logs);
            }

            da.SetDataList(0, featureTexts);
            da.SetDataList(1, logs);
            da.SetData(2, logs[0]);
            da.SetData(3, saved);
            da.SetDataList(4, _diagnostics);
        }

        // ================================================================
        // 右键菜单（参考"实体特征分类"电池设计）
        // ================================================================
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);

            WbSettingsMenuControl control = new WbSettingsMenuControl(this);
            ToolStripControlHost host = new ToolStripControlHost(control)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = control.Size
            };
            menu.Closing += (sender, e) => control.CommitAll();
            menu.Items.Add(host);
        }

        private void SetCriterionEnabled(V5Criterion criterion, bool enabled)
        {
            if (_enabled[criterion] == enabled)
                return;

            _enabled[criterion] = enabled;
            MarkSettingsChanged();
        }

        private void SetCriterionTolerance(V5Criterion criterion, string text)
        {
            if (!TryParseTolerance(text, out double value, out bool useAbsolute))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "误差数值无法识别：" + text);
                return;
            }

            value = Math.Abs(value);
            if (IsAbsoluteOnlyCriterion(criterion))
                useAbsolute = true;

            if (Math.Abs(_tolerances[criterion] - value) <= 1e-12 &&
                _isRelativeTolerance[criterion] == !useAbsolute)
                return;

            _tolerances[criterion] = value;
            _isRelativeTolerance[criterion] = !useAbsolute;
            MarkSettingsChanged();
        }

        private void SetMirrorTolerance(string text)
        {
            if (!TryParseTolerance(text, out double value, out bool _))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "镜像区分容差无法识别：" + text);
                return;
            }

            value = Math.Abs(value);
            if (Math.Abs(_mirrorTolerance - value) <= 1e-12)
                return;

            _mirrorTolerance = value;
            MarkSettingsChanged();
        }

        private void SetFallbackTolerance(string text)
        {
            if (!TryParseTolerance(text, out double value, out bool _))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "回退容差无法识别：" + text);
                return;
            }

            value = Math.Abs(value);
            if (Math.Abs(_fallbackTolerance - value) <= 1e-12)
                return;

            _fallbackTolerance = value;
            MarkSettingsChanged();
        }

        private void SetInertiaDegenerateRatio(string text)
        {
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "退化判定阈值无法识别：" + text);
                return;
            }

            value = Math.Max(0.5, Math.Min(1.0, value));
            if (Math.Abs(_inertiaDegenerateRatio - value) <= 1e-12)
                return;

            _inertiaDegenerateRatio = value;
            MarkSettingsChanged();
        }

        private void SetStrictMaterialColor(bool value)
        {
            if (_strictMaterialColor == value) return;
            _strictMaterialColor = value;
            MarkSettingsChanged();
        }

        private void SetHoleNormalTolerance(string text)
        {
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "法向容差无法识别：" + text);
                return;
            }

            value = Math.Abs(value);
            if (Math.Abs(_holeNormalTolerance - value) <= 1e-12)
                return;

            _holeNormalTolerance = value;
            MarkSettingsChanged();
        }

        private void SetOuterProfileAreaRatio(string text)
        {
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外轮廓面积容差无法识别：" + text);
                return;
            }
            value = Math.Max(0, value);
            if (Math.Abs(_outerProfileAreaRatio - value) <= 1e-15) return;
            _outerProfileAreaRatio = value;
            MarkSettingsChanged();
        }

        private void SetOuterProfilePerimeterMin(string text)
        {
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外轮廓最小周长容差无法识别：" + text);
                return;
            }
            value = Math.Max(0, value);
            if (Math.Abs(_outerProfilePerimeterMin - value) <= 1e-15) return;
            _outerProfilePerimeterMin = value;
            MarkSettingsChanged();
        }

        private void SetDegeneratePositionMultiplier(string text)
        {
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "退化孔容差倍数无法识别：" + text);
                return;
            }
            value = Math.Max(1.0, value);
            if (Math.Abs(_degeneratePositionMultiplier - value) <= 1e-15) return;
            _degeneratePositionMultiplier = value;
            MarkSettingsChanged();
        }

        private void SetEnableDegenerateMatching(bool value)
        {
            if (_enableDegenerateMatching == value) return;
            _enableDegenerateMatching = value;
            MarkSettingsChanged();
        }

        private void SetStorageDecimalPlaces(V5Criterion criterion, string text)
        {
            if (!TryParseStorageDecimalPlaces(text, out int value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "入库精度无法识别：" + text);
                return;
            }

            value = ClampStorageDecimalPlaces(value);
            if (_decimals[criterion] == value)
                return;

            _decimals[criterion] = value;
            MarkSettingsChanged();
        }

        private static bool TryParseTolerance(string text, out double value, out bool useAbsolute)
        {
            value = 0.0;
            useAbsolute = true;
            text = (text ?? string.Empty).Trim();
            if (text.EndsWith("%", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1).Trim();
                useAbsolute = false;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseStorageDecimalPlaces(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static int ClampStorageDecimalPlaces(int value)
        {
            return Math.Max(0, Math.Min(12, value));
        }

        private string FormatToleranceForCriterion(V5Criterion criterion)
        {
            string text = _tolerances[criterion].ToString("G6", CultureInfo.InvariantCulture);
            return IsAbsoluteOnlyCriterion(criterion) || _isRelativeTolerance[criterion] == false
                ? text
                : text + "%";
        }

        private static string CriterionDisplayName(V5Criterion criterion)
        {
            switch (criterion)
            {
                case V5Criterion.Length: return "长度";
                case V5Criterion.Inertia: return "转动惯量";
                case V5Criterion.Area: return "表面积";
                case V5Criterion.Volume: return "体积";
                case V5Criterion.EdgeSum: return "所有边周长";
                case V5Criterion.HoleArea: return "孔面积";
                case V5Criterion.HolePosition: return "孔位";
                default: return criterion.ToString();
            }
        }

        /// <summary>
        /// 孔位强制绝对容差（不可切换为 % 相对模式）。
        /// 设计决策：孔位是空间坐标偏差，没有自然的"自身值"作相对基准；
        /// 若用包围盒尺寸归一化，则同一绝对偏差在小件和大件上判为不同比例，
        /// 引入新的不一致。跨尺度混库场景通过用户自行选择合适绝对容差来应对。
        /// </summary>
        private static bool IsAbsoluteOnlyCriterion(V5Criterion criterion)
        {
            return criterion == V5Criterion.HolePosition;
        }

        private void MarkSettingsChanged()
        {
            Attributes?.ExpireLayout();
            OnDisplayExpired(true);
        }

        private sealed class WbSettingsMenuControl : UserControl
        {
            private static readonly V5Criterion[] Criteria =
            {
                V5Criterion.Length,
                V5Criterion.Inertia,
                V5Criterion.Area,
                V5Criterion.Volume,
                V5Criterion.EdgeSum,
                V5Criterion.HoleArea,
                V5Criterion.HolePosition
            };

            private readonly WbClassificationMigration _owner;
            private readonly ToolTip _toolTip = new ToolTip();
            private readonly List<Action> _commitActions = new List<Action>();

            public WbSettingsMenuControl(WbClassificationMigration owner)
            {
                _owner = owner;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = SystemColors.Control;
                Margin = Padding.Empty;
                Padding = new Padding(8, 6, 8, 8);
                Size = new Size(360, 556);

                TableLayoutPanel layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 3,
                    RowCount = Criteria.Length + 11,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
                for (int i = 0; i < Criteria.Length + 9; i++)
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
                Controls.Add(layout);

                AddHeader(layout);
                for (int i = 0; i < Criteria.Length; i++)
                    AddCriterionRow(layout, i + 1, Criteria[i]);

                AddMirrorRow(layout, Criteria.Length + 1);
                AddFallbackToleranceRow(layout, Criteria.Length + 2);
                AddInertiaRow(layout, Criteria.Length + 3);
                AddMaterialStrictRow(layout, Criteria.Length + 4);
                AddOuterProfileAreaRow(layout, Criteria.Length + 5);
                AddOuterProfilePerimeterRow(layout, Criteria.Length + 6);
                AddDegenerateMultiplierRow(layout, Criteria.Length + 7);
                AddDegenerateMatchRow(layout, Criteria.Length + 8);
                AddNormalToleranceRow(layout, Criteria.Length + 9);
                AddHelp(layout, Criteria.Length + 10);
            }

            private void AddHeader(TableLayoutPanel layout)
            {
                layout.Controls.Add(CreateHeaderLabel(""), 0, 0);
                layout.Controls.Add(CreateHeaderLabel("容差"), 1, 0);
                layout.Controls.Add(CreateHeaderLabel("入库精度"), 2, 0);
            }

            private void AddCriterionRow(TableLayoutPanel layout, int row, V5Criterion criterion)
            {
                bool isLength = criterion == V5Criterion.Length;
                CheckBox checkBox = new CheckBox
                {
                    Text = CriterionDisplayName(criterion),
                    Checked = _owner._enabled[criterion],
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 4, 1),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Enabled = !isLength
                };
                if (!isLength)
                    checkBox.CheckedChanged += (sender, e) => _owner.SetCriterionEnabled(criterion, checkBox.Checked);
                layout.Controls.Add(checkBox, 0, row);

                TextBox toleranceBox = CreateTextBox(_owner.FormatToleranceForCriterion(criterion));
                _toolTip.SetToolTip(toleranceBox, IsAbsoluteOnlyCriterion(criterion)
                    ? "孔位只支持绝对误差，例如 0.5。"
                    : "带 % 表示相对误差，例如 0.5%；不带 % 表示绝对误差，例如 100。");
                RegisterTextBoxCommit(
                    toleranceBox,
                    () => _owner.SetCriterionTolerance(criterion, toleranceBox.Text));
                layout.Controls.Add(toleranceBox, 1, row);

                TextBox precisionBox = CreateTextBox(criterion == V5Criterion.Length
                    ? "原值"
                    : _owner._decimals[criterion].ToString(CultureInfo.InvariantCulture));
                if (criterion == V5Criterion.Length)
                {
                    precisionBox.Enabled = false;
                    _toolTip.SetToolTip(precisionBox, "包围盒三边按原始双精度值保存，不在这里改写。");
                }
                else
                {
                    _toolTip.SetToolTip(precisionBox, "保存到数据库时保留的小数位数，范围 0 到 12。");
                    RegisterTextBoxCommit(
                        precisionBox,
                        () => _owner.SetStorageDecimalPlaces(criterion, precisionBox.Text));
                }
                layout.Controls.Add(precisionBox, 2, row);
            }

            private void AddMirrorRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("镜像区分");
                layout.Controls.Add(label, 0, row);

                TextBox toleranceBox = CreateTextBox(_owner._mirrorTolerance.ToString("G6", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(toleranceBox, "镜像区分只支持绝对误差；小于等于该值时不区分 A/B。");
                RegisterTextBoxCommit(
                    toleranceBox,
                    () => _owner.SetMirrorTolerance(toleranceBox.Text));
                layout.Controls.Add(toleranceBox, 1, row);
            }

            private void AddFallbackToleranceRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("回退容差");
                layout.Controls.Add(label, 0, row);

                TextBox toleranceBox = CreateTextBox(_owner._fallbackTolerance.ToString("G6", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(toleranceBox, "无 Rhino 文档时的曲线拼接容差回退值。正常 Rhino 交互环境下不使用此值。");
                RegisterTextBoxCommit(
                    toleranceBox,
                    () => _owner.SetFallbackTolerance(toleranceBox.Text));
                layout.Controls.Add(toleranceBox, 1, row);
            }

            private void AddInertiaRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("退化判定阈值");
                layout.Controls.Add(label, 0, row);

                TextBox ratioBox = CreateTextBox(_owner._inertiaDegenerateRatio.ToString("G6", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(ratioBox, "相邻主惯量比值超过此值时标记为退化件，范围 0.5~1.0。默认 0.99。");
                RegisterTextBoxCommit(
                    ratioBox,
                    () => _owner.SetInertiaDegenerateRatio(ratioBox.Text));
                layout.Controls.Add(ratioBox, 1, row);
            }

            private void AddOuterProfileAreaRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("外轮廓面积容差");
                layout.Controls.Add(label, 0, row);
                TextBox tolBox = CreateTextBox(_owner._outerProfileAreaRatio.ToString("G6", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "外轮廓面积相对容差：|A-B|/max(A,B,1) 低于此值时通过。默认 0.0001。");
                RegisterTextBoxCommit(tolBox, () => _owner.SetOuterProfileAreaRatio(tolBox.Text));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddOuterProfilePerimeterRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("外轮廓周长最小容差");
                layout.Controls.Add(label, 0, row);
                TextBox tolBox = CreateTextBox(_owner._outerProfilePerimeterMin.ToString("G6", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "外轮廓周长匹配取 max(孔位容差, 此值) 的最小值。默认 0.01。");
                RegisterTextBoxCommit(tolBox, () => _owner.SetOuterProfilePerimeterMin(tolBox.Text));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddDegenerateMultiplierRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("退化孔容差倍数");
                layout.Controls.Add(label, 0, row);
                TextBox tolBox = CreateTextBox(_owner._degeneratePositionMultiplier.ToString("G6", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "退化孔匹配时几何容差乘以此倍数。默认 2.0，范围 >= 1.0。");
                RegisterTextBoxCommit(tolBox, () => _owner.SetDegeneratePositionMultiplier(tolBox.Text));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddDegenerateMatchRow(TableLayoutPanel layout, int row)
            {
                CheckBox checkBox = new CheckBox
                {
                    Text = "退化孔低置信度匹配",
                    Checked = _owner._enableDegenerateMatching,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 4, 1),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _toolTip.SetToolTip(checkBox, "开启后使用拟合平面特征（投影面积/拟合法向/圆形度）对退化孔进行低置信度匹配。默认关闭。");
                checkBox.CheckedChanged += (sender, e) => _owner.SetEnableDegenerateMatching(checkBox.Checked);
                layout.Controls.Add(checkBox, 0, row);

                Label hint = CreateBodyLabel("谨慎启用：低置信度");
                hint.ForeColor = Color.DimGray;
                hint.AutoSize = false;
                hint.Dock = DockStyle.Fill;
                hint.TextAlign = ContentAlignment.MiddleLeft;
                layout.SetColumnSpan(hint, 2);
                layout.Controls.Add(hint, 1, row);
            }

            private void AddNormalToleranceRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("法向兼容阈值");
                layout.Controls.Add(label, 0, row);

                TextBox tolBox = CreateTextBox(_owner._holeNormalTolerance.ToString("G6", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "|cos(法向夹角)| 低于此值时法向比较不通过。默认 0.9986（约 3°）。");
                RegisterTextBoxCommit(
                    tolBox,
                    () => _owner.SetHoleNormalTolerance(tolBox.Text));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddMaterialStrictRow(TableLayoutPanel layout, int row)
            {
                CheckBox checkBox = new CheckBox
                {
                    Text = "材质/颜色严格模式",
                    Checked = _owner._strictMaterialColor,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 4, 1),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _toolTip.SetToolTip(checkBox, "开启后，材质和颜色必须双方均填写且相等，或双方均为空才匹配。关闭时任一方为空即视为匹配（宽松模式）。");
                checkBox.CheckedChanged += (sender, e) => _owner.SetStrictMaterialColor(checkBox.Checked);
                layout.Controls.Add(checkBox, 0, row);
                
                Label hint = CreateBodyLabel("开启后空值不再通配");
                hint.ForeColor = Color.DimGray;
                hint.AutoSize = false;
                hint.Dock = DockStyle.Fill;
                hint.TextAlign = ContentAlignment.MiddleLeft;
                layout.SetColumnSpan(hint, 2);
                layout.Controls.Add(hint, 1, row);
            }

            private void AddHelp(TableLayoutPanel layout, int row)
            {
                Label help = CreateBodyLabel("容差默认按相对误差填写；输入 0.5% 表示相对误差，输入 100 表示绝对误差。孔位和镜像区分只能使用绝对误差。");
                help.ForeColor = Color.DimGray;
                help.AutoSize = false;
                help.Dock = DockStyle.Fill;
                layout.SetColumnSpan(help, 3);
                layout.Controls.Add(help, 0, row);
            }

            private static Label CreateHeaderLabel(string text)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Font = new Font(SystemFonts.MenuFont, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 0, 4, 3)
                };
            }

            private static Label CreateBodyLabel(string text)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 1, 4, 1)
                };
            }

            private static TextBox CreateTextBox(string text)
            {
                return new TextBox
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 8, 1),
                    BorderStyle = BorderStyle.FixedSingle
                };
            }

            public void CommitAll()
            {
                foreach (Action commit in _commitActions.ToArray())
                {
                    commit();
                }
            }

            private void RegisterTextBoxCommit(TextBox box, Action commit)
            {
                _commitActions.Add(commit);
                box.KeyDown += (sender, e) =>
                {
                    if (e.KeyCode != Keys.Enter)
                        return;

                    commit();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                };
            }
        }

        // ================================================================
        // 序列化
        // ================================================================
        public override bool Write(GH_IWriter writer)
        {
            foreach (V5Criterion c in _enabled.Keys)
            {
                writer.SetBoolean(SettingsChunk + c + "Enabled", _enabled[c]);
                writer.SetDouble(SettingsChunk + c + "Tolerance", _tolerances[c]);
                writer.SetBoolean(SettingsChunk + c + "TolIsRel", _isRelativeTolerance[c]);
            }
            foreach (V5Criterion c in _decimals.Keys)
                writer.SetInt32(SettingsChunk + c + "Decimals", _decimals[c]);
            writer.SetDouble(SettingsChunk + "MirrorTolerance", _mirrorTolerance);
            writer.SetDouble(SettingsChunk + "FallbackTolerance", _fallbackTolerance);
            writer.SetDouble(SettingsChunk + "InertiaDegenerateRatio", _inertiaDegenerateRatio);
            writer.SetDouble(SettingsChunk + "HoleNormalTolerance", _holeNormalTolerance);
            writer.SetBoolean(SettingsChunk + "StrictMaterialColor", _strictMaterialColor);
            writer.SetDouble(SettingsChunk + "OuterProfileAreaRatio", _outerProfileAreaRatio);
            writer.SetDouble(SettingsChunk + "OuterProfilePerimeterMin", _outerProfilePerimeterMin);
            writer.SetDouble(SettingsChunk + "DegeneratePositionMultiplier", _degeneratePositionMultiplier);
            writer.SetBoolean(SettingsChunk + "EnableDegenerateMatching", _enableDegenerateMatching);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            foreach (V5Criterion c in _enabled.Keys.ToList())
            {
                bool e = _enabled[c];
                double t = _tolerances[c];
                bool isRel = _isRelativeTolerance[c];
                if (reader.TryGetBoolean(SettingsChunk + c + "Enabled", ref e) &&
                    c != V5Criterion.Length)
                    _enabled[c] = e;
                if (reader.TryGetDouble(SettingsChunk + c + "Tolerance", ref t))
                    _tolerances[c] = Math.Abs(t);
                if (reader.TryGetBoolean(SettingsChunk + c + "TolIsRel", ref isRel))
                    _isRelativeTolerance[c] = isRel;
            }
            _enabled[V5Criterion.Length] = true;
            foreach (V5Criterion c in _decimals.Keys.ToList())
            {
                int d = _decimals[c];
                if (reader.TryGetInt32(SettingsChunk + c + "Decimals", ref d))
                    _decimals[c] = Math.Max(0, Math.Min(12, d));
            }

            double mirrorTolerance = _mirrorTolerance;
            if (reader.TryGetDouble(SettingsChunk + "MirrorTolerance", ref mirrorTolerance))
                _mirrorTolerance = Math.Abs(mirrorTolerance);

            double fallbackTolerance = _fallbackTolerance;
            if (reader.TryGetDouble(SettingsChunk + "FallbackTolerance", ref fallbackTolerance))
                _fallbackTolerance = Math.Max(0, fallbackTolerance);

            double inertiaDegenerateRatio = _inertiaDegenerateRatio;
            if (reader.TryGetDouble(SettingsChunk + "InertiaDegenerateRatio", ref inertiaDegenerateRatio))
                _inertiaDegenerateRatio = Math.Max(0.5, Math.Min(1.0, inertiaDegenerateRatio));

            double holeNormalTolerance = _holeNormalTolerance;
            if (reader.TryGetDouble(SettingsChunk + "HoleNormalTolerance", ref holeNormalTolerance))
                _holeNormalTolerance = holeNormalTolerance;

            bool strictMaterialColor = _strictMaterialColor;
            if (reader.TryGetBoolean(SettingsChunk + "StrictMaterialColor", ref strictMaterialColor))
                _strictMaterialColor = strictMaterialColor;

            double outerProfileAreaRatio = _outerProfileAreaRatio;
            if (reader.TryGetDouble(SettingsChunk + "OuterProfileAreaRatio", ref outerProfileAreaRatio))
                _outerProfileAreaRatio = Math.Max(0, outerProfileAreaRatio);

            double outerProfilePerimeterMin = _outerProfilePerimeterMin;
            if (reader.TryGetDouble(SettingsChunk + "OuterProfilePerimeterMin", ref outerProfilePerimeterMin))
                _outerProfilePerimeterMin = Math.Max(0, outerProfilePerimeterMin);

            double degeneratePositionMultiplier = _degeneratePositionMultiplier;
            if (reader.TryGetDouble(SettingsChunk + "DegeneratePositionMultiplier", ref degeneratePositionMultiplier))
                _degeneratePositionMultiplier = Math.Max(1.0, degeneratePositionMultiplier);

            bool enableDegenerateMatching = _enableDegenerateMatching;
            if (reader.TryGetBoolean(SettingsChunk + "EnableDegenerateMatching", ref enableDegenerateMatching))
                _enableDegenerateMatching = enableDegenerateMatching;

            return base.Read(reader);
        }

        // ================================================================
        // 文件操作
        // ================================================================
        private void OpenCurrentFile()
        {
            string filePath = _pendingFiles[_fileIndex];
            if (IsActiveRhinoFile(filePath)) return;

            RhinoDoc current = RhinoDoc.ActiveDoc;
            if (current != null)
            {
                current.Modified = false;
                RhinoApp.RunScript("_-Close _No", false);
                RhinoApp.Wait();
            }

            string escaped = filePath.Replace("\"", "\"\"");
            if (!RhinoApp.RunScript("_-Open \"" + escaped + "\"", false))
                throw new InvalidOperationException("无法打开文件：" + filePath);
            RhinoApp.Wait();

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null || !IsActiveRhinoFile(filePath))
                throw new InvalidOperationException("当前文档未切换到：" + filePath);
        }

        private static bool IsActiveRhinoFile(string filePath)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null) return false;
            return string.Equals(
                NormalizePath(doc.Path), NormalizePath(filePath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return Path.GetFullPath(path.Trim()); }
            catch { return path.Trim(); }
        }

        /// <summary>
        /// 验证检查点文件列表与当前输入文件列表一致性。
        /// 按原顺序逐项比较规范化路径；顺序不同即失败（FileIndex 依赖原顺序）。
        /// 返回是否通过，失败时 reason 包含诊断消息。
        /// </summary>
        internal static bool ValidateFileListConsistency(
            List<string> checkpointFiles, List<string> currentFiles, out string reason)
        {
            if (currentFiles == null || currentFiles.Count == 0)
            {
                reason = "当前输入文件列表为空。";
                return false;
            }
            if (checkpointFiles == null || checkpointFiles.Count == 0)
            {
                reason = "检查点文件列表为空。";
                return false;
            }
            if (checkpointFiles.Count != currentFiles.Count)
            {
                reason = "检查点文件列表数量不一致（检查点 " +
                    checkpointFiles.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个，当前输入 " +
                    currentFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个）。";
                return false;
            }

            for (int i = 0; i < checkpointFiles.Count; i++)
            {
                string cpNorm = NormalizePath(checkpointFiles[i]);
                string curNorm = NormalizePath(currentFiles[i]);
                if (!string.Equals(cpNorm, curNorm, StringComparison.OrdinalIgnoreCase))
                {
                    // 检查是否为同一文件集合但顺序不同（给用户更明确的提示）
                    var cpSet = new HashSet<string>(
                        checkpointFiles.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
                    var curSet = new HashSet<string>(
                        currentFiles.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
                    string mismatchType = cpSet.SetEquals(curSet)
                        ? "文件集合相同但顺序不一致"
                        : "文件内容不一致";
                    reason = "检查点文件列表与当前输入不一致：位置 " +
                        i.ToString(CultureInfo.InvariantCulture) +
                        "（检查点 " + Path.GetFileName(cpNorm) +
                        "，当前 " + Path.GetFileName(curNorm) +
                        "）——" + mismatchType + "。";
                    return false;
                }
            }
            reason = null;
            return true;
        }

        private static void CloseCurrentFile()
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null) return;
            doc.Modified = false;
            RhinoApp.RunScript("_-Close _No", false);
            RhinoApp.Wait();
        }

        private static Brep ReadBrepFromActiveDocument(Guid id, int index)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            Rhino.DocObjects.RhinoObject obj = doc?.Objects.FindId(id);
            if (obj == null)
                throw new InvalidOperationException("找不到第 " +
                    (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 GUID：" + id);
            if (obj.Geometry is Brep brep) return brep.DuplicateBrep();
            if (obj.Geometry is Surface surface) return surface.ToBrep();
            if (obj.Geometry is Extrusion extrusion) return extrusion.ToBrep();
            throw new InvalidOperationException("GUID " + id +
                " 对应的对象不是 Brep、曲面或拉伸体。");
        }

        // ================================================================
        // 数据稳定性
        // ================================================================
        private void ResetDataStability()
        {
            _lastDataSignature = string.Empty;
            _stableDataCount = 0;
            _dataCheckCount = 0;
        }

        private bool IsDataStable(List<string> numbers, List<GH_Guid> ids)
        {
            _dataCheckCount++;
            string activePath = NormalizePath(
                RhinoDoc.ActiveDoc == null ? string.Empty : RhinoDoc.ActiveDoc.Path);
            string numberValues = string.Join("\u001f", numbers.Select(x => x ?? string.Empty));
            string guidValues = string.Join("\u001f",
                ids.Select(x => x == null ? string.Empty : x.Value.ToString("D")));
            string signature = activePath + "\u001e" + numberValues + "\u001e" + guidValues;

            if (string.Equals(signature, _lastDataSignature, StringComparison.Ordinal))
                _stableDataCount++;
            else
            {
                _lastDataSignature = signature;
                _stableDataCount = 1;
            }
            return _stableDataCount >= 3;
        }

        private void ResetMigration()
        {
            CancelRefresh();
            StopFileOpenTimer();
            _state = State.Idle;
            _runRequested = false;
            _paused = false;
            _pendingFiles.Clear();
            _fileIndex = 0;
            _waitingAttempts = 0;
            _failedFileCount = 0;
            _pendingDatabasePath = string.Empty;
        }

        private void SaveCheckpoint()
        {
            if (string.IsNullOrWhiteSpace(_pendingDatabasePath)) return;
            try
            {
                var cp = new WbMigrationCheckpoint
                {
                    DatabasePath = _pendingDatabasePath,
                    PendingFiles = new List<string>(_pendingFiles),
                    FileIndex = _fileIndex,
                    Diagnostics = new List<string>(_diagnostics)
                };
                foreach (var r in _collectedRecords)
                    cp.CollectedRecords.Add(new WbMigrationCheckpoint.CheckpointRecord
                    {
                        Index = r.Index,
                        Number = r.Number,
                        FeatureText = r.Feature.Serialize(),
                        SourceFile = r.SourceFile
                    });
                cp.Save(WbMigrationCheckpoint.GetCheckpointPath(_pendingDatabasePath));
            }
            catch (Exception ex)
            {
                _diagnostics.Add("检查点保存失败：" + ex.Message + "；迁移将继续但无断点续跑保护。");
            }
        }

        private void DeleteCheckpoint()
        {
            try
            {
                string cpPath = WbMigrationCheckpoint.GetCheckpointPath(_pendingDatabasePath);
                if (File.Exists(cpPath)) File.Delete(cpPath);
            }
            catch { }
        }

        private bool TryResumeCheckpoint(string databasePath, List<string> currentFiles)
        {
            string cpPath = WbMigrationCheckpoint.GetCheckpointPath(databasePath);
            WbMigrationCheckpoint cp;
            try
            {
                cp = WbMigrationCheckpoint.Load(cpPath);
            }
            catch (Exception ex)
            {
                _diagnostics.Add("检查点加载失败（" + ex.Message + "）；将从头开始迁移。");
                return false;
            }
            if (cp == null) return false;
            // 验证检查点一致性：数据库路径匹配，文件列表一致
            if (!string.Equals(NormalizePath(cp.DatabasePath), NormalizePath(databasePath),
                StringComparison.OrdinalIgnoreCase))
                return false;
            if (cp.PendingFiles == null || cp.PendingFiles.Count == 0) return false;
            if (cp.FileIndex <= 0 || cp.FileIndex >= cp.PendingFiles.Count) return false;

            // 验证当前输入文件列表与检查点文件列表一致性
            // 按原顺序逐项比较规范化路径；顺序不同即拒绝恢复（FileIndex 依赖原顺序）
            if (!ValidateFileListConsistency(cp.PendingFiles, currentFiles, out string listReason))
            {
                _diagnostics.Add(listReason + "将从头开始迁移。");
                return false;
            }

            // 恢复状态
            _pendingDatabasePath = databasePath;
            _pendingFiles.Clear();
            _pendingFiles.AddRange(cp.PendingFiles);
            _fileIndex = cp.FileIndex;
            _waitingAttempts = 0;
            if (cp.Diagnostics != null)
                _diagnostics.AddRange(cp.Diagnostics);

            // 恢复已收集记录
            if (cp.CollectedRecords != null)
            {
                foreach (var cr in cp.CollectedRecords)
                {
                    try
                    {
                        WbFeature feature = WbFeature.Deserialize(cr.FeatureText);
                        _collectedRecords.Add(new WbMigrationRecord(
                            cr.Index, cr.Number, feature, cr.SourceFile ?? string.Empty));
                    }
                    catch (Exception ex)
                    {
                        // 序列化特征解析失败，该条记录需要在恢复时重新提取
                        // 这通常不应发生；如果发生则无法安全恢复，回退到完全重启
                        _diagnostics.Add("检查点记录反序列化失败：" + ex.Message);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            "检查点恢复失败：" + ex.Message + " 将从头开始迁移。");
                        _collectedRecords.Clear();
                        _fileIndex = 0;
                        return false;
                    }
                }
            }

            _diagnostics.Add("从检查点恢复：已处理前 " + _fileIndex.ToString(CultureInfo.InvariantCulture) +
                " 个文件，共收集 " + _collectedRecords.Count.ToString(CultureInfo.InvariantCulture) +
                " 条记录；从第 " + (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件继续。");
            return true;
        }

        // ================================================================
        // 文件推进 → 聚类 → 入库
        // ================================================================
        private bool AdvanceFile(List<string> logs)
        {
            // 保存检查点（跳过失败文件时也会到达这里）
            SaveCheckpoint();
            _fileIndex++;
            if (_fileIndex < _pendingFiles.Count)
            {
                _waitingAttempts = 0;
                _state = State.OpeningFile;
                ResetDataStability();
                _statusText = "准备打开第 " +
                    (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                    " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个文件。";
                ScheduleOpenCurrentFile();
                logs.Add("当前文件处理完成；即将打开第 " +
                    (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                    " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个文件。");
                return false;
            }

            // 全部处理完 → 聚类 → 入库
            var audit = WbMigrationAudit.Create(_collectedRecords, _enabled, _tolerances, _isRelativeTolerance, _mirrorTolerance, _strictMaterialColor, _holeNormalTolerance,
                _outerProfileAreaRatio, _outerProfilePerimeterMin, _degeneratePositionMultiplier, _enableDegenerateMatching);
            _diagnostics.AddRange(audit.DiagnosticLines());
            var toInsert = audit.RecordsToInsert();
            WbMigrationStore.Rebuild(_pendingDatabasePath, toInsert, audit,
                _enabled, _tolerances, _decimals, _isRelativeTolerance, _mirrorTolerance,
                _inertiaDegenerateRatio, _holeNormalTolerance, _strictMaterialColor,
                _outerProfileAreaRatio, _outerProfilePerimeterMin, _degeneratePositionMultiplier,
                _enableDegenerateMatching, _fallbackTolerance);
            WbMigrationStore.Verify(_pendingDatabasePath, toInsert);
            if (_failedFileCount > 0)
                logs.Add("迁移过程中 " + _failedFileCount.ToString(CultureInfo.InvariantCulture) +
                    " 个文件处理失败已跳过；详见诊断输出。");
            logs.Add("已完成 V9 分类数据库重建，写入 " +
                toInsert.Count.ToString(CultureInfo.InvariantCulture) + " 条（读入 " +
                _collectedRecords.Count.ToString(CultureInfo.InvariantCulture) + " 条）：" +
                _pendingDatabasePath);
            logs.AddRange(audit.ToLogs());
            DeleteCheckpoint();
            ResetMigration();
            ScheduleFinalClose();
            return true;
        }

        // ================================================================
        // 定时器
        // ================================================================
        private void ScheduleRefresh(int waitMs)
        {
            CancelRefresh();
            int token = ++_refreshToken;
            GH_Document doc = OnPingDocument();
            if (doc != null)
            {
                doc.ScheduleSolution(Math.Max(1, waitMs), d =>
                {
                    if (token != _refreshToken) return;
                    ExpireSolution(false);
                });
                return;
            }
            _refreshTimer = new Timer { Interval = Math.Max(1, waitMs) };
            _refreshTimer.Tick += (s, e) =>
            {
                Timer t = _refreshTimer;
                _refreshTimer = null;
                t?.Stop(); t?.Dispose();
                if (token != _refreshToken) return;
                ExpireSolution(false);
            };
            _refreshTimer.Start();
        }

        private void CancelRefresh()
        {
            _refreshToken++;
            if (_refreshTimer == null) return;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _refreshTimer = null;
        }

        private void ScheduleOpenCurrentFile()
        {
            StopFileOpenTimer();
            _fileOpenTimer = new Timer { Interval = 100 };
            _fileOpenTimer.Tick += (s, e) =>
            {
                Timer t = _fileOpenTimer;
                _fileOpenTimer = null;
                t?.Stop(); t?.Dispose();
                if (_paused || _state != State.OpeningFile) return;
                try
                {
                    OpenCurrentFile();
                    _state = State.AwaitingData;
                    _waitingAttempts = 0;
                    ResetDataStability();
                    _statusText = "已打开第 " +
                        (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                        " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) +
                        " 个文件；等待旧编号和 GUID 稳定。";
                    ScheduleRefresh(1500);
                }
                catch (Exception ex)
                {
                    _statusText = "错误：" + ex.Message;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    ResetMigration();
                    ScheduleRefresh(100);
                }
            };
            _fileOpenTimer.Start();
        }

        private void StopFileOpenTimer()
        {
            if (_fileOpenTimer == null) return;
            _fileOpenTimer.Stop();
            _fileOpenTimer.Dispose();
            _fileOpenTimer = null;
        }

        private void ScheduleFinalClose()
        {
            StopFinalCloseTimer();
            _finalCloseTimer = new Timer { Interval = 100 };
            _finalCloseTimer.Tick += (s, e) =>
            {
                Timer t = _finalCloseTimer;
                _finalCloseTimer = null;
                t?.Stop(); t?.Dispose();
                CloseCurrentFile();
            };
            _finalCloseTimer.Start();
        }

        private void StopFinalCloseTimer()
        {
            if (_finalCloseTimer == null) return;
            _finalCloseTimer.Stop();
            _finalCloseTimer.Dispose();
            _finalCloseTimer = null;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            CancelRefresh();
            StopFileOpenTimer();
            StopFinalCloseTimer();
            base.RemovedFromDocument(document);
        }

        // ================================================================
        // 辅助
        // ================================================================
        private static void ValidateCount<T>(List<T> values, int count, string name, bool optional)
        {
            if (count == 0)
                throw new ArgumentException("实体 GUID 不能为空。");
            if (values == null || values.Count == 0)
            {
                if (optional) return;
                throw new ArgumentException(name + "不能为空。");
            }
            if (values.Count != count)
                throw new ArgumentException(name + "数量必须与实体 GUID 数量一致。");
        }

        private static string TextAt(List<string> values, int index) =>
            values == null || values.Count == 0 ? string.Empty : values[index] ?? string.Empty;

        public override Guid ComponentGuid =>
            new Guid("B1D97B05-3A4A-4F3C-91F0-4AC1D59EE805");
    }

    // ========================================================================
    // 自定义渲染（启动 / 暂停按钮）
    // ========================================================================
    internal sealed class WbClassificationMigrationAttributes : GH_ComponentAttributes
    {
        public WbClassificationMigrationAttributes(WbClassificationMigration owner)
            : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 44f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            var component = (WbClassificationMigration)Owner;

            // 暂停按钮
            RectangleF pauseRect = new RectangleF(
                Bounds.X, Bounds.Bottom - 44f, Bounds.Width, 20f);
            pauseRect.Inflate(-5f, -2f);
            using (GH_Capsule pause = GH_Capsule.CreateCapsule(pauseRect, GH_Palette.Grey))
                pause.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            using (Font pf = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat sf = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString(component.IsPaused ? "继续" : "暂停",
                    pf, Brushes.White, pauseRect, sf);

            // 启动按钮
            RectangleF rect = new RectangleF(
                Bounds.X, Bounds.Bottom - 22f, Bounds.Width, 20f);
            rect.Inflate(-5f, -2f);
            using (GH_Capsule cap = GH_Capsule.CreateCapsule(rect, GH_Palette.Black))
                cap.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            using (Font f = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat s = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("启动", f, Brushes.White, rect, s);
        }

        public override GH_ObjectResponse RespondToMouseDown(
            GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF rect = new RectangleF(Bounds.X, Bounds.Bottom - 22f, Bounds.Width, 20f);
            RectangleF pauseRect = new RectangleF(
                Bounds.X, Bounds.Bottom - 44f, Bounds.Width, 20f);

            if (e.Button == MouseButtons.Left && rect.Contains(e.CanvasLocation))
            {
                ((WbClassificationMigration)Owner).StartMigration();
                return GH_ObjectResponse.Handled;
            }
            if (e.Button == MouseButtons.Left && pauseRect.Contains(e.CanvasLocation))
            {
                var component = (WbClassificationMigration)Owner;
                if (component.IsPaused) component.ResumeMigration();
                else component.PauseMigration();
                sender.Invalidate();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }
    }

    /// <summary>创建分类库迁移_WB 配套的 SQLite 空数据库。</summary>
    public sealed class WbCreateMigrationDatabase : GH_Component
    {
        private bool _createRequested;
        private string _lastPath = string.Empty;

        public WbCreateMigrationDatabase()
            : base("创建分类迁移数据库（WB）", "建迁移库_WB",
                  "创建供\"分类库迁移_WB\"使用的空 SQLite 数据库。",
                  "Parrot", "Library") { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("文件夹", "文件夹", "新版数据库所在文件夹。", GH_ParamAccess.item);
            p.AddTextParameter("数据库名称", "名称", "数据库文件名；未填写扩展名时自动添加 .db。", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("新版数据库地址", "数据库", "创建后的 WB 数据库完整地址。", GH_ParamAccess.item);
            p.AddTextParameter("状态", "状态", "创建状态。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string folder = string.Empty, name = string.Empty;
            if (!da.GetData(0, ref folder) || !da.GetData(1, ref name)) return;
            if (!_createRequested)
            {
                da.SetData(0, _lastPath);
                da.SetData(1, "右键点击\"创建新版数据库\"后执行。");
                return;
            }
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("文件夹和数据库名称不能为空。");
                string fileName = name.Trim();
                if (!string.Equals(Path.GetFileName(fileName), fileName,
                    StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("数据库名称只能是文件名，不能包含路径。");
                if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                    fileName += ".db";
                string path = Path.GetFullPath(
                    Path.Combine(Path.GetFullPath(folder), fileName));
                if (File.Exists(path))
                    throw new InvalidOperationException(
                        "目标数据库已存在；请使用新名称，或在迁移电池中重建该库。");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                SQLiteConnection.CreateFile(path);
                WbMigrationStore.CreateEmpty(path);
                _lastPath = path;
                da.SetData(0, path);
                da.SetData(1, "已创建 WB 分类迁移数据库。");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                da.SetData(0, _lastPath);
                da.SetData(1, "错误：" + ex.Message);
            }
            finally { _createRequested = false; }
        }

        protected override void AppendAdditionalComponentMenuItems(
            ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "创建新版数据库", (s, e) =>
            {
                _createRequested = true;
                ExpireSolution(true);
            });
        }

        public override Guid ComponentGuid =>
            new Guid("C8E4A2F1-7D3B-4E5C-91A6-2F8B0D3E4F1A");
    }
}
