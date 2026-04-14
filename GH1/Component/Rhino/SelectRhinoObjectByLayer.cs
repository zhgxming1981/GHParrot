using System;
using System.Collections.Generic;
using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace NS_Parrot
{
    public class SelectRhinoObjectByLayer : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SelectRhinoObjectByLayer class.
        /// </summary>
        public SelectRhinoObjectByLayer()
          : base("按图层选中", "按图层选中",
              "按图层选中",
              "Parrot", "Rhino")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("图层", "图层", "图层", GH_ParamAccess.list);
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

            List<string> layerName = new List<string>();
            if (!DA.GetDataList(0, layerName)) { return; }

            List<Guid> result_guid = new List<Guid>();

            int count = layerName.Count;
            for (int i = 0; i < count; i++)
            {
                int layerIndex_C = Rhino.RhinoDoc.ActiveDoc.Layers.FindByFullPath(layerName[i], -1);//查找图层的索引号
                if (layerIndex_C != -1)//如果图层存在
                {
                    Layer theLayer = Rhino.RhinoDoc.ActiveDoc.Layers.FindIndex(layerIndex_C);

                    foreach (var item in Rhino.RhinoDoc.ActiveDoc.Objects)
                    {
                        if (item.Attributes.LayerIndex == layerIndex_C)
                        {
                            result_guid.Add(item.Id);
                        }
                    }
                }
            }
            DA.SetDataList(0, result_guid);
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
            get { return new Guid("4E29B3B6-A914-4AAC-B7C6-5269F1343D00"); }
        }
    }
}