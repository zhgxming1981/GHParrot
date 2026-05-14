using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using CommonFunction;
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


        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Guid> ids = new List<Guid>();
            Transform xform = Transform.Identity;
            bool run = false;

            if (!DA.GetDataList(0, ids)) return;
            if (!DA.GetData(1, ref xform)) return;
            if (!DA.GetData(2, ref run)) return;

            // 输出缓存
            if (!run)
            {
                DA.SetDataList(0, _lastResult);
                return;
            }

            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                DA.SetDataList(0, _lastResult);
                return;
            }

            List<Guid> result = new List<Guid>();

            try
            {
                foreach (Guid id in ids)
                {
                    Rhino.DocObjects.RhinoObject obj = doc.Objects.Find(id);
                    if (obj == null) continue;

                    // =====================================================
                    // ✔ 只处理 Block Instance（核心）
                    // =====================================================
                    if (obj is Rhino.DocObjects.InstanceObject instObj)
                    {
                        var idef = instObj.InstanceDefinition;
                        if (idef == null) continue;

                        // 原始 Block Transform（很关键）
                        Transform original = instObj.InstanceXform;

                        // =================================================
                        // ✔ 叠加 Transform（工业标准）
                        // =================================================
                        Transform final = xform * original;

                        // =================================================
                        // ✔ 复制属性（避免污染原块）
                        // =================================================
                        Rhino.DocObjects.ObjectAttributes attr =
                            instObj.Attributes.Duplicate();

                        // =================================================
                        // ✔ 可选：修改 UserText
                        // =================================================
                        // attr.SetUserString("Source", "GH_Bake");

                        // =================================================
                        // ✔ 真正 Bake Block Instance（核心）
                        // =================================================
                        Guid newId = doc.Objects.AddInstanceObject(
                            idef.Index,
                            final,
                            attr
                        );

                        if (newId != Guid.Empty)
                            result.Add(newId);
                    }
                    else
                    {
                        // =================================================
                        // fallback（非 Block）
                        // =================================================
                        Rhino.Geometry.GeometryBase geo = obj.Geometry?.Duplicate();
                        if (geo == null) continue;

                        geo.Transform(xform);

                        Guid newId = doc.Objects.Add(
                            geo,
                            obj.Attributes
                        );

                        if (newId != Guid.Empty)
                            result.Add(newId);
                    }
                }

                doc.Views.Redraw();

                _lastResult = result;
                DA.SetDataList(0, result);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(
                    Grasshopper.Kernel.GH_RuntimeMessageLevel.Error,
                    ex.Message
                );

                DA.SetDataList(0, _lastResult);
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