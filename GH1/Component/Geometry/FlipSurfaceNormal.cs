using CommonFunction;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;

namespace NS_Parrot
{
    public class FlipSurfaceNormal : GH_Component
    {
        public FlipSurfaceNormal()
          : base("按向量翻转曲面法向", "翻转法向",
              "当曲面中心处的法向与输入向量反向时，翻转曲面的 U 方向。",
              "Parrot", "Geometry")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddSurfaceParameter("曲面", "S", "需要判断并可能翻转的曲面。", GH_ParamAccess.item);
            pManager.AddVectorParameter("向量", "V", "用来判断曲面法向方向的参考向量。", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddSurfaceParameter("曲面", "S", "法向与参考向量同向的曲面。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality()) return;

            Surface surface = null;
            Vector3d vector = Vector3d.Unset;
            if (!DA.GetData(0, ref surface) || surface == null) return;
            if (!DA.GetData(1, ref vector) || !vector.IsValid || vector.IsZero)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "请输入有效的非零参考向量。");
                return;
            }

            Interval uDomain = surface.Domain(0);
            Interval vDomain = surface.Domain(1);
            double u = (uDomain.T0 + uDomain.T1) * 0.5;
            double v = (vDomain.T0 + vDomain.T1) * 0.5;
            Vector3d normal = surface.NormalAt(u, v);
            double dot = normal * vector;

            Surface result = surface;
            if (dot < 0)
            {
                result = surface.Reverse(0);
                if (result == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "曲面翻转失败。");
                    return;
                }
            }

            DA.SetData(0, result);
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Sketch");

        public override Guid ComponentGuid => new Guid("B57E3956-D529-41F5-BBB9-987FF72D7F76");
    }
}
