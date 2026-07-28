using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.FileIO;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>
    /// 迁移专用的 V5 特征计算器。它只生成可保存的特征文本，不读取或写入数据库。
    /// </summary>
    internal sealed class EntityFeatureMigrationV5 : GH_Component
    {
        private const string SettingsChunk = "EntityFeatureMigrationV5Settings";
        private readonly Dictionary<V5Criterion, int> _decimals = new Dictionary<V5Criterion, int>
        {
            { V5Criterion.Length, 2 }, { V5Criterion.Inertia, 2 }, { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 },
            { V5Criterion.EdgeSum, 2 }, { V5Criterion.HolePosition, 4 }
        };

        public EntityFeatureMigrationV5()
            : base("实体分类特征计算（迁移版）", "实体特征V5", "计算迁移专用的 V5 实体分类特征；不写入数据库。", "Parrot", "Library") { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("材质", "材质", "与实体一一对应的材质，可为空。", GH_ParamAccess.list);
            p.AddTextParameter("颜色", "颜色", "与实体一一对应的颜色，可为空。", GH_ParamAccess.list);
            p.AddTextParameter("包围盒三边", "包围盒", "与实体一一对应。输入“最长边”或“最长边,中边,短边”；后两边可空。", GH_ParamAccess.list);
            p.AddBrepParameter("实体列表", "实体", "待计算的 Brep 实体。", GH_ParamAccess.list);
            p[0].Optional = true; p[1].Optional = true; p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("V5分类特征", "特征", "可直接输入“分类数据库迁移（V5）”的标准特征文本。", GH_ParamAccess.list);
            p.AddTextParameter("特征摘要", "摘要", "便于检查的实体特征摘要。", GH_ParamAccess.list);
            p.AddTextParameter("状态", "状态", "计算状态。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var materials = new List<string>(); var colors = new List<string>(); var lengths = new List<string>(); var breps = new List<Brep>();
            da.GetDataList(0, materials); da.GetDataList(1, colors); da.GetDataList(2, lengths);
            if (!da.GetDataList(3, breps) || breps.Count == 0) return;
            ValidateOptionalCount(materials, breps.Count, "材质");
            ValidateOptionalCount(colors, breps.Count, "颜色");
            ValidateOptionalCount(lengths, breps.Count, "包围盒三边");
            var features = new List<string>(); var summaries = new List<string>();
            for (int i = 0; i < breps.Count; i++)
            {
                try
                {
                    V5Feature feature = V5Feature.Create(TextAt(materials, i), TextAt(colors, i), TextAt(lengths, i), breps[i], _decimals, i);
                    features.Add(feature.Serialize()); summaries.Add(feature.ToSummary());
                }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
            }
            da.SetDataList(0, features); da.SetDataList(1, summaries);
            da.SetData(2, "已计算 " + features.Count.ToString(CultureInfo.InvariantCulture) + " 个 V5 分类特征。");
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu); Menu_AppendSeparator(menu);
            AddDecimalItem(menu, V5Criterion.Length, "包围盒入库精度"); AddDecimalItem(menu, V5Criterion.Inertia, "惯量入库精度"); AddDecimalItem(menu, V5Criterion.Area, "表面积入库精度");
            AddDecimalItem(menu, V5Criterion.Volume, "体积入库精度"); AddDecimalItem(menu, V5Criterion.EdgeSum, "所有边周长入库精度");
            AddDecimalItem(menu, V5Criterion.HolePosition, "孔位入库精度");
        }

        private void AddDecimalItem(ToolStripDropDown menu, V5Criterion criterion, string name)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(name + "：" + _decimals[criterion].ToString(CultureInfo.InvariantCulture) + " 位");
            item.Click += (s, e) =>
            {
                using (var dialog = new Form { Text = name, Width = 240, Height = 125, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false })
                using (var box = new NumericUpDown { Minimum = 0, Maximum = 12, Value = _decimals[criterion], Left = 20, Top = 15, Width = 180 })
                using (var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 125, Top = 48, Width = 75 })
                {
                    dialog.Controls.Add(box); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                    if (dialog.ShowDialog() == DialogResult.OK) { _decimals[criterion] = (int)box.Value; ExpireSolution(true); }
                }
            };
            menu.Items.Add(item);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetInt32(SettingsChunk + "Length", _decimals[V5Criterion.Length]); writer.SetInt32(SettingsChunk + "Inertia", _decimals[V5Criterion.Inertia]); writer.SetInt32(SettingsChunk + "Area", _decimals[V5Criterion.Area]);
            writer.SetInt32(SettingsChunk + "Volume", _decimals[V5Criterion.Volume]); writer.SetInt32(SettingsChunk + "Edge", _decimals[V5Criterion.EdgeSum]); writer.SetInt32(SettingsChunk + "Hole", _decimals[V5Criterion.HolePosition]);
            return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            ReadDecimal(reader, "Length", V5Criterion.Length); ReadDecimal(reader, "Inertia", V5Criterion.Inertia); ReadDecimal(reader, "Area", V5Criterion.Area); ReadDecimal(reader, "Volume", V5Criterion.Volume); ReadDecimal(reader, "Edge", V5Criterion.EdgeSum); ReadDecimal(reader, "Hole", V5Criterion.HolePosition);
            return base.Read(reader);
        }
        private void ReadDecimal(GH_IReader reader, string key, V5Criterion criterion) { int value = 0; if (reader.TryGetInt32(SettingsChunk + key, ref value)) _decimals[criterion] = Math.Max(0, Math.Min(12, value)); }
        private static void ValidateOptionalCount(List<string> list, int count, string name) { if (list != null && list.Count != 0 && list.Count != count) throw new ArgumentException(name + "数量为空时允许省略；不为空时必须与实体数量一致。"); }
        private static string TextAt(List<string> list, int index) { return list == null || list.Count == 0 ? string.Empty : (list[index] ?? string.Empty); }
        public override Guid ComponentGuid => new Guid("B1D97B01-3A4A-4F3C-91F0-4AC1D59EE801");
    }

    /// <summary>将 V5 特征和旧编号写入新的迁移数据库，并报告旧编号与真实特征的冲突。</summary>
    internal sealed class EntityFeatureDatabaseMigrationV5 : GH_Component
    {
        private const string SettingsChunk = "EntityFeatureDatabaseMigrationV5Settings";
        private readonly Dictionary<V5Criterion, bool> _enabled = new Dictionary<V5Criterion, bool>
        {
            { V5Criterion.Length, true }, { V5Criterion.Inertia, true }, { V5Criterion.Area, false },
            { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, false }, { V5Criterion.HolePosition, true }
        };
        private readonly Dictionary<V5Criterion, double> _tolerances = new Dictionary<V5Criterion, double>
        {
            { V5Criterion.Length, .5 }, { V5Criterion.Inertia, .2 }, { V5Criterion.Area, 1 },
            { V5Criterion.Volume, .5 }, { V5Criterion.EdgeSum, 1 }, { V5Criterion.HolePosition, .5 }
        };
        private double _mirrorTolerance = .001;
        private bool _rebuildRequested;

        public EntityFeatureDatabaseMigrationV5()
            : base("分类数据库迁移（V5）", "分类库迁移V5", "按 V5 分类特征重建迁移数据库，并校核同件异号与异件同号。", "Parrot", "Library") { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("新版数据库地址", "数据库", "迁移专用 SQLite 数据库文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("旧编号", "旧编号", "与分类特征一一对应的完整旧编号。", GH_ParamAccess.list);
            p.AddTextParameter("分类特征", "特征", "由“实体分类特征计算（迁移版）”输出的 V5 分类特征。", GH_ParamAccess.list);
        }
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("日志", "日志", "校核和迁移日志。", GH_ParamAccess.list);
            p.AddTextParameter("状态", "状态", "迁移状态。", GH_ParamAccess.item);
            p.AddBooleanParameter("重建完成", "完成", "成功重建新版数据库时为 True。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string path = string.Empty; var numbers = new List<string>(); var texts = new List<string>();
            if (!da.GetData(0, ref path) || !da.GetDataList(1, numbers) || !da.GetDataList(2, texts)) return;
            var logs = new List<string>(); bool saved = false;
            try
            {
                if (numbers.Count != texts.Count || numbers.Count == 0) throw new ArgumentException("旧编号和分类特征必须为数量相同的非空列表。");
                var records = new List<V5MigrationRecord>();
                for (int i = 0; i < texts.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(numbers[i])) throw new ArgumentException("第 " + (i + 1).ToString(CultureInfo.InvariantCulture) + " 个旧编号为空。");
                    records.Add(new V5MigrationRecord(i, numbers[i].Trim(), V5Feature.Deserialize(texts[i])));
                }
                V5MigrationAudit audit = V5MigrationAudit.Create(records, _enabled, _tolerances, _mirrorTolerance);
                logs.AddRange(audit.ToLogs());
                if (_rebuildRequested)
                {
                    V5MigrationStore.Rebuild(path, records, audit); saved = true; _rebuildRequested = false;
                    logs.Insert(0, "已重建 V5 迁移数据库：" + path);
                }
                else logs.Insert(0, "校核完成；右键点击“重建新版数据库”后才会写入数据库。");
            }
            catch (Exception ex) { logs.Insert(0, "错误：" + ex.Message); AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); _rebuildRequested = false; }
            da.SetDataList(0, logs); da.SetData(1, logs[0]); da.SetData(2, saved);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu); Menu_AppendSeparator(menu);
            foreach (V5Criterion criterion in Enum.GetValues(typeof(V5Criterion)).Cast<V5Criterion>().Where(x => x != V5Criterion.Mirror)) AddToleranceItem(menu, criterion);
            ToolStripMenuItem mirror = new ToolStripMenuItem("镜像区分容差：" + _mirrorTolerance.ToString("G6", CultureInfo.InvariantCulture));
            mirror.Click += (s, e) => EditDouble("镜像区分容差", _mirrorTolerance, value => _mirrorTolerance = Math.Abs(value)); menu.Items.Add(mirror);
            Menu_AppendSeparator(menu);
            ToolStripMenuItem rebuild = Menu_AppendItem(menu, "重建新版数据库", (s, e) => { _rebuildRequested = true; ExpireSolution(true); });
            rebuild.ToolTipText = "先备份目标文件，再重建 V5 迁移表。";
        }

        private void AddToleranceItem(ToolStripDropDown menu, V5Criterion criterion)
        {
            string name = V5Feature.CriterionName(criterion);
            ToolStripMenuItem enabled = Menu_AppendItem(menu, name + "（参与校核）", (s, e) => { _enabled[criterion] = !_enabled[criterion]; ExpireSolution(true); }, true, _enabled[criterion]);
            ToolStripMenuItem value = new ToolStripMenuItem(name + "容差：" + _tolerances[criterion].ToString("G6", CultureInfo.InvariantCulture));
            value.Click += (s, e) => EditDouble(name + "容差", _tolerances[criterion], x => _tolerances[criterion] = Math.Abs(x)); menu.Items.Add(value);
        }
        private void EditDouble(string title, double current, Action<double> set)
        {
            using (var form = new Form { Text = title, Width = 250, Height = 130, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false })
            using (var box = new TextBox { Text = current.ToString("G17", CultureInfo.InvariantCulture), Left = 18, Top = 15, Width = 195 })
            using (var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 138, Top = 48, Width = 75 })
            {
                form.Controls.Add(box); form.Controls.Add(ok); form.AcceptButton = ok;
                if (form.ShowDialog() == DialogResult.OK && double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) { set(value); ExpireSolution(true); }
            }
        }
        public override bool Write(GH_IWriter writer)
        {
            foreach (V5Criterion c in _enabled.Keys) { writer.SetBoolean(SettingsChunk + c + "Enabled", _enabled[c]); writer.SetDouble(SettingsChunk + c + "Tolerance", _tolerances[c]); }
            writer.SetDouble(SettingsChunk + "Mirror", _mirrorTolerance); return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            foreach (V5Criterion c in _enabled.Keys.ToList()) { bool e = _enabled[c]; double t = _tolerances[c]; if (reader.TryGetBoolean(SettingsChunk + c + "Enabled", ref e)) _enabled[c] = e; if (reader.TryGetDouble(SettingsChunk + c + "Tolerance", ref t)) _tolerances[c] = Math.Abs(t); }
            double m = _mirrorTolerance; if (reader.TryGetDouble(SettingsChunk + "Mirror", ref m)) _mirrorTolerance = Math.Abs(m); return base.Read(reader);
        }
        public override Guid ComponentGuid => new Guid("B1D97B02-3A4A-4F3C-91F0-4AC1D59EE802");
    }

    internal enum V5Criterion { Length, Inertia, Area, Volume, EdgeSum, HolePosition, Mirror, HoleArea }

    /// <summary>
    /// 一体化 V5 迁移：从 3dm 文件按 GUID 提取实体、计算特征、校核并重建迁移数据库。
    /// 使用 File3dm 只读加载，不会切换或关闭用户当前打开的 Rhino 文档。
    /// </summary>
    public sealed class EntityFeatureMigrationV5Integrated : GH_Component
    {
        private enum MigrationState { Idle, OpeningCurrentFile, AwaitingCurrentFileData }
        private const string SettingsChunk = "EntityFeatureMigrationV5IntegratedSettings";
        private readonly Dictionary<V5Criterion, int> _decimals = new Dictionary<V5Criterion, int>
        {
            { V5Criterion.Length, 2 }, { V5Criterion.Inertia, 2 }, { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 }, { V5Criterion.EdgeSum, 2 }, { V5Criterion.HoleArea, 2 }, { V5Criterion.HolePosition, 4 }
        };
        private readonly Dictionary<V5Criterion, bool> _enabled = new Dictionary<V5Criterion, bool>
        {
            { V5Criterion.Length, true }, { V5Criterion.Inertia, true }, { V5Criterion.Area, false }, { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true }, { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, true }
        };
        private readonly Dictionary<V5Criterion, double> _tolerances = new Dictionary<V5Criterion, double>
        {
            { V5Criterion.Length, .5 }, { V5Criterion.Inertia, .2 }, { V5Criterion.Area, 1 }, { V5Criterion.Volume, .5 }, { V5Criterion.EdgeSum, 1 }, { V5Criterion.HoleArea, 1 }, { V5Criterion.HolePosition, .5 }
        };
        private double _mirrorTolerance = .001;
        private bool _runRequested;
        private bool _paused;
        private bool _lastRunInput;
        private MigrationState _state;
        private readonly List<string> _pendingFiles = new List<string>();
        private readonly List<V6MigrationRecord> _collectedRecords = new List<V6MigrationRecord>();
        private readonly List<string> _diagnostics = new List<string>();
        private int _fileIndex;
        private int _waitingAttempts;
        private string _lastDataSignature = string.Empty;
        private int _stableDataCount;
        private int _dataCheckCount;
        private int _refreshScheduleToken;
        private Timer _refreshTimer;
        private Timer _fileOpenTimer;
        private Timer _finalCloseTimer;
        private string _automationStatus = string.Empty;
        private string _pendingDatabasePath = string.Empty;

        public void StartMigration()
        {
            CancelPendingRefresh();
            StopFileOpenTimer();
            StopFinalCloseTimer();
            _paused = false;
            _state = MigrationState.Idle;
            _pendingFiles.Clear();
            _collectedRecords.Clear();
            _diagnostics.Clear();
            _fileIndex = 0;
            _waitingAttempts = 0;
            _pendingDatabasePath = string.Empty;
            _runRequested = true;
            ScheduleNextCollection();
        }
        public void PauseMigration()
        {
            if (_state != MigrationState.AwaitingCurrentFileData) return;
            _paused = true;
            CancelPendingRefresh();
        }
        public void ResumeMigration()
        {
            if (!_paused || _state != MigrationState.AwaitingCurrentFileData) return;
            _paused = false;
            ScheduleNextCollection();
        }
        public bool IsPaused => _paused;

        public EntityFeatureMigrationV5Integrated()
            : base("分类数据库迁移（V8）", "分类库迁移V8", "按 3dm 文件路径和实体 GUID 计算稳定特征、校核并迁移 V8 分类数据库。", "Parrot", "Library") { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("新版数据库地址", "数据库", "迁移专用 SQLite 数据库文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("旧编号", "旧编号", "与文件路径和实体 GUID 一一对应的完整旧编号。", GH_ParamAccess.list);
            p.AddTextParameter("材质", "材质", "与实体一一对应的材质，可为空。", GH_ParamAccess.list);
            p.AddTextParameter("颜色", "颜色", "与实体一一对应的颜色，可为空。", GH_ParamAccess.list);
            p.AddTextParameter("文件路径", "文件", "与实体 GUID 一一对应的 .3dm 文件路径。", GH_ParamAccess.list);
            p.AddGenericParameter("实体 GUID", "GUID", "文件中待迁移 Brep 实体的 GUID。", GH_ParamAccess.list);
            p.AddBooleanParameter("启动", "启动", "上升沿触发；清空当前进度并从第 0 个文件开始。", GH_ParamAccess.item, false);
            p.AddBooleanParameter("暂停", "暂停", "True 时暂停；恢复为 False 后从当前文件继续。", GH_ParamAccess.item, false);
            // 旧编号和 GUID 由打开当前文件后的上游电池自动生成；启动前不能作为求解前提。
            p[1].Optional = true; p[2].Optional = true; p[3].Optional = true; p[5].Optional = true;
        }
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("V8分类特征", "特征", "本次计算并写入迁移库的标准 V8 特征。", GH_ParamAccess.list);
            p.AddTextParameter("日志", "日志", "校核和迁移日志。", GH_ParamAccess.list);
            p.AddTextParameter("状态", "状态", "迁移状态。", GH_ParamAccess.item);
            p.AddBooleanParameter("重建完成", "完成", "成功重建新版数据库时为 True。", GH_ParamAccess.item);
            p.AddTextParameter("详细诊断", "诊断", "质心、主轴、逐孔坐标和匹配距离等诊断参数。", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            string database = string.Empty; var numbers = new List<string>(); var materials = new List<string>(); var colors = new List<string>(); var files = new List<string>(); var ids = new List<GH_Guid>();
            var logs = new List<string>(); var featureTexts = new List<string>(); bool saved = false;
            bool run = false; bool pause = false;
            da.GetData(0, ref database); da.GetDataList(4, files);
            da.GetData(6, ref run); da.GetData(7, ref pause);
            da.SetDataList(4, _diagnostics);
            bool runRisingEdge = run && !_lastRunInput;
            _lastRunInput = run;
            if (runRisingEdge)
            {
                CancelPendingRefresh();
                StopFileOpenTimer();
                StopFinalCloseTimer();
                _paused = false;
                _state = MigrationState.Idle;
                _pendingFiles.Clear();
                _collectedRecords.Clear();
                _diagnostics.Clear();
                _fileIndex = 0;
                _waitingAttempts = 0;
                _pendingDatabasePath = string.Empty;
                _runRequested = true;
            }
            bool wasPaused = _paused;
            if (pause)
            {
                if (!_paused) { CancelPendingRefresh(); StopFileOpenTimer(); }
                _paused = true;
                da.SetDataList(0, featureTexts); da.SetDataList(1, new[] { "已暂停：将“暂停”恢复为 False 后从当前文件继续；再次触发“启动”则从第 0 个文件重新开始。" });
                da.SetData(2, "已暂停。"); da.SetData(3, false); return;
            }
            _paused = false;
            if (wasPaused && _state == MigrationState.OpeningCurrentFile) ScheduleOpenCurrentFile();
            if (_runRequested)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(database)) throw new ArgumentException("新版数据库地址不能为空。");
                    if (files.Count == 0) throw new ArgumentException("文件路径不能为空。");
                    if (files.Any(string.IsNullOrWhiteSpace) || files.Any(path => !File.Exists(path))) throw new FileNotFoundException("文件路径中包含不存在的文件。");
                    _pendingFiles.Clear(); _pendingFiles.AddRange(files.Select(Path.GetFullPath)); _pendingDatabasePath = database; _collectedRecords.Clear(); _fileIndex = 0; _waitingAttempts = 0; _state = MigrationState.OpeningCurrentFile;
                    ResetDataStability();
                    _automationStatus = "准备打开第 1 / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。";
                    ScheduleOpenCurrentFile();
                    logs.Add("已启动迁移；本轮求解结束后打开第 1 / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。");
                }
                catch (Exception ex) { logs.Add("错误：" + ex.Message); AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); ResetMigration(); }
                finally { _runRequested = false; }
                da.SetDataList(0, featureTexts); da.SetDataList(1, logs); da.SetData(2, logs[0]); da.SetData(3, false); return;
            }

            if (_state == MigrationState.OpeningCurrentFile)
            {
                logs.Add(string.IsNullOrWhiteSpace(_automationStatus) ? "等待在求解结束后打开当前文件。" : _automationStatus);
                da.SetDataList(0, featureTexts); da.SetDataList(1, logs); da.SetData(2, logs[0]); da.SetData(3, false); return;
            }

            if (_state != MigrationState.AwaitingCurrentFileData)
            {
                da.SetDataList(0, featureTexts); da.SetDataList(1, new[] { string.IsNullOrWhiteSpace(_automationStatus) ? "等待启动；触发“启动”输入后自动处理全部文件。" : _automationStatus });
                da.SetData(2, string.IsNullOrWhiteSpace(_automationStatus) ? "等待启动。" : _automationStatus); da.SetData(3, false); return;
            }

            // 当前文件打开后，才向上游收集自动产生的旧编号、GUID 与属性。
            da.GetDataList(1, numbers); da.GetDataList(2, materials); da.GetDataList(3, colors); da.GetDataList(5, ids);
            if (!IsDataStable(numbers, ids))
            {
                if (_dataCheckCount >= 20)
                {
                    logs.Add("跳过当前文件：当前文件路径、旧编号和 GUID 的值未稳定；最后数量=" + numbers.Count.ToString(CultureInfo.InvariantCulture) + "/" + ids.Count.ToString(CultureInfo.InvariantCulture) + "。");
                    saved = AdvanceFile(logs); da.SetDataList(0, featureTexts); da.SetDataList(1, logs); da.SetData(2, logs[0]); da.SetData(3, saved); return;
                }
                logs.Add("等待当前文件路径、旧编号和 GUID 的值稳定：当前数量=" + numbers.Count.ToString(CultureInfo.InvariantCulture) + "/" + ids.Count.ToString(CultureInfo.InvariantCulture) + "，稳定=" + _stableDataCount.ToString(CultureInfo.InvariantCulture) + "/3。");
                ScheduleNextCollectionAfterCurrentSolution(500); da.SetDataList(0, featureTexts); da.SetDataList(1, logs); da.SetData(2, logs[0]); da.SetData(3, false); return;
            }
            if (numbers.Count > 1 || ids.Count > 1)
            {
                logs.Add("跳过当前文件：旧编号数量=" + numbers.Count.ToString(CultureInfo.InvariantCulture) + "，GUID 数量=" + ids.Count.ToString(CultureInfo.InvariantCulture) + "；每个文件必须各有且只有 1 个。");
                saved = AdvanceFile(logs); da.SetDataList(0, featureTexts); da.SetDataList(1, logs); da.SetData(2, logs[0]); da.SetData(3, saved); return;
            }
            if (numbers.Count == 0 || ids.Count == 0)
            {
                _waitingAttempts++;
                if (_waitingAttempts <= 50)
                {
                    logs.Add("等待当前文件自动生成旧编号和 GUID（" + _waitingAttempts.ToString(CultureInfo.InvariantCulture) + " / 50）。");
                    ScheduleNextCollectionAfterCurrentSolution(); da.SetDataList(0, featureTexts); da.SetDataList(1, logs); da.SetData(2, logs[0]); da.SetData(3, false); return;
                }
                logs.Add("跳过当前文件：等待旧编号和 GUID 超时；每个文件必须各有且只有 1 个。"); saved = AdvanceFile(logs); da.SetDataList(0, featureTexts); da.SetDataList(1, logs); da.SetData(2, logs[0]); da.SetData(3, saved); return;
            }
            try
            {
                int count = ids.Count;
                ValidateCount(numbers, count, "旧编号", false); ValidateCount(materials, count, "材质", true); ValidateCount(colors, count, "颜色", true);
                for (int i = 0; i < count; i++)
                {
                    if (string.IsNullOrWhiteSpace(numbers[i])) throw new ArgumentException("第 " + (i + 1).ToString(CultureInfo.InvariantCulture) + " 个旧编号为空。");
                    if (ids[i] == null || ids[i].Value == Guid.Empty) throw new ArgumentException("第 " + (i + 1).ToString(CultureInfo.InvariantCulture) + " 个 GUID 无效。");
                    Brep brep = ReadBrepFromActiveDocument(ids[i].Value, i);
                    V6Feature feature = V6Feature.Create(TextAt(materials, i), TextAt(colors, i), brep, _decimals, _collectedRecords.Count);
                    _collectedRecords.Add(new V6MigrationRecord(_collectedRecords.Count, numbers[i].Trim(), feature, _pendingFiles[_fileIndex])); featureTexts.Add(feature.Serialize());
                    _diagnostics.AddRange(feature.DiagnosticLines(numbers[i].Trim(), _pendingFiles[_fileIndex]));
                }
                saved = AdvanceFile(logs);
            }
            catch (Exception ex) { logs.Insert(0, "错误：" + ex.Message); AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); ResetMigration(); }
            da.SetDataList(0, featureTexts); da.SetDataList(1, logs); da.SetData(2, logs[0]); da.SetData(3, saved); da.SetDataList(4, _diagnostics);
        }
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu); Menu_AppendSeparator(menu);
            AddDecimalItem(menu, V5Criterion.Inertia); AddDecimalItem(menu, V5Criterion.Area); AddDecimalItem(menu, V5Criterion.Volume); AddDecimalItem(menu, V5Criterion.EdgeSum); AddDecimalItem(menu, V5Criterion.HolePosition);
            Menu_AppendSeparator(menu);
            AddRequiredBoundingBoxToleranceItem(menu);
            foreach (V5Criterion c in _enabled.Keys.Where(x => x != V5Criterion.Length).ToList()) AddToleranceItem(menu, c);
            // 镜像不再使用采样分数和容差，而是在逐孔比较中分别尝试正规与反射变换。
        }
        private void AddDecimalItem(ToolStripDropDown menu, V5Criterion c) { ToolStripMenuItem item = new ToolStripMenuItem(V5Feature.CriterionName(c) + "入库精度：" + _decimals[c].ToString(CultureInfo.InvariantCulture) + " 位"); item.Click += (s, e) => EditInteger(V5Feature.CriterionName(c) + "入库精度", _decimals[c], x => _decimals[c] = x); menu.Items.Add(item); }
        private void AddRequiredBoundingBoxToleranceItem(ToolStripDropDown menu) { ToolStripMenuItem item = new ToolStripMenuItem("包围盒容差（必选）：" + _tolerances[V5Criterion.Length].ToString("G6", CultureInfo.InvariantCulture)); item.Click += (s, e) => EditDouble("包围盒容差", _tolerances[V5Criterion.Length], x => _tolerances[V5Criterion.Length] = Math.Abs(x)); menu.Items.Add(item); }
        private void AddToleranceItem(ToolStripDropDown menu, V5Criterion c) { Menu_AppendItem(menu, V5Feature.CriterionName(c) + "（参与校核）", (s, e) => { _enabled[c] = !_enabled[c]; ExpireSolution(true); }, true, _enabled[c]); ToolStripMenuItem item = new ToolStripMenuItem(V5Feature.CriterionName(c) + "容差：" + _tolerances[c].ToString("G6", CultureInfo.InvariantCulture)); item.Click += (s, e) => EditDouble(V5Feature.CriterionName(c) + "容差", _tolerances[c], x => _tolerances[c] = Math.Abs(x)); menu.Items.Add(item); }
        private void EditInteger(string title, int current, Action<int> set) { using (var form = new Form { Text = title, Width = 240, Height = 125, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false }) using (var box = new NumericUpDown { Minimum = 0, Maximum = 12, Value = current, Left = 20, Top = 15, Width = 180 }) using (var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 125, Top = 48, Width = 75 }) { form.Controls.Add(box); form.Controls.Add(ok); form.AcceptButton = ok; if (form.ShowDialog() == DialogResult.OK) { set((int)box.Value); ExpireSolution(true); } } }
        private void EditDouble(string title, double current, Action<double> set) { using (var form = new Form { Text = title, Width = 250, Height = 130, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false }) using (var box = new TextBox { Text = current.ToString("G17", CultureInfo.InvariantCulture), Left = 18, Top = 15, Width = 195 }) using (var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 138, Top = 48, Width = 75 }) { form.Controls.Add(box); form.Controls.Add(ok); form.AcceptButton = ok; if (form.ShowDialog() == DialogResult.OK && double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) { set(value); ExpireSolution(true); } } }
        private static void ValidateCount<T>(List<T> values, int count, string name, bool optional) { if (count == 0) throw new ArgumentException("实体 GUID 不能为空。"); if (values == null || values.Count == 0) { if (optional) return; throw new ArgumentException(name + "不能为空。"); } if (values.Count != count) throw new ArgumentException(name + "数量必须与实体 GUID 数量一致。"); }
        private static string TextAt(List<string> values, int index) { return values == null || values.Count == 0 ? string.Empty : values[index] ?? string.Empty; }
        private void OpenCurrentFile()
        {
            string filePath = _pendingFiles[_fileIndex];
            if (IsActiveRhinoFile(filePath)) return;
            RhinoDoc currentDocument = RhinoDoc.ActiveDoc;
            if (currentDocument != null)
            {
                currentDocument.Modified = false;
                RhinoApp.RunScript("_-Close _No", false);
                RhinoApp.Wait();
            }
            string escapedPath = filePath.Replace("\"", "\"\"");
            if (!RhinoApp.RunScript("_-Open \"" + escapedPath + "\"", false)) throw new InvalidOperationException("无法打开文件：" + filePath);
            RhinoApp.Wait();
            RhinoDoc document = RhinoDoc.ActiveDoc;
            if (document == null) throw new InvalidOperationException("无法打开文件：" + _pendingFiles[_fileIndex]);
            if (!IsActiveRhinoFile(filePath)) throw new InvalidOperationException("命令已执行但当前文档未切换到：" + filePath);
        }
        private static bool IsActiveRhinoFile(string filePath)
        {
            RhinoDoc document = RhinoDoc.ActiveDoc;
            if (document == null) return false;
            return string.Equals(NormalizePath(document.Path), NormalizePath(filePath), StringComparison.OrdinalIgnoreCase);
        }
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return Path.GetFullPath(path.Trim()); }
            catch { return path.Trim(); }
        }
        private static void CloseCurrentFile()
        {
            RhinoDoc document = RhinoDoc.ActiveDoc;
            if (document == null) return;
            document.Modified = false;
            RhinoApp.RunScript("_-Close _No", false);
            RhinoApp.Wait();
        }
        private static Brep ReadBrepFromActiveDocument(Guid id, int index)
        {
            RhinoDoc document = RhinoDoc.ActiveDoc;
            Rhino.DocObjects.RhinoObject obj = document?.Objects.FindId(id);
            if (obj == null) throw new InvalidOperationException("当前文件中找不到第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 GUID：" + id);
            if (obj.Geometry is Brep brep) return brep.DuplicateBrep();
            if (obj.Geometry is Surface surface) return surface.ToBrep();
            if (obj.Geometry is Extrusion extrusion) return extrusion.ToBrep();
            throw new InvalidOperationException("GUID " + id + " 对应的对象不是 Brep、曲面或拉伸体。");
        }
        private void ResetDataStability()
        {
            _lastDataSignature = string.Empty;
            _stableDataCount = 0;
            _dataCheckCount = 0;
        }
        private bool IsDataStable(List<string> numbers, List<GH_Guid> ids)
        {
            _dataCheckCount++;
            string activePath = NormalizePath(RhinoDoc.ActiveDoc == null ? string.Empty : RhinoDoc.ActiveDoc.Path);
            string numberValues = string.Join("\u001f", numbers.Select(x => x ?? string.Empty));
            string guidValues = string.Join("\u001f", ids.Select(x => x == null ? string.Empty : x.Value.ToString("D")));
            string signature = activePath + "\u001e" + numberValues + "\u001e" + guidValues;
            if (string.Equals(signature, _lastDataSignature, StringComparison.Ordinal)) _stableDataCount++;
            else
            {
                _lastDataSignature = signature;
                _stableDataCount = 1;
            }
            return _stableDataCount >= 3;
        }
        private void ResetMigration()
        {
            CancelPendingRefresh();
            StopFileOpenTimer();
            _state = MigrationState.Idle; _runRequested = false; _paused = false; _pendingFiles.Clear(); _fileIndex = 0; _waitingAttempts = 0; _pendingDatabasePath = string.Empty;
        }
        private void ScheduleNextCollection(int waitMilliseconds = 1500)
        {
            CancelPendingRefresh();
            int token = ++_refreshScheduleToken;
            GH_Document document = OnPingDocument();
            if (document != null)
            {
                document.ScheduleSolution(Math.Max(1, waitMilliseconds), doc =>
                {
                    if (token != _refreshScheduleToken) return;
                    ExpireSolution(false);
                });
                return;
            }
            _refreshTimer = new Timer { Interval = Math.Max(1, waitMilliseconds) };
            _refreshTimer.Tick += (sender, e) =>
            {
                Timer timer = _refreshTimer;
                _refreshTimer = null;
                timer?.Stop();
                timer?.Dispose();
                if (token != _refreshScheduleToken) return;
                ExpireSolution(false);
            };
            _refreshTimer.Start();
        }
        private void ScheduleNextCollectionAfterCurrentSolution(int waitMilliseconds = 1500)
        {
            ScheduleNextCollection(waitMilliseconds);
        }
        private void CancelPendingRefresh()
        {
            _refreshScheduleToken++;
            if (_refreshTimer == null) return;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _refreshTimer = null;
        }
        private bool AdvanceFile(List<string> logs)
        {
            _fileIndex++;
            if (_fileIndex < _pendingFiles.Count)
            {
                _waitingAttempts = 0; _state = MigrationState.OpeningCurrentFile; ResetDataStability();
                _automationStatus = "准备打开第 " + (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。";
                ScheduleOpenCurrentFile();
                logs.Add("当前文件处理完成；本轮求解结束后打开第 " + (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。");
                return false;
            }
            V6MigrationAudit audit = V6MigrationAudit.Create(_collectedRecords, _enabled, _tolerances);
            _diagnostics.AddRange(audit.DiagnosticLines());
            List<V6MigrationRecord> recordsToInsert = audit.RecordsToInsert();
            V6MigrationStore.Rebuild(_pendingDatabasePath, recordsToInsert, audit, _enabled, _tolerances, _decimals);
            V6MigrationStore.Verify(_pendingDatabasePath, recordsToInsert);
            logs.Add("已重建并回读校核 V8 分类数据库，写入 " + recordsToInsert.Count.ToString(CultureInfo.InvariantCulture) + " 条（已读取 " + _collectedRecords.Count.ToString(CultureInfo.InvariantCulture) + " 条）：" + _pendingDatabasePath); logs.AddRange(audit.ToLogs()); ResetMigration(); ScheduleFinalClose();
            return true;
        }
        private void ScheduleOpenCurrentFile()
        {
            StopFileOpenTimer();
            _fileOpenTimer = new Timer { Interval = 100 };
            _fileOpenTimer.Tick += (sender, e) =>
            {
                Timer timer = _fileOpenTimer;
                _fileOpenTimer = null;
                timer?.Stop();
                timer?.Dispose();
                if (_paused || _state != MigrationState.OpeningCurrentFile) return;
                try
                {
                    OpenCurrentFile();
                    _state = MigrationState.AwaitingCurrentFileData;
                    _waitingAttempts = 0;
                    ResetDataStability();
                    _automationStatus = "已打开第 " + (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件；等待旧编号和 GUID 稳定。";
                    ScheduleNextCollection(1500);
                }
                catch (Exception ex)
                {
                    _automationStatus = "错误：" + ex.Message;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    ResetMigration();
                    ScheduleNextCollection(100);
                }
            };
            _fileOpenTimer.Start();
        }
        private void StopFileOpenTimer()
        {
            if (_fileOpenTimer == null) return;
            _fileOpenTimer.Stop();
            _fileOpenTimer.Dispose();
            _fileOpenTimer = null;
        }
        private void ScheduleFinalClose()
        {
            StopFinalCloseTimer();
            _finalCloseTimer = new Timer { Interval = 100 };
            _finalCloseTimer.Tick += (sender, e) =>
            {
                Timer timer = _finalCloseTimer;
                _finalCloseTimer = null;
                timer?.Stop();
                timer?.Dispose();
                CloseCurrentFile();
            };
            _finalCloseTimer.Start();
        }
        private void StopFinalCloseTimer()
        {
            if (_finalCloseTimer == null) return;
            _finalCloseTimer.Stop();
            _finalCloseTimer.Dispose();
            _finalCloseTimer = null;
        }
        public override void RemovedFromDocument(GH_Document document)
        {
            CancelPendingRefresh();
            StopFileOpenTimer();
            StopFinalCloseTimer();
            base.RemovedFromDocument(document);
        }
        public override bool Write(GH_IWriter writer) { foreach (V5Criterion c in _enabled.Keys) { writer.SetBoolean(SettingsChunk + c + "Enabled", _enabled[c]); writer.SetDouble(SettingsChunk + c + "Tolerance", _tolerances[c]); } foreach (V5Criterion c in _decimals.Keys) writer.SetInt32(SettingsChunk + c + "Decimals", _decimals[c]); writer.SetDouble(SettingsChunk + "Mirror", _mirrorTolerance); return base.Write(writer); }
        public override bool Read(GH_IReader reader) { foreach (V5Criterion c in _enabled.Keys.ToList()) { bool e = _enabled[c]; double t = _tolerances[c]; if (reader.TryGetBoolean(SettingsChunk + c + "Enabled", ref e) && c != V5Criterion.Length) _enabled[c] = e; if (reader.TryGetDouble(SettingsChunk + c + "Tolerance", ref t)) _tolerances[c] = Math.Abs(t); } _enabled[V5Criterion.Length] = true; foreach (V5Criterion c in _decimals.Keys.ToList()) { int d = _decimals[c]; if (reader.TryGetInt32(SettingsChunk + c + "Decimals", ref d)) _decimals[c] = Math.Max(0, Math.Min(12, d)); } double m = _mirrorTolerance; if (reader.TryGetDouble(SettingsChunk + "Mirror", ref m)) _mirrorTolerance = Math.Abs(m); return base.Read(reader); }
        public override Guid ComponentGuid => new Guid("B1D97B04-3A4A-4F3C-91F0-4AC1D59EE804");
    }

    internal sealed class EntityFeatureMigrationV5IntegratedAttributes : GH_ComponentAttributes
    {
        public EntityFeatureMigrationV5IntegratedAttributes(EntityFeatureMigrationV5Integrated owner) : base(owner) { }
        protected override void Layout() { base.Layout(); Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 44.0f); }
        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel); if (channel != GH_CanvasChannel.Objects) return;
            EntityFeatureMigrationV5Integrated component = (EntityFeatureMigrationV5Integrated)Owner;
            RectangleF pauseRect = new RectangleF(Bounds.X, Bounds.Bottom - 44.0f, Bounds.Width, 20.0f); pauseRect.Inflate(-5.0f, -2.0f);
            using (GH_Capsule pause = GH_Capsule.CreateCapsule(pauseRect, GH_Palette.Grey)) pause.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            using (Font pauseFont = new Font(GH_FontServer.Small, FontStyle.Bold)) using (StringFormat pauseFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) graphics.DrawString(component.IsPaused ? "继续" : "暂停", pauseFont, Brushes.White, pauseRect, pauseFormat);
            RectangleF rect = new RectangleF(Bounds.X, Bounds.Bottom - 22.0f, Bounds.Width, 20.0f); rect.Inflate(-5.0f, -2.0f);
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(rect, GH_Palette.Black)) capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold)) using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) graphics.DrawString("启动", font, Brushes.White, rect, format);
        }
        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF rect = new RectangleF(Bounds.X, Bounds.Bottom - 22.0f, Bounds.Width, 20.0f);
            RectangleF pauseRect = new RectangleF(Bounds.X, Bounds.Bottom - 44.0f, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && rect.Contains(e.CanvasLocation)) { ((EntityFeatureMigrationV5Integrated)Owner).StartMigration(); return GH_ObjectResponse.Handled; }
            if (e.Button == MouseButtons.Left && pauseRect.Contains(e.CanvasLocation))
            {
                EntityFeatureMigrationV5Integrated component = (EntityFeatureMigrationV5Integrated)Owner;
                if (component.IsPaused) component.ResumeMigration(); else component.PauseMigration();
                sender.Invalidate();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }
    }

    /// <summary>创建迁移专用 V8 SQLite 数据库；不会触及原实体分类数据库表。</summary>
    public sealed class CreateEntityFeatureMigrationDatabaseV5 : GH_Component
    {
        private bool _createRequested;
        private string _lastPath = string.Empty;

        public CreateEntityFeatureMigrationDatabaseV5()
            : base("创建分类迁移数据库（V8）", "建迁移库V8", "创建供“分类数据库迁移（V8）”使用的空 SQLite 数据库。", "Parrot", "Library") { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("文件夹", "文件夹", "新版数据库所在文件夹。", GH_ParamAccess.item);
            p.AddTextParameter("数据库名称", "名称", "数据库文件名；未填写扩展名时自动添加 .db。", GH_ParamAccess.item);
        }
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("新版数据库地址", "数据库", "创建后的 V5 数据库完整地址。", GH_ParamAccess.item);
            p.AddTextParameter("状态", "状态", "创建状态。", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            string folder = string.Empty, name = string.Empty;
            if (!da.GetData(0, ref folder) || !da.GetData(1, ref name)) return;
            if (!_createRequested) { da.SetData(0, _lastPath); da.SetData(1, "右键点击“创建新版数据库”后执行。"); return; }
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("文件夹和数据库名称不能为空。");
                string fileName = name.Trim();
                if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("数据库名称只能是文件名，不能包含路径。");
                if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName))) fileName += ".db";
                string path = Path.GetFullPath(Path.Combine(Path.GetFullPath(folder), fileName));
                if (File.Exists(path)) throw new InvalidOperationException("目标数据库已存在；请使用新名称，或在迁移电池中重建该库。");
                Directory.CreateDirectory(Path.GetDirectoryName(path)); SQLiteConnection.CreateFile(path); V6MigrationStore.CreateEmpty(path);
                _lastPath = path; da.SetData(0, path); da.SetData(1, "已创建 V8 分类迁移数据库。");
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); da.SetData(0, _lastPath); da.SetData(1, "错误：" + ex.Message); }
            finally { _createRequested = false; }
        }
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu); Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "创建新版数据库", (s, e) => { _createRequested = true; ExpireSolution(true); });
        }
        public override Guid ComponentGuid => new Guid("B1D97B03-3A4A-4F3C-91F0-4AC1D59EE803");
    }

    internal sealed class V5Hole
    {
        public double Perimeter; public double U; public double V; public double W;
        public V5Hole(double perimeter, double u, double v, double w) { Perimeter = perimeter; U = u; V = v; W = w; }
    }

    internal sealed class V5Feature
    {
        internal const string Header = "ENTITY_FEATURE_V5";
        public string Material = string.Empty; public string Color = string.Empty; public string Length = string.Empty;
        public double Area; public double Volume; public double EdgeSum; public double[] Inertia = new double[3]; public double MirrorScore; public double HoleMirrorScore;
        public List<V5Hole> Holes = new List<V5Hole>();

        public static V5Feature Create(string material, string color, string length, Brep brep, Dictionary<V5Criterion, int> decimals, int index)
        {
            if (brep == null) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 Brep 为空。");
            AreaMassProperties area = AreaMassProperties.Compute(brep, true, true, true, true);
            if (area == null) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 Brep 无法计算面积属性。");
            VolumeMassProperties volume = brep.IsSolid ? VolumeMassProperties.Compute(brep, true, true, true, true) : null;
            Point3d centroid; double[] inertia; Vector3d[] axes;
            if (!TryPrincipal(volume, out centroid, out inertia, out axes) && !TryPrincipal(area, out centroid, out inertia, out axes)) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 Brep 无法计算主惯量轴。");
            var samples = SamplePoints(brep); OrientAxes(samples, centroid, axes);
            var feature = new V5Feature { Material = material ?? string.Empty, Color = color ?? string.Empty, Length = NormalizeBoundingBox(length, decimals[V5Criterion.Length], index), Area = Round(area.Area, decimals[V5Criterion.Area]), Volume = Round(volume == null ? 0 : volume.Volume, decimals[V5Criterion.Volume]), Inertia = inertia.Select(x => Round(x, decimals[V5Criterion.Inertia])).ToArray() };
            feature.EdgeSum = Round(brep.Edges.Sum(edge => edge.GetLength()), decimals[V5Criterion.EdgeSum]);
            feature.MirrorScore = ComputeMirror(samples, centroid, axes);
            feature.Holes = ExtractHoles(brep, centroid, axes, decimals[V5Criterion.HolePosition], out double holeMirror);
            feature.HoleMirrorScore = holeMirror;
            return feature;
        }

        public string Serialize()
        {
            string holes = string.Join("/", Holes.Select(h => F(h.Perimeter) + "," + F(h.U) + "," + F(h.V) + "," + F(h.W)));
            return string.Join(";", new[] { Header, B(Material), B(Color), B(Length), F(Area), F(Volume), F(EdgeSum), F(Inertia[0]), F(Inertia[1]), F(Inertia[2]), F(MirrorScore), F(HoleMirrorScore), holes });
        }
        public static V5Feature Deserialize(string text)
        {
            string[] p = (text ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.None);
            if (p.Length != 13 || p[0] != Header) throw new ArgumentException("分类特征不是有效的 V5 格式。");
            var f = new V5Feature { Material = UB(p[1]), Color = UB(p[2]), Length = UB(p[3]), Area = D(p[4]), Volume = D(p[5]), EdgeSum = D(p[6]), Inertia = new[] { D(p[7]), D(p[8]), D(p[9]) }, MirrorScore = D(p[10]), HoleMirrorScore = D(p[11]) };
            if (!string.IsNullOrWhiteSpace(p[12])) foreach (string row in p[12].Split('/')) { string[] h = row.Split(','); if (h.Length != 4) throw new ArgumentException("V5 孔位数据格式无效。"); f.Holes.Add(new V5Hole(D(h[0]), D(h[1]), D(h[2]), D(h[3]))); }
            return f;
        }
        public string ToSummary() { return string.Format(CultureInfo.InvariantCulture, "材质={0}; 颜色={1}; 包围盒={2}; 惯量=({3:0.###},{4:0.###},{5:0.###}); 体积={6:0.###}; 孔位=数量={7}; 镜像值={8:0.######}; 孔镜像值={9:0.######}", Material, Color, string.IsNullOrWhiteSpace(Length) ? "" : "(" + Length + ")", Inertia[0], Inertia[1], Inertia[2], Volume, Holes.Count, MirrorScore, HoleMirrorScore); }
        public static string CriterionName(V5Criterion c) { switch (c) { case V5Criterion.Length: return "包围盒"; case V5Criterion.Inertia: return "转动惯量"; case V5Criterion.Area: return "表面积"; case V5Criterion.Volume: return "体积"; case V5Criterion.EdgeSum: return "孔周长"; case V5Criterion.HoleArea: return "孔面积"; case V5Criterion.HolePosition: return "孔位"; default: return "镜像区分"; } }
        private static string F(double x) { return x.ToString("R", CultureInfo.InvariantCulture); } private static double D(string x) { return double.Parse(x, CultureInfo.InvariantCulture); }
        private static string B(string x) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(x ?? string.Empty)); } private static string UB(string x) { return Encoding.UTF8.GetString(Convert.FromBase64String(x)); }
        private static double Round(double value, int decimals) { return Math.Round(value, Math.Max(0, Math.Min(12, decimals)), MidpointRounding.AwayFromZero); }
        internal static bool TryReadBoundingBox(string text, out double[] sizes)
        {
            sizes = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] values = text.Split(new[] { ',', '，', ';', '；', 'x', 'X', '×' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 1 && values.Length != 3) return false;
            var parsed = new List<double>();
            foreach (string value in values)
            {
                double size;
                if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out size) || double.IsNaN(size) || double.IsInfinity(size) || size <= 0) return false;
                parsed.Add(size);
            }
            sizes = parsed.OrderByDescending(x => x).ToArray();
            return true;
        }
        private static string NormalizeBoundingBox(string text, int decimals, int index)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            double[] sizes;
            if (!TryReadBoundingBox(text, out sizes)) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个包围盒必须输入 1 条最长边，或 3 条正数边长（格式：最长边,中边,短边）。");
            return string.Join(",", sizes.Select(x => F(Round(x, decimals))));
        }
        private static bool TryPrincipal(VolumeMassProperties props, out Point3d center, out double[] inertia, out Vector3d[] axes) { center = Point3d.Unset; inertia = null; axes = null; if (props == null) return false; center = props.Centroid; if (!props.CentroidCoordinatesPrincipalMomentsOfInertia(out double a, out Vector3d av, out double b, out Vector3d bv, out double c, out Vector3d cv)) return false; return Order(new[] { a, b, c }, new[] { av, bv, cv }, out inertia, out axes); }
        private static bool TryPrincipal(AreaMassProperties props, out Point3d center, out double[] inertia, out Vector3d[] axes) { center = Point3d.Unset; inertia = null; axes = null; if (props == null) return false; center = props.Centroid; if (!props.CentroidCoordinatesPrincipalMomentsOfInertia(out double a, out Vector3d av, out double b, out Vector3d bv, out double c, out Vector3d cv)) return false; return Order(new[] { a, b, c }, new[] { av, bv, cv }, out inertia, out axes); }
        private static bool Order(double[] values, Vector3d[] raw, out double[] inertia, out Vector3d[] axes) { int[] order = Enumerable.Range(0, 3).OrderByDescending(i => values[i]).ToArray(); inertia = order.Select(i => values[i]).ToArray(); axes = order.Select(i => { Vector3d x = raw[i]; return x.Unitize() ? x : Vector3d.Unset; }).ToArray(); return axes.All(x => x.IsValid); }
        private static List<Point3d> SamplePoints(Brep brep) { var p = brep.Vertices.Select(x => x.Location).ToList(); foreach (BrepEdge e in brep.Edges) { if (e == null) continue; p.Add(e.PointAtStart); p.Add(e.PointAtEnd); p.Add(e.PointAtNormalizedLength(.5)); } return p; }
        private static void OrientAxes(List<Point3d> points, Point3d center, Vector3d[] axes) { for (int i = 0; i < 3; i++) { double third = points.Sum(p => { double x = (p - center) * axes[i]; return x * x * x; }); if (third < 0) axes[i] = -axes[i]; } if (Vector3d.CrossProduct(axes[0], axes[1]) * axes[2] < 0) axes[2] = -axes[2]; }
        private static double ComputeMirror(List<Point3d> points, Point3d center, Vector3d[] axes) { double s = 0, scale = 0; foreach (Point3d p in points) { Vector3d q = p - center; double v = (q * axes[0]) * (q * axes[1]) * (q * axes[2]); s += v; scale += Math.Abs(v); } return scale <= RhinoMath.ZeroTolerance ? 0 : s / scale; }
        private static List<V5Hole> ExtractHoles(Brep brep, Point3d center, Vector3d[] axes, int decimals, out double holeMirror)
        {
            var result = new List<V5Hole>(); double signed = 0, scale = 0;
            foreach (BrepFace face in brep.Faces) foreach (BrepLoop loop in face.Loops) if (loop.LoopType == BrepLoopType.Inner)
            {
                var points = new List<Point3d>(); double perimeter = 0;
                foreach (BrepTrim trim in loop.Trims) { BrepEdge edge = trim.Edge; if (edge == null) continue; perimeter += edge.GetLength(); points.Add(edge.PointAtStart); points.Add(edge.PointAtEnd); points.Add(edge.PointAtNormalizedLength(.5)); }
                if (perimeter <= RhinoMath.ZeroTolerance || points.Count == 0) continue;
                Point3d c = new Point3d(points.Average(x => x.X), points.Average(x => x.Y), points.Average(x => x.Z)); Vector3d v = c - center;
                double u = v * axes[0], vv = v * axes[1], w = v * axes[2]; result.Add(new V5Hole(Round(perimeter, decimals), Round(u, decimals), Round(vv, decimals), Round(w, decimals)));
                signed += perimeter * vv; scale += perimeter * Math.Sqrt(vv * vv + w * w);
            }
            holeMirror = scale <= RhinoMath.ZeroTolerance ? 0 : Math.Max(-1, Math.Min(1, signed / scale));
            return result.OrderBy(x => x.Perimeter).ThenBy(x => x.U).ThenBy(x => x.V).ThenBy(x => x.W).ToList();
        }
    }

    internal sealed class V5MigrationRecord { public int Index; public string Number; public V5Feature Feature; public string SourceFile; public V5MigrationRecord(int index, string number, V5Feature feature, string sourceFile = "") { Index = index; Number = number; Feature = feature; SourceFile = sourceFile ?? string.Empty; } }

    internal sealed class V5MigrationAudit
    {
        public List<List<V5MigrationRecord>> Classes = new List<List<V5MigrationRecord>>(); public List<string> SamePartDifferentNumbers = new List<string>(); public List<string> DifferentPartsSameNumber = new List<string>();
        public static V5MigrationAudit Create(List<V5MigrationRecord> records, Dictionary<V5Criterion, bool> enabled, Dictionary<V5Criterion, double> tolerances, double mirrorTolerance)
        {
            // 容差匹配不是严格的传递关系；不能用 A≈B、B≈C 就把 A/C 强行合并。
            // 只有与同一类内全部记录均匹配，才放入该实际零件类。
            var audit = new V5MigrationAudit();
            foreach (V5MigrationRecord record in records)
            {
                List<V5MigrationRecord> target = audit.Classes.FirstOrDefault(group => group.All(existing => Matches(record.Feature, existing.Feature, enabled, tolerances, mirrorTolerance)));
                if (target == null) { target = new List<V5MigrationRecord>(); audit.Classes.Add(target); }
                target.Add(record);
            }
            foreach (var c in audit.Classes) { var numbers = c.Select(x => x.Number).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToList(); if (numbers.Count > 1) audit.SamePartDifferentNumbers.Add("实际相同、编号不同：" + string.Join("、", numbers) + "；输入序号：" + string.Join("、", c.Select(x => (x.Index + 1).ToString(CultureInfo.InvariantCulture)))); }
            foreach (var group in records.GroupBy(x => x.Number, StringComparer.Ordinal)) { int count = audit.Classes.Count(c => c.Any(x => x.Number == group.Key)); if (count > 1) audit.DifferentPartsSameNumber.Add("实际不同、编号相同：" + group.Key + "；分为 " + count.ToString(CultureInfo.InvariantCulture) + " 类；输入序号：" + string.Join("、", group.Select(x => (x.Index + 1).ToString(CultureInfo.InvariantCulture)))); }
            return audit;
        }
        public IEnumerable<string> ToLogs() { yield return "共识别 " + Classes.Count.ToString(CultureInfo.InvariantCulture) + " 个实际零件类。"; if (SamePartDifferentNumbers.Count == 0) yield return "未发现实际相同但编号不同。"; else foreach (string s in SamePartDifferentNumbers) yield return s; if (DifferentPartsSameNumber.Count == 0) yield return "未发现实际不同但编号相同。"; else foreach (string s in DifferentPartsSameNumber) yield return s; }
        private static bool Matches(V5Feature a, V5Feature b, Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t, double mirror)
        {
            if (!string.Equals(a.Material, b.Material, StringComparison.Ordinal) || !string.Equals(a.Color, b.Color, StringComparison.Ordinal)) return false;
            if (MirrorSide(a, mirror) != MirrorSide(b, mirror)) return false;
            if (e[V5Criterion.Length] && !BoundingBoxMatches(a.Length, b.Length, t[V5Criterion.Length])) return false;
            if (e[V5Criterion.Inertia] && !a.Inertia.Zip(b.Inertia, (x, y) => Percent(x, y, t[V5Criterion.Inertia])).All(x => x)) return false;
            if (e[V5Criterion.Area] && !Percent(a.Area, b.Area, t[V5Criterion.Area])) return false; if (e[V5Criterion.Volume] && !Percent(a.Volume, b.Volume, t[V5Criterion.Volume])) return false; if (e[V5Criterion.EdgeSum] && !Percent(a.EdgeSum, b.EdgeSum, t[V5Criterion.EdgeSum])) return false;
            return !e[V5Criterion.HolePosition] || HoleMatches(a.Holes, b.Holes, t[V5Criterion.HolePosition]);
        }
        private static bool Percent(double a, double b, double p) { double s = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-9); return Math.Abs(a - b) <= s * Math.Abs(p) / 100.0; }
        private static bool BoundingBoxMatches(string a, string b, double tolerance)
        {
            double[] aa, bb;
            if (!V5Feature.TryReadBoundingBox(a, out aa) || !V5Feature.TryReadBoundingBox(b, out bb)) return false;
            if (!Percent(aa[0], bb[0], tolerance)) return false;
            // 只提供最长边时，按最长边校核；双方均提供三边时才增加其余两边的约束。
            return aa.Length != 3 || bb.Length != 3 || (Percent(aa[1], bb[1], tolerance) && Percent(aa[2], bb[2], tolerance));
        }
        private static string MirrorSide(V5Feature f, double t) { double s = Math.Abs(f.HoleMirrorScore) > t ? f.HoleMirrorScore : f.MirrorScore; return Math.Abs(s) <= t ? string.Empty : (s >= 0 ? "A" : "B"); }
        private static bool HoleMatches(List<V5Hole> a, List<V5Hole> b, double tolerance)
        {
            if (a.Count != b.Count) return false;
            int n = a.Count;
            double[][] signs = { new[] { 1d, 1d, 1d }, new[] { -1d, -1d, 1d }, new[] { -1d, 1d, -1d }, new[] { 1d, -1d, -1d } };
            foreach (double[] sign in signs)
            {
                int[] rightToLeft = Enumerable.Repeat(-1, n).ToArray();
                bool complete = true;
                for (int left = 0; left < n; left++)
                {
                    bool[] visited = new bool[n];
                    if (!TryAssign(left, a, b, sign, tolerance, rightToLeft, visited)) { complete = false; break; }
                }
                if (complete) return true;
            }
            return false;
        }
        private static bool TryAssign(int left, List<V5Hole> a, List<V5Hole> b, double[] s, double t, int[] rightToLeft, bool[] visited)
        {
            for (int right = 0; right < b.Count; right++)
            {
                if (visited[right]) continue;
                double du = a[left].U - b[right].U * s[0], dv = a[left].V - b[right].V * s[1], dw = a[left].W - b[right].W * s[2];
                bool edge = Math.Abs(a[left].Perimeter - b[right].Perimeter) < t && Math.Sqrt(du * du + dv * dv + dw * dw) < t;
                if (!edge) continue;
                visited[right] = true;
                if (rightToLeft[right] < 0 || TryAssign(rightToLeft[right], a, b, s, t, rightToLeft, visited)) { rightToLeft[right] = left; return true; }
            }
            return false;
        }
    }

    internal static class V5MigrationStore
    {
        public static void EnsureSchema(string path)
        {
            using (var connection = new SQLiteConnection("Data Source=" + path + ";Version=3;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE IF NOT EXISTS entity_feature_migration_v5 (id INTEGER PRIMARY KEY AUTOINCREMENT, old_number TEXT NOT NULL, feature_text TEXT NOT NULL, source_index INTEGER NOT NULL, source_file TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP); CREATE INDEX IF NOT EXISTS idx_entity_feature_migration_v5_number ON entity_feature_migration_v5(old_number); CREATE TABLE IF NOT EXISTS entity_feature_migration_v5_audit (id INTEGER PRIMARY KEY AUTOINCREMENT, issue_type TEXT NOT NULL, detail TEXT NOT NULL);";
                    command.ExecuteNonQuery();
                }
            }
        }
        public static void Rebuild(string path, List<V5MigrationRecord> records, V5MigrationAudit audit)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("新版数据库地址不能为空。"); string full = Path.GetFullPath(path); string dir = Path.GetDirectoryName(full); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(full)) File.Copy(full, full + ".v5backup_" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), true);
            using (var connection = new SQLiteConnection("Data Source=" + full + ";Version=3;")) { connection.Open(); using (var command = connection.CreateCommand()) { command.CommandText = "DROP TABLE IF EXISTS entity_feature_migration_v5; DROP TABLE IF EXISTS entity_feature_migration_v5_audit; CREATE TABLE entity_feature_migration_v5 (id INTEGER PRIMARY KEY AUTOINCREMENT, old_number TEXT NOT NULL, feature_text TEXT NOT NULL, source_index INTEGER NOT NULL, source_file TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP); CREATE TABLE entity_feature_migration_v5_audit (id INTEGER PRIMARY KEY AUTOINCREMENT, issue_type TEXT NOT NULL, detail TEXT NOT NULL);"; command.ExecuteNonQuery(); using (var tx = connection.BeginTransaction()) { foreach (var r in records) { command.CommandText = "INSERT INTO entity_feature_migration_v5 (old_number, feature_text, source_index, source_file) VALUES (@n,@f,@i,@s);"; command.Parameters.Clear(); command.Parameters.AddWithValue("@n", r.Number); command.Parameters.AddWithValue("@f", r.Feature.Serialize()); command.Parameters.AddWithValue("@i", r.Index + 1); command.Parameters.AddWithValue("@s", r.SourceFile); command.ExecuteNonQuery(); } foreach (string x in audit.SamePartDifferentNumbers.Select(y => "同件异号|" + y).Concat(audit.DifferentPartsSameNumber.Select(y => "异件同号|" + y))) { string[] pair = x.Split(new[] { '|' }, 2); command.CommandText = "INSERT INTO entity_feature_migration_v5_audit (issue_type, detail) VALUES (@t,@d);"; command.Parameters.Clear(); command.Parameters.AddWithValue("@t", pair[0]); command.Parameters.AddWithValue("@d", pair[1]); command.ExecuteNonQuery(); } tx.Commit(); } } }
        }
        public static void Verify(string path, List<V5MigrationRecord> records)
        {
            string full = Path.GetFullPath(path);
            using (var connection = new SQLiteConnection("Data Source=" + full + ";Version=3;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT old_number, feature_text, source_index, source_file FROM entity_feature_migration_v5 ORDER BY source_index;";
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        int row = 0;
                        while (reader.Read())
                        {
                            if (row >= records.Count) throw new InvalidOperationException("数据库回读记录数多于待写入记录数。");
                            V5MigrationRecord expected = records[row];
                            string number = Convert.ToString(reader["old_number"], CultureInfo.InvariantCulture) ?? string.Empty;
                            string feature = Convert.ToString(reader["feature_text"], CultureInfo.InvariantCulture) ?? string.Empty;
                            int sourceIndex = Convert.ToInt32(reader["source_index"], CultureInfo.InvariantCulture);
                            string sourceFile = Convert.ToString(reader["source_file"], CultureInfo.InvariantCulture) ?? string.Empty;
                            if (!string.Equals(number, expected.Number, StringComparison.Ordinal) || !string.Equals(feature, expected.Feature.Serialize(), StringComparison.Ordinal) || sourceIndex != expected.Index + 1 || !string.Equals(Path.GetFullPath(sourceFile), Path.GetFullPath(expected.SourceFile), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("数据库回读校核失败，记录序号：" + (row + 1).ToString(CultureInfo.InvariantCulture));
                            row++;
                        }
                        if (row != records.Count) throw new InvalidOperationException("数据库回读记录数与待写入记录数不一致：" + row.ToString(CultureInfo.InvariantCulture) + "/" + records.Count.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
        }
    }

    /// <summary>V6 不使用插点采样或镜像分数；孔特征由闭合轮廓的面积质心、面积、周长和法向构成。</summary>
    internal sealed class V6Feature
    {
        internal const string Header = "ENTITY_FEATURE_V8";
        public string Material = string.Empty;
        public string Color = string.Empty;
        public string BoundingBox = string.Empty;
        public double Area;
        public double Volume;
        public double[] Inertia = new double[3];
        public Point3d Centroid = Point3d.Unset;
        public Point3d LocalOrigin = Point3d.Unset;
        public Vector3d[] Axes = new Vector3d[3];
        public List<V6Hole> Holes = new List<V6Hole>();
        // 所有平面外环的去共线顶点；与孔位共用包围盒中心局部主轴坐标。
        public List<V6OuterProfile> OuterProfiles = new List<V6OuterProfile>();
        public double MirrorScore;
        public double HoleMirrorScore;

        public static V6Feature Create(string material, string color, Brep brep, Dictionary<V5Criterion, int> decimals, int index)
        {
            if (brep == null) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 Brep 为空。");
            AreaMassProperties area = AreaMassProperties.Compute(brep, true, true, true, true);
            if (area == null) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 Brep 无法计算面积属性。");
            VolumeMassProperties volume = brep.IsSolid ? VolumeMassProperties.Compute(brep, true, true, true, true) : null;
            Point3d center; double[] inertia; Vector3d[] axes;
            if (!TryPrincipal(volume, out center, out inertia, out axes) && !TryPrincipal(area, out center, out inertia, out axes)) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 Brep 无法计算主惯量轴。");
            List<Point3d> mirrorPoints = BuildDeterministicMirrorPoints(brep);
            OrientAxesByMirrorPoints(mirrorPoints, center, axes);
            MakeRightHanded(axes);
            OrientedBox box = CalculateOrientedBoundingBox(brep, center, axes);
            var f = new V6Feature
            {
                Material = material ?? string.Empty,
                Color = color ?? string.Empty,
                BoundingBox = box.Sizes,
                Area = Round(area.Area, decimals[V5Criterion.Area]),
                Volume = Round(volume == null ? 0 : volume.Volume, decimals[V5Criterion.Volume]),
                Inertia = inertia.Select(x => Round(x, decimals[V5Criterion.Inertia])).ToArray(),
                Centroid = center,
                LocalOrigin = box.Center,
                Axes = axes.Select(x => x).ToArray()
            };
            f.Holes = ExtractHoles(brep, box.Center, axes, decimals, index);
            f.OuterProfiles = ExtractOuterProfiles(brep, box.Center, axes);
            f.MirrorScore = ComputeMirrorScore(mirrorPoints, center, axes);
            f.HoleMirrorScore = ComputeHoleMirrorScore(f.Holes);
            return f;
        }

        public string Serialize()
        {
            string holes = string.Join("/", Holes.Select(h => string.Join(",", new[] { F(h.Perimeter), F(h.Area), F(h.U), F(h.V), F(h.W), F(h.NU), F(h.NV), F(h.NW) })));
            string profiles = string.Join("|", OuterProfiles.Select(ProfileText));
            return string.Join(";", new[] { Header, B(Material), B(Color), B(BoundingBox), F(Area), F(Volume), F(Inertia[0]), F(Inertia[1]), F(Inertia[2]), holes, F(MirrorScore), F(HoleMirrorScore), profiles });
        }
        public string ToSummary()
        {
            return string.Format(CultureInfo.InvariantCulture, "材质={0}; 颜色={1}; 包围盒={2}; 惯量=({3:0.###},{4:0.###},{5:0.###}); 表面积={6:0.###}; 体积={7:0.###}; 孔位=数量={8}; 孔周长和={9:0.###}; 孔面积和={10:0.###}", Material, Color, string.IsNullOrWhiteSpace(BoundingBox) ? "" : "(" + BoundingBox + ")", Inertia[0], Inertia[1], Inertia[2], Area, Volume, Holes.Count, Holes.Sum(x => x.Perimeter), Holes.Sum(x => x.Area));
        }
        public IEnumerable<string> DiagnosticLines(string number, string sourceFile)
        {
            yield return "===== 实体诊断：" + number + " =====";
            yield return "来源文件=" + sourceFile;
            yield return "质量质心(世界坐标)=" + PointText(Centroid) + "；孔位/外轮廓局部原点(定向包围盒中心)=" + PointText(LocalOrigin);
            yield return "主轴U=" + VectorText(Axes[0]) + "；主轴V=" + VectorText(Axes[1]) + "；主轴W=" + VectorText(Axes[2]);
            yield return "惯量=" + string.Join(",", Inertia.Select(F)) + "；表面积=" + F(Area) + "；体积=" + F(Volume) + "；包围盒=" + BoundingBox;
            yield return "实际提取孔数量=" + Holes.Count.ToString(CultureInfo.InvariantCulture);
            yield return "外轮廓：可用平面外环=" + OuterProfiles.Count.ToString(CultureInfo.InvariantCulture) + "；镜像值=" + F(MirrorScore) + "；孔镜像值=" + F(HoleMirrorScore);
            for (int i = 0; i < Holes.Count; i++)
            {
                V6Hole h = Holes[i];
                yield return "孔[" + i.ToString(CultureInfo.InvariantCulture) + "] 模式=" + h.CenterMode + "；世界坐标=" + PointText(new Point3d(h.X, h.Y, h.Z)) + "；局部坐标=(" + F(h.U) + "," + F(h.V) + "," + F(h.W) + ")；周长=" + F(h.Perimeter) + "；面积=" + F(h.Area) + "；局部法向=(" + F(h.NU) + "," + F(h.NV) + "," + F(h.NW) + ")";
            }
        }
        private static string PointText(Point3d p) { return "(" + F(p.X) + "," + F(p.Y) + "," + F(p.Z) + ")"; }
        private static string VectorText(Vector3d v) { return "(" + F(v.X) + "," + F(v.Y) + "," + F(v.Z) + ")"; }
        private static bool TryPrincipal(VolumeMassProperties p, out Point3d center, out double[] inertia, out Vector3d[] axes)
        {
            center = Point3d.Unset; inertia = null; axes = null;
            if (p == null) return false;
            center = p.Centroid;
            double a, b, c; Vector3d av, bv, cv;
            if (!p.CentroidCoordinatesPrincipalMomentsOfInertia(out a, out av, out b, out bv, out c, out cv)) return false;
            return Order(new[] { a, b, c }, new[] { av, bv, cv }, out inertia, out axes);
        }
        private static bool TryPrincipal(AreaMassProperties p, out Point3d center, out double[] inertia, out Vector3d[] axes)
        {
            center = Point3d.Unset; inertia = null; axes = null;
            if (p == null) return false;
            center = p.Centroid;
            double a, b, c; Vector3d av, bv, cv;
            if (!p.CentroidCoordinatesPrincipalMomentsOfInertia(out a, out av, out b, out bv, out c, out cv)) return false;
            return Order(new[] { a, b, c }, new[] { av, bv, cv }, out inertia, out axes);
        }
        private static bool Order(double[] values, Vector3d[] raw, out double[] inertia, out Vector3d[] axes)
        {
            int[] order = Enumerable.Range(0, 3).OrderByDescending(i => values[i]).ToArray();
            inertia = order.Select(i => values[i]).ToArray();
            axes = order.Select(i => { Vector3d x = raw[i]; return x.Unitize() ? x : Vector3d.Unset; }).ToArray();
            return axes.All(x => x.IsValid);
        }
        private static void MakeRightHanded(Vector3d[] axes) { if (Vector3d.CrossProduct(axes[0], axes[1]) * axes[2] < 0) axes[2] = -axes[2]; }
        private static List<V6Hole> ExtractHoles(Brep brep, Point3d center, Vector3d[] axes, Dictionary<V5Criterion, int> decimals, int index)
        {
            var holes = new List<V6Hole>();
            foreach (BrepFace face in brep.Faces)
            foreach (BrepLoop loop in face.Loops)
            {
                if (loop.LoopType != BrepLoopType.Inner) continue;
                LoopBoundary boundary = ReadLoopBoundary(loop);
                if (boundary.Perimeter <= RhinoMath.ZeroTolerance) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个实体存在长度为零的孔轮廓。");
                Point3d holeCenter; double holeArea = 0; Vector3d normal = Vector3d.Zero; string centerMode;
                Plane plane; AreaMassProperties props = boundary.ClosedCurve == null ? null : AreaMassProperties.Compute(boundary.ClosedCurve);
                if (props != null && props.Area > RhinoMath.ZeroTolerance && boundary.ClosedCurve.TryGetPlane(out plane))
                {
                    holeCenter = props.Centroid; holeArea = props.Area; normal = plane.Normal; normal.Unitize(); centerMode = "平面面积质心";
                }
                else
                {
                    // 非平面孔环没有唯一的面积质心；使用固定弧长位置的长度质心，绝不随机取点。
                    holeCenter = DeterministicLengthCentroid(boundary.Curves, boundary.Perimeter); centerMode = "确定性长度质心";
                }
                Vector3d position = holeCenter - center;
                holes.Add(new V6Hole(
                    Round(boundary.Perimeter, decimals[V5Criterion.EdgeSum]), Round(holeArea, decimals[V5Criterion.HoleArea]),
                    Round(position * axes[0], decimals[V5Criterion.HolePosition]), Round(position * axes[1], decimals[V5Criterion.HolePosition]), Round(position * axes[2], decimals[V5Criterion.HolePosition]),
                    Round(normal * axes[0], 6), Round(normal * axes[1], 6), Round(normal * axes[2], 6), holeCenter.X, holeCenter.Y, holeCenter.Z, centerMode));
            }
            return holes.OrderBy(x => x.Area).ThenBy(x => x.Perimeter).ThenBy(x => x.U).ThenBy(x => x.V).ThenBy(x => x.W).ToList();
        }
        private static OrientedBox CalculateOrientedBoundingBox(Brep brep, Point3d center, Vector3d[] axes)
        {
            Plane plane = new Plane(center, axes[0], axes[1]);
            BoundingBox box = brep.GetBoundingBox(plane);
            if (!box.IsValid) throw new ArgumentException("实体无法计算主轴定向包围盒。");
            double[] sizes = { Math.Max(0, box.Max.X - box.Min.X), Math.Max(0, box.Max.Y - box.Min.Y), Math.Max(0, box.Max.Z - box.Min.Z) };
            Point3d localCenter = new Point3d((box.Min.X + box.Max.X) * .5, (box.Min.Y + box.Max.Y) * .5, (box.Min.Z + box.Max.Z) * .5);
            return new OrientedBox { Center = plane.PointAt(localCenter.X, localCenter.Y, localCenter.Z), Sizes = string.Join(",", sizes.OrderByDescending(x => x).Select(F)) };
        }
        private static List<V6OuterProfile> ExtractOuterProfiles(Brep brep, Point3d center, Vector3d[] axes)
        {
            var profiles = new List<V6OuterProfile>();
            foreach (BrepFace face in brep.Faces)
            foreach (BrepLoop loop in face.Loops)
            {
                if (loop.LoopType != BrepLoopType.Outer) continue;
                LoopBoundary boundary = ReadLoopBoundary(loop);
                AreaMassProperties props = boundary.ClosedCurve == null ? null : AreaMassProperties.Compute(boundary.ClosedCurve);
                Plane ignored;
                if (props == null || props.Area <= RhinoMath.ZeroTolerance || !boundary.ClosedCurve.TryGetPlane(out ignored)) continue;
                var vertices = new List<Point3d>();
                foreach (Curve curve in boundary.Curves)
                {
                    if (vertices.Count == 0 || vertices[vertices.Count - 1].DistanceTo(curve.PointAtStart) > .001) vertices.Add(curve.PointAtStart);
                    if (vertices.Count == 0 || vertices[vertices.Count - 1].DistanceTo(curve.PointAtEnd) > .001) vertices.Add(curve.PointAtEnd);
                }
                if (vertices.Count > 1 && vertices[0].DistanceTo(vertices[vertices.Count - 1]) <= .001) vertices.RemoveAt(vertices.Count - 1);
                RemoveCollinearVertices(vertices, .01);
                if (vertices.Count < 3) continue;
                Vector3d normal = ignored.Normal; normal.Unitize();
                profiles.Add(new V6OuterProfile
                {
                    Area = props.Area,
                    Perimeter = boundary.Perimeter,
                    NU = normal * axes[0], NV = normal * axes[1], NW = normal * axes[2],
                    Vertices = vertices.Select(p => new V6OuterVertex((p - center) * axes[0], (p - center) * axes[1], (p - center) * axes[2])).ToList()
                });
            }
            return profiles.OrderBy(x => x.Area).ThenBy(x => x.Perimeter).ThenBy(x => x.Vertices.Count).ToList();
        }
        private static List<Point3d> BuildDeterministicMirrorPoints(Brep brep)
        {
            var points = new List<Point3d>();
            foreach (BrepVertex vertex in brep.Vertices) points.Add(vertex.Location);
            foreach (BrepEdge edge in brep.Edges)
            {
                double length = edge.GetLength();
                if (length <= RhinoMath.ZeroTolerance) continue;
                for (int i = 1; i < 8; i++) points.Add(edge.PointAtNormalizedLength(i / 8.0));
            }
            return points;
        }
        private static void OrientAxesByMirrorPoints(List<Point3d> points, Point3d center, Vector3d[] axes)
        {
            for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
            {
                double thirdMoment = 0;
                foreach (Point3d point in points) { Vector3d v = point - center; double coordinate = v * axes[axisIndex]; thirdMoment += coordinate * coordinate * coordinate; }
                if (thirdMoment < 0) axes[axisIndex] = -axes[axisIndex];
            }
        }
        private static double ComputeMirrorScore(List<Point3d> points, Point3d center, Vector3d[] axes)
        {
            double score = 0, scale = 0;
            foreach (Point3d point in points) { Vector3d v = point - center; double product = (v * axes[0]) * (v * axes[1]) * (v * axes[2]); score += product; scale += Math.Abs(product); }
            return scale <= RhinoMath.ZeroTolerance ? 0 : score / scale;
        }
        private static double ComputeHoleMirrorScore(List<V6Hole> holes)
        {
            double signedV = 0, signedW = 0, signedUv = 0, signedUw = 0, scale = 0, maxU = 0;
            foreach (V6Hole h in holes) { double p = h.Perimeter; signedV += p * h.V; signedW += p * h.W; signedUv += p * h.U * h.V; signedUw += p * h.U * h.W; scale += p * Math.Sqrt(h.V * h.V + h.W * h.W); maxU = Math.Max(maxU, Math.Abs(h.U)); }
            if (scale <= RhinoMath.ZeroTolerance) return 0;
            double[] candidates = { signedV / scale, signedW / scale, maxU <= RhinoMath.ZeroTolerance ? 0 : signedUv / (scale * maxU), maxU <= RhinoMath.ZeroTolerance ? 0 : signedUw / (scale * maxU) };
            return candidates.OrderByDescending(Math.Abs).First();
        }
        private static void RemoveCollinearVertices(List<Point3d> vertices, double tolerance)
        {
            bool removed = true;
            while (removed && vertices.Count >= 3)
            {
                removed = false;
                for (int i = 0; i < vertices.Count; i++)
                {
                    Point3d a = vertices[(i + vertices.Count - 1) % vertices.Count], b = vertices[i], c = vertices[(i + 1) % vertices.Count];
                    Vector3d ab = b - a, ac = c - a;
                    if (ab.Length <= tolerance || ac.Length <= tolerance || Vector3d.CrossProduct(ab, ac).Length / ac.Length <= tolerance)
                    { vertices.RemoveAt(i); removed = true; break; }
                }
            }
        }
        private static LoopBoundary ReadLoopBoundary(BrepLoop loop)
        {
            var edges = new List<Curve>();
            foreach (BrepTrim trim in loop.Trims)
            {
                if (trim.Edge == null) continue;
                Curve edge = trim.Edge.DuplicateCurve();
                if (trim.IsReversed()) edge.Reverse();
                edges.Add(edge);
            }
            if (edges.Count == 0) throw new ArgumentException("孔轮廓不包含有效边。");
            double tolerance = RhinoDoc.ActiveDoc == null ? .001 : RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            Curve[] joined = Curve.JoinCurves(edges, tolerance);
            Curve closed = joined != null && joined.Length == 1 && joined[0].IsClosed ? joined[0] : null;
            return new LoopBoundary(edges, closed);
        }
        private static Point3d DeterministicLengthCentroid(List<Curve> curves, double totalLength)
        {
            const int sampleCount = 128;
            var lengths = curves.Select(x => x.GetLength()).ToArray();
            double sumX = 0, sumY = 0, sumZ = 0;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                double target = totalLength * (sample + .5) / sampleCount, accumulated = 0;
                int curveIndex = lengths.Length - 1;
                for (int i = 0; i < lengths.Length; i++) { if (target <= accumulated + lengths[i]) { curveIndex = i; break; } accumulated += lengths[i]; }
                double local = lengths[curveIndex] <= RhinoMath.ZeroTolerance ? .5 : (target - accumulated) / lengths[curveIndex];
                local = Math.Max(0, Math.Min(1, local)); Point3d p = curves[curveIndex].PointAtNormalizedLength(local);
                sumX += p.X; sumY += p.Y; sumZ += p.Z;
            }
            return new Point3d(sumX / sampleCount, sumY / sampleCount, sumZ / sampleCount);
        }
        private static double Round(double x, int d) { return Math.Round(x, Math.Max(0, Math.Min(12, d)), MidpointRounding.AwayFromZero); }
        private static string ProfileText(V6OuterProfile p) { return F(p.Area) + "," + F(p.Perimeter) + "," + F(p.NU) + "," + F(p.NV) + "," + F(p.NW) + ":" + string.Join("/", p.Vertices.Select(v => F(v.U) + "," + F(v.V) + "," + F(v.W))); }
        private static string F(double x) { return x.ToString("R", CultureInfo.InvariantCulture); }
        private static string B(string x) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(x ?? string.Empty)); }
    }

    internal sealed class OrientedBox { public Point3d Center; public string Sizes; }

    internal sealed class LoopBoundary
    {
        public List<Curve> Curves; public Curve ClosedCurve; public double Perimeter;
        public LoopBoundary(List<Curve> curves, Curve closedCurve) { Curves = curves; ClosedCurve = closedCurve; Perimeter = curves.Sum(x => x.GetLength()); }
    }

    internal sealed class V6Hole
    {
        public double Perimeter; public double Area; public double U; public double V; public double W; public double NU; public double NV; public double NW;
        public double X; public double Y; public double Z; public string CenterMode;
        public V6Hole(double perimeter, double area, double u, double v, double w, double nu, double nv, double nw, double x = 0, double y = 0, double z = 0, string centerMode = "") { Perimeter = perimeter; Area = area; U = u; V = v; W = w; NU = nu; NV = nv; NW = nw; X = x; Y = y; Z = z; CenterMode = centerMode ?? string.Empty; }
    }

    internal sealed class V6OuterVertex
    {
        public double U; public double V; public double W;
        public V6OuterVertex(double u, double v, double w) { U = u; V = v; W = w; }
    }

    internal sealed class V6OuterProfile
    {
        public double Area; public double Perimeter; public double NU; public double NV; public double NW;
        public List<V6OuterVertex> Vertices = new List<V6OuterVertex>();
        public bool IsAvailable => Area > RhinoMath.ZeroTolerance && Perimeter > RhinoMath.ZeroTolerance && Vertices.Count >= 3;
    }

    internal sealed class V6MigrationRecord
    {
        public int Index; public string Number; public V6Feature Feature; public string SourceFile;
        public V6MigrationRecord(int index, string number, V6Feature feature, string sourceFile) { Index = index; Number = number; Feature = feature; SourceFile = sourceFile ?? string.Empty; }
    }

    internal sealed class V6MigrationAudit
    {
        public List<List<V6MigrationRecord>> Classes = new List<List<V6MigrationRecord>>();
        public List<string> SamePartDifferentNumbers = new List<string>();
        public List<string> DifferentPartsSameNumber = new List<string>();
        public List<string> MirrorParts = new List<string>();
        public List<string> ExcludedNumberConflicts = new List<string>();
        private Dictionary<V5Criterion, bool> _diagnosticEnabled;
        private Dictionary<V5Criterion, double> _diagnosticTolerances;
        public static V6MigrationAudit Create(List<V6MigrationRecord> records, Dictionary<V5Criterion, bool> enabled, Dictionary<V5Criterion, double> tolerances)
        {
            var audit = new V6MigrationAudit { _diagnosticEnabled = enabled, _diagnosticTolerances = tolerances };
            foreach (V6MigrationRecord record in records)
            {
                List<V6MigrationRecord> target = audit.Classes.FirstOrDefault(group => group.All(x => Matches(record.Feature, x.Feature, enabled, tolerances)));
                if (target == null) { target = new List<V6MigrationRecord>(); audit.Classes.Add(target); }
                target.Add(record);
            }
            foreach (var group in audit.Classes)
            {
                var numbers = group.Select(x => x.Number).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                if (numbers.Count > 1) audit.SamePartDifferentNumbers.Add("实际相同、编号不同：" + string.Join("、", numbers) + "；输入序号：" + string.Join("、", group.Select(x => (x.Index + 1).ToString(CultureInfo.InvariantCulture))));
            }
            foreach (var group in records.GroupBy(x => x.Number, StringComparer.OrdinalIgnoreCase))
            {
                int classCount = audit.Classes.Count(c => c.Any(x => string.Equals(x.Number, group.Key, StringComparison.OrdinalIgnoreCase)));
                if (classCount > 1) audit.DifferentPartsSameNumber.Add("实际不同、编号相同：" + group.Key + "；分为 " + classCount.ToString(CultureInfo.InvariantCulture) + " 类；输入序号：" + string.Join("、", group.Select(x => (x.Index + 1).ToString(CultureInfo.InvariantCulture))));
            }
            for (int left = 0; left < audit.Classes.Count; left++)
            for (int right = left + 1; right < audit.Classes.Count; right++)
            {
                V6MigrationRecord a = audit.Classes[left].OrderBy(x => x.Index).First();
                V6MigrationRecord b = audit.Classes[right].OrderBy(x => x.Index).First();
                if (MirrorMatches(a.Feature, b.Feature, enabled, tolerances)) audit.MirrorParts.Add("镜像零件：" + a.Number + "（序号 " + (a.Index + 1).ToString(CultureInfo.InvariantCulture) + "）与 " + b.Number + "（序号 " + (b.Index + 1).ToString(CultureInfo.InvariantCulture) + "）。");
            }
            return audit;
        }
        public List<V6MigrationRecord> RecordsToInsert()
        {
            var result = new List<V6MigrationRecord>();
            foreach (V6MigrationRecord r in Classes.Select(x => x.OrderBy(y => y.Index).First()).OrderBy(x => x.Index))
            {
                V6MigrationRecord kept = result.FirstOrDefault(x => string.Equals(x.Number, r.Number, StringComparison.OrdinalIgnoreCase));
                if (kept == null) result.Add(r);
                else ExcludedNumberConflicts.Add("实际不同、编号相同，未写入正式库：" + r.Number + "；保留来源=" + kept.SourceFile + "；隔离来源=" + r.SourceFile);
            }
            return result;
        }
        public IEnumerable<string> ToLogs()
        {
            yield return "共识别 " + Classes.Count.ToString(CultureInfo.InvariantCulture) + " 个实际零件类。";
            if (SamePartDifferentNumbers.Count == 0) yield return "未发现实际相同但编号不同。"; else foreach (string x in SamePartDifferentNumbers) yield return x;
            if (DifferentPartsSameNumber.Count == 0) yield return "未发现实际不同但编号相同。"; else foreach (string x in DifferentPartsSameNumber) yield return x;
            foreach (string x in MirrorParts) yield return x;
            foreach (string x in ExcludedNumberConflicts) yield return x;
        }
        public IEnumerable<string> DiagnosticLines()
        {
            if (_diagnosticEnabled == null || _diagnosticTolerances == null) yield break;
            foreach (List<V6MigrationRecord> group in Classes.Where(x => x.Count > 1))
            for (int left = 0; left < group.Count; left++)
            for (int right = left + 1; right < group.Count; right++)
            {
                V6MigrationRecord a = group[left], b = group[right];
                yield return "===== 同件判定诊断：" + a.Number + " ↔ " + b.Number + " =====";
                yield return "质心A=" + PointText(a.Feature.Centroid) + "；质心B=" + PointText(b.Feature.Centroid) + "；世界质心距离=" + F(a.Feature.Centroid.DistanceTo(b.Feature.Centroid));
                yield return "孔数量A/B=" + a.Feature.Holes.Count.ToString(CultureInfo.InvariantCulture) + "/" + b.Feature.Holes.Count.ToString(CultureInfo.InvariantCulture);
                double[][] signs = { new[] { 1d, 1d, 1d }, new[] { -1d, -1d, 1d }, new[] { -1d, 1d, -1d }, new[] { 1d, -1d, -1d } };
                double[] bestSign = signs[0]; double bestMaximum = double.PositiveInfinity; double bestAverage = double.PositiveInfinity;
                foreach (double[] sign in signs)
                {
                    var minima = a.Feature.Holes.Select(h => b.Feature.Holes.Count == 0 ? double.PositiveInfinity : b.Feature.Holes.Min(x => Distance(h, x, sign))).ToList();
                    double maximum = minima.Count == 0 ? 0 : minima.Max(), average = minima.Count == 0 ? 0 : minima.Average();
                    bool assignment = PerfectAssignment(a.Feature.Holes, b.Feature.Holes, sign, _diagnosticEnabled, _diagnosticTolerances);
                    yield return "正规变换(" + string.Join(",", sign.Select(F)) + ")：逐孔最近距离 最大=" + F(maximum) + "，平均=" + F(average) + "，完整一对一匹配=" + assignment.ToString();
                    if (maximum < bestMaximum || (Math.Abs(maximum - bestMaximum) <= 1e-12 && average < bestAverage)) { bestMaximum = maximum; bestAverage = average; bestSign = sign; }
                }
                yield return "最近距离最小的变换=(" + string.Join(",", bestSign.Select(F)) + ")";
                for (int hole = 0; hole < a.Feature.Holes.Count; hole++)
                {
                    if (b.Feature.Holes.Count == 0) { yield return "A孔[" + hole.ToString(CultureInfo.InvariantCulture) + "]：B 中没有孔。"; continue; }
                    int target = Enumerable.Range(0, b.Feature.Holes.Count).OrderBy(x => Distance(a.Feature.Holes[hole], b.Feature.Holes[x], bestSign)).First();
                    yield return "A孔[" + hole.ToString(CultureInfo.InvariantCulture) + "] → B孔[" + target.ToString(CultureInfo.InvariantCulture) + "]：合成距离=" + F(Distance(a.Feature.Holes[hole], b.Feature.Holes[target], bestSign)) + "；A局部=" + HolePoint(a.Feature.Holes[hole], new[] { 1d, 1d, 1d }) + "；B变换后=" + HolePoint(b.Feature.Holes[target], bestSign);
                }
            }
        }
        private static double Distance(V6Hole a, V6Hole b, double[] sign) { double du = a.U - b.U * sign[0], dv = a.V - b.V * sign[1], dw = a.W - b.W * sign[2]; return Math.Sqrt(du * du + dv * dv + dw * dw); }
        private static string HolePoint(V6Hole h, double[] sign) { return "(" + F(h.U * sign[0]) + "," + F(h.V * sign[1]) + "," + F(h.W * sign[2]) + ")"; }
        private static string PointText(Point3d p) { return "(" + F(p.X) + "," + F(p.Y) + "," + F(p.Z) + ")"; }
        private static string F(double x) { return x.ToString("0.######", CultureInfo.InvariantCulture); }
        public IEnumerable<KeyValuePair<string, string>> AuditRows()
        {
            foreach (string x in SamePartDifferentNumbers) yield return new KeyValuePair<string, string>("同件异号", x);
            foreach (string x in DifferentPartsSameNumber) yield return new KeyValuePair<string, string>("异件同号", x);
            foreach (string x in MirrorParts) yield return new KeyValuePair<string, string>("镜像零件", x);
            foreach (string x in ExcludedNumberConflicts) yield return new KeyValuePair<string, string>("隔离异件同号", x);
        }
        private static bool Matches(V6Feature a, V6Feature b, Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t)
        {
            return GlobalMatches(a, b, e, t) && SameMirrorCode(a, b) && GeometryMatches(a, b, e, t, false);
        }
        private static bool MirrorMatches(V6Feature a, V6Feature b, Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t) { return GlobalMatches(a, b, e, t) && !SameMirrorCode(a, b) && GeometryMatches(a, b, e, t, true); }
        private static bool GlobalMatches(V6Feature a, V6Feature b, Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t)
        {
            if (!SameOptionalText(a.Material, b.Material) || !SameOptionalText(a.Color, b.Color)) return false;
            if (!BoundingBoxMatches(a.BoundingBox, b.BoundingBox, t[V5Criterion.Length])) return false;
            if (e[V5Criterion.Inertia] && !a.Inertia.Zip(b.Inertia, (x, y) => Percent(x, y, t[V5Criterion.Inertia])).All(x => x)) return false;
            return (!e[V5Criterion.Area] || Percent(a.Area, b.Area, t[V5Criterion.Area])) && (!e[V5Criterion.Volume] || Percent(a.Volume, b.Volume, t[V5Criterion.Volume]));
        }
        private static bool SameOptionalText(string a, string b) { return string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || string.Equals(a, b, StringComparison.Ordinal); }
        private static bool SameMirrorCode(V6Feature a, V6Feature b) { return string.Equals(MirrorCode(a), MirrorCode(b), StringComparison.Ordinal); }
        private static string MirrorCode(V6Feature f) { double value = Math.Abs(f.HoleMirrorScore) > .001 ? f.HoleMirrorScore : f.MirrorScore; return Math.Abs(value) <= .001 ? string.Empty : (value >= 0 ? "A" : "B"); }
        private static bool BoundingBoxMatches(string a, string b, double tolerance)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return true;
            double[] aa, bb;
            if (!V5Feature.TryReadBoundingBox(a, out aa) || !V5Feature.TryReadBoundingBox(b, out bb) || !Percent(aa[0], bb[0], tolerance)) return false;
            return aa.Length != 3 || bb.Length != 3 || (Percent(aa[1], bb[1], tolerance) && Percent(aa[2], bb[2], tolerance));
        }
        private static bool Percent(double a, double b, double p) { return Math.Abs(a - b) <= Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-9) * Math.Abs(p) / 100.0; }
        private static bool GeometryMatches(V6Feature a, V6Feature b, Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t, bool reflection)
        {
            if (a.Holes.Count != b.Holes.Count) return false;
            double[][] transforms = reflection ? new[] { new[] { -1d, 1d, 1d }, new[] { 1d, -1d, 1d }, new[] { 1d, 1d, -1d }, new[] { -1d, -1d, -1d } } : new[] { new[] { 1d, 1d, 1d }, new[] { -1d, -1d, 1d }, new[] { -1d, 1d, -1d }, new[] { 1d, -1d, -1d } };
            return transforms.Any(sign => (!e[V5Criterion.HolePosition] || PerfectAssignment(a.Holes, b.Holes, sign, e, t)) && OuterProfilesMatch(a.OuterProfiles, b.OuterProfiles, sign, t[V5Criterion.HolePosition]));
        }
        private static bool OuterProfilesMatch(List<V6OuterProfile> a, List<V6OuterProfile> b, double[] sign, double tolerance)
        {
            if (a.Count == 0 || b.Count == 0) return true;
            if (a.Count != b.Count) return false;
            int[] assigned = Enumerable.Repeat(-1, b.Count).ToArray();
            for (int left = 0; left < a.Count; left++) if (!AssignProfile(left, a, b, sign, tolerance, assigned, new bool[b.Count])) return false;
            return true;
        }
        private static bool AssignProfile(int left, List<V6OuterProfile> a, List<V6OuterProfile> b, double[] sign, double tolerance, int[] assigned, bool[] visited)
        {
            for (int right = 0; right < b.Count; right++)
            {
                if (visited[right] || !OuterProfileMatches(a[left], b[right], sign, tolerance)) continue;
                visited[right] = true;
                if (assigned[right] < 0 || AssignProfile(assigned[right], a, b, sign, tolerance, assigned, visited)) { assigned[right] = left; return true; }
            }
            return false;
        }
        private static bool OuterProfileMatches(V6OuterProfile a, V6OuterProfile b, double[] sign, double tolerance)
        {
            if (!a.IsAvailable || !b.IsAvailable) return false;
            if (a.Vertices.Count != b.Vertices.Count) return false;
            if (Math.Abs(a.Area - b.Area) > Math.Max(Math.Max(a.Area, b.Area), 1) * .0001 || Math.Abs(a.Perimeter - b.Perimeter) > Math.Max(tolerance, .01)) return false;
            if (Math.Abs(a.NU * b.NU * sign[0] + a.NV * b.NV * sign[1] + a.NW * b.NW * sign[2]) < .9986) return false;
            int[] assigned = Enumerable.Repeat(-1, b.Vertices.Count).ToArray();
            for (int left = 0; left < a.Vertices.Count; left++) if (!AssignOuter(left, a.Vertices, b.Vertices, sign, Math.Max(tolerance, .01), assigned, new bool[b.Vertices.Count])) return false;
            return true;
        }
        private static bool AssignOuter(int left, List<V6OuterVertex> a, List<V6OuterVertex> b, double[] sign, double tolerance, int[] assigned, bool[] visited)
        {
            for (int right = 0; right < b.Count; right++)
            {
                if (visited[right] || !OuterCompatible(a[left], b[right], sign, tolerance)) continue;
                visited[right] = true;
                if (assigned[right] < 0 || AssignOuter(assigned[right], a, b, sign, tolerance, assigned, visited)) { assigned[right] = left; return true; }
            }
            return false;
        }
        private static bool OuterCompatible(V6OuterVertex a, V6OuterVertex b, double[] s, double tolerance)
        {
            double du = a.U - b.U * s[0], dv = a.V - b.V * s[1], dw = a.W - b.W * s[2];
            return Math.Sqrt(du * du + dv * dv + dw * dw) < tolerance;
        }
        private static bool PerfectAssignment(List<V6Hole> a, List<V6Hole> b, double[] s, Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t)
        {
            int[] assigned = Enumerable.Repeat(-1, b.Count).ToArray();
            for (int left = 0; left < a.Count; left++) if (!Assign(left, a, b, s, e, t, assigned, new bool[b.Count])) return false;
            return true;
        }
        private static bool Assign(int left, List<V6Hole> a, List<V6Hole> b, double[] s, Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t, int[] assigned, bool[] visited)
        {
            for (int right = 0; right < b.Count; right++)
            {
                if (visited[right] || !HoleCompatible(a[left], b[right], s, e, t)) continue;
                visited[right] = true;
                if (assigned[right] < 0 || Assign(assigned[right], a, b, s, e, t, assigned, visited)) { assigned[right] = left; return true; }
            }
            return false;
        }
        private static bool HoleCompatible(V6Hole a, V6Hole b, double[] s, Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t)
        {
            if (e[V5Criterion.EdgeSum] && !Percent(a.Perimeter, b.Perimeter, t[V5Criterion.EdgeSum])) return false;
            if (e[V5Criterion.HoleArea] && !Percent(a.Area, b.Area, t[V5Criterion.HoleArea])) return false;
            double du = a.U - b.U * s[0], dv = a.V - b.V * s[1], dw = a.W - b.W * s[2];
            if (Math.Sqrt(du * du + dv * dv + dw * dw) >= t[V5Criterion.HolePosition]) return false;
            double aNormalLength = Math.Sqrt(a.NU * a.NU + a.NV * a.NV + a.NW * a.NW), bNormalLength = Math.Sqrt(b.NU * b.NU + b.NV * b.NV + b.NW * b.NW);
            if (aNormalLength <= RhinoMath.ZeroTolerance || bNormalLength <= RhinoMath.ZeroTolerance) return true;
            double dot = Math.Abs(a.NU * b.NU * s[0] + a.NV * b.NV * s[1] + a.NW * b.NW * s[2]);
            return dot >= .9986; // 约 3°，法向正负本身不作为孔轮廓方向。
        }
    }

    internal static class V6MigrationStore
    {
        public static void CreateEmpty(string path)
        {
            var enabled = new Dictionary<V5Criterion, bool> { { V5Criterion.Length, true }, { V5Criterion.Inertia, true }, { V5Criterion.Area, false }, { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true }, { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, true } };
            var tolerances = new Dictionary<V5Criterion, double> { { V5Criterion.Length, .5 }, { V5Criterion.Inertia, .2 }, { V5Criterion.Area, 1 }, { V5Criterion.Volume, .5 }, { V5Criterion.EdgeSum, 1 }, { V5Criterion.HoleArea, 1 }, { V5Criterion.HolePosition, .5 } };
            var decimals = new Dictionary<V5Criterion, int> { { V5Criterion.Length, 2 }, { V5Criterion.Inertia, 2 }, { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 }, { V5Criterion.EdgeSum, 2 }, { V5Criterion.HoleArea, 2 }, { V5Criterion.HolePosition, 4 } };
            Rebuild(path, new List<V6MigrationRecord>(), new V6MigrationAudit(), enabled, tolerances, decimals, true);
        }
        public static void Rebuild(string path, List<V6MigrationRecord> records, V6MigrationAudit audit, Dictionary<V5Criterion, bool> enabled, Dictionary<V5Criterion, double> tolerances, Dictionary<V5Criterion, int> decimals, bool creatingEmpty = false)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("新版数据库地址不能为空。");
            string full = Path.GetFullPath(path); string dir = Path.GetDirectoryName(full); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (!creatingEmpty && File.Exists(full)) File.Copy(full, full + ".v8backup_" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), true);
            using (var c = new SQLiteConnection("Data Source=" + full + ";Version=3;"))
            {
                c.Open(); using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_keys=ON; DROP TABLE IF EXISTS outer_vertices; DROP TABLE IF EXISTS outer_profiles; DROP TABLE IF EXISTS holes; DROP TABLE IF EXISTS parts; DROP TABLE IF EXISTS classification_profile; DROP TABLE IF EXISTS migration_audit; DROP TABLE IF EXISTS migration_runs; DROP TABLE IF EXISTS database_info; DROP TABLE IF EXISTS entity_feature_migration_v5; DROP TABLE IF EXISTS entity_feature_migration_v5_audit; CREATE TABLE database_info (id INTEGER PRIMARY KEY CHECK (id=1), schema_version INTEGER NOT NULL, algorithm_version TEXT NOT NULL, created_time TEXT NOT NULL); CREATE TABLE classification_profile (profile_id INTEGER PRIMARY KEY, settings_text TEXT NOT NULL); CREATE TABLE parts (part_id INTEGER PRIMARY KEY AUTOINCREMENT, profile_id INTEGER NOT NULL, part_number TEXT NOT NULL UNIQUE COLLATE NOCASE, material TEXT, color TEXT, entity_type INTEGER NOT NULL, is_solid INTEGER NOT NULL, volume REAL NOT NULL, surface_area REAL NOT NULL, bbox_max REAL NOT NULL, bbox_mid REAL NOT NULL, bbox_min REAL NOT NULL, bbox_count INTEGER NOT NULL CHECK(bbox_count=3), inertia_1 REAL NOT NULL, inertia_2 REAL NOT NULL, inertia_3 REAL NOT NULL, hole_count INTEGER NOT NULL, hole_perimeter_sum REAL NOT NULL, hole_area_sum REAL NOT NULL, source_file TEXT NOT NULL, source_index INTEGER NOT NULL, created_time TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(profile_id) REFERENCES classification_profile(profile_id)); CREATE TABLE holes (hole_id INTEGER PRIMARY KEY AUTOINCREMENT, part_id INTEGER NOT NULL, hole_index INTEGER NOT NULL, center_u REAL NOT NULL, center_v REAL NOT NULL, center_w REAL NOT NULL, perimeter REAL NOT NULL, enclosed_area REAL NOT NULL, normal_u REAL NOT NULL, normal_v REAL NOT NULL, normal_w REAL NOT NULL, FOREIGN KEY(part_id) REFERENCES parts(part_id) ON DELETE CASCADE, UNIQUE(part_id,hole_index)); CREATE TABLE outer_profiles (part_id INTEGER NOT NULL, profile_index INTEGER NOT NULL, enclosed_area REAL NOT NULL, perimeter REAL NOT NULL, normal_u REAL NOT NULL, normal_v REAL NOT NULL, normal_w REAL NOT NULL, vertex_count INTEGER NOT NULL, FOREIGN KEY(part_id) REFERENCES parts(part_id) ON DELETE CASCADE, PRIMARY KEY(part_id,profile_index)); CREATE TABLE outer_vertices (part_id INTEGER NOT NULL, profile_index INTEGER NOT NULL, vertex_index INTEGER NOT NULL, local_u REAL NOT NULL, local_v REAL NOT NULL, local_w REAL NOT NULL, FOREIGN KEY(part_id) REFERENCES parts(part_id) ON DELETE CASCADE, PRIMARY KEY(part_id,profile_index,vertex_index)); CREATE TABLE migration_runs (run_id INTEGER PRIMARY KEY AUTOINCREMENT, start_time TEXT NOT NULL, source_count INTEGER NOT NULL, inserted_count INTEGER NOT NULL, completed INTEGER NOT NULL); CREATE TABLE migration_audit (audit_id INTEGER PRIMARY KEY AUTOINCREMENT, issue_type TEXT NOT NULL, detail TEXT NOT NULL); CREATE INDEX idx_parts_prefilter ON parts(profile_id,entity_type,is_solid,hole_count,bbox_max); CREATE INDEX idx_parts_material_color ON parts(material,color); CREATE INDEX idx_holes_part ON holes(part_id);";
                    cmd.ExecuteNonQuery();
                    using (var tx = c.BeginTransaction())
                    {
                        cmd.CommandText = "INSERT INTO database_info (id,schema_version,algorithm_version,created_time) VALUES (1,8,'EntityFeatureMigrationV8',@time); INSERT INTO classification_profile (profile_id,settings_text) VALUES (1,@settings); INSERT INTO migration_runs (start_time,source_count,inserted_count,completed) VALUES (@time,@source,@inserted,1);";
                        cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("o", CultureInfo.InvariantCulture)); cmd.Parameters.AddWithValue("@settings", Settings(enabled, tolerances, decimals)); cmd.Parameters.AddWithValue("@source", audit.Classes.Sum(x => x.Count)); cmd.Parameters.AddWithValue("@inserted", records.Count); cmd.ExecuteNonQuery();
                        foreach (V6MigrationRecord r in records) InsertPart(c, r);
                        foreach (KeyValuePair<string, string> row in audit.AuditRows()) { cmd.CommandText = "INSERT INTO migration_audit (issue_type,detail) VALUES (@type,@detail);"; cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("@type", row.Key); cmd.Parameters.AddWithValue("@detail", row.Value); cmd.ExecuteNonQuery(); }
                        tx.Commit();
                    }
                }
            }
        }
        private static void InsertPart(SQLiteConnection c, V6MigrationRecord r)
        {
            double[] box; if (!V5Feature.TryReadBoundingBox(r.Feature.BoundingBox, out box) || box == null || box.Length != 3) throw new InvalidOperationException("V7 自动包围盒必须包含三条边。");
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO parts (profile_id,part_number,material,color,entity_type,is_solid,volume,surface_area,bbox_max,bbox_mid,bbox_min,bbox_count,inertia_1,inertia_2,inertia_3,hole_count,hole_perimeter_sum,hole_area_sum,source_file,source_index) VALUES (1,@number,@material,@color,@entity,@solid,@volume,@area,@max,@mid,@min,@count,@i1,@i2,@i3,@holes,@perimeters,@holeareas,@file,@index);";
                cmd.Parameters.AddWithValue("@number", r.Number); cmd.Parameters.AddWithValue("@material", r.Feature.Material); cmd.Parameters.AddWithValue("@color", r.Feature.Color); cmd.Parameters.AddWithValue("@entity", 1); cmd.Parameters.AddWithValue("@solid", r.Feature.Volume > 0 ? 1 : 0); cmd.Parameters.AddWithValue("@volume", r.Feature.Volume); cmd.Parameters.AddWithValue("@area", r.Feature.Area); cmd.Parameters.AddWithValue("@max", box[0]); cmd.Parameters.AddWithValue("@mid", box[1]); cmd.Parameters.AddWithValue("@min", box[2]); cmd.Parameters.AddWithValue("@count", 3); cmd.Parameters.AddWithValue("@i1", r.Feature.Inertia[0]); cmd.Parameters.AddWithValue("@i2", r.Feature.Inertia[1]); cmd.Parameters.AddWithValue("@i3", r.Feature.Inertia[2]); cmd.Parameters.AddWithValue("@holes", r.Feature.Holes.Count); cmd.Parameters.AddWithValue("@perimeters", r.Feature.Holes.Sum(x => x.Perimeter)); cmd.Parameters.AddWithValue("@holeareas", r.Feature.Holes.Sum(x => x.Area)); cmd.Parameters.AddWithValue("@file", r.SourceFile); cmd.Parameters.AddWithValue("@index", r.Index + 1); cmd.ExecuteNonQuery();
                cmd.CommandText = "SELECT last_insert_rowid();"; cmd.Parameters.Clear(); long id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                for (int i = 0; i < r.Feature.Holes.Count; i++) { V6Hole h = r.Feature.Holes[i]; cmd.CommandText = "INSERT INTO holes (part_id,hole_index,center_u,center_v,center_w,perimeter,enclosed_area,normal_u,normal_v,normal_w) VALUES (@id,@index,@u,@v,@w,@perimeter,@area,@nu,@nv,@nw);"; cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@index", i); cmd.Parameters.AddWithValue("@u", h.U); cmd.Parameters.AddWithValue("@v", h.V); cmd.Parameters.AddWithValue("@w", h.W); cmd.Parameters.AddWithValue("@perimeter", h.Perimeter); cmd.Parameters.AddWithValue("@area", h.Area); cmd.Parameters.AddWithValue("@nu", h.NU); cmd.Parameters.AddWithValue("@nv", h.NV); cmd.Parameters.AddWithValue("@nw", h.NW); cmd.ExecuteNonQuery(); }
                for (int profileIndex = 0; profileIndex < r.Feature.OuterProfiles.Count; profileIndex++) { V6OuterProfile profile = r.Feature.OuterProfiles[profileIndex]; cmd.CommandText = "INSERT INTO outer_profiles (part_id,profile_index,enclosed_area,perimeter,normal_u,normal_v,normal_w,vertex_count) VALUES (@id,@profile,@area,@perimeter,@nu,@nv,@nw,@count);"; cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@profile", profileIndex); cmd.Parameters.AddWithValue("@area", profile.Area); cmd.Parameters.AddWithValue("@perimeter", profile.Perimeter); cmd.Parameters.AddWithValue("@nu", profile.NU); cmd.Parameters.AddWithValue("@nv", profile.NV); cmd.Parameters.AddWithValue("@nw", profile.NW); cmd.Parameters.AddWithValue("@count", profile.Vertices.Count); cmd.ExecuteNonQuery(); for (int i = 0; i < profile.Vertices.Count; i++) { V6OuterVertex v = profile.Vertices[i]; cmd.CommandText = "INSERT INTO outer_vertices (part_id,profile_index,vertex_index,local_u,local_v,local_w) VALUES (@id,@profile,@index,@u,@v,@w);"; cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@profile", profileIndex); cmd.Parameters.AddWithValue("@index", i); cmd.Parameters.AddWithValue("@u", v.U); cmd.Parameters.AddWithValue("@v", v.V); cmd.Parameters.AddWithValue("@w", v.W); cmd.ExecuteNonQuery(); } }
            }
        }
        public static void Verify(string path, List<V6MigrationRecord> records)
        {
            using (var c = new SQLiteConnection("Data Source=" + Path.GetFullPath(path) + ";Version=3;"))
            {
                c.Open(); var ids = new List<long>();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT part_id,part_number,source_index,source_file,volume,surface_area,bbox_max,bbox_mid,bbox_min,bbox_count,inertia_1,inertia_2,inertia_3,hole_count,hole_perimeter_sum,hole_area_sum FROM parts ORDER BY source_index;";
                    using (var reader = cmd.ExecuteReader())
                    {
                        int row = 0;
                        while (reader.Read())
                        {
                            if (row >= records.Count) throw new InvalidOperationException("数据库回读记录数多于写入记录数。");
                            V6MigrationRecord r = records[row]; double[] box; V5Feature.TryReadBoundingBox(r.Feature.BoundingBox, out box);
                            bool valid = string.Equals(Convert.ToString(reader["part_number"]), r.Number, StringComparison.Ordinal) && Convert.ToInt32(reader["source_index"]) == r.Index + 1 && string.Equals(Path.GetFullPath(Convert.ToString(reader["source_file"])), Path.GetFullPath(r.SourceFile), StringComparison.OrdinalIgnoreCase) && Equal(reader["volume"], r.Feature.Volume) && Equal(reader["surface_area"], r.Feature.Area) && EqualNullable(reader["bbox_max"], box == null ? (double?)null : box[0]) && EqualNullable(reader["bbox_mid"], box != null && box.Length == 3 ? (double?)box[1] : null) && EqualNullable(reader["bbox_min"], box != null && box.Length == 3 ? (double?)box[2] : null) && Convert.ToInt32(reader["bbox_count"]) == (box == null ? 0 : box.Length) && Equal(reader["inertia_1"], r.Feature.Inertia[0]) && Equal(reader["inertia_2"], r.Feature.Inertia[1]) && Equal(reader["inertia_3"], r.Feature.Inertia[2]) && Convert.ToInt32(reader["hole_count"]) == r.Feature.Holes.Count && Equal(reader["hole_perimeter_sum"], r.Feature.Holes.Sum(x => x.Perimeter)) && Equal(reader["hole_area_sum"], r.Feature.Holes.Sum(x => x.Area));
                            if (!valid) throw new InvalidOperationException("数据库回读校核失败，记录序号：" + (row + 1).ToString(CultureInfo.InvariantCulture));
                            ids.Add(Convert.ToInt64(reader["part_id"])); row++;
                        }
                        if (row != records.Count) throw new InvalidOperationException("数据库回读记录数与写入记录数不一致。");
                    }
                }
                for (int i = 0; i < records.Count; i++) using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT center_u,center_v,center_w,perimeter,enclosed_area,normal_u,normal_v,normal_w FROM holes WHERE part_id=@id ORDER BY hole_index;"; cmd.Parameters.AddWithValue("@id", ids[i]);
                    using (var reader = cmd.ExecuteReader())
                    {
                        int holeIndex = 0; while (reader.Read()) { if (holeIndex >= records[i].Feature.Holes.Count) throw new InvalidOperationException("孔位回读记录数多于写入记录数。"); V6Hole h = records[i].Feature.Holes[holeIndex]; if (!Equal(reader["center_u"], h.U) || !Equal(reader["center_v"], h.V) || !Equal(reader["center_w"], h.W) || !Equal(reader["perimeter"], h.Perimeter) || !Equal(reader["enclosed_area"], h.Area) || !Equal(reader["normal_u"], h.NU) || !Equal(reader["normal_v"], h.NV) || !Equal(reader["normal_w"], h.NW)) throw new InvalidOperationException("孔位回读校核失败，记录序号：" + (i + 1).ToString(CultureInfo.InvariantCulture)); holeIndex++; }
                        if (holeIndex != records[i].Feature.Holes.Count) throw new InvalidOperationException("孔位回读记录数不一致，记录序号：" + (i + 1).ToString(CultureInfo.InvariantCulture));
                    }
                }
                for (int i = 0; i < records.Count; i++) using (var cmd = c.CreateCommand())
                {
                    List<V6OuterProfile> profiles = records[i].Feature.OuterProfiles;
                    cmd.CommandText = "SELECT profile_index,enclosed_area,perimeter,normal_u,normal_v,normal_w,vertex_count FROM outer_profiles WHERE part_id=@id ORDER BY profile_index;"; cmd.Parameters.AddWithValue("@id", ids[i]);
                    using (var reader = cmd.ExecuteReader())
                    {
                        int profileIndex = 0;
                        while (reader.Read()) { if (profileIndex >= profiles.Count) throw new InvalidOperationException("外轮廓回读数过多。"); V6OuterProfile profile = profiles[profileIndex]; if (Convert.ToInt32(reader["profile_index"]) != profileIndex || !Equal(reader["enclosed_area"], profile.Area) || !Equal(reader["perimeter"], profile.Perimeter) || !Equal(reader["normal_u"], profile.NU) || !Equal(reader["normal_v"], profile.NV) || !Equal(reader["normal_w"], profile.NW) || Convert.ToInt32(reader["vertex_count"]) != profile.Vertices.Count) throw new InvalidOperationException("外轮廓回读校核失败，记录序号：" + (i + 1).ToString(CultureInfo.InvariantCulture)); profileIndex++; }
                        if (profileIndex != profiles.Count) throw new InvalidOperationException("外轮廓回读数不一致，记录序号：" + (i + 1).ToString(CultureInfo.InvariantCulture));
                    }
                    for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++) { V6OuterProfile profile = profiles[profileIndex]; cmd.CommandText = "SELECT local_u,local_v,local_w FROM outer_vertices WHERE part_id=@id AND profile_index=@profile ORDER BY vertex_index;"; cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("@id", ids[i]); cmd.Parameters.AddWithValue("@profile", profileIndex); using (var reader = cmd.ExecuteReader()) { int vertexIndex = 0; while (reader.Read()) { if (vertexIndex >= profile.Vertices.Count) throw new InvalidOperationException("外轮廓顶点回读数过多。"); V6OuterVertex v = profile.Vertices[vertexIndex]; if (!Equal(reader["local_u"], v.U) || !Equal(reader["local_v"], v.V) || !Equal(reader["local_w"], v.W)) throw new InvalidOperationException("外轮廓顶点回读校核失败，记录序号：" + (i + 1).ToString(CultureInfo.InvariantCulture)); vertexIndex++; } if (vertexIndex != profile.Vertices.Count) throw new InvalidOperationException("外轮廓顶点回读数不一致，记录序号：" + (i + 1).ToString(CultureInfo.InvariantCulture)); } }
                }
            }
        }
        private static bool Equal(object databaseValue, double expected) { return !Convert.IsDBNull(databaseValue) && Math.Abs(Convert.ToDouble(databaseValue, CultureInfo.InvariantCulture) - expected) <= 1e-10; }
        private static bool EqualNullable(object databaseValue, double? expected) { return expected.HasValue ? Equal(databaseValue, expected.Value) : Convert.IsDBNull(databaseValue); }
        private static string Settings(Dictionary<V5Criterion, bool> enabled, Dictionary<V5Criterion, double> tolerances, Dictionary<V5Criterion, int> decimals) { return string.Join(";", enabled.Keys.OrderBy(x => x).Select(x => x + "=" + enabled[x] + ",tol=" + tolerances[x].ToString("R", CultureInfo.InvariantCulture) + ",precision=" + decimals[x].ToString(CultureInfo.InvariantCulture))); }
    }
}
