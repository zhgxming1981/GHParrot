using CommonFunction;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class ToSTP : GH_Component
    {
        private string _lastInputSignature;
        private string _lastStatus;
        private string _pendingRhinoFilePath;
        private bool _waitingForGuidStable;
        private bool _runRequested;
        private bool _lastRunInput;
        private int _refreshAttemptCount;
        private int _lastGuidCount = -1;
        private int _stableGuidCount;
        private int _guidCheckCount;
        private Timer _refreshTimer;

        public ToSTP()
          : base("ToSTP", "存为Stp",
              "存为Stp，并由Catia打开",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件路径", "文件路径", "需要先打开的Rhino文件路径。不接时按当前文档导出。", GH_ParamAccess.item);
            pManager.AddGenericParameter("Guid", "Guid", "几何体", GH_ParamAccess.list);
            pManager.AddTextParameter("保存路径", "保存路径", "STP保存路径", GH_ParamAccess.list);
            pManager.AddIntegerParameter("等待毫秒", "等待毫秒", "打开Rhino文件后等待Pipeline刷新的时间", GH_ParamAccess.item, 1500);
            pManager.AddBooleanParameter("执行", "执行", "导出为stp格式", GH_ParamAccess.item, false);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status", "导出状态", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            try
            {
                SolveInstanceCore(DA);
            }
            catch (Exception ex)
            {
                StopRefreshTimer();
                _waitingForGuidStable = false;
                _runRequested = false;
                _lastStatus = "异常：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastStatus);
                DA.SetData(0, _lastStatus);
            }
        }

        private void SolveInstanceCore(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string rhinoFilePath = "";
            DA.GetData(0, ref rhinoFilePath);
            rhinoFilePath = NormalizePath(rhinoFilePath);

            int waitMilliseconds = 1500;
            DA.GetData(3, ref waitMilliseconds);
            waitMilliseconds = Math.Max(0, waitMilliseconds);

            bool run = false;
            DA.GetData(4, ref run);
            bool runRisingEdge = run && !_lastRunInput;
            _lastRunInput = run;

            List<GH_Guid> guidList = new List<GH_Guid>();
            bool hasGuidList = DA.GetDataList(1, guidList);

            List<string> savePaths = new List<string>();
            bool hasSavePaths = DA.GetDataList(2, savePaths);

            string inputSignature = BuildInputSignature(rhinoFilePath, guidList, savePaths, waitMilliseconds);
            if (_lastInputSignature != inputSignature)
            {
                _lastInputSignature = inputSignature;
                if (!_waitingForGuidStable)
                {
                    _runRequested = false;
                    _lastStatus = null;
                }
            }

            if (runRisingEdge)
            {
                StopRefreshTimer();
                _runRequested = true;
                _waitingForGuidStable = false;
                _refreshAttemptCount = 0;
                ResetGuidStability();
                _pendingRhinoFilePath = rhinoFilePath;
                _lastStatus = null;
            }

            if (!_runRequested && !_waitingForGuidStable)
            {
                DA.SetData(0, _lastStatus ?? $"未执行：执行={run}，执行端接线数={Params.Input[4].Sources.Count}");
                return;
            }

            if (_runRequested && !string.IsNullOrWhiteSpace(_pendingRhinoFilePath) && !_waitingForGuidStable)
            {
                if (!File.Exists(_pendingRhinoFilePath))
                {
                    _runRequested = false;
                    _lastStatus = $"未执行：Rhino文件不存在。文件：{_pendingRhinoFilePath}";
                    DA.SetData(0, _lastStatus);
                    return;
                }

                if (!IsActiveRhinoFile(_pendingRhinoFilePath))
                {
                    _lastStatus = "正在关闭当前文件并打开：" + _pendingRhinoFilePath;
                    DA.SetData(0, _lastStatus);

                    string openDiagnostics;
                    if (!CloseCurrentFileAndOpen(_pendingRhinoFilePath, out openDiagnostics))
                    {
                        _runRequested = false;
                        _lastStatus = $"未执行：Rhino文件打开失败。{openDiagnostics}";
                        DA.SetData(0, _lastStatus);
                        return;
                    }
                }

                _waitingForGuidStable = true;
                _refreshAttemptCount = 0;
                ResetGuidStability();
                _lastStatus = "已打开文件，等待Guid刷新稳定";
                ScheduleRefresh(waitMilliseconds);
                DA.SetData(0, _lastStatus);
                return;
            }

            if (_waitingForGuidStable)
            {
                int guidCount = hasGuidList ? guidList.Count : 0;
                if (!IsGuidCountStable(guidCount))
                {
                    if (_guidCheckCount >= 20)
                    {
                        _waitingForGuidStable = false;
                        _runRequested = false;
                        _lastStatus = $"未执行：Guid数量未稳定。最后数量 {guidCount}，检测 {_guidCheckCount}/20。";
                        DA.SetData(0, _lastStatus);
                        return;
                    }

                    _lastStatus = $"等待Guid稳定：当前 {guidCount} 个，稳定 {_stableGuidCount}/3，检测 {_guidCheckCount}/20";
                    ScheduleRefresh(500);
                    DA.SetData(0, _lastStatus);
                    return;
                }
            }

            if (!hasGuidList || !hasSavePaths)
            {
                if (_waitingForGuidStable && _refreshAttemptCount < 3)
                {
                    _refreshAttemptCount++;
                    _lastStatus = "等待刷新：尚未获得Guid或保存路径";
                    ScheduleRefresh(Math.Max(300, waitMilliseconds));
                    DA.SetData(0, _lastStatus);
                    return;
                }

                _waitingForGuidStable = false;
                _runRequested = false;
                _lastStatus = "未执行：缺少Guid或保存路径";
                DA.SetData(0, _lastStatus);
                return;
            }

            ExportCurrentDocument(guidList, savePaths, out int success, out int fail);

            _waitingForGuidStable = false;
            _runRequested = false;
            _refreshAttemptCount = 0;

            string fileName = string.IsNullOrWhiteSpace(_pendingRhinoFilePath)
                ? Path.GetFileName(RhinoDoc.ActiveDoc?.Path ?? "")
                : Path.GetFileName(_pendingRhinoFilePath);

            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "当前文档";

            _lastStatus = $"当前文件：{fileName}；处理成功 {success}，失败 {fail}。";
            DA.SetData(0, _lastStatus);
        }

        private void ExportCurrentDocument(List<GH_Guid> guidList, List<string> savePaths, out int success, out int fail)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            RhinoApp.SetFocusToMainWindow();

            success = 0;
            fail = 0;
            for (int i = 0; i < guidList.Count; i++)
            {
                RhinoObject rhinoObject = doc?.Objects.FindId(guidList[i].Value);
                if (rhinoObject == null)
                {
                    fail++;
                    continue;
                }

                if (i >= savePaths.Count)
                {
                    fail++;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "保存路径数量少于实体数量");
                    continue;
                }

                string exportPath = savePaths[i];
                if (string.IsNullOrWhiteSpace(exportPath))
                {
                    fail++;
                    continue;
                }

                if (!string.Equals(Path.GetExtension(exportPath), ".stp", StringComparison.OrdinalIgnoreCase))
                    exportPath = Path.ChangeExtension(exportPath, ".stp");

                string directory = Path.GetDirectoryName(exportPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                doc.Objects.UnselectAll();
                rhinoObject.Select(true, true);
                RhinoApp.SendKeystrokes(" ", true);

                if (doc.ExportSelected(exportPath))
                {
                    success++;
                }
                else
                {
                    fail++;
                    RhinoApp.WriteLine($"导出失败: {exportPath}");
                }
            }
        }

        private string BuildInputSignature(string rhinoFilePath, List<GH_Guid> guidList, List<string> savePaths, int waitMilliseconds)
        {
            List<string> parts = new List<string>();
            parts.Add("RhinoFileSources=" + Params.Input[0].Sources.Count);
            parts.Add("RhinoFile=" + (rhinoFilePath ?? ""));
            parts.Add("GuidSources=" + Params.Input[1].Sources.Count);
            parts.Add("PathSources=" + Params.Input[2].Sources.Count);
            parts.Add("Wait=" + waitMilliseconds);
            parts.Add("GuidCount=" + guidList.Count);
            for (int i = 0; i < guidList.Count; i++)
                parts.Add(guidList[i].Value.ToString());

            parts.Add("PathCount=" + savePaths.Count);
            for (int i = 0; i < savePaths.Count; i++)
                parts.Add(savePaths[i] ?? "");

            return string.Join("|", parts);
        }

        private void ResetGuidStability()
        {
            _lastGuidCount = -1;
            _stableGuidCount = 0;
            _guidCheckCount = 0;
        }

        private bool IsGuidCountStable(int guidCount)
        {
            _guidCheckCount++;
            if (guidCount == _lastGuidCount)
            {
                _stableGuidCount++;
            }
            else
            {
                _lastGuidCount = guidCount;
                _stableGuidCount = 1;
            }

            return _stableGuidCount >= 3;
        }

        private void ScheduleRefresh(int waitMilliseconds)
        {
            StopRefreshTimer();

            _refreshTimer = new Timer();
            _refreshTimer.Interval = Math.Max(1, waitMilliseconds);
            _refreshTimer.Tick += (sender, e) =>
            {
                StopRefreshTimer();
                ExpireSolution(true);
            };
            _refreshTimer.Start();
        }

        private void StopRefreshTimer()
        {
            if (_refreshTimer == null)
                return;

            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _refreshTimer = null;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopRefreshTimer();
            base.RemovedFromDocument(document);
        }

        private bool CloseCurrentFileAndOpen(string filePath, out string diagnostics)
        {
            diagnostics = "";
            RhinoDoc currentDoc = RhinoDoc.ActiveDoc;
            if (currentDoc != null)
            {
                currentDoc.Modified = false;
                RhinoApp.RunScript("_-Close _No", false);
                RhinoApp.Wait();
            }

            string escapedPath = filePath.Replace("\"", "\"\"");
            bool commandResult = RhinoApp.RunScript("_-Open \"" + escapedPath + "\"", false);
            if (!commandResult)
            {
                diagnostics = $"命令返回False。文件存在={File.Exists(filePath)}，当前文档={NormalizePath(RhinoDoc.ActiveDoc?.Path)}";
                return false;
            }

            RhinoApp.Wait();
            if (!IsActiveRhinoFile(filePath))
            {
                diagnostics = $"命令已执行但当前文档未切换。目标={filePath}，当前文档={NormalizePath(RhinoDoc.ActiveDoc?.Path)}";
                return false;
            }

            return true;
        }

        private bool IsActiveRhinoFile(string filePath)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
                return false;

            return string.Equals(NormalizePath(doc.Path), NormalizePath(filePath), StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ToSTP"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("53E7F888-77FF-47ED-A2A4-8EEF454BB559"); }
        }
    }
}
