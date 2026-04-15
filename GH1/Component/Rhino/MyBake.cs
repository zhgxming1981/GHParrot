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

        private List<Guid> _lastResult = new List<Guid>();
        private static bool _lastBake = false;
        private bool _triggerBake = false;

        //private int group_index = -1;//保证判断是否有组名的代码只运行一次
        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("对象", "Obj", "支持几何/文字/标注/块", GH_ParamAccess.list);

            pManager.AddTextParameter("图层", "Layer", "图层", GH_ParamAccess.item);
            pManager.AddColourParameter("颜色", "Color", "颜色", GH_ParamAccess.item);

            pManager.AddTextParameter("Key", "Key", "键名", GH_ParamAccess.list);
            pManager.AddTextParameter("KeyValue", "Value", "键值", GH_ParamAccess.list);

            pManager.AddBooleanParameter("IsGroup", "Group", "是否成组", GH_ParamAccess.item, false);
            pManager.AddTextParameter("GroupName", "GN", "组名", GH_ParamAccess.item);

            pManager.AddBooleanParameter("IsBake", "Bake", "执行", GH_ParamAccess.item, false);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("GUID", "GUID", "Bake结果的GUID", GH_ParamAccess.item);
        }

        public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendItem(menu, "运行 Bake", (s, e) =>
            {
                _triggerBake = true;
                ExpireSolution(true);
            });
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>旧版本，无用代码
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<object> objs = new List<object>();

            string layerName = null;
            System.Drawing.Color color = System.Drawing.Color.Empty;

            List<string> keys = new List<string>();
            List<string> values = new List<string>();

            bool isGroup = false;
            string groupName = null;
            bool isBake = false;

            // ===== 必须输入 =====
            if (!DA.GetDataList(0, objs)) return;

            // ===== 可选参数 =====
            string tempLayer = null;
            if (DA.GetData(1, ref tempLayer))
                layerName = tempLayer;

            System.Drawing.Color tempColor = System.Drawing.Color.Empty;
            if (DA.GetData(2, ref tempColor))
                color = tempColor;

            DA.GetDataList(3, keys);
            DA.GetDataList(4, values);

            bool tempGroup = false;
            if (DA.GetData(5, ref tempGroup))
                isGroup = tempGroup;

            string tempGroupName = null;
            if (DA.GetData(6, ref tempGroupName))
                groupName = tempGroupName;

            bool tempBake = false;
            if (DA.GetData(7, ref tempBake))
                isBake = tempBake;

            // ===== 触发逻辑 =====
            bool trigger = isBake || _triggerBake;

            // ❗ 未触发：保持上次结果（防闪）
            if (!trigger)
            {
                DA.SetDataList(0, _lastResult);
                return;
            }

            // ❗ Button 防抖（只屏蔽“持续为 true”）
            if (isBake && _lastBake)
            {
                DA.SetDataList(0, _lastResult);
                return;
            }

            _lastBake = isBake;
            _triggerBake = false;

            var doc = Rhino.RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                DA.SetDataList(0, _lastResult);
                return;
            }

            try
            {
                // ===== 图层 =====
                int layerIndex;

                if (string.IsNullOrWhiteSpace(layerName))
                {
                    layerIndex = doc.Layers.CurrentLayerIndex;
                }
                else
                {
                    layerIndex = doc.Layers.FindByFullPath(layerName, -1);

                    if (layerIndex < 0)
                    {
                        var layer = new Rhino.DocObjects.Layer();
                        layer.Name = layerName;
                        layerIndex = doc.Layers.Add(layer);
                    }
                }

                // ===== 属性 =====
                var attr = new Rhino.DocObjects.ObjectAttributes();
                attr.LayerIndex = layerIndex;

                if (color != System.Drawing.Color.Empty)
                {
                    attr.ObjectColor = color;
                    attr.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject;
                }

                if (keys != null && values != null && keys.Count == values.Count)
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(keys[i]))
                            attr.SetUserString(keys[i], values[i]);
                    }
                }

                List<Guid> resultGuids = new List<Guid>();

                // ===== 去重缓存 =====
                HashSet<string> existingHashes = new HashSet<string>();
                foreach (var o in doc.Objects)
                {
                    string h = o.Attributes.GetUserString("GH_HASH");
                    if (!string.IsNullOrEmpty(h))
                        existingHashes.Add(h);
                }

                // ===== Bake =====
                foreach (var obj in objs)
                {
                    Rhino.Geometry.GeometryBase geo = null;

                    if (obj is IGH_GeometricGoo ggoo)
                    {
                        if (!ggoo.IsValid) continue;
                        if (!ggoo.CastTo(out geo)) continue;
                    }
                    else if (obj is GH_ObjectWrapper wrapper)
                    {
                        if (wrapper.Value is Rhino.Geometry.GeometryBase g)
                            geo = g;
                    }
                    else if (obj is Rhino.Geometry.GeometryBase g2)
                    {
                        geo = g2;
                    }

                    if (geo == null) continue;

                    string hash = geo.GetHashCode().ToString();
                    if (existingHashes.Contains(hash))
                        continue;

                    attr.SetUserString("GH_HASH", hash);

                    Guid guid = doc.Objects.Add(geo, attr);

                    if (guid != Guid.Empty)
                    {
                        resultGuids.Add(guid);
                        existingHashes.Add(hash);
                    }
                }

                // ===== 分组 =====
                if (isGroup && resultGuids.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(groupName))
                    {
                        groupName = "GH_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    }

                    var group = doc.Groups.FindName(groupName);
                    int groupIndex = group == null ? doc.Groups.Add(groupName) : group.Index;

                    foreach (var g in resultGuids)
                        doc.Groups.AddToGroup(groupIndex, g);
                }

                doc.Views.Redraw();

                // ✅ 缓存结果
                _lastResult = resultGuids;

                DA.SetDataList(0, resultGuids);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetDataList(0, _lastResult);
            }
        }


        //protected override void SolveInstance(IGH_DataAccess DA)
        //{
        //    if (!CHardware.CheckLegality())
        //        return;


        //    bool bake = true;
        //    if (!DA.GetData(4, ref bake)) { return; }
        //    if (!bake) { return; }

        //    Rhino.Geometry.GeometryBase geo = null;
        //    if (!DA.GetData(0, ref geo)) { return; }

        //    string layerName = "";
        //    if (!DA.GetData(1, ref layerName)) { return; }


        //    List<string> key = new List<string>();
        //    if (!DA.GetDataList(2, key)) { return; }

        //    List<string> keyValue = new List<string>();
        //    if (!DA.GetDataList(3, keyValue)) { return; }


        //    Guid guid = Rhino.RhinoDoc.ActiveDoc.Objects.Add(geo);//将几何体加入到Rhino文档中

        //    string[] layer_P_C;
        //    char[] ch = { ':', ':' };
        //    layer_P_C = layerName.Split(ch);
        //    string layer_P, layer_C = "";
        //    layer_P = layer_P_C[0];
        //    Rhino.DocObjects.Layer parentLayer, childLayer;

        //    int layerIndex_P = Rhino.RhinoDoc.ActiveDoc.Layers.FindByFullPath(layer_P, -1);//查找图层的索引号
        //    if (layerIndex_P == -1)//如果图层不存在，就新建图层
        //    {
        //        parentLayer = new Rhino.DocObjects.Layer();
        //        parentLayer.Name = layer_P;
        //        parentLayer.Id = System.Guid.NewGuid();//必须先给图层赋予ID值，才能加入，否则不会成为子图层
        //        layerIndex_P = Rhino.RhinoDoc.ActiveDoc.Layers.Add(parentLayer);
        //    }
        //    else
        //    {
        //        parentLayer = Rhino.RhinoDoc.ActiveDoc.Layers.FindIndex(layerIndex_P);
        //    }

        //    if (layer_P_C.Length == 1)
        //    {
        //        MyChangeLayer(guid, layerIndex_P);
        //        //return;
        //    }


        //    if (layer_P_C.Length == 3)
        //    {
        //        layer_C = layer_P_C[2];

        //        int layerIndex_C = Rhino.RhinoDoc.ActiveDoc.Layers.FindByFullPath(layerName, -1);//查找图层的索引号
        //        if (layerIndex_C == -1)//如果图层不存在，就新建图层
        //        {
        //            childLayer = new Rhino.DocObjects.Layer();
        //            childLayer.Name = layer_C;
        //            childLayer.ParentLayerId = parentLayer.Id;//设为子图层
        //            childLayer.Id = System.Guid.NewGuid();//必须先给图层赋予ID值，才能加入，否则不会成为子图层，而是并列的图层
        //            layerIndex_C = Rhino.RhinoDoc.ActiveDoc.Layers.Add(childLayer);
        //        }
        //        else
        //        {
        //            childLayer = Rhino.RhinoDoc.ActiveDoc.Layers.FindIndex(layerIndex_C);
        //        }

        //        MyChangeLayer(guid, layerIndex_C);
        //    }

        //    Rhino.DocObjects.RhinoObject obj = Rhino.RhinoDoc.ActiveDoc.Objects.FindId(guid);

        //    int count = key.Count;
        //    for (int i = 0; i < count; i++)
        //    {
        //        obj.Attributes.SetUserString(key[i], keyValue[i]);
        //    }
        //    DA.SetData(0, guid);
        //}

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