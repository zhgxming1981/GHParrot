using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class TransformVectorToUCS : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the TransformVectorToUCS class.
        /// </summary>
        public TransformVectorToUCS()
          : base("TransformVectorToUCS", "V2U",
              "将世界坐标系中的向量转换到用户坐标系中",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddVectorParameter("向量", "wV", "世界坐标系描述的向量", GH_ParamAccess.item);
            pManager.AddPlaneParameter("平面", "UCS", "用户坐标系", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("向量", "uV", "用户坐标系描述的向量", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Vector3d V1 = new Vector3d();
            if (!DA.GetData(0, ref V1)) { return; }

            Plane PL = new Plane();
            if (!DA.GetData(1, ref PL)) { return; }

            Vector3d V2 = MyTransform.VectorToUCS(V1, PL);
            DA.SetData(0, V2);
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
            get { return new Guid("2715B909-9F16-4C5E-8AB4-43E8BDE531B0"); }
        }
    }
}