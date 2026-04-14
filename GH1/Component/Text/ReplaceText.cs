using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class ReplaceText : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ReplaceText class.
        /// </summary>
        public ReplaceText()
          : base("ReplaceText", "ReplaceText",
              "替换文本",
              "Parrot", "文本")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("待替换的文本", "Txt", "待替换的文本", GH_ParamAccess.list);
            pManager.AddTextParameter("待替换的内容", "Find", "待替换的内容", GH_ParamAccess.list);
            pManager.AddTextParameter("替换后的内容", "Replaced", "待替换的内容", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("替换后的文本", "Txt", "替换后的文本", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;


            List<string> InHere = new List<string>();
            if (!DA.GetDataList(0, InHere)) { return; }

            List<string> FindTxt = new List<string>();
            if (!DA.GetDataList(1, FindTxt)) { return; }

            List<string> Replaced = new List<string>();
            if (!DA.GetDataList(2, Replaced)) { return; }

            int count_find = FindTxt.Count;
            int count_inHere=InHere .Count; 
            for (int i = 0; i < count_find; i++)
            {
                for(int j = 0; j < count_inHere; j++)
                {
                    if (InHere[j].Contains(FindTxt[i]))
                    {
                        InHere[j]=InHere[j].Replace(FindTxt[i], Replaced[i]);
                    }
                }
            }

            DA.SetDataList(0, InHere);
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
                //return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("C4A133FB-ADCF-4516-BB30-136E223B0CE4"); }
        }
    }
}