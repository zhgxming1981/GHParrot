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
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>
    /// 仅导出当前 Rhino 文档中指定对象的手动导出电池。
    /// </summary>
    public class ManualExportModel : GH_Component
    {
        private const string SettingsChunk = "ManualExportModelSettings";

        private bool _exportStp = true;
        private bool _export3dm = true;
        private bool _runRequested;
        private bool _exporting;
        private string _lastStatus;

        public ManualExportModel()
            : base("手动导出模型", "手动导出模型",
                "手动将当前 Rhino 文档中指定对象导出为 STP 或 .3dm。Guid 与文件名必须一一对应。",
                "Parrot", "Modeling")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "要导出的当前 Rhino 文档对象 Guid 列表。", GH_ParamAccess.list);
            pManager.AddTextParameter("保存路径", "保存路径", "导出根目录。可输入 1 个路径，或与 Guid 一一对应的路径列表。", GH_ParamAccess.list);
            pManager.AddTextParameter("文件名", "文件名", "导出文件名列表，不带后缀名；必须与 Guid 一一对应。", GH_ParamAccess.list);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status", "导出状态。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            if (!_runRequested || _exporting)
            {
                DA.SetData(0, _lastStatus ?? "未执行：点击 Run 开始导出。");
                return;
            }

            _runRequested = false;

            if (!_exportStp && !_export3dm)
            {
                SetError(DA, "未执行：请在右键菜单至少勾选一种导出格式：导出 STP 或导出 .3dm。");
                return;
            }

            List<GH_Guid> guidList = new List<GH_Guid>();
            List<string> savePaths = new List<string>();
            List<string> fileNames = new List<string>();
            bool hasGuids = DA.GetDataList(0, guidList);
            bool hasSavePaths = DA.GetDataList(1, savePaths);
            bool hasFileNames = DA.GetDataList(2, fileNames);

            if (!hasGuids || !hasSavePaths || !hasFileNames || guidList.Count == 0 || savePaths.Count == 0 || fileNames.Count == 0)
            {
                SetError(DA, "未执行：缺少 Guid、保存路径或文件名。");
                return;
            }

            if (guidList.Count != fileNames.Count)
            {
                SetError(DA, $"未执行：Guid 与文件名必须一一对应。当前 Guid {guidList.Count} 个，文件名 {fileNames.Count} 个。");
                return;
            }

            if (savePaths.Count != 1 && savePaths.Count != guidList.Count)
            {
                SetError(DA, $"未执行：保存路径只能输入 1 个，或与 Guid 一一对应。当前 Guid {guidList.Count} 个，保存路径 {savePaths.Count} 个。");
                return;
            }

            if (!ValidateInputs(guidList, savePaths, fileNames, out string validationMessage))
            {
                SetError(DA, validationMessage);
                return;
            }

            _exporting = true;
            _lastStatus = "正在导出当前 Rhino 文档。";
            DA.SetData(0, _lastStatus);

            List<GH_Guid> exportGuids = new List<GH_Guid>(guidList);
            List<string> exportSavePaths = new List<string>(savePaths);
            List<string> exportFileNames = new List<string>(fileNames);
            ScheduleRhinoAction(() => Export(exportGuids, exportSavePaths, exportFileNames));
        }

        private bool ValidateInputs(List<GH_Guid> guidList, List<string> savePaths, List<string> fileNames, out string message)
        {
            message = "";
            for (int i = 0; i < guidList.Count; i++)
            {
                if (guidList[i].Value == Guid.Empty)
                {
                    message = $"未执行：第 {i + 1} 个 Guid 为空。";
                    return false;
                }

                string savePath = (savePaths.Count == 1 ? savePaths[0] : savePaths[i])?.Trim();
                if (string.IsNullOrWhiteSpace(savePath))
                {
                    message = $"未执行：第 {i + 1} 个保存路径为空。";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(Path.GetExtension(savePath)))
                {
                    message = "未执行：保存路径请输入导出根目录，不要填写文件名或后缀名：" + savePath;
                    return false;
                }

                string fileName = fileNames[i]?.Trim();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    message = $"未执行：第 {i + 1} 个文件名为空。";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                {
                    message = "未执行：文件名不要填写后缀名，程序会自动添加 .stp / .3dm：" + fileName;
                    return false;
                }
            }

            return true;
        }

        private void Export(List<GH_Guid> guidList, List<string> savePaths, List<string> fileNames)
        {
            int success = 0;
            int fail = 0;
            try
            {
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                {
                    _lastStatus = "导出失败：没有活动的 Rhino 文档。";
                    return;
                }

                RhinoApp.SetFocusToMainWindow();
                for (int i = 0; i < guidList.Count; i++)
                {
                    RhinoObject rhinoObject = doc.Objects.FindId(guidList[i].Value);
                    if (rhinoObject == null)
                    {
                        fail += EnabledFormatCount();
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"找不到第 {i + 1} 个 Guid 对应的 Rhino 对象：{guidList[i].Value}");
                        continue;
                    }

                    string saveRoot = (savePaths.Count == 1 ? savePaths[0] : savePaths[i]).Trim();
                    string fileName = fileNames[i].Trim();
                    Directory.CreateDirectory(saveRoot);
                    string basePath = Path.Combine(saveRoot, fileName);

                    if (_exportStp)
                        ExportOne(doc, rhinoObject, basePath + ".stp", false, ref success, ref fail);
                    if (_export3dm)
                        ExportOne(doc, rhinoObject, basePath + ".3dm", true, ref success, ref fail);
                }

                _lastStatus = $"导出完成：成功 {success}，失败 {fail}。";
            }
            catch (Exception ex)
            {
                _lastStatus = "导出异常：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastStatus);
            }
            finally
            {
                _exporting = false;
                ScheduleRefresh();
            }
        }

        private void ExportOne(RhinoDoc doc, RhinoObject rhinoObject, string exportPath, bool export3dm, ref int success, ref int fail)
        {
            doc.Objects.UnselectAll();
            rhinoObject.Select(true, true);

            bool exported;
            if (export3dm)
            {
                FileWriteOptions options = new FileWriteOptions
                {
                    WriteSelectedObjectsOnly = true,
                    SuppressDialogBoxes = true,
                    UpdateDocumentPath = false
                };
                exported = doc.WriteFile(exportPath, options) && ContainsExportedObjects(exportPath);
            }
            else
            {
                RhinoApp.SendKeystrokes(" ", true);
                exported = doc.ExportSelected(exportPath);
            }

            if (exported)
                success++;
            else
            {
                fail++;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "导出失败：" + exportPath);
            }
        }

        private int EnabledFormatCount()
        {
            return (_exportStp ? 1 : 0) + (_export3dm ? 1 : 0);
        }

        private static bool ContainsExportedObjects(string exportPath)
        {
            try
            {
                File3dm exportedFile = File3dm.Read(exportPath);
                return exportedFile != null && exportedFile.Objects.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private void SetError(IGH_DataAccess DA, string message)
        {
            _lastStatus = message;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
            DA.SetData(0, message);
        }

        private void ScheduleRhinoAction(Action action)
        {
            Timer timer = new Timer { Interval = 1 };
            timer.Tick += (sender, e) =>
            {
                timer.Stop();
                timer.Dispose();
                action();
            };
            timer.Start();
        }

        private void ScheduleRefresh()
        {
            GH_Document document = OnPingDocument();
            if (document != null)
            {
                document.ScheduleSolution(1, doc => ExpireSolution(false));
                return;
            }

            ExpireSolution(false);
        }

        internal void RequestRun()
        {
            if (_exporting)
                return;

            _runRequested = true;
        }

        public override void CreateAttributes()
        {
            Attributes = new ManualExportModelAttributes(this);
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
            get { return new Guid("71534176-4A1A-4CF6-AF0D-18C665941E55"); }
        }
    }

    internal class ManualExportModelAttributes : GH_ComponentAttributes
    {
        public ManualExportModelAttributes(ManualExportModel owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 24.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects)
                return;

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 24.0f, Bounds.Width, 21.0f);
            buttonRect.Inflate(-5.0f, -2.0f);
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, GH_Palette.Black))
            {
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            }

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("Run", font, Brushes.White, buttonRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && new RectangleF(Bounds.X + 5.0f, Bounds.Bottom - 22.0f, Bounds.Width - 10.0f, 17.0f).Contains(e.CanvasLocation))
            {
                ManualExportModel component = (ManualExportModel)Owner;
                component.RequestRun();
                component.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return base.RespondToMouseDown(sender, e);
        }
    }
}
