using System;
using System.Collections.Generic;
using CommonFunction;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class SelectRhinoObjectByLayer : GH_Component
    {
        private bool _refreshScheduled = false;
        private uint _lastDocumentSerialNumber = 0;
        private string _lastDocumentPath = "";
        private bool _includeHiddenObjects;
        private bool _includeLockedObjects;

        /// <summary>
        /// Initializes a new instance of the SelectRhinoObjectByLayer class.
        /// </summary>
        public SelectRhinoObjectByLayer()
          : base("按图层选中", "按图层选中",
              "按图层选中",
              "Parrot", "Rhino")
        {
            RhinoDoc.AddRhinoObject += OnRhinoDocumentChanged;
            RhinoDoc.DeleteRhinoObject += OnRhinoDocumentChanged;
            RhinoDoc.UndeleteRhinoObject += OnRhinoDocumentChanged;
            RhinoDoc.ReplaceRhinoObject += OnRhinoDocumentChanged;
            RhinoDoc.ModifyObjectAttributes += OnRhinoObjectAttributesChanged;
            RhinoDoc.LayerTableEvent += OnRhinoLayerTableEvent;
            RhinoDoc.ActiveDocumentChanged += OnRhinoDocumentChanged;
            RhinoDoc.EndOpenDocument += OnRhinoDocumentChanged;
            Command.EndCommand += OnRhinoCommandEnded;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("图层", "图层", "图层", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "Guid", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<string> layerName = new List<string>();
            if (!DA.GetDataList(0, layerName)) { return; }

            List<Guid> result_guid = new List<Guid>();
            HashSet<Guid> resultIdSet = new HashSet<Guid>();
            RhinoDoc activeDoc = RhinoDoc.ActiveDoc;

            if (activeDoc == null)
            {
                DA.SetDataList(0, result_guid);
                return;
            }

            _lastDocumentSerialNumber = activeDoc.RuntimeSerialNumber;
            _lastDocumentPath = activeDoc.Path ?? "";

            HashSet<int> layerIndices = new HashSet<int>();
            int count = layerName.Count;
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrWhiteSpace(layerName[i]))
                    continue;

                foreach (int layerIndex in FindLayerIndices(activeDoc, layerName[i]))
                    layerIndices.Add(layerIndex);

            }

            ObjectEnumeratorSettings objectSettings = new ObjectEnumeratorSettings
            {
                NormalObjects = true,
                HiddenObjects = true,
                LockedObjects = true
            };

            foreach (var item in activeDoc.Objects.GetObjectList(objectSettings))
            {
                if (!layerIndices.Contains(item.Attributes.LayerIndex))
                    continue;

                if (item.IsHidden && !_includeHiddenObjects)
                    continue;

                if (item.IsLocked && !_includeLockedObjects)
                    continue;

                if (resultIdSet.Add(item.Id))
                    result_guid.Add(item.Id);
            }

            DA.SetDataList(0, result_guid);
        }

        private static IEnumerable<int> FindLayerIndices(RhinoDoc doc, string rawLayerName)
        {
            string layerName = rawLayerName?.Trim();
            if (doc == null || string.IsNullOrWhiteSpace(layerName))
                yield break;

            if (ContainsWildcard(layerName))
            {
                for (int i = 0; i < doc.Layers.Count; i++)
                {
                    Layer layer = doc.Layers[i];
                    if (LayerMatchesWildcard(layer, layerName))
                        yield return layer.Index;
                }

                yield break;
            }

            int layerIndex = doc.Layers.FindByFullPath(layerName, -1);//查找图层的索引号，支持 A::B::C 子图层完整路径
            if (layerIndex != -1)//如果图层存在
                yield return layerIndex;
        }

        private static bool ContainsWildcard(string value)
        {
            return value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;
        }

        private static bool LayerMatchesWildcard(Layer layer, string wildcardPattern)
        {
            if (layer == null)
                return false;

            string fullPath = layer.FullPath ?? layer.Name ?? "";
            string name = layer.Name ?? "";
            string regexPattern = "^" + Regex.Escape(wildcardPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";

            return Regex.IsMatch(fullPath, regexPattern, RegexOptions.IgnoreCase) ||
                Regex.IsMatch(name, regexPattern, RegexOptions.IgnoreCase);
        }

        private void OnRhinoDocumentChanged(object sender, EventArgs e)
        {
            RhinoDoc activeDoc = RhinoDoc.ActiveDoc;
            uint currentSerialNumber = activeDoc?.RuntimeSerialNumber ?? 0;
            string currentPath = activeDoc?.Path ?? "";

            bool documentChanged = currentSerialNumber != _lastDocumentSerialNumber ||
                !string.Equals(currentPath, _lastDocumentPath, StringComparison.OrdinalIgnoreCase);

            if (!documentChanged && _refreshScheduled)
                return;

            _lastDocumentSerialNumber = currentSerialNumber;
            _lastDocumentPath = currentPath;

            ScheduleRefresh();
        }

        private void OnRhinoObjectAttributesChanged(object sender, Rhino.DocObjects.RhinoModifyObjectAttributesEventArgs e)
        {
            int oldLayerIndex = e?.OldAttributes?.LayerIndex ?? -1;
            int newLayerIndex = e?.NewAttributes?.LayerIndex ?? -1;

            if (oldLayerIndex == newLayerIndex)
                return;

            OnRhinoDocumentChanged(sender, e);
        }

        private void OnRhinoLayerTableEvent(object sender, Rhino.DocObjects.Tables.LayerTableEventArgs e)
        {
            if (!LayerTableChangeAffectsSelection(e))
                return;

            OnRhinoDocumentChanged(sender, e);
        }

        private void OnRhinoCommandEnded(object sender, CommandEventArgs e)
        {
            if (!CommandChangesObjectVisibilityOrLocking(e))
                return;

            OnRhinoDocumentChanged(sender, e);
        }

        private static bool CommandChangesObjectVisibilityOrLocking(CommandEventArgs e)
        {
            string commandName = e?.CommandEnglishName;
            if (string.IsNullOrEmpty(commandName))
                return false;

            switch (commandName)
            {
                case "Hide":
                case "Show":
                case "HideSwap":
                case "Lock":
                case "Unlock":
                case "UnlockAll":
                case "LockSwap":
                case "Isolate":
                case "Unisolate":
                    return true;

                default:
                    return false;
            }
        }

        private static bool LayerTableChangeAffectsSelection(Rhino.DocObjects.Tables.LayerTableEventArgs e)
        {
            if (e == null)
                return false;

            switch (e.EventType)
            {
                case Rhino.DocObjects.Tables.LayerTableEventType.Added:
                case Rhino.DocObjects.Tables.LayerTableEventType.Deleted:
                case Rhino.DocObjects.Tables.LayerTableEventType.Undeleted:
                    return true;

                case Rhino.DocObjects.Tables.LayerTableEventType.Modified:
                    Layer oldLayer = e.OldState;
                    Layer newLayer = e.NewState;
                    if (oldLayer == null || newLayer == null)
                        return true;

                    return !string.Equals(oldLayer.Name, newLayer.Name, StringComparison.OrdinalIgnoreCase) ||
                        oldLayer.ParentLayerId != newLayer.ParentLayerId ||
                        oldLayer.IsVisible != newLayer.IsVisible ||
                        oldLayer.IsLocked != newLayer.IsLocked;

                default:
                    return false;
            }
        }

        private void ScheduleRefresh()
        {
            if (_refreshScheduled)
                return;

            GH_Document document = OnPingDocument();
            if (document == null)
                return;

            _refreshScheduled = true;
            document.ScheduleSolution(100, doc =>
            {
                _refreshScheduled = false;
                ExpireSolution(false);
            });
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "选中隐藏对象", (sender, args) => ToggleIncludeHiddenObjects(), true, _includeHiddenObjects);
            Menu_AppendItem(menu, "选中锁定对象", (sender, args) => ToggleIncludeLockedObjects(), true, _includeLockedObjects);
        }

        private void ToggleIncludeHiddenObjects()
        {
            _includeHiddenObjects = !_includeHiddenObjects;
            ExpireSolution(true);
        }

        private void ToggleIncludeLockedObjects()
        {
            _includeLockedObjects = !_includeLockedObjects;
            ExpireSolution(true);
        }

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter chunk = writer.CreateChunk("SelectRhinoObjectByLayerSettings");
            chunk.SetBoolean("IncludeHiddenObjects", _includeHiddenObjects);
            chunk.SetBoolean("IncludeLockedObjects", _includeLockedObjects);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            GH_IReader chunk = reader.FindChunk("SelectRhinoObjectByLayerSettings");
            if (chunk != null)
            {
                bool includeHiddenObjects = _includeHiddenObjects;
                bool includeLockedObjects = _includeLockedObjects;
                if (chunk.TryGetBoolean("IncludeHiddenObjects", ref includeHiddenObjects))
                    _includeHiddenObjects = includeHiddenObjects;
                if (chunk.TryGetBoolean("IncludeLockedObjects", ref includeLockedObjects))
                    _includeLockedObjects = includeLockedObjects;
            }

            return base.Read(reader);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            RhinoDoc.AddRhinoObject -= OnRhinoDocumentChanged;
            RhinoDoc.DeleteRhinoObject -= OnRhinoDocumentChanged;
            RhinoDoc.UndeleteRhinoObject -= OnRhinoDocumentChanged;
            RhinoDoc.ReplaceRhinoObject -= OnRhinoDocumentChanged;
            RhinoDoc.ModifyObjectAttributes -= OnRhinoObjectAttributesChanged;
            RhinoDoc.LayerTableEvent -= OnRhinoLayerTableEvent;
            RhinoDoc.ActiveDocumentChanged -= OnRhinoDocumentChanged;
            RhinoDoc.EndOpenDocument -= OnRhinoDocumentChanged;
            Command.EndCommand -= OnRhinoCommandEnded;
            base.RemovedFromDocument(document);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return GeneratedIcon.Get("gen_SelectRhinoObjectByLayer");
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("4E29B3B6-A914-4AAC-B7C6-5269F1343D00"); }
        }
    }
}
