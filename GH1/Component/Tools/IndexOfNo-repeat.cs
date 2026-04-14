using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class IndexOfNo_repeat : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the IndexOfNo_repeat class.
        /// </summary>
        public IndexOfNo_repeat()
          : base("不重复项索引", "不重复项索引",
              "得到一个不含重复项的索引",
              "Parrot", "工具")
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文本", "文本", "文本列表", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("索引", "索引", "索引不重复的索引", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            if (!CHardware.CheckLegality())
                return;

            List<string> str_list = new List<string>();
            if (!DA.GetDataList(0, str_list)) { return; }


            int count = str_list.Count;
            List<int> index_list = new List<int>();
            for (int i = 0; i < count; i++)
            {
                index_list.Add(i);
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (str_list[i] == str_list[j])
                    {
                        index_list.Remove(j);
                    }
                }
            }

            DA.SetDataList(0, index_list);
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
            get { return new Guid("C6978FA0-7804-4C21-A480-8028DAA44C84"); }
        }
    }
}