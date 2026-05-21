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

        public static int MergeSameRangeFromSheets(string sourcePath, IEnumerable<string> ignoreSheetNames, string rangeText, string targetPath, string targetSheetName)
        {
            return MergeSameRangeFromSheets(sourcePath, ignoreSheetNames, rangeText, targetPath, targetSheetName, "A1", null);
        }

        public static int MergeSameRangeFromSheets(string sourcePath, IEnumerable<string> ignoreSheetNames, string rangeText, string targetPath, string targetSheetName, string targetStartCellText)
        {
            return MergeSameRangeFromSheets(sourcePath, ignoreSheetNames, rangeText, targetPath, targetSheetName, targetStartCellText, null);
        }

        public static int MergeSameRangeFromSheets(string sourcePath, IEnumerable<string> ignoreSheetNames, string rangeText, string targetPath, string targetSheetName, string targetStartCellText, IEnumerable<string> emptyTexts)
        {
            Excel.Application app = null;
            Excel.Workbook sourceWb = null;
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

                sourceWb = GetOrOpenWorkbook(app, sourcePath);
                targetWb = GetOrCreateWorkbook(app, targetPath, out targetCreated);
                targetWs = GetOrCreateWorksheet(targetWb, targetSheetName);
                int startRow;
                int startCol;
                ParseCell(targetStartCellText, out startRow, out startCol);
                ClearWorksheetFromStart(targetWs, startRow, startCol);

                HashSet<string> ignored = BuildIgnoredSheetSet(ignoreSheetNames);
                HashSet<string> emptyTextSet = BuildEmptyTextSet(emptyTexts);
                int targetRow = startRow;
                int copiedRows = 0;
                int sheetCount = sourceWb.Worksheets.Count;

                for (int sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
                {
                    Excel.Worksheet sourceWs = null;
                    Excel.Range sourceRange = null;

                    try
                    {
                        sourceWs = sourceWb.Worksheets[sheetIndex] as Excel.Worksheet;
                        if (sourceWs == null)
                            continue;

                        if (ignored.Contains(sourceWs.Name))
                            continue;

                        if (IsSameWorkbook(sourceWb, targetWb) && string.Equals(sourceWs.Name, targetSheetName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        sourceRange = GetSourceRange(sourceWs, rangeText);
                        if (sourceRange == null || sourceRange.Rows.Count == 0 || sourceRange.Columns.Count == 0)
                            continue;

                        copiedRows += WriteSourceValuesWithSourceNameSkippingEmptyRows(sourceRange, sourceWs.Name, targetWs, ref targetRow, startCol, emptyTextSet);
                    }
                    finally
                    {
                        ReleaseCom(sourceRange);
                        ReleaseCom(sourceWs);
                    }
                }

                if (targetCreated)
                    targetWb.SaveAs(Path.GetFullPath(targetPath));
                else
                    targetWb.Save();
                return copiedRows;
            }
            finally
            {
                ReleaseCom(targetWs);
                ReleaseCom(targetWb);
                ReleaseCom(sourceWb);
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

        private static Excel.Workbook GetOrCreateWorkbook(Excel.Application app, string filePath, out bool created)
        {
            created = false;

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("写入Excel文件路径为空。");

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

        private static bool IsSameWorkbook(Excel.Workbook first, Excel.Workbook second)
        {
            if (first == null || second == null)
                return false;

            try
            {
                return string.Equals(Path.GetFullPath(first.FullName), Path.GetFullPath(second.FullName), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return ReferenceEquals(first, second);
            }
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

        private static Excel.Worksheet GetOrCreateWorksheet(Excel.Workbook wb, string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName))
                throw new ArgumentException("写入SheetName为空。");

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

        private static void ClearWorksheet(Excel.Worksheet ws)
        {
            Excel.Range usedRange = null;
            try
            {
                usedRange = ws.UsedRange;
                usedRange.Clear();
            }
            finally
            {
                ReleaseCom(usedRange);
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

        private static HashSet<string> BuildIgnoredSheetSet(IEnumerable<string> ignoreSheetNames)
        {
            HashSet<string> ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ignoreSheetNames == null)
                return ignored;

            foreach (string source in ignoreSheetNames)
            {
                string text = (source ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    ignored.Add(text);
            }

            return ignored;
        }

        private static HashSet<string> BuildEmptyTextSet(IEnumerable<string> emptyTexts)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (emptyTexts == null)
                return set;

            foreach (string source in emptyTexts)
            {
                string text = (source ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    set.Add(text);
            }

            return set;
        }

        private static int WriteSourceValuesWithSourceNameSkippingEmptyRows(Excel.Range sourceRange, string sourceName, Excel.Worksheet targetWs, ref int targetRow, int startCol, HashSet<string> emptyTexts)
        {
            int rowCount = sourceRange.Rows.Count;
            int colCount = sourceRange.Columns.Count;
            object[,] values = GetRangeValues(sourceRange, rowCount, colCount);
            List<object[]> rows = new List<object[]>();

            for (int row = 1; row <= rowCount; row++)
            {
                if (IsFirstValueEmpty(values, row, emptyTexts))
                    continue;

                object[] rowValues = new object[colCount + 1];
                rowValues[0] = sourceName;

                for (int col = 1; col <= colCount; col++)
                    rowValues[col] = values[row, col];

                rows.Add(rowValues);
            }

            if (rows.Count == 0)
                return 0;

            object[,] output = new object[rows.Count, colCount + 1];
            for (int row = 0; row < rows.Count; row++)
            {
                for (int col = 0; col <= colCount; col++)
                    output[row, col] = rows[row][col];
            }

            Excel.Range startCell = null;
            Excel.Range endCell = null;
            Excel.Range targetRange = null;

            try
            {
                startCell = targetWs.Cells[targetRow, startCol] as Excel.Range;
                endCell = targetWs.Cells[targetRow + rows.Count - 1, startCol + colCount] as Excel.Range;
                targetRange = targetWs.Range[startCell, endCell];
                targetRange.Value2 = output;
            }
            finally
            {
                ReleaseCom(targetRange);
                ReleaseCom(endCell);
                ReleaseCom(startCell);
            }

            targetRow += rows.Count;
            return rows.Count;
        }

        private static object[,] GetRangeValues(Excel.Range range, int rowCount, int colCount)
        {
            object raw = range.Value2;
            object[,] values = raw as object[,];

            if (values != null)
                return values;

            object[,] singleValue = new object[rowCount + 1, colCount + 1];
            singleValue[1, 1] = raw;
            return singleValue;
        }

        private static bool IsFirstValueEmpty(object[,] values, int row, HashSet<string> emptyTexts)
        {
            object value = values[row, 1];
            if (value == null)
                return true;

            string text = value.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) || (emptyTexts != null && emptyTexts.Contains(text));
        }

        private static Excel.Range GetSourceRange(Excel.Worksheet ws, string rangeText)
        {
            string text = (rangeText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("要合并的区域为空。");

            int startRow;
            int endRow;
            if (TryParseRowRange(text, out startRow, out endRow))
            {
                Excel.Range usedRange = null;
                Excel.Range startCell = null;
                Excel.Range endCell = null;

                try
                {
                    usedRange = ws.UsedRange;
                    int startCol = usedRange.Column;
                    int endCol = usedRange.Column + usedRange.Columns.Count - 1;

                    startCell = ws.Cells[startRow, startCol] as Excel.Range;
                    endCell = ws.Cells[endRow, endCol] as Excel.Range;
                    return ws.Range[startCell, endCell];
                }
                finally
                {
                    ReleaseCom(endCell);
                    ReleaseCom(startCell);
                    ReleaseCom(usedRange);
                }
            }

            return ws.Range[text.Replace("~", ":")];
        }

        private static bool TryParseRowRange(string text, out int startRow, out int endRow)
        {
            startRow = 0;
            endRow = 0;

            string normalized = text.Replace("：", ":").Replace("-", "~").Replace(":", "~").Replace("行", "");
            string[] parts = normalized.Split(new[] { '~' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return false;

            if (!int.TryParse(parts[0].Trim(), out startRow) || !int.TryParse(parts[1].Trim(), out endRow))
                return false;

            if (startRow < 1 || endRow < 1)
                throw new ArgumentException("行号必须大于0: " + text);

            if (startRow > endRow)
            {
                int temp = startRow;
                startRow = endRow;
                endRow = temp;
            }

            return true;
        }

        private static void ParseCell(string cellText, out int row, out int column)
        {
            string text = (cellText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                text = "A1";

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
                throw new ArgumentException("开始写入位置格式错误: " + cellText);

            column = ParseColumn(letters);
            if (!int.TryParse(numbers, out row) || row < 1)
                throw new ArgumentException("开始写入位置格式错误: " + cellText);
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
