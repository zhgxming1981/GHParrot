using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class TransformTheVectorToWCS : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the TransformTheVectorToWCS class.
        /// </summary>
        public TransformTheVectorToWCS()
          : base("TransformTheVectorToWCS", "V2W",
              "将用户坐标系中的向量转换到世界坐标系中",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddVectorParameter("向量", "uV", "用户坐标系描述的向量", GH_ParamAccess.item);
            pManager.AddPlaneParameter("平面", "UCS", "用户坐标系", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddVectorParameter("向量", "wV", "世界坐标系描述的向量", GH_ParamAccess.item);
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

            Vector3d V2 = MyTransform.VectorToWCS(V1, PL);
            DA.SetData(0, V2);
        }


        private double DotX(Vector3d a, Vector3d b) //向量点乘
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
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
            get { return new Guid("EDD1B73D-69D4-464D-BE45-6C73D918EA97"); }
        }
    }
}