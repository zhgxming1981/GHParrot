using System;
using System.Collections.Generic;
using System.Linq;
using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace NS_Parrot
{
    public class ClearUerString : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ClearUerString class.
        /// </summary>
        public ClearUerString()
          : base("ClearUerString", "清除UserString",
              "清除UserString",
              "Parrot", "文本")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "Rhino中的物件", GH_ParamAccess.item);
            pManager.AddTextParameter("key", "key", "要清除的key", GH_ParamAccess.list);
            pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
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

            List<string> key = new List<string>();
            DA.GetDataList(1, key);

            //List<string> value = new List<string>();
            //if (!DA.GetDataList(0, value)) { return; }

            Rhino.DocObjects.RhinoObject obj = RhinoDoc.ActiveDoc.Objects.Find(guid.Value);

            if (key.Count == 0)
            {
                obj.Attributes.DeleteAllUserStrings();
            }

            if (key.Count > 0)
            {
                int count = key.Count;
                for (int i = 0; i < count; i++)
                {
                    DeleteUerStringByKey(obj, key[i]);
                }
            }
        }


        string GetFullName(string[] txts, string txt)
        {
            int len = txts.Length;
            string result = "";
            for (int i = 0; i < len; i++)
            {
                if (txts[i].Contains(txt))
                {
                    result = txts[i];
                }
            }
            return result;
        }

        void DeleteUerStringByKey(Rhino.DocObjects.RhinoObject obj, string key)
        {
            for (int i = 0; i < obj.Attributes.UserStringCount; i++)
            {
                var keys = obj.Attributes.GetUserStrings().AllKeys;
                string fullName = GetFullName(keys, key);
                if (obj.Attributes.DeleteUserString(fullName))
                {
                    i--;
                }
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
            get { return new Guid("3BF8D21A-FC2F-4E17-8D46-177B5F5EE44C"); }
        }
    }
}