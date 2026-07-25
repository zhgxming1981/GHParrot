using Excel = Microsoft.Office.Interop.Excel;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

namespace NS_Parrot
{
    public class Excel2FilterByNumber : GH_Component
    {
        private long _lastSequence = -1;
        public Excel2FilterByNumber() : base("Excel按编号筛选 V2", "编号筛选V2", "从源工作簿全部 Sheet 筛选后写入目标 Sheet 引用", "Parrot", "Excel2") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("执行引用", "执行", "主链执行引用；其当前 Sheet 作为目标 Sheet", GH_ParamAccess.item);
            p.AddGenericParameter("源Sheet引用", "源Sheet", "源工作簿中的任一 Sheet 引用", GH_ParamAccess.item);
            p.AddTextParameter("忽略表", "忽略", "可为空", GH_ParamAccess.list);
            p.AddTextParameter("编号位", "编号位", "例如 R10", GH_ParamAccess.item);
            p.AddTextParameter("类型列", "类型列", "例如 A、B、AB", GH_ParamAccess.item);
            p.AddTextParameter("类型", "类型", "匹配类型", GH_ParamAccess.list);
            p.AddTextParameter("复制列", "复制列", "例如 H~L,O,P", GH_ParamAccess.item);
            p.AddTextParameter("位置", "位置", "默认 A1", GH_ParamAccess.item, "A1");
            p[2].Optional = p[7].Optional = true;
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddIntegerParameter("行数", "行数", "写入行数", GH_ParamAccess.item); p.AddBooleanParameter("完成", "完成", "执行完成", GH_ParamAccess.item); p.AddTextParameter("日志", "日志", "结果或错误", GH_ParamAccess.item); p.AddTextParameter("数据", "数据", "分类后的数据；每行格式为 A|B|C", GH_ParamAccess.list); p.AddGenericParameter("执行引用", "执行", "以目标 Sheet 继续的执行引用", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object reference = null, sourceReference = null; List<string> ignored = new List<string>(), types = new List<string>(); string numberCell = string.Empty, typeColumn = string.Empty, copyColumns = string.Empty, targetCell = "A1";
            DA.GetData(0, ref reference); DA.GetData(1, ref sourceReference); DA.GetDataList(2, ignored); DA.GetData(3, ref numberCell); DA.GetData(4, ref typeColumn); DA.GetDataList(5, types); DA.GetData(6, ref copyColumns); DA.GetData(7, ref targetCell);
            try
            {
                Excel2ExecutionContext context = Excel2SheetTools.GetContext(reference); if (context.Sequence == _lastSequence) return; _lastSequence = context.Sequence;
                Excel.Worksheet sourceSheet = Excel2SheetTools.GetSheet(sourceReference), targetSheet = Excel2SheetTools.GetSheet(context); Excel.Workbook sourceBook = sourceSheet.Parent as Excel.Workbook;
                ParseCell(numberCell, out int numberRow, out int numberColumn); int typeCol = Excel2SheetTools.ParseColumn(typeColumn); List<int> copyCols = ParseColumnRanges(copyColumns); HashSet<string> typeSet = Set(types), ignoredSet = Set(ignored); ignoredSet.Add(targetSheet.Name);
                List<string> output = new List<string>();
                foreach (Excel.Worksheet sheet in sourceBook.Worksheets)
                {
                    try
                    {
                        if (ignoredSet.Contains(sheet.Name)) continue;
                        List<string> numbers = ReadNumbers(sheet, numberRow, numberColumn);
                        foreach (int row in UsedRows(sheet))
                        {
                            if (!typeSet.Contains(Read(sheet, row, typeCol))) continue;
                            foreach (string number in numbers)
                            {
                                List<string> values = new List<string> { sheet.Name, number };
                                foreach (int col in copyCols) values.Add(Read(sheet, row, col));
                                output.Add(string.Join("|", values));
                            }
                        }
                    }
                    finally { if (!object.ReferenceEquals(sheet, sourceSheet)) Excel2SheetTools.Release(sheet); }
                }
                Excel2SheetTools.WriteRows(targetSheet, targetCell, output, true);
                DA.SetData(0, output.Count); DA.SetData(1, true); DA.SetData(2, "汇总完成，共写入 " + output.Count + " 行。"); DA.SetDataList(3, output); DA.SetData(4, context.Next(targetSheet));
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "汇总失败: " + ex.Message); DA.SetData(1, false); DA.SetData(2, "汇总失败: " + ex.Message); DA.SetDataList(3, new List<string>()); }
        }
        private static HashSet<string> Set(IEnumerable<string> texts) { HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (string text in texts) if (!string.IsNullOrWhiteSpace(text)) set.Add(text.Trim()); return set; }
        private static List<string> ReadNumbers(Excel.Worksheet sheet, int startRow, int column) { List<string> result = new List<string>(); for (int row = startRow; row <= sheet.Rows.Count; row++) { string text = Read(sheet, row, column); if (string.IsNullOrWhiteSpace(text)) break; result.Add(text); } return result; }
        private static IEnumerable<int> UsedRows(Excel.Worksheet sheet) { Excel.Range used = null; try { used = sheet.UsedRange; for (int row = used.Row; row < used.Row + used.Rows.Count; row++) yield return row; } finally { Excel2SheetTools.Release(used); } }
        private static string Read(Excel.Worksheet sheet, int row, int column) { Excel.Range cell = null; try { cell = sheet.Cells[row, column] as Excel.Range; object value = cell.Value2; return value == null ? string.Empty : value.ToString().Trim(); } finally { Excel2SheetTools.Release(cell); } }
        private static void ParseCell(string text, out int row, out int column) { string letters = string.Empty, numbers = string.Empty; foreach (char c in (text ?? string.Empty)) { if (char.IsLetter(c)) letters += c; if (char.IsDigit(c)) numbers += c; } column = Excel2SheetTools.ParseColumn(letters); if (!int.TryParse(numbers, out row) || row < 1) throw new ArgumentException("编号位格式错误: " + text); }
        private static List<int> ParseColumnRanges(string text) { List<int> result = new List<int>(); foreach (string part in (text ?? string.Empty).Replace("-", "~").Split(',')) { string[] bounds = part.Trim().Split('~'); int first = Excel2SheetTools.ParseColumn(bounds[0]); int last = bounds.Length == 1 ? first : Excel2SheetTools.ParseColumn(bounds[1]); for (int col = first; col != last + (first <= last ? 1 : -1); col += first <= last ? 1 : -1) if (!result.Contains(col)) result.Add(col); } if (result.Count == 0) throw new ArgumentException("复制列为空。"); return result; }
        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2FilterByNumber"); public override Guid ComponentGuid => new Guid("0643109E-2C2F-4204-ABC5-ED1A4FD5C3E7");
    }
}
