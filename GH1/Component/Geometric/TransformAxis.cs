using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class TransformThePointToWCS : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the TransformAxis class.
        /// </summary>
        public TransformThePointToWCS()
          : base("TransformThePointToWCS", "P2W",
              "将用户坐标系中的点转换到世界坐标系中",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("点", "uPt", "用户坐标系描述的点", GH_ParamAccess.item);
            pManager.AddPlaneParameter("平面", "UCS", "用户坐标系", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("世界坐标系下的点", "wPt", "世界坐标系描述的点", GH_ParamAccess.item);
        }



        /// <summary>
        /// 将局部坐标转换会世界坐标
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

            Point3d P2 = MyTransform.PointToWCS(P1, PL);
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
                return  null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("6EACB986-BE0B-4F12-A996-507842CE0C15"); }
        }
    }
}