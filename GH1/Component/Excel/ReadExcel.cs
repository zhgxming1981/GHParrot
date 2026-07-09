using CommonFunction;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class ReadExcel : GH_Component
    {
        private const string SettingsChunk = "ReadExcelSettings";

        private bool _ignoreEmptyRows = true;
        private bool _lastRunInput = false;
        private bool _menuRefreshRequested = false;
        private readonly List<string> _lastResult = new List<string>();

        public string FilePath { get; private set; } = string.Empty;
        public string SheetName { get; private set; } = string.Empty;
        public string RangeText { get; private set; } = string.Empty;

        public ReadExcel()
          : base("读取Excel", "读取Excel",
              "读取 Excel 指定区域，每行输出为 A|B|C 格式",
              "Parrot", "Excel")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("路径", "路径", "Excel 文件完整路径", GH_ParamAccess.item);
            pManager.AddTextParameter("Sheet", "Sheet", "要读取的 Sheet 名称；可在右键菜单中从当前文件选择", GH_ParamAccess.item);
            pManager.AddTextParameter("读取区域", "区域", "读取区域，例如 A1:C10", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Run", "Run", "从 False 变为 True 时读取 Excel", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("结果", "结果", "每行读取结果，格式为 A|B|C", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string filePath = FilePath;
            string sheetName = SheetName;
            string rangeText = RangeText;
            bool runInput = false;

            DA.GetData(0, ref filePath);
            DA.GetData(1, ref sheetName);
            DA.GetData(2, ref rangeText);
            DA.GetData(3, ref runInput);

            FilePath = filePath;
            SheetName = sheetName;
            RangeText = rangeText;

            if (_menuRefreshRequested || (runInput && !_lastRunInput))
            {
                _menuRefreshRequested = false;
                try
                {
                    _lastResult.Clear();
                    _lastResult.AddRange(ReadRows(filePath, sheetName, rangeText, _ignoreEmptyRows));
                    Message = null;
                }
                catch (Exception ex)
                {
                    _lastResult.Clear();
                    string errorMessage = BuildReadErrorMessage(ex);
                    Message = ShortenMessage(errorMessage);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, errorMessage);
                }
            }

            _lastRunInput = runInput;
            DA.SetDataList(0, _lastResult);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "忽略空行", (sender, args) => SetIgnoreEmptyRows(true), true, _ignoreEmptyRows);
            Menu_AppendItem(menu, "保留空行", (sender, args) => SetIgnoreEmptyRows(false), true, !_ignoreEmptyRows);

            Menu_AppendSeparator(menu);
            ToolStripMenuItem sheetRoot = new ToolStripMenuItem("Sheet");
            menu.Items.Add(sheetRoot);
            AppendSheetMenuItems(sheetRoot);
        }

        private void SetIgnoreEmptyRows(bool ignoreEmptyRows)
        {
            _ignoreEmptyRows = ignoreEmptyRows;
            _menuRefreshRequested = true;
            ExpireSolution(true);
        }

        private void AppendSheetMenuItems(ToolStripMenuItem root)
        {
            string filePath = FilePath;
            TryReadPathInput(ref filePath);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                root.DropDownItems.Add(new ToolStripMenuItem("请先输入路径") { Enabled = false });
                return;
            }

            try
            {
                List<string> sheetNames = ReadSheetNames(filePath);
                if (sheetNames.Count == 0)
                {
                    root.DropDownItems.Add(new ToolStripMenuItem("未找到 Sheet") { Enabled = false });
                    return;
                }

                foreach (string sheetName in sheetNames)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(sheetName)
                    {
                        Checked = string.Equals(sheetName, SheetName, StringComparison.OrdinalIgnoreCase),
                        Tag = sheetName
                    };
                    item.Click += SheetMenuItemClicked;
                    root.DropDownItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                string errorMessage = BuildReadErrorMessage(ex);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, errorMessage);
                root.DropDownItems.Add(new ToolStripMenuItem(ShortenMessage(errorMessage)) { Enabled = false });
            }
        }

        private static string BuildReadErrorMessage(Exception ex)
        {
            string reason = ex == null ? "未知错误。" : ex.Message;
            return "读取 Excel 失败，原因：" + reason;
        }

        private static string ShortenMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "读取失败";

            const int maxLength = 48;
            return message.Length <= maxLength ? message : message.Substring(0, maxLength) + "...";
        }

        private void SheetMenuItemClicked(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem item) || !(item.Tag is string sheetName))
                return;

            SheetName = sheetName;
            SetSheetInputPersistentData(sheetName);
            _menuRefreshRequested = true;
            ExpireSolution(true);
        }

        private void SetSheetInputPersistentData(string sheetName)
        {
            if (Params.Input.Count < 2)
                return;

            Param_String sheetParam = Params.Input[1] as Param_String;
            if (sheetParam == null)
                return;

            if (sheetParam.SourceCount > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Sheet 输入端已接线，右键选择不会覆盖上游输入。");
                return;
            }

            sheetParam.PersistentData.Clear();
            sheetParam.SetPersistentData(sheetName);
        }

        private void TryReadPathInput(ref string filePath)
        {
            if (Params.Input.Count == 0 || Params.Input[0].VolatileData.IsEmpty)
                return;

            foreach (IGH_Goo goo in Params.Input[0].VolatileData.AllData(true))
            {
                if (goo == null)
                    continue;

                string text;
                if (!goo.CastTo(out text))
                    text = goo.ToString();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    filePath = text;
                    return;
                }
            }
        }

        private static List<string> ReadRows(string filePath, string sheetName, string rangeText, bool ignoreEmptyRows)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("路径为空。");
            if (string.IsNullOrWhiteSpace(sheetName))
                throw new ArgumentException("Sheet 为空。");
            if (string.IsNullOrWhiteSpace(rangeText))
                throw new ArgumentException("读取区域为空。");

            Excel.Application app = null;
            Excel.Workbook wb = null;
            Excel.Worksheet ws = null;
            Excel.Range range = null;
            bool appCreated = false;

            try
            {
                try
                {
                    app = GetExcelApplication(out appCreated);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("连接或启动 Excel 失败，原因：" + ex.Message, ex);
                }

                try
                {
                    wb = GetOrOpenWorkbook(app, filePath);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("打开Excel文件失败，原因：" + ex.Message, ex);
                }

                try
                {
                    ws = GetWorksheet(wb, sheetName);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("获取Sheet失败，原因：" + ex.Message, ex);
                }

                try
                {
                    range = ws.Range[rangeText];
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("读取区域无效或不可访问，区域：" + rangeText + "，原因：" + ex.Message, ex);
                }

                int rowCount;
                int columnCount;
                try
                {
                    rowCount = range.Rows.Count;
                    columnCount = range.Columns.Count;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("读取区域行列数失败，区域：" + rangeText + "，原因：" + ex.Message, ex);
                }

                object value;
                try
                {
                    value = range.Value2;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("读取单元格值失败，区域：" + rangeText + "，原因：" + ex.Message, ex);
                }

                List<string> rows = new List<string>();
                object[,] values = value as object[,];

                for (int row = 1; row <= rowCount; row++)
                {
                    string[] cells = new string[columnCount];
                    bool rowEmpty = true;

                    for (int column = 1; column <= columnCount; column++)
                    {
                        object cellValue = values == null ? value : values[row, column];
                        string cellText = ConvertCellToText(cellValue);
                        cells[column - 1] = cellText;

                        if (!string.IsNullOrWhiteSpace(cellText))
                            rowEmpty = false;
                    }

                    if (rowEmpty && ignoreEmptyRows)
                        continue;

                    rows.Add(string.Join("|", cells));
                }

                return rows;
            }
            finally
            {
                ReleaseCom(range);
                ReleaseCom(ws);
                ReleaseCom(wb);
                if (app != null && appCreated)
                    TryQuitExcel(app);
                ReleaseCom(app);
            }
        }

        private static List<string> ReadSheetNames(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("路径为空。");

            Excel.Application app = null;
            Excel.Workbook wb = null;
            bool appCreated = false;

            try
            {
                try
                {
                    app = GetExcelApplication(out appCreated);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("连接或启动 Excel 失败，原因：" + ex.Message, ex);
                }

                try
                {
                    wb = GetOrOpenWorkbook(app, filePath);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("打开Excel文件失败，原因：" + ex.Message, ex);
                }

                List<string> sheetNames = new List<string>();
                try
                {
                    foreach (Excel.Worksheet sheet in wb.Worksheets)
                    {
                        try
                        {
                            sheetNames.Add(sheet.Name);
                        }
                        finally
                        {
                            ReleaseCom(sheet);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("读取Sheet列表失败，原因：" + ex.Message, ex);
                }

                return sheetNames;
            }
            finally
            {
                ReleaseCom(wb);
                if (app != null && appCreated)
                    TryQuitExcel(app);
                ReleaseCom(app);
            }
        }

        private static string ConvertCellToText(object value)
        {
            if (value == null)
                return string.Empty;

            if (value is string text)
                return text;

            if (value is double doubleValue)
                return doubleValue.ToString("G15", CultureInfo.InvariantCulture);

            if (value is float floatValue)
                return floatValue.ToString("G9", CultureInfo.InvariantCulture);

            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString();
        }

        private static Excel.Application GetExcelApplication(out bool appCreated)
        {
            try
            {
                appCreated = false;
                return (Excel.Application)Marshal.GetActiveObject("Excel.Application");
            }
            catch
            {
                appCreated = true;
                return new Excel.Application();
            }
        }

        private static Excel.Workbook GetOrOpenWorkbook(Excel.Application app, string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);

            foreach (Excel.Workbook workbook in app.Workbooks)
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

            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Excel 文件不存在。", fullPath);

            return app.Workbooks.Open(fullPath);
        }

        private static Excel.Worksheet GetWorksheet(Excel.Workbook wb, string sheetName)
        {
            foreach (Excel.Worksheet sheet in wb.Worksheets)
            {
                if (string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return sheet;

                ReleaseCom(sheet);
            }

            throw new ArgumentException("找不到 Sheet: " + sheetName);
        }

        private static void ReleaseCom(object obj)
        {
            try
            {
                if (obj != null && Marshal.IsComObject(obj))
                    Marshal.ReleaseComObject(obj);
            }
            catch
            {
            }
        }

        private static void TryQuitExcel(Excel.Application app)
        {
            try
            {
                app.Quit();
            }
            catch
            {
            }
        }

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter chunk = writer.CreateChunk(SettingsChunk);
            chunk.SetBoolean("IgnoreEmptyRows", _ignoreEmptyRows);
            chunk.SetString("SheetName", SheetName ?? string.Empty);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            GH_IReader chunk = reader.FindChunk(SettingsChunk);
            if (chunk != null)
            {
                bool ignoreEmptyRows = _ignoreEmptyRows;
                string sheetName = SheetName;

                if (chunk.TryGetBoolean("IgnoreEmptyRows", ref ignoreEmptyRows))
                    _ignoreEmptyRows = ignoreEmptyRows;
                if (chunk.TryGetString("SheetName", ref sheetName))
                    SheetName = sheetName;
            }

            return base.Read(reader);
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Gh2Excel");

        public override Guid ComponentGuid => new Guid("9F32BF51-B3DE-4C17-A5DD-5C625E1C8A71");
    }
}
