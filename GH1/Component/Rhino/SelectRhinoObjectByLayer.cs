using System;
using System.Collections.Generic;
using CommonFunction;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using System.Text.RegularExpressions;

namespace NS_Parrot
{
    public class SelectRhinoObjectByLayer : GH_Component
    {
        private bool _refreshScheduled = false;
        private uint _lastDocumentSerialNumber = 0;
        private string _lastDocumentPath = "";

        /// <summary>
        /// Initializes a new instance of the SelectRhinoObjectByLayer class.
        /// </summary>
        public SelectRhinoObjectByLayer()
          : base("SelectRhinoObjectByLayer", "按图层选中",
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

            foreach (var item in activeDoc.Objects)
            {
                if (layerIndices.Contains(item.Attributes.LayerIndex) && resultIdSet.Add(item.Id))
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
