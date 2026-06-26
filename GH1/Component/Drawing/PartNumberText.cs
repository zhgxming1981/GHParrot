using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class PartNumberText : GH_Component
    {
        private const double ZeroTolerance = 1e-9;
        private bool _lastBake;

        internal bool ButtonBake { get; set; }
        internal bool ButtonPressed { get; set; }

        public PartNumberText()
          : base("Part Number Text", "零件编号",
              "生成零件编号文字，合并重叠编号，并避开零件投影线。",
              "Parrot", "Drawing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("零件", "G", "要编号的零件列表。", GH_ParamAccess.list);
            pManager.AddTextParameter("编号", "N", "零件编号列表，数量必须与零件数量一致。", GH_ParamAccess.list);
            pManager.AddNumberParameter("文字大小", "H", "编号文字高度。", GH_ParamAccess.item, 50.0);
            pManager.AddTextParameter("图层", "L", "Bake 到 Rhino 的目标图层；为空时使用当前图层。", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Bake", "B", "由 False 变为 True 时执行一次 Bake；保持 True 不会重复 Bake。", GH_ParamAccess.item, false);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("编号文字", "T", "合并重叠编号后的可预览、可 Bake 文字。", GH_ParamAccess.list);
            pManager.AddTextParameter("合并编号", "N", "每个输出文字对应的合并编号。", GH_ParamAccess.list);
            pManager.AddPointParameter("文字位置", "P", "每个输出文字的放置位置。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<IGH_GeometricGoo> geometry = new List<IGH_GeometricGoo>();
            List<string> numbers = new List<string>();
            double textHeight = 50.0;
            string layerName = string.Empty;
            bool bake = false;

            DA.GetDataList(0, geometry);
            DA.GetDataList(1, numbers);
            DA.GetData(2, ref textHeight);
            DA.GetData(3, ref layerName);
            DA.GetData(4, ref bake);

            if (geometry.Count != numbers.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "零件数量与编号数量不一致，已停止计算。");
                return;
            }

            if (textHeight <= ZeroTolerance)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "文字大小必须大于 0。");
                return;
            }

            List<LabelInfo> labels = new List<LabelInfo>();
            List<List<Curve>> projectedGeometry = geometry
                .Select(GetProjectedCurves)
                .ToList();
            for (int i = 0; i < geometry.Count; i++)
            {
                BoundingBox box = geometry[i]?.Boundingbox ?? BoundingBox.Empty;
                if (!box.IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "零件 " + i + " 的包围盒无效，已跳过。");
                    continue;
                }

                Point3d center = GetCentroid(geometry[i], box);
                center.Z = 0.0;

                Vector3d textXAxis = GetLongestHorizontalAxis(box);
                Plane textPlane = CreateTextPlane(center, textXAxis);

                string number = numbers[i] ?? string.Empty;
                Text3d text = CreateText(number, textPlane, textHeight);
                labels.Add(new LabelInfo(i, number, center, textXAxis, box, text.BoundingBox));
                text.Dispose();
            }

            List<List<int>> groups = FindOverlapGroups(labels);
            List<Text3dGoo> outputText = new List<Text3dGoo>();
            List<string> outputNumbers = new List<string>();
            List<Point3d> outputPoints = new List<Point3d>();
            List<BoundingBox> placedTextBoxes = new List<BoundingBox>();
            BoundingBox allPartsBox = BoundingBox.Empty;
            foreach (LabelInfo label in labels)
                allPartsBox.Union(label.ObjectBox);

            foreach (List<int> group in groups)
            {
                List<LabelInfo> members = group.Select(index => labels[index]).ToList();
                string mergedNumber = string.Join("，", members
                    .OrderBy(item => item.InputIndex)
                    .Select(item => item.Number));

                Point3d position = new Point3d(
                    members.Average(item => item.Position.X),
                    members.Average(item => item.Position.Y),
                    0.0);
                Vector3d xAxis = GetGroupTextAxis(members);

                Vector3d moveDirection = GetMoveDirection(
                    position, xAxis, allPartsBox);
                position = MoveTextOutsideMemberParts(
                    mergedNumber,
                    position,
                    xAxis,
                    moveDirection,
                    projectedGeometry,
                    placedTextBoxes,
                    textHeight);

                Plane plane = CreateTextPlane(position, xAxis);
                Text3d text = CreateText(mergedNumber, plane, textHeight);
                placedTextBoxes.Add(text.BoundingBox);
                outputText.Add(new Text3dGoo(text));
                outputNumbers.Add(mergedNumber);
                outputPoints.Add(position);
            }

            DA.SetDataList(0, outputText);
            DA.SetDataList(1, outputNumbers);
            DA.SetDataList(2, outputPoints);

            bool shouldBake = ButtonBake || (bake && !_lastBake);
            ButtonBake = false;
            _lastBake = bake;
            if (shouldBake)
                BakeTexts(outputText, layerName);
        }

        private static Text3d CreateText(string value, Plane plane, double height)
        {
            return new Text3d(value ?? string.Empty, plane, height)
            {
                HorizontalAlignment = Rhino.DocObjects.TextHorizontalAlignment.Center,
                VerticalAlignment = Rhino.DocObjects.TextVerticalAlignment.Middle,
                FontFace = "宋体"
            };
        }

        private void BakeTexts(List<Text3dGoo> texts, string layerName)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bake 失败：当前没有可用的 Rhino 文档。");
                return;
            }

            int layerIndex = ResolveLayer(doc, layerName);
            if (!string.IsNullOrWhiteSpace(layerName) && layerIndex < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bake 失败：无法找到或创建图层“" + layerName.Trim() + "”。");
                return;
            }

            int bakedCount = 0;
            foreach (Text3dGoo text in texts)
            {
                if (text?.Value == null)
                    continue;

                ObjectAttributes attributes = doc.CreateDefaultAttributes();
                if (layerIndex >= 0)
                    attributes.LayerIndex = layerIndex;

                Guid id = doc.Objects.AddText(text.Value, attributes);
                if (id != Guid.Empty)
                    bakedCount++;
            }

            doc.Views.Redraw();
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "已 Bake " + bakedCount + " 个零件编号文字。");
        }

        private static int ResolveLayer(RhinoDoc doc, string layerName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(layerName))
                return -1;

            string name = layerName.Trim();
            int index = doc.Layers.FindByFullPath(name, RhinoMath.UnsetIntIndex);
            if (index >= 0)
                return index;

            index = doc.Layers.FindName(name)?.Index ?? -1;
            if (index >= 0)
                return index;

            return doc.Layers.Add(name, Color.Black);
        }

        public override void CreateAttributes()
        {
            Attributes = new PartNumberTextAttributes(this);
        }

        private static Plane CreateTextPlane(Point3d origin, Vector3d xAxis)
        {
            xAxis.Z = 0.0;
            if (!xAxis.Unitize())
                xAxis = Vector3d.XAxis;

            Vector3d yAxis = Vector3d.CrossProduct(Vector3d.ZAxis, xAxis);
            if (!yAxis.Unitize())
                yAxis = Vector3d.YAxis;

            return new Plane(origin, xAxis, yAxis);
        }

        private static Vector3d GetLongestHorizontalAxis(BoundingBox box)
        {
            Vector3d diagonal = box.Diagonal;
            return Math.Abs(diagonal.X) >= Math.Abs(diagonal.Y)
                ? Vector3d.XAxis
                : Vector3d.YAxis;
        }

        private static Vector3d GetGroupTextAxis(List<LabelInfo> members)
        {
            BoundingBox box = BoundingBox.Empty;
            foreach (LabelInfo member in members)
                box.Union(member.ObjectBox);
            return GetLongestHorizontalAxis(box);
        }

        private static Vector3d GetMoveDirection(
            Point3d position,
            Vector3d textXAxis,
            BoundingBox allPartsBox)
        {
            bool horizontalText =
                Math.Abs(textXAxis * Vector3d.XAxis) >=
                Math.Abs(textXAxis * Vector3d.YAxis);

            Point3d referenceCenter = allPartsBox.IsValid
                ? allPartsBox.Center
                : Point3d.Origin;

            if (horizontalText)
            {
                return position.Y >= referenceCenter.Y
                    ? Vector3d.YAxis
                    : -Vector3d.YAxis;
            }

            return position.X <= referenceCenter.X
                ? -Vector3d.XAxis
                : Vector3d.XAxis;
        }

        private static Point3d MoveTextOutsideMemberParts(
            string value,
            Point3d startPosition,
            Vector3d textXAxis,
            Vector3d moveDirection,
            List<List<Curve>> projectedGeometry,
            List<BoundingBox> placedTextBoxes,
            double textHeight)
        {
            Point3d position = startPosition;
            double gap = Math.Max(textHeight * 0.2, 1.0);

            for (int attempt = 0; attempt < 100; attempt++)
            {
                BoundingBox textBox;
                using (Text3d text = CreateText(
                    value,
                    CreateTextPlane(position, textXAxis),
                    textHeight))
                {
                    textBox = text.BoundingBox;
                }

                double distance = 0.0;

                for (int objectIndex = 0; objectIndex < projectedGeometry.Count; objectIndex++)
                {
                    foreach (Curve curve in projectedGeometry[objectIndex])
                    {
                        if (!CurveIntersectsBoxInWorldXY(curve, textBox))
                            continue;

                        distance = Math.Max(
                            distance,
                            GetRequiredMoveDistance(
                                textBox,
                                curve.GetBoundingBox(true),
                                moveDirection,
                                gap));
                    }
                }

                foreach (BoundingBox placedBox in placedTextBoxes)
                {
                    if (BoxesOverlapInWorldXY(textBox, placedBox))
                    {
                        distance = Math.Max(
                            distance,
                            GetRequiredMoveDistance(
                                textBox,
                                placedBox,
                                moveDirection,
                                gap));
                    }
                }

                if (distance <= ZeroTolerance)
                    return position;

                position += moveDirection * distance;
                position.Z = 0.0;
            }

            return position;
        }

        private static List<Curve> GetProjectedCurves(IGH_GeometricGoo goo)
        {
            List<Curve> result = new List<Curve>();
            if (goo == null || !goo.CastTo(out GeometryBase geometry) || geometry == null)
                return result;

            if (geometry is Brep brep)
            {
                foreach (BrepEdge edge in brep.Edges)
                    AddProjectedCurve(result, edge);
                return result;
            }

            if (geometry is Mesh mesh)
            {
                Polyline[] outlines = mesh.GetOutlines(Plane.WorldXY);
                if (outlines != null)
                {
                    foreach (Polyline outline in outlines)
                        AddProjectedCurve(result, new PolylineCurve(outline));
                }
                return result;
            }

            if (geometry is Surface surface)
            {
                Brep surfaceBrep = surface.ToBrep();
                if (surfaceBrep != null)
                {
                    foreach (BrepEdge edge in surfaceBrep.Edges)
                        AddProjectedCurve(result, edge);
                }
                return result;
            }

            if (geometry is Curve curve)
                AddProjectedCurve(result, curve);

            return result;
        }

        private static void AddProjectedCurve(List<Curve> result, Curve source)
        {
            Curve projected = Curve.ProjectToPlane(source, Plane.WorldXY);
            if (projected != null && projected.IsValid)
                result.Add(projected);
        }

        private static bool CurveIntersectsBoxInWorldXY(Curve curve, BoundingBox box)
        {
            if (curve == null || !curve.IsValid || !box.IsValid)
                return false;

            BoundingBox curveBox = curve.GetBoundingBox(true);
            if (!BoxesOverlapInWorldXY(curveBox, box))
                return false;

            Rectangle3d rectangle = new Rectangle3d(
                Plane.WorldXY,
                new Interval(box.Min.X, box.Max.X),
                new Interval(box.Min.Y, box.Max.Y));
            Curve boundary = rectangle.ToNurbsCurve();
            Rhino.Geometry.Intersect.CurveIntersections intersections =
                Rhino.Geometry.Intersect.Intersection.CurveCurve(
                    curve,
                    boundary,
                    0.001,
                    0.001);
            if (intersections != null && intersections.Count > 0)
                return true;

            PointContainment containment = curve.Contains(
                new Point3d(box.Center.X, box.Center.Y, 0.0),
                Plane.WorldXY,
                0.001);
            return containment == PointContainment.Inside ||
                   containment == PointContainment.Coincident;
        }

        private static double GetRequiredMoveDistance(
            BoundingBox movingBox,
            BoundingBox obstacleBox,
            Vector3d direction,
            double gap)
        {
            if (!movingBox.IsValid || !obstacleBox.IsValid ||
                !BoxesOverlapInWorldXY(movingBox, obstacleBox))
                return 0.0;

            if (direction.X > 0.5)
                return obstacleBox.Max.X + gap - movingBox.Min.X;
            if (direction.X < -0.5)
                return movingBox.Max.X - obstacleBox.Min.X + gap;
            if (direction.Y > 0.5)
                return obstacleBox.Max.Y + gap - movingBox.Min.Y;

            return movingBox.Max.Y - obstacleBox.Min.Y + gap;
        }

        private static Point3d GetCentroid(IGH_GeometricGoo goo, BoundingBox fallbackBox)
        {
            if (goo == null || !goo.CastTo(out GeometryBase geometry) || geometry == null)
                return fallbackBox.Center;

            try
            {
                if (geometry is Brep brep)
                {
                    if (brep.IsSolid)
                    {
                        using (VolumeMassProperties volume = VolumeMassProperties.Compute(brep))
                        {
                            if (volume != null)
                                return volume.Centroid;
                        }
                    }

                    using (AreaMassProperties area = AreaMassProperties.Compute(brep))
                    {
                        if (area != null)
                            return area.Centroid;
                    }
                }
                else if (geometry is Mesh mesh)
                {
                    if (mesh.IsClosed)
                    {
                        using (VolumeMassProperties volume = VolumeMassProperties.Compute(mesh))
                        {
                            if (volume != null)
                                return volume.Centroid;
                        }
                    }

                    using (AreaMassProperties area = AreaMassProperties.Compute(mesh))
                    {
                        if (area != null)
                            return area.Centroid;
                    }
                }
                else if (geometry is Surface surface)
                {
                    using (AreaMassProperties area = AreaMassProperties.Compute(surface))
                    {
                        if (area != null)
                            return area.Centroid;
                    }
                }
                else if (geometry is Curve curve)
                {
                    if (curve.IsClosed && curve.IsPlanar())
                    {
                        using (AreaMassProperties area = AreaMassProperties.Compute(curve))
                        {
                            if (area != null)
                                return area.Centroid;
                        }
                    }

                    using (LengthMassProperties length = LengthMassProperties.Compute(curve))
                    {
                        if (length != null)
                            return length.Centroid;
                    }
                }
                else if (geometry is Rhino.Geometry.Point point)
                {
                    return point.Location;
                }
            }
            catch
            {
            }

            return fallbackBox.Center;
        }

        private static List<List<int>> FindOverlapGroups(List<LabelInfo> labels)
        {
            int count = labels.Count;
            int[] parent = Enumerable.Range(0, count).ToArray();

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (BoxesOverlapInWorldXY(labels[i].TextBox, labels[j].TextBox))
                        Union(parent, i, j);
                }
            }

            Dictionary<int, List<int>> groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < count; i++)
            {
                int root = Find(parent, i);
                if (!groups.TryGetValue(root, out List<int> group))
                {
                    group = new List<int>();
                    groups.Add(root, group);
                }
                group.Add(i);
            }

            return groups.Values
                .OrderBy(group => group.Min(index => labels[index].InputIndex))
                .ToList();
        }

        private static bool BoxesOverlapInWorldXY(BoundingBox a, BoundingBox b)
        {
            if (!a.IsValid || !b.IsValid)
                return false;

            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
                   a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;
        }

        private static int Find(int[] parent, int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }
            return value;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int rootA = Find(parent, a);
            int rootB = Find(parent, b);
            if (rootA != rootB)
                parent[rootB] = rootA;
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_CreateTextDot"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("6257E315-01E6-4EA2-A39A-B5BE9D8BA66B"); }
        }

        private sealed class LabelInfo
        {
            public LabelInfo(int inputIndex, string number, Point3d position,
                Vector3d textXAxis, BoundingBox objectBox, BoundingBox textBox)
            {
                InputIndex = inputIndex;
                Number = number;
                Position = position;
                TextXAxis = textXAxis;
                ObjectBox = objectBox;
                TextBox = textBox;
            }

            public int InputIndex { get; }
            public string Number { get; }
            public Point3d Position { get; }
            public Vector3d TextXAxis { get; }
            public BoundingBox ObjectBox { get; }
            public BoundingBox TextBox { get; }
        }
    }

    internal sealed class PartNumberTextAttributes : GH_ComponentAttributes
    {
        private const float ButtonHeight = 20.0f;

        public PartNumberTextAttributes(PartNumberText owner) : base(owner)
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

            RectangleF button = GetButtonBounds();
            PartNumberText owner = (PartNumberText)Owner;
            GH_Palette palette = owner.ButtonPressed ? GH_Palette.Grey : GH_Palette.Black;
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(button, palette))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
                graphics.DrawString("Bake", font, Brushes.White, button, format);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != MouseButtons.Left || !GetButtonBounds().Contains(e.CanvasLocation))
                return GH_ObjectResponse.Ignore;

            PartNumberText owner = (PartNumberText)Owner;
            owner.ButtonPressed = true;
            owner.ButtonBake = true;
            owner.ExpireSolution(true);
            CMath.Delay(50);
            owner.ButtonPressed = false;
            owner.ExpireSolution(true);
            return GH_ObjectResponse.Handled;
        }

        private RectangleF GetButtonBounds()
        {
            RectangleF button = new RectangleF(Bounds.X, Bounds.Bottom - ButtonHeight, Bounds.Width, ButtonHeight);
            button.Inflate(-5.0f, -2.0f);
            return button;
        }
    }
}
