using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Rhino.DocObjects;
using Rhino.Geometry;
using Tekla.Structures.Geometry3d;

namespace NS_Parrot
{
    public class SelectRhinoObjectByKey_Value : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SelectRhinoObjectByKey_Value class.
        /// </summary>
        public SelectRhinoObjectByKey_Value()
          : base("按Uerstring选中", "按Uerstring选中",
              "按Uerstring选中",
              "Parrot", "Rhino")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Key", "Key", "Key", GH_ParamAccess.item);
            pManager.AddTextParameter("Value", "Value", "Value", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "Guid", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string key = null;
            if (!DA.GetData(0, ref key)) { return; }

            string value = null;
            if (!DA.GetData(1, ref value)) { return; }
            List<Guid> result_guid = new List<Guid>();

            int count = Rhino.RhinoDoc.ActiveDoc.Objects.Count;


            foreach (var item in Rhino.RhinoDoc.ActiveDoc.Objects)
            {
                if (item.Attributes.GetUserString(key) == value)
                {
                    result_guid.Add(item.Id);
                }
            }


            DA.SetDataList(0, result_guid);





        }



        //bool IsHaveUserString(Rhino.DocObjects.RhinoObject obj, string key)
        //{

        //}

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
            get { return new Guid("383DE270-5A2B-4123-89E4-9EE86B1548CB"); }
        }
    }
}