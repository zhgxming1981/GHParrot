// ========================================================================
// WbClassificationCore — 分类库核心（WbClassificationMigration 与 WbEntityClassifier 共用）
//
// 包含类：WbUtil, WbHole, WbFeature, WbMigrationRecord,
//        WbMigrationAudit, WbMigrationStore, WbClassificationSettings
// ========================================================================
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GH_IO.Serialization;
using Rhino;
using Rhino.Geometry;

namespace NS_Parrot
{
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
        public int FaceIndex = -1;   // 来源面索引（诊断用；-1=未知，如反序列化旧数据）
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
            double inertiaDegenerateRatio,
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
                Area = area.Area,
                Volume = volume == null ? 0 : volume.Volume,
                Inertia = inertia,
                Centroid = center,
                LocalOrigin = box.Center,
                Axes = axes.Select(x => x).ToArray()
            };

            // 一次遍历提取孔 + 外轮廓
            ExtractFeatures(brep, center, axes, index, fallbackTolerance,
                out f.Holes, out f.OuterProfiles, out f.UsedFallbackTolerance);

            f.MirrorScore = ComputeMirrorScore(faceCentroids, center, axes);
            f.HoleMirrorScore = ComputeHoleMirrorScore(f.Holes);

            // 惯量退化检测：相邻主惯量比值 > 99% 则标记，后续匹配将遍历全部轴排列
            f.IsDegenerate =
                (inertia[0] > 1e-10 && inertia[1] / inertia[0] > inertiaDegenerateRatio) ||
                (inertia[1] > 1e-10 && inertia[2] / inertia[1] > inertiaDegenerateRatio);

            return f;
        }

        // ================================================================
        // 串行化
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
                WbUtil.FR(p.NU) + "," + WbUtil.FR(p.NV) + "," + WbUtil.FR(p.NW) + "," +
                p.FaceIndex.ToString(CultureInfo.InvariantCulture)));

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
                            NW = double.Parse(pv[4], CultureInfo.InvariantCulture),
                            FaceIndex = pv.Length >= 6
                                ? int.Parse(pv[5], CultureInfo.InvariantCulture)
                                : -1
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
            for (int i = 0; i < OuterProfiles.Count; i++)
            {
                WbOuterProfile p = OuterProfiles[i];
                yield return "外轮廓[" + i.ToString(CultureInfo.InvariantCulture) +
                    "] FaceIndex=" + p.FaceIndex.ToString(CultureInfo.InvariantCulture) +
                    "；面积=" + WbUtil.FR(p.Area) +
                    "；周长=" + WbUtil.FR(p.Perimeter) +
                    "；法向=(" + WbUtil.FR(p.NU) + "," + WbUtil.FR(p.NV) + "," + WbUtil.FR(p.NW) + ")" +
                    "；有效=" + p.IsAvailable;
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

        internal static double ComputeHoleMirrorScore(List<WbHole> holes)
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
            int index,
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
                        boundary.Perimeter,
                        holeArea,
                        position * axes[0],
                        position * axes[1],
                        position * axes[2],
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
                        NW = normal * axes[2],
                        FaceIndex = face.FaceIndex
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
            // RMS 距离 = sqrt(trace / n)；若小于 RhinoMath.ZeroTolerance（固定 1e-12 数学零常量），
            // 点集在数值上近共点，无法拟合有效平面。此为数学零判断，非模型制造容差。
            // 不再使用固定 1e-20 绝对阈值，避免微小但有效的退化孔被误拒
            double rmsDist = Math.Sqrt(trace / points.Count);
            if (rmsDist <= RhinoMath.ZeroTolerance) return;

            // 3x3 Jacobi 特征分解：协方差矩阵最小特征值对应平面法向
            // 替代幂迭代以消除单初值/收敛不稳定问题，保留固定 64 采样点以保证可复现
            double[,] cov = new double[3, 3]
            {
                { cxx, cxy, cxz },
                { cxy, cyy, cyz },
                { cxz, cyz, czz }
            };
            JacobiEigenSolver.SolveSymmetric(cov, out double[] eigenValues, out Vector3d[] eigenVectors);
            int minIdx = 0;
            if (eigenValues[1] < eigenValues[minIdx]) minIdx = 1;
            if (eigenValues[2] < eigenValues[minIdx]) minIdx = 2;
            Vector3d normal = eigenVectors[minIdx];

            // 建立平面二维基
            Vector3d uBasis = Math.Abs(normal.Z) < 0.9
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
        public List<string> EmptyFiles = new List<string>();

        private Dictionary<V5Criterion, bool> _enabled;
        private Dictionary<V5Criterion, double> _tolerances;
        private Dictionary<V5Criterion, bool> _isRel;
        private bool _strictMaterialColor;
        private double _holeNormalTolerance;
        private bool _enableDegenerateMatching;
        private double _degeneratePositionMultiplier;
        private double _outerProfileAreaRatio;
        private double _outerProfilePerimeterMin;
        private double _fallbackTolerance;

        public static WbMigrationAudit Create(
            List<WbMigrationRecord> records,
            Dictionary<V5Criterion, bool> enabled,
            Dictionary<V5Criterion, double> tolerances,
            Dictionary<V5Criterion, bool> isRel,
            bool strictMaterialColor,
            double holeNormalTolerance,
            double outerProfileAreaRatio = 0.0001,
            double outerProfilePerimeterMin = 0.01,
            double degeneratePositionMultiplier = 2.0,
            bool enableDegenerateMatching = false,
            double fallbackTolerance = 0.001)
        {
            var audit = new WbMigrationAudit
            {
                _enabled = enabled,
                _tolerances = tolerances,
                _isRel = isRel,
                _strictMaterialColor = strictMaterialColor,
                _holeNormalTolerance = holeNormalTolerance,
                _enableDegenerateMatching = enableDegenerateMatching,
                _degeneratePositionMultiplier = degeneratePositionMultiplier,
                _outerProfileAreaRatio = outerProfileAreaRatio,
                _outerProfilePerimeterMin = outerProfilePerimeterMin,
                _fallbackTolerance = fallbackTolerance
            };

            // 按来源文件+编号+索引排序，确保可复现性
            records = records.OrderBy(r => r.SourceFile ?? string.Empty)
                             .ThenBy(r => r.Number ?? string.Empty)
                             .ThenBy(r => r.Index).ToList();

            // 含退化孔记录不再隔离：统一参与自动聚类；DegenerateHoleRecords 仅用于日志统计
            var normalRecords = new List<WbMigrationRecord>();
            foreach (var r in records)
            {
                normalRecords.Add(r);
                if (r.Feature.HasDegenerateHoles)
                    audit.DegenerateHoleRecords.Add(r);
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
                        enabled, tolerances, isRel, strictMaterialColor, holeNormalTolerance,
                        outerProfileAreaRatio, outerProfilePerimeterMin,
                        degeneratePositionMultiplier, enableDegenerateMatching,
                        fallbackTolerance: fallbackTolerance);
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

            // 检测同件异号
            foreach (var group in audit.Classes)
            {
                var numbers = group.Select(r => r.Number)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(num => num).ToList();
                if (numbers.Count > 1)
                    audit.SamePartDifferentNumbers.Add(
                        "同件异号：" + string.Join("、", numbers) +
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
                var ambiguityDetails = audit.AmbiguityGroups
                    .Where(g => g.Any(r => string.Equals(r.Number, group.Key,
                        StringComparison.OrdinalIgnoreCase)))
                    .Select(g => "(" + string.Join(",",
                        g.Where(r => string.Equals(r.Number, group.Key,
                            StringComparison.OrdinalIgnoreCase))
                         .OrderBy(r => r.Index)
                         .Select(r => (r.Index + 1).ToString(CultureInfo.InvariantCulture))) + ")")
                    .ToList();
                if (classCount > 1 || ambiguityDetails.Count > 0)
                {
                    var mismatchedClasses = audit.Classes
                        .Where(c => c.Any(r => string.Equals(r.Number, group.Key,
                            StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    var classDetails = mismatchedClasses
                        .Select(c => "(" + string.Join(",",
                            c.Where(r => string.Equals(r.Number, group.Key,
                                StringComparison.OrdinalIgnoreCase))
                             .OrderBy(r => r.Index)
                             .Select(r => r.Index.ToString(CultureInfo.InvariantCulture))) + ")");
                    string detail = "异件同号：" + group.Key +
                        "；" + (classCount > 0
                            ? "分为 " + classCount.ToString(CultureInfo.InvariantCulture) + " 类" +
                              "；分类明细：" + string.Join(" ", classDetails)
                            : "未形成已确认类别");
                    if (ambiguityDetails.Count > 0)
                        detail += "；歧义待复核：" + string.Join(" ", ambiguityDetails);

                    // 诊断失败判据：从各分类取代表记录，两两比较收集未确认原因
                    if (mismatchedClasses.Count > 1)
                    {
                        var mismatchReasons = new HashSet<string>(StringComparer.Ordinal);
                        for (int ci = 0; ci < mismatchedClasses.Count; ci++)
                        for (int cj = ci + 1; cj < mismatchedClasses.Count; cj++)
                        {
                            var repA = mismatchedClasses[ci].OrderBy(r => r.Index).First();
                            var repB = mismatchedClasses[cj].OrderBy(r => r.Index).First();
                            foreach (string reason in DiagnoseFeatureMismatch(
                                repA.Feature, repB.Feature,
                                enabled, tolerances, isRel, strictMaterialColor, holeNormalTolerance,
                                degeneratePositionMultiplier, enableDegenerateMatching))
                            {
                                mismatchReasons.Add(reason);
                            }
                        }
                        foreach (string reason in mismatchReasons)
                            detail += "；" + reason;
                    }

                    audit.DifferentPartsSameNumber.Add(detail);
                }
            }

            // 检测镜像零件
            for (int left = 0; left < audit.Classes.Count; left++)
            for (int right = left + 1; right < audit.Classes.Count; right++)
            {
                WbMigrationRecord a = audit.Classes[left].OrderBy(r => r.Index).First();
                WbMigrationRecord b = audit.Classes[right].OrderBy(r => r.Index).First();
                if (MirrorMatches(a.Feature, b.Feature, enabled, tolerances, isRel, strictMaterialColor, holeNormalTolerance,
                    outerProfileAreaRatio, outerProfilePerimeterMin,
                    fallbackTolerance: fallbackTolerance))
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
                {
                    string prefix = kept.Feature.HasDegenerateHoles || r.Feature.HasDegenerateHoles
                        ? "含退化孔：未在当前特征与容差下确认同件，待复核："
                        : "异件同号：";
                    string detail = prefix + r.Number +
                        "；保留来源=" + kept.SourceFile +
                        "；待复核来源=" + r.SourceFile;

                    // 诊断失败判据
                    var mismatchReasons = DiagnoseFeatureMismatch(
                        kept.Feature, r.Feature,
                        _enabled, _tolerances, _isRel, _strictMaterialColor, _holeNormalTolerance,
                        _degeneratePositionMultiplier, _enableDegenerateMatching);
                    foreach (string reason in mismatchReasons)
                        detail += "；" + reason;

                    ExcludedNumberConflicts.Add(detail);
                }
            }
            return result;
        }

        public IEnumerable<string> ToLogs()
        {
            if (!_strictMaterialColor)
                yield return "注意：材质/颜色宽松模式——任一方为空即视为匹配，未参与严格确认。";
            if (_enableDegenerateMatching)
                yield return "注意：退化孔容差已放宽（×" +
                    _degeneratePositionMultiplier.ToString("0.##", CultureInfo.InvariantCulture) +
                    "）——仅影响退化孔配对容差，不控制聚类参与。";
            yield return "共识别 " + Classes.Count.ToString(CultureInfo.InvariantCulture) +
                " 个实际零件类（每类以输入顺序首件为代表，特征保存全精度；后续件在容差内不再重复入库）。";
            if (DegenerateHoleRecords.Count > 0)
            {
                yield return "发现 " + DegenerateHoleRecords.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个含退化孔（非平面内环）记录——已参与自动聚类：" +
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
                yield return "未发现编号相同但实际不同的零件。";
            else foreach (string x in DifferentPartsSameNumber) yield return x;
            foreach (string x in MirrorParts) yield return x;
            foreach (string x in ExcludedNumberConflicts) yield return x;
            if (EmptyFiles.Count > 0)
            {
                yield return "以下文件未包含有效零件，已跳过（共 " +
                    EmptyFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个）：";
                foreach (string f in EmptyFiles)
                    yield return "  " + f;
            }
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
                yield return new KeyValuePair<string, string>("待复核异件同号", x);
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
                    // 外轮廓对比诊断（外轮廓已不参与匹配，以下仅作参考。
                    // 注意：按提取顺序逐项配对，不同模型的面遍历顺序可能错位；
                    // "兼容=否"不一定代表几何差异，可对照 FaceIndex 核实。）
                    int profileCount = Math.Min(a.Feature.OuterProfiles.Count, b.Feature.OuterProfiles.Count);
                    for (int pi = 0; pi < profileCount; pi++)
                    {
                        WbOuterProfile pa = a.Feature.OuterProfiles[pi];
                        WbOuterProfile pb = b.Feature.OuterProfiles[pi];
                        double dot = Math.Abs(pa.NU * pb.NU * bestSign[0] +
                                              pa.NV * pb.NV * bestSign[1] +
                                              pa.NW * pb.NW * bestSign[2]);
                        bool compatible = OuterProfileCompatible(pa, pb, bestSign,
                            _tolerances[V5Criterion.HolePosition], _holeNormalTolerance,
                            _outerProfileAreaRatio, _outerProfilePerimeterMin, _fallbackTolerance);
                        yield return "外轮廓[" + pi.ToString(CultureInfo.InvariantCulture) +
                            "] FaceIndex A/B=" + pa.FaceIndex.ToString(CultureInfo.InvariantCulture) +
                            "/" + pb.FaceIndex.ToString(CultureInfo.InvariantCulture) +
                            "；面积=" + WbUtil.FR(pa.Area) + "/" + WbUtil.FR(pb.Area) +
                            "；周长=" + WbUtil.FR(pa.Perimeter) + "/" + WbUtil.FR(pb.Perimeter) +
                            "；法向点积=" + WbUtil.FD(dot) +
                            "；兼容=" + (compatible ? "是" : "否");
                    }
                    if (a.Feature.OuterProfiles.Count != b.Feature.OuterProfiles.Count)
                        yield return "外轮廓计数不一致：A=" +
                            a.Feature.OuterProfiles.Count.ToString(CultureInfo.InvariantCulture) +
                            "，B=" + b.Feature.OuterProfiles.Count.ToString(CultureInfo.InvariantCulture) +
                            "（计数不稳定时可在此处观察到）";
                }
                clsIdx++;
            }
        }

        // ================================================================
        // 匹配逻辑
        // ================================================================
        internal static bool Matches(
            WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, bool strictMaterialColor, double normalTol,
            double outerAreaRatio, double outerPerimMin,
            double degenMult = 2.0, bool enableDegenMatch = false,
            double fallbackTolerance = 0.001)
        {
            return GlobalMatches(a, b, e, t, isRel, strictMaterialColor) &&
                   GeometryMatches(a, b, e, t, isRel, false, normalTol, outerAreaRatio, outerPerimMin, degenMult, enableDegenMatch, fallbackTolerance);
        }

        internal static bool MirrorMatches(
            WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, bool strictMaterialColor, double normalTol,
            double outerAreaRatio, double outerPerimMin,
            double degenMult = 2.0, bool enableDegenMatch = false,
            double fallbackTolerance = 0.001)
        {
            if (!GlobalMatches(a, b, e, t, isRel, strictMaterialColor))
                return false;
            // 非镜像变换下已匹配 → 本就是同件，不应再标为镜像关系
            if (GeometryMatches(a, b, e, t, isRel, false, normalTol, outerAreaRatio, outerPerimMin, degenMult, enableDegenMatch, fallbackTolerance))
                return false;
            return GeometryMatches(a, b, e, t, isRel, true, normalTol, outerAreaRatio, outerPerimMin, degenMult, enableDegenMatch, fallbackTolerance);
        }

        internal static bool GlobalMatches(
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
        /// <summary>
        /// 诊断两特征未匹配的原因，返回人类可读的失败判据列表。
        /// 判据按尺寸→几何→诊断的顺序输出。
        /// 若两特征实际匹配则返回空列表。
        /// </summary>
        private static List<string> DiagnoseFeatureMismatch(
            WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> enabled,
            Dictionary<V5Criterion, double> tolerances,
            Dictionary<V5Criterion, bool> isRel,
            bool strictMaterialColor,
            double holeNormalTolerance,
            double degenMult = 2.0,
            bool enableDegenMatch = false)
        {
            var reasons = new List<string>();

            // 1. 材质/颜色
            if (!WbUtil.SameOptionalText(a.Material, b.Material, strictMaterialColor) ||
                !WbUtil.SameOptionalText(a.Color, b.Color, strictMaterialColor))
            {
                reasons.Add("原因：材质或颜色不同");
            }

            // 2. 包围盒各维度独立检查（不短路）
            if (a.BoundingBox != null && b.BoundingBox != null &&
                a.BoundingBox.Length == 3 && b.BoundingBox.Length == 3)
            {
                double bbTol = tolerances[V5Criterion.Length];
                bool bbRel = isRel[V5Criterion.Length];
                bool bbFailed = false;
                for (int i = 0; i < 3 && !bbFailed; i++)
                {
                    if (!WbUtil.WithinTolerance(a.BoundingBox[i], b.BoundingBox[i], bbTol, bbRel))
                        bbFailed = true;
                }
                if (bbFailed)
                    reasons.Add("原因：包围盒长度不同");
            }

            // 3. 体积或惯量
            bool volumeOrInertiaFailed = false;
            if (enabled[V5Criterion.Volume] &&
                !WbUtil.WithinTolerance(a.Volume, b.Volume,
                    tolerances[V5Criterion.Volume], isRel[V5Criterion.Volume]))
            {
                volumeOrInertiaFailed = true;
            }
            if (!volumeOrInertiaFailed && enabled[V5Criterion.Inertia])
            {
                double inertiaTol = tolerances[V5Criterion.Inertia];
                bool inertiaRel = isRel[V5Criterion.Inertia];
                if (!a.Inertia.Zip(b.Inertia,
                    (x, y) => WbUtil.WithinTolerance(x, y, inertiaTol, inertiaRel)).All(x => x))
                {
                    volumeOrInertiaFailed = true;
                }
            }
            if (volumeOrInertiaFailed)
                reasons.Add("原因：体积或惯量超出容差");

            // 4. 孔匹配
            bool aDeg = a.HasDegenerateHoles;
            bool bDeg = b.HasDegenerateHoles;
            if (aDeg != bDeg)
            {
                reasons.Add("原因：退化孔状态不一致");
            }

            if (a.Holes.Count != b.Holes.Count)
            {
                reasons.Add("原因：孔数不同（" +
                    a.Holes.Count.ToString(CultureInfo.InvariantCulture) + " vs " +
                    b.Holes.Count.ToString(CultureInfo.InvariantCulture) + "）");
            }
            else if (aDeg == bDeg)
            {
                bool holeMatch = false;
                if (a.IsDegenerate || b.IsDegenerate)
                {
                    foreach (int[] perm in AxisPermutations)
                    {
                        if (holeMatch) break;
                        foreach (double[] sign in NonReflectionSigns)
                        {
                            var bRemapped = RemapHoles(b.Holes, perm, sign);
                            if (PerfectAssignment(a.Holes, bRemapped, IdentitySign,
                                enabled, tolerances, isRel, holeNormalTolerance,
                                degenMult, enableDegenMatch))
                            { holeMatch = true; break; }
                        }
                    }
                }
                if (!holeMatch)
                {
                    foreach (double[] sign in NonReflectionSigns)
                    {
                        if (PerfectAssignment(a.Holes, b.Holes, sign,
                            enabled, tolerances, isRel, holeNormalTolerance,
                            degenMult, enableDegenMatch))
                        { holeMatch = true; break; }
                    }
                }
                if (!holeMatch)
                    reasons.Add("原因：孔特征无法完整匹配");
            }

            // 5. 外轮廓计数（仅诊断，不参与同件判定）
            if (a.OuterProfiles.Count != b.OuterProfiles.Count)
            {
                reasons.Add("外轮廓数 " +
                    a.OuterProfiles.Count.ToString(CultureInfo.InvariantCulture) + " vs " +
                    b.OuterProfiles.Count.ToString(CultureInfo.InvariantCulture) +
                    "（仅诊断，不参与同件判定）");
            }

            // 兜底：各项判据均通过但不在一类（歧义组导致）
            if (reasons.Count == 0)
                reasons.Add("原因：容差不传递（详见诊断日志）");

            return reasons;
        }

        /// <summary>将 b 侧孔坐标按轴排列+符号重新映射（退化路径用）。</summary>
        private static List<WbHole> RemapHoles(
            List<WbHole> holes, int[] perm, double[] sign)
        {
            return holes.Select(h =>
            {
                double[] coords = { h.U, h.V, h.W };
                double[] norms = { h.NU, h.NV, h.NW };
                double[] degenNorms = { h.DegenerateNormalU, h.DegenerateNormalV, h.DegenerateNormalW };
                return new WbHole(
                    h.Perimeter, h.Area,
                    coords[perm[0]] * sign[0],
                    coords[perm[1]] * sign[1],
                    coords[perm[2]] * sign[2],
                    norms[perm[0]] * sign[0],
                    norms[perm[1]] * sign[1],
                    norms[perm[2]] * sign[2],
                    h.X, h.Y, h.Z,
                    h.CenterMode,
                    h.IsDegenerate, h.DegenerateType)
                {
                    DegenerateProjectedArea = h.DegenerateProjectedArea,
                    DegenerateCircularity = h.DegenerateCircularity,
                    DegenerateAspectRatio = h.DegenerateAspectRatio,
                    DegenerateNormalU = degenNorms[perm[0]] * sign[0],
                    DegenerateNormalV = degenNorms[perm[1]] * sign[1],
                    DegenerateNormalW = degenNorms[perm[2]] * sign[2]
                };
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
                    NW = norms[perm[2]] * sign[2],
                    FaceIndex = p.FaceIndex
                };
            }).ToList();
        }

        internal static bool GeometryMatches(
            WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, bool reflection, double normalTol,
            double outerAreaRatio, double outerPerimMin,
            double degenMult = 2.0, bool enableDegenMatch = false,
            double fallbackTolerance = 0.001)
        {
            // 保留参数 outerAreaRatio/outerPerimMin/fallbackTolerance：
            // 外轮廓已不参与匹配判定，以上参数仅用于诊断，保留以兼容调用方。

            if (!CanAutoMatch(a, b, enableDegenMatch))
                return false;

            if (a.Holes.Count != b.Holes.Count) return false;
            double[][] signs = reflection ? ReflectionSigns : NonReflectionSigns;

            // 退化路径（惯量退化）：遍历全部 6 种轴排列 × N 种符号
            if (a.IsDegenerate || b.IsDegenerate)
            {
                foreach (int[] perm in AxisPermutations)
                foreach (double[] sign in signs)
                {
                    var bHolesRemapped = RemapHoles(b.Holes, perm, sign);

                    if (!PerfectAssignment(a.Holes, bHolesRemapped, IdentitySign, e, t, isRel, normalTol, degenMult, enableDegenMatch))
                        continue;
                    return true;
                }
                return false;
            }

            // 正常路径：非退化零件只遍历符号
            foreach (double[] sign in signs)
            {
                if (!PerfectAssignment(a.Holes, b.Holes, sign, e, t, isRel, normalTol, degenMult, enableDegenMatch))
                    continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 判断两特征是否允许进入自动匹配。
        /// 一侧退化孔一侧正常孔 → 禁止；双方均退化孔或均正常 → 允许。
        /// 退化孔记录始终参与聚类，参数 enableDegenerateMatching 仅控制
        /// HoleCompatible 中退化孔配对容差是否放宽（保留参数以兼容调用方）。
        /// </summary>
        internal static bool CanAutoMatch(WbFeature a, WbFeature b, bool enableDegenerateMatching)
        {
            bool aDeg = a.HasDegenerateHoles;
            bool bDeg = b.HasDegenerateHoles;
            return aDeg == bDeg;
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
            double outerAreaRatio, double outerPerimMin, double fallbackTolerance)
        {
            if (a.Count == 0 && b.Count == 0) return true;
            if (a.Count != b.Count) return false;
            return WbUtil.TryBipartiteMatch(a, b,
                (left, right) => OuterProfileCompatible(left, right, sign, tolerance, normalTol, outerAreaRatio, outerPerimMin, fallbackTolerance));
        }

        private static bool OuterProfileCompatible(
            WbOuterProfile a, WbOuterProfile b, double[] sign, double tolerance, double normalTol,
            double outerAreaRatio, double outerPerimMin, double fallbackTolerance)
        {
            if (!a.IsAvailable || !b.IsAvailable) return false;
            // 面积 + 周长 + 法向一致性判别
            double areaFloor = fallbackTolerance * fallbackTolerance;
            if (Math.Abs(a.Area - b.Area) >
                Math.Max(Math.Max(a.Area, b.Area), areaFloor) * outerAreaRatio ||
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
            // 禁止退化孔与正常孔成为一对
            if (a.IsDegenerate != b.IsDegenerate)
                return false;

            // 退化孔配对始终使用拟合平面特征；开关仅控制容差是否放宽
            bool bothDegenerate = a.IsDegenerate && b.IsDegenerate;
            bool relax = enableDegenMatch && bothDegenerate;

            if (e[V5Criterion.EdgeSum])
            {
                double edgeTol = relax
                    ? t[V5Criterion.EdgeSum] * degenMult  // 容差放宽：周长容差 × 倍数
                    : t[V5Criterion.EdgeSum];
                if (!WbUtil.WithinTolerance(a.Perimeter, b.Perimeter, edgeTol,
                    isRel[V5Criterion.EdgeSum]))
                    return false;
            }
            if (e[V5Criterion.HoleArea])
            {
                double areaA = bothDegenerate ? a.DegenerateProjectedArea : a.Area;
                double areaB = bothDegenerate ? b.DegenerateProjectedArea : b.Area;
                double areaTol = relax
                    ? Math.Max(t[V5Criterion.HoleArea] * degenMult, 5.0)  // 容差放宽：最低面积容差 5%
                    : t[V5Criterion.HoleArea];
                if (!WbUtil.WithinTolerance(areaA, areaB, areaTol,
                    isRel[V5Criterion.HoleArea]))
                    return false;
            }

            if (e[V5Criterion.HolePosition])
            {
                double tol = t[V5Criterion.HolePosition];
                if (relax && (a.IsDegenerate || b.IsDegenerate)) tol *= degenMult;
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
                return dot >= (relax ? normalTol * 0.95 : normalTol);
            }

            // 正常孔法向路径
            double aLen2 = Math.Sqrt(a.NU * a.NU + a.NV * a.NV + a.NW * a.NW);
            double bLen2 = Math.Sqrt(b.NU * b.NU + b.NV * b.NV + b.NW * b.NW);
            if (aLen2 <= RhinoMath.ZeroTolerance || bLen2 <= RhinoMath.ZeroTolerance)
                return true;

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

    }

    // ========================================================================
    // 检查点：断点续跑支持
    // ========================================================================

    /// <summary>迁移阶段，定义在命名空间级别供 WbMigrationCheckpoint 和 WbClassificationMigration 共用。</summary>
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
            // 不抛异常的重建
            Rebuild(path, new List<WbMigrationRecord>(), new WbMigrationAudit(),
                enabled, tolerances, isRel, 0.99, 0.9986, true, creatingEmpty: true);
        }

        public static (List<long> insertedIds, List<WbMigrationRecord> insertedRecords, int skippedCount) Rebuild(
            string path, List<WbMigrationRecord> records, WbMigrationAudit audit,
            Dictionary<V5Criterion, bool> enabled,
            Dictionary<V5Criterion, double> tolerances,
            Dictionary<V5Criterion, bool> isRel,
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

            var insertedIds = new List<long>();
            var insertedRecords = new List<WbMigrationRecord>();
            int skippedCount = 0;

            using (var c = new SQLiteConnection("Data Source=" + full + ";Version=3;"))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText =
                        "PRAGMA foreign_keys=ON;" +
                        "CREATE TABLE IF NOT EXISTS database_info (" +
                            "id INTEGER PRIMARY KEY CHECK(id=1)," +
                            "schema_version INTEGER NOT NULL," +
                            "algorithm_version TEXT NOT NULL," +
                            "created_time TEXT NOT NULL);" +
                        "CREATE TABLE IF NOT EXISTS classification_profile (" +
                            "profile_id INTEGER PRIMARY KEY," +
                            "settings_text TEXT NOT NULL);" +
                        "CREATE TABLE IF NOT EXISTS parts (" +
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
                        "CREATE TABLE IF NOT EXISTS holes (" +
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
                        "CREATE TABLE IF NOT EXISTS outer_profiles (" +
                            "part_id INTEGER NOT NULL," +
                            "profile_index INTEGER NOT NULL," +
                            "enclosed_area REAL NOT NULL," +
                            "perimeter REAL NOT NULL," +
                            "normal_u REAL NOT NULL," +
                            "normal_v REAL NOT NULL," +
                            "normal_w REAL NOT NULL," +
                            "FOREIGN KEY(part_id) REFERENCES parts(part_id) ON DELETE CASCADE," +
                            "UNIQUE(part_id,profile_index));" +
                        "CREATE TABLE IF NOT EXISTS part_features (" +
                            "part_id INTEGER PRIMARY KEY, " +
                            "feature_text TEXT NOT NULL);" +
                        "CREATE TABLE IF NOT EXISTS migration_runs (" +
                            "run_id INTEGER PRIMARY KEY AUTOINCREMENT," +
                            "start_time TEXT NOT NULL," +
                            "source_count INTEGER NOT NULL," +
                            "inserted_count INTEGER NOT NULL," +
                            "completed INTEGER NOT NULL);" +
                        "CREATE TABLE IF NOT EXISTS migration_audit (" +
                            "audit_id INTEGER PRIMARY KEY AUTOINCREMENT," +
                            "issue_type TEXT NOT NULL," +
                            "detail TEXT NOT NULL);";
                    cmd.ExecuteNonQuery();

                    using (var tx = c.BeginTransaction())
                    {
                        string now = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                        string settings = SettingsText(enabled, tolerances, isRel,
                            inertiaDegenerateRatio, holeNormalTolerance, strictMaterialColor,
                            outerProfileAreaRatio, outerProfilePerimeterMin, degeneratePositionMultiplier,
                            enableDegenerateMatching, fallbackTolerance);

                        cmd.CommandText =
                            "INSERT OR IGNORE INTO database_info (id,schema_version,algorithm_version,created_time) " +
                            "VALUES (1,9,'WbClassificationMigration_v9',@time);" +
                            // 用 UPDATE + 条件 INSERT 代替 REPLACE（避免外键引用下 DELETE +INSERT 触发约束失败）
                            "UPDATE classification_profile SET settings_text=@settings WHERE profile_id=1;" +
                            "INSERT INTO classification_profile(profile_id,settings_text) " +
                            "SELECT 1,@settings WHERE changes()=0;";
                        cmd.Parameters.AddWithValue("@time", now);
                        cmd.Parameters.AddWithValue("@settings", settings);
                        cmd.ExecuteNonQuery();

                        // 查询已有零件（编号 + 特征文本），用于跨批次四类去重判定
                        var existingNumberToFeature = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        cmd.CommandText = "SELECT p.part_number, pf.feature_text FROM parts p JOIN part_features pf ON p.part_id = pf.part_id;";
                        using (var featureReader = cmd.ExecuteReader())
                        {
                            while (featureReader.Read())
                            {
                                string num = featureReader.GetString(0);
                                if (!existingNumberToFeature.ContainsKey(num))
                                    existingNumberToFeature[num] = featureReader.GetString(1);
                            }
                        }

                        // 反序列化已有特征（用于特征比对）
                        var existingFeatures = new List<(string number, WbFeature feature)>();
                        foreach (var kv in existingNumberToFeature)
                        {
                            try
                            {
                                existingFeatures.Add((kv.Key, WbFeature.Deserialize(kv.Value)));
                            }
                            catch { /* 跳过损坏的特征记录 */ }
                        }

                        // 四类判定插入：同号同特征→跳过 / 同号异特征→冲突隔离 /
                        //   异号同特征→审计但不阻止入库 / 异号异特征→正常插入
                        var dedupAudit = new List<KeyValuePair<string, string>>();
                        foreach (WbMigrationRecord r in records)
                        {
                            // 1. 查找同号已有记录
                            var sameNumber = existingFeatures.Where(ef =>
                                string.Equals(ef.number, r.Number, StringComparison.OrdinalIgnoreCase)).ToList();

                            if (sameNumber.Count > 0)
                            {
                                // 同号 → 比对特征
                                bool sameFeatures = sameNumber.Any(ef =>
                                    WbMigrationAudit.Matches(
                                        r.Feature, ef.feature,
                                        enabled, tolerances, isRel,
                                        strictMaterialColor, holeNormalTolerance,
                                        outerProfileAreaRatio, outerProfilePerimeterMin,
                                        degeneratePositionMultiplier, enableDegenerateMatching,
                                        fallbackTolerance: fallbackTolerance));

                                if (sameFeatures)
                                {
                                    // 同号同特征 → 已入库，跳过
                                    skippedCount++;
                                    continue;
                                }
                                else
                                {
                                    // 同号异特征 → 编号冲突，隔离不入库
                                    dedupAudit.Add(new KeyValuePair<string, string>(
                                        "同号异特征",
                                        string.Format(CultureInfo.InvariantCulture,
                                            "编号 {0} 已存在但几何特征不同，新记录未入库（来源：{1}）。",
                                            r.Number, r.SourceFile)));
                                    skippedCount++;
                                    continue;
                                }
                            }

                            // 2. 异号 → 比对是否与已有记录特征相同
                            var sameFeatureExisting = existingFeatures.Where(ef =>
                                WbMigrationAudit.Matches(
                                    r.Feature, ef.feature,
                                    enabled, tolerances, isRel,
                                    strictMaterialColor, holeNormalTolerance,
                                    outerProfileAreaRatio, outerProfilePerimeterMin,
                                    degeneratePositionMultiplier, enableDegenerateMatching,
                                    fallbackTolerance: fallbackTolerance)).ToList();

                            foreach (var ef in sameFeatureExisting)
                            {
                                dedupAudit.Add(new KeyValuePair<string, string>(
                                    "异号同特征",
                                    string.Format(CultureInfo.InvariantCulture,
                                        "编号 {0} 与已有编号 {1} 特征匹配，疑似同件异号（来源：{2}）。",
                                        r.Number, ef.number, r.SourceFile)));
                            }

                            // 异号异特征（或异号同特征但需入库）→ 正常插入
                            long id = InsertPart(c, cmd, r);
                            cmd.CommandText = "INSERT OR REPLACE INTO part_features (part_id,feature_text) VALUES (@fid,@ftext);";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@fid", id);
                            cmd.Parameters.AddWithValue("@ftext", r.Feature.Serialize());
                            cmd.ExecuteNonQuery();
                            insertedIds.Add(id);
                            insertedRecords.Add(r);

                            // 加入已有列表，防止同批次同号重复写入
                            existingFeatures.Add((r.Number, r.Feature));
                        }

                        cmd.CommandText =
                            "INSERT INTO migration_runs (start_time,source_count,inserted_count,completed) " +
                            "VALUES (@time,@source,@inserted,1);";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@time", now);
                        cmd.Parameters.AddWithValue("@source",
                            audit.Classes.Sum(cls => cls.Count));
                        cmd.Parameters.AddWithValue("@inserted", insertedRecords.Count);
                        cmd.ExecuteNonQuery();

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
                        // 写入跨批次去重审计
                        foreach (var row in dedupAudit)
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

            return (insertedIds, insertedRecords, skippedCount);
        }

        internal static long InsertPart(
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

            return id;
        }

        public static void Verify(string path, List<WbMigrationRecord> insertedRecords, List<long> insertedIds)
        {
            if (insertedRecords.Count != insertedIds.Count)
                throw new InvalidOperationException("插入记录数与ID数不一致。");
            if (insertedRecords.Count == 0) return;

            using (var c = new SQLiteConnection(
                "Data Source=" + Path.GetFullPath(path) + ";Version=3;"))
            {
                c.Open();

                // 校核零件主表（按 part_id 查询，仅校核本轮插入的记录）
                for (int i = 0; i < insertedRecords.Count; i++)
                using (var cmd = c.CreateCommand())
                {
                    WbMigrationRecord r = insertedRecords[i];
                    double[] box = r.Feature.BoundingBox;
                    cmd.CommandText =
                        "SELECT part_number,source_index,source_file," +
                        "volume,surface_area,bbox_max,bbox_mid,bbox_min,bbox_count," +
                        "inertia_1,inertia_2,inertia_3,hole_count,hole_perimeter_sum,hole_area_sum " +
                        "FROM parts WHERE part_id=@id;";
                    cmd.Parameters.AddWithValue("@id", insertedIds[i]);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new InvalidOperationException(
                                "数据库回读失败：part_id=" +
                                insertedIds[i].ToString(CultureInfo.InvariantCulture) + " 不存在。");
                        bool valid =
                            string.Equals(
                                Convert.ToString(reader["part_number"]),
                                r.Number, StringComparison.Ordinal) &&
                            Convert.ToInt32(reader["source_index"]) == r.Index + 1 &&
                            string.Equals(
                                Convert.ToString(reader["source_file"]),
                                r.SourceFile, StringComparison.OrdinalIgnoreCase) &&
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
                                (i + 1).ToString(CultureInfo.InvariantCulture));
                    }
                }

                // 校核孔
                for (int i = 0; i < insertedRecords.Count; i++)
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT center_u,center_v,center_w,perimeter,enclosed_area," +
                        "normal_u,normal_v,normal_w " +
                        "FROM holes WHERE part_id=@id ORDER BY hole_index;";
                    cmd.Parameters.AddWithValue("@id", insertedIds[i]);
                    using (var reader = cmd.ExecuteReader())
                    {
                        int hi = 0;
                        while (reader.Read())
                        {
                            if (hi >= insertedRecords[i].Feature.Holes.Count)
                                throw new InvalidOperationException(
                                    "孔位回读记录数多于写入记录数。");
                            WbHole h = insertedRecords[i].Feature.Holes[hi];
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
                        if (hi != insertedRecords[i].Feature.Holes.Count)
                            throw new InvalidOperationException(
                                "孔位回读记录数不一致，记录序号：" +
                                (i + 1).ToString(CultureInfo.InvariantCulture));
                    }
                }

                // 校核外轮廓
                for (int i = 0; i < insertedRecords.Count; i++)
                using (var cmd = c.CreateCommand())
                {
                    var profiles = insertedRecords[i].Feature.OuterProfiles;
                    cmd.CommandText =
                        "SELECT profile_index,enclosed_area,perimeter," +
                        "normal_u,normal_v,normal_w " +
                        "FROM outer_profiles WHERE part_id=@id ORDER BY profile_index;";
                    cmd.Parameters.AddWithValue("@id", insertedIds[i]);
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

        internal static string SettingsText(
            Dictionary<V5Criterion, bool> enabled,
            Dictionary<V5Criterion, double> tolerances,
            Dictionary<V5Criterion, bool> isRel,
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
                    (isRel[x] ? "%" : ""))) +
                ";inertia_degen=" + inertiaDegenerateRatio.ToString("R", CultureInfo.InvariantCulture) +
                ";normal_tol=" + holeNormalTolerance.ToString("R", CultureInfo.InvariantCulture) +
                ";strict_mat=" + strictMaterialColor.ToString(CultureInfo.InvariantCulture) +
                ";outer_area=" + outerProfileAreaRatio.ToString("R", CultureInfo.InvariantCulture) +
                ";outer_perim=" + outerProfilePerimeterMin.ToString("R", CultureInfo.InvariantCulture) +
                ";degen_mult=" + degeneratePositionMultiplier.ToString("R", CultureInfo.InvariantCulture) +
                ";degen_match=" + enableDegenerateMatching.ToString(CultureInfo.InvariantCulture) +
                ";fallback_tol=" + fallbackTolerance.ToString("R", CultureInfo.InvariantCulture);
        }

        // ================================================================
        // 实体分类电池专用：加载已有件 / 增量插入单件
        // ================================================================

        /// <summary>确保 part_features 表存在（存储 WbFeature.Serialize() 全精度文本）。</summary>
        internal static void EnsurePartFeaturesTable(SQLiteConnection c, SQLiteCommand cmd)
        {
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS part_features (" +
                "part_id INTEGER PRIMARY KEY, " +
                "feature_text TEXT NOT NULL);";
            cmd.ExecuteNonQuery();
        }

        /// <summary>从数据库加载所有已有件（纯只读，不创建表、不回填）。</summary>
        internal static List<WbMigrationRecord> LoadAllParts(string path)
        {
            var result = new List<WbMigrationRecord>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return result;

            string full = Path.GetFullPath(path);
            using (var c = new SQLiteConnection("Data Source=" + full + ";Version=3;"))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    // 检查 part_features 表是否存在（不创建）
                    cmd.CommandText =
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='part_features';";
                    bool hasFeatureTable = Convert.ToInt64(
                        cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;

                    if (hasFeatureTable)
                    {
                        // LEFT JOIN：有 feature_text 的反序列化，没有的走内存重建
                        cmd.CommandText =
                            "SELECT p.part_id, p.part_number, p.material, p.color, " +
                            "p.volume, p.surface_area, p.bbox_max, p.bbox_mid, p.bbox_min, " +
                            "p.inertia_1, p.inertia_2, p.inertia_3, pf.feature_text " +
                            "FROM parts p LEFT JOIN part_features pf ON p.part_id = pf.part_id " +
                            "ORDER BY p.part_id;";

                        var pendingRebuild = new List<(long id, string number, string material, string color,
                            double volume, double area, double[] bbox, double[] inertia)>();

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                long id = reader.GetInt64(0);
                                string number = reader.GetString(1);

                                if (!reader.IsDBNull(12))
                                {
                                    string featureText = reader.GetString(12);
                                    try
                                    {
                                        WbFeature feature = WbFeature.Deserialize(featureText);
                                        result.Add(new WbMigrationRecord(
                                            result.Count, number, feature, string.Empty));
                                        continue;
                                    }
                                    catch { /* 反序列化失败，走重建 */ }
                                }

                                // 缺少 feature_text 或反序列化失败，收集待重建
                                string material = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                                string color = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                                double volume = reader.GetDouble(4);
                                double area = reader.GetDouble(5);
                                double[] bbox = { reader.GetDouble(6), reader.GetDouble(7), reader.GetDouble(8) };
                                double[] inertia = { reader.GetDouble(9), reader.GetDouble(10), reader.GetDouble(11) };
                                pendingRebuild.Add((id, number, material, color, volume, area, bbox, inertia));
                            }
                        }

                        // 对缺失 feature_text 的件走内存重建
                        foreach (var pd in pendingRebuild)
                        {
                            var feature = RebuildSingleFeatureFromLegacy(
                                cmd, pd.id, pd.material, pd.color,
                                pd.volume, pd.area, pd.bbox, pd.inertia);
                            result.Add(new WbMigrationRecord(
                                result.Count, pd.number, feature, string.Empty));
                        }
                    }
                    else
                    {
                        // part_features 表不存在，全部走内存重建
                        LoadFromLegacyTables(c, cmd, result);
                    }
                }
            }
            return result;
        }

        /// <summary>从 parts/holes/outer_profiles 表重建所有 WbFeature（纯内存，不回填）。</summary>
        private static void LoadFromLegacyTables(
            SQLiteConnection c, SQLiteCommand cmd, List<WbMigrationRecord> result)
        {
            cmd.CommandText =
                "SELECT part_id, part_number, material, color, " +
                "volume, surface_area, bbox_max, bbox_mid, bbox_min, " +
                "inertia_1, inertia_2, inertia_3 " +
                "FROM parts ORDER BY part_id;";
            var partsData = new List<(long id, string number, string material, string color,
                double volume, double area, double[] bbox, double[] inertia)>();

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    string number = reader.GetString(1);
                    string material = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    string color = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                    double volume = reader.GetDouble(4);
                    double area = reader.GetDouble(5);
                    double[] bbox = { reader.GetDouble(6), reader.GetDouble(7), reader.GetDouble(8) };
                    double[] inertia = { reader.GetDouble(9), reader.GetDouble(10), reader.GetDouble(11) };
                    partsData.Add((id, number, material, color, volume, area, bbox, inertia));
                }
            }

            foreach (var pd in partsData)
            {
                var feature = RebuildSingleFeatureFromLegacy(
                    cmd, pd.id, pd.material, pd.color,
                    pd.volume, pd.area, pd.bbox, pd.inertia);
                result.Add(new WbMigrationRecord(result.Count, pd.number, feature, string.Empty));
            }
        }

        /// <summary>从 parts/holes/outer_profiles 表重建单个件的特征（纯内存，不回填）。</summary>
        private static WbFeature RebuildSingleFeatureFromLegacy(
            SQLiteCommand cmd,
            long partId, string material, string color,
            double volume, double area, double[] bbox, double[] inertia)
        {
            var feature = new WbFeature
            {
                Material = material,
                Color = color,
                BoundingBox = bbox,
                Area = area,
                Volume = volume,
                Inertia = inertia,
                MirrorScore = 0,
                IsDegenerate = (inertia[0] > 1e-10 && inertia[1] / inertia[0] > 0.99) ||
                               (inertia[1] > 1e-10 && inertia[2] / inertia[1] > 0.99)
            };

            cmd.CommandText =
                "SELECT center_u, center_v, center_w, perimeter, enclosed_area, " +
                "normal_u, normal_v, normal_w FROM holes WHERE part_id=@id ORDER BY hole_index;";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", partId);
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    feature.Holes.Add(new WbHole(
                        reader.GetDouble(3), reader.GetDouble(4),
                        reader.GetDouble(0), reader.GetDouble(1), reader.GetDouble(2),
                        reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7)));
                }
            }

            feature.HoleMirrorScore = WbFeature.ComputeHoleMirrorScore(feature.Holes);

            cmd.CommandText =
                "SELECT enclosed_area, perimeter, normal_u, normal_v, normal_w " +
                "FROM outer_profiles WHERE part_id=@id ORDER BY profile_index;";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", partId);
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    feature.OuterProfiles.Add(new WbOuterProfile
                    {
                        Area = reader.GetDouble(0),
                        Perimeter = reader.GetDouble(1),
                        NU = reader.GetDouble(2),
                        NV = reader.GetDouble(3),
                        NW = reader.GetDouble(4)
                    });
                }
            }

            return feature;
        }

        /// <summary>增量插入单件到数据库（不重建库）。</summary>
        internal static void InsertSinglePart(string path, WbMigrationRecord record)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("数据库地址不能为空。");

            string full = Path.GetFullPath(path);
            if (!File.Exists(full))
                throw new FileNotFoundException("数据库文件不存在：" + full);

            using (var c = new SQLiteConnection("Data Source=" + full + ";Version=3;"))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    EnsurePartFeaturesTable(c, cmd);
                    using (var tx = c.BeginTransaction())
                    {
                        long partId = InsertPart(c, cmd, record);

                        cmd.CommandText = "INSERT INTO part_features (part_id, feature_text) VALUES (@id, @text);";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@id", partId);
                        cmd.Parameters.AddWithValue("@text", record.Feature.Serialize());
                        cmd.ExecuteNonQuery();

                        tx.Commit();
                    }
                }
            }
        }

        /// <summary>批量插入多件到数据库（单事务，失败全部回滚）。</summary>
        internal static void InsertPartsBatch(string path, List<WbMigrationRecord> records, string settingsText = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("数据库地址不能为空。");

            string full = Path.GetFullPath(path);
            if (!File.Exists(full))
                throw new FileNotFoundException("数据库文件不存在：" + full);

            using (var c = new SQLiteConnection("Data Source=" + full + ";Version=3;"))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    EnsurePartFeaturesTable(c, cmd);
                    using (var tx = c.BeginTransaction())
                    {
                        try
                        {
                            // 如有参数快照，在同一事务内先更新 classification_profile，
                            // 确保入库记录与参数快照始终一致。
                            if (!string.IsNullOrWhiteSpace(settingsText))
                            {
                                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='classification_profile';";
                                long profileCount = (long)cmd.ExecuteScalar();
                                if (profileCount > 0)
                                {
                                    cmd.CommandText =
                                        "UPDATE classification_profile SET settings_text=@settings WHERE profile_id=1;" +
                                        "INSERT INTO classification_profile(profile_id,settings_text) " +
                                        "SELECT 1,@settings WHERE changes()=0;";
                                    cmd.Parameters.Clear();
                                    cmd.Parameters.AddWithValue("@settings", settingsText);
                                    cmd.ExecuteNonQuery();
                                }
                            }

                            foreach (var record in records)
                            {
                                long partId = InsertPart(c, cmd, record);

                                cmd.CommandText = "INSERT INTO part_features (part_id, feature_text) VALUES (@id, @text);";
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@id", partId);
                                cmd.Parameters.AddWithValue("@text", record.Feature.Serialize());
                                cmd.ExecuteNonQuery();
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            }
        }

        /// <summary>将当前设置写入数据库的 classification_profile 表（覆盖旧记录）。</summary>
        internal static void SaveSettings(string path, string settings)
        {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full)) return;
            using (var c = new SQLiteConnection("Data Source=" + full + ";Version=3;"))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    // 先判断 classification_profile 表是否存在；不存在则跳过，后续 Rebuild 会建表
                    cmd.CommandText =
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='classification_profile';";
                    long count = (long)cmd.ExecuteScalar();
                    if (count == 0) return;
                }
                // 表存在，执行写入；任何异常都应视为真实数据库故障，直接抛出
                using (var cmd = c.CreateCommand())
                {
                    // 用 UPDATE + 条件 INSERT 代替 REPLACE，避免外键引用下 DELETE 触发约束失败
                    cmd.CommandText =
                        "UPDATE classification_profile SET settings_text=@settings WHERE profile_id=1;" +
                        "INSERT INTO classification_profile(profile_id,settings_text) " +
                        "SELECT 1,@settings WHERE changes()=0;";
                    cmd.Parameters.AddWithValue("@settings", settings);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>从 classification_profile 读取当前参数快照字符串。</summary>
        internal static string LoadProfileSettings(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            string full = Path.GetFullPath(path);
            using (var c = new SQLiteConnection("Data Source=" + full + ";Version=3;"))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='classification_profile';";
                    if (Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                        return null;
                }
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT settings_text FROM classification_profile WHERE profile_id=1;";
                    object val = cmd.ExecuteScalar();
                    return val == null || val == DBNull.Value ? null : val.ToString();
                }
            }
        }
    }
    internal sealed class WbClassificationSettings
    {
        public Dictionary<V5Criterion, bool> IsRelativeTolerance { get; } = new Dictionary<V5Criterion, bool>
        {
            { V5Criterion.Length, true }, { V5Criterion.Inertia, true },
            { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true },
            { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, false }
        };

        public Dictionary<V5Criterion, bool> Enabled { get; } = new Dictionary<V5Criterion, bool>
        {
            { V5Criterion.Length, true }, { V5Criterion.Inertia, true },
            { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true },
            { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, true }
        };

        public Dictionary<V5Criterion, double> Tolerances { get; } = new Dictionary<V5Criterion, double>
        {
            { V5Criterion.Length, 0.5 }, { V5Criterion.Inertia, 0.2 },
            { V5Criterion.Volume, 0.5 }, { V5Criterion.EdgeSum, 1 },
            { V5Criterion.HoleArea, 1 }, { V5Criterion.HolePosition, 0.5 }
        };

        public double FallbackTolerance = 0.001;
        public double InertiaDegenerateRatio = 0.99;
        public double HoleNormalTolerance = 0.9986;
        public bool StrictMaterialColor = true;
        public double OuterProfileAreaRatio = 0.0001;
        public double OuterProfilePerimeterMin = 0.01;
        public double DegeneratePositionMultiplier = 2.0;
        public bool EnableDegenerateMatching;

        public void Write(GH_IWriter writer, string chunk)
        {
            foreach (V5Criterion c in Enabled.Keys)
            {
                writer.SetBoolean(chunk + c + "Enabled", Enabled[c]);
                writer.SetDouble(chunk + c + "Tolerance", Tolerances[c]);
                writer.SetBoolean(chunk + c + "TolIsRel", IsRelativeTolerance[c]);
            }
            writer.SetDouble(chunk + "FallbackTolerance", FallbackTolerance);
            writer.SetDouble(chunk + "InertiaDegenerateRatio", InertiaDegenerateRatio);
            writer.SetDouble(chunk + "HoleNormalTolerance", HoleNormalTolerance);
            writer.SetBoolean(chunk + "StrictMaterialColor", StrictMaterialColor);
            writer.SetDouble(chunk + "OuterProfileAreaRatio", OuterProfileAreaRatio);
            writer.SetDouble(chunk + "OuterProfilePerimeterMin", OuterProfilePerimeterMin);
            writer.SetDouble(chunk + "DegeneratePositionMultiplier", DegeneratePositionMultiplier);
            writer.SetBoolean(chunk + "EnableDegenerateMatching", EnableDegenerateMatching);
        }

        public void Read(GH_IReader reader, string chunk)
        {
            foreach (V5Criterion c in Enabled.Keys.ToList())
            {
                bool e = Enabled[c];
                double t = Tolerances[c];
                bool isRel = IsRelativeTolerance[c];
                if (reader.TryGetBoolean(chunk + c + "Enabled", ref e) &&
                    c != V5Criterion.Length)
                    Enabled[c] = e;
                if (reader.TryGetDouble(chunk + c + "Tolerance", ref t))
                    Tolerances[c] = Math.Abs(t);
                if (reader.TryGetBoolean(chunk + c + "TolIsRel", ref isRel))
                    IsRelativeTolerance[c] = isRel;
            }
            Enabled[V5Criterion.Length] = true;

            double v = FallbackTolerance;
            if (reader.TryGetDouble(chunk + "FallbackTolerance", ref v))
                FallbackTolerance = Math.Max(0, v);

            v = InertiaDegenerateRatio;
            if (reader.TryGetDouble(chunk + "InertiaDegenerateRatio", ref v))
                InertiaDegenerateRatio = Math.Max(0.5, Math.Min(1.0, v));

            v = HoleNormalTolerance;
            if (reader.TryGetDouble(chunk + "HoleNormalTolerance", ref v))
                HoleNormalTolerance = Math.Max(0.0, Math.Min(1.0, v));

            bool b = StrictMaterialColor;
            if (reader.TryGetBoolean(chunk + "StrictMaterialColor", ref b))
                StrictMaterialColor = b;

            v = OuterProfileAreaRatio;
            if (reader.TryGetDouble(chunk + "OuterProfileAreaRatio", ref v))
                OuterProfileAreaRatio = Math.Max(0, v);

            v = OuterProfilePerimeterMin;
            if (reader.TryGetDouble(chunk + "OuterProfilePerimeterMin", ref v))
                OuterProfilePerimeterMin = Math.Max(0, v);

            v = DegeneratePositionMultiplier;
            if (reader.TryGetDouble(chunk + "DegeneratePositionMultiplier", ref v))
                DegeneratePositionMultiplier = Math.Max(1.0, v);

            b = EnableDegenerateMatching;
            if (reader.TryGetBoolean(chunk + "EnableDegenerateMatching", ref b))
                EnableDegenerateMatching = b;
        }

        internal string SettingsText()
        {
            return string.Join(";",
                Enabled.Keys.OrderBy(x => x).Select(x =>
                    x + "=" + Enabled[x] +
                    ",tol=" + Tolerances[x].ToString("R", CultureInfo.InvariantCulture) +
                    (IsRelativeTolerance[x] ? "%" : ""))) +
                ";inertia_degen=" + InertiaDegenerateRatio.ToString("R", CultureInfo.InvariantCulture) +
                ";normal_tol=" + HoleNormalTolerance.ToString("R", CultureInfo.InvariantCulture) +
                ";strict_mat=" + StrictMaterialColor.ToString(CultureInfo.InvariantCulture) +
                ";outer_area=" + OuterProfileAreaRatio.ToString("R", CultureInfo.InvariantCulture) +
                ";outer_perim=" + OuterProfilePerimeterMin.ToString("R", CultureInfo.InvariantCulture) +
                ";degen_mult=" + DegeneratePositionMultiplier.ToString("R", CultureInfo.InvariantCulture) +
                ";degen_match=" + EnableDegenerateMatching.ToString(CultureInfo.InvariantCulture) +
                ";fallback_tol=" + FallbackTolerance.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>深拷贝一份设置快照，用于分类执行期间冻结参数。</summary>
        public WbClassificationSettings Clone()
        {
            var c = new WbClassificationSettings();
            foreach (V5Criterion k in Enabled.Keys)
            {
                c.Enabled[k] = Enabled[k];
                c.Tolerances[k] = Tolerances[k];
                c.IsRelativeTolerance[k] = IsRelativeTolerance[k];
            }
            c.FallbackTolerance = FallbackTolerance;
            c.InertiaDegenerateRatio = InertiaDegenerateRatio;
            c.HoleNormalTolerance = HoleNormalTolerance;
            c.StrictMaterialColor = StrictMaterialColor;
            c.OuterProfileAreaRatio = OuterProfileAreaRatio;
            c.OuterProfilePerimeterMin = OuterProfilePerimeterMin;
            c.DegeneratePositionMultiplier = DegeneratePositionMultiplier;
            c.EnableDegenerateMatching = EnableDegenerateMatching;
            return c;
        }

        /// <summary>将 SettingsText() 字符串反向解析为设置对象。</summary>
        public static bool TryParse(string text, out WbClassificationSettings result)
        {
            result = new WbClassificationSettings();
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                var nameToCriterion = new Dictionary<string, V5Criterion>
                {
                    { "EdgeSum", V5Criterion.EdgeSum },
                    { "EdgesSum", V5Criterion.EdgeSum },   // 兼容旧解析器的拼写错误
                    { "HoleArea", V5Criterion.HoleArea },
                    { "HolePosition", V5Criterion.HolePosition },
                    { "Inertia", V5Criterion.Inertia },
                    { "Length", V5Criterion.Length },
                    { "Volume", V5Criterion.Volume }
                };

                // 六项必选分类准则，缺任一项即解析失败
                var parsedCriteria = new HashSet<V5Criterion>();

                foreach (string part in text.Split(';'))
                {
                    string trimmed = part.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq < 0) continue;

                    string key = trimmed.Substring(0, eq);

                    if (nameToCriterion.TryGetValue(key, out V5Criterion criterion))
                    {
                        // Format: CriterionName=True/False,tol=N or tol=N%
                        // 布尔值和容差均成功解析、数值为有限值，才算该准则解析成功
                        bool parsedOk = false;
                        string value = trimmed.Substring(eq + 1);
                        int comma = value.IndexOf(',');
                        if (comma >= 0)
                        {
                            bool enabledOk = bool.TryParse(value.Substring(0, comma), out bool enabled);
                            if (enabledOk)
                                result.Enabled[criterion] = enabled;

                            string tolPart = value.Substring(comma + 1);
                            if (tolPart.StartsWith("tol="))
                            {
                                string tolStr = tolPart.Substring(4);
                                bool isRelative = tolStr.EndsWith("%");
                                if (isRelative) tolStr = tolStr.TrimEnd('%');
                                if (double.TryParse(tolStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double tol)
                                    && !double.IsNaN(tol) && !double.IsInfinity(tol))
                                {
                                    result.Tolerances[criterion] = Math.Abs(tol);
                                    result.IsRelativeTolerance[criterion] = isRelative;
                                    parsedOk = enabledOk;
                                }
                            }
                        }

                        if (parsedOk)
                            parsedCriteria.Add(criterion);
                    }
                    else
                    {
                        string val = trimmed.Substring(eq + 1);
                        switch (key)
                        {
                            case "inertia_degen":
                                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double id))
                                    result.InertiaDegenerateRatio = Math.Max(0.5, Math.Min(1.0, id));
                                break;
                            case "normal_tol":
                                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double nt))
                                    result.HoleNormalTolerance = Math.Max(0.0, Math.Min(1.0, nt));
                                break;
                            case "strict_mat":
                                if (bool.TryParse(val, out bool sm))
                                    result.StrictMaterialColor = sm;
                                break;
                            case "outer_area":
                                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double oa))
                                    result.OuterProfileAreaRatio = Math.Max(0, oa);
                                break;
                            case "outer_perim":
                                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double op))
                                    result.OuterProfilePerimeterMin = Math.Max(0, op);
                                break;
                            case "degen_mult":
                                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double dm))
                                    result.DegeneratePositionMultiplier = Math.Max(1.0, dm);
                                break;
                            case "degen_match":
                                if (bool.TryParse(val, out bool em))
                                    result.EnableDegenerateMatching = em;
                                break;
                            case "fallback_tol":
                                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double ft))
                                    result.FallbackTolerance = Math.Max(0, ft);
                                break;
                        }
                    }
                }
                // 六项必选分类准则必须全部存在，缺任一项即视为解析失败
                var required = new[]
                {
                    V5Criterion.Length, V5Criterion.Inertia, V5Criterion.Volume,
                    V5Criterion.EdgeSum, V5Criterion.HoleArea, V5Criterion.HolePosition
                };
                if (required.Any(c => !parsedCriteria.Contains(c)))
                {
                    result = null;
                    return false;
                }

                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }
    }
}
