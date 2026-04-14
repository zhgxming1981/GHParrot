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
    public class MinItemInList : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MinItemInList class.
        /// </summary>
        public MinItemInList()
          : base("MinItemInList", "找最小值",
              "找列表中的最小值",
              "Parrot", "工具")
        {
            //base("MaxItemInList", "找最大值",
            //  "找列表中的最大值",
            //  "Parrot", "工具")
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("列表", "Lst", "double类型的列表", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("最小值", "Min", "列表中的最小值", GH_ParamAccess.item);
            pManager.AddIntegerParameter("最小值的索引", "Index", "列表中最小值的索引", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<double> lst = new List<double>();
            if (!DA.GetDataList(0, lst)) { return; }

            if (lst.Count == 0) return;

            double min = lst[0];
            double retIndex = 0;
            int count = lst.Count;
            for (int i = 0; i < count; i++)
            {
                if (lst[i] < min)
                {
                    retIndex = i;
                    min = lst[i];
                }
            }

            DA.SetData(0, min);
            DA.SetData(1, retIndex);
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
                return Resources.min;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("E804C445-6402-4CB7-9837-D87E6096A8DD"); }
        }
    }
}