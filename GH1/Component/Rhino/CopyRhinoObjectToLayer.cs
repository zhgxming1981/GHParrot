using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class CopyRhinoObjectToLayer : GH_Component
    {
        public enum ButtonColor { Black, Grey }

        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        internal bool ButtonRun { get; set; }

        private bool _lastInputRun;
        private List<Guid> _lastResult = new List<Guid>();

        public CopyRhinoObjectToLayer()
          : base("CopyRhinoObjectToLayer", "复制到图层",
              "复制Rhino对象，并可指定复制后对象所在图层",
              "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "Rhino对象Guid列表", GH_ParamAccess.list);
            pManager.AddVectorParameter("向量", "V", "复制对象相对于原对象的位移向量", GH_ParamAccess.item, Vector3d.Zero);
            pManager.AddTextParameter("图层", "Layer", "复制后对象所在图层列表；为空时按原图层复制", GH_ParamAccess.list);
            pManager.AddBooleanParameter("run", "run", "由False变为True时执行复制", GH_ParamAccess.item, false);

            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("GUID", "GUID", "复制后对象的Guid", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<GH_Guid> guids = new List<GH_Guid>();
            Vector3d moveVector = Vector3d.Zero;
            List<string> layerNames = new List<string>();
            bool inputRun = false;

            if (!DA.GetDataList(0, guids)) { return; }
            DA.GetData(1, ref moveVector);
            DA.GetDataList(2, layerNames);
            DA.GetData(3, ref inputRun);

            bool shouldRun = ButtonRun || (inputRun && !_lastInputRun);
            _lastInputRun = inputRun;
            ButtonRun = false;

            if (!shouldRun)
            {
                DA.SetDataList(0, _lastResult);
                return;
            }

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "当前没有可用的Rhino文档。");
                DA.SetDataList(0, _lastResult);
                return;
            }

            Transform transform = Transform.Translation(moveVector);
            List<Guid> result = new List<Guid>();

            for (int i = 0; i < guids.Count; i++)
            {
                GH_Guid ghGuid = guids[i];
                if (ghGuid == null || ghGuid.Value == Guid.Empty)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "第" + (i + 1) + "个Guid为空。");
                    continue;
                }

                RhinoObject obj = doc.Objects.FindId(ghGuid.Value);
                if (obj == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "找不到第" + (i + 1) + "个Guid对应的Rhino对象。");
                    continue;
                }

                string layerName = GetLayerName(layerNames, i);
                if (TryCopyObject(doc, obj, transform, layerName, out Guid newId))
                    result.Add(newId);
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "第" + (i + 1) + "个对象复制失败。");
            }

            doc.Views.Redraw();
            _lastResult = result;
            DA.SetDataList(0, result);
        }

        private static string GetLayerName(List<string> layerNames, int index)
        {
            if (layerNames == null || layerNames.Count == 0)
                return string.Empty;

            if (layerNames.Count == 1)
                return layerNames[0];

            if (index < layerNames.Count)
                return layerNames[index];

            return string.Empty;
        }

        private static bool TryCopyObject(RhinoDoc doc, RhinoObject obj, Transform transform, string layerName, out Guid newId)
        {
            newId = Guid.Empty;
            if (doc == null || obj == null)
                return false;

            ObjectAttributes attributes = obj.Attributes?.Duplicate() ?? doc.CreateDefaultAttributes();
            attributes.ObjectId = Guid.NewGuid();
            if (!string.IsNullOrWhiteSpace(layerName))
            {
                int layerIndex = EnsureLayer(doc, layerName);
                if (layerIndex < 0)
                    return false;

                attributes.LayerIndex = layerIndex;
            }

            if (obj is InstanceObject instanceObject)
            {
                InstanceDefinition definition = instanceObject.InstanceDefinition;
                if (definition == null)
                    return false;

                Transform instanceTransform = instanceObject.InstanceXform;
                instanceTransform = transform * instanceTransform;
                newId = doc.Objects.AddInstanceObject(definition.Index, instanceTransform, attributes);
                return newId != Guid.Empty;
            }

            GeometryBase geometry = obj.Geometry?.Duplicate();
            if (geometry == null)
                return false;

            if (!geometry.Transform(transform))
                return false;

            newId = doc.Objects.Add(geometry, attributes);
            return newId != Guid.Empty;
        }

        private static int EnsureLayer(RhinoDoc doc, string layerName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(layerName))
                return -1;

            int layerIndex = doc.Layers.FindByFullPath(layerName, -1);
            if (layerIndex >= 0)
                return layerIndex;

            string[] names = layerName.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
            if (names.Length == 0)
                return -1;

            Guid parentId = Guid.Empty;
            string fullPath = string.Empty;
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i].Trim();
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

                Layer layer = new Layer();
                layer.Name = name;
                if (parentId != Guid.Empty)
                    layer.ParentLayerId = parentId;

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
            Attributes = new CButton_CopyRhinoObjectToLayer(this);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_CopyRhinoObjectToLayer"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("A607F913-1977-4653-A749-6E3869E4037B"); }
        }
    }

    internal class CButton_CopyRhinoObjectToLayer : GH_ComponentAttributes
    {
        public CButton_CopyRhinoObjectToLayer(CopyRhinoObjectToLayer component) : base(component) { }

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

            GH_Palette palette = ((CopyRhinoObjectToLayer)Owner).CurrentButtonColor == CopyRhinoObjectToLayer.ButtonColor.Black
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
                CopyRhinoObjectToLayer owner = (CopyRhinoObjectToLayer)Owner;
                owner.CurrentButtonColor = CopyRhinoObjectToLayer.ButtonColor.Grey;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                CMath.Delay(50);
                owner.CurrentButtonColor = CopyRhinoObjectToLayer.ButtonColor.Black;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
