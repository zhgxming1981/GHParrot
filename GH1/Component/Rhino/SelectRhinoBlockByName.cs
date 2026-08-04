using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CommonFunction;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;

namespace NS_Parrot
{
    public class SelectRhinoBlockByName : GH_Component
    {
        private bool _refreshScheduled;
        private uint _lastDocumentSerialNumber;
        private string _lastDocumentPath = "";

        public SelectRhinoBlockByName()
          : base("按块名选块", "按块名选块",
              "按块名和图层筛选当前Rhino模型中的块实例，输入支持正则表达式",
              "Parrot", "Rhino")
        {
            RhinoDoc.AddRhinoObject += OnRhinoDocumentChanged;
            RhinoDoc.DeleteRhinoObject += OnRhinoDocumentChanged;
            RhinoDoc.UndeleteRhinoObject += OnRhinoDocumentChanged;
            RhinoDoc.ReplaceRhinoObject += OnRhinoDocumentChanged;
            RhinoDoc.ModifyObjectAttributes += OnRhinoObjectAttributesChanged;
            RhinoDoc.LayerTableEvent += OnRhinoLayerTableEvent;
            RhinoDoc.InstanceDefinitionTableEvent += OnRhinoInstanceDefinitionTableEvent;
            RhinoDoc.ActiveDocumentChanged += OnRhinoDocumentChanged;
            RhinoDoc.EndOpenDocument += OnRhinoDocumentChanged;
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("块名", "Block", "块名或块名正则表达式", GH_ParamAccess.list);
            pManager.AddTextParameter("图层", "Layer", "图层名、完整图层路径或图层正则表达式；为空时不过滤图层", GH_ParamAccess.list);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "匹配到的块实例Guid", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<string> blockPatterns = new List<string>();
            List<string> layerPatterns = new List<string>();
            if (!DA.GetDataList(0, blockPatterns))
                return;
            DA.GetDataList(1, layerPatterns);

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            List<Guid> result = new List<Guid>();
            if (doc == null)
            {
                DA.SetDataList(0, result);
                return;
            }

            _lastDocumentSerialNumber = doc.RuntimeSerialNumber;
            _lastDocumentPath = doc.Path ?? "";

            List<Regex> blockRegexes;
            List<Regex> layerRegexes;
            if (!TryCreateRegexes(blockPatterns, "块名", out blockRegexes) ||
                !TryCreateRegexes(layerPatterns, "图层", out layerRegexes))
            {
                DA.SetDataList(0, result);
                return;
            }

            if (blockRegexes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "块名不能为空。");
                DA.SetDataList(0, result);
                return;
            }

            ObjectEnumeratorSettings objectSettings = new ObjectEnumeratorSettings
            {
                ActiveObjects = true,
                HiddenObjects = true,
                LockedObjects = true,
                ObjectTypeFilter = ObjectType.InstanceReference
            };

            foreach (RhinoObject obj in doc.Objects.GetObjectList(objectSettings))
            {
                if (!(obj is InstanceObject instanceObject))
                    continue;

                InstanceDefinition definition = instanceObject.InstanceDefinition;
                string blockName = definition?.Name ?? "";
                if (!MatchesAny(blockRegexes, blockName))
                    continue;

                if (layerRegexes.Count > 0 && !ObjectLayerMatches(doc, obj, layerRegexes))
                    continue;

                result.Add(instanceObject.Id);
            }

            DA.SetDataList(0, result);
        }

        private bool TryCreateRegexes(IEnumerable<string> patterns, string inputName, out List<Regex> regexes)
        {
            regexes = new List<Regex>();
            if (patterns == null)
                return true;

            foreach (string rawPattern in patterns)
            {
                string pattern = rawPattern?.Trim();
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                try
                {
                    regexes.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                }
                catch (ArgumentException ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, inputName + "正则表达式无效：" + pattern + "；" + ex.Message);
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesAny(IEnumerable<Regex> regexes, string value)
        {
            string text = value ?? "";
            foreach (Regex regex in regexes)
            {
                if (regex.IsMatch(text))
                    return true;
            }

            return false;
        }

        private static bool ObjectLayerMatches(RhinoDoc doc, RhinoObject obj, IEnumerable<Regex> layerRegexes)
        {
            if (doc == null || obj?.Attributes == null)
                return false;

            int layerIndex = obj.Attributes.LayerIndex;
            if (layerIndex < 0 || layerIndex >= doc.Layers.Count)
                return false;

            Layer layer = doc.Layers[layerIndex];
            string fullPath = layer?.FullPath ?? "";
            string name = layer?.Name ?? "";

            foreach (Regex regex in layerRegexes)
            {
                if (regex.IsMatch(fullPath) || regex.IsMatch(name))
                    return true;
            }

            return false;
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
            OnRhinoDocumentChanged(sender, e);
        }

        private void OnRhinoInstanceDefinitionTableEvent(object sender, Rhino.DocObjects.Tables.InstanceDefinitionTableEventArgs e)
        {
            OnRhinoDocumentChanged(sender, e);
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

        public override void RemovedFromDocument(GH_Document document)
        {
            RhinoDoc.AddRhinoObject -= OnRhinoDocumentChanged;
            RhinoDoc.DeleteRhinoObject -= OnRhinoDocumentChanged;
            RhinoDoc.UndeleteRhinoObject -= OnRhinoDocumentChanged;
            RhinoDoc.ReplaceRhinoObject -= OnRhinoDocumentChanged;
            RhinoDoc.ModifyObjectAttributes -= OnRhinoObjectAttributesChanged;
            RhinoDoc.LayerTableEvent -= OnRhinoLayerTableEvent;
            RhinoDoc.InstanceDefinitionTableEvent -= OnRhinoInstanceDefinitionTableEvent;
            RhinoDoc.ActiveDocumentChanged -= OnRhinoDocumentChanged;
            RhinoDoc.EndOpenDocument -= OnRhinoDocumentChanged;
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_Block"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("2FBAC67E-6408-4A6C-9FD6-0BB016BCCF40"); }
        }
    }
}
