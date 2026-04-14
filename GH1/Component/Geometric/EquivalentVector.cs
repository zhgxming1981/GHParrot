using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using NS_Parrot;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class EquivalentVector : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the EquivalentVector class.
        /// </summary>
        public EquivalentVector()
          : base("等效向量", "等效向量",
              "判断2个向量是否等效",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddVectorParameter("V1", "V1", "向量1", GH_ParamAccess.item);
            pManager.AddVectorParameter("V2", "V2", "向量2", GH_ParamAccess.item);
            pManager.AddNumberParameter("容差", "容差", "容差", GH_ParamAccess.item, 0.001);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("结果", "结果", "是等效，否表示不等效", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Vector3d v1 = new Vector3d();
            if (!DA.GetData(0, ref v1)) { return; }

            Vector3d v2 = new Vector3d();
            if (!DA.GetData(1, ref v2)) { return; }

            double tolerance = 0;
            if (!DA.GetData(2, ref tolerance)) { return; }

            string retVal;
            double[] a = { v1.X, v1.Y, v1.Z };
            double[] b = { v2.X, v2.Y, v2.Z };
            if (CMath.IsEqArray(a, b, tolerance) == 1)
            {
                retVal = "相同";
            }
            else if (CMath.IsEqArray(a, b, tolerance) == -1)
            {
                retVal = "相反";
            }
            else
            {
                retVal = "不同";
            }

            DA.SetData(0, retVal);
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
            get { return new Guid("0BD5851C-F259-42F8-B06B-2B977102F0B7"); }
        }
    }
}