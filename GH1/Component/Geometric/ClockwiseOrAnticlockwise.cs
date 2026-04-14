using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class ClockwiseOrAnticlockwise : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ClockwiseOrAnticlockwise class.
        /// </summary>
        public ClockwiseOrAnticlockwise()
          : base("顺/逆时针", "顺/逆时针",
              "判断给定的2点相对于参考面是顺时针还是逆时针",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("P1", "P1", "第一个点", GH_ParamAccess.item);
            pManager.AddPointParameter("P2", "P2", "第二个点", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Plane", "Plane", "参考面", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("结果", "结果", "是表示顺时针，否表示逆时针", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Point3d P1 = new Point3d();
            if (!DA.GetData(0, ref P1)) { return; }

            Point3d P2 = new Point3d();
            if (!DA.GetData(1, ref P2)) { return; }

            Plane PL = new Plane();
            if (!DA.GetData(2, ref PL)) { return; }

            ////Point3d O = pla.Origin; 
            ////Point3d q1 = CMath.TransformPoint_W2UCS(P1, PL);
            ////Point3d q2 = CMath.TransformPoint_W2UCS(P2, PL);
            //Point3d q1 = MyTransform.PointToUCS(P1, PL);
            //Point3d q2 = MyTransform.PointToUCS(P2, PL);


            ////double m = (q2.X - q1.X) * (O.Y - q1.Y) - (q2.Y - q1.Y) * (O.X - q1.X);
            //double m = (q2.X - q1.X) * (0 - q1.Y) - (q2.Y - q1.Y) * (0 - q1.X);
            //bool? A;
            //if (m < 0)
            //    A = true;
            //else if (m > 0)
            //    A = false;
            //else
            //    A = null;
            bool? A = CMath.ClockwiseOrAnticlockwise(P1, P2, PL);

            DA.SetData(0, A);
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
            get { return new Guid("2582CB06-DA67-4577-99C7-6D7EB066F9C7"); }
        }
    }
}