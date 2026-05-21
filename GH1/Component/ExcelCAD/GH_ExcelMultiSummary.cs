using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class GH_ExcelMultiSummary : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GH_ExcelMultiSummary class.
        /// </summary>
        public GH_ExcelMultiSummary()
          : base("ExcelMultiSummary", "数据汇总表",
              "自动填写数据汇总表",
              "Parrot", "ExcelCAD")
        {
        }
        private bool _triggerRun = false;
        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        

      





        // ===== 右键菜单 =====
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
                "()=分组列  {}=求和列  其它=复制列",
                GH_ParamAccess.item
            );

            p.AddIntegerParameter("StartRow", "SR", "起始行（从第N行开始）", GH_ParamAccess.item, 1);

            p.AddTextParameter("IgnoreKeywords", "IK", "忽略关键字（完全匹配）", GH_ParamAccess.list);

            p.AddBooleanParameter("Run", "R", "执行", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Lines", "L", "输出行 A|B|C", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string srcPath = "";
            List<string> ignoreSheets = new List<string>();
            string colDefStr = ""; // ⭐ 唯一列定义
            int startRow = 1;
            List<string> ignoreKeys = new List<string>();
            bool run = false;

            if (!DA.GetData(0, ref srcPath)) return;
            DA.GetDataList(1, ignoreSheets);
            if (!DA.GetData(2, ref colDefStr)) return;
            if (!DA.GetData(3, ref startRow)) return;
            DA.GetDataList(4, ignoreKeys);
            DA.GetData(5, ref run);

            if (!(run || _triggerRun))
            {
                DA.SetDataList(0, new List<string> { "未触发" });
                return;
            }
            _triggerRun = false;

            var parsed = ParseColumns(colDefStr);

            var copyCols = parsed.copyCols;
            var groupCols = parsed.groupCols;
            var sumCols = parsed.sumCols;
            var ordered = parsed.ordered;

            if (groupCols.Length == 0)
                throw new Exception("必须指定分组列（用()）");

            Excel.Application app = null;
            Excel.Workbook srcWb = null;
            bool appCreated = false;

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

                app.Visible = false;
                app.ScreenUpdating = false;
                app.DisplayAlerts = false;

                srcWb = app.Workbooks.Open(srcPath);

                Dictionary<string, (Dictionary<int, object> vals, Dictionary<int, double> sums)> dict
                    = new Dictionary<string, (Dictionary<int, object>, Dictionary<int, double>)>();

                int baseCol = groupCols[0];

                foreach (Excel.Worksheet ws in srcWb.Sheets)
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
                            if (val != null)
                            {
                                string txt = val.ToString();
                                foreach (var key in ignoreKeys)
                                    if (!string.IsNullOrEmpty(key) && txt.Equals(key))
                                        skip = true;
                            }
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
                                if (type != 2) // copy + group
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

                // ===== 输出 =====
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

                DA.SetDataList(0, lines);
            }
            catch (Exception ex)
            {
                DA.SetDataList(0, new List<string> { ex.Message });
            }
            finally
            {
                if (srcWb != null) Marshal.ReleaseComObject(srcWb);

                if (app != null && appCreated)
                {
                    app.Quit();
                    Marshal.ReleaseComObject(app);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }


        // 修改后的 ParseColumns，处理复制列与求和列
        // 解析输入列：A~D,(E~G),H,{K~M}
        private (int[] copyCols, int[] groupCols, int[] sumCols, List<(int col, int type)> ordered)
        ParseColumns(string input)
        {
            // type: 0=copy, 1=group, 2=sum
            var parts = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            List<int> copy = new List<int>();
            List<int> group = new List<int>();
            List<int> sum = new List<int>();
            List<(int col, int type)> ordered = new List<(int, int)>();

            foreach (var raw in parts)
            {
                string part = raw.Trim();

                int type = 0; // 默认 copy

                if (part.StartsWith("(") && part.EndsWith(")"))
                {
                    type = 1;
                    part = part.Trim('(', ')');
                }
                else if (part.StartsWith("{") && part.EndsWith("}"))
                {
                    type = 2;
                    part = part.Trim('{', '}');
                }

                // ===== 支持区间（关键点）=====
                if (part.Contains("~"))
                {
                    var sp = part.Split('~');
                    int s = ColumnNameToIndex(sp[0]);
                    int e = ColumnNameToIndex(sp[1]);

                    if (s > e) // 防止反写 K~A
                    {
                        int tmp = s;
                        s = e;
                        e = tmp;
                    }

                    for (int i = s; i <= e; i++)
                    {
                        if (type == 1) group.Add(i);
                        else if (type == 2) sum.Add(i);
                        else copy.Add(i);

                        ordered.Add((i, type));
                    }
                }
                else
                {
                    int col = ColumnNameToIndex(part);

                    if (type == 1) group.Add(col);
                    else if (type == 2) sum.Add(col);
                    else copy.Add(col);

                    ordered.Add((col, type));
                }
            }

            return (
                copy.Distinct().ToArray(),
                group.Distinct().ToArray(),
                sum.Distinct().ToArray(),
                ordered
            );
        }

        private int ColumnNameToIndex(string col)
        {
            int result = 0;
            foreach (char c in col.Trim().ToUpper())
                result = result * 26 + (c - 'A' + 1);
            return result;
        }

        private void ParseCell(string cell, out int row, out int col)
        {
            col = 0; row = 0;
            string letters = new string(cell.Where(char.IsLetter).ToArray());
            string nums = new string(cell.Where(char.IsDigit).ToArray());

            foreach (char c in letters.ToUpper())
                col = col * 26 + (c - 'A' + 1);

            row = int.Parse(nums);
        }

        private Excel.Worksheet GetSheet(Excel.Workbook wb, string name)
        {
            foreach (Excel.Worksheet ws in wb.Sheets)
                if (ws.Name == name) return ws;
            return null;
        }

        private void Release(object o)
        {
            try { if (o != null) Marshal.ReleaseComObject(o); } catch { }
        }

   







     

     

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return GeneratedIcon.Get("gen_ai_GH_ExcelMultiSummary");
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("58B5063F-1C06-4594-85D3-3DEF8F58C067"); }
        }
    }
}
