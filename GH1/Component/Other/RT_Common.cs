using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class RT_Common : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the RT_Common class.
        /// </summary>
        public RT_Common()
          : base("RT_Common", "RT_Common",
              "any object",
              "Parrot", "杂项")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("I", "I", "任意类型的数据", GH_ParamAccess.item);
            //pManager.AddGenericParameter("Data2", "Data2", "任意类型的数据", GH_ParamAccess.item);
            pManager[0].Optional = true;
            //pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("O", "O", "任意类型的数据", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            object A = null;
            DA.GetData(0, ref A);
            //DA.GetData(1, ref A);
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
            get { return new Guid("AA773983-8E61-4D9B-8BFA-C0E300CB00B4"); }
        }
    }
}