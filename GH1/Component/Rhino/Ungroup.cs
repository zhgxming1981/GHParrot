using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.DocObjects;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class Ungroup : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Ungroup class.
        /// </summary>
        public Ungroup()
          : base("分解组", "分解组",
              "分解组",
              "Parrot", "Rhino")
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGroupParameter("群组", "群组", "群组", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGroupParameter("列表", "列表", "列表", GH_ParamAccess.list);
            pManager.AddGenericParameter("列表", "列表", "列表", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Rhino.DocObjects.Group group = null;
            if (!DA.GetData(0, ref group)) { return; }

            string groupName = group.Name;
            int index = group.Index;

            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            Rhino.DocObjects.Tables.GroupTable gtab = doc.Groups;
            RhinoObject[] robj = gtab.GroupMembers(index);

            DA.SetDataList(0, robj);
            DA.SetData(1, group.Id);
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
            get { return new Guid("6153BF3F-26E1-44D2-9E4F-1F855D1461B1"); }
        }
    }
}