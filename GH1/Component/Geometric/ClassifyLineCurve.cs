using CommonFunction;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace NS_Parrot
{
    public class ClassifyLineCurve : GH_Component
    {
        public ClassifyLineCurve()
          : base("ClassifyLineCurve", "判断直线曲线",
              "在指定精度下判断曲线是否为直线，并按原列表序号分类输出。",
              "Parrot", "几何")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("曲线", "C", "要判断是否为直线的曲线列表", GH_ParamAccess.list);
            pManager.AddNumberParameter("判断精度", "T", "判断精度；小于等于0时使用Rhino文档绝对容差", GH_ParamAccess.item, 0.0);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("直线", "L", "判断为直线的曲线", GH_ParamAccess.list);
            pManager.AddIntegerParameter("直线序号", "LI", "直线在原列表中的序号", GH_ParamAccess.list);
            pManager.AddCurveParameter("曲线", "C", "判断为非直线的曲线", GH_ParamAccess.list);
            pManager.AddIntegerParameter("曲线序号", "CI", "非直线曲线在原列表中的序号", GH_ParamAccess.list);
            pManager.AddBooleanParameter("是否直线", "B", "与输入曲线一一对应；是直线输出True，否则输出False", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<Curve> curves = new List<Curve>();
            double tolerance = 0.0;

            if (!DA.GetDataList(0, curves))
                return;
            DA.GetData(1, ref tolerance);
            tolerance = ResolveTolerance(tolerance);

            List<Curve> lines = new List<Curve>();
            List<int> lineIndices = new List<int>();
            List<Curve> nonLines = new List<Curve>();
            List<int> nonLineIndices = new List<int>();
            List<bool> flags = new List<bool>();

            for (int i = 0; i < curves.Count; i++)
            {
                Curve curve = curves[i];
                bool isLine = IsLineLikeCurve(curve, tolerance);
                flags.Add(isLine);

                if (isLine)
                {
                    lines.Add(curve);
                    lineIndices.Add(i);
                }
                else
                {
                    nonLines.Add(curve);
                    nonLineIndices.Add(i);
                }
            }

            DA.SetDataList(0, lines);
            DA.SetDataList(1, lineIndices);
            DA.SetDataList(2, nonLines);
            DA.SetDataList(3, nonLineIndices);
            DA.SetDataList(4, flags);
        }

        private static double ResolveTolerance(double tolerance)
        {
            if (tolerance > 0.0)
                return tolerance;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            return doc == null ? 0.001 : doc.ModelAbsoluteTolerance;
        }

        private static bool IsLineLikeCurve(Curve curve, double tolerance)
        {
            if (curve == null || !curve.IsValid)
                return false;

            Point3d start = curve.PointAtStart;
            Point3d end = curve.PointAtEnd;
            Line reference = new Line(start, end);
            if (!reference.IsValid || reference.Length <= tolerance)
                return false;

            double length = curve.GetLength();
            if (Math.Abs(length - reference.Length) > tolerance)
                return false;

            return MaxDistanceToLine(curve, reference) <= tolerance;
        }

        private static double MaxDistanceToLine(Curve curve, Line reference)
        {
            double maxDistance = 0.0;
            const int sampleCount = 32;
            double start = curve.Domain.T0;
            double end = curve.Domain.T1;

            for (int i = 0; i <= sampleCount; i++)
            {
                double t = start + (end - start) * i / sampleCount;
                Point3d point = curve.PointAt(t);
                Point3d closest = reference.ClosestPoint(point, false);
                double distance = point.DistanceTo(closest);
                if (distance > maxDistance)
                    maxDistance = distance;
            }

            Curve[] segments = curve.DuplicateSegments();
            if (segments != null)
            {
                foreach (Curve segment in segments)
                {
                    if (segment == null)
                        continue;

                    UpdateMaxDistance(segment.PointAtStart, reference, ref maxDistance);
                    UpdateMaxDistance(segment.PointAtEnd, reference, ref maxDistance);
                    segment.Dispose();
                }
            }

            return maxDistance;
        }

        private static void UpdateMaxDistance(Point3d point, Line reference, ref double maxDistance)
        {
            Point3d closest = reference.ClosestPoint(point, false);
            double distance = point.DistanceTo(closest);
            if (distance > maxDistance)
                maxDistance = distance;
        }

        protected override Bitmap Icon
        {
            get { return null; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("59C06A7D-EF9D-442C-B759-2C87B34D29A8"); }
        }
    }
}
