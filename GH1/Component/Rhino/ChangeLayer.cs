using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.DocObjects;
using Rhino;
using Rhino.Geometry;
using CommonFunction.Hardware;
using Grasshopper.Kernel.Types;

namespace NS_Parrot
{
    public class ChangeLayer : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ChangeLayer class.
        /// </summary>
        public ChangeLayer()
          : base("修改图层", "修改图层",
              "修改Rhino物件的图层",
              "Parrot", "Rhino")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "Guid", GH_ParamAccess.list);
            pManager.AddTextParameter("layer", "layer", "图层名", GH_ParamAccess.item);
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

            List<GH_Guid> guid = new List<GH_Guid>();
            if (!DA.GetDataList(0, guid)) { return; }


            string layerName = "";
            DA.GetData(1, ref layerName);

            string[] layer_P_C;
            char[] ch = { ':', ':' };
            layer_P_C = layerName.Split(ch);
            string layer_P, layer_C = "";
            layer_P = layer_P_C[0];




            Rhino.DocObjects.Layer parentLayer, childLayer;

            int layerIndex_P = Rhino.RhinoDoc.ActiveDoc.Layers.FindByFullPath(layer_P, -1);//查找图层的索引号
            if (layerIndex_P == -1)//如果图层不存在，就新建图层
            {
                parentLayer = new Rhino.DocObjects.Layer();
                parentLayer.Name = layer_P;
                layerIndex_P = Rhino.RhinoDoc.ActiveDoc.Layers.Add(parentLayer);
            }
            else
            {
                parentLayer = Rhino.RhinoDoc.ActiveDoc.Layers.FindIndex(layerIndex_P);
            }

            if (layer_P_C.Length == 1)
            {
                MyChangeLayer(guid, layerIndex_P);
                return;
            }


            if (layer_P_C.Length == 3)
            {
                layer_C = layer_P_C[2];

                int layerIndex_C = Rhino.RhinoDoc.ActiveDoc.Layers.FindByFullPath(layerName, -1);//查找图层的索引号
                if (layerIndex_C == -1)//如果图层不存在，就新建图层
                {
                    childLayer = new Rhino.DocObjects.Layer();
                    childLayer.Name = layer_C;
                    childLayer.ParentLayerId = parentLayer.Id;//设为子图层
                    layerIndex_C = Rhino.RhinoDoc.ActiveDoc.Layers.Add(childLayer);
                }
                else
                {
                    childLayer = Rhino.RhinoDoc.ActiveDoc.Layers.FindIndex(layerIndex_C);
                }

                MyChangeLayer(guid, layerIndex_C);
            }


        }


        void MyChangeLayer(List<GH_Guid> guid, int layerIndex)
        {
            int count = guid.Count;
            for (int i = 0; i < count; i++)//修改图层
            {
                Rhino.DocObjects.RhinoObject obj = RhinoDoc.ActiveDoc.Objects.Find(guid[i].Value);
                obj.Attributes.LayerIndex = layerIndex;
                obj.CommitChanges();
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
            get { return new Guid("D289AA99-8BE9-4415-8A0B-98F6439876DF"); }
        }
    }
}