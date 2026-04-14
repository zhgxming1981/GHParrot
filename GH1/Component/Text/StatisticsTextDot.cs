using System;
using System.Collections.Generic;
using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace NS_Parrot
{
    public class StatisticsTextDot : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the StatisticsDotText class.
        /// </summary>
        public StatisticsTextDot()
          : base("StatisticsDotText", "统计TextDot",
              "Description",
              "Parrot", "文本")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "TextDot的Guid", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("文本", "Txt", "TextDot的文本", GH_ParamAccess.item);
            pManager.AddPointParameter("坐标", "point", "TextDot的坐标", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            GH_Guid guid = null;
            if (!DA.GetData(0, ref guid)) { return; }

            Rhino.DocObjects.RhinoObject obj = RhinoDoc.ActiveDoc.Objects.Find(guid.Value);
            if (obj is Rhino.DocObjects.TextDotObject)
            {
                TextDotObject dotObject = (TextDotObject)obj;
                Rhino.Geometry.TextDot textDot = (Rhino.Geometry.TextDot)dotObject.Geometry;
                DA.SetData(0, textDot.Text);
                DA.SetData(1, textDot.Point);
            }

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
            get { return new Guid("4F25D172-38FC-4D2B-9131-EA639724A2B9"); }
        }
    }
}