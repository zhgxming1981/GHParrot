using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class Statistics_number : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Statistics_number class.
        /// </summary>
        public Statistics_number()
          : base("统计Num", "统计Num",
              "统计不同数字的数量",
              "Parrot", "数据")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("数字列表", "Num", "数字列表", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("元素", "E", "元素列表", GH_ParamAccess.list);
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
            List<double> number = new List<double>();
            if (!DA.GetDataList(0, number)) { return; }
            number.Sort();

            //去除重复项
            HashSet<double> number2 = new HashSet<double>(number);


            //以下是计算每个元素的数量
            int count = number.Count;
            if (count == 0)
            {
                return;
            }
            List<int> retVal = new List<int>();
            int j = 1;
            for (int i = 0; i < count - 1; i++)
            {
                if (number[i] != number[i + 1])
                {
                    retVal.Add(j);
                    j = 0;
                }
                j++;
            }
            retVal.Add(j);

            //输出数据
            DA.SetDataList(0, number2);
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
            get { return new Guid("873ADD35-8E99-42E5-8527-4603FCB336FF"); }
        }
    }
}