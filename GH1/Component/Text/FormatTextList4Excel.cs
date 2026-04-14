using System;
using System.Collections.Generic;
using System.Text;
using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace NS_Parrot
{
    public class FormatTextList4Excel : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GetUserStringByKey class.
        /// </summary>
        public FormatTextList4Excel()
          : base("格式化文本", "格式化文本",
              "将列表格式转化为Excel写入格式",
              "Parrot", "ExcelCAD")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文本列表", "文本列表", "文本列表", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Value", "Value", "Value", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<string> textList = new List<string>();
            DA.GetDataList(0, textList);

            DA.SetData(0, FormatTextList(textList));
        }
        string FormatTextList(List<string> title)
        {
            int count = title.Count;
            if (count == 0)
            {
                return "";
            }
            StringBuilder retval = new StringBuilder();
            for (int i = 0; i < count - 1; i++)
            {
                retval.Append(title[i]);
                retval.Append("|");
            }
            retval.Append(title[count - 1]);
            return retval.ToString();
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
            get { return new Guid("D364DCAA-1F99-426B-A70F-5A27F39CB2A6"); }
        }
    }
}