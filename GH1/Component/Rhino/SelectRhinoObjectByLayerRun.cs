using CommonFunction;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Rhino;
using Rhino.DocObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class SelectRhinoObjectByLayerRun : GH_Component
    {
        private const string SettingsChunk = "SelectRhinoObjectByLayerRunSettings";
        private const float OptionTextHeight = 18.0f;

        private bool _refreshScheduled = false;
        private uint _lastDocumentSerialNumber = 0;
        private string _lastDocumentPath = "";
        private List<Guid> _lastResult = new List<Guid>();
        private bool _includeHiddenObjects;
        private bool _includeLockedObjects;
        private readonly List<string> _visibilityRuntimeMessages = new List<string>();

        public SelectRhinoObjectByLayerRun()
          : base("SelectRhinoObjectByLayerRun", "按图层选物",
              "按图层获取Rhino对象Guid，Run未接线时实时更新；接入Button时等待Button信号",
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
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("图层", "图层", "图层名，支持A::B::C完整路径和A*通配符", GH_ParamAccess.list);
            pManager.AddBooleanParameter("运行", "Run", "未接线时默认为True实时更新；接线后为True时执行", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "匹配对象的Guid", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<string> layerNames = new List<string>();
            if (!DA.GetDataList(0, layerNames))
                return;

            bool run = true;
            DA.GetData(1, ref run);
            bool runInputConnected = Params.Input.Count > 1 && Params.Input[1].SourceCount > 0;

            if (runInputConnected && !run)
            {
                AddCachedVisibilityRuntimeMessages();
                DA.SetDataList(0, _lastResult);
                return;
            }

            List<Guid> result = new List<Guid>();
            RhinoDoc activeDoc = RhinoDoc.ActiveDoc;
            if (activeDoc == null)
            {
                DA.SetDataList(0, result);
                return;
            }

            _lastDocumentSerialNumber = activeDoc.RuntimeSerialNumber;
            _lastDocumentPath = activeDoc.Path ?? "";

            HashSet<int> layerIndices = new HashSet<int>();
            foreach (string layerName in layerNames)
            {
                if (string.IsNullOrWhiteSpace(layerName))
                    continue;

                foreach (int layerIndex in FindLayerIndices(activeDoc, layerName))
                    layerIndices.Add(layerIndex);
            }

            HashSet<Guid> resultIdSet = new HashSet<Guid>();
            int hiddenCount = 0;
            int lockedCount = 0;
            int skippedHiddenCount = 0;
            int skippedLockedCount = 0;
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

                bool isHidden = item.IsHidden;
                bool isLocked = item.IsLocked;
                if (isHidden)
                    hiddenCount++;
                if (isLocked)
                    lockedCount++;

                if (isHidden && !_includeHiddenObjects)
                {
                    skippedHiddenCount++;
                    continue;
                }
                if (isLocked && !_includeLockedObjects)
                {
                    skippedLockedCount++;
                    continue;
                }

                if (resultIdSet.Add(item.Id))
                    result.Add(item.Id);
            }

            AddVisibilityRuntimeMessages(hiddenCount, lockedCount, skippedHiddenCount, skippedLockedCount);

            _lastResult = result;
            DA.SetDataList(0, result);
        }

        private void AddVisibilityRuntimeMessages(int hiddenCount, int lockedCount, int skippedHiddenCount, int skippedLockedCount)
        {
            _visibilityRuntimeMessages.Clear();

            if (skippedHiddenCount > 0)
                _visibilityRuntimeMessages.Add("已过滤隐藏对象：" + skippedHiddenCount + " 个。右键勾选“包含隐藏对象”可输出。");
            else if (hiddenCount > 0 && _includeHiddenObjects)
                _visibilityRuntimeMessages.Add("已包含隐藏对象：" + hiddenCount + " 个。");

            if (skippedLockedCount > 0)
                _visibilityRuntimeMessages.Add("已过滤锁定对象：" + skippedLockedCount + " 个。右键勾选“包含锁定对象”可输出。");
            else if (lockedCount > 0 && _includeLockedObjects)
                _visibilityRuntimeMessages.Add("已包含锁定对象：" + lockedCount + " 个。");

            AddCachedVisibilityRuntimeMessages();
        }

        private void AddCachedVisibilityRuntimeMessages()
        {
            foreach (string message in _visibilityRuntimeMessages)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "包含隐藏对象", (sender, args) => ToggleIncludeHiddenObjects(), true, _includeHiddenObjects);
            Menu_AppendItem(menu, "包含锁定对象", (sender, args) => ToggleIncludeLockedObjects(), true, _includeLockedObjects);
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

        public string OptionDisplayText
        {
            get
            {
                return "隐藏:" + (_includeHiddenObjects ? "选" : "不选") +
                    "  锁定:" + (_includeLockedObjects ? "选" : "不选");
            }
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
                        oldLayer.ParentLayerId != newLayer.ParentLayerId;

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

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter chunk = writer.CreateChunk(SettingsChunk);
            chunk.SetBoolean("IncludeHiddenObjects", _includeHiddenObjects);
            chunk.SetBoolean("IncludeLockedObjects", _includeLockedObjects);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            GH_IReader chunk = reader.FindChunk(SettingsChunk);
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

        public override void CreateAttributes()
        {
            Attributes = new SelectRhinoObjectByLayerRunAttributes(this);
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
            base.RemovedFromDocument(document);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_SelectRhinoObjectByLayerRun"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("7B8F88D7-71E2-4C2B-9365-C5C69B12D9FB"); }
        }

        internal sealed class SelectRhinoObjectByLayerRunAttributes : GH_ComponentAttributes
        {
            public SelectRhinoObjectByLayerRunAttributes(SelectRhinoObjectByLayerRun owner) : base(owner)
            {
            }

            protected override void Layout()
            {
                base.Layout();
                Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + OptionTextHeight);
            }

            protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
            {
                base.Render(canvas, graphics, channel);

                if (channel != GH_CanvasChannel.Objects)
                    return;

                SelectRhinoObjectByLayerRun owner = (SelectRhinoObjectByLayerRun)Owner;
                RectangleF textRect = new RectangleF(Bounds.X + 3.0f, Bounds.Bottom - OptionTextHeight + 1.0f, Bounds.Width - 6.0f, OptionTextHeight - 2.0f);

                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    graphics.DrawString(owner.OptionDisplayText, GH_FontServer.Small, Brushes.DimGray, textRect, format);
            }
        }
    }
}
