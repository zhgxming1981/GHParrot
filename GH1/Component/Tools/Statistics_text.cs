using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class Statistics_text : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Statistics_text class.
        /// </summary>
        public Statistics_text()
          : base("统计字符", "统计字符",
              "统计不同字符的数量",
              "Parrot", "数据")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文本列表", "Txt", "文本列表", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("元素", "E", "元素列表", GH_ParamAccess.list);
            pManager.AddNumberParameter("数量", "C", "元素数量列表", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            //读取数据
            List<string> text = new List<string>();
            if (!DA.GetDataList(0, text)) { return; }
            text.Sort();

            //去除重复项
            HashSet<string> text2 = new HashSet<string>(text);


            //以下是计算每个元素的数量
            int count = text.Count;
            if (count == 0)
            {
                return;
            }
            List<int> retVal = new List<int>();
            int j = 1;
            for (int i = 0; i < count - 1; i++)
            {
                if (text[i] != text[i + 1])
                {
                    retVal.Add(j);
                    j = 0;
                }
                j++;
            }
            retVal.Add(j);

            //输出数据
            DA.SetDataList(0, text2);
            DA.SetDataList(1, retVal);

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
            get { return new Guid("452999E8-3D71-4EE5-B998-16B2E80F087B"); }
        }
    }
}