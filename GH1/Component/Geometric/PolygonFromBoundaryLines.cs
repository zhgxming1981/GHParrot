using CommonFunction;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace NS_Parrot
{
    public class PolygonFromBoundaryLines : GH_Component
    {
        public PolygonFromBoundaryLines()
          : base("PolygonFromBoundaryLines", "边线成多边形",
              "根据多条无序边线生成一个闭合多边形。边线按直线处理，可相交、可不相交、可过长。",
              "Parrot", "几何")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("边线", "E", "用于形成外轮廓的边线。每条曲线会按端点近似为直线。", GH_ParamAccess.list);
            pManager.AddNumberParameter("容差", "Tol", "几何计算容差；小于等于 0 时使用 Rhino 文档绝对容差。", GH_ParamAccess.item, 0.0);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("多边形", "P", "生成的闭合多边形。", GH_ParamAccess.item);
            pManager.AddPointParameter("顶点", "V", "多边形顶点。", GH_ParamAccess.list);
            pManager.AddTextParameter("状态", "M", "运行状态。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<Curve> curves = new List<Curve>();
            double tolerance = 0.0;

            if (!DA.GetDataList(0, curves)) return;
            DA.GetData(1, ref tolerance);
            tolerance = ResolveTolerance(tolerance);

            try
            {
                List<Line> lines = ExtractLines(curves, tolerance);
                if (lines.Count < 3)
                    throw new InvalidOperationException("至少需要 3 条有效边线。");

                Plane plane = FitPlane(lines);
                List<Line2dData> data = Build2dLines(lines, plane, tolerance);
                if (data.Count < 3)
                    throw new InvalidOperationException("有效边线数量不足。");

                List<Point3d> vertices = BuildPolygonVertices(data, plane, tolerance);
                Polyline polyline = new Polyline(vertices);
                polyline.Add(vertices[0]);
                PolylineCurve polygon = new PolylineCurve(polyline);

                if (!polygon.IsClosed || !polygon.IsValid)
                    throw new InvalidOperationException("生成的多边形无效。");

                DA.SetData(0, polygon);
                DA.SetDataList(1, vertices);
                DA.SetData(2, "完成。边数=" + vertices.Count + "；按边线延长交点裁剪生成。");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(2, ex.Message);
            }
        }

        private static double ResolveTolerance(double tolerance)
        {
            if (tolerance > 0.0)
                return tolerance;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            return doc == null ? 0.001 : doc.ModelAbsoluteTolerance;
        }

        private static List<Line> ExtractLines(IEnumerable<Curve> curves, double tolerance)
        {
            List<Line> result = new List<Line>();
            if (curves == null)
                return result;

            foreach (Curve curve in curves)
            {
                if (curve == null)
                    continue;

                Line line = new Line(curve.PointAtStart, curve.PointAtEnd);

                if (line.IsValid && line.Length > tolerance)
                    result.Add(line);
            }

            return result;
        }

        private static Plane FitPlane(IEnumerable<Line> lines)
        {
            List<Point3d> points = new List<Point3d>();
            foreach (Line line in lines)
            {
                points.Add(line.From);
                points.Add(line.To);
            }

            Plane plane;
            PlaneFitResult result = Plane.FitPlaneToPoints(points, out plane);
            if (result == PlaneFitResult.Failure || !plane.IsValid)
                plane = Plane.WorldXY;

            return plane;
        }

        private static List<Line2dData> Build2dLines(IEnumerable<Line> lines, Plane plane, double tolerance)
        {
            List<Line2dData> result = new List<Line2dData>();
            List<Point2d> endpoints = new List<Point2d>();

            foreach (Line line in lines)
            {
                Point2d a = ToPlanePoint(plane, line.From);
                Point2d b = ToPlanePoint(plane, line.To);
                Vector2d direction = b - a;
                if (direction.Length <= tolerance)
                    continue;

                direction.Unitize();
                Point2d midpoint = new Point2d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
                result.Add(new Line2dData(a, direction, midpoint));
                endpoints.Add(a);
                endpoints.Add(b);
            }

            if (result.Count == 0)
                return result;

            Point2d center = AveragePoint(endpoints);
            foreach (Line2dData item in result)
            {
                Point2d closest = ClosestPointOnLine(item.Point, item.Direction, center);
                Vector2d side = closest - center;
                if (side.Length <= tolerance)
                    side = item.Midpoint - center;

                item.SortAngle = side.Length > tolerance
                    ? Math.Atan2(side.Y, side.X)
                    : Math.Atan2(item.Direction.Y, item.Direction.X);
            }

            return result
                .OrderBy(item => item.SortAngle)
                .ToList();
        }

        private static List<Point3d> BuildPolygonVertices(List<Line2dData> data, Plane plane, double tolerance)
        {
            List<Point3d> vertices = new List<Point3d>();
            for (int i = 0; i < data.Count; i++)
            {
                Line2dData current = data[i];
                Line2dData next = data[(i + 1) % data.Count];

                Point2d intersection;
                if (!TryIntersectLines(current.Point, current.Direction, next.Point, next.Direction, tolerance, out intersection))
                    throw new InvalidOperationException("相邻边线存在平行或近似平行，无法形成唯一角点。");

                Point3d worldPoint = plane.PointAt(intersection.X, intersection.Y);
                if (vertices.Count == 0 || worldPoint.DistanceTo(vertices[vertices.Count - 1]) > tolerance)
                    vertices.Add(worldPoint);
            }

            if (vertices.Count >= 2 && vertices[0].DistanceTo(vertices[vertices.Count - 1]) <= tolerance)
                vertices.RemoveAt(vertices.Count - 1);

            if (vertices.Count < 3)
                throw new InvalidOperationException("生成的有效顶点少于 3 个。");

            if (SignedArea(vertices, plane) < 0.0)
                vertices.Reverse();

            return vertices;
        }

        private static Point2d ToPlanePoint(Plane plane, Point3d point)
        {
            double u;
            double v;
            plane.ClosestParameter(point, out u, out v);
            return new Point2d(u, v);
        }

        private static Point2d AveragePoint(IEnumerable<Point2d> points)
        {
            double x = 0.0;
            double y = 0.0;
            int count = 0;

            foreach (Point2d point in points)
            {
                x += point.X;
                y += point.Y;
                count++;
            }

            return count == 0 ? Point2d.Unset : new Point2d(x / count, y / count);
        }

        private static Point2d ClosestPointOnLine(Point2d point, Vector2d direction, Point2d target)
        {
            Vector2d offset = target - point;
            double t = offset * direction;
            return point + direction * t;
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

        private static double SignedArea(List<Point3d> vertices, Plane plane)
        {
            double area = 0.0;
            for (int i = 0; i < vertices.Count; i++)
            {
                Point2d a = ToPlanePoint(plane, vertices[i]);
                Point2d b = ToPlanePoint(plane, vertices[(i + 1) % vertices.Count]);
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

                    using (Pen linePen = new Pen(Color.FromArgb(48, 48, 48), 2.2f))
                    using (Pen polygonPen = new Pen(Color.FromArgb(36, 108, 52), 1.8f))
                    using (Pen cutPen = new Pen(Color.FromArgb(98, 47, 12), 1.0f))
                    using (SolidBrush polygonBrush = new SolidBrush(Color.FromArgb(120, 126, 207, 130)))
                    using (SolidBrush cutBrush = new SolidBrush(Color.FromArgb(240, 122, 34)))
                    {
                        linePen.StartCap = LineCap.Round;
                        linePen.EndCap = LineCap.Round;

                        graphics.DrawLine(linePen, 3.0f, 18.0f, 9.8f, 2.8f);
                        graphics.DrawLine(linePen, 5.6f, 6.2f, 22.0f, 9.6f);
                        graphics.DrawLine(linePen, 18.8f, 3.6f, 15.6f, 21.2f);
                        graphics.DrawLine(linePen, 1.8f, 16.4f, 20.4f, 21.2f);

                        PointF[] polygon =
                        {
                            new PointF(6.0f, 15.6f),
                            new PointF(8.6f, 6.0f),
                            new PointF(18.0f, 8.2f),
                            new PointF(15.2f, 18.2f)
                        };

                        graphics.FillPolygon(polygonBrush, polygon);
                        graphics.DrawPolygon(polygonPen, polygon);

                        DrawCutMark(graphics, cutBrush, cutPen, 7.6f, 4.9f);
                        DrawCutMark(graphics, cutBrush, cutPen, 17.0f, 7.1f);
                        DrawCutMark(graphics, cutBrush, cutPen, 14.2f, 17.2f);
                        DrawCutMark(graphics, cutBrush, cutPen, 5.0f, 14.6f);
                    }
                }

                return bitmap;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("3174EA0C-6F94-4D01-8B0B-E2B7C82B5D55"); }
        }

        private sealed class Line2dData
        {
            public Line2dData(Point2d point, Vector2d direction, Point2d midpoint)
            {
                Point = point;
                Direction = direction;
                Midpoint = midpoint;
            }

            public Point2d Point { get; private set; }
            public Vector2d Direction { get; private set; }
            public Point2d Midpoint { get; private set; }
            public double SortAngle { get; set; }
        }

        private static void DrawCutMark(Graphics graphics, Brush brush, Pen pen, float x, float y)
        {
            RectangleF rect = new RectangleF(x - 1.4f, y - 1.4f, 2.8f, 2.8f);
            graphics.FillRectangle(brush, rect);
            graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }
}
