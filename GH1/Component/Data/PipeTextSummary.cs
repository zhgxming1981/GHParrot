using CommonFunction;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NS_Parrot
{
    public class PipeTextSummary : GH_Component
    {
        public PipeTextSummary()
          : base("文本分类汇总", "文本汇总",
              "对 A|B|C 格式文本按指定列分类汇总，分类列规则同 Excel汇总",
              "Parrot", "Data")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("分类数据", "数据", "待汇总文本列表，每行格式为 A|B|C", GH_ParamAccess.list);
            pManager.AddTextParameter("分类列", "分类列", "分类与汇总列定义，例如 A~D,(E~G),H~J,{K~M},L；()=分类列，{}=汇总列，其它=复制列", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("汇总", "汇总", "汇总后的文本列表，格式仍为 A|B|C", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<string> data = new List<string>();
            string columnRule = "";

            if (!DA.GetDataList(0, data))
                return;
            if (!DA.GetData(1, ref columnRule) || string.IsNullOrWhiteSpace(columnRule))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "请输入分类列。");
                return;
            }

            try
            {
                ColumnRules parsed = ParseColumnRules(columnRule);
                if (parsed.ClassColumns.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "分类列中缺少分类列，请用 () 标记。");
                    return;
                }
                if (parsed.Ordered.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "分类列规则为空。");
                    return;
                }

                List<SummaryGroup> groups = BuildSummary(data, parsed);
                DA.SetDataList(0, groups.Select(group => BuildOutputLine(group, parsed)));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "汇总失败: " + ex.Message);
            }
        }

        private static List<SummaryGroup> BuildSummary(List<string> lines, ColumnRules rules)
        {
            List<SummaryGroup> groups = new List<SummaryGroup>();
            Dictionary<string, SummaryGroup> groupMap = new Dictionary<string, SummaryGroup>();

            foreach (string line in lines)
            {
                if (line == null)
                    continue;

                string[] cells = line.Split('|');
                string groupKey = BuildGroupKey(cells, rules.ClassColumns);

                if (!groupMap.TryGetValue(groupKey, out SummaryGroup group))
                {
                    group = new SummaryGroup(cells);
                    foreach (int sumColumn in rules.SumColumns)
                        group.SumValues[sumColumn] = ToDouble(GetCell(cells, sumColumn));

                    groupMap.Add(groupKey, group);
                    groups.Add(group);
                }
                else
                {
                    foreach (int sumColumn in rules.SumColumns)
                    {
                        if (!group.SumValues.ContainsKey(sumColumn))
                            group.SumValues[sumColumn] = 0;

                        group.SumValues[sumColumn] += ToDouble(GetCell(cells, sumColumn));
                    }
                }
            }

            return groups;
        }

        private static string BuildOutputLine(SummaryGroup group, ColumnRules rules)
        {
            List<string> cells = new List<string>();

            foreach (ColumnRuleItem item in rules.Ordered)
            {
                if (item.Type == ColumnRuleType.Sum)
                    cells.Add(FormatNumber(group.SumValues.TryGetValue(item.Column, out double value) ? value : 0));
                else
                    cells.Add(GetCell(group.FirstRowCells, item.Column));
            }

            return string.Join("|", cells);
        }

        private static string BuildGroupKey(string[] cells, List<int> classColumns)
        {
            return string.Join("\u001F", classColumns.Select(column => GetCell(cells, column)));
        }

        private static string GetCell(string[] cells, int oneBasedColumn)
        {
            int index = oneBasedColumn - 1;
            if (cells == null || index < 0 || index >= cells.Length)
                return string.Empty;

            return cells[index];
        }

        private static double ToDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double invariantValue))
                return invariantValue;

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out double currentValue))
                return currentValue;

            return 0;
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("G15", CultureInfo.InvariantCulture);
        }

        private static ColumnRules ParseColumnRules(string rule)
        {
            ColumnRules result = new ColumnRules();

            if (string.IsNullOrWhiteSpace(rule))
                return result;

            string normalized = rule.Replace((char)0xFF0C, ',');
            string[] parts = normalized.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string raw in parts)
            {
                string part = raw.Trim();
                if (string.IsNullOrEmpty(part))
                    continue;

                ColumnRuleType type = ColumnRuleType.Copy;

                if (part.StartsWith("(") && part.EndsWith(")"))
                {
                    type = ColumnRuleType.Class;
                    part = part.Substring(1, part.Length - 2).Trim();
                }
                else if (part.StartsWith("{") && part.EndsWith("}"))
                {
                    type = ColumnRuleType.Sum;
                    part = part.Substring(1, part.Length - 2).Trim();
                }

                if (part.Contains("~"))
                {
                    string[] rangeParts = part.Split('~');
                    if (rangeParts.Length != 2)
                        throw new ArgumentException("列范围格式错误: " + raw);

                    int fromColumn = ColumnNameToIndex(rangeParts[0].Trim());
                    int toColumn = ColumnNameToIndex(rangeParts[1].Trim());
                    int start = Math.Min(fromColumn, toColumn);
                    int end = Math.Max(fromColumn, toColumn);

                    for (int column = start; column <= end; column++)
                        AddColumn(column, type, result);
                }
                else
                {
                    AddColumn(ColumnNameToIndex(part), type, result);
                }
            }

            DeduplicateRuleColumns(result);
            return result;
        }

        private static void AddColumn(int column, ColumnRuleType type, ColumnRules rules)
        {
            if (column <= 0)
                throw new ArgumentException("列名无效。");

            if (type == ColumnRuleType.Sum)
                rules.SumColumns.Add(column);
            else if (type == ColumnRuleType.Class)
                rules.ClassColumns.Add(column);
            else
                rules.CopyColumns.Add(column);

            rules.Ordered.Add(new ColumnRuleItem(column, type));
        }

        private static void DeduplicateRuleColumns(ColumnRules rules)
        {
            rules.SumColumns = rules.SumColumns.Distinct().ToList();
            rules.ClassColumns = rules.ClassColumns.Distinct().ToList();
            rules.CopyColumns = rules.CopyColumns.Distinct().ToList();

            foreach (int column in rules.SumColumns.ToList())
            {
                rules.ClassColumns.Remove(column);
                rules.CopyColumns.Remove(column);
            }

            foreach (int column in rules.ClassColumns.ToList())
                rules.CopyColumns.Remove(column);
        }

        private static int ColumnNameToIndex(string columnName)
        {
            string text = columnName.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(text))
                return 0;

            int result = 0;
            foreach (char c in text)
            {
                if (c < 'A' || c > 'Z')
                    return 0;

                result = result * 26 + (c - 'A' + 1);
            }

            return result;
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Statistics_text");

        public override Guid ComponentGuid => new Guid("6E5B3169-9B09-4AA3-BD31-AB6367DBE74E");

        private enum ColumnRuleType
        {
            Copy,
            Class,
            Sum
        }

        private sealed class ColumnRuleItem
        {
            public ColumnRuleItem(int column, ColumnRuleType type)
            {
                Column = column;
                Type = type;
            }

            public int Column { get; }
            public ColumnRuleType Type { get; }
        }

        private sealed class ColumnRules
        {
            public List<int> CopyColumns { get; set; } = new List<int>();
            public List<int> ClassColumns { get; set; } = new List<int>();
            public List<int> SumColumns { get; set; } = new List<int>();
            public List<ColumnRuleItem> Ordered { get; } = new List<ColumnRuleItem>();
        }

        private sealed class SummaryGroup
        {
            public SummaryGroup(string[] firstRowCells)
            {
                FirstRowCells = firstRowCells;
            }

            public string[] FirstRowCells { get; }
            public Dictionary<int, double> SumValues { get; } = new Dictionary<int, double>();
        }
    }
}
