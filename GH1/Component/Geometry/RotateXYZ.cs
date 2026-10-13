using System;
using CommonFunction;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace NS_Parrot
{
    public class RotateXYZ : GH_Component
    {
        public RotateXYZ()
          : base("XYZ旋转", "XYZ旋转",
              "围绕指定中心，固定依次绕世界 X、Y、Z 方向旋转。角度单位为度，结果与修改参数的顺序无关。",
              "Parrot", "Geometry")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("几何体", "G", "要旋转的几何体。", GH_ParamAccess.item);
            pManager.AddPointParameter("旋转中心", "C", "三轴旋转共用的中心，默认为世界原点。", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("X转角", "X", "绕经过旋转中心的世界 X 方向轴旋转的角度，单位：度。", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Y转角", "Y", "绕经过旋转中心的世界 Y 方向轴旋转的角度，单位：度。", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Z转角", "Z", "绕经过旋转中心的世界 Z 方向轴旋转的角度，单位：度。", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("几何体", "G", "旋转后的几何体。", GH_ParamAccess.item);
            pManager.AddTransformParameter("变换", "T", "本次旋转使用的变换矩阵，可用于对其它几何体执行相同变换。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality()) return;

            IGH_GeometricGoo geometry = null;
            Point3d center = Point3d.Origin;
            double x = 0.0, y = 0.0, z = 0.0;
            if (!DA.GetData(0, ref geometry) || geometry == null) return;
            if (!DA.GetData(1, ref center)) return;
            if (!DA.GetData(2, ref x)) return;
            if (!DA.GetData(3, ref y)) return;
            if (!DA.GetData(4, ref z)) return;

            if (!center.IsValid || !IsFinite(x) || !IsFinite(y) || !IsFinite(z))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "旋转中心必须是有效点，转角必须是有限数值。");
                return;
            }

            // 矩阵从右往左应用，固定先 X、再 Y、最后 Z；不保存上次旋转结果。
            const double degreesToRadians = Math.PI / 180.0;
            Transform rotation =
                Transform.Rotation((z % 360.0) * degreesToRadians, Vector3d.ZAxis, center) *
                Transform.Rotation((y % 360.0) * degreesToRadians, Vector3d.YAxis, center) *
                Transform.Rotation((x % 360.0) * degreesToRadians, Vector3d.XAxis, center);

            // 每次复制输入，避免修改上游几何体或在重复求解时累积旋转。
            IGH_GeometricGoo result = geometry.DuplicateGeometry();
            if (result != null) result = result.Transform(rotation);
            if (result == null || !result.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "几何体旋转失败，请检查输入是否为有效且支持变换的几何体。");
                return;
            }

            DA.SetData(0, result);
            DA.SetData(1, rotation);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("FE85931C-6CBD-455A-BB0F-510B5843D228");
    }
}
