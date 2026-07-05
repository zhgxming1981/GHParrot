using CommonFunction;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class To3DM : GH_Component
    {
        private const string SettingsChunk = "ExportModelSettings";
        private const int GuidStableRefreshMilliseconds = 500;

        private readonly List<string> _autoFilePaths = new List<string>();
        private string _lastInputSignature;
        private string _lastStatus;
        private string _pendingRhinoFilePath;
        private bool _exportStp = true;
        private bool _export3dm = true;
        private bool _autoRunning;
        private bool _waitingForGuidStable;
        private bool _runRequested;
        private bool _buttonRunRequested;
        private bool _stopRequested;
        private bool _paused;
        private bool _continueRequested;
        private int _autoIndex;
        private int _autoSuccess;
        private int _autoFail;
        private int _autoRecordSuccess;
        private int _autoRecordFail;
        private int _refreshAttemptCount;
        private string _lastExportInputSignature;
        private int _stableExportInputCount;
        private int _exportInputCheckCount;
        private int _refreshScheduleToken;
        private Timer _refreshTimer;
        private Timer _rhinoActionTimer;
        private int _rhinoActionToken;

        public To3DM()
          : base("导出模型", "导出模型",
              "将指定 Rhino 对象导出为 STP 或 .3dm，并可记录模型导出时间",
              "Parrot", "Modeling")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件路径", "文件路径", "Rhino 文件路径。自动执行时可输入多个文件路径，并依次打开导出；不接时按当前文档导出。", GH_ParamAccess.list);
            pManager.AddGenericParameter("Guid", "Guid", "要导出的 Rhino 对象 Guid 列表。", GH_ParamAccess.list);
            pManager.AddTextParameter("保存路径", "保存路径", "不带后缀名的保存路径列表。程序会按右键选项自动追加 .stp 或 .3dm。", GH_ParamAccess.list);
#if MODEL_EXPORT_DATABASE
            pManager.AddTextParameter("数据库地址", "数据库", "预留 SQLite 数据库文件地址；为空时只导出，不记录。", GH_ParamAccess.item);
            pManager.AddTextParameter("模型编号", "模型编号", "预留模型编号；填写数据库地址时用于记录。", GH_ParamAccess.list);
#endif
            pManager.AddTextParameter("文件名", "文件名", "导出文件名，不带后缀名。最终路径为：保存路径\\当前Rhino文件名\\文件名.stp/.3dm。", GH_ParamAccess.list);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
#if MODEL_EXPORT_DATABASE
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
#else
            pManager[3].Optional = true;
#endif
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status", "导出状态。", GH_ParamAccess.item);
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
                StopRhinoActionTimer();
                _autoRunning = false;
                _waitingForGuidStable = false;
                _runRequested = false;
                _buttonRunRequested = false;
                _paused = false;
                _continueRequested = false;
                _lastStatus = "异常：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastStatus);
                DA.SetData(0, _lastStatus);
            }
        }

        private void SolveInstanceCore(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<string> rhinoFilePaths = new List<string>();
            DA.GetDataList(0, rhinoFilePaths);
            rhinoFilePaths = rhinoFilePaths.Select(NormalizePath).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();

            List<GH_Guid> guidList = new List<GH_Guid>();
            bool hasGuidList = DA.GetDataList(1, guidList);

            List<string> savePaths = new List<string>();
            bool hasSavePaths = DA.GetDataList(2, savePaths);

#if MODEL_EXPORT_DATABASE
            string databasePath = "";
            DA.GetData(3, ref databasePath);
            databasePath = NormalizePath(databasePath);

            List<string> modelCodes = new List<string>();
            DA.GetDataList(4, modelCodes);

            List<string> fileNames = new List<string>();
            bool hasFileNames = DA.GetDataList(5, fileNames);
#else
            string databasePath = "";
            List<string> modelCodes = new List<string>();
            List<string> fileNames = new List<string>();
            bool hasFileNames = DA.GetDataList(3, fileNames);
#endif

            string inputSignature = BuildInputSignature(rhinoFilePaths, guidList, savePaths, databasePath, modelCodes, fileNames);
            if (_lastInputSignature != inputSignature)
            {
                _lastInputSignature = inputSignature;
                if (!_waitingForGuidStable && !_autoRunning)
                {
                    _runRequested = false;
                    _lastStatus = null;
                }
            }

            if (_stopRequested && (_autoRunning || _runRequested || _waitingForGuidStable))
            {
                StopAutoExecution(DA);
                return;
            }

            if (_continueRequested)
            {
                _continueRequested = false;
                ContinueAutoExecution();
            }

            if (_buttonRunRequested)
            {
                _buttonRunRequested = false;
                _stopRequested = false;
                _paused = false;
                _continueRequested = false;
                StopRefreshTimer();
                StartAutoExecution(rhinoFilePaths);
            }

            if (!_runRequested && !_waitingForGuidStable)
            {
                DA.SetData(0, _lastStatus ?? "未执行：点击 Run 开始导出。");
                return;
            }

            if (!_exportStp && !_export3dm)
            {
                StopExecutionWithError(DA, "未执行：请在右键菜单至少勾选一种导出格式：导出 STP 或导出 .3dm。");
                return;
            }

            if (_runRequested && !string.IsNullOrWhiteSpace(_pendingRhinoFilePath) && !_waitingForGuidStable)
            {
                if (!File.Exists(_pendingRhinoFilePath))
                {
                    HandleCurrentFileFinished(0, 1, 0, 0, DA, "未执行：Rhino 文件不存在。文件：" + _pendingRhinoFilePath);
                    return;
                }

                if (!IsActiveRhinoFile(_pendingRhinoFilePath))
                {
                    _lastStatus = BuildCurrentPrefix() + "正在关闭当前文件并打开：" + _pendingRhinoFilePath;
                    DA.SetData(0, _lastStatus);
                    string filePathToOpen = _pendingRhinoFilePath;
                    ScheduleRhinoAction(1, () =>
                    {
                        string openDiagnostics;
                        if (!CloseCurrentFileAndOpen(filePathToOpen, out openDiagnostics))
                        {
                            CompleteCurrentFile(0, 1, 0, 0, "未执行：Rhino 文件打开失败。" + openDiagnostics);
                            ScheduleRefresh(1);
                            return;
                        }

                        _waitingForGuidStable = true;
                        _refreshAttemptCount = 0;
                        ResetExportInputStability();
                        _lastStatus = BuildCurrentPrefix() + "已打开文件，等待当前文件输入刷新稳定";
                        ScheduleRefresh(GuidStableRefreshMilliseconds);
                    });
                    return;
                }

                _waitingForGuidStable = true;
                _refreshAttemptCount = 0;
                ResetExportInputStability();
                _lastStatus = BuildCurrentPrefix() + "已打开文件，等待当前文件输入刷新稳定";
                ScheduleRefresh(GuidStableRefreshMilliseconds);
                DA.SetData(0, _lastStatus);
                return;
            }

            if (_waitingForGuidStable)
            {
                if (!IsExportInputStable(guidList, savePaths, modelCodes, fileNames))
                {
                    if (_exportInputCheckCount >= 30)
                    {
                        HandleCurrentFileFinished(0, 1, 0, 0, DA, $"未执行：当前文件输入未稳定。Guid {guidList.Count} 个，保存路径 {savePaths.Count} 个，文件名 {fileNames.Count} 个，检测 {_exportInputCheckCount}/30。");
                        return;
                    }

                    _lastStatus = BuildCurrentPrefix() + $"等待当前文件输入稳定：Guid {guidList.Count} 个，保存路径 {savePaths.Count} 个，文件名 {fileNames.Count} 个，稳定 {_stableExportInputCount}/3，检测 {_exportInputCheckCount}/30";
                    ScheduleRefresh(GuidStableRefreshMilliseconds);
                    DA.SetData(0, _lastStatus);
                    return;
                }
            }

            if (!hasGuidList || !hasSavePaths || !hasFileNames)
            {
                if (_waitingForGuidStable && _refreshAttemptCount < 3)
                {
                    _refreshAttemptCount++;
                    _lastStatus = BuildCurrentPrefix() + "等待刷新：尚未获得 Guid、保存路径或文件名";
                    ScheduleRefresh(GuidStableRefreshMilliseconds);
                    DA.SetData(0, _lastStatus);
                    return;
                }

                HandleCurrentFileFinished(0, 1, 0, 0, DA, "未执行：缺少 Guid、保存路径或文件名");
                return;
            }

            if (_autoRunning && savePaths.Count != 1 && guidList.Count != savePaths.Count)
            {
                HandleCurrentFileFinished(0, 1, 0, 0, DA, $"未执行：自动执行要求保存路径为 1 个根目录，或与当前文件的 Guid 数量一致。当前 Guid {guidList.Count} 个，保存路径 {savePaths.Count} 个。");
                return;
            }

            if (guidList.Count != fileNames.Count)
            {
                HandleCurrentFileFinished(0, 1, 0, 0, DA, $"未执行：文件名数量必须与当前文件的 Guid 数量一致。当前 Guid {guidList.Count} 个，文件名 {fileNames.Count} 个。");
                return;
            }

            List<GH_Guid> exportGuidList = guidList.ToList();
            List<string> exportSavePaths = savePaths.ToList();
            List<string> exportFileNames = fileNames.ToList();
            List<string> exportModelCodes = modelCodes.ToList();
            string exportDatabasePath = databasePath;

            _lastStatus = BuildCurrentPrefix() + "正在导出当前文件";
            DA.SetData(0, _lastStatus);
            ScheduleRhinoAction(1, () =>
            {
                ExportCurrentDocument(exportGuidList, exportSavePaths, exportFileNames, exportDatabasePath, exportModelCodes, out int success, out int fail, out int recordSuccess, out int recordFail);
                CompleteCurrentFile(success, fail, recordSuccess, recordFail, null);
                ScheduleRefresh(1);
            });
            return;
        }

        private void StartSingleExecution(string rhinoFilePath)
        {
            _autoRunning = false;
            _autoFilePaths.Clear();
            _autoIndex = 0;
            BeginRunForFile(rhinoFilePath);
        }

        private void StartAutoExecution(List<string> rhinoFilePaths)
        {
            _paused = false;
            _stopRequested = false;
            _continueRequested = false;
            _autoFilePaths.Clear();
            _autoFilePaths.AddRange(rhinoFilePaths);
            _autoIndex = 0;
            _autoSuccess = 0;
            _autoFail = 0;
            _autoRecordSuccess = 0;
            _autoRecordFail = 0;

            if (_autoFilePaths.Count == 0)
            {
                _autoRunning = false;
                BeginRunForFile("");
                return;
            }

            _autoRunning = true;
            BeginRunForFile(_autoFilePaths[0]);
        }

        private void ContinueAutoExecution()
        {
            if (!_paused)
                return;

            _paused = false;
            _stopRequested = false;
            StopRefreshTimer();
            StopRhinoActionTimer();

            if (_autoFilePaths.Count > 0)
            {
                if (_autoIndex >= _autoFilePaths.Count)
                {
                    _autoRunning = false;
                    _lastStatus = "已全部完成，无需继续。";
                    return;
                }

                _autoRunning = true;
                BeginRunForFile(_autoFilePaths[_autoIndex]);
                _lastStatus = BuildCurrentPrefix() + "继续处理文件：" + _pendingRhinoFilePath;
                ScheduleRefresh(1);
                return;
            }

            _autoRunning = false;
            BeginRunForFile("");
            _lastStatus = "继续处理当前 Rhino 文档。";
            ScheduleRefresh(1);
        }

        private void BeginRunForFile(string rhinoFilePath)
        {
            _runRequested = true;
            _waitingForGuidStable = false;
            _refreshAttemptCount = 0;
            ResetExportInputStability();
            _pendingRhinoFilePath = rhinoFilePath;
            _lastStatus = null;
        }

        private void HandleCurrentFileFinished(int success, int fail, int recordSuccess, int recordFail, IGH_DataAccess DA, string errorMessage)
        {
            CompleteCurrentFile(success, fail, recordSuccess, recordFail, errorMessage);
            DA.SetData(0, _lastStatus);
        }

        private void CompleteCurrentFile(int success, int fail, int recordSuccess, int recordFail, string errorMessage)
        {
            _waitingForGuidStable = false;
            _runRequested = false;
            _refreshAttemptCount = 0;

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                _lastStatus = BuildCurrentPrefix() + errorMessage;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastStatus);
            }
            else
            {
                string fileName = string.IsNullOrWhiteSpace(_pendingRhinoFilePath)
                    ? Path.GetFileName(RhinoDoc.ActiveDoc?.Path ?? "")
                    : Path.GetFileName(_pendingRhinoFilePath);

                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "当前文档";

                _lastStatus = BuildCurrentPrefix() + $"当前文件：{fileName}；导出成功 {success}，失败 {fail}；记录成功 {recordSuccess}，记录失败 {recordFail}。";
            }

            if (!_autoRunning)
            {
                return;
            }

            _autoSuccess += success;
            _autoFail += fail;
            _autoRecordSuccess += recordSuccess;
            _autoRecordFail += recordFail;
            _autoIndex++;

            if (_stopRequested)
            {
                _paused = true;
                _stopRequested = false;
                _autoRunning = _autoIndex < _autoFilePaths.Count;
                _runRequested = false;
                _waitingForGuidStable = false;
                _lastStatus = $"已停止：完成 {_autoIndex}/{_autoFilePaths.Count} 个文件；导出成功 {_autoSuccess}，失败 {_autoFail}。";
                return;
            }

            if (_autoIndex >= _autoFilePaths.Count)
            {
                _autoRunning = false;
                _paused = false;
                _stopRequested = false;
                _lastStatus = $"自动执行完成：文件 {_autoFilePaths.Count} 个；导出成功 {_autoSuccess}，失败 {_autoFail}；记录成功 {_autoRecordSuccess}，记录失败 {_autoRecordFail}。";
                return;
            }

            BeginRunForFile(_autoFilePaths[_autoIndex]);
            _lastStatus = BuildCurrentPrefix() + "准备处理下一个文件：" + _pendingRhinoFilePath;
            ScheduleRefresh(1);
        }

        private void StopAutoExecution(IGH_DataAccess DA)
        {
            StopRefreshTimer();
            StopRhinoActionTimer();
            _paused = true;
            _stopRequested = false;
            _continueRequested = false;
            _autoRunning = _autoFilePaths.Count > 0 && _autoIndex < _autoFilePaths.Count;
            _runRequested = false;
            _waitingForGuidStable = false;
            _buttonRunRequested = false;
            _lastStatus = $"已停止：完成 {_autoIndex}/{_autoFilePaths.Count} 个文件；导出成功 {_autoSuccess}，失败 {_autoFail}。";
            DA.SetData(0, _lastStatus);
        }

        private void StopExecutionWithError(IGH_DataAccess DA, string message)
        {
            _autoRunning = false;
            _runRequested = false;
            _waitingForGuidStable = false;
            _paused = false;
            _stopRequested = false;
            _continueRequested = false;
            _lastStatus = message;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastStatus);
            DA.SetData(0, _lastStatus);
        }

        private string BuildCurrentPrefix()
        {
            if (!_autoRunning || _autoFilePaths.Count == 0)
                return "";

            return $"自动执行 {_autoIndex + 1}/{_autoFilePaths.Count}：";
        }

        private void ExportCurrentDocument(List<GH_Guid> guidList, List<string> savePaths, List<string> fileNames, string databasePath, List<string> modelCodes, out int success, out int fail, out int recordSuccess, out int recordFail)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            RhinoApp.SetFocusToMainWindow();

            success = 0;
            fail = 0;
            recordSuccess = 0;
            recordFail = 0;

