using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class GH_ExcelSummary : GH_Component
    {
        private bool _triggerRun = false;

        public GH_ExcelSummary()
          : base("Excel分类汇总", "Excel汇总",
              "对Excel中指定列进行分类汇总，支持多列分类、多列汇总、自动编号",
              "Parrot", "Excel")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("文件路径", "路径", "Excel文件完整路径", GH_ParamAccess.item);
            p.AddTextParameter("Sheet名", "Sheet", "目标工作表名称，为空则使用活动Sheet", GH_ParamAccess.item);
            p.AddTextParameter(
                "分类列",
                "分类列",
                "分类与汇总列定义，格式同 GH_ExcelMultiSummary 的 CD 端口：A~D,(E~G),H~J,{K~M},L；()=分类列，{}=汇总列，其它=复制列",
                GH_ParamAccess.item
            );
            p.AddTextParameter("新编号列", "编号列", "新编号写回此列，留空则跳过编号", GH_ParamAccess.item);
            p.AddTextParameter("编号前缀", "前缀", "带双引号如\"A\"=固定前缀A，不带引号如A=从A列读取前缀", GH_ParamAccess.item);
            p.AddIntegerParameter("编号长度", "长度", "编号数字补齐长度，0=不补齐", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("起始编号", "起始", "起始序号，默认1", GH_ParamAccess.item, 1);
            p.AddTextParameter("连字符", "连字", "前缀与数字间的连接符，列前缀模式时生效", GH_ParamAccess.item);
            p.AddBooleanParameter("Run", "Run", "上升沿触发执行", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("状态", "状态", "执行结果信息", GH_ParamAccess.item);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "运行", (s, e) => { _triggerRun = true; ExpireSolution(true); });
            Menu_AppendItem(menu, "显示Excel", (s, e) =>
            {
                try
                {
                    var app = (Excel.Application)Marshal.GetActiveObject("Excel.Application");
                    app.Visible = true;
                    app.ScreenUpdating = true;
                }
                catch { }
            });
            base.AppendAdditionalComponentMenuItems(menu);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            try
            {
                string filePath = null;
                string sheetName = null;
                string columnRule = null;
                string numberColumn = null;
                string prefixInput = null;
                int numberWidth = 0;
                int startNo = 1;
                string connectString = "";
                bool run = false;

                if (!DA.GetData(0, ref filePath) || string.IsNullOrEmpty(filePath))
                {
                    DA.SetData(0, "错误：请输入文件路径");
                    return;
                }
                DA.GetData(1, ref sheetName);
                if (!DA.GetData(2, ref columnRule) || string.IsNullOrEmpty(columnRule))
                {
                    DA.SetData(0, "错误：请输入分类列");
                    return;
                }
                DA.GetData(3, ref numberColumn);
                DA.GetData(4, ref prefixInput);
                DA.GetData(5, ref numberWidth);
                DA.GetData(6, ref startNo);
                DA.GetData(7, ref connectString);
                DA.GetData(8, ref run);

                if (!_triggerRun && !run)
                {
                    DA.SetData(0, "就绪");
                    return;
                }
                _triggerRun = false;

                if (!System.IO.File.Exists(filePath))
                {
                    DA.SetData(0, "错误：文件不存在");
                    return;
                }

                var parsed = ParseColumnRules(columnRule);
                var copyColumns = parsed.copyColumns;
                var classColumns = parsed.classColumns;
                var sumColumns = parsed.sumColumns;
                if (classColumns.Count == 0)
                {
                    DA.SetData(0, "错误：分类列中缺少分类列（用()标记）");
                    return;
                }
                if (sumColumns.Count == 0)
                {
                    DA.SetData(0, "错误：分类列中缺少汇总列（用{}标记）");
                    return;
                }

                bool useFixedPrefix = false;
                string fixedPrefix = "";
                int prefixColumnIndex = -1;
                bool hasNumbering = !string.IsNullOrWhiteSpace(numberColumn);

                if (hasNumbering)
                {
                    if (string.IsNullOrEmpty(prefixInput))
                    {
                        DA.SetData(0, "错误：新编号列非空时，编号前缀不能为空");
                        return;
                    }
                    if (prefixInput.StartsWith("\"") && prefixInput.EndsWith("\""))
                    {
                        useFixedPrefix = true;
                        fixedPrefix = prefixInput.Substring(1, prefixInput.Length - 2);
                    }
                    else
                    {
                        useFixedPrefix = false;
                        prefixColumnIndex = ColumnNameToIndex(prefixInput.Trim());
                    }
                }

                Excel.Application app = null;
                Excel.Workbook wb = null;

                try
                {
                    app = new Excel.Application();
                    app.Visible = false;
                    app.ScreenUpdating = false;
                    app.DisplayAlerts = false;
                    app.EnableEvents = false;
                    app.Calculation = Excel.XlCalculation.xlCalculationManual;

                    wb = app.Workbooks.Open(filePath);
                    Excel.Worksheet ws = GetSheet(wb, sheetName);
                    if (ws == null)
                    {
                        DA.SetData(0, "错误：未找到Sheet \"" + sheetName + "\"");
                        return;
                    }

                    ws.Activate();
                    int lastRow = ws.UsedRange.Rows.Count;
                    if (lastRow <= 1)
                    {
                        DA.SetData(0, "错误：Sheet中无数据");
                        return;
                    }

                    SortSheet(ws, classColumns);
                    string resultMsg = BuildSummary(wb, ws, copyColumns, classColumns, sumColumns, parsed.ordered,
                        hasNumbering, numberColumn, useFixedPrefix, fixedPrefix,
                        prefixColumnIndex, numberWidth, startNo, connectString);
                    wb.Save();
                    DA.SetData(0, resultMsg);
                }
                finally
                {
                    if (app != null)
                    {
                        app.ScreenUpdating = true;
                        app.DisplayAlerts = true;
                        app.EnableEvents = true;
                        app.Calculation = Excel.XlCalculation.xlCalculationAutomatic;
                    }
                    ReleaseComObject(wb);
                    if (app != null)
                    {
                        app.Quit();
                        ReleaseComObject(app);
                    }
                }
            }
            catch (Exception ex)
            {
                DA.SetData(0, "错误：" + ex.Message);
            }
        }

        private (List<int> copyColumns, List<int> classColumns, List<int> sumColumns, List<(int col, int type)> ordered) ParseColumnRules(string rule)
        {
            var copyCols = new List<int>();
            var classCols = new List<int>();
            var sumCols = new List<int>();
            var ordered = new List<(int col, int type)>();

            if (string.IsNullOrWhiteSpace(rule))
                return (copyCols, classCols, sumCols, ordered);

            rule = rule.Replace((char)0xFF0C, ',');
            var parts = rule.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in parts)
            {
                string part = raw.Trim();
                if (string.IsNullOrEmpty(part)) continue;

                int type = 0; // 0=复制列，1=分类列，2=汇总列

                if (part.StartsWith("(") && part.EndsWith(")"))
                {
                    type = 1;
                    part = part.Substring(1, part.Length - 2).Trim();
                }
                else if (part.StartsWith("{") && part.EndsWith("}"))
                {
                    type = 2;
                    part = part.Substring(1, part.Length - 2).Trim();
                }

                if (part.Contains("~"))
                {
                    var rangeParts = part.Split('~');
                    if (rangeParts.Length != 2) continue;

                    int fromCol = ColumnNameToIndex(rangeParts[0].Trim());
                    int toCol = ColumnNameToIndex(rangeParts[1].Trim());
                    if (fromCol <= 0 || toCol <= 0) continue;

                    int start = Math.Min(fromCol, toCol);
                    int end = Math.Max(fromCol, toCol);

                    for (int c = start; c <= end; c++)
                    {
                        AddParsedColumn(c, type, copyCols, classCols, sumCols, ordered);
                    }
                }
                else
                {
                    AddParsedColumn(ColumnNameToIndex(part), type, copyCols, classCols, sumCols, ordered);
                }
            }

            copyCols = copyCols.Distinct().ToList();
            classCols = classCols.Distinct().ToList();
            sumCols = sumCols.Distinct().ToList();

            foreach (var col in sumCols.ToList())
            {
                copyCols.Remove(col);
                classCols.Remove(col);
            }

            foreach (var col in classCols.ToList())
            {
                copyCols.Remove(col);
            }

            return (copyCols, classCols, sumCols, ordered);
        }

        private void AddParsedColumn(int col, int type, List<int> copyCols, List<int> classCols, List<int> sumCols, List<(int col, int type)> ordered)
        {
            if (col <= 0) return;

            if (type == 2) sumCols.Add(col);
            else if (type == 1) classCols.Add(col);
            else copyCols.Add(col);

            ordered.Add((col, type));
        }

        private int ColumnNameToIndex(string colName)
        {
            colName = colName.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(colName)) return 0;

            int result = 0;
            foreach (char c in colName)
            {
                if (c < 'A' || c > 'Z') return 0;
                result = result * 26 + (c - 'A' + 1);
            }
            return result;
        }

        private string IndexToColumnName(int index)
        {
            string result = "";
            while (index > 0)
            {
                int remainder = (index - 1) % 26;
                result = (char)('A' + remainder) + result;
                index = (index - 1) / 26;
            }
            return result;
        }

        private Excel.Worksheet GetSheet(Excel.Workbook wb, string name)
        {
            if (string.IsNullOrEmpty(name))
                return (Excel.Worksheet)wb.ActiveSheet;

            foreach (Excel.Worksheet ws in wb.Sheets)
            {
                if (ws.Name == name) return ws;
            }
            return null;
        }

        private void SortSheet(Excel.Worksheet ws, List<int> classColumns)
        {
            int usedRows = ws.UsedRange.Rows.Count;
            int usedCols = ws.UsedRange.Columns.Count;
            int firstRow = ws.UsedRange.Row;

            ws.Sort.SortFields.Clear();

            foreach (int col in classColumns)
            {
                var keyRange = ws.Range[ws.Cells[firstRow, col], ws.Cells[firstRow + usedRows - 1, col]];
                ws.Sort.SortFields.Add(keyRange,
                    Excel.XlSortOn.xlSortOnValues,
                    Excel.XlSortOrder.xlAscending,
                    Excel.XlSortDataOption.xlSortNormal);
            }

            var sortRange = ws.Range[ws.Cells[firstRow, 1], ws.Cells[firstRow + usedRows - 1, usedCols]];
            ws.Sort.SetRange(sortRange);
            ws.Sort.Header = Excel.XlYesNoGuess.xlYes;
            ws.Sort.Orientation = Excel.XlSortOrientation.xlSortColumns;
            ws.Sort.SortMethod = Excel.XlSortMethod.xlPinYin;
            ws.Sort.MatchCase = false;
            ws.Sort.Apply();
        }

        private string BuildSummary(Excel.Workbook wb, Excel.Worksheet sourceSheet,
            List<int> copyColumns, List<int> classColumns, List<int> sumColumns, List<(int col, int type)> ordered,
            bool hasNumbering, string numberColumnName,
            bool useFixedPrefix, string fixedPrefix,
            int prefixColumnIndex, int numberWidth, int startNo, string connectString)
        {
            int lastRow = sourceSheet.UsedRange.Rows.Count;

            object[,] prefixData = null;
            if (hasNumbering && !useFixedPrefix)
            {
                prefixData = (object[,])sourceSheet.Range[
                    sourceSheet.Cells[1, prefixColumnIndex],
                    sourceSheet.Cells[lastRow, prefixColumnIndex]].Value2;
            }

            var summaryRows = new List<SummaryRow>();
            object[] prevClassValues = null;
            SummaryRow currentGroup = null;
            int numberCounter = startNo;
            string lastPrefixText = null;

            for (int row = 2; row <= lastRow; row++)
            {
                var currentValues = classColumns
                    .Select(col => (sourceSheet.Cells[row, col] as Excel.Range)?.Value2)
                    .ToArray();

                bool isNewGroup = (row == 2) || !ValuesEqual(prevClassValues, currentValues);

                if (isNewGroup)
                {
                    currentGroup = new SummaryRow();
                    currentGroup.Values = new Dictionary<int, object>();
                    currentGroup.SumValues = new Dictionary<int, double>();

                    foreach (var item in ordered)
                    {
                        object value = (sourceSheet.Cells[row, item.col] as Excel.Range)?.Value2;
                        if (item.type == 2)
                            currentGroup.SumValues[item.col] = ToDouble(value);
                        else
                            currentGroup.Values[item.col] = value;
                    }

                    if (hasNumbering)
                    {
                        if (!useFixedPrefix)
                        {
                            string currentPrefixText = GetCellText(prefixData[row, 1]);
                            if (row == 2 || currentPrefixText != lastPrefixText)
                            {
                                numberCounter = 1;
                            }
                            currentGroup.NumberText = BuildNumberText(currentPrefixText, numberCounter, numberWidth, connectString);
                            numberCounter++;
                            lastPrefixText = currentPrefixText;
                        }
                        else
                        {
                            currentGroup.NumberText = BuildNumberText(fixedPrefix, numberCounter, numberWidth, connectString);
                            numberCounter++;
                        }
                    }

                    summaryRows.Add(currentGroup);
                }
                else
                {
                    foreach (int col in sumColumns)
                    {
                        object value = (sourceSheet.Cells[row, col] as Excel.Range)?.Value2;
                        if (!currentGroup.SumValues.ContainsKey(col))
                            currentGroup.SumValues[col] = 0;
                        currentGroup.SumValues[col] += ToDouble(value);
                    }
                }

                if (hasNumbering && currentGroup != null)
                {
                    int numCol = ColumnNameToIndex(numberColumnName);
                    sourceSheet.Cells[row, numCol].Value2 = currentGroup.NumberText;
                }

                prevClassValues = currentValues;
            }

            int sheetNo = 1;
            string baseName = sourceSheet.Name + "(汇总)";
            string newSheetName = baseName;
            while (true)
            {
                bool exists = false;
                foreach (Excel.Worksheet s in wb.Sheets)
                {
                    if (s.Name == newSheetName) { exists = true; break; }
                }
                if (!exists) break;
                sheetNo++;
                newSheetName = baseName.Replace("(汇总)", "") + "(" + sheetNo + ")";
            }

            var newSheet = (Excel.Worksheet)wb.Sheets.Add();
            newSheet.Name = newSheetName;

            int outRows = summaryRows.Count + 1;
            int outCols = ordered.Count + (hasNumbering ? 1 : 0);
            var outputData = new object[outRows, outCols];

            for (int i = 0; i < ordered.Count; i++)
                outputData[0, i] = sourceSheet.Cells[1, ordered[i].col].Value2;
            if (hasNumbering)
                outputData[0, outCols - 1] = "编号";

            for (int i = 0; i < summaryRows.Count; i++)
            {
                var row = summaryRows[i];
                for (int j = 0; j < ordered.Count; j++)
                {
                    var item = ordered[j];
                    if (item.type == 2)
                        outputData[i + 1, j] = row.SumValues.ContainsKey(item.col) ? row.SumValues[item.col] : 0;
                    else
                        outputData[i + 1, j] = row.Values.ContainsKey(item.col) ? row.Values[item.col] : null;
                }
                if (hasNumbering)
                    outputData[i + 1, outCols - 1] = row.NumberText;
            }

            var targetRange = newSheet.Range[
                newSheet.Cells[1, 1],
                newSheet.Cells[outRows, outCols]];
            targetRange.Value2 = outputData;

            return "完成：共" + summaryRows.Count + "组，" + (lastRow - 1) + "行";
        }

        private bool ValuesEqual(object[] a, object[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!object.Equals(a[i], b[i])) return false;
            }
            return true;
        }

        private double ToDouble(object value)
        {
            if (value == null || value == DBNull.Value) return 0.0;
            try { return Convert.ToDouble(value); }
            catch { return 0.0; }
        }

        private string GetCellText(object value)
        {
            if (value == null || value == DBNull.Value) return "";
            return Convert.ToString(value);
        }

        private string BuildNumberText(string prefix, int number, int width, string connect)
        {
            string numStr = width > 0 ? number.ToString().PadLeft(width, '0') : number.ToString();
            return prefix + connect + numStr;
        }

        private void ReleaseComObject(object obj)
        {
            try { if (obj != null) Marshal.ReleaseComObject(obj); }
            catch { }
        }

        private class SummaryRow
        {
            public Dictionary<int, object> Values { get; set; }
            public Dictionary<int, double> SumValues { get; set; }
            public string NumberText { get; set; }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ai_GH_ExcelSummary"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("D3F8A2B1-4C5E-4F6D-9A7B-8C2D1E3F4A5B"); }
        }
    }
}
