using Excel = Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace NS_Parrot
{
    internal static class ExcelPulseTools
    {
        public static void ClearRange(string filePath, string sheetName, string rangeText)
        {
            Excel.Application app = null;
            Excel.Workbook wb = null;
            Excel.Worksheet ws = null;
            Excel.Range range = null;
            bool appCreated = false;

            try
            {
                app = GetExcelApplication(out appCreated);
                wb = GetOrOpenWorkbook(app, filePath);
                ws = GetWorksheet(wb, sheetName);

                range = ws.Range[rangeText];
                range.ClearContents();
                wb.Save();
            }
            finally
            {
                ReleaseCom(range);
                ReleaseCom(ws);
                ReleaseCom(wb);
                if (app != null && appCreated)
                    app.Quit();
                ReleaseCom(app);
            }
        }

        public static int GetNextAppendRow(string filePath, string sheetName, IEnumerable<string> columnTexts, int startRow)
        {
            if (columnTexts == null)
                throw new ArgumentException("没有输入列号。");

            if (startRow < 1)
                startRow = 1;

            Excel.Application app = null;
            Excel.Workbook wb = null;
            Excel.Worksheet ws = null;
            bool appCreated = false;

            try
            {
                app = GetExcelApplication(out appCreated);
                wb = GetOrOpenWorkbook(app, filePath);
                ws = GetWorksheet(wb, sheetName);

                List<int> columns = ParseColumns(columnTexts);
                if (columns.Count == 0)
                    throw new ArgumentException("没有输入列号。");

                for (int row = startRow; row <= ws.Rows.Count; row++)
                {
                    if (IsRowEmpty(ws, row, columns))
                        return row;
                }

                return ws.Rows.Count + 1;
            }
            finally
            {
                ReleaseCom(ws);
                ReleaseCom(wb);
                if (app != null && appCreated)
                    app.Quit();
                ReleaseCom(app);
            }
        }

        public static void ShowExcel(string filePath, string sheetName)
        {
            Excel.Application app = null;
            Excel.Workbook wb = null;
            Excel.Worksheet ws = null;
            bool appCreated = false;

            try
            {
                app = GetExcelApplication(out appCreated);
                wb = GetOrOpenWorkbook(app, filePath);
                ws = GetWorksheet(wb, sheetName);
                ws.Activate();
                app.Visible = true;
                app.ScreenUpdating = true;
                app.DisplayAlerts = true;
                app.WindowState = Excel.XlWindowState.xlMaximized;
            }
            finally
            {
                ReleaseCom(ws);
                ReleaseCom(wb);
                ReleaseCom(app);
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
                throw new ArgumentException("Excel文件路径为空。");

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
                throw new FileNotFoundException("Excel文件不存在。", fullPath);

            return app.Workbooks.Open(fullPath);
        }

        private static Excel.Worksheet GetWorksheet(Excel.Workbook wb, string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName))
                throw new ArgumentException("SheetName为空。");

            foreach (Excel.Worksheet sheet in wb.Worksheets)
            {
                if (string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return sheet;

                ReleaseCom(sheet);
            }

            throw new ArgumentException("找不到Sheet: " + sheetName);
        }

        private static List<int> ParseColumns(IEnumerable<string> columnTexts)
        {
            List<int> columns = new List<int>();
            HashSet<int> seen = new HashSet<int>();

            foreach (string source in columnTexts)
            {
                string text = (source ?? string.Empty).Replace(" ", "").Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

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
                        throw new ArgumentException("列号格式错误: " + part);
                    }
                }
            }

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
                    throw new ArgumentException("列号必须大于0: " + text);
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

        private static bool IsRowEmpty(Excel.Worksheet ws, int row, IEnumerable<int> columns)
        {
            foreach (int column in columns)
            {
                Excel.Range cell = null;
                try
                {
                    cell = (Excel.Range)ws.Cells[row, column];
                    object value = cell.Value2;
                    if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                        return false;
                }
                finally
                {
                    ReleaseCom(cell);
                }
            }

            return true;
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
    }
}
