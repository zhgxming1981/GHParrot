using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;
using Grasshopper.Kernel.Types;
using parrot.Properties;

namespace NS_Parrot
{
    public class MyBake : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyBake class.
        /// </summary>
        public MyBake()
          : base("MyBake", "MyBake",
              "带自定义信息的bake",
              "Parrot", "建模")
        {
        }

        //private int group_index = -1;//保证判断是否有组名的代码只运行一次
        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("几何", "Geo", "几何体", GH_ParamAccess.item);
            pManager.AddTextParameter("图层", "Layer", "图层", GH_ParamAccess.item);
            //pManager.AddColourParameter("颜色", "Color", "颜色", GH_ParamAccess.item);
            pManager.AddTextParameter("Key", "Key", "键名", GH_ParamAccess.list);
            pManager.AddTextParameter("KeyValue", "Value", "键值", GH_ParamAccess.list);
            //pManager.AddBooleanParameter("IsGroup", "Group", "是否成组", GH_ParamAccess.list);
            //pManager.AddTextParameter("GroupName", "GN", "组名", GH_ParamAccess.item);
            pManager.AddBooleanParameter("IsBake", "Bake", "Bake", GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("GUID", "GUID", "Bake结果的GUID", GH_ParamAccess.item);
        }


        private Guid id;

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>旧版本，无用代码
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected void SolveInstance2(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;


            bool bake = true;
            if (!DA.GetData(4, ref bake)) { return; }
            if (!bake) { DA.SetData(0, id); return; }

            Rhino.Geometry.GeometryBase geo = null;
            if (!DA.GetData(0, ref geo)) { return; }

            string layerName = "";
            if (!DA.GetData(1, ref layerName)) { return; }

            System.Drawing.Color layerColor = new System.Drawing.Color();

            List<string> key = new List<string>();
            if (!DA.GetDataList(2, key)) { return; }

            List<string> keyValue = new List<string>();
            if (!DA.GetDataList(3, keyValue)) { return; }


            int layerIndex = Rhino.RhinoDoc.ActiveDoc.Layers.FindByFullPath(layerName, -1);//查找图层的索引号
            if (layerIndex == -1)//如果图层不存在，就新建图层
            {
                layerIndex = Rhino.RhinoDoc.ActiveDoc.Layers.Add(layerName, layerColor);
            }

             id = Rhino.RhinoDoc.ActiveDoc.Objects.Add(geo);//将几何体加入到Rhino文档中


            Rhino.DocObjects.RhinoObject obj = Rhino.RhinoDoc.ActiveDoc.Objects.FindId(id);
            obj.Attributes.LayerIndex = layerIndex;


            int count = key.Count;
            for (int i = 0; i < count; i++)
            {
                obj.Attributes.SetUserString(key[i], keyValue[i]);
            }

            obj.CommitChanges();//重要，否则看不到任何效果    

            DA.SetData(0, id);
        }


        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;


            bool bake = true;
            if (!DA.GetData(4, ref bake)) { return; }
            if (!bake) { return; }

            Rhino.Geometry.GeometryBase geo = null;
            if (!DA.GetData(0, ref geo)) { return; }

            string layerName = "";
            if (!DA.GetData(1, ref layerName)) { return; }


            List<string> key = new List<string>();
            if (!DA.GetDataList(2, key)) { return; }

            List<string> keyValue = new List<string>();
            if (!DA.GetDataList(3, keyValue)) { return; }


            Guid guid = Rhino.RhinoDoc.ActiveDoc.Objects.Add(geo);//将几何体加入到Rhino文档中

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
                parentLayer.Id = System.Guid.NewGuid();//必须先给图层赋予ID值，才能加入，否则不会成为子图层
                layerIndex_P = Rhino.RhinoDoc.ActiveDoc.Layers.Add(parentLayer);
            }
            else
            {
                parentLayer = Rhino.RhinoDoc.ActiveDoc.Layers.FindIndex(layerIndex_P);
            }

            if (layer_P_C.Length == 1)
            {
                MyChangeLayer(guid, layerIndex_P);
                //return;
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
                    childLayer.Id = System.Guid.NewGuid();//必须先给图层赋予ID值，才能加入，否则不会成为子图层，而是并列的图层
                    layerIndex_C = Rhino.RhinoDoc.ActiveDoc.Layers.Add(childLayer);
                }
                else
                {
                    childLayer = Rhino.RhinoDoc.ActiveDoc.Layers.FindIndex(layerIndex_C);
                }

                MyChangeLayer(guid, layerIndex_C);
            }

            Rhino.DocObjects.RhinoObject obj = Rhino.RhinoDoc.ActiveDoc.Objects.FindId(guid);

            int count = key.Count;
            for (int i = 0; i < count; i++)
            {
                obj.Attributes.SetUserString(key[i], keyValue[i]);
            }
            DA.SetData(0, guid);
        }

        void MyChangeLayer(Guid guid, int layerIndex)
        {
            {
                Rhino.DocObjects.RhinoObject obj = RhinoDoc.ActiveDoc.Objects.Find(guid);
                obj.Attributes.LayerIndex = layerIndex;
                obj.CommitChanges();//重要，否则看不到任何效果 
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
                return Resources.烘焙;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("3F8C24F7-BA7E-4018-AD9D-BF821B878693"); }
        }
    }
}