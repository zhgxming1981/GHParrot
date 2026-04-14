using CommonFunction.Hardware;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

namespace NS_Parrot
{
    public class TrueOrFalse : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the TrueOrFalse class.
        /// </summary>
        public TrueOrFalse()
          : base("TrueOrFalse", "T/F",
              "查找True和False对应的索引号",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("BL", "BL", "布尔值列表", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("True", "True", "True的索引号", GH_ParamAccess.list);
            pManager.AddIntegerParameter("False", "False", "False的索引号", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<bool> Input = new List<bool>();
            if (!DA.GetDataList(0, Input)) { return; }



            List<int> Index_true;
            List<int> Index_false;
            Index_true = new List<int>();
            Index_false = new List<int>();

            int count = Input.Count;

            for (int i = 0; i < count; i++)
            {
                if (Input[i])
                {
                    Index_true.Add(i);
                }
                else
                {
                    Index_false.Add(i);
                }
            }

            DA.SetDataList(0, Index_true);
            DA.SetDataList(1, Index_false);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                //return Resources.
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("457646D5-E6B0-45B1-9916-C7BE4BCB0EDD"); }
        }
    }
}