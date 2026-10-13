using CommonFunction;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace NS_Parrot
{
    public class DrawingTitle : GH_Component
    {
        private readonly List<TextEntity> preview = new List<TextEntity>();
        private BoundingBox previewBox = BoundingBox.Empty;
        private Color textColor = Color.Black;
        private bool lastBake;

        public DrawingTitle() : base("标注图名", "标注图名",
            "按 Group 分组的 Top 视图包围盒，在下方居中生成图名文字。", "Parrot", "Drawing") { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGeometryParameter("图纸", "图纸", "连接 Group 输出；每个组独立计算包围盒并生成一个图名。", GH_ParamAccess.tree);
            p.AddTextParameter("视图名称", "名称", "按数据树顺序与各组一一对应的图名，数量必须一致。", GH_ParamAccess.tree);
            p.AddNumberParameter("文字偏移距离", "偏移", "图名上边缘到包围盒下边缘的距离，不能小于零。", GH_ParamAccess.item, 70.0);
            p.AddNumberParameter("文字大小", "大小", "模型单位下的文字高度，必须大于零。", GH_ParamAccess.item, 14.0);
            p.AddTextParameter("图层名", "图层", "Bake 目标图层，支持父层::子层；留空使用当前图层。", GH_ParamAccess.item, "图名");
            p.AddColourParameter("字体颜色", "颜色", "预览和 Bake 的文字颜色，默认黑色。", GH_ParamAccess.item, Color.Black);
            p.AddTextParameter("文字样式", "样式", "Rhino 文档中的标注样式名称；留空使用文档当前默认样式，文字高度由大小输入控制。", GH_ParamAccess.item, "");
            p.AddBooleanParameter("Bake", "Bake", "由 False 切换到 True 时烘焙一次；保持 True 不重复烘焙。", GH_ParamAccess.item, false);
            for (int i = 2; i < 8; i++) p[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("图名列表", "图名", "图名文字实体，保留图纸输入的数据树路径。", GH_ParamAccess.tree);
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            ClearPreview();
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (!CHardware.CheckLegality()) return;
            bool bake = false;
            da.GetData(7, ref bake);
            bool shouldBake = bake && !lastBake;
            lastBake = bake;
            GH_Structure<IGH_GeometricGoo> drawings;
            GH_Structure<GH_String> names;
            if (!da.GetDataTree(0, out drawings) || !da.GetDataTree(1, out names)) return;
            double offset = 70.0, height = 14.0;
            string layer = "图名", styleName = "";
            textColor = Color.Black;
            da.GetData(2, ref offset);
            da.GetData(3, ref height);
            da.GetData(4, ref layer);
            da.GetData(5, ref textColor);
            da.GetData(6, ref styleName);
            if (double.IsNaN(offset) || double.IsInfinity(offset) || offset < 0 ||
                double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "偏移距离必须是非负有限数，文字大小必须是大于零的有限数。");
                return;
            }
            var titleNames = names.Branches.SelectMany(branch => branch).Select(n => n?.Value).ToList();
            if (drawings.DataCount != titleNames.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "图纸组数与视图名称数量不一致，已停止生成和 Bake。");
                return;
            }
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "没有可用的 Rhino 文档。");
                return;
            }
            DimensionStyle style = string.IsNullOrWhiteSpace(styleName)
                ? doc.DimStyles.Current : doc.DimStyles.FindName(styleName.Trim());
            if (style == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "找不到文字样式：" + styleName);
                return;
            }

            var output = new GH_Structure<GH_ObjectWrapper>();
            int index = 0;
            foreach (GH_Path path in drawings.Paths)
            {
                foreach (IGH_GeometricGoo drawing in drawings.get_Branch(path))
                {
                    string name = titleNames[index++];
                    BoundingBox box = drawing?.Boundingbox ?? BoundingBox.Empty;
                    if (!box.IsValid || string.IsNullOrWhiteSpace(name))
                    {
                        ClearPreview();
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "第 " + index + " 组的包围盒无效或图名为空，已停止生成和 Bake。");
                        return;
                    }
                    TextEntity text = TextEntity.Create(name, Plane.WorldXY, style, false, 0, 0);
                    if (text == null)
                    {
                        ClearPreview();
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "第 " + index + " 组文字创建失败。");
                        return;
                    }
                    text.TextHeight = height;
                    text.DimensionScale = 1.0;
                    text.Justification = TextJustification.TopCenter;
                    BoundingBox bounds = text.GetBoundingBox(true);
                    if (!bounds.IsValid)
                    {
                        text.Dispose();
                        ClearPreview();
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "文字包围盒无效，已停止生成和 Bake。");
                        return;
                    }
                    // 按实际文字边界定位，避免字体留白造成偏移不一致。
                    text.Transform(Transform.Translation(
                        box.Center.X - bounds.Center.X, box.Min.Y - offset - bounds.Max.Y, 0));
                    preview.Add(text);
                    previewBox.Union(text.GetBoundingBox(true));
                    output.Append(new GH_ObjectWrapper(text.Duplicate()), path);
                }
            }
            da.SetDataTree(0, output);
            if (shouldBake && preview.Count > 0) BakeTitles(doc, layer);
        }

        private void BakeTitles(RhinoDoc doc, string layerName)
        {
            uint undo = doc.BeginUndoRecord("标注图名");
            try
            {
                int layerIndex = doc.Layers.CurrentLayerIndex;
                if (!string.IsNullOrWhiteSpace(layerName))
                {
                    layerIndex = doc.Layers.FindByFullPath(layerName.Trim(), -1);
                    if (layerIndex < 0) layerIndex = doc.Layers.AddPath(layerName.Trim(), Color.Black);
                }
                if (layerIndex < 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法找到或创建目标图层，Bake 已停止。");
                    return;
                }
                int count = 0;
                foreach (TextEntity text in preview)
                {
                    using (ObjectAttributes attributes = doc.CreateDefaultAttributes())
                    {
                        attributes.LayerIndex = layerIndex;
                        attributes.ColorSource = ObjectColorSource.ColorFromObject;
                        attributes.ObjectColor = textColor;
                        if (doc.Objects.AddText(text, attributes) != Guid.Empty) count++;
                    }
                }
                if (count != preview.Count)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bake 仅成功 " + count + "/" + preview.Count + " 个图名，可在 Rhino 中撤销本次操作。");
            }
            finally
            {
                if (undo != 0) doc.EndUndoRecord(undo);
                doc.Views.Redraw();
            }
        }

        private void ClearPreview()
        {
            foreach (TextEntity text in preview) text.Dispose();
            preview.Clear();
            previewBox = BoundingBox.Empty;
        }

        public override bool IsPreviewCapable => true;
        public override BoundingBox ClippingBox => previewBox;
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            foreach (TextEntity text in preview) args.Display.DrawText(text, textColor);
        }
        public override void RemovedFromDocument(GH_Document document)
        {
            ClearPreview();
            base.RemovedFromDocument(document);
        }
        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("CE839768-D0BB-4A9C-92B3-2FD58585AE37");
    }
}
