using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;
using parrot.Properties;

namespace NS_Parrot
{
    public class CreatPlaneWithXZ : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CreatPlaneWithXZ class.
        /// </summary>
        public CreatPlaneWithXZ()
          : base("CreatPlaneWithXZ", "PlaneOYZ",
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


            double ax, ay, az;//向量a
            ax = Vx.X;
            ay = Vx.Y;
            az = Vx.Z;
            double bx, by, bz;//向量b
            bx = Vz.X;
            by = Vz.Y;
            bz = Vz.Z;
            //向量c，必须同时和向量a、向量b垂直
            //则 ax*cx + ay*cy + az*cz = 0, bx*cx+by0*cy+ bz*cz = 0
            //并且假定cx+cy+cz=1,可以解出cx,cy,cz
            double cx, cy, cz;//向量c
            cx = (ay * bz - az * by) / (ax * by - ax * bz - ay * bx + ay * bz - az * by + az * bx);
            cy = -(ax * bz - az * bx) / (ax * by - ax * bz - ay * bx + ay * bz - az * by + az * bx);
            cz = (ax * by - ay * bx) / (ax * by - ax * bz - ay * bx + ay * bz - az * by + az * bx);
            Vector3d Vy = new Vector3d(cx, cy, cz);
            Plane pl = new Plane(origin, Vx, Vy);
            if (pl.Normal.X != 0)
            {
                if (Vz.X / pl.Normal.X < 0)//判断新平面的x轴和输入的是否一致，小于0表示不一致，要反向
                {
                    pl = new Plane(origin, Vx, -Vy);
                }
            }

            if (pl.Normal.Z != 0)
            {
                if (Vz.Z / pl.Normal.Z < 0)//判断新平面的z轴和输入的是否一致，小于0表示不一致，要反向
                {
                    pl = new Plane(origin, Vx, -Vy);
                }
            }
            //pl = new Plane(origin, Vx, -Vy);
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