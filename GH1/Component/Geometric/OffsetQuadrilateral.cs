using CommonFunction;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace NS_Parrot
{
    public class OffsetQuadrilateral : GH_Component
    {
        private readonly List<LabelData> _labels = new List<LabelData>();

        public OffsetQuadrilateral()
          : base("OffsetQuadrilateral", "四边形偏移",
              "输入平面四边形面和正法向，按上、下、左、右偏移生成新的四边形。正值向内，负值向外。",
              "Parrot", "几何")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddSurfaceParameter("四边形", "四边形", "输入平面的四边形Surface或者封闭的平面四边形曲线", GH_ParamAccess.item);
            pManager.AddVectorParameter("法向", "法向", "四边形法向的正方向，用于确定左右方向和偏移内外。", GH_ParamAccess.item);
            pManager.AddNumberParameter("上", "上", "上边偏移值；正值向内，负值向外。", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("下", "下", "下边偏移值；正值向内，负值向外。", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("左", "左", "左边偏移值；正值向内，负值向外。", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("右", "右", "右边偏移值；正值向内，负值向外。", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("容差", "容差", "几何计算容差；小于等于0时使用Rhino文档绝对容差。", GH_ParamAccess.item, 0.0);

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddSurfaceParameter("四边形", "四边形", "偏移后生成的新四边形面。", GH_ParamAccess.item);
            pManager.AddPointParameter("顶点", "顶点", "新四边形顶点。", GH_ParamAccess.list);
            pManager.AddLineParameter("上下边", "上下边", "偏移后的上下边，顺序为上、下。", GH_ParamAccess.list);
            pManager.AddLineParameter("左右边", "左右边", "偏移后的左右边，顺序为左、右。", GH_ParamAccess.list);
            pManager.AddBooleanParameter("矩形", "矩形", "偏移后的四边形是否为矩形。", GH_ParamAccess.item);
            pManager.AddTextParameter("状态", "状态", "运行状态信息。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Surface quadrilateral = null;
            Vector3d normal = Vector3d.Unset;
            double topOffset = 0.0;
            double bottomOffset = 0.0;
            double leftOffset = 0.0;
            double rightOffset = 0.0;
            double tolerance = 0.0;

            if (!DA.GetData(0, ref quadrilateral)) return;
            if (!DA.GetData(1, ref normal)) return;
            DA.GetData(2, ref topOffset);
            DA.GetData(3, ref bottomOffset);
            DA.GetData(4, ref leftOffset);
            DA.GetData(5, ref rightOffset);
            DA.GetData(6, ref tolerance);

            tolerance = ResolveTolerance(tolerance);

            try
            {
                _labels.Clear();
                QuadrilateralData data = ParseQuadrilateral(quadrilateral, tolerance);
                Plane plane = BuildReferencePlane(data.Surface, normal);
                List<Point2d> points2d = data.Vertices.Select(point => ToPlanePoint(plane, point)).ToList();

                if (SignedArea(points2d) < 0.0)
                    points2d.Reverse();

                List<Edge2d> edges = BuildEdges(points2d, tolerance);
                AssignEdgeNames(edges, plane, tolerance);
                CacheLabels(edges, plane);
                Dictionary<int, double> offsets = BuildOffsetTable(edges, topOffset, bottomOffset, leftOffset, rightOffset);

                List<Line2dData> offsetLines = new List<Line2dData>();
                for (int i = 0; i < edges.Count; i++)
                {
                    Edge2d edge = edges[i];
                    Point2d point = edge.Start + edge.Inward * offsets[edge.Index];
                    offsetLines.Add(new Line2dData(point, edge.Direction));
                }

                List<Point2d> result2d = IntersectOffsetLines(offsetLines, tolerance);
                if (SignedArea(result2d) < 0.0)
                    result2d.Reverse();

                List<Point3d> resultVertices = result2d.Select(point => plane.PointAt(point.X, point.Y)).ToList();
                Polyline polyline = new Polyline(resultVertices);
                polyline.Add(resultVertices[0]);
                PolylineCurve resultCurve = new PolylineCurve(polyline);
                Brep resultSurface = CreatePlanarSurface(resultCurve, normal, tolerance);

                Dictionary<EdgeName, Line> namedEdges = BuildNamedResultEdges(edges, resultVertices);
                bool isRectangle = IsRectangle(resultVertices, tolerance);

                DA.SetData(0, resultSurface);
                DA.SetDataList(1, resultVertices);
                DA.SetDataList(2, new[] { namedEdges[EdgeName.Top], namedEdges[EdgeName.Bottom] });
                DA.SetDataList(3, new[] { namedEdges[EdgeName.Left], namedEdges[EdgeName.Right] });
                DA.SetData(4, isRectangle);
                DA.SetData(5, "完成。");
            }
            catch (Exception ex)
            {
                _labels.Clear();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(5, ex.Message);
            }
        }

        private static double ResolveTolerance(double tolerance)
        {
            if (tolerance > 0.0)
                return tolerance;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            return doc == null ? 0.001 : doc.ModelAbsoluteTolerance;
        }

        private static QuadrilateralData ParseQuadrilateral(Surface surface, double tolerance)
        {
            if (surface == null)
                throw new ArgumentException("四边形为空。");

            Brep brep = surface.ToBrep();
            if (brep == null)
                throw new ArgumentException("无法从四边形Surface提取边界。");

            Curve boundary = ExtractOuterBoundary(brep, tolerance);
            List<Point3d> vertices = ExtractQuadrilateralVertices(boundary, tolerance);
            return new QuadrilateralData(surface, vertices);
        }

        private static Curve ExtractOuterBoundary(Brep brep, double tolerance)
        {
            List<Curve> nakedEdges = brep.DuplicateNakedEdgeCurves(true, true)?.ToList() ?? new List<Curve>();
            Curve[] joined = Curve.JoinCurves(nakedEdges, tolerance);

            Curve boundary = joined
                .Where(curve => curve != null && curve.IsClosed)
                .OrderByDescending(curve => Math.Abs(AreaMassProperties.Compute(curve)?.Area ?? 0.0))
                .FirstOrDefault();

            if (boundary == null)
                throw new ArgumentException("无法从四边形面提取闭合边界。");

            return boundary;
        }

        private static Plane BuildReferencePlane(Surface surface, Vector3d normal)
        {
            if (surface == null)
                throw new ArgumentException("四边形Surface为空。");

            if (!normal.IsValid || normal.Length <= RhinoMath.ZeroTolerance)
                throw new ArgumentException("法向无效。");

            normal.Unitize();

            Interval uDomain = surface.Domain(0);
            Interval vDomain = surface.Domain(1);
            double u = 0.5 * (uDomain.T0 + uDomain.T1);
            double v = 0.5 * (vDomain.T0 + vDomain.T1);

            Plane frame;
            if (!surface.FrameAt(u, v, out frame) || !frame.IsValid)
                throw new ArgumentException("无法从四边形Surface获取UV方向。");

            Vector3d yAxis = Vector3d.ZAxis;
            yAxis = yAxis - normal * (yAxis * normal);
            if (!yAxis.IsValid || yAxis.Length <= RhinoMath.ZeroTolerance)
            {
                yAxis = frame.YAxis;
                yAxis = yAxis - normal * (yAxis * normal);
            }

            if (!yAxis.IsValid || yAxis.Length <= RhinoMath.ZeroTolerance)
                throw new ArgumentException("无法定义四边形的上方向。");

            yAxis.Unitize();
            Vector3d xAxis = Vector3d.CrossProduct(yAxis, normal);
            if (!xAxis.IsValid || xAxis.Length <= RhinoMath.ZeroTolerance)
                throw new ArgumentException("无法根据法向判断左右方向。");

            xAxis.Unitize();
            yAxis = Vector3d.CrossProduct(normal, xAxis);
            yAxis.Unitize();

            Plane plane = new Plane(frame.Origin, xAxis, yAxis);
            if (!plane.IsValid)
                throw new ArgumentException("参考坐标系无效。");

            return plane;
        }

        private static List<Point3d> ExtractQuadrilateralVertices(Curve curve, double tolerance)
        {
            if (curve == null)
                throw new ArgumentException("四边形边界为空。");

            if (!curve.IsClosed)
                throw new ArgumentException("四边形边界必须闭合。");

            if (!curve.TryGetPlane(out _, tolerance))
                throw new ArgumentException("四边形边界必须是平面曲线。");

            Polyline polyline;
            if (curve.TryGetPolyline(out polyline))
                return NormalizeVertices(polyline.ToList(), tolerance);

            Curve[] segments = curve.DuplicateSegments();
            if (segments == null || segments.Length != 4)
                throw new ArgumentException("四边形边界必须能识别为4条边。");

            List<Point3d> vertices = new List<Point3d>();
            foreach (Curve segment in segments)
            {
                if (segment == null || segment.GetLength() <= tolerance)
                    throw new ArgumentException("四边形存在无效边。");

                Line line = new Line(segment.PointAtStart, segment.PointAtEnd);
                if (!line.IsValid || line.Length <= tolerance || !IsNearlyLine(segment, line, tolerance))
                    throw new ArgumentException("四边形的每条边必须近似为直线。");

                vertices.Add(line.From);
            }

            return NormalizeVertices(vertices, tolerance);
        }

        private static List<Point3d> NormalizeVertices(List<Point3d> vertices, double tolerance)
        {
            List<Point3d> result = new List<Point3d>();
            foreach (Point3d vertex in vertices)
            {
                if (!vertex.IsValid)
                    continue;

                if (result.Count == 0 || vertex.DistanceTo(result[result.Count - 1]) > tolerance)
                    result.Add(vertex);
            }

            if (result.Count > 1 && result[0].DistanceTo(result[result.Count - 1]) <= tolerance)
                result.RemoveAt(result.Count - 1);

            if (result.Count != 4)
                throw new ArgumentException("四边形必须有4个有效顶点。");

            return result;
        }

        private static bool IsNearlyLine(Curve curve, Line line, double tolerance)
        {
            double[] parameters = curve.DivideByCount(8, true);
            if (parameters == null || parameters.Length == 0)
                return true;

            foreach (double parameter in parameters)
            {
                Point3d point = curve.PointAt(parameter);
                if (line.DistanceTo(point, true) > tolerance)
                    return false;
            }

            return true;
        }

        private static List<Edge2d> BuildEdges(List<Point2d> points, double tolerance)
        {
            List<Edge2d> edges = new List<Edge2d>();
            for (int i = 0; i < points.Count; i++)
            {
                Point2d start = points[i];
                Point2d end = points[(i + 1) % points.Count];
                Vector2d direction = end - start;
                if (direction.Length <= tolerance)
                    throw new ArgumentException("四边形存在长度过短的边。");

                direction.Unitize();
                Vector2d inward = new Vector2d(-direction.Y, direction.X);
                Point2d midpoint = new Point2d((start.X + end.X) * 0.5, (start.Y + end.Y) * 0.5);
                edges.Add(new Edge2d(i, start, direction, inward, midpoint));
            }

            return edges;
        }

        private static void AssignEdgeNames(List<Edge2d> edges, Plane plane, double tolerance)
        {
            List<Edge2d> ordered = edges
                .OrderByDescending(edge => RoundByTolerance(plane.PointAt(edge.Midpoint.X, edge.Midpoint.Y).Z, tolerance))
                .ThenBy(edge => RoundByTolerance(edge.Midpoint.X, tolerance))
                .ToList();

            Edge2d top = ordered[0];
            Edge2d bottom = ordered[ordered.Count - 1];
            if (top.Index == bottom.Index)
                throw new ArgumentException("无法识别上下边。");

            top.Name = EdgeName.Top;
            bottom.Name = EdgeName.Bottom;

            List<Edge2d> remaining = edges
                .Where(edge => edge.Name == EdgeName.None)
                .ToList();

            if (remaining.Count != 2)
                throw new ArgumentException("无法识别左右边。");

            Edge2d left = edges[(top.Index + 1) % edges.Count];
            Edge2d right = edges[(top.Index + edges.Count - 1) % edges.Count];

            if (left.Name == EdgeName.None && right.Name == EdgeName.None)
            {
                left.Name = EdgeName.Left;
                right.Name = EdgeName.Right;
            }
            else
            {
                remaining = remaining
                    .OrderBy(edge => edge.Midpoint.X)
                    .ToList();

                remaining[0].Name = EdgeName.Left;
                remaining[1].Name = EdgeName.Right;
            }
        }

        private static double RoundByTolerance(double value, double tolerance)
        {
            if (tolerance <= 0.0)
                return value;

            return Math.Round(value / tolerance) * tolerance;
        }

        private void CacheLabels(IEnumerable<Edge2d> edges, Plane plane)
        {
            _labels.Clear();
            foreach (Edge2d edge in edges)
            {
                string text = GetEdgeText(edge.Name);
                if (string.IsNullOrEmpty(text))
                    continue;

                Point3d point = plane.PointAt(edge.Midpoint.X, edge.Midpoint.Y);
                _labels.Add(new LabelData(text, point));
            }
        }

        private static string GetEdgeText(EdgeName name)
        {
            switch (name)
            {
                case EdgeName.Top:
                    return "上";
                case EdgeName.Bottom:
                    return "下";
                case EdgeName.Left:
                    return "左";
                case EdgeName.Right:
                    return "右";
                default:
                    return null;
            }
        }

        private static Dictionary<int, double> BuildOffsetTable(List<Edge2d> edges, double top, double bottom, double left, double right)
        {
            Dictionary<int, double> result = new Dictionary<int, double>();
            foreach (Edge2d edge in edges)
            {
                switch (edge.Name)
                {
                    case EdgeName.Top:
                        result[edge.Index] = top;
                        break;
                    case EdgeName.Bottom:
                        result[edge.Index] = bottom;
                        break;
                    case EdgeName.Left:
                        result[edge.Index] = left;
                        break;
                    case EdgeName.Right:
                        result[edge.Index] = right;
                        break;
                    default:
                        throw new ArgumentException("存在未能识别为上、下、左、右的边。");
                }
            }

            return result;
        }

        private static List<Point2d> IntersectOffsetLines(List<Line2dData> lines, double tolerance)
        {
            List<Point2d> result = new List<Point2d>();
            for (int i = 0; i < lines.Count; i++)
            {
                Line2dData current = lines[i];
                Line2dData next = lines[(i + 1) % lines.Count];

                Point2d intersection;
                if (!TryIntersectLines(current.Point, current.Direction, next.Point, next.Direction, tolerance, out intersection))
                    throw new InvalidOperationException("偏移后相邻边平行或近似平行，无法生成新四边形。");

                result.Add(intersection);
            }

            return result;
        }

        private static Dictionary<EdgeName, Line> BuildNamedResultEdges(List<Edge2d> sourceEdges, List<Point3d> resultVertices)
        {
            Dictionary<EdgeName, Line> result = new Dictionary<EdgeName, Line>();
            foreach (Edge2d edge in sourceEdges)
            {
                Point3d start = resultVertices[edge.Index];
                Point3d end = resultVertices[(edge.Index + 1) % resultVertices.Count];
                result[edge.Name] = new Line(start, end);
            }

            return result;
        }

        private static Brep CreatePlanarSurface(Curve boundary, Vector3d referenceNormal, double tolerance)
        {
            if (boundary == null || !boundary.IsValid || !boundary.IsClosed)
                throw new ArgumentException("偏移后的四边形边界无效，无法生成Surface。");

            Brep[] breps = Brep.CreatePlanarBreps(boundary, tolerance);
            Brep brep = breps?
                .Where(item => item != null && item.IsValid)
                .OrderByDescending(item => AreaMassProperties.Compute(item)?.Area ?? 0.0)
                .FirstOrDefault();

            if (brep == null)
                throw new ArgumentException("无法生成偏移后的Surface。");

            AlignBrepNormal(brep, referenceNormal);
            return brep;
        }

        private static void AlignBrepNormal(Brep brep, Vector3d referenceNormal)
        {
            if (brep == null || brep.Faces.Count == 0 || !referenceNormal.IsValid || referenceNormal.Length <= RhinoMath.ZeroTolerance)
                return;

            referenceNormal.Unitize();
            BrepFace face = brep.Faces[0];
            Interval uDomain = face.Domain(0);
            Interval vDomain = face.Domain(1);
            Vector3d faceNormal = face.NormalAt(
                0.5 * (uDomain.T0 + uDomain.T1),
                0.5 * (vDomain.T0 + vDomain.T1));

            if (!faceNormal.IsValid || faceNormal.Length <= RhinoMath.ZeroTolerance)
                return;

            faceNormal.Unitize();
            if (faceNormal * referenceNormal < 0.0)
                brep.Flip();
        }

        private static bool IsRectangle(List<Point3d> vertices, double tolerance)
        {
            if (vertices == null || vertices.Count != 4)
                return false;

            double dotTolerance = Math.Max(tolerance, RhinoMath.ZeroTolerance);
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3d previous = vertices[(i + vertices.Count - 1) % vertices.Count] - vertices[i];
                Vector3d next = vertices[(i + 1) % vertices.Count] - vertices[i];
                if (!previous.IsValid || !next.IsValid || previous.Length <= tolerance || next.Length <= tolerance)
                    return false;

                previous.Unitize();
                next.Unitize();
                if (Math.Abs(previous * next) > dotTolerance)
                    return false;
            }

            return true;
        }

        private static Point2d ToPlanePoint(Plane plane, Point3d point)
        {
            double u;
            double v;
            plane.ClosestParameter(point, out u, out v);
            return new Point2d(u, v);
        }

        private static bool TryIntersectLines(Point2d p, Vector2d r, Point2d q, Vector2d s, double tolerance, out Point2d intersection)
        {
            intersection = Point2d.Unset;
            double denominator = Cross(r, s);
            if (Math.Abs(denominator) <= tolerance)
                return false;

            Vector2d qp = q - p;
            double t = Cross(qp, s) / denominator;
            intersection = p + r * t;
            return intersection.IsValid;
        }

        private static double Cross(Vector2d a, Vector2d b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        private static double SignedArea(IList<Point2d> points)
        {
            double area = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                Point2d a = points[i];
                Point2d b = points[(i + 1) % points.Count];
                area += a.X * b.Y - b.X * a.Y;
            }

            return area * 0.5;
        }

        protected override Bitmap Icon
        {
            get
            {
                Bitmap bitmap = new Bitmap(24, 24);
                bitmap.SetResolution(96, 96);

                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);

                    PointF[] outer =
                    {
                        new PointF(4.0f, 5.0f),
                        new PointF(20.0f, 4.0f),
                        new PointF(21.0f, 19.0f),
                        new PointF(5.0f, 20.0f)
                    };

                    PointF[] inner =
                    {
                        new PointF(8.0f, 8.0f),
                        new PointF(17.0f, 7.5f),
                        new PointF(17.5f, 16.0f),
                        new PointF(8.5f, 16.5f)
                    };

                    using (Pen outerPen = new Pen(Color.FromArgb(48, 48, 48), 1.8f))
                    using (Pen innerPen = new Pen(Color.FromArgb(30, 120, 70), 2.0f))
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(95, 95, 185, 120)))
                    {
                        graphics.DrawPolygon(outerPen, outer);
                        graphics.FillPolygon(fill, inner);
                        graphics.DrawPolygon(innerPen, inner);
                    }

                    using (Pen arrowPen = new Pen(Color.FromArgb(160, 40, 40), 1.6f))
                    {
                        arrowPen.CustomEndCap = new AdjustableArrowCap(3.0f, 3.0f);
                        graphics.DrawLine(arrowPen, 12.0f, 3.0f, 12.0f, 9.0f);
                    }
                }

                return bitmap;
            }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            foreach (LabelData label in _labels)
                args.Display.DrawDot(label.Point, label.Text, Color.FromArgb(210, 32, 96, 180), Color.White);
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("4DD1EFC0-64AB-4D5B-B80C-102F0FD2B09F"); }
        }

        private enum EdgeName
        {
            None,
            Top,
            Bottom,
            Left,
            Right
        }

        private sealed class QuadrilateralData
        {
            public QuadrilateralData(Surface surface, List<Point3d> vertices)
            {
                Surface = surface;
                Vertices = vertices;
            }

            public Surface Surface { get; }

            public List<Point3d> Vertices { get; }
        }

        private sealed class Edge2d
        {
            public Edge2d(int index, Point2d start, Vector2d direction, Vector2d inward, Point2d midpoint)
            {
                Index = index;
                Start = start;
                Direction = direction;
                Inward = inward;
                Midpoint = midpoint;
                Name = EdgeName.None;
            }

            public int Index { get; }

            public Point2d Start { get; }

            public Vector2d Direction { get; }

            public Vector2d Inward { get; }

            public Point2d Midpoint { get; }

            public EdgeName Name { get; set; }
        }

        private sealed class Line2dData
        {
            public Line2dData(Point2d point, Vector2d direction)
            {
                Point = point;
                Direction = direction;
            }

            public Point2d Point { get; }

            public Vector2d Direction { get; }
        }

        private sealed class LabelData
        {
            public LabelData(string text, Point3d point)
            {
                Text = text;
                Point = point;
            }

            public string Text { get; }

            public Point3d Point { get; }
        }
    }
}
