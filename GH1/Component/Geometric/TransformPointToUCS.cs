using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class TransformPointToUCS : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the TransformPointToUCS class.
        /// </summary>
        public TransformPointToUCS()
          : base("TransformPointToUCS", "P2U",
              "将世界坐标系中的点变换到用户坐标系中",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("点", "wPt", "世界坐标系描述的点", GH_ParamAccess.item);
            pManager.AddPlaneParameter("平面", "UCS", "用户坐标系", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("点", "uPt", "用户坐标系描述的点", GH_ParamAccess.item);
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

            Plane PL = new Plane();
            if (!DA.GetData(1, ref PL)) { return; }

            Point3d P2 = MyTransform.PointToUCS(P1, PL);
            DA.SetData(0, P2);
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
            get { return new Guid("5E7CD9FF-CAD6-4430-9343-15457407155E"); }
        }
    }
}