#if MODEL_EXPORT_DATABASE
            ModelExportRecordStore store = null;
            if (!string.IsNullOrWhiteSpace(databasePath))
                store = new ModelExportRecordStore(databasePath);
#endif

            string sourceRhinoPath = NormalizePath(_pendingRhinoFilePath);
            if (string.IsNullOrWhiteSpace(sourceRhinoPath))
                sourceRhinoPath = NormalizePath(doc?.Path);

            string currentRhinoName = Path.GetFileNameWithoutExtension(sourceRhinoPath);
            if (string.IsNullOrWhiteSpace(currentRhinoName))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法取得当前 Rhino 文件名，不能生成导出子文件夹。");
                fail += guidList.Count;
                return;
            }

            for (int i = 0; i < guidList.Count; i++)
            {
                RhinoObject rhinoObject = doc?.Objects.FindId(guidList[i].Value);
                if (rhinoObject == null)
                {
                    fail++;
                    continue;
                }

                if (savePaths.Count != 1 && i >= savePaths.Count)
                {
                    fail++;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "保存路径数量少于实体数量");
                    continue;
                }

                string saveRoot = NormalizePath(savePaths.Count == 1 ? savePaths[0] : savePaths[i]);
                if (string.IsNullOrWhiteSpace(saveRoot))
                {
                    fail++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(Path.GetExtension(saveRoot)))
                {
                    fail++;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "保存路径请输入导出根目录，不要填写文件名或后缀名：" + saveRoot);
                    continue;
                }

                string fileName = (fileNames[i] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fail++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                {
                    fail++;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "文件名不要填写后缀名，程序会自动添加 .stp / .3dm：" + fileName);
                    continue;
                }

                string directory = Path.Combine(saveRoot, currentRhinoName);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string basePath = Path.Combine(directory, fileName);
                if (_exportStp)
                    ExportOneFormat(doc, rhinoObject, guidList[i].Value, basePath + ".stp", "stp", sourceRhinoPath, modelCodes, i, ref success, ref fail, ref recordSuccess, ref recordFail);

                if (_export3dm)
                    ExportOneFormat(doc, rhinoObject, guidList[i].Value, basePath + ".3dm", "3dm", sourceRhinoPath, modelCodes, i, ref success, ref fail, ref recordSuccess, ref recordFail);
            }
        }

        private void ExportOneFormat(RhinoDoc doc, RhinoObject rhinoObject, Guid objectGuid, string exportPath, string exportFormat, string sourceRhinoPath, List<string> modelCodes, int itemIndex, ref int success, ref int fail, ref int recordSuccess, ref int recordFail)
        {
            doc.Objects.UnselectAll();
            rhinoObject.Select(true, true);
            RhinoApp.SendKeystrokes(" ", true);

            if (doc.ExportSelected(exportPath))
            {
                success++;
#if MODEL_EXPORT_DATABASE
                string modelCode = itemIndex < modelCodes.Count ? (modelCodes[itemIndex] ?? "").Trim() : "";
                string exportEventId = Guid.NewGuid().ToString("N");
                RecordExport(store, objectGuid, exportPath, exportFormat, sourceRhinoPath, modelCode, exportEventId, ref recordSuccess, ref recordFail);
#endif
            }
            else
            {
                fail++;
                RhinoApp.WriteLine("导出失败: " + exportPath);
            }
        }

