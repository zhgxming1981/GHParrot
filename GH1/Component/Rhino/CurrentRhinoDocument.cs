using Grasshopper.Kernel;
using Rhino;
using System;
using System.IO;

namespace NS_Parrot
{
    public class CurrentRhinoDocument : GH_Component
    {
        private uint _lastDocumentSerialNumber = 0;
        private string _lastDocumentPath = "";
        private uint _lastOpenedDocumentSerialNumber = 0;
        private string _lastOpenedFilePath = "";

        public CurrentRhinoDocument()
          : base("CurrentRhinoDocument", "当前文档",
              "获取当前活动Rhino文档的完整路径和文件名，并在活动文档改变时自动更新",
              "Parrot", "Rhino")
        {
            RhinoDoc.ActiveDocumentChanged += OnActiveDocumentChanged;
            RhinoDoc.EndOpenDocument += OnEndOpenDocument;
            RhinoDoc.EndSaveDocument += OnActiveDocumentChanged;
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("完整路径", "Path", "当前活动Rhino文档的完整路径", GH_ParamAccess.item);
            pManager.AddTextParameter("文件名", "Name", "当前活动Rhino文档的文件名", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            RhinoDoc activeDoc = RhinoDoc.ActiveDoc;
            _lastDocumentSerialNumber = activeDoc?.RuntimeSerialNumber ?? 0;

            string fullPath = GetCurrentDocumentPath(activeDoc);
            _lastDocumentPath = fullPath;
            string fileName = string.IsNullOrWhiteSpace(fullPath) ? "" : Path.GetFileName(fullPath);

            DA.SetData(0, fullPath);
            DA.SetData(1, fileName);
        }

        private void OnActiveDocumentChanged(object sender, EventArgs e)
        {
            uint currentSerialNumber = RhinoDoc.ActiveDoc?.RuntimeSerialNumber ?? 0;
            string currentPath = GetCurrentDocumentPath(RhinoDoc.ActiveDoc);

            if (currentSerialNumber == _lastDocumentSerialNumber &&
                string.Equals(currentPath, _lastDocumentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastDocumentSerialNumber = currentSerialNumber;
            _lastDocumentPath = currentPath;

            ScheduleRefresh();
        }

        private void OnEndOpenDocument(object sender, DocumentOpenEventArgs e)
        {
            _lastOpenedDocumentSerialNumber = e?.DocumentSerialNumber ?? 0;
            _lastOpenedFilePath = e?.FileName ?? "";
            OnActiveDocumentChanged(sender, e);
        }

        private string GetCurrentDocumentPath(RhinoDoc activeDoc)
        {
            if (!string.IsNullOrWhiteSpace(activeDoc?.Path))
                return activeDoc.Path;

            uint currentSerialNumber = activeDoc?.RuntimeSerialNumber ?? 0;
            if (currentSerialNumber != 0 &&
                currentSerialNumber == _lastOpenedDocumentSerialNumber &&
                !string.IsNullOrWhiteSpace(_lastOpenedFilePath))
            {
                return _lastOpenedFilePath;
            }

            return "";
        }

        private void ScheduleRefresh()
        {
            GH_Document ghDocument = OnPingDocument();
            ghDocument?.ScheduleSolution(1, doc => ExpireSolution(false));
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            RhinoDoc.ActiveDocumentChanged -= OnActiveDocumentChanged;
            RhinoDoc.EndOpenDocument -= OnEndOpenDocument;
            RhinoDoc.EndSaveDocument -= OnActiveDocumentChanged;
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_CurrentRhinoDocument"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("0D9E3313-A1D7-463F-AE83-211760636E59"); }
        }
    }
}
