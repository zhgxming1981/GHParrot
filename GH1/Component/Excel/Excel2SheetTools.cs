using Excel = Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Grasshopper.Kernel.Types;

namespace NS_Parrot
{
    internal sealed class Excel2ExecutionContext
    {
        public Excel2ExecutionContext(Excel.Workbook workbook, Excel.Worksheet sheet, long sequence)
        {
            Workbook = workbook;
            Sheet = sheet;
            Sequence = sequence;
        }

        public Excel.Workbook Workbook { get; }
        public Excel.Worksheet Sheet { get; }
        public long Sequence { get; }

        public Excel2ExecutionContext Next(Excel.Worksheet sheet = null)
        {
            return new Excel2ExecutionContext(Workbook, sheet ?? Sheet, Sequence + 1);
        }
    }

    internal static class Excel2SheetTools
    {
        public static object Unwrap(object reference)
        {
            GH_ObjectWrapper wrapper = reference as GH_ObjectWrapper;
            if (wrapper != null)
                return wrapper.Value;

            IGH_Goo goo = reference as IGH_Goo;
            if (goo != null)
            {
                object value = goo.ScriptVariable();
                if (!ReferenceEquals(value, reference))
                    return value;
            }

            return reference;
        }

        public static Excel2ExecutionContext GetContext(object reference)
        {
            reference = Unwrap(reference);
            Excel2ExecutionContext context = reference as Excel2ExecutionContext;
            if (context == null)
                throw new ArgumentException("执行引用无效，请从“打开Excel V2”电池开始连接。");
            return context;
        }

        public static Excel.Worksheet GetSheet(object reference)
        {
            reference = Unwrap(reference);
            Excel2ExecutionContext context = reference as Excel2ExecutionContext;
            if (context != null)
            {
                if (context.Sheet == null)
                    throw new ArgumentException("执行引用尚未设置当前 Sheet。");
                return context.Sheet;
            }

            Excel.Worksheet sheet = reference as Excel.Worksheet;
            if (sheet == null)
                throw new ArgumentException("当前Sheet引用不是有效的 Excel 工作表引用。");
            return sheet;
        }

        public static void Show(Excel.Worksheet sheet)
        {
            sheet.Activate();
            Excel.Application app = sheet.Application;
            app.Visible = true;
            app.ScreenUpdating = true;
        }

        public static int GetNextAppendRow(Excel.Worksheet sheet, IEnumerable<string> columns, int startRow)
        {
            if (startRow < 1) startRow = 1;
            List<int> parsedColumns = ParseColumns(columns);
            for (int row = startRow; row <= sheet.Rows.Count; row++)
            {
                bool empty = true;
                foreach (int column in parsedColumns)
                {
                    Excel.Range cell = null;
                    try
                    {
                        cell = sheet.Cells[row, column] as Excel.Range;
                        object value = cell.Value2;
                        if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                        {
                            empty = false;
                            break;
                        }
                    }
                    finally { Release(cell); }
                }
                if (empty) return row;
            }
            return sheet.Rows.Count + 1;
        }

        public static List<string> ReadRows(Excel.Worksheet sheet, string rangeText)
        {
            if (string.IsNullOrWhiteSpace(rangeText)) throw new ArgumentException("读取区域为空。");
            Excel.Range range = null;
            try
            {
                range = sheet.Range[rangeText];
                object[,] values = range.Value2 as object[,];
                List<string> rows = new List<string>();
                if (values == null)
                {
                    object value = range.Value2;
                    rows.Add(value == null ? string.Empty : value.ToString());
                    return rows;
                }
                for (int row = 1; row <= values.GetLength(0); row++)
                {
                    string[] parts = new string[values.GetLength(1)];
                    for (int col = 1; col <= values.GetLength(1); col++)
                        parts[col - 1] = values[row, col] == null ? string.Empty : values[row, col].ToString();
                    rows.Add(string.Join("|", parts));
                }
                return rows;
            }
            finally { Release(range); }
        }

        public static void WriteRows(Excel.Worksheet sheet, string startCell, IList<string> lines, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(startCell)) throw new ArgumentException("起始位置为空。");
            List<string[]> rows = new List<string[]>();
            int colCount = 0;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] values = line.Split('|');
                rows.Add(values);
                colCount = Math.Max(colCount, values.Length);
            }
            if (rows.Count == 0 || colCount == 0) throw new ArgumentException("没有可写入的数据。");

            Excel.Range start = null;
            Excel.Range first = null;
            Excel.Range last = null;
            Excel.Range output = null;
            try
            {
                start = sheet.Range[startCell];
                int row = start.Row;
                int column = start.Column;
                if (!overwrite) row = Math.Max(row, GetNextAppendRow(sheet, new[] { ColumnName(column) }, row));
                first = sheet.Cells[row, column] as Excel.Range;
                last = sheet.Cells[row + rows.Count - 1, column + colCount - 1] as Excel.Range;
                output = sheet.Range[first, last];
                if (overwrite) output.Clear();
                object[,] values = new object[rows.Count, colCount];
                for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < rows[r].Length; c++) values[r, c] = rows[r][c];
                output.Value2 = values;
                Excel.Workbook workbook = sheet.Parent as Excel.Workbook;
                workbook?.Save();
            }
            finally
            {
                Release(output); Release(last); Release(first); Release(start);
            }
        }

        public static List<int> ParseColumns(IEnumerable<string> values)
        {
            List<int> result = new List<int>();
            if (values == null) throw new ArgumentException("没有输入列号。");
            foreach (string raw in values)
            {
                foreach (string part in (raw ?? string.Empty).Replace("，", ",").Split(','))
                {
                    string text = part.Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    result.Add(ParseColumn(text));
                }
            }
            if (result.Count == 0) throw new ArgumentException("没有输入列号。");
            return result;
        }

        public static int ParseColumn(string text)
        {
            int number;
            if (int.TryParse(text, out number) && number > 0) return number;
            int value = 0;
            foreach (char character in (text ?? string.Empty).Trim().ToUpperInvariant())
            {
                if (character < 'A' || character > 'Z') throw new ArgumentException("列号格式错误: " + text);
                value = value * 26 + character - 'A' + 1;
            }
            if (value == 0) throw new ArgumentException("列号为空。");
            return value;
        }

        private static string ColumnName(int column)
        {
            string name = string.Empty;
            while (column > 0) { column--; name = (char)('A' + column % 26) + name; column /= 26; }
            return name;
        }

        public static void Release(object value)
        {
            try { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); } catch { }
        }
    }
}
