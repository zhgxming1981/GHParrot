using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
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
    public class ExplodedView : GH_Component
    {
        private const string SettingsChunk = "ExplodedViewSettings";
        private const double ZeroTolerance = 1e-9;
        private readonly Dictionary<Guid, ExplodedObjectState> originalStates = new Dictionary<Guid, ExplodedObjectState>();
        private readonly List<PreviewItem> previewItems = new List<PreviewItem>();
        private readonly List<Guid> copiedObjectIds = new List<Guid>();
        private readonly List<GeometryBase> lastOutputGeometry = new List<GeometryBase>();
        private readonly List<Color> lastOutputColors = new List<Color>();
        private uint copiedDocSerialNumber;
        private bool lastInputRun;

        public bool CopyEntities { get; set; } = true;
        public bool ButtonRun { get; set; }

        public ExplodedView()
          : base("ExplodedView", "爆炸图",
              "按Guid生成GH爆炸图预览，不移动原Rhino对象",
              "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "要爆炸显示的Rhino对象Guid列表", GH_ParamAccess.list);
            pManager.AddVectorParameter("复制向量", "V", "先按此向量复制一份，再在复制后的基础上做爆炸图", GH_ParamAccess.item, Vector3d.Zero);
            pManager.AddNumberParameter("Dx", "Dx", "X方向爆炸距离；为0时不沿X方向爆炸", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Dy", "Dy", "Y方向爆炸距离；为0时不沿Y方向爆炸", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Dz", "Dz", "Z方向爆炸距离；为0时不沿Z方向爆炸", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Run", "Run", "触发计算", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("对象", "G", "爆炸后的GH几何对象", GH_ParamAccess.list);
            pManager.AddColourParameter("颜色", "C", "与对象对应的原Rhino显示颜色", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<GH_Guid> inputGuids = new List<GH_Guid>();
            Vector3d copyVector = Vector3d.Zero;
            double dx = 0.0;
            double dy = 0.0;
            double dz = 0.0;
            bool inputRun = false;

            if (!DA.GetDataList(0, inputGuids))
                return;
            DA.GetData(1, ref copyVector);
            if (!DA.GetData(2, ref dx))
                return;
            if (!DA.GetData(3, ref dy))
                return;
            if (!DA.GetData(4, ref dz))
                return;
            DA.GetData(5, ref inputRun);

            bool shouldRun = ButtonRun || (inputRun && !lastInputRun);
            lastInputRun = inputRun;
            ButtonRun = false;

            if (!shouldRun)
            {
                DA.SetDataList(0, lastOutputGeometry);
                DA.SetDataList(1, lastOutputColors);
                return;
            }

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "当前没有可用的Rhino文档。");
                return;
            }

            List<Guid> guids = inputGuids
                .Where(x => x != null && x.Value != Guid.Empty)
                .Select(x => x.Value)
                .Distinct()
                .ToList();

            previewItems.Clear();

            if (guids.Count == 0)
            {
                originalStates.Clear();
                ClearLastOutput();
                ClearCopiedObjects(doc);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "没有输入有效的Guid。");
                return;
            }

            HashSet<Guid> activeGuids = new HashSet<Guid>(guids);
            RemoveUnusedCache(activeGuids);

            List<Guid> existingGuids = new List<Guid>();
            int missingCount = 0;
            int invalidCount = 0;

            foreach (Guid id in guids)
            {
                RhinoObject obj = doc.Objects.FindId(id);
                if (obj?.Geometry == null)
                {
                    missingCount++;
                    continue;
                }

                BoundingBox box = obj.Geometry.GetBoundingBox(true);
                if (!box.IsValid)
                {
                    invalidCount++;
                    continue;
                }

                existingGuids.Add(id);
                if (!originalStates.ContainsKey(id))
                    originalStates[id] = CreateState(doc, obj, box.Center);
            }

            if (existingGuids.Count == 0)
            {
                ClearLastOutput();
                ClearCopiedObjects(doc);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "没有找到Guid对应的Rhino对象。");
                return;
            }

            Point3d explodeCenter = GetExplodeCenter(existingGuids);
            BoundingBox explodeBox = GetExplodeBox(existingGuids);
            Dictionary<Guid, Vector3d> separationVectors = GetSeparationVectors(existingGuids, dx, dy, dz);
            List<GeometryBase> outputGeometry = new List<GeometryBase>();
            List<Color> outputColors = new List<Color>();
            int failedCount = 0;

            ClearCopiedObjects(doc);

            foreach (Guid id in existingGuids)
            {
                ExplodedObjectState state = originalStates[id];
                Vector3d explosionVector = GetAxisExplosionVector(state.OriginalCenter, explodeCenter, explodeBox, dx, dy, dz);
                Vector3d separationVector = separationVectors.TryGetValue(id, out Vector3d separation) ? separation : Vector3d.Zero;
                Transform transform = Transform.Translation(copyVector + explosionVector + separationVector);

                foreach (ExplodedPart part in state.Parts)
                {
                    GeometryBase geometry = part.Geometry?.Duplicate();
                    if (geometry == null || !geometry.Transform(transform))
                    {
                        failedCount++;
                        continue;
                    }

                    outputGeometry.Add(geometry);
                    outputColors.Add(part.Color);
                    if (CopyEntities)
                    {
                        if (AddCopiedObject(doc, geometry, part.Attributes, out Guid copiedId))
                            copiedObjectIds.Add(copiedId);
                        else
                            failedCount++;
                    }
                    else
                    {
                        previewItems.Add(new PreviewItem(geometry, part.Color, part.Attributes));
                    }
                }
            }

            DA.SetDataList(0, outputGeometry);
            DA.SetDataList(1, outputColors);
            SetLastOutput(outputGeometry, outputColors);

            if (CopyEntities)
            {
                copiedDocSerialNumber = doc.RuntimeSerialNumber;
                if (copiedObjectIds.Count > 0)
                    doc.Views.Redraw();
            }

            if (missingCount > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "找不到Guid对应的Rhino对象：" + missingCount + " 个。");
            if (invalidCount > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "包围盒无效的对象：" + invalidCount + " 个。");
            if (failedCount > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "生成失败：" + failedCount + " 个对象。");
        }

        private void RemoveUnusedCache(HashSet<Guid> activeGuids)
        {
            List<Guid> removedGuids = originalStates.Keys
                .Where(id => !activeGuids.Contains(id))
                .ToList();

            foreach (Guid id in removedGuids)
                originalStates.Remove(id);
        }

        private void SetLastOutput(List<GeometryBase> geometry, List<Color> colors)
        {
            lastOutputGeometry.Clear();
            lastOutputColors.Clear();

            foreach (GeometryBase item in geometry)
            {
                GeometryBase duplicate = item?.Duplicate();
                if (duplicate != null)
                    lastOutputGeometry.Add(duplicate);
            }

            lastOutputColors.AddRange(colors);
        }

        private void ClearLastOutput()
        {
            lastOutputGeometry.Clear();
            lastOutputColors.Clear();
        }

        private void ClearCopiedObjects(RhinoDoc doc)
        {
            if (copiedObjectIds.Count == 0)
                return;

            if (doc != null && (copiedDocSerialNumber == 0 || copiedDocSerialNumber == doc.RuntimeSerialNumber))
            {
                foreach (Guid id in copiedObjectIds)
                {
                    RhinoObject obj = doc.Objects.FindId(id);
                    if (obj != null)
                        doc.Objects.Delete(id, true);
                }

                doc.Views.Redraw();
            }

            copiedObjectIds.Clear();
            copiedDocSerialNumber = 0;
        }

        private static bool AddCopiedObject(RhinoDoc doc, GeometryBase geometry, ObjectAttributes attributes, out Guid id)
        {
            id = Guid.Empty;
            if (doc == null || geometry == null)
                return false;

            GeometryBase duplicate = geometry.Duplicate();
            if (duplicate == null)
                return false;

            ObjectAttributes copiedAttributes = attributes?.Duplicate() ?? doc.CreateDefaultAttributes();
            copiedAttributes.ObjectId = Guid.NewGuid();
            int explosionLayerIndex = EnsureExplosionLayer(doc, copiedAttributes.LayerIndex);
            if (explosionLayerIndex >= 0)
                copiedAttributes.LayerIndex = explosionLayerIndex;

            id = doc.Objects.Add(duplicate, copiedAttributes);
            return id != Guid.Empty;
        }

        private static int EnsureExplosionLayer(RhinoDoc doc, int sourceLayerIndex)
        {
            if (doc == null)
                return -1;

            Layer sourceLayer = sourceLayerIndex >= 0 ? doc.Layers.FindIndex(sourceLayerIndex) : null;
            string childName = sourceLayer == null || string.IsNullOrWhiteSpace(sourceLayer.Name)
                ? "默认"
                : sourceLayer.Name.Trim();

            int parentIndex = doc.Layers.FindByFullPath("爆炸", -1);
            if (parentIndex < 0)
            {
                Layer parentLayer = new Layer
                {
                    Name = "爆炸",
                    Color = sourceLayer?.Color ?? Color.Black
                };
                parentIndex = doc.Layers.Add(parentLayer);
            }

            if (parentIndex < 0)
                return -1;

            int targetIndex = doc.Layers.FindByFullPath("爆炸::" + childName, -1);
            if (targetIndex >= 0)
                return targetIndex;

            Layer parent = doc.Layers.FindIndex(parentIndex);
            Layer childLayer = new Layer
            {
                Name = childName,
                ParentLayerId = parent?.Id ?? Guid.Empty,
                Color = sourceLayer?.Color ?? Color.Black
            };

            return doc.Layers.Add(childLayer);
        }

        private static ExplodedObjectState CreateState(RhinoDoc doc, RhinoObject obj, Point3d originalCenter)
        {
            List<ExplodedPart> parts = new List<ExplodedPart>();
            AddObjectParts(doc, obj, Transform.Identity, parts, new HashSet<Guid>());

            BoundingBox box = BoundingBox.Empty;
            foreach (ExplodedPart part in parts)
            {
                BoundingBox partBox = part.Geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;
                if (partBox.IsValid)
                    box.Union(partBox);
            }

            if (!box.IsValid)
                box = obj.Geometry.GetBoundingBox(true);

            return new ExplodedObjectState(parts, originalCenter, box);
        }

        private static void AddObjectParts(RhinoDoc doc, RhinoObject obj, Transform transform, List<ExplodedPart> parts, HashSet<Guid> visitedDefinitions)
        {
            if (obj?.Geometry == null)
                return;

            ObjectAttributes attributes = obj.Attributes?.Duplicate() ?? doc.CreateDefaultAttributes();
            Color color = GetObjectDisplayColor(doc, obj);

            if (obj is InstanceObject instanceObject && instanceObject.InstanceDefinition != null)
            {
                AddDefinitionParts(doc, instanceObject.InstanceDefinition, transform * instanceObject.InstanceXform, parts, visitedDefinitions);
                return;
            }

            AddGeometryPart(doc, obj.Geometry, attributes, color, transform, parts, visitedDefinitions);
        }

        private static void AddDefinitionParts(RhinoDoc doc, InstanceDefinition definition, Transform transform, List<ExplodedPart> parts, HashSet<Guid> visitedDefinitions)
        {
            if (definition == null || !visitedDefinitions.Add(definition.Id))
                return;

            foreach (RhinoObject child in definition.GetObjects())
                AddObjectParts(doc, child, transform, parts, visitedDefinitions);

            visitedDefinitions.Remove(definition.Id);
        }

        private static void AddGeometryPart(RhinoDoc doc, GeometryBase geometry, ObjectAttributes attributes, Color color, Transform transform, List<ExplodedPart> parts, HashSet<Guid> visitedDefinitions)
        {
            if (geometry == null)
                return;

            if (geometry is InstanceReferenceGeometry instanceReference)
            {
                InstanceDefinition nestedDefinition = doc.InstanceDefinitions.FindId(instanceReference.ParentIdefId);
                AddDefinitionParts(doc, nestedDefinition, transform * instanceReference.Xform, parts, visitedDefinitions);
                return;
            }

            GeometryBase duplicate = geometry.Duplicate();
            if (duplicate == null || !duplicate.Transform(transform))
                return;

            parts.Add(new ExplodedPart(duplicate, attributes?.Duplicate() ?? new ObjectAttributes(), color));
        }

        private Point3d GetExplodeCenter(List<Guid> guids)
        {
            BoundingBox allBox = BoundingBox.Empty;
            foreach (Guid id in guids)
            {
                if (originalStates.TryGetValue(id, out ExplodedObjectState state))
                    allBox.Union(state.OriginalCenter);
            }

            return allBox.IsValid ? allBox.Center : Point3d.Origin;
        }

        private BoundingBox GetExplodeBox(List<Guid> guids)
        {
            BoundingBox allBox = BoundingBox.Empty;
            foreach (Guid id in guids)
            {
                if (originalStates.TryGetValue(id, out ExplodedObjectState state) && state.OriginalBox.IsValid)
                    allBox.Union(state.OriginalBox);
            }

            return allBox;
        }

        private static Vector3d GetAxisExplosionVector(Point3d objectCenter, Point3d explodeCenter, BoundingBox explodeBox, double dx, double dy, double dz)
        {
            Vector3d offset = objectCenter - explodeCenter;
            Vector3d range = explodeBox.IsValid ? explodeBox.Diagonal : Vector3d.Zero;

            return new Vector3d(
                GetAxisRatio(offset.X, range.X) * dx,
                GetAxisRatio(offset.Y, range.Y) * dy,
                GetAxisRatio(offset.Z, range.Z) * dz);
        }

        private static double GetAxisRatio(double offset, double range)
        {
            if (Math.Abs(range) < 1e-9)
                return 0.0;

            return 2.0 * offset / range;
        }

        private Dictionary<Guid, Vector3d> GetSeparationVectors(List<Guid> guids, double dx, double dy, double dz)
        {
            Dictionary<Guid, Vector3d> result = new Dictionary<Guid, Vector3d>();
            foreach (Guid id in guids)
                result[id] = Vector3d.Zero;

            bool useX = Math.Abs(dx) > ZeroTolerance;
            bool useY = Math.Abs(dy) > ZeroTolerance;
            bool useZ = Math.Abs(dz) > ZeroTolerance;
            if (!useX && !useY && !useZ)
                return result;

            double controlDistance = (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz)) / 3.0;
            if (controlDistance < ZeroTolerance)
                return result;

            double modelScale = GetModelScale(guids);
            double fallbackThreshold = modelScale * 0.015;

            for (int i = 0; i < guids.Count; i++)
            {
                if (!originalStates.TryGetValue(guids[i], out ExplodedObjectState stateA))
                    continue;

                for (int j = i + 1; j < guids.Count; j++)
                {
                    if (!originalStates.TryGetValue(guids[j], out ExplodedObjectState stateB))
                        continue;

                    double boxDistance = GetBoundingBoxDistance(stateA.OriginalBox, stateB.OriginalBox);
                    double pairScale = Math.Max(1e-9, Math.Min(GetBoxDiagonal(stateA.OriginalBox), GetBoxDiagonal(stateB.OriginalBox)));
                    double threshold = Math.Max(fallbackThreshold, pairScale * 0.05);
                    if (boxDistance > threshold)
                        continue;

                    Vector3d away = stateA.OriginalCenter - stateB.OriginalCenter;
                    away = new Vector3d(
                        useX ? away.X : 0.0,
                        useY ? away.Y : 0.0,
                        useZ ? away.Z : 0.0);
                    if (!away.Unitize())
                        away = GetFallbackSeparationDirection(dx, dy, dz, i, j);

                    double weight = 1.0 - Math.Min(1.0, boxDistance / threshold);
                    double amount = controlDistance * 0.15 * weight;
                    result[guids[i]] += away * amount;
                    result[guids[j]] -= away * amount;
                }
            }

            return result;
        }

        private static Vector3d GetFallbackSeparationDirection(double dx, double dy, double dz, int i, int j)
        {
            double ax = Math.Abs(dx);
            double ay = Math.Abs(dy);
            double az = Math.Abs(dz);
            double sign = i <= j ? -1.0 : 1.0;

            if (az >= ax && az >= ay && az > ZeroTolerance)
                return new Vector3d(0.0, 0.0, sign * Math.Sign(dz));

            if (ax >= ay && ax > ZeroTolerance)
                return new Vector3d(sign * Math.Sign(dx), 0.0, 0.0);

            if (ay > ZeroTolerance)
                return new Vector3d(0.0, sign * Math.Sign(dy), 0.0);

            return Vector3d.Zero;
        }

        private double GetModelScale(List<Guid> guids)
        {
            BoundingBox box = GetExplodeBox(guids);
            double diagonal = GetBoxDiagonal(box);
            return diagonal > 1e-9 ? diagonal : 1.0;
        }

        private static double GetBoundingBoxDistance(BoundingBox a, BoundingBox b)
        {
            if (!a.IsValid || !b.IsValid)
                return double.MaxValue;

            double gapX = Math.Max(0.0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
            double gapY = Math.Max(0.0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
            double gapZ = Math.Max(0.0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
            return Math.Sqrt(gapX * gapX + gapY * gapY + gapZ * gapZ);
        }

        private static double GetBoxDiagonal(BoundingBox box)
        {
            if (!box.IsValid)
                return 0.0;

            return box.Diagonal.Length;
        }

        private static Color GetObjectDisplayColor(RhinoDoc doc, RhinoObject obj)
        {
            ObjectAttributes attributes = obj.Attributes;
            if (attributes == null)
                return Color.LightGray;

            if (attributes.ColorSource == ObjectColorSource.ColorFromObject)
                return attributes.ObjectColor;

            if (attributes.ColorSource == ObjectColorSource.ColorFromMaterial)
            {
                Material material = doc.Materials.FindIndex(attributes.MaterialIndex);
                if (material != null)
                    return material.DiffuseColor;
            }

            Layer layer = doc.Layers.FindIndex(attributes.LayerIndex);
            if (layer != null)
                return layer.Color;

            return attributes.ObjectColor.IsEmpty ? Color.LightGray : attributes.ObjectColor;
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox box = base.ClippingBox;
                foreach (PreviewItem item in previewItems)
                {
                    BoundingBox itemBox = item.Geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;
                    if (itemBox.IsValid)
                        box.Union(itemBox);
                }

                return box;
            }
        }

        public override bool IsPreviewCapable
        {
            get { return true; }
        }

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter chunk = writer.CreateChunk(SettingsChunk);
            chunk.SetBoolean(nameof(CopyEntities), CopyEntities);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            if (reader.ChunkExists(SettingsChunk))
            {
                GH_IReader chunk = reader.FindChunk(SettingsChunk);
                bool copyEntities = true;
                if (chunk.TryGetBoolean(nameof(CopyEntities), ref copyEntities))
                    CopyEntities = copyEntities;
            }

            return base.Read(reader);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (Hidden || Locked)
                return;

            foreach (PreviewItem item in previewItems)
                DrawGeometryWires(args, item.Geometry, GetPreviewColor(args, item.Color), 1);
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);

            if (Hidden || Locked)
                return;

            foreach (PreviewItem item in previewItems)
                DrawGeometryMeshes(args, item.Geometry, item.Color);
        }

        private Color GetPreviewColor(IGH_PreviewArgs args, Color color)
        {
            if (Attributes?.Selected == true)
                return args.WireColour_Selected;

            return color.IsEmpty ? args.WireColour : color;
        }

        private static void DrawGeometryWires(IGH_PreviewArgs args, GeometryBase geometry, Color color, int thickness)
        {
            if (geometry is Brep brep)
                args.Display.DrawBrepWires(brep, color, thickness);
            else if (geometry is Curve curve)
                args.Display.DrawCurve(curve, color, thickness);
            else if (geometry is Mesh mesh)
                args.Display.DrawMeshWires(mesh, color);
            else if (geometry is Rhino.Geometry.Point point)
                args.Display.DrawPoint(point.Location, color);
            else if (geometry is PointCloud cloud)
            {
                foreach (PointCloudItem item in cloud)
                    args.Display.DrawPoint(item.Location, color);
            }
        }

        private static void DrawGeometryMeshes(IGH_PreviewArgs args, GeometryBase geometry, Color color)
        {
            Color shadedColor = Color.FromArgb(110, color.R, color.G, color.B);
            DisplayMaterial material = new DisplayMaterial(shadedColor);

            if (geometry is Brep brep)
                args.Display.DrawBrepShaded(brep, material);
            else if (geometry is Mesh mesh)
                args.Display.DrawMeshShaded(mesh, material);
            else if (geometry is Extrusion extrusion)
            {
                Brep brepForm = extrusion.ToBrep();
                if (brepForm != null)
                    args.Display.DrawBrepShaded(brepForm, material);
            }
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_MoveForTekla"); }
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_ExplodedView(this);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            ClearCopiedObjects(RhinoDoc.ActiveDoc);
            base.RemovedFromDocument(document);
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("94E37D58-2E6D-4D24-9163-35547CE98243"); }
        }

        private class ExplodedObjectState
        {
            public ExplodedObjectState(List<ExplodedPart> parts, Point3d originalCenter, BoundingBox originalBox)
            {
                Parts = parts ?? new List<ExplodedPart>();
                OriginalCenter = originalCenter;
                OriginalBox = originalBox;
            }

            public List<ExplodedPart> Parts { get; }
            public Point3d OriginalCenter { get; }
            public BoundingBox OriginalBox { get; }
        }

        private class ExplodedPart
        {
            public ExplodedPart(GeometryBase geometry, ObjectAttributes attributes, Color color)
            {
                Geometry = geometry;
                Attributes = attributes;
                Color = color;
            }

            public GeometryBase Geometry { get; }
            public ObjectAttributes Attributes { get; }
            public Color Color { get; }
        }

        private class PreviewItem
        {
            public PreviewItem(GeometryBase geometry, Color color, ObjectAttributes attributes)
            {
                Geometry = geometry;
                Color = color;
                Attributes = attributes;
            }

            public GeometryBase Geometry { get; }
            public Color Color { get; }
            public ObjectAttributes Attributes { get; }
        }

    }

    internal class CButton_ExplodedView : GH_ComponentAttributes
    {
        private const float ButtonHeight = 20.0f;

        public CButton_ExplodedView(ExplodedView component) : base(component) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + ButtonHeight * 2.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            ExplodedView owner = (ExplodedView)Owner;
            RectangleF buttonRect = GetButtonRect();
            GH_Palette palette = owner.CopyEntities ? GH_Palette.Black : GH_Palette.Grey;

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(owner.CopyEntities ? "复制实体" : "不复制实体", font, Brushes.White, buttonRect, format);

                RectangleF runButtonRect = GetRunButtonRect();
                using (GH_Capsule capsule = GH_Capsule.CreateCapsule(runButtonRect, GH_Palette.Black))
                    capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

                graphics.DrawString("Run", font, Brushes.White, runButtonRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && GetButtonRect().Contains(e.CanvasLocation))
            {
                ExplodedView owner = (ExplodedView)Owner;
                owner.CopyEntities = !owner.CopyEntities;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            if (e.Button == MouseButtons.Left && GetRunButtonRect().Contains(e.CanvasLocation))
            {
                ExplodedView owner = (ExplodedView)Owner;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }

        private RectangleF GetButtonRect()
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - ButtonHeight * 2.0f, Bounds.Width, ButtonHeight);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }

        private RectangleF GetRunButtonRect()
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - ButtonHeight, Bounds.Width, ButtonHeight);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }
    }
}
