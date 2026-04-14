using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System.Diagnostics;
using Grasshopper.Kernel.Types.Transforms;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{





    public class CreateTextDot : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyTag2 class.
        /// </summary>
        public CreateTextDot()
          : base("生成TextDot", "生成TextDot",
              "直接生成TextDot",
              "Parrot", "Rhino")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("位置", "位置", "文字位置", GH_ParamAccess.item);
            pManager.AddTextParameter("文字", "文字", "文字内容", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {

            pManager.AddGenericParameter("文字", "文字", "文字内容", GH_ParamAccess.item);
        }



        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Point3d location = new Point3d();
            if (!DA.GetData(0, ref location)) { return; }

            string text = "";
            DA.GetData(1, ref text);

            Rhino.Geometry.TextDot dot = new Rhino.Geometry.TextDot(text, location);
            dot.FontHeight = 20;
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            doc.Objects.Add(dot);
            DA.SetData(0, dot);
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
            get { return new Guid("6A0F6FAC-C51F-40C0-9248-FAA8B50DD742"); }
        }
    }
}