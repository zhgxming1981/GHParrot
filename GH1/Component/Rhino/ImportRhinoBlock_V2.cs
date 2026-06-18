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
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class ImportRhinoBlock_V2 : GH_Component
    {
        public enum ButtonColor { Black, Grey }

        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        internal bool ButtonRun { get; set; }

        private bool _lastInputRun;
        private readonly List<Guid> _lastGuids = new List<Guid>();
        private readonly List<InstanceReferenceGeometry> _lastBlockReferences = new List<InstanceReferenceGeometry>();
        private string _lastFilePath = "";
        private string _blockNameCacheFilePath = "";
        private DateTime _blockNameCacheLastWriteUtc = DateTime.MinValue;
        private FileSystemWatcher _blockFileWatcher;
        private string _watchedFilePath = "";
        private DateTime _lastWatcherExpireUtc = DateTime.MinValue;
        private string _selectedBlockName = "";
        private readonly List<string> _blockNames = new List<string>();
        private readonly List<GeometryBase> _previewGeometry = new List<GeometryBase>();
        private BoundingBox _previewBox = BoundingBox.Empty;

        private const string SourceFileKey = "Parrot.ImportRhinoBlock_V2.SourceFile";
        private const string SourceBlockNameKey = "Parrot.ImportRhinoBlock_V2.SourceBlockName";
        private const string SourceBlockHashKey = "Parrot.ImportRhinoBlock_V2.SourceBlockHash";
        private const string LocalBlockHashKey = "Parrot.ImportRhinoBlock_V2.LocalBlockHash";
        private const string SourceFileTimeKey = "Parrot.ImportRhinoBlock_V2.SourceFileLastWriteUtc";

        public ImportRhinoBlock_V2()
          : base("ImportRhinoBlock_V2", "ImportRhinoBlock_V2",
              "从未打开的Rhino文件导入指定块，并在当前Rhino中新增一个块实例",
              "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件路径", "File", "Rhino文件路径", GH_ParamAccess.item);
            pManager.AddTextParameter("块名", "Block", "要导入的块名", GH_ParamAccess.item);
            pManager.AddPlaneParameter("工作平面", "Plane", "块实例插入的工作平面", GH_ParamAccess.list, Plane.WorldXY);
            pManager.AddBooleanParameter("Run", "Run", "执行导入并插入块实例", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("输出Guid", "AsGuid", "为True时插入Rhino块实例并输出Guid；为False时输出Grasshopper中未Bake的块", GH_ParamAccess.item, true);
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("块", "Block", "输出Guid为True时输出新增块实例的Guid；为False时输出Grasshopper中未Bake的块", GH_ParamAccess.list);
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_ImportRhinoBlock_V2(this);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string filePath = "";
            string blockName = "";
            bool inputRun = false;
            bool outputGuid = true;
            List<Plane> workPlanes = new List<Plane>();

            if (!DA.GetData(0, ref filePath)) { return; }
            DA.GetData(1, ref blockName);
            DA.GetDataList(2, workPlanes);
            if (workPlanes.Count == 0)
                workPlanes.Add(Plane.WorldXY);
            DA.GetData(3, ref inputRun);
            DA.GetData(4, ref outputGuid);

            ConfigureBlockFileWatcher(filePath);
            bool blockListChanged = RefreshBlockNameCache(filePath);
            _lastFilePath = filePath;

            if (string.IsNullOrWhiteSpace(blockName))
                blockName = _selectedBlockName;

            if (blockListChanged && string.IsNullOrWhiteSpace(blockName) && _blockNames.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外部Rhino文件的块列表已更新，请重新选择要导入的块。");

            UpdatePreview(filePath, blockName, workPlanes);

            bool shouldRun = ButtonRun || (inputRun && !_lastInputRun);
            _lastInputRun = inputRun;
            ButtonRun = false;

            if (!shouldRun)
            {
                CheckReferencedBlockChanges(filePath, blockName);
                if (outputGuid && _lastGuids.Count > 0)
                    DA.SetDataList(0, _lastGuids.Select(guid => new GH_Guid(guid)));
                else if (!outputGuid && _lastBlockReferences.Count > 0)
                    DA.SetDataList(0, _lastBlockReferences.Select(CreateBlockReferenceGoo).Where(reference => reference != null));
                return;
            }

            try
            {
                RefreshBlockNameCache(filePath, true);
                if (string.IsNullOrWhiteSpace(blockName))
                    blockName = _selectedBlockName;
                if (outputGuid)
                {
                    List<Guid> guids = ImportAndInsert(filePath, blockName, workPlanes);
                    _lastGuids.Clear();
                    _lastGuids.AddRange(guids);
                    _lastBlockReferences.Clear();
                    if (guids.Count > 0)
                        DA.SetDataList(0, guids.Select(guid => new GH_Guid(guid)));
                }
                else
                {
                    List<InstanceReferenceGeometry> blockReferences = ImportBlockReferences(filePath, blockName, workPlanes);
                    _lastBlockReferences.Clear();
                    _lastBlockReferences.AddRange(blockReferences);
                    _lastGuids.Clear();
                    if (blockReferences.Count > 0)
                        DA.SetDataList(0, blockReferences.Select(CreateBlockReferenceGoo).Where(reference => reference != null));
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            ToolStripMenuItem root = new ToolStripMenuItem("块名");
            menu.Items.Add(root);

            RefreshBlockNameCache(_lastFilePath);

            if (string.IsNullOrWhiteSpace(_lastFilePath))
            {
                root.DropDownItems.Add(new ToolStripMenuItem("请先输入文件路径") { Enabled = false });
                return;
            }

            if (_blockNames.Count == 0)
            {
                root.DropDownItems.Add(new ToolStripMenuItem("未找到块名") { Enabled = false });
                return;
            }

            foreach (string name in _blockNames)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(name)
                {
                    Checked = string.Equals(name, _selectedBlockName, StringComparison.OrdinalIgnoreCase),
                    Tag = name
                };
                item.Click += SelectBlockNameFromMenu;
                root.DropDownItems.Add(item);
            }
        }

        private void SelectBlockNameFromMenu(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem item && item.Tag is string name)
            {
                _selectedBlockName = name;
                Message = name;
                ExpireSolution(true);
            }
        }

        private bool RefreshBlockNameCache(string filePath, bool force = false)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                bool hadCache = _blockNames.Count > 0 || !string.IsNullOrWhiteSpace(_blockNameCacheFilePath);
                _blockNames.Clear();
                _blockNameCacheFilePath = filePath ?? "";
                _blockNameCacheLastWriteUtc = DateTime.MinValue;
                if (hadCache)
                    Message = "";
                return hadCache;
            }

            DateTime lastWriteUtc = GetFileLastWriteUtc(filePath);
            bool cacheIsCurrent =
                !force &&
                string.Equals(_blockNameCacheFilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
                _blockNameCacheLastWriteUtc == lastWriteUtc;

            if (cacheIsCurrent)
                return false;

            string previousSelectedName = _selectedBlockName;
            _blockNames.Clear();
            _blockNameCacheFilePath = filePath;
            _blockNameCacheLastWriteUtc = lastWriteUtc;

            File3dm file = File3dm.Read(filePath);
            if (file == null)
            {
                Message = "";
                return true;
            }

            foreach (InstanceDefinitionGeometry definition in file.AllInstanceDefinitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
                    continue;
                _blockNames.Add(definition.Name);
            }

            _blockNames.Sort(StringComparer.CurrentCultureIgnoreCase);
            if (_blockNames.Count == 0)
            {
                _selectedBlockName = "";
                Message = "";
                return true;
            }

            if (string.IsNullOrWhiteSpace(_selectedBlockName))
                _selectedBlockName = _blockNames[0];
            else if (!_blockNames.Any(name => string.Equals(name, _selectedBlockName, StringComparison.OrdinalIgnoreCase)))
                _selectedBlockName = "";

            Message = _selectedBlockName;
            return !string.Equals(previousSelectedName, _selectedBlockName, StringComparison.OrdinalIgnoreCase) ||
                !cacheIsCurrent;
        }

        private static DateTime GetFileLastWriteUtc(string filePath)
        {
            try
            {
                return File.GetLastWriteTimeUtc(filePath);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private void ConfigureBlockFileWatcher(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                DisposeBlockFileWatcher();
                return;
            }

            string fullPath = Path.GetFullPath(filePath);
            if (string.Equals(_watchedFilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                return;

            DisposeBlockFileWatcher();

            string directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                return;

            _blockFileWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
            };
            _blockFileWatcher.Changed += BlockFileWatcherChanged;
            _blockFileWatcher.Created += BlockFileWatcherChanged;
            _blockFileWatcher.Deleted += BlockFileWatcherChanged;
            _blockFileWatcher.Renamed += BlockFileWatcherChanged;
            _blockFileWatcher.EnableRaisingEvents = true;
            _watchedFilePath = fullPath;
        }

        private void DisposeBlockFileWatcher()
        {
            if (_blockFileWatcher != null)
            {
                _blockFileWatcher.EnableRaisingEvents = false;
                _blockFileWatcher.Changed -= BlockFileWatcherChanged;
                _blockFileWatcher.Created -= BlockFileWatcherChanged;
                _blockFileWatcher.Deleted -= BlockFileWatcherChanged;
                _blockFileWatcher.Renamed -= BlockFileWatcherChanged;
                _blockFileWatcher.Dispose();
                _blockFileWatcher = null;
            }

            _watchedFilePath = "";
        }

        private void BlockFileWatcherChanged(object sender, FileSystemEventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastWatcherExpireUtc).TotalMilliseconds < 500)
                return;

            _lastWatcherExpireUtc = now;
            GH_Document document = OnPingDocument();
            if (document == null)
                return;

            document.ScheduleSolution(800, doc => ExpireSolution(false));
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetString("SelectedBlockName", _selectedBlockName ?? "");
            writer.SetString("LastFilePath", _lastFilePath ?? "");
            if (_lastGuids.Count > 0)
                writer.SetString("LastGuids", string.Join(";", _lastGuids.Select(guid => guid.ToString())));

            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            string selected = "";
            if (reader.TryGetString("SelectedBlockName", ref selected))
                _selectedBlockName = selected;

            string filePath = "";
            if (reader.TryGetString("LastFilePath", ref filePath))
            {
                _lastFilePath = filePath;
                ConfigureBlockFileWatcher(_lastFilePath);
                RefreshBlockNameCache(_lastFilePath);
            }

            string guidText = "";
            if (reader.TryGetString("LastGuids", ref guidText))
            {
                _lastGuids.Clear();
                foreach (string item in guidText.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Guid.TryParse(item, out Guid guid))
                        _lastGuids.Add(guid);
                }
            }
            else if (reader.TryGetString("LastGuid", ref guidText) && Guid.TryParse(guidText, out Guid guid))
            {
                _lastGuids.Clear();
                _lastGuids.Add(guid);
            }

            Message = _selectedBlockName;
            return base.Read(reader);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            DisposeBlockFileWatcher();
            base.RemovedFromDocument(document);
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox baseBox = base.ClippingBox;
                if (_previewBox.IsValid)
                    baseBox.Union(_previewBox);
                return baseBox;
            }
        }

        public override bool IsPreviewCapable
        {
            get { return true; }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (Hidden || Locked || _previewGeometry.Count == 0)
                return;

            Color color = Attributes?.Selected == true ? args.WireColour_Selected : args.WireColour;
            foreach (GeometryBase geometry in _previewGeometry)
                DrawGeometryWires(args, geometry, color);
        }

        private static void DrawGeometryWires(IGH_PreviewArgs args, GeometryBase geometry, Color color)
        {
            if (geometry is Brep brep)
                args.Display.DrawBrepWires(brep, color);
            else if (geometry is Curve curve)
                args.Display.DrawCurve(curve, color, 1);
            else if (geometry is Mesh mesh)
                args.Display.DrawMeshWires(mesh, color);
            else if (geometry is Rhino.Geometry.Point point)
                args.Display.DrawPoint(point.Location, color);
            else if (geometry is PointCloud cloud)
            {
                foreach (PointCloudItem item in cloud)
                    args.Display.DrawPoint(item.Location, color);
            }
            else if (geometry is TextEntity text)
                args.Display.DrawText(text, color);
            else if (geometry is TextDot textDot)
            {
                args.Display.DrawPoint(textDot.Point, color);
                args.Display.Draw2dText(textDot.Text, color, textDot.Point, false, 12);
            }
        }

        private void UpdatePreview(string filePath, string blockName, IEnumerable<Plane> workPlanes)
        {
            _previewGeometry.Clear();
            _previewBox = BoundingBox.Empty;

            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(blockName) || !File.Exists(filePath))
                return;

            File3dm file = File3dm.Read(filePath);
            InstanceDefinitionGeometry definition = file?.AllInstanceDefinitions.FindName(blockName);
            if (definition == null)
                return;

            Transform sourceTransform = GetSourceBlockInstanceTransform(file, definition);
            foreach (Plane workPlane in workPlanes)
            {
                Transform transform = GetInsertionTransform(workPlane) * sourceTransform;
                foreach (GeometryBase geometry in BuildPreviewGeometry(file, definition, transform, new HashSet<Guid>()))
                {
                    if (geometry == null)
                        continue;
                    _previewGeometry.Add(geometry);
                    BoundingBox box = geometry.GetBoundingBox(false);
                    if (box.IsValid)
                        _previewBox.Union(box);
                }
            }
        }

        private static IEnumerable<GeometryBase> BuildPreviewGeometry(File3dm file, InstanceDefinitionGeometry definition, Transform transform, HashSet<Guid> visited)
        {
            if (file == null || definition == null || !visited.Add(definition.Id))
                yield break;

            foreach (Guid objectId in definition.GetObjectIds())
            {
                File3dmObject fileObject = file.Objects.FindId(objectId);
                if (fileObject?.Geometry == null)
                    continue;

                if (fileObject.Geometry is InstanceReferenceGeometry instanceReference)
                {
                    InstanceDefinitionGeometry nestedDefinition = file.AllInstanceDefinitions.FindId(instanceReference.ParentIdefId);
                    Transform nestedTransform = instanceReference.Xform;
                    nestedTransform = transform * nestedTransform;
                    foreach (GeometryBase nestedGeometry in BuildPreviewGeometry(file, nestedDefinition, nestedTransform, visited))
                        yield return nestedGeometry;
                }
                else
                {
                    GeometryBase duplicated = fileObject.Geometry.Duplicate();
                    if (duplicated == null)
                        continue;
                    duplicated.Transform(transform);
                    yield return duplicated;
                }
            }

            visited.Remove(definition.Id);
        }

        private List<Guid> ImportAndInsert(string filePath, string blockName, IEnumerable<Plane> workPlanes)
        {
            BlockInsertContextV2 context = PrepareBlockInsert(filePath, blockName);
            List<Guid> insertedIds = new List<Guid>();
            if (context == null)
                return insertedIds;

            foreach (Plane workPlane in workPlanes)
            {
                Guid insertedId = InsertBlockInstance(context, workPlane);
                if (insertedId != Guid.Empty)
                    insertedIds.Add(insertedId);
            }

            context.Doc.Views.Redraw();
            return insertedIds;
        }

        private List<InstanceReferenceGeometry> ImportBlockReferences(string filePath, string blockName, IEnumerable<Plane> workPlanes)
        {
            BlockInsertContextV2 context = PrepareBlockInsert(filePath, blockName);
            List<InstanceReferenceGeometry> references = new List<InstanceReferenceGeometry>();
            if (context == null)
                return references;

            foreach (Plane workPlane in workPlanes)
            {
                InstanceReferenceGeometry reference = CreateBlockReference(context, workPlane);
                if (reference != null)
                    references.Add(reference);
            }

            context.Doc.Views.Redraw();
            return references;
        }

        private BlockInsertContextV2 PrepareBlockInsert(string filePath, string blockName)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Rhino文件路径为空。");
            if (!File.Exists(filePath))
                throw new FileNotFoundException("找不到Rhino文件。", filePath);
            if (string.IsNullOrWhiteSpace(blockName))
                throw new ArgumentException("块名为空。");

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
                throw new InvalidOperationException("当前没有可用的Rhino文档。");

            File3dm file = File3dm.Read(filePath);
            if (file == null)
                throw new InvalidOperationException("无法读取Rhino文件。");

            InstanceDefinitionGeometry sourceDefinition = file.AllInstanceDefinitions.FindName(blockName);
            if (sourceDefinition == null)
                throw new InvalidOperationException("在外部Rhino文件中找不到指定块：" + blockName);

            InstanceDefinition existingDefinition = doc.InstanceDefinitions.Find(blockName);
            InstanceDefinition targetDefinition = existingDefinition;
            string sourceHash = BuildFileDefinitionHash(file, sourceDefinition);
            Dictionary<Guid, int> layerMap = new Dictionary<Guid, int>();
            SourceBlockInstanceInfoV2 sourceInstanceInfo = GetSourceBlockInstanceInfoV2(doc, file, sourceDefinition, layerMap);

            if (existingDefinition != null)
            {
                BlockRecordStatusV2 status = GetBlockRecordStatusV2(doc, existingDefinition, sourceHash, filePath);
                ReportRecordStatus(status);

                if (!status.SourceFileTimeChanged && (status.SourceMatchesRecord || AreBlockDefinitionsSame(doc, file, sourceDefinition, existingDefinition)))
                {
                    targetDefinition = existingDefinition;
                    RecordBlockImportMetadata(targetDefinition, filePath, blockName, sourceHash, BuildDocDefinitionHash(doc, targetDefinition));
                }
                else
                {
                    ImportBlockConflictChoiceV2 choice = ImportBlockConflictDialogV2.ShowDialog(blockName);
                    if (choice == ImportBlockConflictChoiceV2.Cancel)
                        return null;

                    if (choice == ImportBlockConflictChoiceV2.KeepCurrent)
                    {
                        targetDefinition = existingDefinition;
                    }
                    else if (choice == ImportBlockConflictChoiceV2.RenameImported)
                    {
                        string newName = GetUniqueBlockName(doc, blockName);
                        int index = ImportDefinitionRecursive(doc, file, sourceDefinition, newName, new Dictionary<Guid, int>(), layerMap);
                        targetDefinition = doc.InstanceDefinitions[index];
                        RecordBlockImportMetadata(targetDefinition, filePath, blockName, sourceHash, BuildDocDefinitionHash(doc, targetDefinition));
                    }
                    else if (choice == ImportBlockConflictChoiceV2.ReplaceCurrent)
                    {
                        targetDefinition = ReplaceDefinition(doc, file, sourceDefinition, existingDefinition);
                        RecordBlockImportMetadata(targetDefinition, filePath, blockName, sourceHash, BuildDocDefinitionHash(doc, targetDefinition));
                    }
                }
            }
            else
            {
                int index = ImportDefinitionRecursive(doc, file, sourceDefinition, blockName, new Dictionary<Guid, int>(), layerMap);
                targetDefinition = doc.InstanceDefinitions[index];
                RecordBlockImportMetadata(targetDefinition, filePath, blockName, sourceHash, BuildDocDefinitionHash(doc, targetDefinition));
            }

            if (targetDefinition == null)
                throw new InvalidOperationException("块定义导入失败。");

            return new BlockInsertContextV2
            {
                Doc = doc,
                TargetDefinition = targetDefinition,
                SourceInstanceInfo = sourceInstanceInfo
            };
        }

        private static Guid InsertBlockInstance(BlockInsertContextV2 context, Plane workPlane)
        {
            RhinoDoc doc = context.Doc;
            InstanceDefinition targetDefinition = context.TargetDefinition;
            SourceBlockInstanceInfoV2 sourceInstanceInfo = context.SourceInstanceInfo;

            ObjectAttributes instanceAttributes = sourceInstanceInfo.Attributes?.Duplicate() ?? doc.CreateDefaultAttributes();
            instanceAttributes.Visible = true;
            instanceAttributes.Mode = ObjectMode.Normal;
            instanceAttributes.LayerIndex = sourceInstanceInfo.LayerIndex;

            Transform insertionTransform = GetInsertionTransform(workPlane) * sourceInstanceInfo.Xform;
            Guid insertedId = doc.Objects.AddInstanceObject(targetDefinition.Index, insertionTransform, instanceAttributes);
            if (insertedId == Guid.Empty)
                throw new InvalidOperationException("块实例插入失败。");

            RhinoObject insertedObject = doc.Objects.FindId(insertedId);
            insertedObject?.Select(true);
            return insertedId;
        }

        private static InstanceReferenceGeometry CreateBlockReference(BlockInsertContextV2 context, Plane workPlane)
        {
            InstanceDefinition targetDefinition = context.TargetDefinition;
            SourceBlockInstanceInfoV2 sourceInstanceInfo = context.SourceInstanceInfo;

            Transform insertionTransform = GetInsertionTransform(workPlane) * sourceInstanceInfo.Xform;
            return new InstanceReferenceGeometry(targetDefinition.Id, insertionTransform);
        }

        private static RhinoBlockReferenceGooV2 CreateBlockReferenceGoo(InstanceReferenceGeometry reference)
        {
            return reference == null ? null : new RhinoBlockReferenceGooV2(reference);
        }

        private static Transform GetInsertionTransform(Plane workPlane)
        {
            if (!workPlane.IsValid)
                workPlane = Plane.WorldXY;

            return Transform.PlaneToPlane(Plane.WorldXY, workPlane);
        }

        private void CheckReferencedBlockChanges(string filePath, string blockName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(blockName) || !File.Exists(filePath))
                return;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
                return;

            InstanceDefinition existingDefinition = doc.InstanceDefinitions.Find(blockName);
            if (existingDefinition == null)
                return;

            File3dm file = File3dm.Read(filePath);
            InstanceDefinitionGeometry sourceDefinition = file?.AllInstanceDefinitions.FindName(blockName);
            if (sourceDefinition == null)
                return;

            string sourceHash = BuildFileDefinitionHash(file, sourceDefinition);
            BlockRecordStatusV2 status = GetBlockRecordStatusV2(doc, existingDefinition, sourceHash, filePath);
            ReportRecordStatus(status);
        }

        private void ReportRecordStatus(BlockRecordStatusV2 status)
        {
            if (status.SourceChanged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外部Rhino文件中的块定义已修改。");
            if (status.SourceFileTimeChanged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外部Rhino文件已重新保存，请确认是否需要替换本图块定义。");
            if (status.LocalChanged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "当前Rhino文档中的块定义已修改。");
        }

        private static bool AreBlockDefinitionsSame(RhinoDoc doc, File3dm file, InstanceDefinitionGeometry sourceDefinition, InstanceDefinition existingDefinition)
        {
            string sourceHash = BuildFileDefinitionHash(file, sourceDefinition);
            string currentHash = BuildDocDefinitionHash(doc, existingDefinition);
            return string.Equals(sourceHash, currentHash, StringComparison.Ordinal);
        }

        private static string BuildFileDefinitionHash(File3dm file, InstanceDefinitionGeometry definition)
        {
            return ComputeHash(BuildFileDefinitionSignature(file, definition, new HashSet<Guid>()));
        }

        private static string BuildDocDefinitionHash(RhinoDoc doc, InstanceDefinition definition)
        {
            return ComputeHash(BuildDocDefinitionSignature(doc, definition, new HashSet<Guid>()));
        }

        private static string ComputeHash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                StringBuilder builder = new StringBuilder(bytes.Length * 2);
                foreach (byte value in bytes)
                    builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }

        private static BlockRecordStatusV2 GetBlockRecordStatusV2(RhinoDoc doc, InstanceDefinition existingDefinition, string sourceHash, string sourceFilePath)
        {
            string recordedSourceHash = existingDefinition.GetUserString(SourceBlockHashKey);
            string recordedLocalHash = existingDefinition.GetUserString(LocalBlockHashKey);
            string recordedSourceFileTime = existingDefinition.GetUserString(SourceFileTimeKey);
            string currentLocalHash = BuildDocDefinitionHash(doc, existingDefinition);
            string currentSourceFileTime = GetSourceFileLastWriteUtc(sourceFilePath);
            bool sourceMatchesCurrent = string.Equals(sourceHash, currentLocalHash, StringComparison.Ordinal);

            return new BlockRecordStatusV2
            {
                HasRecord = !string.IsNullOrWhiteSpace(recordedSourceHash),
                SourceMatchesRecord = !string.IsNullOrWhiteSpace(recordedSourceHash) &&
                    string.Equals(recordedSourceHash, sourceHash, StringComparison.Ordinal),
                SourceChanged = !string.IsNullOrWhiteSpace(recordedSourceHash) &&
                    !string.Equals(recordedSourceHash, sourceHash, StringComparison.Ordinal) &&
                    !sourceMatchesCurrent,
                SourceFileTimeChanged = !string.IsNullOrWhiteSpace(recordedSourceFileTime) &&
                    !string.Equals(recordedSourceFileTime, currentSourceFileTime, StringComparison.Ordinal),
                LocalChanged = !string.IsNullOrWhiteSpace(recordedLocalHash) &&
                    !string.Equals(recordedLocalHash, currentLocalHash, StringComparison.Ordinal) &&
                    !sourceMatchesCurrent
            };
        }

        private static void RecordBlockImportMetadata(InstanceDefinition definition, string filePath, string sourceBlockName, string sourceHash, string localHash)
        {
            if (definition == null)
                return;

            definition.SetUserString(SourceFileKey, filePath ?? "");
            definition.SetUserString(SourceBlockNameKey, sourceBlockName ?? "");
            definition.SetUserString(SourceBlockHashKey, sourceHash ?? "");
            definition.SetUserString(LocalBlockHashKey, localHash ?? "");
            definition.SetUserString(SourceFileTimeKey, GetSourceFileLastWriteUtc(filePath));
        }

        private static string GetSourceFileLastWriteUtc(string filePath)
        {
            return File.Exists(filePath)
                ? File.GetLastWriteTimeUtc(filePath).ToString("O")
                : "";
        }

        private static string BuildFileDefinitionSignature(File3dm file, InstanceDefinitionGeometry definition, HashSet<Guid> visited)
        {
            if (definition == null)
                return "null";
            if (!visited.Add(definition.Id))
                return "recursive:" + definition.Name;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("definition");
            AppendDefinitionProperties(builder, definition.Name, definition.Description, definition.Url, definition.UrlDescription);

            List<string> objectSignatures = new List<string>();
            foreach (Guid objectId in definition.GetObjectIds())
            {
                File3dmObject fileObject = file.Objects.FindId(objectId);
                if (fileObject == null)
                    continue;

                objectSignatures.Add(BuildFileObjectSignature(file, fileObject, visited));
            }

            foreach (string signature in objectSignatures.OrderBy(x => x, StringComparer.Ordinal))
                builder.AppendLine(signature);

            visited.Remove(definition.Id);
            return builder.ToString();
        }

        private static string BuildDocDefinitionSignature(RhinoDoc doc, InstanceDefinition definition, HashSet<Guid> visited)
        {
            if (definition == null)
                return "null";
            if (!visited.Add(definition.Id))
                return "recursive:" + definition.Name;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("definition");
            AppendDefinitionProperties(builder, definition.Name, definition.Description, definition.Url, definition.UrlDescription);

            List<string> objectSignatures = new List<string>();
            foreach (RhinoObject obj in definition.GetObjects())
            {
                if (obj == null)
                    continue;

                objectSignatures.Add(BuildDocObjectSignature(doc, obj, visited));
            }

            foreach (string signature in objectSignatures.OrderBy(x => x, StringComparer.Ordinal))
                builder.AppendLine(signature);

            visited.Remove(definition.Id);
            return builder.ToString();
        }

        private static void AppendDefinitionProperties(StringBuilder builder, string name, string description, string url, string urlDescription)
        {
            builder.AppendLine("name:" + (name ?? ""));
            builder.AppendLine("description:" + (description ?? ""));
            builder.AppendLine("url:" + (url ?? ""));
            builder.AppendLine("urlDescription:" + (urlDescription ?? ""));
        }

        private static string BuildFileObjectSignature(File3dm file, File3dmObject fileObject, HashSet<Guid> visited)
        {
            StringBuilder builder = new StringBuilder();
            GeometryBase geometry = fileObject.Geometry;
            builder.AppendLine("object");
            builder.AppendLine(BuildFileGeometrySignature(file, geometry, visited));
            builder.AppendLine(BuildFileAttributeSignature(file, fileObject.Attributes));
            return builder.ToString();
        }

        private static string BuildDocObjectSignature(RhinoDoc doc, RhinoObject obj, HashSet<Guid> visited)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("object");
            builder.AppendLine(BuildDocGeometrySignature(doc, obj.Geometry, visited));
            builder.AppendLine(BuildDocAttributeSignature(doc, obj.Attributes));
            return builder.ToString();
        }

        private static string BuildFileGeometrySignature(File3dm file, GeometryBase geometry, HashSet<Guid> visited)
        {
            if (geometry == null)
                return "geometry:null";

            if (geometry is InstanceReferenceGeometry instanceReference)
            {
                InstanceDefinitionGeometry nestedDefinition = file.AllInstanceDefinitions.FindId(instanceReference.ParentIdefId);
                return "geometry:InstanceReference\nxform:" + TransformToString(instanceReference.Xform) +
                       "\nnested:" + BuildFileDefinitionSignature(file, nestedDefinition, visited);
            }

            return BuildPlainGeometrySignature(geometry);
        }

        private static string BuildDocGeometrySignature(RhinoDoc doc, GeometryBase geometry, HashSet<Guid> visited)
        {
            if (geometry == null)
                return "geometry:null";

            if (geometry is InstanceReferenceGeometry instanceReference)
            {
                InstanceDefinition nestedDefinition = doc.InstanceDefinitions.FindId(instanceReference.ParentIdefId);
                return "geometry:InstanceReference\nxform:" + TransformToString(instanceReference.Xform) +
                       "\nnested:" + BuildDocDefinitionSignature(doc, nestedDefinition, visited);
            }

            return BuildPlainGeometrySignature(geometry);
        }

        private static string BuildPlainGeometrySignature(GeometryBase geometry)
        {
            SerializationOptions options = new SerializationOptions
            {
                WriteUserData = true,
                WriteRenderMeshes = true,
                WriteAnalysisMeshes = true
            };

            return "geometry:" + geometry.GetType().FullName + "\njson:" + geometry.ToJSON(options);
        }

        private static string BuildFileAttributeSignature(File3dm file, ObjectAttributes attributes)
        {
            return BuildAttributeSignature(
                attributes,
                GetFileLayerName(file, attributes),
                GetFileLinetypeName(file, attributes),
                GetFileMaterialName(file, attributes));
        }

        private static string BuildDocAttributeSignature(RhinoDoc doc, ObjectAttributes attributes)
        {
            return BuildAttributeSignature(
                attributes,
                GetDocLayerName(doc, attributes),
                GetDocLinetypeName(doc, attributes),
                GetDocMaterialName(doc, attributes));
        }

        private static string BuildAttributeSignature(ObjectAttributes attributes, string layerName, string linetypeName, string materialName)
        {
            if (attributes == null)
                return "attributes:null";

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("attributes");
            builder.AppendLine("Name:" + (attributes.Name ?? ""));
            builder.AppendLine("Url:" + (attributes.Url ?? ""));
            builder.AppendLine("Layer:" + (layerName ?? ""));
            builder.AppendLine("Linetype:" + (linetypeName ?? ""));
            builder.AppendLine("Material:" + (materialName ?? ""));
            builder.AppendLine("LinetypeSource:" + attributes.LinetypeSource);
            builder.AppendLine("ColorSource:" + attributes.ColorSource);
            builder.AppendLine("MaterialSource:" + attributes.MaterialSource);
            builder.AppendLine("ObjectColor:" + attributes.ObjectColor.ToArgb());

            var userStrings = attributes.GetUserStrings();
            if (userStrings != null)
            {
                foreach (string key in userStrings.AllKeys.OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (IsInternalUserStringKey(key))
                        continue;
                    builder.AppendLine("UserString:" + key + "=" + userStrings[key]);
                }
            }

            return builder.ToString();
        }

        private static bool IsInternalUserStringKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            return string.Equals(key, SourceFileKey, StringComparison.Ordinal) ||
                string.Equals(key, SourceBlockNameKey, StringComparison.Ordinal) ||
                string.Equals(key, SourceBlockHashKey, StringComparison.Ordinal) ||
                string.Equals(key, LocalBlockHashKey, StringComparison.Ordinal) ||
                string.Equals(key, SourceFileTimeKey, StringComparison.Ordinal);
        }

        private static string TransformToString(Transform transform)
        {
            List<string> values = new List<string>();
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                    values.Add(FormatDouble(transform[row, col]));
            }
            return string.Join(",", values);
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string GetDocLayerName(RhinoDoc doc, ObjectAttributes attributes)
        {
            if (doc == null || attributes == null || attributes.LayerIndex < 0 || attributes.LayerIndex >= doc.Layers.Count)
                return "";
            Layer layer = doc.Layers[attributes.LayerIndex];
            return layer?.FullPath ?? layer?.Name ?? "";
        }

        private static string GetFileLayerName(File3dm file, ObjectAttributes attributes)
        {
            if (file == null || attributes == null || attributes.LayerIndex < 0 || attributes.LayerIndex >= file.AllLayers.Count)
                return "";
            Layer layer = file.AllLayers.ElementAtOrDefault(attributes.LayerIndex);
            return layer?.FullPath ?? layer?.Name ?? "";
        }

        private static string GetDocLinetypeName(RhinoDoc doc, ObjectAttributes attributes)
        {
            if (doc == null || attributes == null || attributes.LinetypeIndex < 0 || attributes.LinetypeIndex >= doc.Linetypes.Count)
                return "";
            Linetype linetype = doc.Linetypes[attributes.LinetypeIndex];
            return linetype?.Name ?? "";
        }

        private static string GetFileLinetypeName(File3dm file, ObjectAttributes attributes)
        {
            if (file == null || attributes == null || attributes.LinetypeIndex < 0 || attributes.LinetypeIndex >= file.AllLinetypes.Count)
                return "";
            Linetype linetype = file.AllLinetypes.ElementAtOrDefault(attributes.LinetypeIndex);
            return linetype?.Name ?? "";
        }

        private static string GetDocMaterialName(RhinoDoc doc, ObjectAttributes attributes)
        {
            if (doc == null || attributes == null || attributes.MaterialIndex < 0 || attributes.MaterialIndex >= doc.Materials.Count)
                return "";
            Material material = doc.Materials[attributes.MaterialIndex];
            return material?.Name ?? "";
        }

        private static string GetFileMaterialName(File3dm file, ObjectAttributes attributes)
        {
            if (file == null || attributes == null || attributes.MaterialIndex < 0 || attributes.MaterialIndex >= file.AllMaterials.Count)
                return "";
            Material material = file.AllMaterials.ElementAtOrDefault(attributes.MaterialIndex);
            return material?.Name ?? "";
        }

        private static Transform GetSourceBlockInstanceTransform(File3dm file, InstanceDefinitionGeometry sourceDefinition)
        {
            if (file != null && sourceDefinition != null)
            {
                foreach (File3dmObject fileObject in file.Objects)
                {
                    if (fileObject?.Geometry is InstanceReferenceGeometry instanceReference &&
                        instanceReference.ParentIdefId == sourceDefinition.Id)
                    {
                        return instanceReference.Xform;
                    }
                }
            }

            return Transform.Identity;
        }

        private static SourceBlockInstanceInfoV2 GetSourceBlockInstanceInfoV2(RhinoDoc doc, File3dm file, InstanceDefinitionGeometry sourceDefinition, Dictionary<Guid, int> layerMap)
        {
            SourceBlockInstanceInfoV2 result = new SourceBlockInstanceInfoV2
            {
                LayerIndex = doc != null && doc.Layers.CurrentLayerIndex >= 0 ? doc.Layers.CurrentLayerIndex : 0,
                Xform = Transform.Identity,
                Attributes = null
            };

            if (doc == null)
                return result;

            if (file != null && sourceDefinition != null)
            {
                foreach (File3dmObject fileObject in file.Objects)
                {
                    if (fileObject?.Geometry is InstanceReferenceGeometry instanceReference &&
                        instanceReference.ParentIdefId == sourceDefinition.Id)
                    {
                        result.LayerIndex = GetOrCreateLayerFromFile(doc, file, fileObject.Attributes?.LayerIndex ?? -1, layerMap);
                        result.Xform = instanceReference.Xform;
                        result.Attributes = fileObject.Attributes?.Duplicate();
                        return result;
                    }
                }
            }

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

        private static InstanceDefinition ReplaceDefinition(RhinoDoc doc, File3dm file, InstanceDefinitionGeometry sourceDefinition, InstanceDefinition existingDefinition)
        {
            string tempName = GetUniqueBlockName(doc, sourceDefinition.Name + "_Import");
            int tempIndex = ImportDefinitionRecursive(doc, file, sourceDefinition, tempName, new Dictionary<Guid, int>(), new Dictionary<Guid, int>());
            InstanceDefinition tempDefinition = doc.InstanceDefinitions[tempIndex];

            List<GeometryBase> geometry = new List<GeometryBase>();
            List<ObjectAttributes> attributes = new List<ObjectAttributes>();
            RhinoObject[] objects = tempDefinition.GetObjects();
            foreach (RhinoObject obj in objects)
            {
                geometry.Add(obj.Geometry.Duplicate());
                attributes.Add(CleanAttributes(obj.Attributes.Duplicate(), doc));
            }

            if (!doc.InstanceDefinitions.ModifyGeometry(existingDefinition.Index, geometry, attributes))
                throw new InvalidOperationException("替换当前块定义失败。");

            doc.InstanceDefinitions.Delete(tempDefinition.Index, true, true);
            return doc.InstanceDefinitions.FindId(existingDefinition.Id);
        }

        private static int ImportDefinitionRecursive(RhinoDoc doc, File3dm file, InstanceDefinitionGeometry sourceDefinition, string targetName, Dictionary<Guid, int> imported, Dictionary<Guid, int> layerMap)
        {
            if (imported.TryGetValue(sourceDefinition.Id, out int importedIndex))
                return importedIndex;

            InstanceDefinition existing = doc.InstanceDefinitions.Find(targetName);
            if (existing != null)
            {
                imported[sourceDefinition.Id] = existing.Index;
                return existing.Index;
            }

            Dictionary<Guid, Guid> idMap = new Dictionary<Guid, Guid>();
            foreach (Guid childId in sourceDefinition.GetObjectIds())
            {
                File3dmObject fileObject = file.Objects.FindId(childId);
                if (fileObject?.Geometry is InstanceReferenceGeometry instanceReference)
                {
                    InstanceDefinitionGeometry childDefinition = file.AllInstanceDefinitions.FindId(instanceReference.ParentIdefId);
                    if (childDefinition == null)
                        continue;

                    string childName = childDefinition.Name;
                    InstanceDefinition docChild = doc.InstanceDefinitions.Find(childName);
                    int childIndex = docChild != null
                        ? docChild.Index
                        : ImportDefinitionRecursive(doc, file, childDefinition, childName, imported, layerMap);
                    InstanceDefinition importedChild = doc.InstanceDefinitions[childIndex];
                    idMap[childDefinition.Id] = importedChild.Id;
                }
            }

            List<GeometryBase> geometry = new List<GeometryBase>();
            List<ObjectAttributes> attributes = new List<ObjectAttributes>();
            foreach (Guid objectId in sourceDefinition.GetObjectIds())
            {
                File3dmObject fileObject = file.Objects.FindId(objectId);
                if (fileObject == null)
                    continue;

                GeometryBase duplicatedGeometry = DuplicateGeometryForCurrentDocument(doc, file, fileObject.Geometry, idMap);
                if (duplicatedGeometry == null)
                    continue;

                geometry.Add(duplicatedGeometry);
                attributes.Add(CleanAttributes(fileObject.Attributes?.Duplicate(), doc, file, layerMap));
            }

            if (geometry.Count == 0)
                throw new InvalidOperationException("块定义中没有可导入的几何：" + sourceDefinition.Name);

            int index = doc.InstanceDefinitions.Add(
                targetName,
                sourceDefinition.Description ?? "",
                Point3d.Origin,
                geometry,
                attributes);

            if (index < 0)
                throw new InvalidOperationException("导入块定义失败：" + targetName);

            imported[sourceDefinition.Id] = index;
            return index;
        }

        private static GeometryBase DuplicateGeometryForCurrentDocument(RhinoDoc doc, File3dm file, GeometryBase geometry, Dictionary<Guid, Guid> idMap)
        {
            if (geometry == null)
                return null;

            if (geometry is InstanceReferenceGeometry instanceReference)
            {
                if (!idMap.TryGetValue(instanceReference.ParentIdefId, out Guid newDefinitionId))
                    return null;
                return new InstanceReferenceGeometry(newDefinitionId, instanceReference.Xform);
            }

            GeometryBase duplicated = geometry.Duplicate();
            if (duplicated is AnnotationBase annotation)
                MapAnnotationStyle(doc, file, annotation);

            return duplicated;
        }

        private static ObjectAttributes CleanAttributes(ObjectAttributes attributes, RhinoDoc doc)
        {
            ObjectAttributes result = attributes ?? new ObjectAttributes();
            result.LayerIndex = GetValidDefinitionLayerIndex(doc, result.LayerIndex);
            result.Visible = true;
            result.Mode = ObjectMode.Normal;
            return result;
        }

        private static ObjectAttributes CleanAttributes(ObjectAttributes attributes, RhinoDoc doc, File3dm file, Dictionary<Guid, int> layerMap)
        {
            ObjectAttributes result = attributes ?? new ObjectAttributes();
            result.LayerIndex = GetOrCreateLayerFromFile(doc, file, result.LayerIndex, layerMap);
            result.LinetypeIndex = GetOrCreateLinetypeFromFile(doc, file, result.LinetypeIndex);
            result.MaterialIndex = GetOrCreateMaterialFromFile(doc, file, result.MaterialIndex);
            result.Visible = true;
            result.Mode = ObjectMode.Normal;
            return result;
        }

        private static void MapAnnotationStyle(RhinoDoc doc, File3dm file, AnnotationBase annotation)
        {
            if (doc == null || file == null || annotation == null || annotation.DimensionStyleId == Guid.Empty)
                return;

            DimensionStyle sourceStyle = file.AllDimStyles.FirstOrDefault(style => style != null && style.Id == annotation.DimensionStyleId);
            if (sourceStyle == null)
                return;

            DimensionStyle existing = doc.DimStyles.FindName(sourceStyle.Name);
            if (existing != null)
            {
                annotation.DimensionStyleId = existing.Id;
                return;
            }

            DimensionStyle duplicated = sourceStyle.Duplicate();
            int index = doc.DimStyles.Add(duplicated, false);
            if (index >= 0)
                annotation.DimensionStyleId = doc.DimStyles[index].Id;
        }

        private static int GetOrCreateLinetypeFromFile(RhinoDoc doc, File3dm file, int sourceLinetypeIndex)
        {
            if (doc == null || file == null || sourceLinetypeIndex < 0 || sourceLinetypeIndex >= file.AllLinetypes.Count)
                return -1;

            Linetype sourceLinetype = file.AllLinetypes.ElementAtOrDefault(sourceLinetypeIndex);
            if (sourceLinetype == null || string.IsNullOrWhiteSpace(sourceLinetype.Name))
                return -1;

            Linetype existing = doc.Linetypes.FindName(sourceLinetype.Name);
            if (existing != null)
                return existing.Index;

            Linetype duplicated = new Linetype(sourceLinetype);
            int index = doc.Linetypes.Add(duplicated);
            return index >= 0 ? index : -1;
        }

        private static int GetOrCreateMaterialFromFile(RhinoDoc doc, File3dm file, int sourceMaterialIndex)
        {
            if (doc == null || file == null || sourceMaterialIndex < 0 || sourceMaterialIndex >= file.AllMaterials.Count)
                return -1;

            Material sourceMaterial = file.AllMaterials.ElementAtOrDefault(sourceMaterialIndex);
            if (sourceMaterial == null)
                return -1;

            string materialName = sourceMaterial.Name ?? "";
            if (!string.IsNullOrWhiteSpace(materialName))
            {
                int existingIndex = doc.Materials.Find(materialName, true);
                if (existingIndex >= 0)
                    return existingIndex;
            }

            Material duplicated = new Material(sourceMaterial);
            int index = doc.Materials.Add(duplicated);
            return index >= 0 ? index : -1;
        }

        private static int GetValidDefinitionLayerIndex(RhinoDoc doc, int preferredLayerIndex)
        {
            if (doc != null && preferredLayerIndex >= 0 && preferredLayerIndex < doc.Layers.Count)
                return preferredLayerIndex;

            if (doc != null && doc.Layers.CurrentLayerIndex >= 0)
                return doc.Layers.CurrentLayerIndex;

            return 0;
        }

        private static string GetUniqueBlockName(RhinoDoc doc, string baseName)
        {
            string cleanBaseName = string.IsNullOrWhiteSpace(baseName) ? "ImportedBlock" : baseName;
            string name = cleanBaseName + "_Import";
            int index = 1;
            while (doc.InstanceDefinitions.Find(name) != null)
            {
                name = cleanBaseName + "_Import_" + index.ToString("000");
                index++;
            }
            return name;
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ImportRhinoBlock"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("9895CB45-259C-46E1-B343-1B409953D474"); }
        }
    }

    internal class CButton_ImportRhinoBlock_V2 : GH_ComponentAttributes
    {
        public CButton_ImportRhinoBlock_V2(ImportRhinoBlock_V2 component) : base(component) { }

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
                GH_Palette palette = ((ImportRhinoBlock_V2)Owner).CurrentButtonColor == ImportRhinoBlock_V2.ButtonColor.Black
                    ? GH_Palette.Black
                    : GH_Palette.Grey;
                using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                {
                    capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                }

                using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    graphics.DrawString("Run", font, Brushes.White, buttonRect, format);
                }
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                ImportRhinoBlock_V2 owner = (ImportRhinoBlock_V2)Owner;
                owner.CurrentButtonColor = ImportRhinoBlock_V2.ButtonColor.Grey;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                CMath.Delay(50);
                owner.CurrentButtonColor = ImportRhinoBlock_V2.ButtonColor.Black;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }
            return GH_ObjectResponse.Ignore;
        }
    }

    internal enum ImportBlockConflictChoiceV2
    {
        Cancel,
        KeepCurrent,
        ReplaceCurrent,
        RenameImported
    }

    internal struct BlockRecordStatusV2
    {
        public bool HasRecord;
        public bool SourceMatchesRecord;
        public bool SourceChanged;
        public bool SourceFileTimeChanged;
        public bool LocalChanged;
    }

    internal struct SourceBlockInstanceInfoV2
    {
        public int LayerIndex;
        public Transform Xform;
        public ObjectAttributes Attributes;
    }

    internal class BlockInsertContextV2
    {
        public RhinoDoc Doc;
        public InstanceDefinition TargetDefinition;
        public SourceBlockInstanceInfoV2 SourceInstanceInfo;
    }

    internal sealed class RhinoBlockReferenceGooV2 : GH_GeometricGoo<InstanceReferenceGeometry>, IGH_BakeAwareData, IGH_PreviewData
    {
        public RhinoBlockReferenceGooV2()
        {
        }

        public RhinoBlockReferenceGooV2(InstanceReferenceGeometry reference)
        {
            m_value = DuplicateReference(reference);
        }

        public override string TypeName
        {
            get { return "Rhino Block"; }
        }

        public override string TypeDescription
        {
            get { return "Grasshopper block reference that can be transformed before baking"; }
        }

        public override BoundingBox Boundingbox
        {
            get
            {
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null || m_value == null)
                    return BoundingBox.Empty;

                InstanceDefinition definition = doc.InstanceDefinitions.FindId(m_value.ParentIdefId);
                return GetDefinitionBoundingBox(doc, definition, m_value.Xform, new HashSet<Guid>());
            }
        }

        public override IGH_GeometricGoo DuplicateGeometry()
        {
            return new RhinoBlockReferenceGooV2(m_value);
        }

        public override BoundingBox GetBoundingBox(Transform xform)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null || m_value == null)
                return BoundingBox.Empty;

            InstanceDefinition definition = doc.InstanceDefinitions.FindId(m_value.ParentIdefId);
            return GetDefinitionBoundingBox(doc, definition, xform * m_value.Xform, new HashSet<Guid>());
        }

        public override IGH_GeometricGoo Transform(Transform xform)
        {
            if (m_value == null)
                return new RhinoBlockReferenceGooV2();

            return new RhinoBlockReferenceGooV2(new InstanceReferenceGeometry(m_value.ParentIdefId, xform * m_value.Xform));
        }

        public override IGH_GeometricGoo Morph(SpaceMorph xmorph)
        {
            return DuplicateGeometry();
        }

        public override string ToString()
        {
            if (m_value == null)
                return "<null block>";

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            InstanceDefinition definition = doc?.InstanceDefinitions.FindId(m_value.ParentIdefId);
            return definition == null ? "Rhino Block" : "Rhino Block: " + definition.Name;
        }

        BoundingBox IGH_PreviewData.ClippingBox
        {
            get { return Boundingbox; }
        }

        void IGH_PreviewData.DrawViewportWires(GH_PreviewWireArgs args)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null || m_value == null)
                return;

            InstanceDefinition definition = doc.InstanceDefinitions.FindId(m_value.ParentIdefId);
            DrawDefinitionWires(args, doc, definition, m_value.Xform, args.Color, new HashSet<Guid>());
        }

        void IGH_PreviewData.DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
        }

        bool IGH_BakeAwareData.BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid id)
        {
            id = Guid.Empty;
            if (doc == null || m_value == null)
                return false;

            InstanceDefinition definition = doc.InstanceDefinitions.FindId(m_value.ParentIdefId);
            if (definition == null)
                return false;

            ObjectAttributes attributes = att?.Duplicate() ?? doc.CreateDefaultAttributes();
            id = doc.Objects.AddInstanceObject(definition.Index, m_value.Xform, attributes);
            return id != Guid.Empty;
        }

        private static InstanceReferenceGeometry DuplicateReference(InstanceReferenceGeometry reference)
        {
            return reference == null ? null : new InstanceReferenceGeometry(reference.ParentIdefId, reference.Xform);
        }

        private static BoundingBox GetDefinitionBoundingBox(RhinoDoc doc, InstanceDefinition definition, Transform transform, HashSet<Guid> visited)
        {
            BoundingBox result = BoundingBox.Empty;
            if (doc == null || definition == null || !visited.Add(definition.Id))
                return result;

            foreach (RhinoObject child in definition.GetObjects())
            {
                BoundingBox box = GetGeometryBoundingBox(doc, child?.Geometry, transform, visited);
                if (box.IsValid)
                    result.Union(box);
            }

            visited.Remove(definition.Id);
            return result;
        }

        private static BoundingBox GetGeometryBoundingBox(RhinoDoc doc, GeometryBase geometry, Transform transform, HashSet<Guid> visited)
        {
            if (geometry == null)
                return BoundingBox.Empty;

            if (geometry is InstanceReferenceGeometry instanceReference)
            {
                InstanceDefinition nestedDefinition = doc.InstanceDefinitions.FindId(instanceReference.ParentIdefId);
                return GetDefinitionBoundingBox(doc, nestedDefinition, transform * instanceReference.Xform, visited);
            }

            GeometryBase duplicate = geometry.Duplicate();
            if (duplicate == null)
                return BoundingBox.Empty;

            duplicate.Transform(transform);
            return duplicate.GetBoundingBox(true);
        }

        private static void DrawDefinitionWires(GH_PreviewWireArgs args, RhinoDoc doc, InstanceDefinition definition, Transform transform, Color color, HashSet<Guid> visited)
        {
            if (definition == null || !visited.Add(definition.Id))
                return;

            foreach (RhinoObject child in definition.GetObjects())
                DrawGeometryWires(args, doc, child?.Geometry, transform, color, visited);

            visited.Remove(definition.Id);
        }

        private static void DrawGeometryWires(GH_PreviewWireArgs args, RhinoDoc doc, GeometryBase geometry, Transform transform, Color color, HashSet<Guid> visited)
        {
            if (geometry == null)
                return;

            if (geometry is InstanceReferenceGeometry instanceReference)
            {
                InstanceDefinition nestedDefinition = doc.InstanceDefinitions.FindId(instanceReference.ParentIdefId);
                DrawDefinitionWires(args, doc, nestedDefinition, transform * instanceReference.Xform, color, visited);
                return;
            }

            GeometryBase previewGeometry = geometry.Duplicate();
            if (previewGeometry == null)
                return;

            previewGeometry.Transform(transform);

            if (previewGeometry is Brep brep)
                args.Pipeline.DrawBrepWires(brep, color);
            else if (previewGeometry is Curve curve)
                args.Pipeline.DrawCurve(curve, color);
            else if (previewGeometry is Mesh mesh)
                args.Pipeline.DrawMeshWires(mesh, color);
            else if (previewGeometry is Rhino.Geometry.Point point)
                args.Pipeline.DrawPoint(point.Location, color);
            else if (previewGeometry is PointCloud cloud)
            {
                foreach (PointCloudItem item in cloud)
                    args.Pipeline.DrawPoint(item.Location, color);
            }
            else if (previewGeometry is TextEntity text)
                args.Pipeline.DrawText(text, color);
            else if (previewGeometry is TextDot textDot)
            {
                args.Pipeline.DrawPoint(textDot.Point, color);
                args.Pipeline.Draw2dText(textDot.Text, color, textDot.Point, false, 12);
            }
            else if (previewGeometry is Extrusion extrusion)
                args.Pipeline.DrawBrepWires(extrusion.ToBrep(), color);
            else
            {
                BoundingBox box = previewGeometry.GetBoundingBox(true);
                if (box.IsValid)
                    args.Pipeline.DrawBox(box, color);
            }
        }
    }

    internal class ImportBlockConflictDialogV2 : Form
    {
        private ImportBlockConflictChoiceV2 _choice = ImportBlockConflictChoiceV2.Cancel;

        private ImportBlockConflictDialogV2(string blockName)
        {
            Text = "块定义已存在";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(390, 185);

            Label label = new Label
            {
                Text = "当前Rhino文档中已存在同名块：\r\n" + blockName,
                AutoSize = false,
                Location = new System.Drawing.Point(12, 12),
                Size = new Size(366, 42)
            };
            Controls.Add(label);

            AddButton("保留本图中的块定义", ImportBlockConflictChoiceV2.KeepCurrent, 12, 62);
            AddButton("用导入的块替换本图的块定义", ImportBlockConflictChoiceV2.ReplaceCurrent, 12, 92);
            AddButton("两者都保留（导入块自动重命名）", ImportBlockConflictChoiceV2.RenameImported, 12, 122);
            AddButton("取消导入", ImportBlockConflictChoiceV2.Cancel, 12, 152);
        }

        public static ImportBlockConflictChoiceV2 ShowDialog(string blockName)
        {
            using (ImportBlockConflictDialogV2 dialog = new ImportBlockConflictDialogV2(blockName))
            {
                dialog.ShowDialog();
                return dialog._choice;
            }
        }

        private void AddButton(string text, ImportBlockConflictChoiceV2 choice, int x, int y)
        {
            Button button = new Button
            {
                Text = text,
                Location = new System.Drawing.Point(x, y),
                Size = new Size(366, 24),
                Tag = choice
            };
            button.Click += (sender, e) =>
            {
                _choice = (ImportBlockConflictChoiceV2)((Button)sender).Tag;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(button);
        }
    }
}
