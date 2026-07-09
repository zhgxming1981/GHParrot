using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class GH_ExcelSummary : GH_Component, IGH_VariableParameterComponent
    {
        private const string SettingsChunk = "ExcelSummarySettings";
        private const string NumberColumnNickName = "编号列";
        private const string PrefixNickName = "前缀";
        private const string WidthNickName = "长度";
        private const string StartNickName = "起始";
        private const string ConnectNickName = "连字";
        private const string RunNickName = "Run";
        private const string PathNickName = "路径";
        private const string SheetNickName = "Sheet";
        private const string RuleNickName = "分类列";
        private const string StatusNickName = "状态";

        private bool _triggerRun = false;
        private bool _numberingEnabled = true;

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
            p.AddTextParameter("新编号列", NumberColumnNickName, "新编号写回此列", GH_ParamAccess.item);
            p.AddTextParameter("编号前缀", PrefixNickName, "带双引号如\"A\"=固定前缀A，不带引号如A=从A列读取前缀", GH_ParamAccess.item);
            p.AddIntegerParameter("编号长度", WidthNickName, "编号数字补齐长度，0=不补齐", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("起始编号", StartNickName, "起始序号，默认1", GH_ParamAccess.item, 1);
            p.AddTextParameter("连字符", ConnectNickName, "前缀与数字间的连接符，列前缀模式时生效", GH_ParamAccess.item);
            p.AddBooleanParameter("Run", RunNickName, "上升沿触发执行", GH_ParamAccess.item, false);

            for (int i = 3; i <= 7; i++)
                p[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("状态", StatusNickName, "执行结果信息", GH_ParamAccess.item);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            Menu_AppendSeparator(menu);
            ToolStripMenuItem numberingMenu = new ToolStripMenuItem("编号模式");
            menu.Items.Add(numberingMenu);
            Menu_AppendItem(numberingMenu.DropDown, "加编号", (s, e) => SetNumberingMode(true), true, _numberingEnabled);
            Menu_AppendItem(numberingMenu.DropDown, "不加编号", (s, e) => SetNumberingMode(false), true, !_numberingEnabled);

            Menu_AppendSeparator(menu);
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
                bool gotNumberColumn = GetInputData(DA, NumberColumnNickName, ref numberColumn);
                bool gotPrefix = GetInputData(DA, PrefixNickName, ref prefixInput);
                bool gotWidth = GetInputData(DA, WidthNickName, ref numberWidth);
                bool gotStart = GetInputData(DA, StartNickName, ref startNo);
                bool gotConnect = GetInputData(DA, ConnectNickName, ref connectString);
                GetInputData(DA, RunNickName, ref run);

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
                bool hasNumbering = _numberingEnabled;

                if (hasNumbering)
                {
                    if (!gotNumberColumn || string.IsNullOrWhiteSpace(numberColumn))
                    {
                        DA.SetData(0, "错误：加编号模式下，编号列不能为空");
                        return;
                    }
                    if (!gotPrefix || string.IsNullOrWhiteSpace(prefixInput))
                    {
                        DA.SetData(0, "错误：加编号模式下，编号前缀不能为空");
                        return;
                    }
                    if (!gotWidth)
                    {
                        DA.SetData(0, "错误：加编号模式下，编号长度不能为空");
                        return;
                    }
                    if (!gotStart)
                    {
                        DA.SetData(0, "错误：加编号模式下，起始编号不能为空");
                        return;
                    }
                    if (prefixInput.StartsWith("\"") && prefixInput.EndsWith("\""))
                    {
                        useFixedPrefix = true;
                        fixedPrefix = prefixInput.Substring(1, prefixInput.Length - 2);
                        connectString = "";
                    }
                    else
                    {
                        if (!gotConnect || string.IsNullOrWhiteSpace(connectString))
                        {
                            DA.SetData(0, "错误：列前缀编号时，连字符不能为空");
                            return;
                        }

                        useFixedPrefix = false;
                        prefixColumnIndex = ColumnNameToIndex(prefixInput.Trim());
                        if (prefixColumnIndex <= 0)
                        {
                            DA.SetData(0, "错误：编号前缀列无效");
                            return;
                        }
                    }
                }

                Excel.Application app = null;
                Excel.Workbook wb = null;

                try
                {
                    try
                    {
                        app = new Excel.Application();
                    }
                    catch (Exception ex)
                    {
                        DA.SetData(0, "错误：启动Excel失败，原因：" + ex.Message);
                        return;
                    }

                    TryPrepareExcelState(app);

                    try
                    {
                        wb = app.Workbooks.Open(filePath);
                    }
                    catch (Exception ex)
                    {
                        DA.SetData(0, "错误：打开Excel文件失败，原因：" + ex.Message);
                        return;
                    }

                    Excel.Worksheet ws;
                    try
                    {
                        ws = GetSheet(wb, sheetName);
                    }
                    catch (Exception ex)
                    {
                        DA.SetData(0, "错误：获取Sheet失败，原因：" + ex.Message);
                        return;
                    }
                    if (ws == null)
                    {
                        DA.SetData(0, "错误：未找到Sheet \"" + sheetName + "\"");
                        return;
                    }

                    int lastRow;
                    try
                    {
                        ws.Activate();
                        lastRow = ws.UsedRange.Rows.Count;
                    }
                    catch (Exception ex)
                    {
                        DA.SetData(0, "错误：读取数据区域失败，原因：" + ex.Message);
                        return;
                    }
                    if (lastRow <= 1)
                    {
                        DA.SetData(0, "错误：Sheet中无数据");
                        return;
                    }

                    string resultMsg;
                    try
                    {
                        resultMsg = BuildSummary(wb, ws, copyColumns, classColumns, sumColumns, parsed.ordered,
                            hasNumbering, numberColumn, useFixedPrefix, fixedPrefix,
                            prefixColumnIndex, numberWidth, startNo, connectString);
                    }
                    catch (Exception ex)
                    {
                        DA.SetData(0, "错误：生成汇总失败，原因：" + ex.Message);
                        return;
                    }

                    try
                    {
                        wb.Save();
                    }
                    catch (Exception ex)
                    {
                        DA.SetData(0, "错误：保存Excel失败，原因：" + ex.Message);
                        return;
                    }
                    DA.SetData(0, resultMsg);
                }
                finally
                {
                    if (app != null)
                        TryRestoreExcelState(app);

                    ReleaseComObject(wb);

                    if (app != null)
                    {
                        TryQuitExcel(app);
                        ReleaseComObject(app);
                    }
                }
            }
            catch (Exception ex)
            {
                DA.SetData(0, "错误：" + ex.Message);
            }
        }

        private void SetNumberingMode(bool enabled)
        {
            if (_numberingEnabled == enabled)
                return;

            _numberingEnabled = enabled;
            NormalizeParams(true);
            ExpireSolution(true);
        }

        public bool CanInsertParameter(GH_ParameterSide side, int index)
        {
            return false;
        }

        public bool CanRemoveParameter(GH_ParameterSide side, int index)
        {
            return false;
        }

        public IGH_Param CreateParameter(GH_ParameterSide side, int index)
        {
            return null;
        }

        public bool DestroyParameter(GH_ParameterSide side, int index)
        {
            return false;
        }

        public void VariableParameterMaintenance()
        {
            NormalizeParams(false);
        }

        private void NormalizeParams(bool notify)
        {
            bool changed = NormalizeInputParams();
            changed |= NormalizeOutputParams();

            if (notify && changed)
            {
                Params.OnParametersChanged();
                if (Attributes != null)
                    Attributes.ExpireLayout();
            }
        }

        private bool NormalizeInputParams()
        {
            var desired = GetDesiredInputNickNames();
            bool changed = false;
            var desiredSet = new HashSet<string>(desired, StringComparer.OrdinalIgnoreCase);
            var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < Params.Input.Count; i++)
            {
                string nickName = Params.Input[i].NickName;
                if (!firstIndexByName.ContainsKey(nickName))
                    firstIndexByName[nickName] = i;
            }

            for (int i = Params.Input.Count - 1; i >= 0; i--)
            {
                string nickName = Params.Input[i].NickName;
                if (!desiredSet.Contains(nickName) || firstIndexByName[nickName] != i)
                {
                    Params.UnregisterInputParameter(Params.Input[i], false);
                    changed = true;
                }
            }

            for (int i = 0; i < desired.Count; i++)
            {
                string nickName = desired[i];
                int currentIndex = FindInputIndex(nickName);
                if (currentIndex < 0)
                {
                    Params.RegisterInputParam(CreateStandardInputParam(nickName), Math.Min(i, Params.Input.Count));
                    changed = true;
                    continue;
                }

                MaintainStandardInputParam(Params.Input[currentIndex], nickName);
            }

            return changed;
        }

        private bool NormalizeOutputParams()
        {
            bool changed = false;

            for (int i = Params.Output.Count - 1; i >= 0; i--)
            {
                if (i > 0)
                {
                    Params.UnregisterOutputParameter(Params.Output[i], false);
                    changed = true;
                }
            }

            if (Params.Output.Count == 0)
            {
                Params.RegisterOutputParam(CreateStatusParam());
                changed = true;
            }

            if (Params.Output.Count > 0)
                MaintainStandardOutputParam(Params.Output[0]);

            return changed;
        }

        private List<string> GetDesiredInputNickNames()
        {
            var desired = new List<string> { PathNickName, SheetNickName, RuleNickName };
            if (_numberingEnabled)
                desired.AddRange(new[] { NumberColumnNickName, PrefixNickName, WidthNickName, StartNickName, ConnectNickName });
            desired.Add(RunNickName);
            return desired;
        }

        private IGH_Param CreateStandardInputParam(string nickName)
        {
            switch (nickName)
            {
                case PathNickName: return CreatePathParam();
                case SheetNickName: return CreateSheetParam();
                case RuleNickName: return CreateRuleParam();
                case NumberColumnNickName: return CreateNumberColumnParam();
                case PrefixNickName: return CreatePrefixParam();
                case WidthNickName: return CreateWidthParam();
                case StartNickName: return CreateStartParam();
                case ConnectNickName: return CreateConnectParam();
                case RunNickName: return CreateRunParam();
                default: return new Param_String { NickName = nickName, Name = nickName, Access = GH_ParamAccess.item, Optional = true };
            }
        }

        private void MaintainStandardInputParam(IGH_Param param, string nickName)
        {
            IGH_Param standard = CreateStandardInputParam(nickName);
            param.Name = standard.Name;
            param.NickName = standard.NickName;
            param.Description = standard.Description;
            param.Access = standard.Access;
            param.Optional = standard.Optional;
        }

        private void MaintainStandardOutputParam(IGH_Param param)
        {
            IGH_Param standard = CreateStatusParam();
            param.Name = standard.Name;
            param.NickName = standard.NickName;
            param.Description = standard.Description;
            param.Access = standard.Access;
            param.Optional = standard.Optional;
        }

        private int FindInputIndex(string nickName)
        {
            for (int i = 0; i < Params.Input.Count; i++)
            {
                if (string.Equals(Params.Input[i].NickName, nickName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private bool GetInputData<T>(IGH_DataAccess da, string nickName, ref T value)
        {
            int index = FindInputIndex(nickName);
            return index >= 0 && da.GetData(index, ref value);
        }

        private static Param_String CreatePathParam()
        {
            return new Param_String
            {
                Name = "文件路径",
                NickName = PathNickName,
                Description = "Excel文件完整路径",
                Access = GH_ParamAccess.item
            };
        }

        private static Param_String CreateSheetParam()
        {
            return new Param_String
            {
                Name = "Sheet名",
                NickName = SheetNickName,
                Description = "目标工作表名称，为空则使用活动Sheet",
                Access = GH_ParamAccess.item
            };
        }

        private static Param_String CreateRuleParam()
        {
            return new Param_String
            {
                Name = "分类列",
                NickName = RuleNickName,
                Description = "分类与汇总列定义，格式同 GH_ExcelMultiSummary 的 CD 端口：A~D,(E~G),H~J,{K~M},L；()=分类列，{}=汇总列，其它=复制列",
                Access = GH_ParamAccess.item
            };
        }

        private static Param_Boolean CreateRunParam()
        {
            return new Param_Boolean
            {
                Name = "Run",
                NickName = RunNickName,
                Description = "上升沿触发执行",
                Access = GH_ParamAccess.item,
                Optional = true
            };
        }

        private static Param_String CreateStatusParam()
        {
            return new Param_String
            {
                Name = "状态",
                NickName = StatusNickName,
                Description = "执行结果信息",
                Access = GH_ParamAccess.item
            };
        }

        private static Param_String CreateNumberColumnParam()
        {
            return new Param_String
            {
                Name = "新编号列",
                NickName = NumberColumnNickName,
                Description = "新编号写回此列",
                Access = GH_ParamAccess.item,
                Optional = true
            };
        }

        private static Param_String CreatePrefixParam()
        {
            return new Param_String
            {
                Name = "编号前缀",
                NickName = PrefixNickName,
                Description = "带双引号如\"A\"=固定前缀A，不带引号如A=从A列读取前缀",
                Access = GH_ParamAccess.item,
                Optional = true
            };
        }

        private static Param_Integer CreateWidthParam()
        {
            return new Param_Integer
            {
                Name = "编号长度",
                NickName = WidthNickName,
                Description = "编号数字补齐长度，0=不补齐",
                Access = GH_ParamAccess.item,
                Optional = true
            };
        }

        private static Param_Integer CreateStartParam()
        {
            return new Param_Integer
            {
                Name = "起始编号",
                NickName = StartNickName,
                Description = "起始序号",
                Access = GH_ParamAccess.item,
                Optional = true
            };
        }

        private static Param_String CreateConnectParam()
        {
            return new Param_String
            {
                Name = "连字符",
                NickName = ConnectNickName,
                Description = "前缀与数字间的连接符，列前缀模式时生效",
                Access = GH_ParamAccess.item,
                Optional = true
            };
        }

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter chunk = writer.CreateChunk(SettingsChunk);
            chunk.SetBoolean("NumberingEnabled", _numberingEnabled);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            bool result = base.Read(reader);
            GH_IReader chunk = reader.FindChunk(SettingsChunk);
            if (chunk != null)
            {
                bool numberingEnabled = _numberingEnabled;
                if (chunk.TryGetBoolean("NumberingEnabled", ref numberingEnabled))
                    _numberingEnabled = numberingEnabled;
            }

            NormalizeParams(true);
            return result;
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
            var parts = SplitRuleParts(rule);

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

                AddColumnToken(part, type, copyCols, classCols, sumCols, ordered);
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

        private List<string> SplitRuleParts(string rule)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < rule.Length; i++)
            {
                char c = rule[i];
                if (c == '(' || c == '{')
                    depth++;
                else if ((c == ')' || c == '}') && depth > 0)
                    depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(rule.Substring(start, i - start));
                    start = i + 1;
                }
            }

            parts.Add(rule.Substring(start));
            return parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
        }

        private void AddColumnToken(string token, int type, List<int> copyCols, List<int> classCols, List<int> sumCols, List<(int col, int type)> ordered)
        {
            foreach (string raw in token.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim();
                if (string.IsNullOrEmpty(part))
                    continue;

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

        private string BuildSummary(Excel.Workbook wb, Excel.Worksheet sourceSheet,
            List<int> copyColumns, List<int> classColumns, List<int> sumColumns, List<(int col, int type)> ordered,
            bool hasNumbering, string numberColumnName,
            bool useFixedPrefix, string fixedPrefix,
            int prefixColumnIndex, int numberWidth, int startNo, string connectString)
        {
            Excel.Range usedRange;
            int usedRows;
            int usedCols;
            int firstRow;
            int firstCol;
            int lastRow;
            int lastCol;
            int headerRow;
            int firstDataRow;
            object usedData;

            try
            {
                usedRange = sourceSheet.UsedRange;
                usedRows = usedRange.Rows.Count;
                usedCols = usedRange.Columns.Count;
                firstRow = usedRange.Row;
                firstCol = usedRange.Column;
                lastRow = firstRow + usedRows - 1;
                lastCol = firstCol + usedCols - 1;
                headerRow = firstRow;
                firstDataRow = firstRow + 1;
                usedData = usedRange.Value2;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("读取数据区域失败，原因：" + ex.Message, ex);
            }

            ValidateReadableColumns(ordered.Select(item => item.col), firstCol, lastCol);
            ValidateReadableColumns(classColumns, firstCol, lastCol);
            ValidateReadableColumns(sumColumns, firstCol, lastCol);
            if (hasNumbering && !useFixedPrefix)
                ValidateReadableColumns(new[] { prefixColumnIndex }, firstCol, lastCol);

            var summaryRows = new List<SummaryRow>();
            var groups = new Dictionary<string, SummaryRow>();
            var prefixCounters = new Dictionary<string, int>();
            int fixedPrefixCounter = startNo;

            for (int row = firstDataRow; row <= lastRow; row++)
            {
                var currentValues = classColumns
                    .Select(col => GetSheetValue(usedData, row, col, firstRow, firstCol))
                    .ToArray();
                string groupKey = BuildGroupKey(currentValues);

                if (!groups.TryGetValue(groupKey, out SummaryRow currentGroup))
                {
                    currentGroup = new SummaryRow
                    {
                        Values = new Dictionary<int, object>(),
                        SumValues = new Dictionary<int, double>(),
                        SourceRows = new List<int>()
                    };

                    foreach (var item in ordered)
                    {
                        object value = GetSheetValue(usedData, row, item.col, firstRow, firstCol);
                        if (item.type == 2)
                            currentGroup.SumValues[item.col] = ToDouble(value);
                        else
                            currentGroup.Values[item.col] = value;
                    }

                    if (hasNumbering)
                    {
                        if (!useFixedPrefix)
                        {
                            string currentPrefixText = GetCellText(GetSheetValue(usedData, row, prefixColumnIndex, firstRow, firstCol));
                            int numberCounter = prefixCounters.ContainsKey(currentPrefixText) ? prefixCounters[currentPrefixText] : startNo;
                            currentGroup.NumberText = BuildNumberText(currentPrefixText, numberCounter, numberWidth, connectString);
                            prefixCounters[currentPrefixText] = numberCounter + 1;
                        }
                        else
                        {
                            currentGroup.NumberText = BuildNumberText(fixedPrefix, fixedPrefixCounter, numberWidth, connectString);
                            fixedPrefixCounter++;
                        }
                    }

                    groups[groupKey] = currentGroup;
                    summaryRows.Add(currentGroup);
                }
                else
                {
                    foreach (int col in sumColumns)
                    {
                        object value = GetSheetValue(usedData, row, col, firstRow, firstCol);
                        if (!currentGroup.SumValues.ContainsKey(col))
                            currentGroup.SumValues[col] = 0;
                        currentGroup.SumValues[col] += ToDouble(value);
                    }
                }

                currentGroup.SourceRows.Add(row);
            }

            if (hasNumbering)
            {
                try
                {
                    WriteNumberingToSource(sourceSheet, summaryRows, numberColumnName);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("写回编号失败，原因：" + ex.Message, ex);
                }
            }

            Excel.Worksheet newSheet;
            try
            {
                string newSheetName = GetAvailableSummarySheetName(wb, sourceSheet.Name);
                newSheet = (Excel.Worksheet)wb.Sheets.Add();
                newSheet.Name = newSheetName;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("创建汇总Sheet失败，原因：" + ex.Message, ex);
            }

            int outRows = summaryRows.Count + 1;
            int outCols = ordered.Count + (hasNumbering ? 1 : 0);
            var outputData = new object[outRows, outCols];

            for (int i = 0; i < ordered.Count; i++)
                outputData[0, i] = GetSheetValue(usedData, headerRow, ordered[i].col, firstRow, firstCol);
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

            try
            {
                var targetRange = newSheet.Range[
                    newSheet.Cells[1, 1],
                    newSheet.Cells[outRows, outCols]];
                targetRange.Value2 = outputData;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("写入汇总结果失败，原因：" + ex.Message, ex);
            }

            return "完成：共" + summaryRows.Count + "组，" + (usedRows - 1) + "行";
        }

        private string GetAvailableSummarySheetName(Excel.Workbook wb, string sourceSheetName)
        {
            string suffix = "(汇总)";
            string baseName = TrimSheetName(sourceSheetName + suffix);
            string newSheetName = baseName;
            int sheetNo = 1;

            while (SheetExists(wb, newSheetName))
            {
                sheetNo++;
                string numberSuffix = "(" + sheetNo + ")";
                newSheetName = TrimSheetName(sourceSheetName + numberSuffix);
            }

            return newSheetName;
        }

        private string TrimSheetName(string name)
        {
            string trimmed = name.Trim();
            if (trimmed.Length > 31)
                trimmed = trimmed.Substring(0, 31);

            return trimmed;
        }

        private bool SheetExists(Excel.Workbook wb, string sheetName)
        {
            foreach (Excel.Worksheet sheet in wb.Sheets)
            {
                if (string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void ValidateReadableColumns(IEnumerable<int> columns, int firstCol, int lastCol)
        {
            foreach (int col in columns.Distinct())
            {
                if (col < firstCol || col > lastCol)
                    throw new InvalidOperationException("列 " + IndexToColumnName(col) + " 不在当前数据区域内");
            }
        }

        private object GetSheetValue(object usedData, int sheetRow, int sheetCol, int firstRow, int firstCol)
        {
            if (usedData is object[,] values)
                return values[sheetRow - firstRow + 1, sheetCol - firstCol + 1];

            return usedData;
        }

        private string BuildGroupKey(object[] values)
        {
            return string.Join("\u001F", values.Select(value => GetCellText(value)));
        }

        private void WriteNumberingToSource(Excel.Worksheet sourceSheet, List<SummaryRow> summaryRows, string numberColumnName)
        {
            int numberColumn = ColumnNameToIndex(numberColumnName);
            if (numberColumn <= 0)
                throw new InvalidOperationException("编号列无效");

            foreach (SummaryRow row in summaryRows)
            {
                foreach (int sourceRow in row.SourceRows)
                    sourceSheet.Cells[sourceRow, numberColumn].Value2 = row.NumberText;
            }
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

        private void TryPrepareExcelState(Excel.Application app)
        {
            try { app.Visible = false; } catch { }
            try { app.ScreenUpdating = false; } catch { }
            try { app.DisplayAlerts = false; } catch { }
            try { app.EnableEvents = false; } catch { }
            try { app.Calculation = Excel.XlCalculation.xlCalculationManual; } catch { }
        }

        private void TryRestoreExcelState(Excel.Application app)
        {
            try { app.ScreenUpdating = true; } catch { }
            try { app.DisplayAlerts = true; } catch { }
            try { app.EnableEvents = true; } catch { }
            try { app.Calculation = Excel.XlCalculation.xlCalculationAutomatic; } catch { }
        }

        private void TryQuitExcel(Excel.Application app)
        {
            try { app.Quit(); } catch { }
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
            public List<int> SourceRows { get; set; }
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
