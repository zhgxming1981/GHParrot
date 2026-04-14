using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class Distance4Point2Plane : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Distance4Point2Plane class.
        /// </summary>
        public Distance4Point2Plane()
          : base("点到平面距离", "P2P",
              "点到平面距离",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("P", "Point", "点", GH_ParamAccess.item);
            pManager.AddPlaneParameter("PL", "Plane", "平面", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("距离", "距离", "点到平面的距离", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Point3d P = new Point3d();
            if (!DA.GetData(0, ref P)) { return; }

            Plane PL = new Plane();
            if (!DA.GetData(1, ref PL)) { return; }


            double A = PL.Normal.X;
            double B = PL.Normal.Y;
            double C = PL.Normal.Z;

            double x0 = PL.Origin.X;
            double y0 = PL.Origin.Y;
            double z0 = PL.Origin.Z;
            double x1 = P.X;
            double y1 = P.Y;
            double z1 = P.Z;
            double D = -A * x0 - B * y0 - C * z0;

            double Dist = Math.Abs(A * x1 + B * y1 + C * z1 + D) / Math.Sqrt(A * A + B * B + C * C);
            DA.SetData(0, Dist);
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
            get { return new Guid("C921A066-0960-4E0C-AF98-7C4C2D5546E9"); }
        }
    }
}