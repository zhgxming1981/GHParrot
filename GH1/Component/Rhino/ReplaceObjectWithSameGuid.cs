using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.Kernel.Types;
//using NS_Parrot.Hardware;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class ReplaceObjectWithSameGuid : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ReplaceObjectWithSameGuid class.
        /// </summary>
        public ReplaceObjectWithSameGuid()
          : base("替换物件", "替换物件",
              "替换物件，保持Guid不变",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("原几何体", "geo1", "原几何体，保持Guid，但几何体被替换", GH_ParamAccess.item);
            pManager.AddGenericParameter("替换的几何体", "geo2", "替换的几何体，Guid丢失", GH_ParamAccess.item);
            pManager.AddBooleanParameter("是否忽略Mode", "ignore", "不知道啥意思", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("替换的几何体", "geo3", "拥有原几何体Guid的新几何体", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            GH_Guid guid = null;//此类型可包容rhino几何体
            if (!DA.GetData(0, ref guid)) { return; }

            GeometryBase geo = null;//此类型可包容rhino几何体
            if (!DA.GetData(1, ref geo)) { return; }

            bool ignoreModes = false;
            if (!DA.GetData(2, ref ignoreModes)) { return; }


            Replace(guid.Value, geo, ignoreModes);
            Rhino.RhinoDoc.ActiveDoc.Views.Redraw();//刷新视图
            DA.SetData(0, guid);
        }


        public void Replace<T>(Guid guid, T geo, bool ignoreModes) where T : GeometryBase
        {
            if (geo is T)
                Rhino.RhinoDoc.ActiveDoc.Objects.Replace(guid, (T)geo, ignoreModes);
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
            get { return new Guid("62139E88-D7D1-4973-AC0B-79E7E9DBA197"); }
        }
    }
}