using AutoCADFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using parrot.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class SelectCADObjectByHandle : GH_Component
    {
        public enum ButtonColor { Black, Grey }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;

        public List<string> Handles { get; } = new List<string>();
        public bool ZoomToObjects { get; private set; } = true;
        public List<string> Messages { get; } = new List<string>();

        public SelectCADObjectByHandle()
          : base("SelectCADObjectByHandle", "选中CAD句柄",
              "根据CAD句柄选中CAD对象，并缩放到对象位置",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("句柄", "H", "CAD对象句柄", GH_ParamAccess.list);
            pManager.AddBooleanParameter("缩放", "Z", "选中后缩放到对象位置", GH_ParamAccess.item, true);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("消息", "M", "选中和缩放结果消息", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Handles.Clear();
            DA.GetDataList(0, Handles);
            DA.GetData(1, ref _zoomToObjects);
            ZoomToObjects = _zoomToObjects;

            DA.SetDataList(0, Messages);
        }

        private bool _zoomToObjects = true;

        public override void CreateAttributes()
        {
            Attributes = new CButton_SelectCADByHandle(this);
        }

        protected override Bitmap Icon => Resources.check;

        public override Guid ComponentGuid => new Guid("68E2D301-9892-49D4-9175-FDB75EB907CB");
    }

    internal class CButton_SelectCADByHandle : GH_ComponentAttributes
    {
        public CButton_SelectCADByHandle(SelectCADObjectByHandle component) : base(component) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 20.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);

            if (channel == GH_CanvasChannel.Objects)
            {
                GH_Palette palette = ((SelectCADObjectByHandle)Owner).CurrentButtonColor == SelectCADObjectByHandle.ButtonColor.Black
                    ? GH_Palette.Black
                    : GH_Palette.Grey;

                using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                {
                    capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                }
            }

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat stringFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("选中CAD", font, Brushes.White, buttonRect, stringFormat);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                SelectCADObjectByHandle info = (SelectCADObjectByHandle)Owner;
                info.CurrentButtonColor = SelectCADObjectByHandle.ButtonColor.Grey;
                info.ExpireSolution(true);
                Thread.Sleep(50);
                info.CurrentButtonColor = SelectCADObjectByHandle.ButtonColor.Black;

                SelectInCAD(info);

                info.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }

        private static void SelectInCAD(SelectCADObjectByHandle info)
        {
            info.Messages.Clear();

            try
            {
                List<string> messages = AutoCADTool.SelectCADObjectsByHandles(info.Handles, info.ZoomToObjects);
                info.Messages.AddRange(messages);
            }
            catch (Exception ex)
            {
                info.Messages.Add("选中CAD对象失败: " + ex.Message);
            }
        }
    }
}
