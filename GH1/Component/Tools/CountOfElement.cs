using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Tekla.Structures.ModelInternal;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class CountOfElement : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CountOfElement class.
        /// </summary>
        public CountOfElement()
          : base("CountOfElement", "CountOfElement",
              "统计指定元素数量",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("FindIn", "FindIn", "在哪里找", GH_ParamAccess.list);
            pManager.AddTextParameter("FindWhat", "FindWhat", "找什么", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("CountList", "CountList", "对应于FindWhat的列表", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<string> findIn = new List<string>();
            if (!DA.GetDataList(0, findIn)) { return; }

            List<string> findWhat = new List<string>();
            if (!DA.GetDataList(1, findWhat)) { return; }

            List<int> count = new List<int>();
            foreach (var item in findWhat)
            {
                int i = 0;
                foreach (var item2 in findIn)
                {
                    if (item == item2)
                    {
                        i++;
                    }
                }
                count.Add(i);
            }

            DA.SetDataList(0, count);
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
            get { return new Guid("6863E9D8-53FB-46E7-BB24-7797B58437D2"); }
        }
    }
}