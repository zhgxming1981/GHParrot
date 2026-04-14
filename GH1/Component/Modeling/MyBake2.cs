using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using NS_Parrot.Hardware;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.UI;

namespace parrot.Component.Modeling
{
    public class MyBake2 : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyBake2 class.
        /// </summary>
        public MyBake2()
             : base("MyBake2", "MyBake2",
              "带自定义信息的bake2",
              "parrot", "建模")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("几何", "Geo", "几何体", GH_ParamAccess.item);
            pManager.AddTextParameter("图层", "Layer", "图层", GH_ParamAccess.item);
            pManager.AddColourParameter("颜色", "Color", "颜色", GH_ParamAccess.item);
            pManager.AddTextParameter("Key", "Key", "键名", GH_ParamAccess.list);
            pManager.AddTextParameter("KeyValue", "Value", "键值", GH_ParamAccess.list);
            //pManager.AddBooleanParameter("IsGroup", "Group", "是否成组", GH_ParamAccess.list);
            //pManager.AddTextParameter("GroupName", "GN", "组名", GH_ParamAccess.item);
            pManager.AddBooleanParameter("IsBake", "Bake", "Bake", GH_ParamAccess.item);

            pManager[5].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("GUID", "GUID", "Bake结果的GUID", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;


            bool bake = true;
            if (!DA.GetData(5, ref bake)) { return; }
            if (!bake) { return; }

            //GH_GeometricGoo<Rhino.Geometry.GeometryBase> geo = null;
            Rhino.Geometry.GeometryBase geo = null;
            if (!DA.GetData(0, ref geo)) { return; }

            string layerName = "";
            if (!DA.GetData(1, ref layerName)) { return; }

            System.Drawing.Color layerColor = new System.Drawing.Color();
            if (!DA.GetData(2, ref layerColor)) { return; }

            List<string> key = new List<string>();
            if (!DA.GetDataList(3, key)) { return; }

            List<string> keyValue = new List<string>();
            if (!DA.GetDataList(4, keyValue)) { return; }

            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            int layerIndex = Rhino.RhinoDoc.ActiveDoc.Layers.FindByFullPath(layerName, -1);
            if (layerIndex == -1)
            {
                layerIndex = Rhino.RhinoDoc.ActiveDoc.Layers.Add(layerName, layerColor);
            }

            ObjectAttributes att = new ObjectAttributes();
            att.LayerIndex = layerIndex;
            att.ColorSource = ObjectColorSource.ColorFromObject;

            List<Guid> obj_ids = new List<Guid>();
            //GH_GeometricGoo<GeometryBase> geo2 = GH_GeometricGoo(GeometryBase);

            //obj_ids.Add(geo2.ReferenceID);
            doc.Objects.Add(geo);
            //base.BakeGeometry(doc, att, obj_ids);
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
            get { return new Guid("768B89A1-B56A-4170-83CA-755B0F402B93"); }
        }
    }
}