#if MODEL_EXPORT_DATABASE
        private void RecordExport(ModelExportRecordStore store, Guid objectGuid, string exportPath, string exportFormat, string sourceRhinoPath, string modelCode, string exportEventId, ref int recordSuccess, ref int recordFail)
        {
            if (store == null)
                return;

            if (string.IsNullOrWhiteSpace(modelCode))
            {
                recordFail++;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "数据库地址已填写，但模型编号为空，已跳过记录：" + exportPath);
                return;
            }

            try
            {
                FileInfo fileInfo = new FileInfo(exportPath);
                store.AddRecord(new ModelExportRecord
                {
                    SourceRhinoPath = sourceRhinoPath,
                    ModelCode = modelCode,
                    ObjectGuid = objectGuid.ToString(),
                    ExportPath = NormalizePath(exportPath),
                    ExportFormat = exportFormat,
                    ExportEventId = exportEventId,
                    ExportedAt = DateTime.Now,
                    FileSize = fileInfo.Exists ? fileInfo.Length : 0
                });
                recordSuccess++;
            }
            catch (Exception ex)
            {
                recordFail++;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "记录导出数据库失败：" + ex.Message);
            }
        }
#endif

        private string BuildInputSignature(List<string> rhinoFilePaths, List<GH_Guid> guidList, List<string> savePaths, string databasePath, List<string> modelCodes, List<string> fileNames)
        {
            List<string> parts = new List<string>();
            parts.Add("RhinoFileSources=" + Params.Input[0].Sources.Count);
            parts.Add("RhinoFileCount=" + rhinoFilePaths.Count);
            foreach (string path in rhinoFilePaths)
                parts.Add(path ?? "");
            parts.Add("GuidSources=" + Params.Input[1].Sources.Count);
            parts.Add("PathSources=" + Params.Input[2].Sources.Count);
            parts.Add("ExportSTP=" + _exportStp);
            parts.Add("Export3DM=" + _export3dm);
            parts.Add("GuidCount=" + guidList.Count);
            foreach (GH_Guid guid in guidList)
                parts.Add(guid.Value.ToString());
            parts.Add("PathCount=" + savePaths.Count);
            foreach (string path in savePaths)
                parts.Add(path ?? "");
#if MODEL_EXPORT_DATABASE
            parts.Add("Database=" + (databasePath ?? ""));
            parts.Add("ModelCodeCount=" + modelCodes.Count);
            foreach (string modelCode in modelCodes)
                parts.Add(modelCode ?? "");
#endif
            parts.Add("FileNameCount=" + fileNames.Count);
            foreach (string fileName in fileNames)
                parts.Add(fileName ?? "");

            return string.Join("|", parts);
        }

        private void ResetExportInputStability()
        {
            _lastExportInputSignature = null;
            _stableExportInputCount = 0;
            _exportInputCheckCount = 0;
        }

        private bool IsExportInputStable(List<GH_Guid> guidList, List<string> savePaths, List<string> modelCodes, List<string> fileNames)
        {
            _exportInputCheckCount++;
            string signature = BuildCurrentExportInputSignature(guidList, savePaths, modelCodes, fileNames);
            if (signature == _lastExportInputSignature)
                _stableExportInputCount++;
            else
            {
                _lastExportInputSignature = signature;
                _stableExportInputCount = 1;
            }

            return _stableExportInputCount >= 3;
        }

        private string BuildCurrentExportInputSignature(List<GH_Guid> guidList, List<string> savePaths, List<string> modelCodes, List<string> fileNames)
        {
            List<string> parts = new List<string>();
            parts.Add("Active=" + NormalizePath(RhinoDoc.ActiveDoc?.Path));
            parts.Add("Pending=" + NormalizePath(_pendingRhinoFilePath));
            parts.Add("GuidCount=" + guidList.Count);
            foreach (GH_Guid guid in guidList)
                parts.Add(guid.Value.ToString());
            parts.Add("PathCount=" + savePaths.Count);
            foreach (string path in savePaths)
                parts.Add(NormalizePath(path));
#if MODEL_EXPORT_DATABASE
            parts.Add("ModelCodeCount=" + modelCodes.Count);
            foreach (string modelCode in modelCodes)
                parts.Add(modelCode ?? "");
#endif
            parts.Add("FileNameCount=" + fileNames.Count);
            foreach (string fileName in fileNames)
                parts.Add(fileName ?? "");

            return string.Join("|", parts);
        }

        private bool TryFindMixedAutoSavePath(List<string> savePaths, out string message)
        {
            message = null;
            if (!_autoRunning || _autoFilePaths.Count <= 1 || string.IsNullOrWhiteSpace(_pendingRhinoFilePath))
                return false;

            string currentName = Path.GetFileNameWithoutExtension(_pendingRhinoFilePath);
            if (string.IsNullOrWhiteSpace(currentName))
                return false;

            List<string> otherNames = _autoFilePaths
                .Where(path => !string.Equals(NormalizePath(path), NormalizePath(_pendingRhinoFilePath), StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (otherNames.Count == 0)
                return false;

            foreach (string savePath in savePaths)
            {
                string normalizedSavePath = NormalizePath(savePath);
                if (string.IsNullOrWhiteSpace(normalizedSavePath))
                    continue;

                bool containsCurrent = normalizedSavePath.IndexOf(currentName, StringComparison.OrdinalIgnoreCase) >= 0;
                string otherName = otherNames.FirstOrDefault(name => normalizedSavePath.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrWhiteSpace(otherName) && !containsCurrent)
                {
                    message = $"未执行：自动执行检测到保存路径混入其它 Rhino 文件的数据。当前文件：{currentName}，混入：{otherName}，路径：{normalizedSavePath}";
                    return true;
                }
            }

            return false;
        }

        private void ScheduleRefresh(int waitMilliseconds)
        {
            StopRefreshTimer();

            int token = ++_refreshScheduleToken;
            GH_Document document = OnPingDocument();
            if (document != null)
            {
                document.ScheduleSolution(Math.Max(1, waitMilliseconds), doc =>
                {
                    if (token != _refreshScheduleToken)
                        return;

                    ExpireSolution(false);
                });
                return;
            }

            _refreshTimer = new Timer();
            _refreshTimer.Interval = Math.Max(1, waitMilliseconds);
            _refreshTimer.Tick += (sender, e) =>
            {
                Timer timer = _refreshTimer;
                _refreshTimer = null;
                timer?.Stop();
                timer?.Dispose();

                if (token != _refreshScheduleToken)
                    return;

                ExpireSolution(false);
            };
            _refreshTimer.Start();
        }

        private void ScheduleRhinoAction(int waitMilliseconds, Action action)
        {
            StopRhinoActionTimer();

            int token = ++_rhinoActionToken;
            _rhinoActionTimer = new Timer();
            _rhinoActionTimer.Interval = Math.Max(1, waitMilliseconds);
            _rhinoActionTimer.Tick += (sender, e) =>
            {
                Timer timer = _rhinoActionTimer;
                _rhinoActionTimer = null;
                timer?.Stop();
                timer?.Dispose();

                if (token != _rhinoActionToken)
                    return;

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _autoRunning = false;
                    _runRequested = false;
                    _waitingForGuidStable = false;
                    _lastStatus = "异常：" + ex.Message;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastStatus);
                    ScheduleRefresh(1);
                }
            };
            _rhinoActionTimer.Start();
        }

        private void StopRefreshTimer()
        {
            _refreshScheduleToken++;

            if (_refreshTimer == null)
                return;

            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _refreshTimer = null;
        }

        private void StopRhinoActionTimer()
        {
            _rhinoActionToken++;

            if (_rhinoActionTimer == null)
                return;

            _rhinoActionTimer.Stop();
            _rhinoActionTimer.Dispose();
            _rhinoActionTimer = null;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopRefreshTimer();
            StopRhinoActionTimer();
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
                diagnostics = $"命令返回 False。文件存在：{File.Exists(filePath)}，当前文档：{NormalizePath(RhinoDoc.ActiveDoc?.Path)}";
                return false;
            }

            RhinoApp.Wait();
            if (!IsActiveRhinoFile(filePath))
            {
                diagnostics = $"命令已执行但当前文档未切换。目标：{filePath}，当前文档：{NormalizePath(RhinoDoc.ActiveDoc?.Path)}";
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
            return ModelExportRecordStore.NormalizePath(path);
        }

        internal void RequestRun()
        {
            _buttonRunRequested = true;
            _stopRequested = false;
            _continueRequested = false;
        }

        internal bool IsPaused
        {
            get { return _paused; }
        }

        internal void RequestStop()
        {
            if (_paused)
            {
                _continueRequested = true;
                _stopRequested = false;
                return;
            }

            _stopRequested = true;
        }

        public override void CreateAttributes()
        {
            Attributes = new To3DMAttributes(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "导出 STP", (sender, args) => ToggleExportStp(), true, _exportStp);
            Menu_AppendItem(menu, "导出 .3dm", (sender, args) => ToggleExport3dm(), true, _export3dm);
        }

        private void ToggleExportStp()
        {
            RecordUndoEvent("切换导出 STP");
            _exportStp = !_exportStp;
            ExpireSolution(true);
        }

        private void ToggleExport3dm()
        {
            RecordUndoEvent("切换导出 .3dm");
            _export3dm = !_export3dm;
            ExpireSolution(true);
        }

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter chunk = writer.CreateChunk(SettingsChunk);
            chunk.SetBoolean("ExportSTP", _exportStp);
            chunk.SetBoolean("Export3DM", _export3dm);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            GH_IReader chunk = reader.FindChunk(SettingsChunk);
            if (chunk != null)
            {
                if (chunk.ItemExists("ExportSTP"))
                    _exportStp = chunk.GetBoolean("ExportSTP");
                if (chunk.ItemExists("Export3DM"))
                    _export3dm = chunk.GetBoolean("Export3DM");
            }

            return base.Read(reader);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ToSTP"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("0E2B614E-6D0B-4630-87DB-C77C581B7B6B"); }
        }
    }

    internal class To3DMAttributes : GH_ComponentAttributes
    {
        public To3DMAttributes(To3DM owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 44.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            To3DM component = (To3DM)Owner;
            RenderButton(graphics, GetRunButtonRect(), "Run", GH_Palette.Black, Brushes.White);
            RenderButton(graphics, GetStopButtonRect(), component.IsPaused ? "Continue" : "Stop", GH_Palette.Grey, Brushes.White);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != MouseButtons.Left)
                return base.RespondToMouseDown(sender, e);

            To3DM component = (To3DM)Owner;
            if (GetRunButtonRect().Contains(e.CanvasLocation))
            {
                component.RequestRun();
                component.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            if (GetStopButtonRect().Contains(e.CanvasLocation))
            {
                component.RequestStop();
                component.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return base.RespondToMouseDown(sender, e);
        }

        private RectangleF GetRunButtonRect()
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 44.0f, Bounds.Width, 21.0f);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }

        private RectangleF GetStopButtonRect()
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 22.0f, Bounds.Width, 21.0f);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }

        private void RenderButton(Graphics graphics, RectangleF buttonRect, string text, GH_Palette palette, Brush textBrush)
        {
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
            {
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            }

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(text, font, textBrush, buttonRect, format);
            }
        }
    }
}
