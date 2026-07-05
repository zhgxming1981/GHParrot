using CommonFunction;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Display;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace NS_Parrot
{
    public class LayoutDrawings : GH_Component
    {
        private static readonly double[] DefaultScales = { 1, 2, 5, 10, 20, 25, 50, 100, 200 };
        private const double DefaultPaperGap = 20.0;
        private const double DefaultPaperMargin = 50.0;
        private const double PaperTextHeight = 3.5;
        private const double PaperDimOffset = 8.0;

        private bool _lastRun;
        private LayoutCache _cache = new LayoutCache();

        public LayoutDrawings()
          : base("图纸布置", "图纸布置",
              "按正视图、左视图、右视图、俯视图、仰视图、后视图、剖视图和详图端口接入情况自动布置图纸，插入图框并生成基本标注和图名。",
              "Parrot", "Drawing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("正视图", "Front", "必选。布置基准视图，接入从正面投影得到的图纸曲线、文字、面边线等二维几何；其它视图围绕正视图按投影关系布置。为空时报错停止。", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("左视图", "Left", "可选。从左边看得到的视图；端口接入且有有效几何时参与布置，按第一角投影规则放在正视图右侧，Y 方向与正视图中心对齐。", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("右视图", "Right", "可选。从右边看得到的视图；端口接入且有有效几何时参与布置，放在正视图左侧，Y 方向与正视图中心对齐。", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("俯视图", "Top", "可选。从上面看得到的视图；端口接入且有有效几何时参与布置，放在正视图下侧，X 方向与正视图中心对齐。", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("仰视图", "Bottom", "可选。从下面看得到的视图；端口接入且有有效几何时参与布置，放在正视图上侧，X 方向与正视图中心对齐。", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("后视图", "Back", "可选。从背面看得到的视图；端口接入且有有效几何时参与布置，优先放在左视图右侧，若没有左视图则放在正视图右侧。", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("剖视图", "Section", "可选。剖面图纸几何，可接 SectionMake2D 输出；端口接入时作为辅助视图布置，默认排在主视图组下方，每个分支视为一个剖视图。", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("详图", "Detail", "可选。详图几何；端口接入时作为辅助视图布置，默认排在主视图组下方并接在剖视图之后，每个分支视为一个详图。", GH_ParamAccess.tree);
            pManager.AddGenericParameter("图框", "Frame", "必选。Rhino 文档中的图框块实例 Guid。组件读取块定义并插入新的图框实例；若块内存在名为“图框绘图区”的图层并有矩形曲线，则以该矩形作为可布置区域，否则以整个图框块包围盒作为绘图区。", GH_ParamAccess.item);
            pManager.AddTextParameter("图框属性", "Attrs", "可选。写入图框标题栏属性文字，按块定义中文字对象顺序匹配；文本中的 {Scale} 会替换为最终比例，例如 1:20。", GH_ParamAccess.list);
            pManager.AddPointParameter("插入点", "Pt", "可选。最终图框左下角点；未接入时使用世界坐标原点。所有布置后的视图、标注、图名与图框一起围绕该点定位。", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("指定比例", "Scale", "可选。出图比例；0 或小于等于 0 表示自动计算，输入 20 表示 1:20。比例会影响图框缩放、视图间距、图框边距、标注偏移和文字高度。", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("标准比例", "Scales", "可选。自动比例候选列表，默认 1, 2, 5, 10, 20, 25, 50, 100, 200；自动计算时从小到大选择第一个能容纳所有视图、标注和图名的比例。", GH_ParamAccess.list);
            pManager.AddNumberParameter("视图间距", "Gap", "可选。图框比例为 1 时的视图间纸面净距基准值；实际间距 = Gap * 使用比例。小于等于 0 时自动取默认值。", GH_ParamAccess.item, DefaultPaperGap);
            pManager.AddNumberParameter("图框边距", "Margin", "可选。图框比例为 1 时，内容到图框绘图区边界的纸面边距基准值；实际边距 = Margin * 使用比例。例如 Margin=50、比例 1:20 时，实际边距为 1000。", GH_ParamAccess.item, DefaultPaperMargin);
            pManager.AddBooleanParameter("标注", "Dim", "可选。是否自动给每个参与布置的视图添加包围盒尺寸标注。默认 True；标注包括水平总宽和竖向总高，标注偏移和文字高度跟随使用比例换算。", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("执行", "Run", "建议必接。控制有副作用的操作；从 False 切换到 True 时插入图框实例并更新缓存，避免 Grasshopper 每次刷新重复插入图框。", GH_ParamAccess.item, false);

            for (int i = 1; i <= 7; i++)
                pManager[i].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
            pManager[12].Optional = true;
            pManager[13].Optional = true;
            pManager[14].Optional = true;
            pManager[15].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("布置图形", "G", "所有参与视图经过移动后的图纸几何。路径按视图类型和原始分支组织，方便后续分层、烘焙或导出。", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("标注", "Dim", "自动生成的包围盒尺寸标注；每个视图通常输出水平、竖向两条标注，关闭 Dim 时为空。", GH_ParamAccess.tree);
            pManager.AddGenericParameter("图名", "Title", "自动生成的图名文字对象；正视图、左视图等使用默认图名，剖视图和详图按分支编号生成。", GH_ParamAccess.tree);
            pManager.AddCurveParameter("包围框", "Box", "每个视图布置后的包围盒矩形线，用于检查排版位置和标注范围。", GH_ParamAccess.tree);
            pManager.AddGenericParameter("图框实例", "Frame", "插入后的图框实例 Guid；Run=False 且已有缓存时输出上一次结果。", GH_ParamAccess.item);
            pManager.AddNumberParameter("使用比例", "Scale", "实际采用的比例值，例如输出 20 表示 1:20。", GH_ParamAccess.item);
            pManager.AddNumberParameter("标注比例", "DimScale", "标注相关换算比例，通常与 Scale 相同；后续接 Rhino 标注样式时可作为 DimensionScale。", GH_ParamAccess.item);
            pManager.AddNumberParameter("文字高度", "TextH", "实际使用的图名和标注文字参考高度，已按比例换算。", GH_ParamAccess.item);
            pManager.AddTextParameter("诊断信息", "Log", "输出端口接入情况、有效视图数量、图框绘图区尺寸、自动比例选择、实际边距/间距、缺失或无效几何等信息。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            bool run = false;
            DA.GetData(16, ref run);
            bool shouldRun = run && !_lastRun;
            _lastRun = run;

            if (!shouldRun)
            {
                if (_cache.Log.Count == 0)
                    _cache.Log.Add("尚未执行：请将 Run 从 False 切换到 True。");
                else
                    _cache.Log.Add("当前输出来自缓存；保持 Run=True 不会重复插入图框。");
                SetOutputs(DA, _cache);
                return;
            }

            LayoutCache result = new LayoutCache();
            try
            {
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "找不到当前 Rhino 文档。");
                    result.Log.Add("停止：找不到当前 Rhino 文档。");
                    SetOutputs(DA, result);
                    return;
                }

                List<ViewItem> views = CollectViews(DA, result.Log);
                ViewItem front = views.FirstOrDefault(v => v.Kind == ViewKind.Front);
                if (front == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "正视图为空。");
                    result.Log.Add("停止：正视图为空。");
                    SetOutputs(DA, result);
                    return;
                }

                IGH_Goo frameGoo = null;
                if (!DA.GetData(8, ref frameGoo) || !TryGetGuid(frameGoo, out Guid sourceFrameId))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "图框输入不是有效的 Rhino 图框块实例 Guid。");
                    result.Log.Add("停止：图框输入无效。");
                    SetOutputs(DA, result);
                    return;
                }

                InstanceObject sourceFrame = doc.Objects.FindId(sourceFrameId) as InstanceObject;
                if (sourceFrame == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "找不到图框块实例。");
                    result.Log.Add("停止：找不到图框块实例。");
                    SetOutputs(DA, result);
                    return;
                }

                InstanceDefinition frameDef = sourceFrame.InstanceDefinition;
                BoundingBox frameBox = GetDefinitionBoundingBox(frameDef);
                BoundingBox drawingAreaBox = GetDrawingAreaBox(doc, frameDef, frameBox);
                if (!frameBox.IsValid || !drawingAreaBox.IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法计算图框或绘图区包围盒。");
                    result.Log.Add("停止：无法计算图框或绘图区包围盒。");
                    SetOutputs(DA, result);
                    return;
                }

                List<string> attrs = new List<string>();
                DA.GetDataList(9, attrs);
                Point3d insertPt = Point3d.Origin;
                double inputScale = 0.0;
                double paperGap = DefaultPaperGap;
                double paperMargin = DefaultPaperMargin;
                bool makeDimensions = true;
                List<double> candidateScales = new List<double>();
                DA.GetData(10, ref insertPt);
                DA.GetData(11, ref inputScale);
                DA.GetDataList(12, candidateScales);
                DA.GetData(13, ref paperGap);
                DA.GetData(14, ref paperMargin);
                DA.GetData(15, ref makeDimensions);

                if (candidateScales.Count == 0)
                    candidateScales.AddRange(DefaultScales);
                candidateScales = candidateScales.Where(s => s > 0.0).Distinct().OrderBy(s => s).ToList();
                if (candidateScales.Count == 0)
                    candidateScales.AddRange(DefaultScales);

                double usedScale = inputScale > 0.0
                    ? inputScale
                    : ChooseScale(views, drawingAreaBox, candidateScales, paperGap, paperMargin, makeDimensions);
                double actualGap = Math.Max(paperGap, 0.0);
                if (actualGap <= 0.0)
                    actualGap = DefaultPaperGap;
                actualGap *= usedScale;
                double actualMargin = Math.Max(paperMargin, 0.0);
                if (actualMargin <= 0.0)
                    actualMargin = DefaultPaperMargin;
                actualMargin *= usedScale;
                double textHeight = PaperTextHeight * usedScale;
                double dimOffset = PaperDimOffset * usedScale;

                LayoutViews(views, actualGap);
                BoundingBox annotatedLocalBox = ComputeAnnotatedBox(views, makeDimensions, dimOffset, textHeight);

                Transform frameTransform = CreateFrameTransform(frameBox, insertPt, usedScale);
                BoundingBox actualDrawingArea = drawingAreaBox;
                actualDrawingArea.Transform(frameTransform);
                Point3d targetCenter = actualDrawingArea.Center;
                Vector3d contentMove = targetCenter - annotatedLocalBox.Center;
                Transform contentTransform = Transform.Translation(contentMove);

                PopulateOutputs(views, contentTransform, makeDimensions, dimOffset, textHeight, result);

                Guid insertedFrameId = doc.Objects.AddInstanceObject(frameDef.Index, frameTransform, new ObjectAttributes());
                result.FrameId = insertedFrameId;
                if (insertedFrameId == Guid.Empty)
                    result.Log.Add("警告：图框插入失败。");

                ApplyFrameText(frameDef, attrs, usedScale);

                result.Scale = usedScale;
                result.DimScale = usedScale;
                result.TextHeight = textHeight;
                result.Log.Add("有效视图数量: " + views.Count.ToString(CultureInfo.InvariantCulture));
                result.Log.Add("使用比例: 1:" + FormatNumber(usedScale));
                result.Log.Add("实际视图间距: " + FormatNumber(actualGap));
                result.Log.Add("实际图框边距: " + FormatNumber(actualMargin));
                result.Log.Add("图框绘图区: 宽 " + FormatNumber(drawingAreaBox.Max.X - drawingAreaBox.Min.X) + "，高 " + FormatNumber(drawingAreaBox.Max.Y - drawingAreaBox.Min.Y));

                if (!FitsInDrawingArea(annotatedLocalBox, drawingAreaBox, usedScale, actualMargin))
                    result.Log.Add("警告：当前指定比例可能无法完全容纳图纸、标注和图名。");

                _cache = result;
                SetOutputs(DA, _cache);
            }
            catch (Exception ex)
            {
                result.Log.Add("执行中断: " + ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                SetOutputs(DA, result);
            }
        }

        private List<ViewItem> CollectViews(IGH_DataAccess DA, List<string> log)
        {
            List<ViewItem> views = new List<ViewItem>();
            AddSingleView(DA, 0, ViewKind.Front, "正视图", log, views);
            AddSingleView(DA, 1, ViewKind.Left, "左视图", log, views);
            AddSingleView(DA, 2, ViewKind.Right, "右视图", log, views);
            AddSingleView(DA, 3, ViewKind.Top, "俯视图", log, views);
            AddSingleView(DA, 4, ViewKind.Bottom, "仰视图", log, views);
            AddSingleView(DA, 5, ViewKind.Back, "后视图", log, views);
            AddBranchViews(DA, 6, ViewKind.Section, "剖视图", log, views);
            AddBranchViews(DA, 7, ViewKind.Detail, "详图", log, views);
            return views;
        }

        private void AddSingleView(IGH_DataAccess DA, int index, ViewKind kind, string title, List<string> log, List<ViewItem> views)
        {
            if (index > 0 && Params.Input[index].SourceCount == 0)
                return;

            if (!DA.GetDataTree(index, out GH_Structure<IGH_GeometricGoo> tree))
                return;

            List<IGH_GeometricGoo> geometry = tree.AllData(true).OfType<IGH_GeometricGoo>().Where(g => g != null).ToList();
            BoundingBox box = GetGeometryBox(geometry);
            if (!box.IsValid)
            {
                log.Add(title + "：端口已接入但没有有效几何。");
                return;
            }

            views.Add(new ViewItem(kind, title, new GH_Path((int)kind), geometry, box));
            log.Add(title + "：接入有效几何 " + geometry.Count.ToString(CultureInfo.InvariantCulture) + " 个。");
        }

        private void AddBranchViews(IGH_DataAccess DA, int index, ViewKind kind, string title, List<string> log, List<ViewItem> views)
        {
            if (Params.Input[index].SourceCount == 0)
                return;

            if (!DA.GetDataTree(index, out GH_Structure<IGH_GeometricGoo> tree))
                return;

            int count = 0;
            for (int i = 0; i < tree.PathCount; i++)
            {
                GH_Path path = tree.Paths[i];
                List<IGH_GeometricGoo> geometry = tree.get_Branch(path).OfType<IGH_GeometricGoo>().Where(g => g != null).ToList();
                BoundingBox box = GetGeometryBox(geometry);
                if (!box.IsValid)
                    continue;

                count++;
                string itemTitle = title + " " + count.ToString(CultureInfo.InvariantCulture);
                views.Add(new ViewItem(kind, itemTitle, new GH_Path((int)kind, count - 1), geometry, box));
            }

            log.Add(title + "：接入有效分支 " + count.ToString(CultureInfo.InvariantCulture) + " 个。");
        }

        private static BoundingBox GetGeometryBox(IEnumerable<IGH_GeometricGoo> geometry)
        {
            BoundingBox box = BoundingBox.Empty;
            foreach (IGH_GeometricGoo item in geometry)
            {
                BoundingBox itemBox = item.Boundingbox;
                if (itemBox.IsValid)
                    box.Union(itemBox);
            }
            return box;
        }

        private static void LayoutViews(List<ViewItem> views, double gap)
        {
            ViewItem front = views.First(v => v.Kind == ViewKind.Front);
            PlaceView(front, new Point3d(0, 0, 0));

            ViewItem left = views.FirstOrDefault(v => v.Kind == ViewKind.Left);
            if (left != null)
                PlaceView(left, new Point3d(front.PlacedBox.Max.X + gap, CenterY(front.PlacedBox) - Height(left.SourceBox) / 2.0, 0));

            ViewItem right = views.FirstOrDefault(v => v.Kind == ViewKind.Right);
            if (right != null)
                PlaceView(right, new Point3d(front.PlacedBox.Min.X - gap - Width(right.SourceBox), CenterY(front.PlacedBox) - Height(right.SourceBox) / 2.0, 0));

            ViewItem top = views.FirstOrDefault(v => v.Kind == ViewKind.Top);
            if (top != null)
                PlaceView(top, new Point3d(CenterX(front.PlacedBox) - Width(top.SourceBox) / 2.0, front.PlacedBox.Min.Y - gap - Height(top.SourceBox), 0));

            ViewItem bottom = views.FirstOrDefault(v => v.Kind == ViewKind.Bottom);
            if (bottom != null)
                PlaceView(bottom, new Point3d(CenterX(front.PlacedBox) - Width(bottom.SourceBox) / 2.0, front.PlacedBox.Max.Y + gap, 0));

            ViewItem back = views.FirstOrDefault(v => v.Kind == ViewKind.Back);
            if (back != null)
            {
                ViewItem anchor = left ?? front;
                PlaceView(back, new Point3d(anchor.PlacedBox.Max.X + gap, CenterY(front.PlacedBox) - Height(back.SourceBox) / 2.0, 0));
            }

            List<ViewItem> aux = views.Where(v => v.Kind == ViewKind.Section || v.Kind == ViewKind.Detail).ToList();
            if (aux.Count == 0)
                return;

            BoundingBox mainBox = ComputePlacedBox(views.Where(v => v.Kind != ViewKind.Section && v.Kind != ViewKind.Detail));
            double rowY = mainBox.Min.Y - gap - aux.Max(v => Height(v.SourceBox));
            double cursorX = mainBox.Min.X;
            foreach (ViewItem view in aux)
            {
                PlaceView(view, new Point3d(cursorX, rowY, 0));
                cursorX = view.PlacedBox.Max.X + gap;
            }
        }

        private static void PlaceView(ViewItem view, Point3d targetMin)
        {
            Vector3d offset = targetMin - view.SourceBox.Min;
            view.LocalTransform = Transform.Translation(offset);
            view.PlacedBox = view.SourceBox;
            view.PlacedBox.Transform(view.LocalTransform);
        }

        private static double ChooseScale(List<ViewItem> views, BoundingBox drawingAreaBox, List<double> candidateScales, double paperGap, double paperMargin, bool makeDimensions)
        {
            foreach (double scale in candidateScales)
            {
                double gap = Math.Max(paperGap, DefaultPaperGap) * scale;
                double margin = Math.Max(paperMargin, DefaultPaperMargin) * scale;
                double textHeight = PaperTextHeight * scale;
                double dimOffset = PaperDimOffset * scale;
                LayoutViews(views, gap);
                BoundingBox annotatedBox = ComputeAnnotatedBox(views, makeDimensions, dimOffset, textHeight);
                if (FitsInDrawingArea(annotatedBox, drawingAreaBox, scale, margin))
                    return scale;
            }

            return candidateScales.Last();
        }

        private static bool FitsInDrawingArea(BoundingBox annotatedBox, BoundingBox drawingAreaBox, double scale, double margin)
        {
            double availableW = Width(drawingAreaBox) * scale - 2.0 * margin;
            double availableH = Height(drawingAreaBox) * scale - 2.0 * margin;
            return Width(annotatedBox) <= availableW && Height(annotatedBox) <= availableH;
        }

        private static BoundingBox ComputeAnnotatedBox(List<ViewItem> views, bool makeDimensions, double dimOffset, double textHeight)
        {
            BoundingBox box = ComputePlacedBox(views);
            double right = makeDimensions ? dimOffset + textHeight * 3.0 : 0.0;
            double bottom = textHeight * 3.0 + (makeDimensions ? dimOffset + textHeight : 0.0);
            box.Union(new Point3d(box.Max.X + right, box.Max.Y, 0));
            box.Union(new Point3d(box.Min.X, box.Min.Y - bottom, 0));
            return box;
        }

        private static BoundingBox ComputePlacedBox(IEnumerable<ViewItem> views)
        {
            BoundingBox box = BoundingBox.Empty;
            foreach (ViewItem view in views)
            {
                if (view.PlacedBox.IsValid)
                    box.Union(view.PlacedBox);
            }
            return box;
        }

        private static void PopulateOutputs(List<ViewItem> views, Transform contentTransform, bool makeDimensions, double dimOffset, double textHeight, LayoutCache result)
        {
            foreach (ViewItem view in views)
            {
                Transform transform = contentTransform * view.LocalTransform;
                foreach (IGH_GeometricGoo item in view.Geometry)
                {
                    IGH_GeometricGoo moved = item.DuplicateGeometry();
                    moved = moved.Transform(transform);
                    result.Geometry.Append(moved, view.Path);
                }

                BoundingBox box = view.PlacedBox;
                box.Transform(contentTransform);
                foreach (Curve curve in BoxToRect(box))
                    result.Boxes.Add(curve, view.Path);

                Text3d titleText = CreateTitle(view.Title, box, textHeight, dimOffset);
                result.Titles.Append(new Text3dGoo(titleText), view.Path);

                if (makeDimensions)
                {
                    foreach (GeometryBase dim in MakeDimensions(box, dimOffset))
                    {
                        IGH_GeometricGoo goo = SectionMake2DV2.CreateGeometryGoo(dim);
                        if (goo != null)
                            result.Dimensions.Append(goo, view.Path);
                    }
                }
            }
        }

        private static Text3d CreateTitle(string title, BoundingBox box, double textHeight, double dimOffset)
        {
            Point3d origin = new Point3d(CenterX(box), box.Min.Y - dimOffset - textHeight * 2.0, 0);
            Plane plane = new Plane(origin, Vector3d.XAxis, Vector3d.YAxis);
            Text3d text = new Text3d(title, plane, textHeight)
            {
                HorizontalAlignment = TextHorizontalAlignment.Center,
                VerticalAlignment = TextVerticalAlignment.Middle
            };
            return text;
        }

        private static List<GeometryBase> MakeDimensions(BoundingBox box, double offset)
        {
            List<GeometryBase> result = new List<GeometryBase>();
            Plane plane = new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis);
            Point2d extA = new Point2d(box.Min.X, box.Min.Y);
            Point2d extB = new Point2d(box.Max.X, box.Min.Y);
            Point2d extC = new Point2d(box.Max.X, box.Max.Y);
            Point2d txtH = new Point2d((box.Min.X + box.Max.X) / 2.0, box.Min.Y - offset);
            Point2d txtV = new Point2d(box.Max.X + offset, (box.Min.Y + box.Max.Y) / 2.0);
            result.Add(new LinearDimension(plane, extA, extB, txtH));
            result.Add(new LinearDimension(plane, extB, extC, txtV));
            return result;
        }

        private static List<Curve> BoxToRect(BoundingBox box)
        {
            Point3d a = new Point3d(box.Min.X, box.Min.Y, 0);
            Point3d b = new Point3d(box.Max.X, box.Min.Y, 0);
            Point3d c = new Point3d(box.Max.X, box.Max.Y, 0);
            Point3d d = new Point3d(box.Min.X, box.Max.Y, 0);
            return new List<Curve>
            {
                new LineCurve(a, b),
                new LineCurve(b, c),
                new LineCurve(c, d),
                new LineCurve(d, a)
            };
        }

        private static Transform CreateFrameTransform(BoundingBox frameBox, Point3d insertPt, double scale)
        {
            Transform scaleTransform = Transform.Scale(Point3d.Origin, scale);
            Point3d scaledMin = frameBox.Min;
            scaledMin.Transform(scaleTransform);
            Transform moveTransform = Transform.Translation(insertPt - scaledMin);
            return moveTransform * scaleTransform;
        }

        private static BoundingBox GetDefinitionBoundingBox(InstanceDefinition def)
        {
            BoundingBox box = BoundingBox.Empty;
            foreach (RhinoObject obj in def.GetObjects())
            {
                BoundingBox itemBox = obj.Geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;
                if (itemBox.IsValid)
                    box.Union(itemBox);
            }
            return box;
        }

        private static BoundingBox GetDrawingAreaBox(RhinoDoc doc, InstanceDefinition def, BoundingBox fallback)
        {
            Layer layer = doc.Layers.FindName("图框绘图区");
            if (layer == null)
                return fallback;

            BoundingBox box = BoundingBox.Empty;
            foreach (RhinoObject obj in def.GetObjects())
            {
                if (obj.Attributes.LayerIndex == layer.Index && obj.Geometry is Curve curve)
                {
                    BoundingBox itemBox = curve.GetBoundingBox(true);
                    if (itemBox.IsValid)
                        box.Union(itemBox);
                }
            }
            return box.IsValid ? box : fallback;
        }

        private static void ApplyFrameText(InstanceDefinition def, List<string> attrs, double scale)
        {
            if (attrs == null || attrs.Count == 0)
                return;

            List<string> values = attrs.Select(a => (a ?? string.Empty).Replace("{Scale}", "1:" + FormatNumber(scale))).ToList();
            int index = 0;
            foreach (RhinoObject obj in def.GetObjects())
            {
                if (index >= values.Count)
                    break;

                if (obj is TextObject textObj && textObj.TextGeometry != null)
                {
                    textObj.TextGeometry.PlainText = values[index];
                    textObj.CommitChanges();
                    index++;
                }
            }
        }

        private static bool TryGetGuid(IGH_Goo goo, out Guid id)
        {
            id = Guid.Empty;
            if (goo == null)
                return false;

            if (goo is GH_Guid ghGuid)
            {
                id = ghGuid.Value;
                return id != Guid.Empty;
            }

            if (goo.CastTo(out Guid castGuid))
            {
                id = castGuid;
                return id != Guid.Empty;
            }

            if (goo is GH_String ghString && Guid.TryParse(ghString.Value, out Guid parsed))
            {
                id = parsed;
                return id != Guid.Empty;
            }

            return false;
        }

        private void SetOutputs(IGH_DataAccess DA, LayoutCache cache)
        {
            DA.SetDataTree(0, cache.Geometry);
            DA.SetDataTree(1, cache.Dimensions);
            DA.SetDataTree(2, cache.Titles);
            DA.SetDataTree(3, cache.Boxes);
            if (cache.FrameId != Guid.Empty)
                DA.SetData(4, cache.FrameId);
            DA.SetData(5, cache.Scale);
            DA.SetData(6, cache.DimScale);
            DA.SetData(7, cache.TextHeight);
            DA.SetDataList(8, cache.Log);
        }

        private static double Width(BoundingBox box)
        {
            return box.Max.X - box.Min.X;
        }

        private static double Height(BoundingBox box)
        {
            return box.Max.Y - box.Min.Y;
        }

        private static double CenterX(BoundingBox box)
        {
            return (box.Min.X + box.Max.X) / 2.0;
        }

        private static double CenterY(BoundingBox box)
        {
            return (box.Min.Y + box.Max.Y) / 2.0;
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_Block"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("D7DF62AF-05AE-4D62-8E58-9F3CE88D4D64"); }
        }

        private enum ViewKind
        {
            Front = 0,
            Left = 1,
            Right = 2,
            Top = 3,
            Bottom = 4,
            Back = 5,
            Section = 6,
            Detail = 7
        }

        private class ViewItem
        {
            public ViewItem(ViewKind kind, string title, GH_Path path, List<IGH_GeometricGoo> geometry, BoundingBox sourceBox)
            {
                Kind = kind;
                Title = title;
                Path = path;
                Geometry = geometry;
                SourceBox = sourceBox;
                PlacedBox = BoundingBox.Empty;
                LocalTransform = Transform.Identity;
            }

            public ViewKind Kind { get; }
            public string Title { get; }
            public GH_Path Path { get; }
            public List<IGH_GeometricGoo> Geometry { get; }
            public BoundingBox SourceBox { get; }
            public BoundingBox PlacedBox { get; set; }
            public Transform LocalTransform { get; set; }
        }

        private class LayoutCache
        {
            public GH_Structure<IGH_GeometricGoo> Geometry { get; } = new GH_Structure<IGH_GeometricGoo>();
            public GH_Structure<IGH_GeometricGoo> Dimensions { get; } = new GH_Structure<IGH_GeometricGoo>();
            public GH_Structure<IGH_Goo> Titles { get; } = new GH_Structure<IGH_Goo>();
            public DataTree<Curve> Boxes { get; } = new DataTree<Curve>();
            public Guid FrameId { get; set; } = Guid.Empty;
            public double Scale { get; set; }
            public double DimScale { get; set; }
            public double TextHeight { get; set; }
            public List<string> Log { get; } = new List<string>();
        }
    }
}
