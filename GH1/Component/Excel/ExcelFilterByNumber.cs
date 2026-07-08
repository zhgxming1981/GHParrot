using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class ExcelFilterByNumber : GH_Component
    {
        public enum ButtonColor { Black, Grey }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;

        public string SourcePath { get; private set; } = string.Empty;
        public List<string> IgnoreSheets { get; } = new List<string>();
        public string NumberCell { get; private set; } = string.Empty;
        public string TypeColumn { get; private set; } = string.Empty;
        public List<string> TypeTexts { get; } = new List<string>();
        public string CopyColumns { get; private set; } = string.Empty;
        public string TargetPath { get; private set; } = string.Empty;
        public string TargetSheet { get; private set; } = string.Empty;
        public string TargetCell { get; private set; } = "A1";
        public bool DonePulse { get; set; } = false;
        public string ResultMessage { get; set; } = string.Empty;
        public int RowCount { get; private set; } = 0;

        private bool _lastRunInput = false;

        public ExcelFilterByNumber()
          : base("Excel按编号筛选", "编号筛选",
              "从源 Excel 所有未忽略 Sheet 中读取编号和类型命中行，并汇总写入新表",
              "Parrot", "Excel")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("源表", "源表", "源 Excel 文件完整路径", GH_ParamAccess.item);
            pManager.AddTextParameter("忽略表", "忽略", "要忽略的 Sheet 名，可空，支持多个", GH_ParamAccess.list);
            pManager.AddTextParameter("编号位", "编号位", "编号起始单元格，例如 R10；从该单元格沿同列向下读取，遇空停止", GH_ParamAccess.item);
            pManager.AddTextParameter("类型列", "类型列", "用于匹配类型的列号，例如 A、B、AB", GH_ParamAccess.item);
            pManager.AddTextParameter("类型", "类型", "要匹配的类型字符串列表；类型列单元格内容完全等于其中一项时命中", GH_ParamAccess.list);
            pManager.AddTextParameter("复制列", "复制列", "命中行需要复制的列，例如 H~L,O,P，也支持 AB~AF；类型列只有写在这里才会输出", GH_ParamAccess.item);
            pManager.AddTextParameter("目标表", "目标表", "目标 Excel 文件完整路径；不存在时会新建", GH_ParamAccess.item);
            pManager.AddTextParameter("目标Sheet", "目标Sheet", "目标 Sheet 名；不存在时会新建", GH_ParamAccess.item);
            pManager.AddTextParameter("位置", "位置", "目标表开始写入位置，例如 A1、B3", GH_ParamAccess.item, "A1");
            pManager.AddBooleanParameter("Run", "Run", "从 False 变为 True 时执行，也可以点击电池按钮执行", GH_ParamAccess.item, false);

            pManager[1].Optional = true;
            pManager[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("行数", "行数", "本次写入的结果行数", GH_ParamAccess.item);
            pManager.AddBooleanParameter("完成", "完成", "执行完成后输出一次 True 脉冲", GH_ParamAccess.item);
            pManager.AddTextParameter("日志", "日志", "执行结果或错误信息", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string sourcePath = SourcePath;
            List<string> ignoreSheets = new List<string>();
            string numberCell = NumberCell;
            string typeColumn = TypeColumn;
            List<string> typeTexts = new List<string>();
            string copyColumns = CopyColumns;
            string targetPath = TargetPath;
            string targetSheet = TargetSheet;
            string targetCell = TargetCell;
            bool runInput = false;

            DA.GetData(0, ref sourcePath);
            DA.GetDataList(1, ignoreSheets);
            DA.GetData(2, ref numberCell);
            DA.GetData(3, ref typeColumn);
            DA.GetDataList(4, typeTexts);
            DA.GetData(5, ref copyColumns);
            DA.GetData(6, ref targetPath);
            DA.GetData(7, ref targetSheet);
            DA.GetData(8, ref targetCell);
            DA.GetData(9, ref runInput);

            SourcePath = sourcePath;
            IgnoreSheets.Clear();
            IgnoreSheets.AddRange(ignoreSheets);
            NumberCell = numberCell;
            TypeColumn = typeColumn;
            TypeTexts.Clear();
            TypeTexts.AddRange(typeTexts);
            CopyColumns = copyColumns;
            TargetPath = targetPath;
            TargetSheet = targetSheet;
            TargetCell = string.IsNullOrWhiteSpace(targetCell) ? "A1" : targetCell;

            if (runInput && !_lastRunInput)
                Execute();

            _lastRunInput = runInput;

            DA.SetData(0, RowCount);
            DA.SetData(1, DonePulse);
            DA.SetData(2, ResultMessage);
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_ExcelFilterByNumber(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "显示目标 Excel", ShowTargetExcelClicked);
        }

        private void ShowTargetExcelClicked(object sender, EventArgs e)
        {
            try
            {
                ExcelPulseTools.ShowExcel(TargetPath, TargetSheet);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "显示目标 Excel 失败: " + ex.Message);
            }
        }

        protected override Bitmap Icon => GeneratedIcon.Get("gen_MergeExcelRangeSheets");

        public override Guid ComponentGuid => new Guid("C34094EC-D8B3-4560-BE28-C2135938FD02");

        public void Execute()
        {
            try
            {
                List<object[]> rows = BuildRows();
                WriteRows(rows);
                RowCount = rows.Count;
                ResultMessage = "汇总完成，共写入 " + RowCount + " 行。";
                PulseDone();
            }
            catch (Exception ex)
            {
                DonePulse = false;
                ResultMessage = "汇总失败: " + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ResultMessage);
                ExpireSolution(true);
            }
        }

        public void PulseDone()
        {
            DonePulse = true;
            ExpireSolution(true);

            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 200;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                DonePulse = false;
                ExpireSolution(true);
            };
            timer.Start();
        }

        private List<object[]> BuildRows()
        {
            ValidateInputs();

            Excel.Application app = null;
            Excel.Workbook sourceWb = null;
            bool appCreated = false;

            try
            {
                app = GetExcelApplication(out appCreated);
                app.Visible = false;
                app.ScreenUpdating = false;
                app.DisplayAlerts = false;

                sourceWb = GetOrOpenWorkbook(app, SourcePath);

                HashSet<string> ignored = BuildTextSet(IgnoreSheets);
                bool targetIsSource = IsSamePath(SourcePath, TargetPath);
                if (targetIsSource && !string.IsNullOrWhiteSpace(TargetSheet))
                    ignored.Add(TargetSheet.Trim());

                ParseCell(NumberCell, out int numberStartRow, out int numberColumn);
                int typeColumn = ParseColumn(TypeColumn);
                List<int> copyColumns = ParseColumns(CopyColumns);
                HashSet<string> typeSet = BuildTextSet(TypeTexts);
                List<object[]> rows = new List<object[]>();

                int sheetCount = sourceWb.Worksheets.Count;
                for (int sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
                {
                    Excel.Worksheet ws = null;

                    try
                    {
                        ws = sourceWb.Worksheets[sheetIndex] as Excel.Worksheet;
                        if (ws == null || ignored.Contains(ws.Name))
                            continue;

                        List<string> numbers = ReadNumberList(ws, numberStartRow, numberColumn);
                        if (numbers.Count == 0)
                            continue;

                        List<SourceRow> matchedRows = ReadMatchedRows(ws, typeColumn, typeSet, copyColumns);
                        if (matchedRows.Count == 0)
                            continue;

                        foreach (string number in numbers)
                        {
                            foreach (SourceRow matched in matchedRows)
                            {
                                object[] output = new object[2 + matched.Values.Length];
                                output[0] = ws.Name;
                                output[1] = number;
                                for (int i = 0; i < matched.Values.Length; i++)
                                    output[2 + i] = matched.Values[i];

                                rows.Add(output);
                            }
                        }
                    }
                    finally
                    {
                        ReleaseCom(ws);
                    }
                }

                return rows;
            }
            finally
            {
                ReleaseCom(sourceWb);
                if (app != null && appCreated)
                    app.Quit();
                ReleaseCom(app);
            }
        }

        private void WriteRows(List<object[]> rows)
        {
            Excel.Application app = null;
            Excel.Workbook targetWb = null;
            Excel.Worksheet targetWs = null;
            bool appCreated = false;
            bool targetCreated = false;

            try
            {
                app = GetExcelApplication(out appCreated);
                app.Visible = false;
                app.ScreenUpdating = false;
                app.DisplayAlerts = false;

                targetWb = GetOrCreateWorkbook(app, TargetPath, out targetCreated);
                targetWs = GetOrCreateWorksheet(targetWb, TargetSheet);

                ParseCell(TargetCell, out int startRow, out int startColumn);
                ClearWorksheetFromStart(targetWs, startRow, startColumn);

                if (rows.Count > 0)
                    WriteOutputRows(targetWs, startRow, startColumn, rows);

                if (targetCreated)
                    targetWb.SaveAs(Path.GetFullPath(TargetPath));
                else
                    targetWb.Save();
            }
            finally
            {
                ReleaseCom(targetWs);
                ReleaseCom(targetWb);
                if (app != null && appCreated)
                    app.Quit();
                ReleaseCom(app);
            }
        }

        private void ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(SourcePath))
                throw new ArgumentException("源表路径为空。");
            if (string.IsNullOrWhiteSpace(NumberCell))
                throw new ArgumentException("编号位置为空。");
            if (string.IsNullOrWhiteSpace(TypeColumn))
                throw new ArgumentException("类型列为空。");
            if (TypeTexts.Count == 0)
                throw new ArgumentException("类型列表为空。");
            if (string.IsNullOrWhiteSpace(CopyColumns))
                throw new ArgumentException("复制列为空。");
            if (string.IsNullOrWhiteSpace(TargetPath))
                throw new ArgumentException("目标表路径为空。");
            if (string.IsNullOrWhiteSpace(TargetSheet))
                throw new ArgumentException("目标 Sheet 为空。");
            if (string.IsNullOrWhiteSpace(TargetCell))
                TargetCell = "A1";
        }

        private List<string> ReadNumberList(Excel.Worksheet ws, int startRow, int column)
        {
            List<string> numbers = new List<string>();
            int maxRows = ws.Rows.Count;

            for (int row = startRow; row <= maxRows; row++)
            {
                string text = ReadCellText(ws, row, column);
                if (string.IsNullOrWhiteSpace(text))
                    break;

                numbers.Add(text);
            }

            return numbers;
        }

        private List<SourceRow> ReadMatchedRows(Excel.Worksheet ws, int typeColumn, HashSet<string> typeSet, List<int> copyColumns)
        {
            List<SourceRow> rows = new List<SourceRow>();
            Excel.Range usedRange = null;

            try
            {
                usedRange = ws.UsedRange;
                int firstRow = usedRange.Row;
                int lastRow = usedRange.Row + usedRange.Rows.Count - 1;

                for (int row = firstRow; row <= lastRow; row++)
                {
                    string typeText = ReadCellText(ws, row, typeColumn);
                    if (!typeSet.Contains(typeText))
                        continue;

                    object[] values = new object[copyColumns.Count];
                    for (int i = 0; i < copyColumns.Count; i++)
                        values[i] = ReadCellValue(ws, row, copyColumns[i]);

                    rows.Add(new SourceRow(typeText, values));
                }
            }
            finally
            {
                ReleaseCom(usedRange);
            }

            return rows;
        }

        private void WriteOutputRows(Excel.Worksheet ws, int startRow, int startColumn, List<object[]> rows)
        {
            int rowCount = rows.Count;
            int colCount = 0;
            foreach (object[] row in rows)
            {
                if (row.Length > colCount)
                    colCount = row.Length;
            }

            object[,] output = new object[rowCount, colCount];
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < rows[row].Length; col++)
                    output[row, col] = rows[row][col];
            }

            Excel.Range startCell = null;
            Excel.Range endCell = null;
            Excel.Range targetRange = null;

            try
            {
                startCell = ws.Cells[startRow, startColumn] as Excel.Range;
                endCell = ws.Cells[startRow + rowCount - 1, startColumn + colCount - 1] as Excel.Range;
                targetRange = ws.Range[startCell, endCell];
                targetRange.Value2 = output;
            }
            finally
            {
                ReleaseCom(targetRange);
                ReleaseCom(endCell);
                ReleaseCom(startCell);
            }
        }

        private static void ClearWorksheetFromStart(Excel.Worksheet ws, int startRow, int startCol)
        {
            Excel.Range usedRange = null;
            Excel.Range startCell = null;
            Excel.Range endCell = null;
            Excel.Range clearRange = null;

            try
            {
                usedRange = ws.UsedRange;
                int usedLastRow = usedRange.Row + usedRange.Rows.Count - 1;
                int usedLastCol = usedRange.Column + usedRange.Columns.Count - 1;

                if (usedLastRow < startRow || usedLastCol < startCol)
                    return;

                startCell = ws.Cells[startRow, startCol] as Excel.Range;
                endCell = ws.Cells[usedLastRow, usedLastCol] as Excel.Range;
                clearRange = ws.Range[startCell, endCell];
                clearRange.Clear();
            }
            finally
            {
                ReleaseCom(clearRange);
                ReleaseCom(endCell);
                ReleaseCom(startCell);
                ReleaseCom(usedRange);
            }
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
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Excel 文件路径为空。");

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

        private static Excel.Workbook GetOrCreateWorkbook(Excel.Application app, string filePath, out bool created)
        {
            created = false;

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("目标 Excel 文件路径为空。");

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

            if (File.Exists(fullPath))
                return app.Workbooks.Open(fullPath);

            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            created = true;
            return app.Workbooks.Add();
        }

        private static Excel.Worksheet GetOrCreateWorksheet(Excel.Workbook wb, string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName))
                throw new ArgumentException("目标 Sheet 为空。");

            foreach (Excel.Worksheet sheet in wb.Worksheets)
            {
                if (string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return sheet;

                ReleaseCom(sheet);
            }

            Excel.Worksheet newSheet = wb.Worksheets.Add(After: wb.Worksheets[wb.Worksheets.Count]) as Excel.Worksheet;
            newSheet.Name = sheetName;
            return newSheet;
        }

        private static bool IsSamePath(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
                return false;

            try
            {
                return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void ParseCell(string cellText, out int row, out int column)
        {
            string text = (cellText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("单元格位置为空。");

            string letters = string.Empty;
            string numbers = string.Empty;

            foreach (char c in text)
            {
                if (char.IsLetter(c))
                    letters += c;
                else if (char.IsDigit(c))
                    numbers += c;
            }

            if (string.IsNullOrWhiteSpace(letters) || string.IsNullOrWhiteSpace(numbers))
                throw new ArgumentException("单元格位置格式错误: " + cellText);

            column = ParseColumn(letters);
            if (!int.TryParse(numbers, out row) || row < 1)
                throw new ArgumentException("单元格位置格式错误: " + cellText);
        }

        private static List<int> ParseColumns(string columnText)
        {
            List<int> columns = new List<int>();
            HashSet<int> seen = new HashSet<int>();
            string text = (columnText ?? string.Empty)
                .Replace(" ", "")
                .Replace("，", ",")
                .Replace("、", ",")
                .Replace("：", ":")
                .Replace("-", "~")
                .Replace(":", "~")
                .Trim();

            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("复制列为空。");

            string[] parts = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string[] range = part.Split(new[] { '~' }, StringSplitOptions.RemoveEmptyEntries);
                if (range.Length == 1)
                {
                    AddColumn(columns, seen, ParseColumn(range[0]));
                }
                else if (range.Length == 2)
                {
                    int start = ParseColumn(range[0]);
                    int end = ParseColumn(range[1]);
                    int step = start <= end ? 1 : -1;

                    for (int col = start; col != end + step; col += step)
                        AddColumn(columns, seen, col);
                }
                else
                {
                    throw new ArgumentException("复制列格式错误: " + part);
                }
            }

            if (columns.Count == 0)
                throw new ArgumentException("复制列为空。");

            return columns;
        }

        private static void AddColumn(List<int> columns, HashSet<int> seen, int column)
        {
            if (seen.Add(column))
                columns.Add(column);
        }

        private static int ParseColumn(string columnText)
        {
            string text = (columnText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("列号为空。");

            int number;
            if (int.TryParse(text, out number))
            {
                if (number < 1)
                    throw new ArgumentException("列号必须大于 0: " + text);
                return number;
            }

            int result = 0;
            foreach (char raw in text.ToUpperInvariant())
            {
                if (raw < 'A' || raw > 'Z')
                    throw new ArgumentException("列号格式错误: " + text);

                result = result * 26 + (raw - 'A' + 1);
            }

            return result;
        }

        private static HashSet<string> BuildTextSet(IEnumerable<string> sources)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sources == null)
                return set;

            foreach (string source in sources)
            {
                string text = (source ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    set.Add(text);
            }

            return set;
        }

        private static string ReadCellText(Excel.Worksheet ws, int row, int column)
        {
            object value = ReadCellValue(ws, row, column);
            return value == null ? string.Empty : value.ToString().Trim();
        }

        private static object ReadCellValue(Excel.Worksheet ws, int row, int column)
        {
            Excel.Range cell = null;

            try
            {
                cell = ws.Cells[row, column] as Excel.Range;
                return cell.Value2;
            }
            finally
            {
                ReleaseCom(cell);
            }
        }

        private static void ReleaseCom(object obj)
        {
            if (obj == null)
                return;

            try
            {
                if (Marshal.IsComObject(obj))
                    Marshal.ReleaseComObject(obj);
            }
            catch
            {
            }
        }

        private class SourceRow
        {
            public SourceRow(string typeText, object[] values)
            {
                TypeText = typeText;
                Values = values;
            }

            public string TypeText { get; private set; }
            public object[] Values { get; private set; }
        }
    }

    internal class CButton_ExcelFilterByNumber : GH_ComponentAttributes
    {
        public CButton_ExcelFilterByNumber(ExcelFilterByNumber component) : base(component) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 20.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);

            if (channel == GH_CanvasChannel.Objects)
            {
                GH_Palette palette = ((ExcelFilterByNumber)Owner).CurrentButtonColor == ExcelFilterByNumber.ButtonColor.Black
                    ? GH_Palette.Black
                    : GH_Palette.Grey;

                using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                {
                    capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                }
            }

            using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat stringFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("汇总", font, Brushes.White, buttonRect, stringFormat);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                ExcelFilterByNumber info = (ExcelFilterByNumber)Owner;
                info.CurrentButtonColor = ExcelFilterByNumber.ButtonColor.Grey;
                info.ExpireSolution(true);
                Thread.Sleep(50);
                info.CurrentButtonColor = ExcelFilterByNumber.ButtonColor.Black;

                info.Execute();

                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
