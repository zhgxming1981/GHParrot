using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Rhino;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class SelectDuplicateRhinoObjects : GH_Component
    {
        public enum ButtonColor { Black, Grey }

        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        internal bool ButtonRun { get; set; }

        private bool _lastInputRun;

        public SelectDuplicateRhinoObjects()
          : base("SelectDuplicateRhinoObjects", "删重复",
              "删除Rhino文档中的重复对象，相当于执行SelDup后Delete",
              "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Run", "Run", "由False变为True时删除重复对象", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            bool inputRun = false;
            DA.GetData(0, ref inputRun);

            bool shouldRun = ButtonRun || (inputRun && !_lastInputRun);
            _lastInputRun = inputRun;
            ButtonRun = false;

            if (!shouldRun)
                return;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "当前没有可用的Rhino文档。");
                return;
            }

            bool success = RhinoApp.RunScript("_SelDup _Delete", false);
            if (!success)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "删除重复对象命令执行失败。");

            doc.Views.Redraw();
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_SelectDuplicateRhinoObjects(this);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_SelectDuplicateRhinoObjects"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("86ED21AC-F75B-47A1-9501-DB8236A19049"); }
        }
    }

    internal class CButton_SelectDuplicateRhinoObjects : GH_ComponentAttributes
    {
        public CButton_SelectDuplicateRhinoObjects(SelectDuplicateRhinoObjects component) : base(component) { }

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

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);

            SelectDuplicateRhinoObjects owner = (SelectDuplicateRhinoObjects)Owner;
            GH_Palette palette = owner.CurrentButtonColor == SelectDuplicateRhinoObjects.ButtonColor.Black
                ? GH_Palette.Black
                : GH_Palette.Grey;

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

            using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("Run", font, Brushes.White, buttonRect, format);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                SelectDuplicateRhinoObjects owner = (SelectDuplicateRhinoObjects)Owner;
                owner.CurrentButtonColor = SelectDuplicateRhinoObjects.ButtonColor.Grey;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                CMath.Delay(50);
                owner.CurrentButtonColor = SelectDuplicateRhinoObjects.ButtonColor.Black;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
