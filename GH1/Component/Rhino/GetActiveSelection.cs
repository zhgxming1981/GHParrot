using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Rhino;
using Rhino.DocObjects;

namespace NS_Parrot
{
    public class GetActiveSelection : GH_Component
    {
        public enum ButtonColor { Black, Grey }

        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        private List<Guid> selectedIds = new List<Guid>();

        public GetActiveSelection()
            : base("获取活动选择集", "获取选择集",
                "点击按钮后，获取当前 Rhino 模型中选中实体的 Guid。",
                "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("选中Guid", "Guid", "当前选中 Rhino 实体的 Guid 列表。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            DA.SetDataList(0, selectedIds);
        }

        internal void CaptureSelection()
        {
            ClearRuntimeMessages();
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "当前没有可用的 Rhino 文档。");
                return;
            }

            List<Guid> result = new List<Guid>();
            IEnumerable<RhinoObject> selectedObjects = doc.Objects.GetSelectedObjects(false, false);
            foreach (RhinoObject obj in selectedObjects)
            {
                if (obj != null && obj.Id != Guid.Empty)
                    result.Add(obj.Id);
            }

            selectedIds = result;
            if (selectedIds.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "当前没有选中实体。");
        }

        public override void CreateAttributes()
        {
            Attributes = new GetActiveSelectionButton(this);
        }

        protected override Bitmap Icon
        {
            get { return null; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("E53D805D-6A25-4D67-8421-5388F7AB69C2"); }
        }
    }

    internal class GetActiveSelectionButton : GH_ComponentAttributes
    {
        public GetActiveSelectionButton(GetActiveSelection component) : base(component) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 20.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects)
                return;

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20.0f, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);
            GetActiveSelection component = (GetActiveSelection)Owner;
            GH_Palette palette = component.CurrentButtonColor == GetActiveSelection.ButtonColor.Black ? GH_Palette.Black : GH_Palette.Grey;

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("获取", font, Brushes.White, buttonRect, format);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20.0f, Bounds.Width, 20.0f);
            if (e.Button != MouseButtons.Left || !buttonRect.Contains(e.CanvasLocation))
                return GH_ObjectResponse.Ignore;

            GetActiveSelection component = (GetActiveSelection)Owner;
            component.CurrentButtonColor = GetActiveSelection.ButtonColor.Grey;
            component.CaptureSelection();
            component.ExpireSolution(true);
            CMath.Delay(50);
            component.CurrentButtonColor = GetActiveSelection.ButtonColor.Black;
            component.ExpireSolution(true);
            return GH_ObjectResponse.Handled;
        }
    }
}
