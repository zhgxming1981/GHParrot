using CommonFunction;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>
    /// 用纵向基准线交点对齐完整剖面和各带孔剖面，分别输出两组交线。
    /// </summary>
    public class CurvedProfileSections : GH_Component
    {
        private bool _forceThroughCircleCenter;

        public CurvedProfileSections()
            : base("弯弧型材打剖面", "弯弧剖面",
                "用无限剖切平面求实体交线，对齐后根据完整剖切符号的短边方向，绕中间线旋转 90°，分别输出到 Top 视图。",
                "Parrot", "Drawing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBrepParameter("型材", "型材", "参与剖切的型材 Brep 列表。", GH_ParamAccess.list);
            p.AddCurveParameter("完整剖切位置", "完整位置", "Top 视图中的 [ 形剖切符号；中间长边确定无限剖切平面，两端短边确定剖切方向。", GH_ParamAccess.item);
            p.AddCurveParameter("带孔剖切位置", "带孔位置", "各带孔位置的 [ 形剖切符号；中间长边确定无限剖切平面，两端短边确定剖切方向。", GH_ParamAccess.list);
            p.AddPointParameter("插入点", "插入点", "第一个结果在 Top 视图中的纵向基准点。", GH_ParamAccess.item);
            p.AddNumberParameter("剖面间距", "间距", "相邻完整剖面包围盒之间的水平净距。", GH_ParamAccess.item);
            p.AddNumberParameter("剖面放大系数", "放大系数", "以纵向基准点放大的系数。", GH_ParamAccess.item, 1.0);
            p.AddCurveParameter("纵向基准线", "基准线", "可选。优先作为剖面定位基准；为空时从 Brep 的纵向圆弧自动寻找参考圆。右键可强制剖切线经过圆心。", GH_ParamAccess.item);
            p.AddNumberParameter("圆弧识别误差", "圆弧误差", "可选。Top 投影圆弧的最大径向偏差，单位为模型单位；为空时使用文档绝对容差的 10 倍。", GH_ParamAccess.item);
            p[3].Optional = true;
            p[4].Optional = true;
            p[5].Optional = true;
            p[6].Optional = true;
            p[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("完整剖面线", "完整剖面", "每个分支均为完整剖切位置得到的交线；仅按对应带孔剖面的位置平移，绝不混入带孔交线。", GH_ParamAccess.tree);
            p.AddCurveParameter("带孔剖面线", "带孔剖面", "每个带孔位置各自得到的原始剖切交线，和同分支完整剖面使用相同放大与平移。", GH_ParamAccess.tree);
            p.AddTextParameter("诊断信息", "诊断", "剖切与对齐信息，以及各剖切平面与纵向基准线的三维交点坐标和共圆偏差。", GH_ParamAccess.list);
            p.AddPointParameter("圆心", "圆心", "输入基准线的 Top 投影圆心；基准线为空时为从 Brep 识别出的圆心。无法识别时留空。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (!CHardware.CheckLegality()) return;

            var breps = new List<Brep>();
            var holeSymbols = new List<Curve>();
            Curve fullSymbol = null, referenceLine = null;
            Point3d insertion = Point3d.Origin;
            double spacing = 1000.0, scale = 1.0;
            if (!da.GetDataList(0, breps) || !da.GetData(1, ref fullSymbol) ||
                !da.GetDataList(2, holeSymbols)) return;
            if (Params.Input[6].VolatileDataCount > 0 && !da.GetData(6, ref referenceLine)) return;
            da.GetData(3, ref insertion);
            da.GetData(4, ref spacing);
            da.GetData(5, ref scale);
            if (breps.Count == 0 || breps.Any(b => b == null || !b.IsValid) ||
                holeSymbols.Count == 0 || holeSymbols.Any(c => c == null || !c.IsValid) ||
                (referenceLine != null && !referenceLine.IsValid) || !insertion.IsValid ||
                double.IsNaN(spacing) || double.IsInfinity(spacing) || spacing <= 0 ||
                double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "输入无效：检查型材、剖切符号、纵向基准线、插入点、正数间距和正数放大系数。");
                return;
            }

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            double tolerance = doc == null ? 0.01 : doc.ModelAbsoluteTolerance;
            double arcTolerance = tolerance * 10;
            if (Params.Input[7].VolatileDataCount > 0 && !da.GetData(7, ref arcTolerance)) return;
            if (double.IsNaN(arcTolerance) || double.IsInfinity(arcTolerance) || arcTolerance <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "圆弧识别误差必须是大于零的有限数值。");
                return;
            }
            var log = new List<string>();
            bool automaticReference = referenceLine == null;
            Point3d circleCenter = Point3d.Unset;
            double circleRadius = 0;
            if (automaticReference)
            {
                Point3d foundCenter;
                double foundRadius;
                var symbols = new List<Curve> { fullSymbol };
                symbols.AddRange(holeSymbols);
                if (!TryFindBrepCircle(breps, symbols, tolerance, arcTolerance, out foundCenter, out foundRadius, log))
                {
                    ReportErrors(log);
                    return;
                }
                circleCenter = foundCenter;
                circleRadius = foundRadius;
                Circle circle = new Circle(new Plane(foundCenter, Vector3d.ZAxis), foundRadius);
                referenceLine = circle.ToNurbsCurve();
                log.Add("基准线为空：已用 Brep 纵向圆弧构造定位圆，圆心 " + FormatPoint(circleCenter) +
                        "，半径 " + FormatNumber(circleRadius) + "。");
            }
            else
            {
                bool hasCircle = TryGetTopCircle(referenceLine, tolerance, arcTolerance, out circleCenter, out circleRadius);
                if (_forceThroughCircleCenter && !hasCircle)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "输入基准线的 Top 投影未通过圆弧识别；请检查曲线或调整圆弧识别误差。当前误差 " +
                        FormatNumber(arcTolerance) + "。");
                    return;
                }
                if (!hasCircle)
                {
                    circleCenter = Point3d.Unset;
                    circleRadius = 0;
                }
                if (_forceThroughCircleCenter)
                    log.Add("强制剖切采用输入基准线 Top 投影的圆心 " + FormatPoint(circleCenter) +
                            "，半径 " + FormatNumber(circleRadius) + "，识别误差 " + FormatNumber(arcTolerance) + "。");
            }

            SectionFrame fullFrame;
            if (!TryCreateSectionPlane(fullSymbol, referenceLine, tolerance, "完整剖面",
                _forceThroughCircleCenter ? (Point3d?)circleCenter : null, automaticReference, out fullFrame, log))
            {
                ReportErrors(log);
                return;
            }
            var referencePoints = new List<Point3d> { fullFrame.Plane.Origin };
            log.Add("完整剖面交点：" + FormatPoint(fullFrame.Plane.Origin));

            List<Curve> fullLines = CutLines(breps, fullFrame.Plane, tolerance);
            if (fullLines.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "完整剖面位置没有得到实体与剖切平面的交线。");
                return;
            }
            // 先在原始竖直剖切面求交，再真正绕完整符号的中间线旋转到 Top。
            Transform turnToTop = Transform.Rotation(-Math.PI / 2, fullFrame.AxisDirection, fullFrame.AxisPoint);
            Point3d turnedReference = fullFrame.Plane.Origin;
            turnedReference.Transform(turnToTop);
            Transform referenceToOrigin = Transform.Translation(-turnedReference.X, -turnedReference.Y, -turnedReference.Z);
            foreach (Curve line in fullLines)
            {
                line.Transform(turnToTop);
                line.Transform(referenceToOrigin);
            }

            var sections = new DataTree<Curve>();
            var holeOutputs = new DataTree<Curve>();
            double previousRight = 0;
            for (int i = 0; i < holeSymbols.Count; i++)
            {
                SectionFrame holeFrame;
                if (!TryCreateSectionPlane(holeSymbols[i], referenceLine, tolerance, "带孔剖面 " + (i + 1),
                    _forceThroughCircleCenter ? (Point3d?)circleCenter : null, automaticReference, out holeFrame, log))
                {
                    ReportErrors(log);
                    return;
                }
                referencePoints.Add(holeFrame.Plane.Origin);
                log.Add("带孔剖面 " + (i + 1) + " 交点：" + FormatPoint(holeFrame.Plane.Origin));
                List<Curve> holeLines = CutLines(breps, holeFrame.Plane, tolerance);
                if (holeLines.Count == 0)
                {
                    log.Add("带孔剖面 " + (i + 1) + " 没有得到实体与剖切平面的交线。");
                    ReportErrors(log);
                    return;
                }
                Transform toFullPlane = Transform.PlaneToPlane(holeFrame.Plane, fullFrame.Plane);
                foreach (Curve line in holeLines)
                    line.Transform(toFullPlane);
                foreach (Curve line in holeLines)
                {
                    line.Transform(turnToTop);
                    line.Transform(referenceToOrigin);
                }

                var complete = fullLines.Select(line => line.DuplicateCurve()).ToList();
                var path = new GH_Path(i);

                Transform enlarge = Transform.Scale(Point3d.Origin, scale);
                BoundingBox scaledBounds = BoundingBox.Empty;
                foreach (Curve line in complete)
                {
                    line.Transform(enlarge);
                    scaledBounds.Union(line.GetBoundingBox(true));
                }
                double horizontalOffset = i == 0 ? insertion.X : previousRight + spacing - scaledBounds.Min.X;
                Transform place = Transform.Translation(horizontalOffset, insertion.Y, 0);
                previousRight = scaledBounds.Max.X + horizontalOffset;
                foreach (Curve line in complete)
                {
                    line.Transform(place);
                    sections.Add(line, path);
                }
                foreach (Curve hole in holeLines)
                {
                    Curve output = hole.DuplicateCurve();
                    output.Transform(enlarge);
                    output.Transform(place);
                    holeOutputs.Add(output, path);
                }
                log.Add("剖面 " + (i + 1) + "：完整剖面 " + complete.Count + " 条交线，带孔剖面 " + holeLines.Count + " 条交线；共用完整剖切轴反向旋转 90°。");
            }

            AppendCircleDiagnostics(referencePoints, tolerance, log);
            da.SetDataTree(0, sections);
            da.SetDataTree(1, holeOutputs);
            da.SetDataList(2, log);
            if (circleCenter.IsValid) da.SetData(3, circleCenter);
        }

        private bool TryCreateSectionPlane(Curve symbol, Curve referenceLine, double tolerance,
            string name, Point3d? forcedCenter, bool automaticReference, out SectionFrame frame, List<string> log)
        {
            frame = null;
            Polyline polyline;
            if (symbol == null || !symbol.TryGetPolyline(out polyline) || polyline.Count != 4)
            {
                log.Add(name + " 必须是由三条直线组成、恰有四个顶点的 [ 形多段线。");
                return false;
            }
            Point3d outerA = polyline[0], innerA = polyline[1];
            Point3d innerB = polyline[2], outerB = polyline[3];
            Vector3d capA = outerA - innerA, capB = outerB - innerB;
            Vector3d spine = innerB - innerA;
            capA.Z = capB.Z = spine.Z = 0;
            if (!capA.Unitize() || !capB.Unitize() || !spine.Unitize() ||
                capA * capB < 0.95 ||
                (!forcedCenter.HasValue && (Math.Abs(capA * spine) > 0.1 ||
                                            Math.Abs(capB * spine) > 0.1)))
            {
                log.Add(name + " 的两条短边必须同向，且与长边近似垂直。");
                return false;
            }
            Point3d axisPoint = innerA;
            if (forcedCenter.HasValue)
            {
                Point3d center = forcedCenter.Value;
                double distanceA = DistanceXY(innerA, center), distanceB = DistanceXY(innerB, center);
                if (Math.Abs(distanceA - distanceB) <= tolerance)
                {
                    log.Add(name + " 的中间线两端到圆心距离相同，无法判断远端点。");
                    return false;
                }
                Point3d far = distanceA > distanceB ? innerA : innerB;
                Point3d centerOnSymbol = new Point3d(center.X, center.Y, far.Z);
                double originalOffset = Math.Abs(Vector3d.CrossProduct(spine,
                    new Vector3d(center.X - innerA.X, center.Y - innerA.Y, 0)).Z);
                if (distanceA > distanceB)
                {
                    innerA = far;
                    innerB = centerOnSymbol;
                }
                else
                {
                    innerA = centerOnSymbol;
                    innerB = far;
                }
                axisPoint = centerOnSymbol;
                spine = innerB - innerA;
                spine.Z = 0;
                if (!spine.Unitize())
                {
                    log.Add(name + " 的远端点与圆心重合，无法重建剖切线。");
                    return false;
                }
                log.Add(name + " 强制过圆心：原线到圆心距离 " + FormatNumber(originalOffset) +
                        "；保留远端点 " + FormatPoint(far) + "，圆心端点 " + FormatPoint(centerOnSymbol) + "。");
            }
            Vector3d cap = capA + capB;
            cap.Unitize();
            Vector3d horizontal = spine;
            // 让剖切方向决定轴的正向；从该侧看剖面时，展开采用负向旋转 90°。
            if (Vector3d.CrossProduct(horizontal, Vector3d.ZAxis) * cap < 0)
                horizontal.Reverse();

            Curve flatReference = referenceLine.DuplicateCurve();
            flatReference.Transform(Transform.PlanarProjection(Plane.WorldXY));
            innerA.Z = innerB.Z = 0;
            BoundingBox centerBounds = flatReference.GetBoundingBox(true);
            Point3d middle = new Point3d((innerA.X + innerB.X) / 2, (innerA.Y + innerB.Y) / 2, 0);
            double reach = centerBounds.Diagonal.Length + middle.DistanceTo(centerBounds.Center) + tolerance * 10;
            using (Curve cutLine = new LineCurve(middle - spine * reach, middle + spine * reach))
            {
                CurveIntersections hits = Intersection.CurveCurve(flatReference, cutLine, tolerance, tolerance);
                if (hits == null || hits.Count == 0)
                {
                    log.Add(name + " 的长边所在直线必须与纵向基准线在 Top 视图中恰好相交一次。");
                    return false;
                }
                IntersectionEvent selectedHit = null;
                double closestDistance = double.MaxValue;
                foreach (IntersectionEvent hit in hits)
                {
                    if (!hit.IsPoint) continue;
                    double distance = hit.PointA.DistanceTo(middle);
                    if (distance < closestDistance)
                    {
                        selectedHit = hit;
                        closestDistance = distance;
                    }
                }
                if (selectedHit == null || (!automaticReference && hits.Count != 1))
                {
                    log.Add(name + " 与纵向基准线的交点不唯一，无法确定对齐点。");
                    return false;
                }
                Point3d origin = referenceLine.PointAt(selectedHit.ParameterA);
                Plane plane = new Plane(origin, horizontal, Vector3d.ZAxis);
                if (!plane.IsValid) return false;
                frame = new SectionFrame(plane, axisPoint, horizontal);
                return true;
            }
        }

        private static List<Curve> CutLines(IEnumerable<Brep> breps, Plane plane, double tolerance)
        {
            var lines = new List<Curve>();
            foreach (Brep brep in breps)
            {
                Curve[] curves;
                Point3d[] points;
                if (!Intersection.BrepPlane(brep, plane, tolerance, out curves, out points) || curves == null)
                    continue;
                Curve[] joined = Curve.JoinCurves(curves, tolerance);
                foreach (Curve curve in joined)
                    lines.Add(curve);
            }
            return lines;
        }

        private sealed class SectionFrame
        {
            public SectionFrame(Plane plane, Point3d axisPoint, Vector3d axisDirection)
            {
                Plane = plane;
                AxisPoint = axisPoint;
                AxisDirection = axisDirection;
            }

            public Plane Plane { get; }
            public Point3d AxisPoint { get; }
            public Vector3d AxisDirection { get; }
        }

        private static bool TryGetTopCircle(Curve curve, double tolerance, double arcTolerance,
            out Point3d center, out double radius)
        {
            center = Point3d.Unset;
            radius = 0;
            if (curve == null || !curve.IsValid) return false;

            // 圆心仅由 Top 投影决定；Brep 边线的 Z 起伏不影响识别。
            var candidates = new Point3d[5];
            for (int i = 0; i < candidates.Length; i++)
            {
                candidates[i] = curve.PointAtNormalizedLength(i / 4.0);
                if (!candidates[i].IsValid) return false;
            }
            int firstIndex = -1, secondIndex = -1, thirdIndex = -1;
            double largestArea = 0;
            for (int i = 0; i < candidates.Length - 2; i++)
                for (int j = i + 1; j < candidates.Length - 1; j++)
                    for (int k = j + 1; k < candidates.Length; k++)
                    {
                        double area = Math.Abs((candidates[j].X - candidates[i].X) *
                                               (candidates[k].Y - candidates[i].Y) -
                                               (candidates[j].Y - candidates[i].Y) *
                                               (candidates[k].X - candidates[i].X));
                        if (area <= largestArea) continue;
                        largestArea = area;
                        firstIndex = i;
                        secondIndex = j;
                        thirdIndex = k;
                    }
            if (firstIndex < 0) return false;
            Point3d first = candidates[firstIndex], second = candidates[secondIndex], third = candidates[thirdIndex];
            double ux = second.X - first.X, uy = second.Y - first.Y;
            double vx = third.X - first.X, vy = third.Y - first.Y;
            double cross = ux * vy - uy * vx;
            double span = Math.Max(Math.Sqrt(ux * ux + uy * uy), Math.Sqrt(vx * vx + vy * vy));
            if (span <= tolerance * 10 || largestArea <= span * span * 1e-8) return false;
            double u2 = ux * ux + uy * uy, v2 = vx * vx + vy * vy;
            center = new Point3d(first.X + (u2 * vy - v2 * uy) / (2 * cross),
                                 first.Y + (v2 * ux - u2 * vx) / (2 * cross), 0);
            radius = DistanceXY(first, center);
            if (!center.IsValid || double.IsNaN(radius) || radius <= tolerance * 10) return false;
            for (int i = 0; i <= 32; i++)
            {
                Point3d sample = curve.PointAtNormalizedLength(i / 32.0);
                if (!sample.IsValid || Math.Abs(DistanceXY(sample, center) - radius) > arcTolerance)
                {
                    center = Point3d.Unset;
                    radius = 0;
                    return false;
                }
            }
            return true;
        }

        private static bool TryFindBrepCircle(IEnumerable<Brep> breps, IEnumerable<Curve> symbols,
            double tolerance, double arcTolerance,
            out Point3d center, out double radius, List<string> log)
        {
            center = Point3d.Unset;
            radius = 0;
            var groups = new List<CircleGroup>();
            double centerTolerance = Math.Max(tolerance * 10, 1e-6);
            foreach (Brep brep in breps)
            {
                foreach (BrepEdge edge in brep.Edges)
                {
                    Point3d candidateCenter;
                    double candidateRadius;
                    if (!TryGetTopCircle(edge, tolerance, arcTolerance, out candidateCenter, out candidateRadius))
                        continue;
                    double length = edge.GetLength();
                    if (length <= tolerance * 20) continue;
                    CircleGroup group = groups.FirstOrDefault(item =>
                        DistanceXY(item.Center, candidateCenter) <= centerTolerance);
                    if (group == null)
                    {
                        group = new CircleGroup(candidateCenter, candidateRadius);
                        groups.Add(group);
                    }
                    group.TotalLength += length;
                    group.EdgeCount++;
                    if (length > group.LongestEdge)
                    {
                        group.LongestEdge = length;
                        group.Radius = candidateRadius;
                        group.Center = candidateCenter;
                    }
                }
            }
            groups.RemoveAll(group => !IsRelatedToCutSymbols(group, symbols, tolerance));
            if (groups.Count == 0)
            {
                log.Add("无法从 Brep 找到同时关联各剖切位置的水平圆弧边；请提供圆弧形纵向基准线。");
                return false;
            }
            groups.Sort((left, right) => right.TotalLength.CompareTo(left.TotalLength));
            CircleGroup best = groups[0];
            if (groups.Count > 1 && groups[1].TotalLength >= best.TotalLength * 0.8)
            {
                log.Add("Brep 存在多个长度接近、圆心不同的圆弧组，无法可靠判断型材弯弧圆；请提供圆弧形纵向基准线。");
                return false;
            }
            center = best.Center;
            radius = best.Radius;
            log.Add("Brep 圆弧识别：" + best.EdgeCount + " 条共圆心边，总弧长 " +
                    FormatNumber(best.TotalLength) + "；取圆心 " + FormatPoint(center) +
                    "，参考半径 " + FormatNumber(radius) + "。");
            return true;
        }

        private static bool IsRelatedToCutSymbols(CircleGroup group, IEnumerable<Curve> symbols, double tolerance)
        {
            double allowedOffset = Math.Max(group.Radius * 0.25, tolerance * 10);
            foreach (Curve symbol in symbols)
            {
                Polyline polyline;
                if (symbol == null || !symbol.TryGetPolyline(out polyline) || polyline.Count != 4)
                    return false;
                Point3d first = polyline[1], second = polyline[2];
                Vector3d direction = second - first;
                direction.Z = 0;
                if (!direction.Unitize()) return false;
                Vector3d toCenter = new Vector3d(group.Center.X - first.X, group.Center.Y - first.Y, 0);
                double offset = Math.Abs(Vector3d.CrossProduct(direction, toCenter).Z);
                if (offset > allowedOffset) return false;
            }
            return true;
        }

        private sealed class CircleGroup
        {
            public CircleGroup(Point3d center, double radius)
            {
                Center = center;
                Radius = radius;
            }

            public Point3d Center { get; set; }
            public double Radius { get; set; }
            public double TotalLength { get; set; }
            public double LongestEdge { get; set; }
            public int EdgeCount { get; set; }
        }

        private static double DistanceXY(Point3d first, Point3d second)
        {
            double x = first.X - second.X;
            double y = first.Y - second.Y;
            return Math.Sqrt(x * x + y * y);
        }

        private static void AppendCircleDiagnostics(IList<Point3d> points, double tolerance, List<string> log)
        {
            if (points.Count < 3)
            {
                log.Add("共圆检查：交点少于 3 个，无法确定圆。");
                return;
            }

            Point3d first = points[0];
            int secondIndex = 1;
            double longest = 0;
            for (int i = 1; i < points.Count; i++)
            {
                double distance = first.DistanceTo(points[i]);
                if (distance > longest)
                {
                    longest = distance;
                    secondIndex = i;
                }
            }
            if (longest <= tolerance)
            {
                log.Add("共圆检查：交点几乎重合，无法确定圆。");
                return;
            }

            Vector3d firstEdge = points[secondIndex] - first;
            int thirdIndex = -1;
            double largestArea = 0;
            for (int i = 1; i < points.Count; i++)
            {
                if (i == secondIndex) continue;
                double area = Vector3d.CrossProduct(firstEdge, points[i] - first).Length;
                if (area > largestArea)
                {
                    largestArea = area;
                    thirdIndex = i;
                }
            }
            if (thirdIndex < 0 || largestArea <= tolerance * longest)
            {
                log.Add("共圆检查：交点近似共线，无法稳定确定圆。");
                return;
            }

            Vector3d secondEdge = points[thirdIndex] - first;
            Vector3d normal = Vector3d.CrossProduct(firstEdge, secondEdge);
            double normalSquared = normal.SquareLength;
            Vector3d offset = (firstEdge.SquareLength * Vector3d.CrossProduct(secondEdge, normal) +
                               secondEdge.SquareLength * Vector3d.CrossProduct(normal, firstEdge)) /
                              (2 * normalSquared);
            Point3d center = first + offset;
            double radius = center.DistanceTo(first);
            normal.Unitize();
            double maxPlaneError = 0, maxRadiusError = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3d fromCenter = points[i] - center;
                double planeError = Math.Abs(fromCenter * normal);
                double inPlaneRadius = (fromCenter - (fromCenter * normal) * normal).Length;
                maxPlaneError = Math.Max(maxPlaneError, planeError);
                maxRadiusError = Math.Max(maxRadiusError, Math.Abs(inPlaneRadius - radius));
            }
            log.Add("共圆检查：用交点 1、" + (secondIndex + 1) + "、" + (thirdIndex + 1) +
                    " 定圆；圆心 " + FormatPoint(center) + "，半径 " + FormatNumber(radius) +
                    "，最大平面偏差 " + FormatNumber(maxPlaneError) +
                    "，最大半径偏差 " + FormatNumber(maxRadiusError) +
                    "，文档容差 " + FormatNumber(tolerance) + "。" +
                    (points.Count == 3 ? "仅有 3 点，无法独立验证共圆。" :
                    (maxPlaneError <= tolerance && maxRadiusError <= tolerance ? "全部交点在容差内共圆。" : "交点未在容差内共圆。")));
        }

        private static string FormatPoint(Point3d point)
        {
            return "(" + FormatNumber(point.X) + ", " + FormatNumber(point.Y) + ", " + FormatNumber(point.Z) + ")";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private void ReportErrors(IEnumerable<string> messages)
        {
            foreach (string message in messages)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "强制剖切线交于圆心", (sender, args) =>
            {
                RecordUndoEvent("切换强制剖切线交于圆心");
                _forceThroughCircleCenter = !_forceThroughCircleCenter;
                ExpireSolution(true);
            }, true, _forceThroughCircleCenter);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean("ForceThroughCircleCenter", _forceThroughCircleCenter);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            if (reader.ItemExists("ForceThroughCircleCenter"))
                _forceThroughCircleCenter = reader.GetBoolean("ForceThroughCircleCenter");
            return base.Read(reader);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("88C87BA5-8C72-4A26-90F6-920F1698A4BC");
    }
}
