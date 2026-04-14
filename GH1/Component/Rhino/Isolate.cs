using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NS_Parrot
{
    public class Isolate : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Isolate class.
        /// </summary>
        public Isolate()
          : base("Isolate", "Isolate",
              "隔离显示",
              "Parrot", "Rhino")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Object GUIDs", "GUIDs", "要显示的对象 GUID 列表", GH_ParamAccess.list);
            //pManager.AddBooleanParameter("Enable", "E", "启用隔离（False 则无操作）", GH_ParamAccess.item);
            //pManager[1].Optional = true;
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
            //bool enable = true;
            //if (!DA.GetData(1, ref enable) || !enable)
            //    return; // 不启用则跳过

            var guidStrings = new List<string>();
            if (!DA.GetDataList(0, guidStrings) || guidStrings.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No GUIDs provided.");
                return;
            }

            var targetGuids = new HashSet<Guid>();
            foreach (string s in guidStrings)
            {
                if (!string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out Guid g))
                    targetGuids.Add(g);
            }

            if (targetGuids.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid GUIDs.");
                return;
            }

            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return;

            // === 关键：先确保所有目标对象存在且可显示 ===
            bool hasValidTarget = false;
            foreach (Guid id in targetGuids)
            {
                var obj = doc.Objects.FindId(id);
                if (obj != null)
                {
                    // 强制解除隐藏、锁定、在关闭图层等问题
                    var attrs = obj.Attributes.Duplicate();

                    // 确保不在关闭/冻结图层
                    var layer = doc.Layers.FindIndex(attrs.LayerIndex);
                    if (layer != null)
                    {
                        if (!layer.IsVisible )
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                $"Object {id} is on a hidden/frozen layer. May not appear.");
                        }
                    }

                    // 确保对象本身未被隐藏
                    if (obj.IsHidden)
                    {
                        doc.Objects.Show(obj.Id, true);
                    }

                    hasValidTarget = true;
                }
            }

            if (!hasValidTarget)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid objects found to isolate.");
                return;
            }

            // === 隐藏所有非目标对象 ===
            int hiddenCount = 0;
            foreach (RhinoObject obj in doc.Objects)
            {
                if (!targetGuids.Contains(obj.Id))
                {
                    if (!obj.IsHidden)
                    {
                        doc.Objects.Hide(obj.Id, true);
                        hiddenCount++;
                    }
                }
            }

            // 刷新所有视图
            doc.Views.Redraw();

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Isolated {targetGuids.Count} object(s). Hid {hiddenCount} others.");
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
            get { return new Guid("3DED321A-105E-4E36-8104-C9101625F21F"); }
        }
    }
}