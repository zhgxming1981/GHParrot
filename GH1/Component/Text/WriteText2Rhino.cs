using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;
using Rhino;

namespace NS_Parrot
{
    public class WriteText2Rhino : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the WriteText2Rhino class.
        /// </summary>
        public WriteText2Rhino()
          : base("WriteText2Rhino", "向Rhino写文字",
              "向Rhino写文字",
              "Parrot", "Rhino")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文字", "文字", "文字", GH_ParamAccess.item);
            pManager.AddPlaneParameter("平面", "平面", "平面", GH_ParamAccess.item);
            pManager.AddNumberParameter("字高", "字高", "字高", GH_ParamAccess.item);
            pManager.AddBooleanParameter("写入", "写入", "写入", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("文字", "文字", "文字", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            bool isWrite=false;
            if (!DA.GetData(3, ref isWrite)) { return; }
            if (!isWrite) { return; }

            string text="" ;
            if (!DA.GetData(0, ref text)) { return; }

            Plane plane = new Plane();
            if (!DA.GetData(1, ref plane)) { return; }

            double  height= 1;
            if (!DA.GetData(2, ref height)) { return; }

            Guid guid = RhinoDoc.ActiveDoc.Objects.AddText(text, plane, height, "宋体", false, false, TextJustification.Center);
            DA.SetData(0, guid);
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
            get { return new Guid("3F767B55-AA4B-4C69-91C8-F60412EDF9F1"); }
        }
    }
}