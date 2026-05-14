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
    public class ImportRhinoBlock : GH_Component
    {
        public enum ButtonColor { Black, Grey }

        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        internal bool ButtonRun { get; set; }

        private bool _lastInputRun;
        private Guid _lastGuid = Guid.Empty;
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

        private const string SourceFileKey = "Parrot.ImportRhinoBlock.SourceFile";
        private const string SourceBlockNameKey = "Parrot.ImportRhinoBlock.SourceBlockName";
        private const string SourceBlockHashKey = "Parrot.ImportRhinoBlock.SourceBlockHash";
        private const string LocalBlockHashKey = "Parrot.ImportRhinoBlock.LocalBlockHash";
        private const string SourceFileTimeKey = "Parrot.ImportRhinoBlock.SourceFileLastWriteUtc";

        public ImportRhinoBlock()
          : base("\u5bfc\u5165Rhino\u5757", "ImportRhinoBlock",
              "\u4ece\u672a\u6253\u5f00\u7684Rhino\u6587\u4ef6\u5bfc\u5165\u6307\u5b9a\u5757\uff0c\u5e76\u5728\u5f53\u524dRhino\u4e2d\u65b0\u589e\u4e00\u4e2a\u5757\u5b9e\u4f8b",
              "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("\u6587\u4ef6\u8def\u5f84", "File", "Rhino\u6587\u4ef6\u8def\u5f84", GH_ParamAccess.item);
            pManager.AddTextParameter("\u5757\u540d", "Block", "\u8981\u5bfc\u5165\u7684\u5757\u540d", GH_ParamAccess.item);
            pManager.AddPlaneParameter("\u5de5\u4f5c\u5e73\u9762", "Plane", "\u5757\u5b9e\u4f8b\u63d2\u5165\u7684\u5de5\u4f5c\u5e73\u9762", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddBooleanParameter("Run", "Run", "\u6267\u884c\u5bfc\u5165\u5e76\u63d2\u5165\u5757\u5b9e\u4f8b", GH_ParamAccess.item, false);
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "\u65b0\u589e\u5757\u5b9e\u4f8b\u7684Guid", GH_ParamAccess.item);
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_ImportRhinoBlock(this);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string filePath = "";
            string blockName = "";
            bool inputRun = false;
            Plane workPlane = Plane.WorldXY;

            if (!DA.GetData(0, ref filePath)) { return; }
            DA.GetData(1, ref blockName);
            DA.GetData(2, ref workPlane);
            DA.GetData(3, ref inputRun);

            ConfigureBlockFileWatcher(filePath);
            bool blockListChanged = RefreshBlockNameCache(filePath);
            _lastFilePath = filePath;

            if (string.IsNullOrWhiteSpace(blockName))
                blockName = _selectedBlockName;

            if (blockListChanged && string.IsNullOrWhiteSpace(blockName) && _blockNames.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "\u5916\u90e8Rhino\u6587\u4ef6\u7684\u5757\u5217\u8868\u5df2\u66f4\u65b0\uff0c\u8bf7\u91cd\u65b0\u9009\u62e9\u8981\u5bfc\u5165\u7684\u5757\u3002");

            UpdatePreview(filePath, blockName, workPlane);

            bool shouldRun = ButtonRun || (inputRun && !_lastInputRun);
            _lastInputRun = inputRun;
            ButtonRun = false;

            if (!shouldRun)
            {
                CheckReferencedBlockChanges(filePath, blockName);
                if (_lastGuid != Guid.Empty)
                    DA.SetData(0, new GH_Guid(_lastGuid));
                return;
            }

            try
            {
                RefreshBlockNameCache(filePath, true);
                if (string.IsNullOrWhiteSpace(blockName))
                    blockName = _selectedBlockName;
                Guid guid = ImportAndInsert(filePath, blockName, workPlane);
                _lastGuid = guid;
                if (guid != Guid.Empty)
                    DA.SetData(0, new GH_Guid(guid));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            ToolStripMenuItem root = new ToolStripMenuItem("\u5757\u540d");
            menu.Items.Add(root);

            RefreshBlockNameCache(_lastFilePath);

            if (string.IsNullOrWhiteSpace(_lastFilePath))
            {
                root.DropDownItems.Add(new ToolStripMenuItem("\u8bf7\u5148\u8f93\u5165\u6587\u4ef6\u8def\u5f84") { Enabled = false });
                return;
            }

            if (_blockNames.Count == 0)
            {
                root.DropDownItems.Add(new ToolStripMenuItem("\u672a\u627e\u5230\u5757\u540d") { Enabled = false });
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
            if (_lastGuid != Guid.Empty)
                writer.SetString("LastGuid", _lastGuid.ToString());

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
            if (reader.TryGetString("LastGuid", ref guidText) && Guid.TryParse(guidText, out Guid guid))
                _lastGuid = guid;

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
        }

        private void UpdatePreview(string filePath, string blockName, Plane workPlane)
        {
            _previewGeometry.Clear();
            _previewBox = BoundingBox.Empty;

            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(blockName) || !File.Exists(filePath))
                return;

            File3dm file = File3dm.Read(filePath);
            InstanceDefinitionGeometry definition = file?.AllInstanceDefinitions.FindName(blockName);
            if (definition == null)
                return;

            Transform transform = GetInsertionTransform(workPlane) * GetSourceBlockInstanceTransform(file, definition);
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

        private Guid ImportAndInsert(string filePath, string blockName, Plane workPlane)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Rhino\u6587\u4ef6\u8def\u5f84\u4e3a\u7a7a\u3002");
            if (!File.Exists(filePath))
                throw new FileNotFoundException("\u627e\u4e0d\u5230Rhino\u6587\u4ef6\u3002", filePath);
            if (string.IsNullOrWhiteSpace(blockName))
                throw new ArgumentException("\u5757\u540d\u4e3a\u7a7a\u3002");

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
                throw new InvalidOperationException("\u5f53\u524d\u6ca1\u6709\u53ef\u7528\u7684Rhino\u6587\u6863\u3002");

            File3dm file = File3dm.Read(filePath);
            if (file == null)
                throw new InvalidOperationException("\u65e0\u6cd5\u8bfb\u53d6Rhino\u6587\u4ef6\u3002");

            InstanceDefinitionGeometry sourceDefinition = file.AllInstanceDefinitions.FindName(blockName);
            if (sourceDefinition == null)
                throw new InvalidOperationException("\u5728\u5916\u90e8Rhino\u6587\u4ef6\u4e2d\u627e\u4e0d\u5230\u6307\u5b9a\u5757\uff1a" + blockName);

            InstanceDefinition existingDefinition = doc.InstanceDefinitions.Find(blockName);
            InstanceDefinition targetDefinition = existingDefinition;
            string sourceHash = BuildFileDefinitionHash(file, sourceDefinition);
            Dictionary<Guid, int> layerMap = new Dictionary<Guid, int>();
            SourceBlockInstanceInfo sourceInstanceInfo = GetSourceBlockInstanceInfo(doc, file, sourceDefinition, layerMap);

            if (existingDefinition != null)
            {
                BlockRecordStatus status = GetBlockRecordStatus(doc, existingDefinition, sourceHash, filePath);
                ReportRecordStatus(status);

                if (!status.SourceFileTimeChanged && (status.SourceMatchesRecord || AreBlockDefinitionsSame(doc, file, sourceDefinition, existingDefinition)))
                {
                    targetDefinition = existingDefinition;
                    RecordBlockImportMetadata(targetDefinition, filePath, blockName, sourceHash, BuildDocDefinitionHash(doc, targetDefinition));
                }
                else
                {
                    ImportBlockConflictChoice choice = ImportBlockConflictDialog.ShowDialog(blockName);
                    if (choice == ImportBlockConflictChoice.Cancel)
                        return Guid.Empty;

                    if (choice == ImportBlockConflictChoice.KeepCurrent)
                    {
                        targetDefinition = existingDefinition;
                    }
                    else if (choice == ImportBlockConflictChoice.RenameImported)
                    {
                        string newName = GetUniqueBlockName(doc, blockName);
                        int index = ImportDefinitionRecursive(doc, file, sourceDefinition, newName, new Dictionary<Guid, int>(), layerMap);
                        targetDefinition = doc.InstanceDefinitions[index];
                        RecordBlockImportMetadata(targetDefinition, filePath, blockName, sourceHash, BuildDocDefinitionHash(doc, targetDefinition));
                    }
                    else if (choice == ImportBlockConflictChoice.ReplaceCurrent)
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
                throw new InvalidOperationException("\u5757\u5b9a\u4e49\u5bfc\u5165\u5931\u8d25\u3002");

            ObjectAttributes instanceAttributes = sourceInstanceInfo.Attributes?.Duplicate() ?? doc.CreateDefaultAttributes();
            instanceAttributes.Visible = true;
            instanceAttributes.Mode = ObjectMode.Normal;
            instanceAttributes.LayerIndex = sourceInstanceInfo.LayerIndex;

            Transform insertionTransform = GetInsertionTransform(workPlane) * sourceInstanceInfo.Xform;
            Guid insertedId = doc.Objects.AddInstanceObject(targetDefinition.Index, insertionTransform, instanceAttributes);
            if (insertedId == Guid.Empty)
                throw new InvalidOperationException("\u5757\u5b9e\u4f8b\u63d2\u5165\u5931\u8d25\u3002");

            RhinoObject insertedObject = doc.Objects.FindId(insertedId);
            insertedObject?.Select(true);
            doc.Views.Redraw();
            return insertedId;
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
            BlockRecordStatus status = GetBlockRecordStatus(doc, existingDefinition, sourceHash, filePath);
            ReportRecordStatus(status);
        }

        private void ReportRecordStatus(BlockRecordStatus status)
        {
            if (status.SourceChanged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "\u5916\u90e8Rhino\u6587\u4ef6\u4e2d\u7684\u5757\u5b9a\u4e49\u5df2\u4fee\u6539\u3002");
            if (status.SourceFileTimeChanged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "\u5916\u90e8Rhino\u6587\u4ef6\u5df2\u91cd\u65b0\u4fdd\u5b58\uff0c\u8bf7\u786e\u8ba4\u662f\u5426\u9700\u8981\u66ff\u6362\u672c\u56fe\u5757\u5b9a\u4e49\u3002");
            if (status.LocalChanged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "\u5f53\u524dRhino\u6587\u6863\u4e2d\u7684\u5757\u5b9a\u4e49\u5df2\u4fee\u6539\u3002");
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

        private static BlockRecordStatus GetBlockRecordStatus(RhinoDoc doc, InstanceDefinition existingDefinition, string sourceHash, string sourceFilePath)
        {
            string recordedSourceHash = existingDefinition.GetUserString(SourceBlockHashKey);
            string recordedLocalHash = existingDefinition.GetUserString(LocalBlockHashKey);
            string recordedSourceFileTime = existingDefinition.GetUserString(SourceFileTimeKey);
            string currentLocalHash = BuildDocDefinitionHash(doc, existingDefinition);
            string currentSourceFileTime = GetSourceFileLastWriteUtc(sourceFilePath);
            bool sourceMatchesCurrent = string.Equals(sourceHash, currentLocalHash, StringComparison.Ordinal);

            return new BlockRecordStatus
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

        private static SourceBlockInstanceInfo GetSourceBlockInstanceInfo(RhinoDoc doc, File3dm file, InstanceDefinitionGeometry sourceDefinition, Dictionary<Guid, int> layerMap)
        {
            SourceBlockInstanceInfo result = new SourceBlockInstanceInfo
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
                throw new InvalidOperationException("\u66ff\u6362\u5f53\u524d\u5757\u5b9a\u4e49\u5931\u8d25\u3002");

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

                GeometryBase duplicatedGeometry = DuplicateGeometryForCurrentDocument(fileObject.Geometry, idMap);
                if (duplicatedGeometry == null)
                    continue;

                geometry.Add(duplicatedGeometry);
                attributes.Add(CleanAttributes(fileObject.Attributes?.Duplicate(), doc, file, layerMap));
            }

            if (geometry.Count == 0)
                throw new InvalidOperationException("\u5757\u5b9a\u4e49\u4e2d\u6ca1\u6709\u53ef\u5bfc\u5165\u7684\u51e0\u4f55\uff1a" + sourceDefinition.Name);

            int index = doc.InstanceDefinitions.Add(
                targetName,
                sourceDefinition.Description ?? "",
                Point3d.Origin,
                geometry,
                attributes);

            if (index < 0)
                throw new InvalidOperationException("\u5bfc\u5165\u5757\u5b9a\u4e49\u5931\u8d25\uff1a" + targetName);

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
            result.Visible = true;
            result.Mode = ObjectMode.Normal;
            return result;
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
            get { return null; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("4D6A2F06-253E-4E6E-9B44-70AB3C5346A0"); }
        }
    }

    internal class CButton_ImportRhinoBlock : GH_ComponentAttributes
    {
        public CButton_ImportRhinoBlock(ImportRhinoBlock component) : base(component) { }

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
                GH_Palette palette = ((ImportRhinoBlock)Owner).CurrentButtonColor == ImportRhinoBlock.ButtonColor.Black
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
                ImportRhinoBlock owner = (ImportRhinoBlock)Owner;
                owner.CurrentButtonColor = ImportRhinoBlock.ButtonColor.Grey;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                CMath.Delay(50);
                owner.CurrentButtonColor = ImportRhinoBlock.ButtonColor.Black;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }
            return GH_ObjectResponse.Ignore;
        }
    }

    internal enum ImportBlockConflictChoice
    {
        Cancel,
        KeepCurrent,
        ReplaceCurrent,
        RenameImported
    }

    internal struct BlockRecordStatus
    {
        public bool HasRecord;
        public bool SourceMatchesRecord;
        public bool SourceChanged;
        public bool SourceFileTimeChanged;
        public bool LocalChanged;
    }

    internal struct SourceBlockInstanceInfo
    {
        public int LayerIndex;
        public Transform Xform;
        public ObjectAttributes Attributes;
    }

    internal class ImportBlockConflictDialog : Form
    {
        private ImportBlockConflictChoice _choice = ImportBlockConflictChoice.Cancel;

        private ImportBlockConflictDialog(string blockName)
        {
            Text = "\u5757\u5b9a\u4e49\u5df2\u5b58\u5728";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(390, 185);

            Label label = new Label
            {
                Text = "\u5f53\u524dRhino\u6587\u6863\u4e2d\u5df2\u5b58\u5728\u540c\u540d\u5757\uff1a\r\n" + blockName,
                AutoSize = false,
                Location = new System.Drawing.Point(12, 12),
                Size = new Size(366, 42)
            };
            Controls.Add(label);

            AddButton("\u4fdd\u7559\u672c\u56fe\u4e2d\u7684\u5757\u5b9a\u4e49", ImportBlockConflictChoice.KeepCurrent, 12, 62);
            AddButton("\u7528\u5bfc\u5165\u7684\u5757\u66ff\u6362\u672c\u56fe\u7684\u5757\u5b9a\u4e49", ImportBlockConflictChoice.ReplaceCurrent, 12, 92);
            AddButton("\u4e24\u8005\u90fd\u4fdd\u7559\uff08\u5bfc\u5165\u5757\u81ea\u52a8\u91cd\u547d\u540d\uff09", ImportBlockConflictChoice.RenameImported, 12, 122);
            AddButton("\u53d6\u6d88\u5bfc\u5165", ImportBlockConflictChoice.Cancel, 12, 152);
        }

        public static ImportBlockConflictChoice ShowDialog(string blockName)
        {
            using (ImportBlockConflictDialog dialog = new ImportBlockConflictDialog(blockName))
            {
                dialog.ShowDialog();
                return dialog._choice;
            }
        }

        private void AddButton(string text, ImportBlockConflictChoice choice, int x, int y)
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
                _choice = (ImportBlockConflictChoice)((Button)sender).Tag;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(button);
        }
    }
}
