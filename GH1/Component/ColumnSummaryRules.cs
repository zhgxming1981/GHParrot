using System;
using System.Collections.Generic;
using System.Linq;

namespace NS_Parrot
{
    internal enum SummaryColumnType
    {
        Copy,
        Class,
        Sum
    }

    internal sealed class SummaryColumnRuleItem
    {
        public SummaryColumnRuleItem(int column, SummaryColumnType type)
        {
            Column = column;
            Type = type;
        }

        public int Column { get; }
        public SummaryColumnType Type { get; }
    }

    internal sealed class SummarySortColumn
    {
        public SummarySortColumn(int column, bool ascending)
        {
            Column = column;
            Ascending = ascending;
        }

        public int Column { get; }
        public bool Ascending { get; }
    }

    internal sealed class SummaryColumnRules
    {
        public List<int> CopyColumns { get; } = new List<int>();
        public List<int> ClassColumns { get; } = new List<int>();
        public List<int> SumColumns { get; } = new List<int>();
        public List<SummaryColumnRuleItem> Ordered { get; } = new List<SummaryColumnRuleItem>();
        public List<SummarySortColumn> SortColumns { get; } = new List<SummarySortColumn>();
    }

    internal static class SummaryColumnRuleParser
    {
        public static SummaryColumnRules Parse(string rule)
        {
            SummaryColumnRules result = new SummaryColumnRules();
            if (string.IsNullOrWhiteSpace(rule))
                return result;

            foreach (string raw in SplitRuleParts(rule.Replace((char)0xFF0C, ',')))
            {
                string part = raw.Trim();
                SummaryColumnType type = SummaryColumnType.Copy;
                if (part.StartsWith("(") && part.EndsWith(")"))
                {
                    type = SummaryColumnType.Class;
                    part = part.Substring(1, part.Length - 2).Trim();
                }
                else if (part.StartsWith("{") && part.EndsWith("}"))
                {
                    type = SummaryColumnType.Sum;
                    part = part.Substring(1, part.Length - 2).Trim();
                }

                AddColumnTokens(part, type, result);
            }

            Normalize(result);
            return result;
        }

        public static int ColumnNameToIndex(string columnName)
        {
            string text = (columnName ?? string.Empty).Trim().ToUpperInvariant();
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

        public static string IndexToColumnName(int index)
        {
            string result = string.Empty;
            while (index > 0)
            {
                int remainder = (index - 1) % 26;
                result = (char)('A' + remainder) + result;
                index = (index - 1) / 26;
            }

            return result;
        }

        private static void AddColumnTokens(string text, SummaryColumnType type, SummaryColumnRules rules)
        {
            foreach (string raw in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw.Trim();
                if (string.IsNullOrEmpty(token))
                    continue;

                bool? ascending = null;
                if (token.Length > 1 && (token[0] == '^' || token[0] == '!'))
                {
                    ascending = token[0] == '^';
                    token = token.Substring(1).Trim();
                }

                if (token.Contains("~"))
                {
                    string[] range = token.Split('~');
                    if (range.Length != 2)
                        continue;

                    int fromColumn = ColumnNameToIndex(range[0]);
                    int toColumn = ColumnNameToIndex(range[1]);
                    if (fromColumn <= 0 || toColumn <= 0)
                        continue;

                    int start = Math.Min(fromColumn, toColumn);
                    int end = Math.Max(fromColumn, toColumn);
                    for (int column = start; column <= end; column++)
                        AddColumn(column, type, rules);

                    if (ascending.HasValue)
                        rules.SortColumns.Add(new SummarySortColumn(fromColumn, ascending.Value));
                }
                else
                {
                    int column = ColumnNameToIndex(token);
                    if (column <= 0)
                        continue;

                    AddColumn(column, type, rules);
                    if (ascending.HasValue)
                        rules.SortColumns.Add(new SummarySortColumn(column, ascending.Value));
                }
            }
        }

        private static void AddColumn(int column, SummaryColumnType type, SummaryColumnRules rules)
        {
            if (type == SummaryColumnType.Sum)
                rules.SumColumns.Add(column);
            else if (type == SummaryColumnType.Class)
                rules.ClassColumns.Add(column);
            else
                rules.CopyColumns.Add(column);

            rules.Ordered.Add(new SummaryColumnRuleItem(column, type));
        }

        private static List<string> SplitRuleParts(string rule)
        {
            List<string> parts = new List<string>();
            int depth = 0;
            int start = 0;
            for (int index = 0; index < rule.Length; index++)
            {
                char character = rule[index];
                if (character == '(' || character == '{') depth++;
                else if ((character == ')' || character == '}') && depth > 0) depth--;
                else if (character == ',' && depth == 0)
                {
                    parts.Add(rule.Substring(start, index - start));
                    start = index + 1;
                }
            }

            parts.Add(rule.Substring(start));
            return parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
        }

        private static void Normalize(SummaryColumnRules rules)
        {
            DistinctInPlace(rules.CopyColumns);
            DistinctInPlace(rules.ClassColumns);
            DistinctInPlace(rules.SumColumns);
            rules.CopyColumns.RemoveAll(column => rules.SumColumns.Contains(column) || rules.ClassColumns.Contains(column));
            rules.ClassColumns.RemoveAll(column => rules.SumColumns.Contains(column));

            List<SummarySortColumn> uniqueSortColumns = rules.SortColumns
                .GroupBy(column => column.Column)
                .Select(group => group.First())
                .ToList();
            rules.SortColumns.Clear();
            rules.SortColumns.AddRange(uniqueSortColumns);
        }

        private static void DistinctInPlace(List<int> columns)
        {
            List<int> distinct = columns.Distinct().ToList();
            columns.Clear();
            columns.AddRange(distinct);
        }
    }
}
