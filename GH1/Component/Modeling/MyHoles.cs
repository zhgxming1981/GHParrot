using System;
using System.Collections.Generic;
using rd = Rhino.NodeInCode;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class MyHoles : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyHoles class.
        /// </summary>
        public MyHoles()
          : base("MyHoles", "Nickname",
              "Description",
              "Parrot", "建模")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("被开孔的实体", "Brep", "被开孔的实体", GH_ParamAccess.item);
            pManager.AddBrepParameter("被开孔的实体表面索引号", "index", "被开孔的实体表面索引号", GH_ParamAccess.item);

            pManager.AddBrepParameter("孔（实体）", "Hole", "要开孔的实体", GH_ParamAccess.list);
            pManager.AddCurveParameter("横梁参考线", "crv", "横梁参考线", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("被开孔的实体", "Brep", "被开孔的实体", GH_ParamAccess.item);
            pManager.AddPointParameter("平面与曲线交点", "intersection", "平面与曲线交点", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            if (!CHardware.CheckLegality())
                return;


            Brep brep = null;
            if (!DA.GetData(0, ref brep)) { return; }

            int index = -1;
            if (!DA.GetData(1, ref index)) { return; }

            List<Brep> holes = new List<Brep>();
            if (!DA.GetDataList(2, holes)) { return; }

            Curve crv = null;
            if (!DA.GetData(3, ref crv)) { return; }

            var func_info1 = rd.Components.FindComponent("DeconstructBrep");//分解曲面
            var func1 = func_info1.Delegate as dynamic;
            Surface[] surfaces = func1(brep)[0];

            var func_info2 = rd.Components.FindComponent("Group");//成组
            var func2 = func_info2.Delegate as dynamic;
            var group = func1(holes);

            var func_info3 = rd.Components.FindComponent("BoundingBox");//成组
            var func3 = func_info3.Delegate as dynamic;
            var boundingBox = func1(group)[0];

            Point3d orign_holes = boundingBox;
            Plane plane_holes = new Plane(orign_holes, new Vector3d(0, 0, 1));

            int count_holes = holes.Count;

            Plane plane_Brep;
            if (surfaces[index].TryGetPlane(out plane_Brep))
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "第" + index + "surface不是平面");
                return;
            }


            List<Point3d> point_inter = new List<Point3d>();
            IEnumerator<IntersectionEvent> enum_inter;
            Rhino.Geometry.Intersect.CurveIntersections cvr_inter;

            cvr_inter = Rhino.Geometry.Intersect.Intersection.CurvePlane(crv, plane_Brep, 0.01);
            enum_inter = cvr_inter.GetEnumerator();
            while (enum_inter.MoveNext())
            {
                Point3d pt = enum_inter.Current.PointA;
                double t;
                if (crv.ClosestPoint(pt, out t, 0.001))
                {
                    point_inter.Add(pt);
                }
            }


            List<Plane> plane_inter = new List<Plane>();


            DA.SetDataList(0, point_inter);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("63862F8D-C4CC-4777-A24B-368A2E669186"); }
        }
    }
}