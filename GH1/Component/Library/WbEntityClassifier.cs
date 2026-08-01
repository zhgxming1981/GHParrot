using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    // ========================================================================
    // 实体分类电池（WB）：基于 WB 分类库对 Brep 实体进行分类，
    // 命中已有特征时复用旧编号，新件生成新编号。
    // 分类算法、容差、镜像保护等与"分类数据库迁移_WB"完全一致。
    // ========================================================================
    public sealed class WbEntityClassifier : GH_Component
    {
        private const string SettingsChunk = "WbEntityClassifierSettings";

        // 分类准则设置（与 WbClassificationMigration 共享 WbClassificationSettings）
        private readonly WbClassificationSettings _settings = new WbClassificationSettings();

        // 参数快照：分类开始时冻结，同一次分类、入库使用同一份参数
        private WbClassificationSettings _frozenSettings;
        // 当前设置是否来自数据库 classification_profile
        private bool _settingsFromDb;
        // 是否曾成功从 DB 加载过 profile（区分"从未加载"与"加载后用户修改"）
        private bool _dbProfileWasLoaded;
        // 上一次数据库路径，路径变化时重新加载 profile
        private string _lastDatabasePath = string.Empty;

        // 运行状态
        private bool _lastRunInput;
        private bool _savePulse;

        // 分类结果缓存（供右键保存和非上升沿输出使用）
        private string _pendingDatabasePath = string.Empty;
        private readonly List<WbMigrationRecord> _newRecords = new List<WbMigrationRecord>();
        private readonly List<string> _lastNumbers = new List<string>();
        private readonly List<string> _lastStatuses = new List<string>();
        private readonly List<string> _lastSummaries = new List<string>();
        private string _lastSummary = string.Empty;

        public WbEntityClassifier()
            : base("实体分类（WB）", "实体分类_WB",
                  "基于 WB 分类库对 Brep 实体进行分类，命中已有特征时复用旧编号，新件生成新编号。右键\"保存/入库\"写入新件。",
                  "Parrot", "Library") { }

        public override Guid ComponentGuid => new Guid("8F2A5D73-4E6B-4C8E-A1F3-7D9B2E5C8A14");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("材质", "材质",
                "与实体一一对应的材质，可为空。用于材质严格匹配。", GH_ParamAccess.list);
            p.AddTextParameter("颜色", "颜色",
                "与实体一一对应的颜色，可为空。用于颜色严格匹配。", GH_ParamAccess.list);
            p.AddBrepParameter("实体列表", "实体",
                "待分类零件 Brep。", GH_ParamAccess.list);
            p.AddTextParameter("数据库地址", "数据库",
                "WB 分类迁移库 SQLite 文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("编号前缀", "前缀",
                "新编号的前缀；可为空。允许不同实体使用不同前缀。", GH_ParamAccess.list);
            p.AddIntegerParameter("数字长度", "位数",
                "编号数字部分的最小位数，例如 4 生成 0001。", GH_ParamAccess.item, 4);
            p.AddTextParameter("连字符", "连字符",
                "前缀与数字部分的连接符；可为空。", GH_ParamAccess.item, "-");
            p.AddIntegerParameter("起始序号", "起始",
                "新件编号从此值起；程序在当前格式下扫描已有最大编号并加一。", GH_ParamAccess.item, 1);
            p.AddBooleanParameter("运行", "运行",
                "上升沿执行分类。", GH_ParamAccess.item, false);
            p[0].Optional = true; p[1].Optional = true; p[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("编号", "编号",
                "与输入实体一一对应。命中已有特征时复用旧完整编号；新件生成完整新编号。", GH_ParamAccess.list);
            p.AddTextParameter("命中状态", "状态",
                "已复用 / 新建编号 / 镜像件独立新建 / 匹配歧义未入库 / 跳过。", GH_ParamAccess.list);
            p.AddTextParameter("特征摘要", "摘要",
                "便于核对分类依据。", GH_ParamAccess.list);
            p.AddTextParameter("汇总状态", "汇总",
                "本次复用数、新建数、歧义数、跳过数及数据库状态。", GH_ParamAccess.item);
            p.AddBooleanParameter("存储完成脉冲", "入库",
                "成功保存新件时输出 True，其余时候为 False。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            bool pulse = _savePulse;
            _savePulse = false;

            var materials = new List<string>();
            var colors = new List<string>();
            var breps = new List<Brep>();
            string database = string.Empty;
            var prefixes = new List<string>();
            int digitLength = 4;
            string connector = "-";
            int startSeq = 1;
            bool run = false;

            da.GetDataList(0, materials);
            da.GetDataList(1, colors);
            da.GetDataList(2, breps);
            da.GetData(3, ref database);
            da.GetDataList(4, prefixes);
            da.GetData(5, ref digitLength);
            da.GetData(6, ref connector);
            da.GetData(7, ref startSeq);
            da.GetData(8, ref run);

            bool rising = run && !_lastRunInput;
            _lastRunInput = run;

            if (!rising)
            {
                da.SetDataList(0, _lastNumbers);
                da.SetDataList(1, _lastStatuses);
                da.SetDataList(2, _lastSummaries);
                da.SetData(3, pulse ? _lastSummary + " 入库完成。" : _lastSummary);
                da.SetData(4, pulse);
                return;
            }

            // 上升沿执行分类
            _lastNumbers.Clear();
            _lastStatuses.Clear();
            _lastSummaries.Clear();
            _newRecords.Clear();
            _pendingDatabasePath = database ?? string.Empty;

            try
            {
                int count = breps.Count;
                if (count == 0)
                    throw new ArgumentException("实体列表为空。");
                if (string.IsNullOrWhiteSpace(database))
                    throw new ArgumentException("数据库地址不能为空。");
                if (!File.Exists(database))
                    throw new FileNotFoundException("数据库文件不存在：" + database);
                if (digitLength < 1) digitLength = 1;
                if (startSeq < 1) startSeq = 1;

                // DB 路径变化时读取 classification_profile，同步参数
                if (!string.Equals(database, _lastDatabasePath, StringComparison.OrdinalIgnoreCase))
                {
                    _lastDatabasePath = database;
                    _settingsFromDb = false;

                    string profileText = WbMigrationStore.LoadProfileSettings(database);
                    if (!string.IsNullOrWhiteSpace(profileText))
                    {
                        if (WbClassificationSettings.TryParse(profileText, out WbClassificationSettings dbSettings))
                        {
                            // 用 DB 参数覆盖当前设置
                            foreach (V5Criterion k in _settings.Enabled.Keys)
                            {
                                _settings.Enabled[k] = dbSettings.Enabled[k];
                                _settings.Tolerances[k] = dbSettings.Tolerances[k];
                                _settings.IsRelativeTolerance[k] = dbSettings.IsRelativeTolerance[k];
                            }
                            _settings.FallbackTolerance = dbSettings.FallbackTolerance;
                            _settings.InertiaDegenerateRatio = dbSettings.InertiaDegenerateRatio;
                            _settings.HoleNormalTolerance = dbSettings.HoleNormalTolerance;
                            _settings.StrictMaterialColor = dbSettings.StrictMaterialColor;
                            _settings.OuterProfileAreaRatio = dbSettings.OuterProfileAreaRatio;
                            _settings.OuterProfilePerimeterMin = dbSettings.OuterProfilePerimeterMin;
                            _settings.DegeneratePositionMultiplier = dbSettings.DegeneratePositionMultiplier;
                            _settings.EnableDegenerateMatching = dbSettings.EnableDegenerateMatching;
                            _settingsFromDb = true;
                            _dbProfileWasLoaded = true;
                            Attributes?.ExpireLayout();
                            OnDisplayExpired(true);
                        }
                    }
                    if (!_settingsFromDb)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "数据库无 classification_profile 或解析失败，使用本地参数。");
                }

                // 冻结参数快照：同一次分类、入库使用同一份参数
                _frozenSettings = _settings.Clone();

                // 加载数据库已有件
                var existingParts = WbMigrationStore.LoadAllParts(database);
                bool dbOk = existingParts != null;

                int reused = 0, newCount = 0, mirrorCount = 0, ambiguous = 0, skipped = 0;
                string rhinoPath = Rhino.RhinoDoc.ActiveDoc?.Path ?? string.Empty;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        Brep brep = breps[i];
                        string mat = TextAt(materials, i);
                        string col = TextAt(colors, i);

                        WbFeature feature = WbFeature.Create(
                            mat, col, brep, _frozenSettings.InertiaDegenerateRatio, _frozenSettings.FallbackTolerance, i);

                        // 与库中件比较
                        int matchCount = 0;
                        string matchedNumber = null;
                        bool isMirror = false;

                        if (dbOk)
                        {
                            foreach (var existing in existingParts)
                            {
                                if (!WbMigrationAudit.CanAutoMatch(feature, existing.Feature,
                                        _frozenSettings.EnableDegenerateMatching))
                                    continue;

                                if (CallMatches(feature, existing.Feature, false))
                                {
                                    matchCount++;
                                    matchedNumber = existing.Number;
                                }

                                if (matchCount > 1)
                                    break;                  // 已歧义，后续比较不再改变结果

                                // 仅尚未发现精确匹配时再检查镜像
                                if (matchCount == 0 && CallMatches(feature, existing.Feature, true))
                                    isMirror = true;
                            }
                        }

                        // 与本轮已生成的新记录比较（防止同批次相同件分配不同编号）
                        if (matchCount == 0)
                        {
                            foreach (var nr in _newRecords)
                            {
                                if (!WbMigrationAudit.CanAutoMatch(feature, nr.Feature,
                                        _frozenSettings.EnableDegenerateMatching))
                                    continue;

                                if (CallMatches(feature, nr.Feature, false))
                                {
                                    matchCount++;
                                    matchedNumber = nr.Number;
                                    break;              // 同批次找到相同件，无需继续
                                }

                                // 仅未匹配时再检查镜像
                                if (CallMatches(feature, nr.Feature, true))
                                    isMirror = true;
                            }
                        }

                        if (matchCount == 1)
                        {
                            _lastNumbers.Add(matchedNumber);
                            _lastStatuses.Add("已复用");
                            _lastSummaries.Add(feature.ToSummary());
                            reused++;
                        }
                        else if (matchCount > 1)
                        {
                            _lastNumbers.Add(string.Empty);
                            _lastStatuses.Add("匹配歧义未入库");
                            _lastSummaries.Add(feature.ToSummary());
                            ambiguous++;
                        }
                        else if (isMirror)
                        {
                            string prefix = TextAt(prefixes, i);
                            string newNumber = GenerateNewNumber(
                                prefix, connector, digitLength, startSeq,
                                existingParts, _lastNumbers);
                            _lastNumbers.Add(newNumber);
                            _lastStatuses.Add("镜像件独立新建");
                            _lastSummaries.Add(feature.ToSummary());
                            _newRecords.Add(new WbMigrationRecord(
                                i, newNumber, feature, rhinoPath));
                            mirrorCount++;
                        }
                        else
                        {
                            string prefix = TextAt(prefixes, i);
                            string newNumber = GenerateNewNumber(
                                prefix, connector, digitLength, startSeq,
                                existingParts, _lastNumbers);
                            _lastNumbers.Add(newNumber);
                            _lastStatuses.Add("新建编号");
                            _lastSummaries.Add(feature.ToSummary());
                            _newRecords.Add(new WbMigrationRecord(
                                i, newNumber, feature, rhinoPath));
                            newCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _lastNumbers.Add(string.Empty);
                        _lastStatuses.Add("跳过：" + ex.Message);
                        _lastSummaries.Add(string.Empty);
                        skipped++;
                    }
                }

                _lastSummary = string.Format(CultureInfo.InvariantCulture,
                    "复用 {0}，新建 {1}，镜像 {2}，歧义 {3}，跳过 {4}，共 {5} 件。" +
                    (dbOk ? "数据库正常。" : "数据库加载失败。"),
                    reused, newCount, mirrorCount, ambiguous, skipped, count);
            }
            catch (Exception ex)
            {
                _lastSummary = "错误：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }

            da.SetDataList(0, _lastNumbers);
            da.SetDataList(1, _lastStatuses);
            da.SetDataList(2, _lastSummaries);
            da.SetData(3, _lastSummary);
            da.SetData(4, pulse);
        }

        // ================================================================
        // 右键保存 / 入库
        // ================================================================
        public void SaveToDatabase()
        {
            if (_newRecords.Count == 0 || string.IsNullOrWhiteSpace(_pendingDatabasePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "没有待保存的新件。");
                return;
            }

            try
            {
                WbMigrationStore.InsertPartsBatch(_pendingDatabasePath, _newRecords, _frozenSettings?.SettingsText());

                _savePulse = true;
                _newRecords.Clear();
                ExpireSolution(true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "保存失败：" + ex.Message);
            }
        }

        // ================================================================
        // 右键菜单
        // ================================================================
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);

            var control = new WbEntityClassifierSettingsMenuControl(this);
            ToolStripControlHost host = new ToolStripControlHost(control)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = control.Size
            };
            menu.Closing += (sender, e) => control.CommitAll();
            menu.Items.Add(host);
        }

        // ================================================================
        // 设置方法
        // ================================================================
        private void SetCriterionEnabled(V5Criterion criterion, bool enabled)
        {
            if (criterion == V5Criterion.Length) return;
            if (_settings.Enabled[criterion] == enabled) return;
            _settings.Enabled[criterion] = enabled;
            MarkSettingsChanged();
        }

        private void SetCriterionTolerance(V5Criterion criterion, string text)
        {
            if (!WbClassificationMigration.TryParseTolerance(text, out double value, out bool useAbsolute))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "误差数值无法识别：" + text);
                return;
            }
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    WbClassificationMigration.CriterionDisplayName(criterion) + " 容差不能为 NaN 或无穷大：" + text);
                return;
            }
            _settings.Tolerances[criterion] = Math.Abs(value);
            if (!WbClassificationMigration.IsAbsoluteOnlyCriterion(criterion))
                _settings.IsRelativeTolerance[criterion] = !useAbsolute;
            MarkSettingsChanged();
        }

        private void SetFallbackTolerance(string text)
        {
            if (!WbClassificationMigration.TryParseTolerance(text, out double value, out _))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "回退容差无法识别：" + text);
                return;
            }
            _settings.FallbackTolerance = Math.Max(0, value);
            MarkSettingsChanged();
        }

        private void SetInertiaDegenerateRatio(string text)
        {
            if (!WbClassificationMigration.TryParseTolerance(text, out double value, out _))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "退化判定阈值无法识别：" + text);
                return;
            }
            _settings.InertiaDegenerateRatio = Math.Max(0.5, Math.Min(1.0, value));
            MarkSettingsChanged();
        }

        private void SetStrictMaterialColor(bool value)
        {
            if (_settings.StrictMaterialColor == value) return;
            _settings.StrictMaterialColor = value;
            MarkSettingsChanged();
        }

        private void SetHoleNormalTolerance(string text)
        {
            if (!WbClassificationMigration.TryParseTolerance(text, out double value, out _))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "法向兼容阈值无法识别：" + text);
                return;
            }
            _settings.HoleNormalTolerance = Math.Max(0.0, Math.Min(1.0, value));
            MarkSettingsChanged();
        }

        private void SetOuterProfileAreaRatio(string text)
        {
            if (!WbClassificationMigration.TryParseTolerance(text, out double value, out _))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外轮廓面积容差无法识别：" + text);
                return;
            }
            _settings.OuterProfileAreaRatio = Math.Max(0, value);
            MarkSettingsChanged();
        }

        private void SetOuterProfilePerimeterMin(string text)
        {
            if (!WbClassificationMigration.TryParseTolerance(text, out double value, out _))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外轮廓周长最小容差无法识别：" + text);
                return;
            }
            _settings.OuterProfilePerimeterMin = Math.Max(0, value);
            MarkSettingsChanged();
        }

        private void SetDegeneratePositionMultiplier(string text)
        {
            if (!WbClassificationMigration.TryParseTolerance(text, out double value, out _))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "退化孔容差倍数无法识别：" + text);
                return;
            }
            _settings.DegeneratePositionMultiplier = Math.Max(1.0, value);
            MarkSettingsChanged();
        }

        private void SetEnableDegenerateMatching(bool value)
        {
            if (_settings.EnableDegenerateMatching == value) return;
            _settings.EnableDegenerateMatching = value;
            MarkSettingsChanged();
        }

        private string FormatToleranceForCriterion(V5Criterion criterion)
        {
            string text = _settings.Tolerances[criterion].ToString("R", CultureInfo.InvariantCulture);
            return WbClassificationMigration.IsAbsoluteOnlyCriterion(criterion) || _settings.IsRelativeTolerance[criterion] == false
                ? text
                : text + "%";
        }

        private void MarkSettingsChanged()
        {
            _settingsFromDb = false;
            Attributes?.ExpireLayout();
            OnDisplayExpired(true);
        }

        // ================================================================
        // 辅助方法
        // ================================================================
        private bool CallMatches(WbFeature a, WbFeature b, bool mirror)
        {
            if (mirror)
                return WbMigrationAudit.MirrorMatches(a, b, _frozenSettings.Enabled, _frozenSettings.Tolerances, _frozenSettings.IsRelativeTolerance,
                    _frozenSettings.StrictMaterialColor, _frozenSettings.HoleNormalTolerance,
                    _frozenSettings.OuterProfileAreaRatio, _frozenSettings.OuterProfilePerimeterMin,
                    _frozenSettings.DegeneratePositionMultiplier, _frozenSettings.EnableDegenerateMatching,
                    fallbackTolerance: _frozenSettings.FallbackTolerance);
            return WbMigrationAudit.Matches(a, b, _frozenSettings.Enabled, _frozenSettings.Tolerances, _frozenSettings.IsRelativeTolerance,
                _frozenSettings.StrictMaterialColor, _frozenSettings.HoleNormalTolerance,
                _frozenSettings.OuterProfileAreaRatio, _frozenSettings.OuterProfilePerimeterMin,
                _frozenSettings.DegeneratePositionMultiplier, _frozenSettings.EnableDegenerateMatching,
                fallbackTolerance: _frozenSettings.FallbackTolerance);
        }

        private static string TextAt(List<string> values, int index) =>
            (values != null && index >= 0 && index < values.Count) ? (values[index] ?? string.Empty) : string.Empty;

        private static string BuildPrefixPart(string prefix, string connector)
        {
            if (string.IsNullOrEmpty(prefix)) return string.Empty;
            return prefix + (connector ?? string.Empty);
        }

        private static int ParseSequence(string number, string prefixPart)
        {
            if (string.IsNullOrEmpty(number)) return -1;
            if (!number.StartsWith(prefixPart, StringComparison.OrdinalIgnoreCase)) return -1;
            string remainder = number.Substring(prefixPart.Length);
            return int.TryParse(remainder, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seq) ? seq : -1;
        }

        private static string GenerateNewNumber(
            string prefix, string connector, int digitLength, int startSeq,
            List<WbMigrationRecord> existingParts, List<string> currentNumbers)
        {
            string prefixPart = BuildPrefixPart(prefix, connector);
            int maxSeq = startSeq - 1;

            if (existingParts != null)
            {
                foreach (var part in existingParts)
                {
                    int seq = ParseSequence(part.Number, prefixPart);
                    if (seq > maxSeq) maxSeq = seq;
                }
            }

            foreach (var num in currentNumbers)
            {
                if (string.IsNullOrEmpty(num)) continue;
                int seq = ParseSequence(num, prefixPart);
                if (seq > maxSeq) maxSeq = seq;
            }

            return prefixPart + (maxSeq + 1).ToString(
                "D" + digitLength.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        // ================================================================
        // 序列化
        // ================================================================
        public override bool Write(GH_IWriter writer)
        {
            _settings.Write(writer, SettingsChunk);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            _settings.Read(reader, SettingsChunk);
            return base.Read(reader);
        }

        public override void CreateAttributes()
        {
            m_attributes = new WbEntityClassifierAttributes(this);
        }

        // ================================================================
        // 右键设置菜单控件（布局与 WbClassificationMigration 一致）
        // ================================================================
        private sealed class WbEntityClassifierSettingsMenuControl : UserControl
        {
            private static readonly V5Criterion[] Criteria =
            {
                V5Criterion.Length,
                V5Criterion.Inertia,
                V5Criterion.Volume,
                V5Criterion.EdgeSum,
                V5Criterion.HoleArea,
                V5Criterion.HolePosition
            };

            private readonly WbEntityClassifier _owner;
            private readonly ToolTip _toolTip = new ToolTip();
            private readonly List<Action> _commitActions = new List<Action>();

            public WbEntityClassifierSettingsMenuControl(WbEntityClassifier owner)
            {
                _owner = owner;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = SystemColors.Control;
                Margin = Padding.Empty;
                Padding = new Padding(8, 6, 8, 8);
                Size = new Size(256, 544);

                TableLayoutPanel layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = Criteria.Length + 10,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
                for (int i = 0; i < Criteria.Length + 8; i++)
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
                Controls.Add(layout);

                AddHeader(layout);
                for (int i = 0; i < Criteria.Length; i++)
                    AddCriterionRow(layout, i + 1, Criteria[i]);

                AddFallbackToleranceRow(layout, Criteria.Length + 1);
                AddInertiaRow(layout, Criteria.Length + 2);
                AddMaterialStrictRow(layout, Criteria.Length + 3);
                AddOuterProfileAreaRow(layout, Criteria.Length + 4);
                AddOuterProfilePerimeterRow(layout, Criteria.Length + 5);
                AddDegenerateMultiplierRow(layout, Criteria.Length + 6);
                AddDegenerateMatchRow(layout, Criteria.Length + 7);
                AddNormalToleranceRow(layout, Criteria.Length + 8);
                AddHelp(layout, Criteria.Length + 9);
            }

            private void AddHeader(TableLayoutPanel layout)
            {
                layout.Controls.Add(CreateHeaderLabel(""), 0, 0);
                layout.Controls.Add(CreateHeaderLabel("容差"), 1, 0);
            }

            private void AddCriterionRow(TableLayoutPanel layout, int row, V5Criterion criterion)
            {
                bool isLength = criterion == V5Criterion.Length;
                CheckBox checkBox = new CheckBox
                {
                    Text = WbClassificationMigration.CriterionDisplayName(criterion),
                    Checked = _owner._settings.Enabled[criterion],
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 4, 1),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Enabled = !isLength
                };
                if (!isLength)
                    checkBox.CheckedChanged += (sender, e) => _owner.SetCriterionEnabled(criterion, checkBox.Checked);
                layout.Controls.Add(checkBox, 0, row);

                TextBox toleranceBox = CreateTextBox(_owner.FormatToleranceForCriterion(criterion));
                _toolTip.SetToolTip(toleranceBox, WbClassificationMigration.CriterionTooltip(criterion));
                RegisterTextBoxCommit(
                    toleranceBox,
                    () => _owner.SetCriterionTolerance(criterion, toleranceBox.Text),
                    () => _owner.FormatToleranceForCriterion(criterion));
                layout.Controls.Add(toleranceBox, 1, row);
            }

            private void AddFallbackToleranceRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("回退容差");
                layout.Controls.Add(label, 0, row);

                TextBox toleranceBox = CreateTextBox(_owner._settings.FallbackTolerance.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(toleranceBox, "无 Rhino 文档时的曲线拼接容差回退值。正常 Rhino 交互环境下不使用此值，仅在离线批处理或异常路径下生效。\n" +
                    "建议：0.0001~0.01（当前 Rhino 文档单位），默认 0.001。一般无需修改。");
                RegisterTextBoxCommit(
                    toleranceBox,
                    () => _owner.SetFallbackTolerance(toleranceBox.Text),
                    () => _owner._settings.FallbackTolerance.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(toleranceBox, 1, row);
            }

            private void AddInertiaRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("退化判定阈值");
                layout.Controls.Add(label, 0, row);

                TextBox ratioBox = CreateTextBox(_owner._settings.InertiaDegenerateRatio.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(ratioBox, "相邻主惯量比值 > 此值时标记为退化件（如近圆柱体、球体）。退化件缺少明确的主惯量轴方向，后续匹配走专用流程。\n" +
                    "范围：0.5~1.0，默认 0.99。建议 0.95~0.99。越低越容易判为退化（更保守），越高越严格。");
                RegisterTextBoxCommit(
                    ratioBox,
                    () => _owner.SetInertiaDegenerateRatio(ratioBox.Text),
                    () => _owner._settings.InertiaDegenerateRatio.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(ratioBox, 1, row);
            }

            private void AddOuterProfileAreaRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("外轮廓面积容差（仅诊断）");
                layout.Controls.Add(label, 0, row);
                TextBox tolBox = CreateTextBox(_owner._settings.OuterProfileAreaRatio.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "外轮廓已不参与匹配判定，此参数仅在同件判定诊断输出中参考。保留以兼容旧设置与旧数据库参数。");
                RegisterTextBoxCommit(
                    tolBox,
                    () => _owner.SetOuterProfileAreaRatio(tolBox.Text),
                    () => _owner._settings.OuterProfileAreaRatio.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddOuterProfilePerimeterRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("外轮廓周长最小容差（仅诊断）");
                layout.Controls.Add(label, 0, row);
                TextBox tolBox = CreateTextBox(_owner._settings.OuterProfilePerimeterMin.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "外轮廓已不参与匹配判定，此参数仅在同件判定诊断输出中参考。保留以兼容旧设置与旧数据库参数。");
                RegisterTextBoxCommit(
                    tolBox,
                    () => _owner.SetOuterProfilePerimeterMin(tolBox.Text),
                    () => _owner._settings.OuterProfilePerimeterMin.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddDegenerateMultiplierRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("退化孔容差倍数");
                layout.Controls.Add(label, 0, row);
                TextBox tolBox = CreateTextBox(_owner._settings.DegeneratePositionMultiplier.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "退化孔匹配时将周长、面积、位置容差乘以此倍数以放宽匹配门槛。拟合法向阈值固定按 normalTol × 0.95 放宽，不受此倍数影响。退化孔特征少、置信度低，需要更大的容差空间。\n" +
                    "范围：≥ 1.0，默认 2.0。建议 1.5~5.0。值越大越宽松，值越小越严格。");
                RegisterTextBoxCommit(
                    tolBox,
                    () => _owner.SetDegeneratePositionMultiplier(tolBox.Text),
                    () => _owner._settings.DegeneratePositionMultiplier.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddDegenerateMatchRow(TableLayoutPanel layout, int row)
            {
                CheckBox checkBox = new CheckBox
                {
                    Text = "退化孔容差放宽",
                    Checked = _owner._settings.EnableDegenerateMatching,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 4, 1),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _toolTip.SetToolTip(checkBox, "开启后，对双方均含退化孔的配对，将周长/面积/位置容差乘以退化孔容差倍数以放宽匹配门槛；拟合法向阈值固定按 normalTol × 0.95 放宽。\n" +
                    "关闭时退化孔件使用常规容差参与匹配，且始终正常参与自动聚类。默认关闭。");
                checkBox.CheckedChanged += (sender, e) => _owner.SetEnableDegenerateMatching(checkBox.Checked);
                layout.Controls.Add(checkBox, 0, row);

                Label hint = CreateBodyLabel("默认关闭：仅放宽容差，不控制聚类参与");
                hint.ForeColor = Color.DimGray;
                hint.AutoSize = false;
                hint.Dock = DockStyle.Fill;
                hint.TextAlign = ContentAlignment.MiddleLeft;
                layout.SetColumnSpan(hint, 1);
                layout.Controls.Add(hint, 1, row);
            }

            private void AddNormalToleranceRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("法向兼容阈值");
                layout.Controls.Add(label, 0, row);

                TextBox tolBox = CreateTextBox(_owner._settings.HoleNormalTolerance.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "|cos| 法向兼容阈值，同时用于孔法向比较和外轮廓法向比较。使用绝对值 |dot| 判断，正反法向均兼容。\n" +
                    "|cos(法向夹角)| 低于此值时判定为不兼容。0.9986 ≈ 3° 偏差。\n" +
                    "范围：0~1，默认 0.9986。建议 0.99~0.9999。越接近 1 越严格，越接近 0 越宽松。");
                RegisterTextBoxCommit(
                    tolBox,
                    () => _owner.SetHoleNormalTolerance(tolBox.Text),
                    () => _owner._settings.HoleNormalTolerance.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddMaterialStrictRow(TableLayoutPanel layout, int row)
            {
                CheckBox checkBox = new CheckBox
                {
                    Text = "材质/颜色严格模式",
                    Checked = _owner._settings.StrictMaterialColor,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 4, 1),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _toolTip.SetToolTip(checkBox, "开启后，材质和颜色必须双方均填写且相等，或双方均为空才匹配。关闭时任一方为空即视为匹配（宽松模式）。\n" +
                    "建议：同一批生产数据开启严格模式；跨厂家跨批数据关闭，避免空值导致无法匹配。");
                checkBox.CheckedChanged += (sender, e) => _owner.SetStrictMaterialColor(checkBox.Checked);
                layout.Controls.Add(checkBox, 0, row);

                Label hint = CreateBodyLabel("开启后空值不再通配");
                hint.ForeColor = Color.DimGray;
                hint.AutoSize = false;
                hint.Dock = DockStyle.Fill;
                hint.TextAlign = ContentAlignment.MiddleLeft;
                layout.SetColumnSpan(hint, 1);
                layout.Controls.Add(hint, 1, row);
            }

            private void AddHelp(TableLayoutPanel layout, int row)
            {
                string baseText = "提示：容差末尾带 % 为相对误差（如 0.5%），不带 % 为绝对误差。" +
                    "孔位强制绝对误差。鼠标悬停各参数可查看建议取值范围与详细说明。";
                if (_owner._settingsFromDb)
                    baseText += " 当前参数来自数据库 classification_profile，分类和入库均使用此快照。";
                else if (_owner._dbProfileWasLoaded)
                    baseText += " 本地参数已修改，下一次入库将更新数据库参数。";

                Label help = CreateBodyLabel(baseText);
                help.ForeColor = Color.DimGray;
                help.AutoSize = false;
                help.Dock = DockStyle.Fill;
                layout.SetColumnSpan(help, 2);
                layout.Controls.Add(help, 0, row);
            }

            private static Label CreateHeaderLabel(string text)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Font = new Font(SystemFonts.MenuFont, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 0, 4, 3)
                };
            }

            private static Label CreateBodyLabel(string text)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 1, 4, 1)
                };
            }

            private static TextBox CreateTextBox(string text)
            {
                return new TextBox
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 8, 1),
                    BorderStyle = BorderStyle.FixedSingle
                };
            }

            public void CommitAll()
            {
                foreach (Action commit in _commitActions.ToArray())
                    commit();
            }

            private void RegisterTextBoxCommit(TextBox box, Action commit, Func<string> format)
            {
                _commitActions.Add(commit);
                box.Leave += (sender, e) =>
                {
                    commit();
                    box.Text = format();
                };
                box.KeyDown += (sender, e) =>
                {
                    if (e.KeyCode != Keys.Enter)
                        return;
                    commit();
                    box.Text = format();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                };
            }
        }
    }

    // ========================================================================
    // 自定义渲染（保存 / 入库按钮）
    // ========================================================================
    internal sealed class WbEntityClassifierAttributes : GH_ComponentAttributes
    {
        public WbEntityClassifierAttributes(WbEntityClassifier owner)
            : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 24f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            RectangleF rect = new RectangleF(
                Bounds.X, Bounds.Bottom - 24f, Bounds.Width, 20f);
            rect.Inflate(-5f, -2f);
            using (GH_Capsule cap = GH_Capsule.CreateCapsule(rect, GH_Palette.Black))
                cap.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            using (Font f = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat s = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("保存 / 入库", f, Brushes.White, rect, s);
        }

        public override GH_ObjectResponse RespondToMouseDown(
            GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF rect = new RectangleF(
                Bounds.X, Bounds.Bottom - 24f, Bounds.Width, 20f);

            if (e.Button == MouseButtons.Left && rect.Contains(e.CanvasLocation))
            {
                ((WbEntityClassifier)Owner).SaveToDatabase();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }
    }
}
