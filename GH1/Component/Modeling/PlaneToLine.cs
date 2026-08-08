using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Drawing;

namespace NS_Parrot
{
    public class PlaneToLine : GH_Component
    {
        public PlaneToLine()
          : base("平面法向线", "平面法向线",
              "以平面原点为起点，沿平面法向（或反向）生成一段指定长度的线段",
              "Parrot", "Modeling")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("平面", "平面", "参考平面，线段起点为平面原点，方向为平面法向", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("长度", "长度", "线段长度", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("反向", "反向", "True 时线段方向与平面法向相反", GH_ParamAccess.item, false);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("线段", "线段", "以平面原点为起点、沿法向（或反向）延伸指定长度的线段", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Plane plane = Plane.WorldXY;
            double length = 1.0;
            bool reverse = false;

            DA.GetData(0, ref plane);
            DA.GetData(1, ref length);
            DA.GetData(2, ref reverse);

            if (!plane.IsValid)
                return;

            if (length <= 0)
                return;

            Vector3d direction = plane.Normal;
            if (reverse)
                direction.Reverse();

            Point3d start = plane.Origin;
            Point3d end = start + direction * length;
            Line line = new Line(start, end);

            DA.SetData(0, line);
        }

        protected override Bitmap Icon => GeneratedIcon.GetPlaneToLine();

        public override Guid ComponentGuid => new Guid("D3E8F7A2-1B5C-4F9D-A6E2-7C8B0A1D4F3E");
    }
}
