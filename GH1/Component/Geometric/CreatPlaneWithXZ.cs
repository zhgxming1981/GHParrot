using CommonFunction;
using Grasshopper.Kernel;
using parrot.Properties;
using Rhino.Geometry;
using System;

namespace NS_Parrot
{
    public class CreatPlaneWithXZ : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CreatPlaneWithXZ class.
        /// </summary>
        public CreatPlaneWithXZ()
          : base("CreatPlaneWithXZ", "PlaneOXZ",
              "通过xz轴生成平面",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("原点", "O", "原点", GH_ParamAccess.item);
            pManager.AddVectorParameter("x轴", "x", "x轴", GH_ParamAccess.item);
            pManager.AddVectorParameter("z轴", "z", "z轴", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPlaneParameter("新平面", "PL", "新平面", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Point3d origin = new Point3d();
            if (!DA.GetData(0, ref origin)) { return; }

            Vector3d Vx = new Vector3d();
            if (!DA.GetData(1, ref Vx)) { return; }

            Vector3d Vz = new Vector3d();
            if (!DA.GetData(2, ref Vz)) { return; }

            if (!Vx.Unitize())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "x轴不能为零向量。");
                return;
            }

            if (!Vz.Unitize())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "z轴不能为零向量。");
                return;
            }

            Vector3d Vy = Vector3d.CrossProduct(Vz, Vx);
            if (!Vy.Unitize())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "x轴和z轴不能平行。");
                return;
            }

            Plane pl = new Plane(origin, Vx, Vy);

            if (pl.Normal * Vz < 0)
            {
                pl = new Plane(origin, Vx, -Vy);
            }

            DA.SetData(0, pl);
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
                //return null;
                return Resources.OXZ;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("6B413D5D-2E6A-4421-A64F-EB592E9CBA27"); }
        }
    }
}
