using System;
using System.Collections.Generic;
using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace NS_Parrot
{
    public class Text2GUID : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Text2GUID class.
        /// </summary>
        public Text2GUID()
          : base("Text2GUID", "Text2GUID",
              "把文本转换为GUID",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("txt", "txt", "想要转换成GUID的文本", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "Guid", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;
            string guidText = "";
            if (!DA.GetData(0, ref guidText)) { return; }
            Guid guid = new Guid(guidText);
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
            get { return new Guid("AD1EEDD2-5A46-44D4-AE8A-7C12EDE84A21"); }
        }
    }
}