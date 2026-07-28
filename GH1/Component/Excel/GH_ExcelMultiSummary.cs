using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class GH_ExcelMultiSummary : GH_Component
    {
        private bool _triggerRun = false;
        private List<string> _lastLines = new List<string>();
        private bool _hasLastLines = false;
        private string _lastErrorMessage = null;
        private bool _hasLastError = false;

        public GH_ExcelMultiSummary()
          : base("GH数据汇总表", "数据汇总表",
              "自动填写数据汇总表",
              "Parrot", "Excel")
        {
        }

        protected override void AppendAdditionalComponentMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "运行", (s, e) =>
            {
                _triggerRun = true;
                ExpireSolution(true);
            });

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

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("SourcePath", "SP", "源Excel路径", GH_ParamAccess.item);
            p.AddTextParameter("IgnoreSheets", "IS", "忽略Sheet（可为空）", GH_ParamAccess.list);
            p.AddTextParameter(
                "ColumnDefinition",
                "CD",
                "列定义：支持复制/分组/求和\n" +
                "示例：A~D,(E~G),H~J,{K~M},L\n" +
                "()=分组列，{}=求和列，其它=复制列",
                GH_ParamAccess.item
            );
            p.AddIntegerParameter("StartRow", "SR", "起始行（从第N行开始）", GH_ParamAccess.item, 1);
            p.AddTextParameter("IgnoreKeywords", "IK", "忽略关键字：仅检查分组列；支持正则表达式，任意分组列单元格被正则完全匹配时忽略该行", GH_ParamAccess.list);
            p.AddTextParameter("SortColumns", "SC", "排序列，可为空；按输入列顺序排序输出，支持空格、中英文逗号，如 A，B", GH_ParamAccess.list);
            p.AddBooleanParameter("Run", "R", "执行；也可右键组件选择“运行”", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Lines", "L", "输出行 A|B|C", GH_ParamAccess.list);
            p.AddBooleanParameter("Done", "D", "计算完成后输出True脉冲，用于触发下游电池", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string srcPath = "";
            List<string> ignoreSheets = new List<string>();
            string colDefStr = "";
            int startRow = 1;
            List<string> ignoreKeys = new List<string>();
            List<string> sortColumns = new List<string>();
            bool run = false;

            if (!DA.GetData(0, ref srcPath)) return;
            DA.GetDataList(1, ignoreSheets);
            if (!DA.GetData(2, ref colDefStr)) return;
            if (!DA.GetData(3, ref startRow)) return;
            DA.GetDataList(4, ignoreKeys);
            DA.GetDataList(5, sortColumns);
            DA.GetData(6, ref run);

            if (!(run || _triggerRun))
            {
                DA.SetDataList(0, _hasLastLines ? _lastLines : new List<string> { "就绪：等待Run" });
                if (_hasLastError && !string.IsNullOrEmpty(_lastErrorMessage))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastErrorMessage);
                DA.SetData(1, false);
                return;
            }
            _triggerRun = false;

            try
            {
                if (string.IsNullOrWhiteSpace(srcPath))
                    throw new Exception("请输入源Excel路径");

                if (!File.Exists(srcPath))
                    throw new Exception("文件不存在：" + srcPath);

                var parsed = ParseColumns(colDefStr);
                var groupCols = parsed.groupCols;
                var sumCols = parsed.sumCols;
                var ordered = parsed.ordered;
                var sortIndexes = ParseSortIndexes(sortColumns, ordered);

                if (groupCols.Length == 0)
                    throw new Exception("必须指定分组列（用()标记）");

                List<string> lines = BuildLines(srcPath, ignoreSheets, ignoreKeys, startRow, groupCols, sumCols, ordered);
                SortLines(lines, sortIndexes);
                _lastLines = new List<string>(lines);
                _hasLastLines = true;
                _lastErrorMessage = null;
                _hasLastError = false;
                DA.SetDataList(0, lines);
                DA.SetData(1, true);
            }
            catch (Exception ex)
            {
                string message = "错误：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
                _lastLines = new List<string> { message };
                _hasLastLines = true;
                _lastErrorMessage = message;
                _hasLastError = true;
                DA.SetDataList(0, new List<string> { message });
                DA.SetData(1, false);
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private List<string> BuildLines(
            string srcPath,
            List<string> ignoreSheets,
            List<string> ignoreKeys,
            int startRow,
            int[] groupCols,
            int[] sumCols,
            List<(int col, int type)> ordered)
        {
            Excel.Application app = null;
            Excel.Workbook srcWb = null;
            bool appCreated = false;
            bool workbookOpenedByComponent = false;
            bool workbookAlreadyOpen = false;
            bool hasOriginalState = false;
            bool originalVisible = false;
            bool originalScreenUpdating = true;
            bool originalDisplayAlerts = true;

            try
            {
                try
                {
                    app = (Excel.Application)Marshal.GetActiveObject("Excel.Application");
                }
                catch
                {
                    app = new Excel.Application();
                    appCreated = true;
                }

                try
                {
                    originalVisible = app.Visible;
                    originalScreenUpdating = app.ScreenUpdating;
                    originalDisplayAlerts = app.DisplayAlerts;
                    hasOriginalState = true;
                }
                catch { }

                app.Visible = false;
                app.ScreenUpdating = false;
                app.DisplayAlerts = false;

                srcWb = FindOpenWorkbook(app, srcPath);
                workbookAlreadyOpen = srcWb != null;
                if (!workbookAlreadyOpen)
                {
                    srcWb = app.Workbooks.Open(srcPath);
                    workbookOpenedByComponent = true;
                }

                Dictionary<string, (Dictionary<int, object> vals, Dictionary<int, double> sums)> dict
                    = new Dictionary<string, (Dictionary<int, object>, Dictionary<int, double>)>();

                int baseCol = groupCols[0];

                foreach (Excel.Worksheet ws in srcWb.Sheets)
                {
                    try
                    {
                        if (ignoreSheets.Contains(ws.Name)) continue;

                        int lastRow = ((Excel.Range)ws.Cells[ws.Rows.Count, baseCol])
                            .End[Excel.XlDirection.xlUp].Row;

                        if (lastRow < startRow) continue;

                        for (int r = startRow; r <= lastRow; r++)
                        {
                            bool skip = false;

                            foreach (int c in groupCols)
                            {
                                var val = (ws.Cells[r, c] as Excel.Range)?.Value2;
                                if (val == null) continue;

                                string txt = val.ToString();
                                foreach (var key in ignoreKeys)
                                {
                                    if (IsFullRegexMatch(txt, key))
                                    {
                                        skip = true;
                                        break;
                                    }
                                }

                                if (skip) break;
                            }

                            if (skip) continue;

                            string keyStr = string.Join("|",
                                groupCols.Select(c =>
                                    (ws.Cells[r, c] as Excel.Range)?.Value2?.ToString() ?? "")
                            );

                            if (!dict.ContainsKey(keyStr))
                            {
                                var valDict = new Dictionary<int, object>();
                                var sumDict = new Dictionary<int, double>();

                                foreach (var (col, type) in ordered)
                                {
                                    if (type != 2)
                                    {
                                        valDict[col] = (ws.Cells[r, col] as Excel.Range)?.Value2;
                                    }
                                    else
                                    {
                                        sumDict[col] = 0;
                                    }
                                }

                                dict[keyStr] = (valDict, sumDict);
                            }

                            var entry = dict[keyStr];

                            foreach (int c in sumCols)
                            {
                                var v = (ws.Cells[r, c] as Excel.Range)?.Value2;
                                double d = 0;
                                if (v != null) double.TryParse(v.ToString(), out d);
                                entry.sums[c] += d;
                            }

                            dict[keyStr] = entry;
                        }
                    }
                    finally
                    {
                        Release(ws);
                    }
                }

                List<string> lines = new List<string>();
                foreach (var kv in dict)
                {
                    var vals = kv.Value.vals;
                    var sums = kv.Value.sums;
                    List<string> row = new List<string>();

                    foreach (var (col, type) in ordered)
                    {
                        if (type == 2)
                            row.Add(sums[col].ToString());
                        else
                            row.Add(vals[col]?.ToString() ?? "");
                    }

                    lines.Add(string.Join("|", row));
                }

                return lines;
            }
            finally
            {
                if (srcWb != null)
                {
                    try
                    {
                        if (workbookOpenedByComponent)
                            srcWb.Close(false);
                    }
                    catch { }
                    Release(srcWb);
                }

                if (app != null)
                {
                    try
                    {
                        app.DisplayAlerts = hasOriginalState ? originalDisplayAlerts : true;
                        app.ScreenUpdating = workbookAlreadyOpen ? true : (hasOriginalState ? originalScreenUpdating : true);
                        app.Visible = workbookAlreadyOpen ? true : (hasOriginalState ? originalVisible : false);
                    }
                    catch { }

                    if (appCreated)
                    {
                        try { app.Quit(); } catch { }
                    }

                    Release(app);
                }
            }
        }

        private List<int> ParseSortIndexes(List<string> sortColumns, List<(int col, int type)> ordered)
        {
            var result = new List<int>();
            if (sortColumns == null || sortColumns.Count == 0) return result;

            foreach (string input in sortColumns)
            {
                if (string.IsNullOrWhiteSpace(input)) continue;

                string normalized = input.Replace((char)0xFF0C, ',');
                var parts = normalized.Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string raw in parts)
                {
                    int col = ColumnNameToIndex(raw);
                    if (col <= 0) continue;

                    int outputIndex = ordered.FindIndex(item => item.col == col);
                    if (outputIndex >= 0 && !result.Contains(outputIndex))
                        result.Add(outputIndex);
                }
            }

            return result;
        }

        private void SortLines(List<string> lines, List<int> sortIndexes)
        {
            if (lines == null || lines.Count <= 1 || sortIndexes == null || sortIndexes.Count == 0) return;
            lines.Sort((a, b) => CompareLines(a, b, sortIndexes));
        }

        private int CompareLines(string a, string b, List<int> sortIndexes)
        {
            string[] av = (a ?? "").Split('|');
            string[] bv = (b ?? "").Split('|');

            foreach (int index in sortIndexes)
            {
                string left = index < av.Length ? av[index] : "";
                string right = index < bv.Length ? bv[index] : "";

                double leftNumber;
                double rightNumber;
                bool leftIsNumber = double.TryParse(left, out leftNumber);
                bool rightIsNumber = double.TryParse(right, out rightNumber);

                int compare = leftIsNumber && rightIsNumber
                    ? leftNumber.CompareTo(rightNumber)
                    : string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase);

                if (compare != 0) return compare;
            }

            return 0;
        }

        private bool IsFullRegexMatch(string text, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            return Regex.IsMatch(text ?? "", "\\A(?:" + pattern + ")\\z");
        }

        private Excel.Workbook FindOpenWorkbook(Excel.Application app, string path)
        {
            string fullPath = NormalizeFullPath(path);

            foreach (Excel.Workbook wb in app.Workbooks)
            {
                try
                {
                    if (string.Equals(NormalizeFullPath(wb.FullName), fullPath, StringComparison.OrdinalIgnoreCase))
                        return wb;

                    Release(wb);
                }
                catch
                {
                    Release(wb);
                }
            }

            return null;
        }

        private string NormalizeFullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path ?? ""; }
        }

        private (int[] copyCols, int[] groupCols, int[] sumCols, List<(int col, int type)> ordered) ParseColumns(string input)
        {
            SummaryColumnRules parsed = SummaryColumnRuleParser.Parse(input);
            return (
                parsed.CopyColumns.ToArray(),
                parsed.ClassColumns.ToArray(),
                parsed.SumColumns.ToArray(),
                parsed.Ordered.Select(item => (item.Column, (int)item.Type)).ToList());
        }

        private int ColumnNameToIndex(string col)
        {
            return SummaryColumnRuleParser.ColumnNameToIndex(col);
        }

        private void Release(object o)
        {
            try { if (o != null) Marshal.ReleaseComObject(o); } catch { }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ai_GH_ExcelMultiSummary"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("58B5063F-1C06-4594-85D3-3DEF8F58C067"); }
        }
    }
}
