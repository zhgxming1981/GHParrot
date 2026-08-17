using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;

namespace NS_Parrot
{
    public class ChangeLayer : GH_Component
    {
        public enum ButtonColor { Black, Grey }

        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        internal bool ButtonRun { get; set; }
        private bool lastInputRun;

        public ChangeLayer()
          : base("修改图层", "修改图层",
              "将 Rhino 实体移动到指定图层；图层支持 A::B::C 子图层路径。",
              "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("实体Guid", "Guid", "要修改图层的 Rhino 实体 Guid 列表。", GH_ParamAccess.list);
            pManager.AddTextParameter("图层", "Layer", "目标图层，支持 A::B::C 子图层路径。", GH_ParamAccess.item);
            pManager.AddBooleanParameter("运行", "Run", "由 False 变为 True 时执行修改。", GH_ParamAccess.item, false);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<GH_Guid> guids = new List<GH_Guid>();
            if (!DA.GetDataList(0, guids))
                return;

            string layerName = string.Empty;
            if (!DA.GetData(1, ref layerName) || string.IsNullOrWhiteSpace(layerName))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "目标图层不能为空。");
                return;
            }

            bool inputRun = false;
            DA.GetData(2, ref inputRun);
            bool shouldRun = ButtonRun || (inputRun && !lastInputRun);
            lastInputRun = inputRun;
            ButtonRun = false;
            if (!shouldRun)
                return;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "当前没有可用的 Rhino 文档。");
                return;
            }

            int layerIndex = EnsureLayer(doc, layerName);
            if (layerIndex < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法找到或创建目标图层：" + layerName);
                return;
            }

            int changedCount = 0;
            for (int i = 0; i < guids.Count; i++)
            {
                GH_Guid ghGuid = guids[i];
                if (ghGuid == null || ghGuid.Value == Guid.Empty)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "第 " + (i + 1) + " 个 Guid 为空。");
                    continue;
                }

                RhinoObject obj = doc.Objects.FindId(ghGuid.Value);
                if (obj == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "找不到第 " + (i + 1) + " 个 Guid 对应的 Rhino 实体。");
                    continue;
                }

                obj.Attributes.LayerIndex = layerIndex;
                if (obj.CommitChanges())
                    changedCount++;
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "第 " + (i + 1) + " 个实体修改图层失败。");
            }

            if (changedCount > 0)
                doc.Views.Redraw();
        }

        private static int EnsureLayer(RhinoDoc doc, string layerName)
        {
            string normalizedName = (layerName ?? string.Empty).Trim();
            int layerIndex = doc.Layers.FindByFullPath(normalizedName, -1);
            if (layerIndex >= 0)
                return layerIndex;

            string[] names = normalizedName.Split(new[] { "::" }, StringSplitOptions.None);
            if (names.Length == 0)
                return -1;

            Guid parentId = Guid.Empty;
            string fullPath = string.Empty;
            foreach (string part in names)
            {
                string name = part.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    return -1;

                fullPath = string.IsNullOrEmpty(fullPath) ? name : fullPath + "::" + name;
                layerIndex = doc.Layers.FindByFullPath(fullPath, -1);
                if (layerIndex >= 0)
                {
                    Layer existingLayer = doc.Layers.FindIndex(layerIndex);
                    parentId = existingLayer == null ? Guid.Empty : existingLayer.Id;
                    continue;
                }

                Layer layer = new Layer { Name = name, ParentLayerId = parentId };
                layerIndex = doc.Layers.Add(layer);
                if (layerIndex < 0)
                    return -1;

                Layer newLayer = doc.Layers.FindIndex(layerIndex);
                parentId = newLayer == null ? Guid.Empty : newLayer.Id;
            }

            return layerIndex;
        }

        public override void CreateAttributes()
        {
            Attributes = new ChangeLayerButton(this);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ChangeLayer"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("D289AA99-8BE9-4415-8A0B-98F6439876DF"); }
        }
    }

    internal class ChangeLayerButton : GH_ComponentAttributes
    {
        public ChangeLayerButton(ChangeLayer component) : base(component) { }

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
            ChangeLayer component = (ChangeLayer)Owner;
            GH_Palette palette = component.CurrentButtonColor == ChangeLayer.ButtonColor.Black ? GH_Palette.Black : GH_Palette.Grey;

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("Run", font, Brushes.White, buttonRect, format);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20.0f, Bounds.Width, 20.0f);
            if (e.Button != MouseButtons.Left || !buttonRect.Contains(e.CanvasLocation))
                return GH_ObjectResponse.Ignore;

            ChangeLayer component = (ChangeLayer)Owner;
            component.CurrentButtonColor = ChangeLayer.ButtonColor.Grey;
            component.ButtonRun = true;
            component.ExpireSolution(true);
            CMath.Delay(50);
            component.CurrentButtonColor = ChangeLayer.ButtonColor.Black;
            component.ExpireSolution(true);
            return GH_ObjectResponse.Handled;
        }
    }
}
