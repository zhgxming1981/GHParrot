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
        //protected override void SolveInstance(IGH_DataAccess DA)
        //{
        //    List<object> objs = new List<object>();

        //    string layerName = null;
        //    System.Drawing.Color color = System.Drawing.Color.Empty;

        //    List<string> keys = new List<string>();
        //    List<string> values = new List<string>();

        //    bool isGroup = false;
        //    string groupName = null;
        //    bool isBake = false;

        //    // ===== 必须输入 =====
        //    if (!DA.GetDataList(0, objs)) return;

        //    // ===== 可选参数 =====
        //    string tempLayer = null;
        //    if (DA.GetData(1, ref tempLayer))
        //        layerName = tempLayer;

        //    System.Drawing.Color tempColor = System.Drawing.Color.Empty;
        //    if (DA.GetData(2, ref tempColor))
        //        color = tempColor;

        //    DA.GetDataList(3, keys);
        //    DA.GetDataList(4, values);

        //    bool tempGroup = false;
        //    if (DA.GetData(5, ref tempGroup))
        //        isGroup = tempGroup;

        //    string tempGroupName = null;
        //    if (DA.GetData(6, ref tempGroupName))
        //        groupName = tempGroupName;

        //    bool tempBake = false;
        //    if (DA.GetData(7, ref tempBake))
        //        isBake = tempBake;

        //    // ===== 触发逻辑 =====
        //    bool trigger = isBake || _triggerBake;

        //    if (!trigger)
        //    {
        //        DA.SetDataList(0, _lastResult);
        //        return;
        //    }

        //    _triggerBake = false;

        //    var doc = Rhino.RhinoDoc.ActiveDoc;
        //    if (doc == null)
        //    {
        //        DA.SetDataList(0, _lastResult);
        //        return;
        //    }

        //    try
        //    {
        //        // ===== 图层处理（支持 A::B::C）=====
        //        int layerIndex;

        //        if (string.IsNullOrWhiteSpace(layerName))
        //        {
        //            layerIndex = doc.Layers.CurrentLayerIndex;
        //        }
        //        else
        //        {
        //            string[] parts = layerName.Split(new string[] { "::" }, StringSplitOptions.RemoveEmptyEntries);

        //            int parentIndex = -1;
        //            string fullPath = "";

        //            for (int i = 0; i < parts.Length; i++)
        //            {
        //                fullPath = i == 0 ? parts[i] : fullPath + "::" + parts[i];

        //                int idx = doc.Layers.FindByFullPath(fullPath, -1);

        //                if (idx < 0)
        //                {
        //                    var layer = new Rhino.DocObjects.Layer();
        //                    layer.Name = parts[i];

        //                    if (parentIndex >= 0)
        //                        layer.ParentLayerId = doc.Layers[parentIndex].Id;

        //                    idx = doc.Layers.Add(layer);
        //                }

        //                parentIndex = idx;
        //            }

        //            layerIndex = parentIndex;
        //        }

        //        // ===== 属性 =====
        //        var attr = new Rhino.DocObjects.ObjectAttributes();
        //        attr.LayerIndex = layerIndex;

        //        if (color != System.Drawing.Color.Empty)
        //        {
        //            attr.ObjectColor = color;
        //            attr.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject;
        //        }

        //        // 只有在有 Key/Value 时才写
        //        if (keys != null && values != null && keys.Count == values.Count && keys.Count > 0)
        //        {
        //            for (int i = 0; i < keys.Count; i++)
        //            {
        //                if (!string.IsNullOrWhiteSpace(keys[i]) &&
        //                    !string.IsNullOrWhiteSpace(values[i]))
        //                {
        //                    attr.SetUserString(keys[i], values[i]);
        //                }
        //            }
        //        }

        //        List<Guid> resultGuids = new List<Guid>();

        //        // ===== Bake =====
        //        foreach (var obj in objs)
        //        {
        //            Rhino.Geometry.GeometryBase geo = null;

        //            if (obj is IGH_GeometricGoo ggoo)
        //            {
        //                if (!ggoo.IsValid) continue;
        //                if (!ggoo.CastTo(out geo)) continue;
        //            }
        //            else if (obj is GH_ObjectWrapper wrapper)
        //            {
        //                if (wrapper.Value is Rhino.Geometry.GeometryBase g)
        //                    geo = g;
        //            }
        //            else if (obj is Rhino.Geometry.GeometryBase g2)
        //            {
        //                geo = g2;
        //            }

        //            if (geo == null) continue;

        //            Guid guid = doc.Objects.Add(geo, attr);

        //            if (guid != Guid.Empty)
        //                resultGuids.Add(guid);
        //        }

        //        // ===== 分组 =====
        //        if (isGroup && resultGuids.Count > 0)
        //        {
        //            if (string.IsNullOrWhiteSpace(groupName))
        //            {
        //                groupName = "GH_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        //            }

        //            var group = doc.Groups.FindName(groupName);
        //            int groupIndex = group == null ? doc.Groups.Add(groupName) : group.Index;

        //            foreach (var g in resultGuids)
        //                doc.Groups.AddToGroup(groupIndex, g);
        //        }

        //        doc.Views.Redraw();

        //        // 缓存结果（防闪）
        //        _lastResult = resultGuids;

        //        DA.SetDataList(0, resultGuids);
        //    }
        //    catch (Exception ex)
        //    {
        //        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        //        DA.SetDataList(0, _lastResult);
        //    }
        //}


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

            if (!trigger)
            {
                DA.SetDataList(0, _lastResult);
                return;
            }

            _triggerBake = false;

            var doc = Rhino.RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                DA.SetDataList(0, _lastResult);
                return;
            }

            try
            {
                // ===== 图层处理（支持 A::B::C）===== 
                int layerIndex;

                if (string.IsNullOrWhiteSpace(layerName))
                {
                    layerIndex = doc.Layers.CurrentLayerIndex;
                }
                else
                {
                    string[] parts = layerName.Split(new string[] { "::" }, StringSplitOptions.RemoveEmptyEntries);

                    int parentIndex = -1;
                    string fullPath = "";

                    for (int i = 0; i < parts.Length; i++)
                    {
                        fullPath = i == 0 ? parts[i] : fullPath + "::" + parts[i];

                        int idx = doc.Layers.FindByFullPath(fullPath, -1);

                        if (idx < 0)
                        {
                            var layer = new Rhino.DocObjects.Layer();
                            layer.Name = parts[i];

                            if (parentIndex >= 0)
                                layer.ParentLayerId = doc.Layers[parentIndex].Id;

                            idx = doc.Layers.Add(layer);
                        }

                        parentIndex = idx;
                    }

                    layerIndex = parentIndex;
                }

                // ===== 属性 =====
                var attr = new Rhino.DocObjects.ObjectAttributes();
                attr.LayerIndex = layerIndex;

                if (color != System.Drawing.Color.Empty)
                {
                    attr.ObjectColor = color;
                    attr.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject;
                }

                // 只有在有 Key/Value 时才写
                if (keys != null && values != null && keys.Count == values.Count && keys.Count > 0)
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(keys[i]) &&
                            !string.IsNullOrWhiteSpace(values[i]))
                        {
                            attr.SetUserString(keys[i], values[i]);
                        }
                    }
                }

                List<Guid> resultGuids = new List<Guid>();

                // ===== Bake =====
                foreach (var obj in objs)
                {
                    Rhino.Geometry.GeometryBase geo = null;

                    // 处理 Rhino 内部的 InstanceObject（图块）
                    if (obj is Rhino.DocObjects.InstanceObject instance)
                    {
                        geo = instance.Geometry;
                    }
                    // 处理 GH_InstanceReference（图块引用）
                    else if (obj is Grasshopper.Kernel.Types.GH_InstanceReference ghInst)
                    {
                        var instGeo = ghInst.Value; // ✔ 这就是 InstanceReferenceGeometry

                        if (instGeo != null)
                        {
                            geo = instGeo.Duplicate(); // ⭐ 关键：复制一份
                        }
                    }
                    // 处理 GH_GeometricGoo 和其它类型
                    else if (obj is IGH_GeometricGoo ggoo)
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

                    Guid guid = doc.Objects.Add(geo, attr);

                    if (guid != Guid.Empty)
                        resultGuids.Add(guid);
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

                // 缓存结果（防闪）
                _lastResult = resultGuids;

                DA.SetDataList(0, resultGuids);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetDataList(0, _lastResult);
            }
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