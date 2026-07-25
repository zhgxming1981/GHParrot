using Excel = Microsoft.Office.Interop.Excel;
using Grasshopper.Kernel;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class OpenExcelWorkbook : GH_Component
    {
        private string _openedPath = string.Empty;
        private Excel.Application _excelApplication;
        private Excel.Workbook _workbook;
        private bool _lastRun;
        private bool _triggerRun;

        public OpenExcelWorkbook()
          : base("打开Excel文件", "打开Excel",
              "打开指定 Excel 文件并输出工作簿引用；找不到正在运行的 Excel 时会新建 Excel 实例",
              "Parrot", "Excel2")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Excel文件路径", "Path", "要打开的 Excel 文件完整路径", GH_ParamAccess.item);
            pManager.AddBooleanParameter("运行", "Run", "False 变为 True 时打开 Excel 文件", GH_ParamAccess.item, false);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Excel工作簿引用", "Workbook", "已打开的 Excel 工作簿引用", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = string.Empty;
            bool run = false;
            DA.GetData(0, ref filePath);
            DA.GetData(1, ref run);

            bool trigger = _triggerRun || (run && !_lastRun);
            _triggerRun = false;
            _lastRun = run;
            if (!trigger)
                return;

            try
            {
                string fullPath = ValidateFilePath(filePath);
                OpenWorkbook(fullPath);

                Message = "已打开";
                DA.SetData(0, _workbook);
            }
            catch (FileNotFoundException ex)
            {
                ReportError("未找到 Excel 文件: " + ex.FileName, DA);
            }
            catch (Exception ex)
            {
                ReportError("无法打开或新建 Excel: " + ex.Message, DA);
            }
        }

        private static string ValidateFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Excel 文件路径为空。");

            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("未找到 Excel 文件。", fullPath);

            return fullPath;
        }

        private void OpenWorkbook(string fullPath)
        {
            if (_workbook != null && string.Equals(_openedPath, fullPath, StringComparison.OrdinalIgnoreCase))
                return;

            _excelApplication = GetOrCreateExcelApplication();
            _workbook = FindOpenWorkbook(_excelApplication, fullPath) ?? _excelApplication.Workbooks.Open(fullPath);
            _openedPath = fullPath;
        }

        private static Excel.Application GetOrCreateExcelApplication()
        {
            try
            {
                return (Excel.Application)Marshal.GetActiveObject("Excel.Application");
            }
            catch
            {
                return new Excel.Application();
            }
        }

        private static Excel.Workbook FindOpenWorkbook(Excel.Application application, string fullPath)
        {
            foreach (Excel.Workbook workbook in application.Workbooks)
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(workbook.FullName), fullPath, StringComparison.OrdinalIgnoreCase))
                        return workbook;
                }
                catch
                {
                }
            }

            return null;
        }

        private void ReportError(string errorMessage, IGH_DataAccess dataAccess)
        {
            Message = "打开失败";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, errorMessage);
            dataAccess.SetData(0, null);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "运行", OnRunClicked);
        }

        private void OnRunClicked(object sender, EventArgs e)
        {
            _triggerRun = true;
            ExpireSolution(true);
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2OpenWorkbook");

        public override Guid ComponentGuid => new Guid("6D8F08E3-EC10-4A2F-A7F2-046CC15F7747");
    }
}
