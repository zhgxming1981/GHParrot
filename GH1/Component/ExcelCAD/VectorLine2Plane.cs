using Grasshopper.Kernel;
using Rhino.Geometry;
using System;

namespace NS_Parrot
{
    public class VectorLine2Plane : GH_Component
    {
        public VectorLine2Plane()
          : base("VectorLine2Plane", "Line2Plane",
              "根据有方向的线生成平面：线起点为平面原点，终点指向平面法向",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("代表向量", "Line", "起点为平面原点，终点指向平面法向", GH_ParamAccess.list);
            pManager.AddBooleanParameter("反向", "R", "反向解释线方向", GH_ParamAccess.item, false);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPlaneParameter("平面", "Plane", "由代表向量线生成的平面", GH_ParamAccess.list);
            pManager.AddVectorParameter("法向", "Normal", "平面法向", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            System.Collections.Generic.List<Line> lines = new System.Collections.Generic.List<Line>();
            if (!DA.GetDataList(0, lines))
                return;

            bool reverse = false;
            DA.GetData(1, ref reverse);

            System.Collections.Generic.List<Plane> planes = new System.Collections.Generic.List<Plane>();
            System.Collections.Generic.List<Vector3d> normals = new System.Collections.Generic.List<Vector3d>();
            System.Collections.Generic.List<string> errors = new System.Collections.Generic.List<string>();

            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i];
                if (!line.IsValid || line.Length <= Rhino.RhinoMath.ZeroTolerance)
                {
                    errors.Add($"[{i}] 代表向量线无效或长度为0");
                    continue;
                }

                Point3d origin = reverse ? line.To : line.From;
                Point3d normalPoint = reverse ? line.From : line.To;
                Vector3d normal = normalPoint - origin;

                if (!normal.Unitize())
                {
                    errors.Add($"[{i}] 代表向量线无法生成有效法向");
                    continue;
                }

                Vector3d yAxis = Vector3d.ZAxis;
                if (Math.Abs(Vector3d.Multiply(normal, yAxis)) > 0.999)
                    yAxis = Vector3d.XAxis;

                Vector3d xAxis = Vector3d.CrossProduct(yAxis, normal);
                if (!xAxis.Unitize())
                {
                    errors.Add($"[{i}] 无法构造平面X轴");
                    continue;
                }

                yAxis = Vector3d.CrossProduct(normal, xAxis);
                yAxis.Unitize();

                planes.Add(new Plane(origin, xAxis, yAxis));
                normals.Add(normal);
            }

            if (errors.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", errors));

            DA.SetDataList(0, planes);
            DA.SetDataList(1, normals);
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_VectorLine2Plane");

        public override Guid ComponentGuid => new Guid("A06B1128-6783-4CE8-9ED4-D77F2218832A");
    }
}
