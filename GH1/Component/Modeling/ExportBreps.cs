using CatiaFunction;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>
    /// 将 Grasshopper Brep 导出为 3dm、STEP 或经 CATIA 重转的 STEP。
    /// </summary>
    public class ExportBreps : GH_Component
    {
        private const string SettingsChunk = "ExportBrepsSettings";

        private bool _export3dm;
        private bool _exportStp;
        private bool _exportCatiaStp = true;
        private string _lastRunSignature;
        private List<string> _lastStatus = new List<string>();

        public ExportBreps()
            : base("导出 Brep", "导出Brep",
                "将 Brep 列表导出为 3dm 或 STP；STP 可选择经 CATIA 重新转换。",
                "Parrot", "Modeling")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Brep", "Brep", "要导出的 Brep 列表。", GH_ParamAccess.list);
            pManager.AddTextParameter("导出地址", "路径", "导出文件的完整存放地址列表。可带或不带 .3dm/.stp 后缀；Brep 与地址必须一一对应。", GH_ParamAccess.list);
            pManager.AddBooleanParameter("导出", "导出", "为 True 时导出。", GH_ParamAccess.item, false);
            pManager[0].Optional = true;
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("状态", "状态", "每个 Brep、每种格式的导出结果。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Brep> breps = new List<Brep>();
            List<string> paths = new List<string>();
            bool run = false;
            DA.GetDataList(0, breps);
            DA.GetDataList(1, paths);
            DA.GetData(2, ref run);

            string signature = BuildSignature(breps, paths);
            if (!run)
            {
                IEnumerable<string> status = _lastStatus.Count > 0
                    ? (IEnumerable<string>)_lastStatus
                    : new[] { "未执行：将“导出”设为 True 后开始导出。" };
                DA.SetDataList(0, status);
                return;
            }

            if (signature == _lastRunSignature)
            {
                DA.SetDataList(0, _lastStatus);
                return;
            }

            if (!_export3dm && !_exportStp && !_exportCatiaStp)
            {
                SetError(DA, "未执行：请在右键菜单至少选择一种导出格式。");
                return;
            }

            if (breps.Count == 0 || paths.Count == 0)
            {
                SetError(DA, "未执行：缺少 Brep 或导出地址。");
                return;
            }

            if (breps.Count != paths.Count)
            {
                SetError(DA, $"未执行：Brep 与导出地址必须一一对应。当前 Brep {breps.Count} 个，导出地址 {paths.Count} 个。");
                return;
            }

            List<string> basePaths = new List<string>();
            for (int i = 0; i < paths.Count; i++)
            {
                string basePath;
                string error;
                if (!TryNormalizeBasePath(paths[i], out basePath, out error))
                {
                    SetError(DA, $"未执行：第 {i + 1} 个导出地址{error}");
                    return;
                }

                if (breps[i] == null || !breps[i].IsValid)
                {
                    SetError(DA, $"未执行：第 {i + 1} 个 Brep 为空或无效。");
                    return;
                }

                basePaths.Add(basePath);
            }

            if (_exportCatiaStp && !EnsureCatiaConnected())
            {
                SetError(DA, "未执行：未检测到正在运行的 CATIA。请先启动 CATIA，再开启导出。");
                return;
            }

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                SetError(DA, "未执行：没有活动的 Rhino 文档。");
                return;
            }

            List<string> statuses = new List<string>();
            for (int i = 0; i < breps.Count; i++)
            {
                List<string> itemStatuses = ExportOne(doc, breps[i], basePaths[i]);
                foreach (string itemStatus in itemStatuses)
                {
                    string status = $"{i + 1}. {itemStatus}";
                    statuses.Add(status);
                    if (!itemStatus.StartsWith("成功：", StringComparison.Ordinal))
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, status);
                }
            }

            _lastRunSignature = signature;
            _lastStatus = statuses;
            DA.SetDataList(0, statuses);
        }

        private bool EnsureCatiaConnected()
        {
            if (Common4Catia.CATIA != null)
                return true;

            Common4Catia.CATIA = Common4Catia.ConnectCatia();
            return Common4Catia.CATIA != null;
        }

        private List<string> ExportOne(RhinoDoc doc, Brep brep, string basePath)
        {
            List<string> statuses = new List<string>();
            Guid objectId = Guid.Empty;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(basePath));
                objectId = doc.Objects.AddBrep(brep);
                if (objectId == Guid.Empty)
                {
                    statuses.Add("失败：无法将 Brep 添加到 Rhino 文档。");
                    return statuses;
                }

                doc.Objects.UnselectAll();
                RhinoObject rhinoObject = doc.Objects.FindId(objectId);
                if (rhinoObject == null || rhinoObject.Select(true, true) == 0)
                {
                    statuses.Add("失败：无法选中待导出的 Brep。");
                    return statuses;
                }

                if (_export3dm)
                    statuses.Add(Export3dm(doc, basePath + ".3dm"));
                if (_exportStp)
                    statuses.Add(ExportStp(doc, basePath + ".stp"));
                if (_exportCatiaStp)
                    statuses.Add(ExportCatiaStp(doc, basePath + ".stp"));
            }
            catch (Exception ex)
            {
                statuses.Add("失败：" + ex.Message + "（" + basePath + "）");
            }
            finally
            {
                doc.Objects.UnselectAll();
                if (objectId != Guid.Empty)
                    doc.Objects.Delete(objectId, true);
            }

            return statuses;
        }

        private static string Export3dm(RhinoDoc doc, string outputPath)
        {
            FileWriteOptions options = new FileWriteOptions
            {
                WriteSelectedObjectsOnly = true,
                SuppressDialogBoxes = true,
                UpdateDocumentPath = false
            };

            if (!doc.WriteFile(outputPath, options) || !ContainsExportedObjects(outputPath))
                return "失败：3DM 导出失败：" + outputPath;

            return "成功：导出 3DM：" + outputPath;
        }

        private static string ExportStp(RhinoDoc doc, string outputPath)
        {
            RhinoApp.SetFocusToMainWindow();
            RhinoApp.SendKeystrokes(" ", true);
            if (!doc.ExportSelected(outputPath) || !File.Exists(outputPath))
                return "失败：STP 导出失败：" + outputPath;

            return "成功：导出 STP：" + outputPath;
        }

        private static string ExportCatiaStp(RhinoDoc doc, string outputPath)
        {
            string temporaryDirectory = null;
            try
            {
                temporaryDirectory = Path.Combine(Path.GetDirectoryName(outputPath), ".catia_step_temp_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryDirectory);
                string temporaryStepPath = Path.Combine(temporaryDirectory, Path.GetFileName(outputPath));

                RhinoApp.SetFocusToMainWindow();
                RhinoApp.SendKeystrokes(" ", true);
                if (!doc.ExportSelected(temporaryStepPath) || !File.Exists(temporaryStepPath))
                    return "失败：CATIA 转换前的 Rhino STEP 导出失败：" + outputPath;

                string result = Common4Catia.ConvertOneSTEP_WithLog(temporaryStepPath, temporaryDirectory, Path.GetDirectoryName(outputPath));
                if (!string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase) || !File.Exists(outputPath))
                    return "失败：CATIA STP 转换失败：" + result + "（" + outputPath + "）";

                return "成功：经 CATIA 导出 STP：" + outputPath;
            }
            catch (Exception ex)
            {
                return "失败：" + ex.Message + "（" + outputPath + "）";
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporaryDirectory))
                {
                    try
                    {
                        if (Directory.Exists(temporaryDirectory))
                            Directory.Delete(temporaryDirectory, true);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static bool ContainsExportedObjects(string outputPath)
        {
            try
            {
                File3dm exportedFile = File3dm.Read(outputPath);
                return exportedFile != null && exportedFile.Objects.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryNormalizeBasePath(string input, out string basePath, out string error)
        {
            basePath = null;
            error = "";
            string path = (input ?? "").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "为空。";
                return false;
            }

            try
            {
                path = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                error = "无效：" + ex.Message;
                return false;
            }

            if (string.IsNullOrWhiteSpace(Path.GetFileName(path)))
            {
                error = "必须包含文件名。";
                return false;
            }

            string extension = Path.GetExtension(path);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                if (!string.Equals(extension, ".3dm", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".stp", StringComparison.OrdinalIgnoreCase))
                {
                    error = "后缀必须是 .3dm 或 .stp。";
                    return false;
                }

                path = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
            }

            basePath = path;
            return true;
        }

        private string BuildSignature(List<Brep> breps, List<string> paths)
        {
            List<string> values = new List<string>
            {
                breps.Count.ToString(),
                paths.Count.ToString(),
                _export3dm.ToString(),
                _exportStp.ToString(),
                _exportCatiaStp.ToString()
            };
            for (int i = 0; i < breps.Count; i++)
                values.Add(breps[i] == null ? "null" : breps[i].GetHashCode().ToString());
            values.AddRange(paths);
            return string.Join("|", values);
        }

        private void SetError(IGH_DataAccess DA, string message)
        {
            _lastRunSignature = null;
            _lastStatus = new List<string> { message };
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
            DA.SetDataList(0, _lastStatus);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "导出为 3dm", (sender, args) => ToggleExport3dm(), true, _export3dm);
            Menu_AppendItem(menu, "导出为 stp", (sender, args) => ToggleExportStp(), true, _exportStp);
            Menu_AppendItem(menu, "经 Catia 导出为 stp", (sender, args) => ToggleExportCatiaStp(), true, _exportCatiaStp);
        }

        private void ToggleExport3dm()
        {
            RecordUndoEvent("切换导出 3dm");
            _export3dm = !_export3dm;
            ExpireSolution(true);
        }

        private void ToggleExportStp()
        {
            RecordUndoEvent("切换导出 stp");
            _exportStp = !_exportStp;
            if (_exportStp)
                _exportCatiaStp = false;
            ExpireSolution(true);
        }

        private void ToggleExportCatiaStp()
        {
            RecordUndoEvent("切换经 Catia 导出 stp");
            _exportCatiaStp = !_exportCatiaStp;
            if (_exportCatiaStp)
                _exportStp = false;
            ExpireSolution(true);
        }

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter chunk = writer.CreateChunk(SettingsChunk);
            chunk.SetBoolean("Export3dm", _export3dm);
            chunk.SetBoolean("ExportStp", _exportStp);
            chunk.SetBoolean("ExportCatiaStp", _exportCatiaStp);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            GH_IReader chunk = reader.FindChunk(SettingsChunk);
            if (chunk != null)
            {
                if (chunk.ItemExists("Export3dm")) _export3dm = chunk.GetBoolean("Export3dm");
                if (chunk.ItemExists("ExportStp")) _exportStp = chunk.GetBoolean("ExportStp");
                if (chunk.ItemExists("ExportCatiaStp")) _exportCatiaStp = chunk.GetBoolean("ExportCatiaStp");
            }

            if (_exportStp && _exportCatiaStp)
                _exportCatiaStp = false;
            return base.Read(reader);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ToSTP_ByCatia"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("8E0F7788-7A51-46ED-AE85-3D9255DCC85E"); }
        }
    }
}

