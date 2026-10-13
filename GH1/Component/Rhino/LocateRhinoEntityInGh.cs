using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>
    /// 将 Rhino 对象与本电池实例关联；关联数据保存在 Rhino 对象的用户文本中。
    /// </summary>
    public sealed class LocateRhinoEntityInGh : GH_Component
    {
        internal const string UserStringKey = "Parrot.GHComponentLocator";
        private readonly List<Guid> _referencedObjectIds = new List<Guid>();

        public LocateRhinoEntityInGh()
            : base("实体定位GH", "实体定位GH",
                "记录 Rhino 实体与当前电池的关联。选中实体后运行 ParrotLocateGh 命令可跳转到本电池。",
                "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("实体", "G", "来自其它电池的 Rhino 实体引用，支持 Rhino 对象 Guid、引用几何及 RhinoObject。", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("实体Guid", "Guid", "已关联的 Rhino 实体 Guid。", GH_ParamAccess.list);
            pManager.AddTextParameter("状态", "S", "关联结果。", GH_ParamAccess.item);
        }

        public override void CreateAttributes()
        {
            Attributes = new LocateRhinoEntityInGhAttributes(this);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<object> inputs = new List<object>();
            if (!DA.GetDataList(0, inputs))
                return;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                DA.SetData(1, "当前没有可用的 Rhino 文档。");
                return;
            }

            List<Guid> ids = inputs
                .Select(TryGetRhinoObjectId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            _referencedObjectIds.Clear();
            _referencedObjectIds.AddRange(ids);

            int written = 0;
            foreach (Guid id in ids)
            {
                RhinoObject obj = doc.Objects.FindId(id);
                if (obj == null)
                    continue;

                obj.Attributes.SetUserString(UserStringKey, InstanceGuid.ToString("D"));
                if (obj.CommitChanges())
                    written++;
            }

            DA.SetDataList(0, ids.Select(id => id.ToString("D")));
            DA.SetData(1, "已关联 " + written + " 个实体；在 Rhino 命令行运行 ParrotLocateGh 可定位此电池。");
        }

        internal void SelectReferencedObjects()
        {
            ClearRuntimeMessages();
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "当前没有可用的 Rhino 文档。");
                return;
            }

            int selectedCount = 0;
            int missingCount = 0;
            foreach (Guid id in _referencedObjectIds)
            {
                RhinoObject obj = doc.Objects.FindId(id);
                if (obj == null)
                {
                    missingCount++;
                    continue;
                }

                if (obj.Select(true) > 0)
                    selectedCount++;
            }

            if (selectedCount > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "已选中指向的实体：" + selectedCount + " 个。 ");
            if (missingCount > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "找不到指向的 Rhino 实体：" + missingCount + " 个。 ");
            if (selectedCount == 0 && missingCount == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "没有可选中的指向实体；请先让电池完成计算。 ");

            doc.Views.Redraw();
        }

        internal static Guid TryGetRhinoObjectId(object value)
        {
            if (value is GH_Guid ghGuid)
                return ghGuid.Value;

            if (value is Guid guid)
                return guid;

            if (value is RhinoObject rhinoObject)
                return rhinoObject.Id;

            if (value is IGH_GeometricGoo geometricGoo && geometricGoo.ReferenceID != Guid.Empty)
                return geometricGoo.ReferenceID;

            if (value is GH_ObjectWrapper wrapper)
                return TryGetRhinoObjectId(wrapper.Value);

            if (value is string text && Guid.TryParse(text, out Guid parsed))
                return parsed;

            return Guid.Empty;
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("B274166F-2D38-4AAE-9CB1-22C8676B3678");
    }

    internal sealed class LocateRhinoEntityInGhAttributes : GH_ComponentAttributes
    {
        private const float ButtonHeight = 20.0f;

        public LocateRhinoEntityInGhAttributes(LocateRhinoEntityInGh owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + ButtonHeight);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects)
                return;

            RectangleF buttonBounds = GetButtonBounds();
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonBounds, GH_Palette.Black))
            {
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            }

            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("选中实体", GH_FontServer.Small, Brushes.White, buttonBounds, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != MouseButtons.Left || !GetButtonBounds().Contains(e.CanvasLocation))
                return GH_ObjectResponse.Ignore;

            ((LocateRhinoEntityInGh)Owner).SelectReferencedObjects();
            sender.Invalidate();
            return GH_ObjectResponse.Handled;
        }

        private RectangleF GetButtonBounds()
        {
            RectangleF bounds = new RectangleF(Bounds.X, Bounds.Bottom - ButtonHeight, Bounds.Width, ButtonHeight);
            bounds.Inflate(-5.0f, -2.0f);
            return bounds;
        }
    }

    /// <summary>
    /// 由 Rhino 命令 ParrotLocateGh 调用的定位入口。
    /// </summary>
    public static class RhinoEntityGhLocator
    {
        public static bool LocateSelectedObject(out string message)
        {
            try
            {
                RhinoDoc rhinoDocument = RhinoDoc.ActiveDoc;
                RhinoObject selected = rhinoDocument?.Objects.GetSelectedObjects(false, false).FirstOrDefault();
                if (selected == null)
                {
                    message = "请先选中一个已关联的 Rhino 实体。";
                    return false;
                }

                RhinoApp.WriteLine("[ParrotLocateGh] 选中实体：" + selected.Id.ToString("D"));
                string storedId = selected.Attributes.GetUserString(LocateRhinoEntityInGh.UserStringKey);
                if (!Guid.TryParse(storedId, out Guid componentInstanceId))
                {
                    message = "选中实体未关联“实体定位GH”电池。";
                    return false;
                }

                List<GH_Document> documents = Instances.DocumentServer.ToList();
                RhinoApp.WriteLine("[ParrotLocateGh] 关联电池：" + componentInstanceId.ToString("D") + "；已打开 GH 定义：" + documents.Count);
                foreach (GH_Document document in documents)
                {
                    IGH_DocumentObject target = document.FindObject(componentInstanceId, true);
                    if (target == null)
                        continue;

                    RhinoApp.WriteLine("[ParrotLocateGh] 已在 GH 定义中找到电池：" + target.NickName);
                    GH_Canvas canvas = Instances.ActiveCanvas;
                    if (canvas == null)
                    {
                        message = "Grasshopper 画布尚未打开。";
                        return false;
                    }

                    ActivateDocumentEditor();
                    canvas.Document = document;
                    document.DeselectAll();
                    if (target.Attributes == null)
                        target.CreateAttributes();
                    target.Attributes.Selected = true;
                    target.Attributes.ExpireLayout();
                    target.Attributes.PerformLayout();
                    FocusViewport(canvas, target.Attributes);
                    canvas.Invalidate();
                    canvas.Refresh();
                    message = "已定位到 Grasshopper 电池。";
                    return true;
                }

                message = "关联的 Grasshopper 电池不在当前已打开的定义中。";
                return false;
            }
            catch (Exception exception)
            {
                RhinoApp.WriteLine("[ParrotLocateGh] 异常：" + exception);
                message = "定位失败，详情已写入 Rhino 命令行。";
                return false;
            }
        }

        private static void ActivateDocumentEditor()
        {
            GH_DocumentEditor editor = Instances.DocumentEditor;
            if (editor == null)
                return;

            if (!editor.Visible)
                editor.Show();

            editor.BringToFront();
            editor.Activate();
        }

        private static void FocusViewport(GH_Canvas canvas, IGH_Attributes attributes)
        {
            if (canvas?.Viewport == null || attributes == null)
                return;

            RectangleF bounds = attributes.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                bounds = new RectangleF(attributes.Pivot.X - 60.0f, attributes.Pivot.Y - 30.0f, 120.0f, 60.0f);

            canvas.Viewport.Zoom = 1.6f;
            canvas.Viewport.MidPoint = new PointF(bounds.Left + bounds.Width / 2.0f, bounds.Top + bounds.Height / 2.0f);
            canvas.Viewport.ComputeProjection();
        }
    }
}
