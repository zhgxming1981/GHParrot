using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction;
using parrot.Properties;

namespace NS_Parrot
{
    public class CreatPlaneWithYZ : GH_Component
    {

        /// <summary>
        /// Initializes a new instance of the CreatPlaneWithYZ class.
        /// </summary>
        public CreatPlaneWithYZ()
          : base("CreatPlaneWithYZ", "PlaneOYZ",
              "通过yz轴生成平面",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("原点", "O", "原点", GH_ParamAccess.item);
            pManager.AddVectorParameter("y轴", "y", "y轴", GH_ParamAccess.item);
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

            Vector3d Vy = new Vector3d();
            if (!DA.GetData(1, ref Vy)) { return; }

            Vector3d Vz = new Vector3d();
            if (!DA.GetData(2, ref Vz)) { return; }

            if (!Vy.Unitize())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "y轴不能为零向量。");
                return;
            }

            if (!Vz.Unitize())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "z轴不能为零向量。");
                return;
            }

            Vector3d Vx = Vector3d.CrossProduct(Vy, Vz);
            if (!Vx.Unitize())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "y轴和z轴不能平行。");
                return;
            }

            Plane pl = new Plane(origin, Vx, Vy);

            if (pl.Normal * Vz < 0)
            {
                pl = new Plane(origin, -Vx, Vy);
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
                return Resources.OYZ;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("E2E309E2-5EAA-47A8-870E-808D1917BD2A"); }
        }
    }
}
