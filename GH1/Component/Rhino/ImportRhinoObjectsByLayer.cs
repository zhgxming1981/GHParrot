using CommonFunction;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class ImportRhinoObjectsByLayer : GH_Component
    {
        public enum ButtonColor { Black, Grey }

        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        internal bool ButtonRun { get; set; }

        private bool _lastInputRun;
        private readonly List<Guid> _lastResult = new List<Guid>();

        public ImportRhinoObjectsByLayer()
          : base("按图层导入Rhino对象", "ImportByLayer",
              "从未打开的Rhino文件中导入指定图层上的顶层对象到当前Rhino文档",
              "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件路径", "File", "Rhino文件路径", GH_ParamAccess.item);
            pManager.AddTextParameter("图层名", "Layer", "要导入的图层名，支持完整图层路径", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Run", "Run", "执行导入", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("实体", "Obj", "导入到当前Rhino文档中的Rhino对象", GH_ParamAccess.list);
            pManager.AddGenericParameter("引用", "Ref", "导入对象的Grasshopper引用对象", GH_ParamAccess.list);
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_ImportRhinoObjectsByLayer(this);
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox box = base.ClippingBox;
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null || _lastResult.Count == 0)
                    return box;

                foreach (Guid id in _lastResult)
                {
                    RhinoObject obj = doc.Objects.FindId(id);
                    BoundingBox objectBox = obj?.Geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;
                    if (objectBox.IsValid)
                        box.Union(objectBox);
                }

                return box;
            }
        }

        public override bool IsPreviewCapable
        {
            get { return true; }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (Hidden || Locked || Attributes?.Selected != true || _lastResult.Count == 0)
                return;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
                return;

            foreach (Guid id in _lastResult)
            {
                RhinoObject obj = doc.Objects.FindId(id);
                if (obj != null)
                    DrawImportedObjectPreview(args, doc, obj, args.WireColour_Selected);
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string filePath = "";
            List<string> layerNames = new List<string>();
            bool inputRun = false;

            if (!DA.GetData(0, ref filePath)) { return; }
            if (!DA.GetDataList(1, layerNames)) { return; }
            DA.GetData(2, ref inputRun);

            bool shouldRun = ButtonRun || (inputRun && !_lastInputRun);
            _lastInputRun = inputRun;
            ButtonRun = false;

            if (!shouldRun)
            {
                DA.SetDataList(0, GetOutputObjects());
                DA.SetDataList(1, GetOutputReferences());
                return;
            }

            try
            {
                List<Guid> result = ImportObjects(filePath, layerNames);
                _lastResult.Clear();
                _lastResult.AddRange(result);
                DA.SetDataList(0, GetOutputObjects());
                DA.SetDataList(1, GetOutputReferences());
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetDataList(0, GetOutputObjects());
                DA.SetDataList(1, GetOutputReferences());
            }
        }

        private List<object> GetOutputObjects()
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            List<object> output = new List<object>();

            foreach (Guid id in _lastResult)
            {
                RhinoObject obj = doc?.Objects.FindId(id);
                output.Add(obj ?? (object)new GH_Guid(id));
            }

            return output;
        }

        private List<object> GetOutputReferences()
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            List<object> output = new List<object>();

            foreach (Guid id in _lastResult)
            {
                RhinoObject obj = doc?.Objects.FindId(id);
                output.Add(CreateReferenceGoo(obj, id));
            }

            return output;
        }

        private static object CreateReferenceGoo(RhinoObject obj, Guid id)
        {
            if (obj?.Geometry == null || id == Guid.Empty)
                return new GH_Guid(id);

            GeometryBase geometry = obj.Geometry;
            if (geometry is Curve && TryCreateReferenceGoo(typeof(GH_Curve), id, out IGH_Goo curveGoo))
                return curveGoo;

            if ((geometry is Brep || geometry is Surface || geometry is Extrusion) &&
                TryCreateReferenceGoo(typeof(GH_Brep), id, out IGH_Goo brepGoo))
                return brepGoo;

            if (geometry is Mesh && TryCreateReferenceGoo(typeof(GH_Mesh), id, out IGH_Goo meshGoo))
                return meshGoo;

            if (geometry is Rhino.Geometry.Point && TryCreateReferenceGoo(typeof(GH_Point), id, out IGH_Goo pointGoo))
                return pointGoo;

            if (geometry is PointCloud && TryCreateReferenceGoo(typeof(GH_PointCloud), id, out IGH_Goo cloudGoo))
                return cloudGoo;

            return new GH_Guid(id);
        }

        private static bool TryCreateReferenceGoo(Type gooType, Guid id, out IGH_Goo goo)
        {
            goo = null;
            try
            {
                goo = Activator.CreateInstance(gooType, id) as IGH_Goo;
                return goo != null;
            }
            catch
            {
                return false;
            }
        }

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter resultChunk = writer.CreateChunk("LastResult");
            resultChunk.SetInt32("Count", _lastResult.Count);
            for (int i = 0; i < _lastResult.Count; i++)
            {
                GH_IWriter itemChunk = resultChunk.CreateChunk("Item", i);
                itemChunk.SetString("Guid", _lastResult[i].ToString());
            }

            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            _lastResult.Clear();

            GH_IReader resultChunk = reader.FindChunk("LastResult");
            if (resultChunk != null)
            {
                int count = 0;
                resultChunk.TryGetInt32("Count", ref count);
                for (int i = 0; i < count; i++)
                {
                    GH_IReader itemChunk = resultChunk.FindChunk("Item", i);
                    string value = "";
                    if (itemChunk != null && itemChunk.TryGetString("Guid", ref value) && Guid.TryParse(value, out Guid guid))
                        _lastResult.Add(guid);
                }
            }

            return base.Read(reader);
        }

        private List<Guid> ImportObjects(string filePath, List<string> layerNames)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Rhino文件路径为空。");
            if (!File.Exists(filePath))
                throw new FileNotFoundException("找不到Rhino文件。", filePath);
            if (layerNames == null || layerNames.All(string.IsNullOrWhiteSpace))
                throw new ArgumentException("图层名为空。");

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
                throw new InvalidOperationException("当前没有可用的Rhino文档。");

            File3dm file = File3dm.Read(filePath);
            if (file == null)
                throw new InvalidOperationException("无法读取Rhino文件。");

            HashSet<int> sourceLayerIndices = FindSourceLayerIndices(file, layerNames);
            if (sourceLayerIndices.Count == 0)
                throw new InvalidOperationException("在外部Rhino文件中找不到指定图层。");

            HashSet<Guid> definitionObjectIds = GetDefinitionObjectIds(file);
            Dictionary<Guid, int> layerMap = new Dictionary<Guid, int>();
            Dictionary<Guid, int> importedDefinitions = new Dictionary<Guid, int>();
            List<Guid> result = new List<Guid>();
            List<string> warnings = new List<string>();

            foreach (File3dmObject fileObject in file.Objects)
            {
                if (fileObject == null || fileObject.Geometry == null || fileObject.Attributes == null)
                    continue;
                if (definitionObjectIds.Contains(fileObject.Id))
                    continue;
                if (!sourceLayerIndices.Contains(fileObject.Attributes.LayerIndex))
                    continue;

                Guid id = AddFileObjectToDocument(doc, file, fileObject, importedDefinitions, layerMap);
                if (id == Guid.Empty)
                    warnings.Add("对象导入失败：" + fileObject.Id);
                else
                    result.Add(id);
            }

            foreach (string warning in warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            if (result.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "指定图层上没有可导入的顶层对象。");

            doc.Views.Redraw();
            return result;
        }

        private void AddLayerMatchWarning(string layerName, int count)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "短图层名匹配到多个图层，将全部导入：" + layerName + " (" + count + ")");
        }

        private HashSet<int> FindSourceLayerIndices(File3dm file, IEnumerable<string> layerNames)
        {
            HashSet<int> result = new HashSet<int>();

            foreach (string rawName in layerNames)
            {
                string layerName = rawName?.Trim();
                if (string.IsNullOrWhiteSpace(layerName))
                    continue;

                if (layerName.EndsWith("*", StringComparison.Ordinal))
                {
                    string prefix = layerName.Substring(0, layerName.Length - 1);
                    List<Layer> wildcardMatches = file.AllLayers
                        .Where(layer => LayerFullPathStartsWith(layer, prefix))
                        .ToList();

                    if (wildcardMatches.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "找不到匹配图层：" + layerName);
                        continue;
                    }

                    foreach (Layer layer in wildcardMatches)
                        result.Add(layer.Index);
                    continue;
                }

                List<Layer> fullPathMatches = file.AllLayers
                    .Where(layer => string.Equals(layer.FullPath, layerName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (fullPathMatches.Count > 0)
                {
                    foreach (Layer layer in fullPathMatches)
                        result.Add(layer.Index);
                    continue;
                }

                List<Layer> nameMatches = file.AllLayers
                    .Where(layer => string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (nameMatches.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "找不到图层：" + layerName);
                    continue;
                }

                if (nameMatches.Count > 1)
                    AddLayerMatchWarning(layerName, nameMatches.Count);

                foreach (Layer layer in nameMatches)
                    result.Add(layer.Index);
            }

            return result;
        }

        private static bool LayerFullPathStartsWith(Layer layer, string prefix)
        {
            if (layer == null)
                return false;
            if (string.IsNullOrEmpty(prefix))
                return true;

            string fullPath = layer.FullPath ?? layer.Name ?? "";
            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<Guid> GetDefinitionObjectIds(File3dm file)
        {
            HashSet<Guid> result = new HashSet<Guid>();
            if (file == null)
                return result;

            foreach (InstanceDefinitionGeometry definition in file.AllInstanceDefinitions)
            {
                if (definition == null)
                    continue;

                foreach (Guid objectId in definition.GetObjectIds())
                    result.Add(objectId);
            }

            return result;
        }

        private static void DrawImportedObjectPreview(IGH_PreviewArgs args, RhinoDoc doc, RhinoObject obj, Color color)
        {
            if (obj is InstanceObject instanceObject)
            {
                InstanceDefinition definition = instanceObject.InstanceDefinition;
                if (definition != null)
                    DrawInstanceDefinitionPreview(args, doc, definition, instanceObject.InstanceXform, color, new HashSet<Guid>());
                return;
            }

            DrawGeometryPreview(args, doc, obj.Geometry, Transform.Identity, color, new HashSet<Guid>());
        }

        private static void DrawInstanceDefinitionPreview(IGH_PreviewArgs args, RhinoDoc doc, InstanceDefinition definition, Transform transform, Color color, HashSet<Guid> visited)
        {
            if (definition == null || !visited.Add(definition.Id))
                return;

            foreach (RhinoObject child in definition.GetObjects())
            {
                if (child?.Geometry == null)
                    continue;

                DrawGeometryPreview(args, doc, child.Geometry, transform, color, visited);
            }

            visited.Remove(definition.Id);
        }

        private static void DrawGeometryPreview(IGH_PreviewArgs args, RhinoDoc doc, GeometryBase geometry, Transform transform, Color color, HashSet<Guid> visited)
        {
            if (geometry == null)
                return;

            if (geometry is InstanceReferenceGeometry instanceReference)
            {
                InstanceDefinition nestedDefinition = doc.InstanceDefinitions.FindId(instanceReference.ParentIdefId);
                Transform nestedTransform = transform * instanceReference.Xform;
                DrawInstanceDefinitionPreview(args, doc, nestedDefinition, nestedTransform, color, visited);
                return;
            }

            GeometryBase previewGeometry = geometry.Duplicate();
            if (previewGeometry == null)
                return;

            previewGeometry.Transform(transform);

            if (previewGeometry is Brep brep)
                args.Display.DrawBrepWires(brep, color, 2);
            else if (previewGeometry is Curve curve)
                args.Display.DrawCurve(curve, color, 2);
            else if (previewGeometry is Mesh mesh)
                args.Display.DrawMeshWires(mesh, color);
            else if (previewGeometry is Rhino.Geometry.Point point)
                args.Display.DrawPoint(point.Location, color);
            else if (previewGeometry is PointCloud cloud)
            {
                foreach (PointCloudItem item in cloud)
                    args.Display.DrawPoint(item.Location, color);
            }
            else if (previewGeometry is TextEntity text)
                args.Display.DrawText(text, color);
            else if (previewGeometry is TextDot textDot)
            {
                args.Display.DrawPoint(textDot.Point, color);
                args.Display.Draw2dText(textDot.Text, color, textDot.Point, false, 12);
            }
            else if (previewGeometry is Extrusion extrusion)
                args.Display.DrawBrepWires(extrusion.ToBrep(), color, 2);
            else
            {
                BoundingBox box = previewGeometry.GetBoundingBox(true);
                if (box.IsValid)
                    args.Display.DrawBox(box, color);
            }
        }

        private static Guid AddFileObjectToDocument(
            RhinoDoc doc,
            File3dm file,
            File3dmObject fileObject,
            Dictionary<Guid, int> importedDefinitions,
            Dictionary<Guid, int> layerMap)
        {
            ObjectAttributes attributes = CleanAttributes(fileObject.Attributes?.Duplicate(), doc, file, layerMap);

            if (fileObject.Geometry is InstanceReferenceGeometry instanceReference)
            {
                InstanceDefinitionGeometry sourceDefinition = file.AllInstanceDefinitions.FindId(instanceReference.ParentIdefId);
                if (sourceDefinition == null)
                    return Guid.Empty;

                int definitionIndex = ImportDefinitionRecursive(
                    doc,
                    file,
                    sourceDefinition,
                    sourceDefinition.Name,
                    importedDefinitions,
                    layerMap);

                return doc.Objects.AddInstanceObject(definitionIndex, instanceReference.Xform, attributes);
            }

            GeometryBase geometry = fileObject.Geometry.Duplicate();
            if (geometry == null)
                return Guid.Empty;

            return doc.Objects.Add(geometry, attributes);
        }

        private static int ImportDefinitionRecursive(
            RhinoDoc doc,
            File3dm file,
            InstanceDefinitionGeometry sourceDefinition,
            string targetName,
            Dictionary<Guid, int> imported,
            Dictionary<Guid, int> layerMap)
        {
            if (sourceDefinition == null)
                throw new ArgumentNullException(nameof(sourceDefinition));

            if (imported.TryGetValue(sourceDefinition.Id, out int importedIndex))
                return importedIndex;

            string cleanTargetName = string.IsNullOrWhiteSpace(targetName) ? "ImportedBlock" : targetName;
            InstanceDefinition existing = doc.InstanceDefinitions.Find(cleanTargetName);
            if (existing != null)
            {
                imported[sourceDefinition.Id] = existing.Index;
                return existing.Index;
            }

            Dictionary<Guid, Guid> idMap = new Dictionary<Guid, Guid>();
            foreach (Guid childId in sourceDefinition.GetObjectIds())
            {
                File3dmObject childObject = file.Objects.FindId(childId);
                if (childObject?.Geometry is InstanceReferenceGeometry childReference)
                {
                    InstanceDefinitionGeometry childDefinition = file.AllInstanceDefinitions.FindId(childReference.ParentIdefId);
                    if (childDefinition == null)
                        continue;

                    int childIndex = ImportDefinitionRecursive(
                        doc,
                        file,
                        childDefinition,
                        childDefinition.Name,
                        imported,
                        layerMap);

                    InstanceDefinition importedChild = doc.InstanceDefinitions[childIndex];
                    idMap[childDefinition.Id] = importedChild.Id;
                }
            }

            List<GeometryBase> geometry = new List<GeometryBase>();
            List<ObjectAttributes> attributes = new List<ObjectAttributes>();
            foreach (Guid objectId in sourceDefinition.GetObjectIds())
            {
                File3dmObject childObject = file.Objects.FindId(objectId);
                if (childObject == null)
                    continue;

                GeometryBase duplicatedGeometry = DuplicateGeometryForCurrentDocument(childObject.Geometry, idMap);
                if (duplicatedGeometry == null)
                    continue;

                geometry.Add(duplicatedGeometry);
                attributes.Add(CleanAttributes(childObject.Attributes?.Duplicate(), doc, file, layerMap));
            }

            if (geometry.Count == 0)
                throw new InvalidOperationException("块定义中没有可导入的几何：" + sourceDefinition.Name);

            int index = doc.InstanceDefinitions.Add(
                cleanTargetName,
                sourceDefinition.Description ?? "",
                Point3d.Origin,
                geometry,
                attributes);

            if (index < 0)
                throw new InvalidOperationException("导入块定义失败：" + cleanTargetName);

            imported[sourceDefinition.Id] = index;
            return index;
        }

        private static GeometryBase DuplicateGeometryForCurrentDocument(GeometryBase geometry, Dictionary<Guid, Guid> idMap)
        {
            if (geometry == null)
                return null;

            if (geometry is InstanceReferenceGeometry instanceReference)
            {
                if (!idMap.TryGetValue(instanceReference.ParentIdefId, out Guid newDefinitionId))
                    return null;

                return new InstanceReferenceGeometry(newDefinitionId, instanceReference.Xform);
            }

            return geometry.Duplicate();
        }

        private static ObjectAttributes CleanAttributes(ObjectAttributes attributes, RhinoDoc doc, File3dm file, Dictionary<Guid, int> layerMap)
        {
            ObjectAttributes result = attributes ?? new ObjectAttributes();
            result.LayerIndex = GetOrCreateLayerFromFile(doc, file, result.LayerIndex, layerMap);
            result.Visible = true;
            result.Mode = ObjectMode.Normal;
            return result;
        }

        private static int GetOrCreateLayerFromFile(RhinoDoc doc, File3dm file, int sourceLayerIndex, Dictionary<Guid, int> layerMap)
        {
            if (doc == null)
                return -1;

            if (file == null || sourceLayerIndex < 0 || sourceLayerIndex >= file.AllLayers.Count)
                return doc.Layers.CurrentLayerIndex >= 0 ? doc.Layers.CurrentLayerIndex : 0;

            Layer sourceLayer = file.AllLayers.ElementAtOrDefault(sourceLayerIndex);
            if (sourceLayer == null)
                return doc.Layers.CurrentLayerIndex >= 0 ? doc.Layers.CurrentLayerIndex : 0;

            if (layerMap != null && layerMap.TryGetValue(sourceLayer.Id, out int mappedLayerIndex))
                return mappedLayerIndex;

            string fullPath = sourceLayer.FullPath ?? sourceLayer.Name ?? "";
            int existingIndex = string.IsNullOrWhiteSpace(fullPath) ? -1 : doc.Layers.FindByFullPath(fullPath, -1);
            if (existingIndex >= 0)
            {
                if (layerMap != null)
                    layerMap[sourceLayer.Id] = existingIndex;
                return existingIndex;
            }

            int parentIndex = -1;
            if (sourceLayer.ParentLayerId != Guid.Empty)
            {
                Layer parentLayer = file.AllLayers.FirstOrDefault(layer => layer.Id == sourceLayer.ParentLayerId);
                if (parentLayer != null)
                    parentIndex = GetOrCreateLayerFromFile(doc, file, parentLayer.Index, layerMap);
            }

            Layer newLayer = new Layer
            {
                Name = string.IsNullOrWhiteSpace(sourceLayer.Name) ? "ImportedLayer" : sourceLayer.Name,
                Color = sourceLayer.Color,
                IsVisible = sourceLayer.IsVisible,
                IsLocked = sourceLayer.IsLocked
            };

            if (parentIndex >= 0 && parentIndex < doc.Layers.Count)
                newLayer.ParentLayerId = doc.Layers[parentIndex].Id;

            int newIndex = doc.Layers.Add(newLayer);
            if (newIndex < 0)
            {
                string fallbackName = string.IsNullOrWhiteSpace(sourceLayer.Name) ? "ImportedLayer" : sourceLayer.Name;
                newIndex = doc.Layers.Add(fallbackName, sourceLayer.Color);
            }

            if (newIndex < 0)
                newIndex = doc.Layers.CurrentLayerIndex >= 0 ? doc.Layers.CurrentLayerIndex : 0;

            if (layerMap != null)
                layerMap[sourceLayer.Id] = newIndex;
            return newIndex;
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ImportRhinoObjectsByLayer"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("6E7DF21D-29CB-45EF-9A3B-5AA4D5F3866E"); }
        }
    }

    internal class CButton_ImportRhinoObjectsByLayer : GH_ComponentAttributes
    {
        public CButton_ImportRhinoObjectsByLayer(ImportRhinoObjectsByLayer component) : base(component) { }

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
                GH_Palette palette = ((ImportRhinoObjectsByLayer)Owner).CurrentButtonColor == ImportRhinoObjectsByLayer.ButtonColor.Black
                    ? GH_Palette.Black
                    : GH_Palette.Grey;

                using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                    capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

                using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    graphics.DrawString("Run", font, Brushes.White, buttonRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                ImportRhinoObjectsByLayer owner = (ImportRhinoObjectsByLayer)Owner;
                owner.CurrentButtonColor = ImportRhinoObjectsByLayer.ButtonColor.Grey;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                CMath.Delay(50);
                owner.CurrentButtonColor = ImportRhinoObjectsByLayer.ButtonColor.Black;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
