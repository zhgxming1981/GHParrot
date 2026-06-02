using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class SetUserString : GH_Component
    {
        public enum ButtonColor { Black, Grey }

        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        internal bool ButtonRun { get; set; }

        private bool _lastInputRun;

        public SetUserString()
          : base("SetUserString", "写入UserStr",
              "给Rhino对象写入UserString",
              "Parrot", "文本")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "Rhino对象Guid", GH_ParamAccess.item);
            pManager.AddTextParameter("key", "key", "UserString的key", GH_ParamAccess.item);
            pManager.AddTextParameter("Value", "Value", "UserString的Value", GH_ParamAccess.item);
            pManager.AddBooleanParameter("run", "run", "由False变为True时写入UserString", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            GH_Guid guid = null;
            string key = "";
            string value = "";
            bool inputRun = false;

            if (!DA.GetData(0, ref guid)) { return; }
            if (!DA.GetData(1, ref key)) { return; }
            if (!DA.GetData(2, ref value)) { return; }
            DA.GetData(3, ref inputRun);

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

            if (guid == null || guid.Value == Guid.Empty)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Guid为空。");
                return;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "key不能为空。");
                return;
            }

            RhinoObject obj = doc.Objects.FindId(guid.Value);
            if (obj == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "找不到指定Guid的Rhino对象。");
                return;
            }

            obj.Attributes.SetUserString(key, value ?? string.Empty);
            if (!obj.CommitChanges())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "写入UserString失败。");
                return;
            }

            doc.Views.Redraw();
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "UserString写入完成。");
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_SetUserString(this);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_GetUserString"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("31141469-8290-4B54-9869-00E20D26DF61"); }
        }
    }

    internal class CButton_SetUserString : GH_ComponentAttributes
    {
        public CButton_SetUserString(SetUserString component) : base(component) { }

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

            GH_Palette palette = ((SetUserString)Owner).CurrentButtonColor == SetUserString.ButtonColor.Black
                ? GH_Palette.Black
                : GH_Palette.Grey;

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("Run", font, Brushes.White, buttonRect, format);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                SetUserString owner = (SetUserString)Owner;
                owner.CurrentButtonColor = SetUserString.ButtonColor.Grey;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                CMath.Delay(50);
                owner.CurrentButtonColor = SetUserString.ButtonColor.Black;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
