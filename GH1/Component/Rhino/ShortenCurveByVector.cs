using CommonFunction;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;

namespace NS_Parrot
{
    public class ShortenCurveByVector : GH_Component
    {
        public ShortenCurveByVector()
          : base("按方向缩短曲线", "方向缩短",
              "根据方向向量确定曲线起终点，并按指定长度缩短或延伸直线。",
              "Parrot", "Geometry")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("曲线", "曲线", "要处理的曲线。", GH_ParamAccess.item);
            pManager.AddVectorParameter("方向", "方向", "用于确定起点到终点方向的非零向量。", GH_ParamAccess.item);
            pManager.AddNumberParameter("起点缩短", "起点缩短", "起点沿曲线方向缩短的长度；直线可为负值以向外延伸。", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("终点缩短", "终点缩短", "终点沿曲线反方向缩短的长度；直线可为负值以向外延伸。", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("结果", "结果", "处理后的曲线。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Curve curve = null;
            Vector3d direction = Vector3d.Unset;
            double startShorten = 0.0;
            double endShorten = 0.0;

            if (!DA.GetData(0, ref curve))
                return;
            if (!DA.GetData(1, ref direction))
                return;
            if (!DA.GetData(2, ref startShorten))
                return;
            if (!DA.GetData(3, ref endShorten))
                return;

            if (curve == null || !curve.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "请输入有效曲线。");
                return;
            }

            if (!direction.IsValid || direction.IsZero)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "请输入非零向量，用于确定起点到终点的方向。");
                return;
            }

            Curve workingCurve = curve.DuplicateCurve();
            Vector3d curveDirection = workingCurve.PointAtEnd - workingCurve.PointAtStart;
            if (Vector3d.Multiply(curveDirection, direction) < 0)
                workingCurve.Reverse();

            if (workingCurve.IsLinear())
            {
                Line line = new Line(workingCurve.PointAtStart, workingCurve.PointAtEnd);
                if (line.Length <= RhinoMath.ZeroTolerance)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "直线长度为零。");
                    return;
                }

                Vector3d lineDirection = line.Direction;
                lineDirection.Unitize();

                Point3d newStart = line.From + lineDirection * startShorten;
                Point3d newEnd = line.To - lineDirection * endShorten;
                if ((newEnd - newStart).Length <= RhinoMath.ZeroTolerance)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "缩短后的直线长度不能小于或等于零。");
                    return;
                }

                DA.SetData(0, new LineCurve(newStart, newEnd));
                return;
            }

            if (startShorten < 0 || endShorten < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "非直线曲线的缩短量不能为负值。");
                return;
            }

            double totalLength = workingCurve.GetLength();
            if (startShorten + endShorten >= totalLength)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "起点和终点缩短量之和必须小于曲线总长度。");
                return;
            }

            double startParameter;
            double endParameter;
            bool hasStartParameter = workingCurve.LengthParameter(startShorten, out startParameter);
            bool hasEndParameter = workingCurve.LengthParameter(totalLength - endShorten, out endParameter);
            if (!hasStartParameter || !hasEndParameter)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法计算曲线缩短位置。");
                return;
            }

            Curve result = workingCurve.Trim(startParameter, endParameter);
            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法生成缩短后的曲线。");
                return;
            }

            DA.SetData(0, result);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ShortenCurveByVector"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("B5933D15-5EBE-4D75-9D78-4EA1E21B2B14"); }
        }
    }
}
