using CommonFunction;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Rhino;
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
    public class EntityFeatureClassifier : GH_Component
    {
        private const string SettingsChunk = "EntityFeatureClassifierSettings";
        internal const int FeatureVersion = 4;

        public enum ButtonVisual
        {
            Black,
            Grey
        }

        private readonly Dictionary<ComparisonCriterion, bool> _criterionStates = new Dictionary<ComparisonCriterion, bool>
        {
            { ComparisonCriterion.Inertia, false },
            { ComparisonCriterion.Area, false },
            { ComparisonCriterion.Volume, true },
            { ComparisonCriterion.EdgeSum, false },
            { ComparisonCriterion.HolePosition, false }
        };

        private readonly Dictionary<string, FeatureDbRecord> _lastSaveCandidates = new Dictionary<string, FeatureDbRecord>(StringComparer.Ordinal);
        private readonly List<EntityFeature> _lastComputedFeatures = new List<EntityFeature>();
        private readonly List<string> _lastNumbers = new List<string>();
        private readonly List<int> _lastBaseNumbers = new List<int>();
        private readonly List<string> _lastMirrorSides = new List<string>();
        private readonly List<string> _lastFeatureSummaries = new List<string>();

        private bool _runRequested;
        private bool _saveRequested;
        private bool _hasSeenRunInput;
        private bool _lastRunInput;
        private bool _hasUnsavedDatabaseRecords;
        private bool _isHandlingRhinoDocumentChange;
        private bool _isHandlingPendingSavePrompt;
        private uint _lastRhinoDocumentSerialNumber;
        private string _lastRhinoDocumentPath = string.Empty;
        private string _lastDatabasePath = string.Empty;
        private string _lastStatusMessage = "等待运行。";

        public ButtonVisual RunButtonColor { get; set; } = ButtonVisual.Black;
        public ButtonVisual SaveButtonColor { get; set; } = ButtonVisual.Black;
        public List<double> CurrentTolerances { get; private set; } = new List<double> { 1.0, 1.0, 1.0, 1.0, 1.0 };
        public double MirrorAbsoluteTolerance { get; private set; } = 0.001;

        public EntityFeatureClassifier()
          : this("EntityFeatureClassifier", "EntityFeatureClassifier",
              "根据字符串和几何特征对实体分类、编号，并可保存到 SQLite 数据库")
        {
        }

        protected EntityFeatureClassifier(string name, string nickname, string description)
          : base(name, nickname, description, "Parrot", "Tools")
        {
            RhinoDoc activeDoc = RhinoDoc.ActiveDoc;
            _lastRhinoDocumentSerialNumber = activeDoc?.RuntimeSerialNumber ?? 0;
            _lastRhinoDocumentPath = activeDoc?.Path ?? string.Empty;

            RhinoDoc.ActiveDocumentChanged += OnRhinoDocumentChanged;
            RhinoDoc.EndOpenDocument += OnRhinoDocumentChanged;
            RhinoDoc.CloseDocument += OnRhinoDocumentClosing;
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("材质", "材质", "与实体一一对应的材质列表", GH_ParamAccess.list);
            pManager.AddTextParameter("颜色", "颜色", "与实体一一对应的颜色列表", GH_ParamAccess.list);
            pManager.AddTextParameter("其它", "其它", "与实体一一对应的其它信息列表", GH_ParamAccess.list);
            pManager.AddBrepParameter("实体列表", "实体", "待分类的 Brep 实体列表", GH_ParamAccess.list);
            pManager.AddTextParameter("数据库地址", "数据库", "SQLite 数据库文件地址，文件必须已存在", GH_ParamAccess.item);
            pManager.AddTextParameter("编号前缀", "编号前缀", "与实体一一对应的编号前缀列表", GH_ParamAccess.list);
            pManager.AddIntegerParameter("数字长度", "数字长度", "数字部分长度，例如 2 -> 01, 02", GH_ParamAccess.item, 2);
            pManager.AddTextParameter("连字符", "连字符", "连接前缀和数字部分的分隔符，可为空", GH_ParamAccess.item, "-");
            pManager.AddBooleanParameter("运行", "运行", "True 或点击按钮后执行分类", GH_ParamAccess.item, false);
            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("编号", "编号", "与输入实体一一对应的编号", GH_ParamAccess.list);
            pManager.AddIntegerParameter("基础号", "基础号", "不含镜像后缀的基础数字编号", GH_ParamAccess.list);
            pManager.AddTextParameter("镜像侧", "镜像侧", "镜像方向标识：A、B 或空", GH_ParamAccess.list);
            pManager.AddTextParameter("特征摘要", "特征摘要", "实体特征摘要", GH_ParamAccess.list);
            pManager.AddTextParameter("状态", "状态", "运行状态信息", GH_ParamAccess.item);
        }

        public override void CreateAttributes()
        {
            Attributes = new EntityFeatureClassifierAttributes(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            Menu_AppendSeparator(menu);
            AppendCriterionMenuItem(menu, ComparisonCriterion.Inertia, "转动惯量");
            AppendCriterionMenuItem(menu, ComparisonCriterion.Area, "表面积");
            AppendCriterionMenuItem(menu, ComparisonCriterion.Volume, "体积");
            AppendCriterionMenuItem(menu, ComparisonCriterion.EdgeSum, "所有边周长");
            AppendCriterionMenuItem(menu, ComparisonCriterion.HolePosition, "孔位");
            AppendToleranceTextBox(menu, "镜像绝对误差", FormatDouble(MirrorAbsoluteTolerance), text => SetMirrorTolerance(text));
            ToolStripMenuItem mirrorItem = new ToolStripMenuItem("镜像区分    " + FormatDouble(MirrorAbsoluteTolerance))
            {
                Checked = true,
                Enabled = false
            };
            menu.Items.Add(mirrorItem);
        }

        private void AppendCriterionMenuItem(ToolStripDropDown menu, ComparisonCriterion criterion, string text)
        {
            string toleranceText = UseAbsoluteComparison(criterion)
                ? FormatDouble(ToleranceForCriterion(criterion))
                : FormatPercent(ToleranceForCriterion(criterion));
            ToolStripMenuItem item = Menu_AppendItem(menu, text + "    " + toleranceText, ToggleCriterionClicked, true, _criterionStates[criterion]);
            item.Tag = criterion;
            string toleranceLabel = UseAbsoluteComparison(criterion) ? "绝对误差" : "百分比误差";
            AppendToleranceTextBox(item.DropDown, toleranceLabel, FormatDouble(ToleranceForCriterion(criterion)), value => SetCriterionTolerance(criterion, value));
        }

        private void AppendToleranceTextBox(ToolStripDropDown menu, string label, string value, Action<string> commit)
        {
            ToolStripTextBox box = new ToolStripTextBox
            {
                Text = value,
                ToolTipText = label
            };
            box.KeyDown += (sender, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;

                commit(box.Text);
                e.Handled = true;
                e.SuppressKeyPress = true;
            };
            box.Leave += (sender, e) => commit(box.Text);

            ToolStripMenuItem holder = new ToolStripMenuItem(label)
            {
                Enabled = false
            };
            menu.Items.Add(holder);
            menu.Items.Add(box);
        }

        protected virtual bool UseAbsoluteComparison(ComparisonCriterion criterion)
        {
            return criterion == ComparisonCriterion.HolePosition;
        }

        private void ToggleCriterionClicked(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem item) || !(item.Tag is ComparisonCriterion criterion))
                return;

            _criterionStates[criterion] = !_criterionStates[criterion];
            ExpireSolution(true);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            bool runInput = false;
            DA.GetData(8, ref runInput);

            bool inputRunTriggered = _hasSeenRunInput && runInput && !_lastRunInput;
            _hasSeenRunInput = true;
            _lastRunInput = runInput;

            bool shouldRun = inputRunTriggered || _runRequested || _saveRequested;
            if (!shouldRun)
            {
                DA.SetDataList(0, _lastNumbers);
                DA.SetDataList(1, _lastBaseNumbers);
                DA.SetDataList(2, _lastMirrorSides);
                DA.SetDataList(3, _lastFeatureSummaries);
                DA.SetData(4, HasCachedOutputs() ? _lastStatusMessage : "等待手动运行；自动求解未读取输入。");
                return;
            }

            List<string> materialTexts = new List<string>();
            DA.GetDataList(0, materialTexts);

            List<string> colorTexts = new List<string>();
            DA.GetDataList(1, colorTexts);

            List<string> otherTexts = new List<string>();
            DA.GetDataList(2, otherTexts);

            List<Brep> geometries = new List<Brep>();
            if (!DA.GetDataList(3, geometries))
                return;

            string databasePath = string.Empty;
            if (!DA.GetData(4, ref databasePath))
                return;

            List<string> prefixes = new List<string>();
            DA.GetDataList(5, prefixes);

            int numberLength = 2;
            DA.GetData(6, ref numberLength);

            string separator = "-";
            DA.GetData(7, ref separator);

            _runRequested = false;

            List<string> numbers = new List<string>();
            List<int> baseNumbers = new List<int>();
            List<string> mirrorSides = new List<string>();
            List<string> featureSummaries = new List<string>();
            List<string> databaseMatchedNumbers = new List<string>();
            string statusMessage = string.Empty;

            try
            {
                ValidateInputs(materialTexts, colorTexts, otherTexts, prefixes, geometries, databasePath, numberLength);

                List<ComparisonCriterion> activeCriteria = GetActiveCriteria();
                double[] tolerances = CurrentTolerances.ToArray();
                double mirrorTolerance = MirrorAbsoluteTolerance;

                SQLiteFeatureStore store = new SQLiteFeatureStore(databasePath);
                store.EnsureSchema();
                List<FeatureDbRecord> databaseRecords = store.LoadRecords();

                List<EntityFeature> features = BuildFeatures(materialTexts, colorTexts, otherTexts, geometries, activeCriteria);
                _lastComputedFeatures.Clear();
                _lastComputedFeatures.AddRange(features);

                ClassificationResult result = Classify(features, prefixes, activeCriteria, tolerances, mirrorTolerance, databaseRecords);

                for (int i = 0; i < features.Count; i++)
                {
                    EntityFeature feature = features[i];
                    ClassificationItem item = result.Items[i];
                    string finalSide = item.UseSuffix ? item.NormalizedMirrorCode : string.Empty;
                    string finalNumber = FormatNumber(TextAt(prefixes, i), separator, item.BaseNumber, numberLength, finalSide);

                    numbers.Add(finalNumber);
                    baseNumbers.Add(item.BaseNumber);
                    mirrorSides.Add(finalSide);
                    featureSummaries.Add(feature.ToSummaryString(TextAt(prefixes, i), activeCriteria, finalSide));

                    if (result.DatabaseMatchedBaseKeys.Contains(BuildPrefixBaseKey(TextAt(prefixes, i), item.BaseNumber)))
                        databaseMatchedNumbers.Add(finalNumber);
                }

                CacheOutputs(numbers, baseNumbers, mirrorSides, featureSummaries);
                _lastDatabasePath = databasePath;

                if (_saveRequested)
                {
                    int savedCount = store.SaveRecords(result.SaveCandidates.Values.ToList());
                    statusMessage = $"分类完成，共 {features.Count} 个实体；保存 {savedCount} 条特征记录。";
                    _lastSaveCandidates.Clear();
                    foreach (KeyValuePair<string, FeatureDbRecord> pair in result.SaveCandidates)
                    {
                        _lastSaveCandidates[pair.Key] = pair.Value;
                    }

                    _hasUnsavedDatabaseRecords = store.HasMissingRecords(result.SaveCandidates.Values.ToList());
                }
                else
                {
                    statusMessage = BuildRunStatusMessage(features.Count, result.DatabaseMatchedBaseKeys.Count, databaseMatchedNumbers);
                    _lastSaveCandidates.Clear();
                    foreach (KeyValuePair<string, FeatureDbRecord> pair in result.SaveCandidates)
                    {
                        _lastSaveCandidates[pair.Key] = pair.Value;
                    }

                    _hasUnsavedDatabaseRecords = store.HasMissingRecords(result.SaveCandidates.Values.ToList());
                }

                _lastStatusMessage = statusMessage;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                statusMessage = ex.Message;
                _lastStatusMessage = statusMessage;
            }
            finally
            {
                _saveRequested = false;
            }

            DA.SetDataList(0, numbers);
            DA.SetDataList(1, baseNumbers);
            DA.SetDataList(2, mirrorSides);
            DA.SetDataList(3, featureSummaries);
            DA.SetData(4, statusMessage);
        }

        private static string BuildRunStatusMessage(int featureCount, int databaseMatchedBaseCount, List<string> databaseMatchedNumbers)
        {
            List<string> uniqueNumbers = (databaseMatchedNumbers ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            string message = $"分类完成，共 {featureCount} 个实体；命中数据库基础类 {databaseMatchedBaseCount} 个";
            if (uniqueNumbers.Count > 0)
                message += "；完整编号：" + string.Join(", ", uniqueNumbers);

            return message + "。";
        }

        private void CacheOutputs(List<string> numbers, List<int> baseNumbers, List<string> mirrorSides, List<string> featureSummaries)
        {
            _lastNumbers.Clear();
            _lastNumbers.AddRange(numbers);

            _lastBaseNumbers.Clear();
            _lastBaseNumbers.AddRange(baseNumbers);

            _lastMirrorSides.Clear();
            _lastMirrorSides.AddRange(mirrorSides);

            _lastFeatureSummaries.Clear();
            _lastFeatureSummaries.AddRange(featureSummaries);
        }

        private bool HasCachedOutputs()
        {
            return _lastNumbers.Count > 0 ||
                _lastBaseNumbers.Count > 0 ||
                _lastMirrorSides.Count > 0 ||
                _lastFeatureSummaries.Count > 0;
        }

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter chunk = writer.CreateChunk(SettingsChunk);
            chunk.SetBoolean(nameof(ComparisonCriterion.Inertia), _criterionStates[ComparisonCriterion.Inertia]);
            chunk.SetBoolean(nameof(ComparisonCriterion.Area), _criterionStates[ComparisonCriterion.Area]);
            chunk.SetBoolean(nameof(ComparisonCriterion.Volume), _criterionStates[ComparisonCriterion.Volume]);
            chunk.SetBoolean(nameof(ComparisonCriterion.EdgeSum), _criterionStates[ComparisonCriterion.EdgeSum]);
            chunk.SetBoolean(nameof(ComparisonCriterion.HolePosition), _criterionStates[ComparisonCriterion.HolePosition]);
            chunk.SetDouble(nameof(ComparisonCriterion.Inertia) + "Tolerance", ToleranceForCriterion(ComparisonCriterion.Inertia));
            chunk.SetDouble(nameof(ComparisonCriterion.Area) + "Tolerance", ToleranceForCriterion(ComparisonCriterion.Area));
            chunk.SetDouble(nameof(ComparisonCriterion.Volume) + "Tolerance", ToleranceForCriterion(ComparisonCriterion.Volume));
            chunk.SetDouble(nameof(ComparisonCriterion.EdgeSum) + "Tolerance", ToleranceForCriterion(ComparisonCriterion.EdgeSum));
            chunk.SetDouble(nameof(ComparisonCriterion.HolePosition) + "Tolerance", ToleranceForCriterion(ComparisonCriterion.HolePosition));
            chunk.SetDouble(nameof(MirrorAbsoluteTolerance), MirrorAbsoluteTolerance);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            GH_IReader chunk = reader.FindChunk(SettingsChunk);
            if (chunk != null)
            {
                bool value = false;
                if (chunk.TryGetBoolean(nameof(ComparisonCriterion.Inertia), ref value))
                    _criterionStates[ComparisonCriterion.Inertia] = value;
                if (chunk.TryGetBoolean(nameof(ComparisonCriterion.Area), ref value))
                    _criterionStates[ComparisonCriterion.Area] = value;
                if (chunk.TryGetBoolean(nameof(ComparisonCriterion.Volume), ref value))
                    _criterionStates[ComparisonCriterion.Volume] = value;
                if (chunk.TryGetBoolean(nameof(ComparisonCriterion.EdgeSum), ref value))
                    _criterionStates[ComparisonCriterion.EdgeSum] = value;
                if (chunk.TryGetBoolean(nameof(ComparisonCriterion.HolePosition), ref value))
                    _criterionStates[ComparisonCriterion.HolePosition] = value;

                ReadTolerance(chunk, ComparisonCriterion.Inertia);
                ReadTolerance(chunk, ComparisonCriterion.Area);
                ReadTolerance(chunk, ComparisonCriterion.Volume);
                ReadTolerance(chunk, ComparisonCriterion.EdgeSum);
                ReadTolerance(chunk, ComparisonCriterion.HolePosition);

                double mirrorTolerance = MirrorAbsoluteTolerance;
                if (chunk.TryGetDouble(nameof(MirrorAbsoluteTolerance), ref mirrorTolerance))
                    MirrorAbsoluteTolerance = Math.Abs(mirrorTolerance);
            }

            return base.Read(reader);
        }

        public void RequestRun()
        {
            _runRequested = true;
            ExpireSolution(true);
        }

        public void RequestSave()
        {
            _saveRequested = true;
            ExpireSolution(true);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            PromptToSavePendingRecords();
            RhinoDoc.ActiveDocumentChanged -= OnRhinoDocumentChanged;
            RhinoDoc.EndOpenDocument -= OnRhinoDocumentChanged;
            RhinoDoc.CloseDocument -= OnRhinoDocumentClosing;
            base.RemovedFromDocument(document);
        }

        private void OnRhinoDocumentClosing(object sender, EventArgs e)
        {
            PromptToSavePendingRecords();
        }

        private void OnRhinoDocumentChanged(object sender, EventArgs e)
        {
            RhinoDoc activeDoc = RhinoDoc.ActiveDoc;
            uint currentSerialNumber = activeDoc?.RuntimeSerialNumber ?? 0;
            string currentPath = activeDoc?.Path ?? string.Empty;

            if (currentSerialNumber == _lastRhinoDocumentSerialNumber &&
                string.Equals(currentPath, _lastRhinoDocumentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastRhinoDocumentSerialNumber = currentSerialNumber;
            _lastRhinoDocumentPath = currentPath;

            if (!_hasUnsavedDatabaseRecords || _isHandlingRhinoDocumentChange)
                return;

            _isHandlingRhinoDocumentChange = true;
            try
            {
                PromptToSavePendingRecords();
            }
            finally
            {
                _isHandlingRhinoDocumentChange = false;
            }
        }

        private void PromptToSavePendingRecords()
        {
            if (!_hasUnsavedDatabaseRecords || _isHandlingPendingSavePrompt)
                return;

            _isHandlingPendingSavePrompt = true;
            try
            {
                PromptToSavePendingRecordsCore();
            }
            finally
            {
                _isHandlingPendingSavePrompt = false;
            }
        }

        private void PromptToSavePendingRecordsCore()
        {
            int missingCount = GetPendingRecordCount();
            if (missingCount <= 0)
            {
                _hasUnsavedDatabaseRecords = false;
                return;
            }

            DialogResult result = MessageBox.Show(
                $"当前编号还有 {missingCount} 条未保存进数据库，是否现在保存？",
                "编号未保存",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                SavePendingRecords();
                return;
            }

            _hasUnsavedDatabaseRecords = false;
        }

        private int GetPendingRecordCount()
        {
            if (string.IsNullOrWhiteSpace(_lastDatabasePath) || !File.Exists(_lastDatabasePath) || _lastSaveCandidates.Count == 0)
                return 0;

            SQLiteFeatureStore store = new SQLiteFeatureStore(_lastDatabasePath);
            return store.CountMissingRecords(_lastSaveCandidates.Values.ToList());
        }

        private void SavePendingRecords()
        {
            if (string.IsNullOrWhiteSpace(_lastDatabasePath) || !File.Exists(_lastDatabasePath) || _lastSaveCandidates.Count == 0)
            {
                _hasUnsavedDatabaseRecords = false;
                return;
            }

            try
            {
                SQLiteFeatureStore store = new SQLiteFeatureStore(_lastDatabasePath);
                int savedCount = store.SaveRecords(_lastSaveCandidates.Values.ToList());
                _hasUnsavedDatabaseRecords = store.HasMissingRecords(_lastSaveCandidates.Values.ToList());
                _lastStatusMessage = $"文档切换时已保存 {savedCount} 条特征记录。";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "保存编号到数据库失败：" + ex.Message,
                    "编号保存失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void SetCriterionTolerance(ComparisonCriterion criterion, string text)
        {
            if (!TryParseTolerance(text, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "误差数值无法识别：" + text);
                return;
            }

            value = Math.Abs(value);
            if (Math.Abs(ToleranceForCriterion(criterion) - value) <= 1e-12)
                return;

            SetToleranceForCriterion(criterion, value);
            ExpireSolution(true);
        }

        private void SetMirrorTolerance(string text)
        {
            if (!TryParseTolerance(text, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "镜像绝对误差无法识别：" + text);
                return;
            }

            value = Math.Abs(value);
            if (Math.Abs(MirrorAbsoluteTolerance - value) <= 1e-12)
                return;

            MirrorAbsoluteTolerance = value;
            ExpireSolution(true);
        }

        private void ReadTolerance(GH_IReader chunk, ComparisonCriterion criterion)
        {
            double value = ToleranceForCriterion(criterion);
            if (chunk.TryGetDouble(criterion.ToString() + "Tolerance", ref value))
                SetToleranceForCriterion(criterion, value);
        }

        private void SetToleranceForCriterion(ComparisonCriterion criterion, double value)
        {
            EnsureToleranceSlots();
            CurrentTolerances[CriterionIndex(criterion)] = Math.Abs(value);
        }

        private void EnsureToleranceSlots()
        {
            while (CurrentTolerances.Count < 5)
            {
                CurrentTolerances.Add(1.0);
            }
        }

        public string GetConditionDisplayText()
        {
            List<string> text = new List<string> { "编号前缀", "材质", "颜色", "其它" };
            foreach (ComparisonCriterion criterion in GetActiveCriteria())
            {
                switch (criterion)
                {
                    case ComparisonCriterion.Inertia:
                        text.Add("转动惯量");
                        break;
                    case ComparisonCriterion.Area:
                        text.Add("表面积");
                        break;
                    case ComparisonCriterion.Volume:
                        text.Add("体积");
                        break;
                    case ComparisonCriterion.EdgeSum:
                        text.Add("所有边周长");
                        break;
                    case ComparisonCriterion.HolePosition:
                        text.Add("孔位");
                        break;
                }
            }

            text.Add("镜像区分");
            return string.Join(" + ", text);
        }

        private double ToleranceForCriterion(ComparisonCriterion criterion)
        {
            return ToleranceAt(CurrentTolerances, criterion);
        }

        internal static double ToleranceAt(IList<double> tolerances, ComparisonCriterion criterion)
        {
            if (tolerances == null || tolerances.Count == 0)
                return 1.0;

            if (tolerances.Count == 1)
                return tolerances[0];

            int index = CriterionIndex(criterion);
            if (index < tolerances.Count)
                return tolerances[index];

            return tolerances[tolerances.Count - 1];
        }

        internal static int CriterionIndex(ComparisonCriterion criterion)
        {
            switch (criterion)
            {
                case ComparisonCriterion.Inertia:
                    return 0;
                case ComparisonCriterion.Area:
                    return 1;
                case ComparisonCriterion.Volume:
                    return 2;
                case ComparisonCriterion.EdgeSum:
                    return 3;
                case ComparisonCriterion.HolePosition:
                    return 4;
                default:
                    return 0;
            }
        }

        private static string FormatPercent(double value)
        {
            return value.ToString("G6", CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("G6", CultureInfo.InvariantCulture);
        }

        protected override Bitmap Icon => GeneratedIcon.Get("gen_EntityFeatureClassifier");

        public override Guid ComponentGuid => new Guid("F1F780F3-7A3D-44B8-92CC-31B91536E6A0");

        private static void ValidateInputs(List<string> materialTexts, List<string> colorTexts, List<string> otherTexts, List<string> prefixes, List<Brep> geometries, string databasePath, int numberLength)
        {
            if (geometries == null || geometries.Count == 0)
                throw new ArgumentException("实体列表不能为空。");

            ValidateOptionalTextCount(materialTexts, geometries.Count, "材质");
            ValidateOptionalTextCount(colorTexts, geometries.Count, "颜色");
            ValidateOptionalTextCount(otherTexts, geometries.Count, "其它");
            ValidateRequiredTextCount(prefixes, geometries.Count, "编号前缀");

            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("数据库地址不能为空。");

            if (!File.Exists(databasePath))
                throw new FileNotFoundException("数据库文件不存在。", databasePath);

            if (numberLength <= 0)
                throw new ArgumentException("编号数字部分长度必须大于 0。");
        }

        private List<ComparisonCriterion> GetActiveCriteria()
        {
            List<ComparisonCriterion> criteria = new List<ComparisonCriterion>();
            if (_criterionStates[ComparisonCriterion.Inertia])
                criteria.Add(ComparisonCriterion.Inertia);
            if (_criterionStates[ComparisonCriterion.Area])
                criteria.Add(ComparisonCriterion.Area);
            if (_criterionStates[ComparisonCriterion.Volume])
                criteria.Add(ComparisonCriterion.Volume);
            if (_criterionStates[ComparisonCriterion.EdgeSum])
                criteria.Add(ComparisonCriterion.EdgeSum);
            if (_criterionStates[ComparisonCriterion.HolePosition])
                criteria.Add(ComparisonCriterion.HolePosition);
            return criteria;
        }

        private static bool TryParseTolerance(string text, out double value)
        {
            value = 0.0;
            text = (text ?? string.Empty).Trim();
            if (text.EndsWith("%", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 1).Trim();

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static void ValidateOptionalTextCount(List<string> texts, int geometryCount, string name)
        {
            if (texts == null || texts.Count == 0)
                return;

            if (texts.Count != geometryCount)
                throw new ArgumentException(name + "列表为空时允许省略；不为空时数量必须与实体列表一致。");
        }

        private static void ValidateRequiredTextCount(List<string> texts, int geometryCount, string name)
        {
            if (texts == null || texts.Count != geometryCount)
                throw new ArgumentException(name + "数量必须与实体列表一致。");
        }

        private static List<EntityFeature> BuildFeatures(List<string> materialTexts, List<string> colorTexts, List<string> otherTexts, List<Brep> geometries, List<ComparisonCriterion> activeCriteria)
        {
            List<EntityFeature> features = new List<EntityFeature>(geometries.Count);
            for (int i = 0; i < geometries.Count; i++)
            {
                if (geometries[i] == null)
                    throw new ArgumentException($"第 {i + 1} 个实体为空。");

                EntityFeature feature = EntityFeature.Create(
                    TextAt(materialTexts, i),
                    TextAt(colorTexts, i),
                    TextAt(otherTexts, i),
                    geometries[i],
                    activeCriteria,
                    i);
                features.Add(feature);
            }

            return features;
        }

        private static string TextAt(List<string> texts, int index)
        {
            if (texts == null || texts.Count == 0)
                return string.Empty;

            return texts[index] ?? string.Empty;
        }

        private ClassificationResult Classify(List<EntityFeature> features, List<string> prefixes, List<ComparisonCriterion> activeCriteria, double[] tolerances, double mirrorTolerance, List<FeatureDbRecord> databaseRecords)
        {
            ClassificationResult result = new ClassificationResult(features.Count);
            Dictionary<string, HashSet<string>> databaseSidesByBase = databaseRecords
                .GroupBy(x => BuildPrefixBaseKey(x.PrefixText, x.BaseNumber))
                .ToDictionary(
                    x => x.Key,
                    x => new HashSet<string>(x.Select(y => y.MirrorCode).Where(y => !string.IsNullOrWhiteSpace(y)), StringComparer.Ordinal));

            List<WorkingBaseGroup> groups = new List<WorkingBaseGroup>();
            Dictionary<string, int> nextBaseNumbers = databaseRecords
                .GroupBy(x => x.PrefixText ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Max(y => y.BaseNumber) + 1, StringComparer.Ordinal);

            for (int i = 0; i < features.Count; i++)
            {
                EntityFeature feature = features[i];
                string prefix = TextAt(prefixes, i);
                WorkingBaseGroup localGroup = groups.FirstOrDefault(x => x.CanAccept(prefix, feature, activeCriteria, tolerances, UseAbsoluteComparison));
                if (localGroup == null)
                {
                    int baseNumber = FindMatchingDatabaseBaseNumber(prefix, feature, activeCriteria, tolerances, databaseRecords, UseAbsoluteComparison);
                    bool matchedDatabase = baseNumber > 0;
                    if (!matchedDatabase)
                    {
                        if (!nextBaseNumbers.TryGetValue(prefix, out baseNumber))
                            baseNumber = 1;

                        nextBaseNumbers[prefix] = baseNumber + 1;
                    }
                    else
                    {
                        result.DatabaseMatchedBaseKeys.Add(BuildPrefixBaseKey(prefix, baseNumber));
                    }

                    localGroup = new WorkingBaseGroup(prefix, feature, baseNumber, matchedDatabase);
                    groups.Add(localGroup);
                }

                localGroup.Add(feature.Index);
            }

            foreach (WorkingBaseGroup group in groups)
            {
                HashSet<string> currentSides = new HashSet<string>(
                    group.MemberIndexes
                        .Select(index => features[index].MirrorCodeForTolerance(mirrorTolerance))
                        .Where(side => !string.IsNullOrWhiteSpace(side)),
                    StringComparer.Ordinal);

                HashSet<string> databaseSides = databaseSidesByBase.TryGetValue(BuildPrefixBaseKey(group.PrefixText, group.BaseNumber), out HashSet<string> sideSet)
                    ? sideSet
                    : new HashSet<string>(StringComparer.Ordinal);

                HashSet<string> allSides = new HashSet<string>(currentSides, StringComparer.Ordinal);
                allSides.UnionWith(databaseSides);
                bool useSuffix = allSides.Count >= 2;

                foreach (int index in group.MemberIndexes)
                {
                    result.Items[index] = new ClassificationItem
                    {
                        BaseNumber = group.BaseNumber,
                        NormalizedMirrorCode = features[index].MirrorCodeForTolerance(mirrorTolerance),
                        UseSuffix = useSuffix
                    };
                }

                foreach (IGrouping<string, EntityFeature> orientationGroup in group.MemberIndexes
                    .Select(index => features[index])
                    .GroupBy(x => x.MirrorCodeForTolerance(mirrorTolerance), StringComparer.Ordinal))
                {
                    EntityFeature representative = orientationGroup.First();
                    string storeMirrorCode = representative.MirrorCodeForTolerance(mirrorTolerance);
                    string saveKey = FeatureDbRecord.BuildKey(group.PrefixText, group.BaseNumber, storeMirrorCode, representative);

                    if (!result.SaveCandidates.ContainsKey(saveKey))
                    {
                        result.SaveCandidates.Add(saveKey, FeatureDbRecord.FromFeature(group.PrefixText, group.BaseNumber, storeMirrorCode, representative));
                    }
                }
            }

            return result;
        }

        private static int FindMatchingDatabaseBaseNumber(string prefix, EntityFeature feature, List<ComparisonCriterion> activeCriteria, double[] tolerances, List<FeatureDbRecord> databaseRecords, Func<ComparisonCriterion, bool> useAbsoluteComparison)
        {
            FeatureDbRecord match = databaseRecords.FirstOrDefault(record =>
                string.Equals(record.PrefixText, prefix ?? string.Empty, StringComparison.Ordinal) &&
                record.MatchesBase(feature, activeCriteria, tolerances, useAbsoluteComparison));
            return match?.BaseNumber ?? -1;
        }

        private static string BuildPrefixBaseKey(string prefix, int baseNumber)
        {
            return (prefix ?? string.Empty) + "|" + baseNumber.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatNumber(string prefix, string separator, int baseNumber, int numberLength, string suffix)
        {
            string numeric = baseNumber.ToString().PadLeft(numberLength, '0');
            string head = string.IsNullOrEmpty(prefix) ? numeric : prefix + (separator ?? string.Empty) + numeric;
            return string.IsNullOrEmpty(suffix) ? head : head + suffix;
        }

        public enum ComparisonCriterion
        {
            Inertia,
            Area,
            Volume,
            EdgeSum,
            HolePosition
        }
    }

    public class EntityFeatureClassifierAbsolute : EntityFeatureClassifier
    {
        public EntityFeatureClassifierAbsolute()
            : base("EntityFeatureClassifierAbsolute", "EFC绝对误差",
                "根据字符串和几何特征对实体分类、编号；所有特征误差均按绝对误差比较")
        {
        }

        protected override bool UseAbsoluteComparison(ComparisonCriterion criterion)
        {
            return true;
        }

        public override Guid ComponentGuid => new Guid("2AE6997E-F4D5-4E71-8506-42B9E851F719");
    }

    internal sealed class EntityFeatureClassifierAttributes : GH_ComponentAttributes
    {
        private const float ButtonHeight = 20.0f;
        private const float TextHeight = 18.0f;

        public EntityFeatureClassifierAttributes(EntityFeatureClassifier owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + ButtonHeight + TextHeight);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            EntityFeatureClassifier owner = (EntityFeatureClassifier)Owner;

            RectangleF rowRect = new RectangleF(Bounds.X + 5.0f, Bounds.Bottom - ButtonHeight - TextHeight + 2.0f, Bounds.Width - 10.0f, ButtonHeight - 4.0f);
            RectangleF runRect = new RectangleF(rowRect.X, rowRect.Y, rowRect.Width / 2.0f - 2.0f, rowRect.Height);
            RectangleF saveRect = new RectangleF(runRect.Right + 4.0f, rowRect.Y, rowRect.Width / 2.0f - 2.0f, rowRect.Height);
            RectangleF textRect = new RectangleF(Bounds.X + 3.0f, Bounds.Bottom - TextHeight + 1.0f, Bounds.Width - 6.0f, TextHeight - 2.0f);

            RenderButton(graphics, runRect, owner.RunButtonColor == EntityFeatureClassifier.ButtonVisual.Black ? GH_Palette.Black : GH_Palette.Grey, "运行");
            RenderButton(graphics, saveRect, owner.SaveButtonColor == EntityFeatureClassifier.ButtonVisual.Black ? GH_Palette.Black : GH_Palette.Grey, "保存");

            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(owner.GetConditionDisplayText(), GH_FontServer.Small, Brushes.DimGray, textRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF rowRect = new RectangleF(Bounds.X + 5.0f, Bounds.Bottom - ButtonHeight - TextHeight + 2.0f, Bounds.Width - 10.0f, ButtonHeight - 4.0f);
            RectangleF runRect = new RectangleF(rowRect.X, rowRect.Y, rowRect.Width / 2.0f - 2.0f, rowRect.Height);
            RectangleF saveRect = new RectangleF(runRect.Right + 4.0f, rowRect.Y, rowRect.Width / 2.0f - 2.0f, rowRect.Height);

            if (e.Button != MouseButtons.Left)
                return GH_ObjectResponse.Ignore;

            EntityFeatureClassifier owner = (EntityFeatureClassifier)Owner;

            if (runRect.Contains(e.CanvasLocation))
            {
                owner.RunButtonColor = EntityFeatureClassifier.ButtonVisual.Grey;
                owner.ExpireSolution(true);
                CMath.Delay(50);
                owner.RunButtonColor = EntityFeatureClassifier.ButtonVisual.Black;
                owner.RequestRun();
                return GH_ObjectResponse.Handled;
            }

            if (saveRect.Contains(e.CanvasLocation))
            {
                owner.SaveButtonColor = EntityFeatureClassifier.ButtonVisual.Grey;
                owner.ExpireSolution(true);
                CMath.Delay(50);
                owner.SaveButtonColor = EntityFeatureClassifier.ButtonVisual.Black;
                owner.RequestSave();
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }

        private static void RenderButton(Graphics graphics, RectangleF rect, GH_Palette palette, string text)
        {
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(rect, palette))
            {
                capsule.Render(graphics, false, false, false);
            }

            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(text, new Font(GH_FontServer.Small, FontStyle.Bold), Brushes.White, rect, format);
            }
        }
    }

    internal sealed class ClassificationResult
    {
        public ClassificationResult(int count)
        {
            Items = new ClassificationItem[count];
        }

        public ClassificationItem[] Items { get; }

        public Dictionary<string, FeatureDbRecord> SaveCandidates { get; } = new Dictionary<string, FeatureDbRecord>(StringComparer.Ordinal);

        public HashSet<string> DatabaseMatchedBaseKeys { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    internal sealed class ClassificationItem
    {
        public int BaseNumber { get; set; }

        public string NormalizedMirrorCode { get; set; } = string.Empty;

        public bool UseSuffix { get; set; }
    }

    internal sealed class WorkingBaseGroup
    {
        public WorkingBaseGroup(string prefixText, EntityFeature representative, int baseNumber, bool matchedDatabase)
        {
            PrefixText = prefixText ?? string.Empty;
            Representative = representative;
            BaseNumber = baseNumber;
            MatchedDatabase = matchedDatabase;
        }

        public string PrefixText { get; }

        public EntityFeature Representative { get; }

        public int BaseNumber { get; }

        public bool MatchedDatabase { get; }

        public List<int> MemberIndexes { get; } = new List<int>();

        public void Add(int index)
        {
            MemberIndexes.Add(index);
        }

        public bool CanAccept(string prefixText, EntityFeature feature, List<EntityFeatureClassifier.ComparisonCriterion> activeCriteria, double[] tolerances, Func<EntityFeatureClassifier.ComparisonCriterion, bool> useAbsoluteComparison)
        {
            if (!string.Equals(PrefixText, prefixText ?? string.Empty, StringComparison.Ordinal))
                return false;

            return Representative.MatchesBase(feature, activeCriteria, tolerances, useAbsoluteComparison);
        }
    }

    internal sealed class FeatureDbRecord
    {
        public string PrefixText { get; set; } = string.Empty;

        public int BaseNumber { get; set; }

        public string MaterialText { get; set; } = string.Empty;

        public string ColorText { get; set; } = string.Empty;

        public string OtherText { get; set; } = string.Empty;

        public double Area { get; set; }

        public double Volume { get; set; }

        public double EdgeSum { get; set; }

        public double Inertia1 { get; set; }

        public double Inertia2 { get; set; }

        public double Inertia3 { get; set; }

        public string HoleData { get; set; } = string.Empty;

        public string MirrorCode { get; set; } = string.Empty;

        public double MirrorScore { get; set; }

        public static FeatureDbRecord FromFeature(string prefixText, int baseNumber, string mirrorCode, EntityFeature feature)
        {
            return new FeatureDbRecord
            {
                PrefixText = prefixText ?? string.Empty,
                BaseNumber = baseNumber,
                MaterialText = feature.MaterialText,
                ColorText = feature.ColorText,
                OtherText = feature.OtherText,
                Area = feature.Area,
                Volume = feature.Volume,
                EdgeSum = feature.EdgeSum,
                Inertia1 = feature.Inertia[0],
                Inertia2 = feature.Inertia[1],
                Inertia3 = feature.Inertia[2],
                HoleData = HoleFeature.ToStorageString(feature.HoleFeatures),
                MirrorCode = mirrorCode ?? string.Empty,
                MirrorScore = feature.MirrorScore
            };
        }

        public bool MatchesBase(EntityFeature feature, List<EntityFeatureClassifier.ComparisonCriterion> activeCriteria, double[] tolerances, Func<EntityFeatureClassifier.ComparisonCriterion, bool> useAbsoluteComparison)
        {
            if (!string.Equals(MaterialText, feature.MaterialText, StringComparison.Ordinal) ||
                !string.Equals(ColorText, feature.ColorText, StringComparison.Ordinal) ||
                !string.Equals(OtherText, feature.OtherText, StringComparison.Ordinal))
                return false;

            for (int i = 0; i < activeCriteria.Count; i++)
            {
                double tolerance = EntityFeatureClassifier.ToleranceAt(tolerances, activeCriteria[i]);
                switch (activeCriteria[i])
                {
                    case EntityFeatureClassifier.ComparisonCriterion.Inertia:
                        if (!EntityFeature.WithinTolerance(Inertia1, feature.Inertia[0], tolerance, useAbsoluteComparison(activeCriteria[i])) ||
                            !EntityFeature.WithinTolerance(Inertia2, feature.Inertia[1], tolerance, useAbsoluteComparison(activeCriteria[i])) ||
                            !EntityFeature.WithinTolerance(Inertia3, feature.Inertia[2], tolerance, useAbsoluteComparison(activeCriteria[i])))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.Area:
                        if (!EntityFeature.WithinTolerance(Area, feature.Area, tolerance, useAbsoluteComparison(activeCriteria[i])))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.Volume:
                        if (!EntityFeature.WithinTolerance(Volume, feature.Volume, tolerance, useAbsoluteComparison(activeCriteria[i])))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.EdgeSum:
                        if (!EntityFeature.WithinTolerance(EdgeSum, feature.EdgeSum, tolerance, useAbsoluteComparison(activeCriteria[i])))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.HolePosition:
                        if (!HoleFeature.Matches(HoleFeature.FromStorageString(HoleData), feature.HoleFeatures, tolerance))
                            return false;
                        break;
                }
            }

            return true;
        }

        public static string BuildKey(string prefixText, int baseNumber, string mirrorCode, EntityFeature feature)
        {
            return string.Join("|",
                prefixText ?? string.Empty,
                baseNumber.ToString(CultureInfo.InvariantCulture),
                feature.MaterialText ?? string.Empty,
                feature.ColorText ?? string.Empty,
                feature.OtherText ?? string.Empty,
                mirrorCode ?? string.Empty,
                feature.Volume.ToString("R", CultureInfo.InvariantCulture),
                feature.Area.ToString("R", CultureInfo.InvariantCulture),
                feature.EdgeSum.ToString("R", CultureInfo.InvariantCulture),
                feature.Inertia[0].ToString("R", CultureInfo.InvariantCulture),
                feature.Inertia[1].ToString("R", CultureInfo.InvariantCulture),
                feature.Inertia[2].ToString("R", CultureInfo.InvariantCulture),
                HoleFeature.ToStorageString(feature.HoleFeatures));
        }
    }

    internal sealed class SQLiteFeatureStore
    {
        private readonly string _path;

        public SQLiteFeatureStore(string path)
        {
            _path = path;
        }

        public void EnsureSchema()
        {
            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    @"CREATE TABLE IF NOT EXISTS entity_feature_classes (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        prefix_text TEXT NOT NULL DEFAULT '',
                        material_text TEXT NOT NULL,
                        color_text TEXT NOT NULL,
                        other_text TEXT NOT NULL,
                        base_number INTEGER NOT NULL,
                        area REAL NOT NULL,
                        volume REAL NOT NULL,
                        edge_sum REAL NOT NULL,
                        inertia_1 REAL NOT NULL,
                        inertia_2 REAL NOT NULL,
                        inertia_3 REAL NOT NULL,
                        hole_data TEXT NOT NULL DEFAULT '',
                        mirror_code TEXT NOT NULL,
                        mirror_score REAL NOT NULL,
                        feature_version INTEGER NOT NULL,
                        created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );";
                command.ExecuteNonQuery();

                command.CommandText =
                    @"CREATE INDEX IF NOT EXISTS idx_entity_feature_classes_text_number
                      ON entity_feature_classes(prefix_text, material_text, color_text, other_text, base_number);";
                command.ExecuteNonQuery();
            }

            ValidateSchema();
        }

        private void ValidateSchema()
        {
            HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(entity_feature_classes);";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader.GetString(1));
                    }
                }
            }

            string[] requiredColumns =
            {
                "prefix_text",
                "material_text",
                "color_text",
                "other_text",
                "base_number",
                "area",
                "volume",
                "edge_sum",
                "inertia_1",
                "inertia_2",
                "inertia_3",
                "hole_data",
                "mirror_code",
                "mirror_score",
                "feature_version"
            };

            if (columns.Contains("model_text"))
                throw new InvalidOperationException("数据库表 entity_feature_classes 含有旧字段 model_text，请使用 CreateBrepCodeDatabase 重建数据库。");

            foreach (string column in requiredColumns)
            {
                if (!columns.Contains(column))
                    throw new InvalidOperationException("数据库表 entity_feature_classes 不是新版结构，请使用新版数据库或重建该表。");
            }
        }

        public List<FeatureDbRecord> LoadRecords()
        {
            List<FeatureDbRecord> records = new List<FeatureDbRecord>();
            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    @"SELECT prefix_text, base_number, material_text, color_text, other_text, area, volume, edge_sum, inertia_1, inertia_2, inertia_3, hole_data, mirror_code, mirror_score
                      FROM entity_feature_classes
                      WHERE feature_version = @feature_version;";
                command.Parameters.AddWithValue("@feature_version", EntityFeatureClassifier.FeatureVersion);

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new FeatureDbRecord
                        {
                            PrefixText = reader.GetString(0),
                            BaseNumber = reader.GetInt32(1),
                            MaterialText = reader.GetString(2),
                            ColorText = reader.GetString(3),
                            OtherText = reader.GetString(4),
                            Area = reader.GetDouble(5),
                            Volume = reader.GetDouble(6),
                            EdgeSum = reader.GetDouble(7),
                            Inertia1 = reader.GetDouble(8),
                            Inertia2 = reader.GetDouble(9),
                            Inertia3 = reader.GetDouble(10),
                            HoleData = reader.GetString(11),
                            MirrorCode = reader.GetString(12),
                            MirrorScore = reader.GetDouble(13)
                        });
                    }
                }
            }

            return records;
        }

        public int SaveRecords(List<FeatureDbRecord> records)
        {
            if (records == null || records.Count == 0)
                return 0;

            int inserted = 0;
            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            {
                foreach (FeatureDbRecord record in records)
                {
                    if (RecordExists(connection, transaction, record))
                        continue;

                    using (SQLiteCommand command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText =
                            @"INSERT INTO entity_feature_classes
                              (prefix_text, material_text, color_text, other_text, base_number, area, volume, edge_sum, inertia_1, inertia_2, inertia_3, hole_data, mirror_code, mirror_score, feature_version)
                              VALUES
                              (@prefix_text, @material_text, @color_text, @other_text, @base_number, @area, @volume, @edge_sum, @inertia_1, @inertia_2, @inertia_3, @hole_data, @mirror_code, @mirror_score, @feature_version);";
                        command.Parameters.AddWithValue("@prefix_text", record.PrefixText ?? string.Empty);
                        command.Parameters.AddWithValue("@material_text", record.MaterialText);
                        command.Parameters.AddWithValue("@color_text", record.ColorText);
                        command.Parameters.AddWithValue("@other_text", record.OtherText);
                        command.Parameters.AddWithValue("@base_number", record.BaseNumber);
                        command.Parameters.AddWithValue("@area", record.Area);
                        command.Parameters.AddWithValue("@volume", record.Volume);
                        command.Parameters.AddWithValue("@edge_sum", record.EdgeSum);
                        command.Parameters.AddWithValue("@inertia_1", record.Inertia1);
                        command.Parameters.AddWithValue("@inertia_2", record.Inertia2);
                        command.Parameters.AddWithValue("@inertia_3", record.Inertia3);
                        command.Parameters.AddWithValue("@hole_data", record.HoleData ?? string.Empty);
                        command.Parameters.AddWithValue("@mirror_code", record.MirrorCode ?? string.Empty);
                        command.Parameters.AddWithValue("@mirror_score", record.MirrorScore);
                        command.Parameters.AddWithValue("@feature_version", EntityFeatureClassifier.FeatureVersion);
                        inserted += command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            return inserted;
        }

        public bool HasMissingRecords(List<FeatureDbRecord> records)
        {
            return CountMissingRecords(records) > 0;
        }

        public int CountMissingRecords(List<FeatureDbRecord> records)
        {
            if (records == null || records.Count == 0)
                return 0;

            int missingCount = 0;
            using (SQLiteConnection connection = OpenConnection())
            {
                foreach (FeatureDbRecord record in records)
                {
                    if (!RecordExists(connection, null, record))
                        missingCount++;
                }
            }

            return missingCount;
        }

        private bool RecordExists(SQLiteConnection connection, SQLiteTransaction transaction, FeatureDbRecord record)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    @"SELECT COUNT(1)
                      FROM entity_feature_classes
                      WHERE prefix_text = @prefix_text
                        AND material_text = @material_text
                        AND color_text = @color_text
                        AND other_text = @other_text
                        AND base_number = @base_number
                        AND mirror_code = @mirror_code
                        AND feature_version = @feature_version
                        AND ABS(area - @area) < 1e-9
                        AND ABS(volume - @volume) < 1e-9
                        AND ABS(edge_sum - @edge_sum) < 1e-9
                        AND ABS(inertia_1 - @inertia_1) < 1e-9
                        AND ABS(inertia_2 - @inertia_2) < 1e-9
                        AND ABS(inertia_3 - @inertia_3) < 1e-9
                        AND hole_data = @hole_data;";
                command.Parameters.AddWithValue("@prefix_text", record.PrefixText ?? string.Empty);
                command.Parameters.AddWithValue("@material_text", record.MaterialText);
                command.Parameters.AddWithValue("@color_text", record.ColorText);
                command.Parameters.AddWithValue("@other_text", record.OtherText);
                command.Parameters.AddWithValue("@base_number", record.BaseNumber);
                command.Parameters.AddWithValue("@mirror_code", record.MirrorCode ?? string.Empty);
                command.Parameters.AddWithValue("@area", record.Area);
                command.Parameters.AddWithValue("@volume", record.Volume);
                command.Parameters.AddWithValue("@edge_sum", record.EdgeSum);
                command.Parameters.AddWithValue("@inertia_1", record.Inertia1);
                command.Parameters.AddWithValue("@inertia_2", record.Inertia2);
                command.Parameters.AddWithValue("@inertia_3", record.Inertia3);
                command.Parameters.AddWithValue("@hole_data", record.HoleData ?? string.Empty);
                command.Parameters.AddWithValue("@feature_version", EntityFeatureClassifier.FeatureVersion);

                long count = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                return count > 0;
            }
        }

        private SQLiteConnection OpenConnection()
        {
            SQLiteConnection connection = new SQLiteConnection($"Data Source={_path};Version=3;");
            connection.Open();
            return connection;
        }
    }

    internal sealed class EntityFeature
    {
        private const double MirrorEpsilon = 1e-9;
        private const double PrincipalInertiaDegenerateTolerance = 1e-6;
        private const double SamplePointMergeToleranceRatio = 1e-8;

        private EntityFeature()
        {
        }

        public int Index { get; private set; }

        public string MaterialText { get; private set; } = string.Empty;

        public string ColorText { get; private set; } = string.Empty;

        public string OtherText { get; private set; } = string.Empty;

        public double Area { get; private set; }

        public double Volume { get; private set; }

        public double EdgeSum { get; private set; }

        public double[] Inertia { get; private set; } = new double[3];

        public List<HoleFeature> HoleFeatures { get; private set; } = new List<HoleFeature>();

        public double MirrorScore { get; private set; }

        public string NormalizedMirrorCode { get; private set; } = string.Empty;

        public static EntityFeature Create(string materialText, string colorText, string otherText, Brep geometry, List<EntityFeatureClassifier.ComparisonCriterion> activeCriteria, int index)
        {
            EntityFeature feature = new EntityFeature
            {
                Index = index,
                MaterialText = materialText ?? string.Empty,
                ColorText = colorText ?? string.Empty,
                OtherText = otherText ?? string.Empty
            };

            if (!TryBuildFromBrep(geometry, activeCriteria, out ShapeData shapeData))
                throw new ArgumentException($"第 {index + 1} 个 Brep 实体无法分析。");

            feature.Area = shapeData.Area;
            feature.Volume = shapeData.Volume;
            feature.EdgeSum = shapeData.EdgeSum;
            feature.HoleFeatures = shapeData.HoleFeatures;

            List<Point3d> samplePoints = shapeData.SamplePoints;
            if (samplePoints.Count < 4)
                throw new ArgumentException($"第 {index + 1} 个实体可用于分析的采样点不足。");

            if (shapeData.RhinoInertia != null && shapeData.RhinoAxes != null)
            {
                feature.Inertia = shapeData.RhinoInertia;
                feature.MirrorScore = HasDegeneratePrincipalInertia(feature.Inertia)
                    ? 0.0
                    : ComputeMirrorScore(samplePoints, shapeData.RhinoCentroid, shapeData.RhinoAxes);
            }
            else
            {
                ComputePointCloudInertiaAndMirror(samplePoints, out double[] inertia, out double mirrorScore);
                feature.Inertia = inertia;
                feature.MirrorScore = mirrorScore;
            }
            feature.NormalizedMirrorCode = Math.Abs(feature.MirrorScore) < MirrorEpsilon ? string.Empty : (feature.MirrorScore >= 0.0 ? "A" : "B");

            if (activeCriteria.Contains(EntityFeatureClassifier.ComparisonCriterion.Volume) && feature.Volume <= 0.0)
                throw new ArgumentException($"第 {index + 1} 个实体无法计算有效体积，请输入封闭实体或取消“体积”条件。");

            return feature;
        }

        public bool MatchesBase(EntityFeature other, List<EntityFeatureClassifier.ComparisonCriterion> activeCriteria, double[] tolerances, Func<EntityFeatureClassifier.ComparisonCriterion, bool> useAbsoluteComparison)
        {
            if (!string.Equals(MaterialText, other.MaterialText, StringComparison.Ordinal) ||
                !string.Equals(ColorText, other.ColorText, StringComparison.Ordinal) ||
                !string.Equals(OtherText, other.OtherText, StringComparison.Ordinal))
                return false;

            for (int i = 0; i < activeCriteria.Count; i++)
            {
                double tolerance = EntityFeatureClassifier.ToleranceAt(tolerances, activeCriteria[i]);
                switch (activeCriteria[i])
                {
                    case EntityFeatureClassifier.ComparisonCriterion.Inertia:
                        if (!WithinTolerance(Inertia[0], other.Inertia[0], tolerance, useAbsoluteComparison(activeCriteria[i])) ||
                            !WithinTolerance(Inertia[1], other.Inertia[1], tolerance, useAbsoluteComparison(activeCriteria[i])) ||
                            !WithinTolerance(Inertia[2], other.Inertia[2], tolerance, useAbsoluteComparison(activeCriteria[i])))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.Area:
                        if (!WithinTolerance(Area, other.Area, tolerance, useAbsoluteComparison(activeCriteria[i])))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.Volume:
                        if (!WithinTolerance(Volume, other.Volume, tolerance, useAbsoluteComparison(activeCriteria[i])))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.EdgeSum:
                        if (!WithinTolerance(EdgeSum, other.EdgeSum, tolerance, useAbsoluteComparison(activeCriteria[i])))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.HolePosition:
                        if (!HoleFeature.Matches(HoleFeatures, other.HoleFeatures, tolerance))
                            return false;
                        break;
                }
            }

            return true;
        }

        public string MirrorCodeForTolerance(double mirrorTolerance)
        {
            return Math.Abs(MirrorScore) <= Math.Abs(mirrorTolerance) ? string.Empty : (MirrorScore >= 0.0 ? "A" : "B");
        }

        public string ToSummaryString(string prefixText, List<EntityFeatureClassifier.ComparisonCriterion> activeCriteria, string mirrorSide)
        {
            List<string> parts = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "编号前缀={0}", prefixText ?? string.Empty),
                string.Format(CultureInfo.InvariantCulture, "材质={0}", MaterialText),
                string.Format(CultureInfo.InvariantCulture, "颜色={0}", ColorText),
                string.Format(CultureInfo.InvariantCulture, "其它={0}", OtherText)
            };

            foreach (EntityFeatureClassifier.ComparisonCriterion criterion in activeCriteria ?? new List<EntityFeatureClassifier.ComparisonCriterion>())
            {
                switch (criterion)
                {
                    case EntityFeatureClassifier.ComparisonCriterion.Inertia:
                        parts.Add(string.Format(CultureInfo.InvariantCulture, "惯量=({0:0.###},{1:0.###},{2:0.###})", Inertia[0], Inertia[1], Inertia[2]));
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.Area:
                        parts.Add(string.Format(CultureInfo.InvariantCulture, "表面积={0:0.###}", Area));
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.Volume:
                        parts.Add(string.Format(CultureInfo.InvariantCulture, "体积={0:0.###}", Volume));
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.EdgeSum:
                        parts.Add(string.Format(CultureInfo.InvariantCulture, "边周长={0:0.###}", EdgeSum));
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.HolePosition:
                        parts.Add("孔位=" + HoleFeature.ToSummaryString(HoleFeatures));
                        break;
                }
            }

            parts.Add(string.Format(CultureInfo.InvariantCulture, "镜像侧={0}", mirrorSide ?? string.Empty));
            parts.Add(string.Format(CultureInfo.InvariantCulture, "镜像值={0:0.######}", MirrorScore));
            return string.Join("; ", parts);
        }

        public static bool WithinPercent(double a, double b, double tolerancePercent)
        {
            if (Math.Abs(a) < 1e-12 && Math.Abs(b) < 1e-12)
                return true;

            double scale = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-9);
            double limit = scale * tolerancePercent / 100.0;
            return Math.Abs(a - b) <= limit;
        }

        public static bool WithinTolerance(double a, double b, double tolerance, bool useAbsolute)
        {
            return useAbsolute ? WithinAbsolute(a, b, tolerance) : WithinPercent(a, b, tolerance);
        }

        public static bool WithinAbsolute(double a, double b, double absoluteTolerance)
        {
            return Math.Abs(a - b) <= Math.Abs(absoluteTolerance);
        }

        private static bool TryBuildFromBrep(Brep brep, List<EntityFeatureClassifier.ComparisonCriterion> activeCriteria, out ShapeData shapeData)
        {
            shapeData = null;
            if (brep == null)
                return false;

            AreaMassProperties areaProps = AreaMassProperties.Compute(brep, true, true, true, true);
            if (areaProps == null)
                return false;

            bool useVolumeMoments = activeCriteria != null && activeCriteria.Contains(EntityFeatureClassifier.ComparisonCriterion.Volume);
            VolumeMassProperties volumeProps = VolumeMassProperties.Compute(brep, true, true, true, true);
            double volume = volumeProps?.Volume ?? 0.0;

            double edgeSum = 0.0;
            foreach (BrepEdge edge in brep.Edges)
            {
                edgeSum += edge.GetLength();
            }

            List<Point3d> points = new List<Point3d>();
            Mesh[] meshes = Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh);
            if (meshes != null)
            {
                foreach (Mesh mesh in meshes)
                {
                    if (mesh == null)
                        continue;

                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        points.Add(mesh.Vertices.Point3dAt(i));
                    }
                }
            }

            AddEdgeSamplePoints(brep, points);

            if (points.Count < 4)
            {
                foreach (BrepVertex vertex in brep.Vertices)
                {
                    points.Add(vertex.Location);
                }
            }

            points = DeduplicateSamplePoints(brep, points);

            shapeData = new ShapeData
            {
                Area = areaProps.Area,
                Volume = volume,
                EdgeSum = edgeSum,
                SamplePoints = points
            };

            Point3d centroid;
            double[] inertia;
            Vector3d[] axes;
            bool gotRhinoMoments = useVolumeMoments
                ? TryGetVolumePrincipalInertia(volumeProps, out centroid, out inertia, out axes)
                : TryGetAreaPrincipalInertia(areaProps, out centroid, out inertia, out axes);

            if (gotRhinoMoments)
            {
                OrientAxesBySamplePoints(points, centroid, axes);
                shapeData.RhinoCentroid = centroid;
                shapeData.RhinoInertia = inertia;
                shapeData.RhinoAxes = axes;
                shapeData.HoleFeatures = ExtractHoleFeatures(brep, centroid, axes);
            }

            return true;
        }

        private static List<HoleFeature> ExtractHoleFeatures(Brep brep, Point3d centroid, Vector3d[] axes)
        {
            int holeCount = 0;
            double perimeterSum = 0.0;
            double perimeterSquareSum = 0.0;
            double inertiaU = 0.0;
            double inertiaV = 0.0;
            double inertiaW = 0.0;

            if (brep == null || axes == null || axes.Length < 3)
                return new List<HoleFeature>();

            foreach (BrepFace face in brep.Faces)
            {
                foreach (BrepLoop loop in face.Loops)
                {
                    if (loop.LoopType != BrepLoopType.Inner)
                        continue;

                    List<Point3d> loopPoints = new List<Point3d>();
                    double perimeter = 0.0;
                    foreach (BrepTrim trim in loop.Trims)
                    {
                        BrepEdge edge = trim.Edge;
                        if (edge == null)
                            continue;

                        double length = edge.GetLength();
                        if (length <= Rhino.RhinoMath.ZeroTolerance)
                            continue;

                        perimeter += length;
                        int sampleCount = Math.Max(4, (int)Math.Ceiling(length / Math.Max(perimeter / 16.0, Rhino.RhinoMath.ZeroTolerance)));
                        sampleCount = Math.Min(sampleCount, 32);
                        for (int i = 0; i <= sampleCount; i++)
                        {
                            double lengthAtPoint = length * i / sampleCount;
                            if (edge.LengthParameter(lengthAtPoint, out double parameter))
                                loopPoints.Add(edge.PointAt(parameter));
                        }
                    }

                    if (perimeter <= Rhino.RhinoMath.ZeroTolerance || loopPoints.Count == 0)
                        continue;

                    Point3d center = new Point3d(
                        loopPoints.Average(point => point.X),
                        loopPoints.Average(point => point.Y),
                        loopPoints.Average(point => point.Z));
                    Vector3d vector = center - centroid;
                    double u = vector * axes[0];
                    double v = vector * axes[1];
                    double w = vector * axes[2];

                    holeCount++;
                    perimeterSum += perimeter;
                    perimeterSquareSum += perimeter * perimeter;
                    inertiaU += perimeter * (v * v + w * w);
                    inertiaV += perimeter * (u * u + w * w);
                    inertiaW += perimeter * (u * u + v * v);
                }
            }

            if (holeCount == 0)
                return new List<HoleFeature>();

            return new List<HoleFeature>
            {
                new HoleFeature(
                    holeCount,
                    perimeterSum,
                    Math.Sqrt(perimeterSquareSum),
                    RadiusOfGyration(inertiaU, perimeterSum),
                    RadiusOfGyration(inertiaV, perimeterSum),
                    RadiusOfGyration(inertiaW, perimeterSum))
            };
        }

        private static double RadiusOfGyration(double inertia, double weight)
        {
            return weight <= Rhino.RhinoMath.ZeroTolerance ? 0.0 : Math.Sqrt(Math.Abs(inertia) / weight);
        }

        private static bool TryGetAreaPrincipalInertia(AreaMassProperties areaProps, out Point3d centroid, out double[] inertia, out Vector3d[] axes)
        {
            centroid = Point3d.Unset;
            inertia = null;
            axes = null;

            if (areaProps == null)
                return false;

            centroid = areaProps.Centroid;
            if (!areaProps.CentroidCoordinatesPrincipalMomentsOfInertia(
                out double i0, out Vector3d axis0,
                out double i1, out Vector3d axis1,
                out double i2, out Vector3d axis2))
                return false;

            return TryOrderPrincipalInertia(
                new[] { i0, i1, i2 },
                new[] { axis0, axis1, axis2 },
                out inertia,
                out axes);
        }

        private static bool TryGetVolumePrincipalInertia(VolumeMassProperties volumeProps, out Point3d centroid, out double[] inertia, out Vector3d[] axes)
        {
            centroid = Point3d.Unset;
            inertia = null;
            axes = null;

            if (volumeProps == null)
                return false;

            centroid = volumeProps.Centroid;
            if (!volumeProps.CentroidCoordinatesPrincipalMomentsOfInertia(
                out double i0, out Vector3d axis0,
                out double i1, out Vector3d axis1,
                out double i2, out Vector3d axis2))
                return false;

            return TryOrderPrincipalInertia(
                new[] { i0, i1, i2 },
                new[] { axis0, axis1, axis2 },
                out inertia,
                out axes);
        }

        private static bool TryOrderPrincipalInertia(double[] values, Vector3d[] rawAxes, out double[] inertia, out Vector3d[] axes)
        {
            inertia = null;
            axes = null;

            if (values == null || rawAxes == null || values.Length != 3 || rawAxes.Length != 3)
                return false;

            int[] order = Enumerable.Range(0, 3)
                .OrderByDescending(i => values[i])
                .ToArray();

            inertia = order.Select(i => values[i]).ToArray();
            axes = order.Select(i => Normalize(rawAxes[i])).ToArray();
            return axes.All(axis => axis.IsValid && axis.SquareLength > Rhino.RhinoMath.ZeroTolerance);
        }

        private static void AddEdgeSamplePoints(Brep brep, List<Point3d> points)
        {
            BoundingBox box = brep.GetBoundingBox(true);
            double diagonal = box.IsValid ? box.Diagonal.Length : 0.0;
            double sampleStep = Math.Max(diagonal / 100.0, Rhino.RhinoMath.ZeroTolerance);

            foreach (BrepEdge edge in brep.Edges)
            {
                if (edge == null)
                    continue;

                double length = edge.GetLength();
                if (length <= Rhino.RhinoMath.ZeroTolerance)
                    continue;

                int sampleCount = Math.Max(2, (int)Math.Ceiling(length / sampleStep));
                sampleCount = Math.Min(sampleCount, 64);

                for (int i = 0; i <= sampleCount; i++)
                {
                    double lengthAtPoint = length * i / sampleCount;
                    if (edge.LengthParameter(lengthAtPoint, out double parameter))
                        points.Add(edge.PointAt(parameter));
                }
            }
        }

        private static void ComputePointCloudInertiaAndMirror(List<Point3d> points, out double[] eigenValues, out double mirrorScore)
        {
            Point3d centroid = new Point3d(
                points.Average(p => p.X),
                points.Average(p => p.Y),
                points.Average(p => p.Z));

            double[,] covariance = new double[3, 3];
            foreach (Point3d point in points)
            {
                double x = point.X - centroid.X;
                double y = point.Y - centroid.Y;
                double z = point.Z - centroid.Z;

                covariance[0, 0] += x * x;
                covariance[0, 1] += x * y;
                covariance[0, 2] += x * z;
                covariance[1, 1] += y * y;
                covariance[1, 2] += y * z;
                covariance[2, 2] += z * z;
            }

            covariance[1, 0] = covariance[0, 1];
            covariance[2, 0] = covariance[0, 2];
            covariance[2, 1] = covariance[1, 2];

            Vector3d[] axes = new Vector3d[3];
            double[] solvedEigenValues;
            JacobiEigenSolver.SolveSymmetric(covariance, out solvedEigenValues, out axes);

            int[] order = Enumerable.Range(0, 3)
                .OrderByDescending(i => solvedEigenValues[i])
                .ToArray();

            eigenValues = order.Select(i => solvedEigenValues[i]).ToArray();
            axes = order.Select(i => axes[i]).ToArray();

            OrientAxesBySamplePoints(points, centroid, axes);
            mirrorScore = HasDegeneratePrincipalInertia(eigenValues) ? 0.0 : ComputeMirrorScore(points, centroid, axes);
        }

        private static List<Point3d> DeduplicateSamplePoints(Brep brep, List<Point3d> points)
        {
            if (points == null || points.Count <= 1)
                return points ?? new List<Point3d>();

            BoundingBox box = brep.GetBoundingBox(true);
            double diagonal = box.IsValid ? box.Diagonal.Length : 0.0;
            double tolerance = Math.Max(diagonal * SamplePointMergeToleranceRatio, Rhino.RhinoMath.ZeroTolerance * 10.0);

            HashSet<SamplePointKey> keys = new HashSet<SamplePointKey>();
            List<Point3d> uniquePoints = new List<Point3d>(points.Count);
            foreach (Point3d point in points)
            {
                SamplePointKey key = SamplePointKey.FromPoint(point, tolerance);
                if (keys.Add(key))
                    uniquePoints.Add(point);
            }

            return uniquePoints;
        }

        private static bool HasDegeneratePrincipalInertia(double[] inertia)
        {
            if (inertia == null || inertia.Length < 3)
                return true;

            for (int i = 0; i < inertia.Length - 1; i++)
            {
                double scale = Math.Max(Math.Max(Math.Abs(inertia[i]), Math.Abs(inertia[i + 1])), 1e-12);
                if (Math.Abs(inertia[i] - inertia[i + 1]) / scale <= PrincipalInertiaDegenerateTolerance)
                    return true;
            }

            return false;
        }

        private static void OrientAxesBySamplePoints(List<Point3d> points, Point3d centroid, Vector3d[] axes)
        {
            for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
            {
                double thirdMoment = 0.0;
                Vector3d axis = axes[axisIndex];
                foreach (Point3d point in points)
                {
                    Vector3d vector = point - centroid;
                    double coordinate = vector * axis;
                    thirdMoment += coordinate * coordinate * coordinate;
                }

                if (thirdMoment < 0.0)
                    axes[axisIndex] = -axes[axisIndex];
            }

            if (Vector3d.CrossProduct(axes[0], axes[1]) * axes[2] < 0.0)
                axes[2] = -axes[2];
        }

        private static double ComputeMirrorScore(List<Point3d> points, Point3d centroid, Vector3d[] axes)
        {
            double mirrorScore = 0.0;
            double mirrorScale = 0.0;
            foreach (Point3d point in points)
            {
                Vector3d vector = point - centroid;
                double x = vector * axes[0];
                double y = vector * axes[1];
                double z = vector * axes[2];
                double product = x * y * z;
                mirrorScore += product;
                mirrorScale += Math.Abs(product);
            }

            return mirrorScale <= Rhino.RhinoMath.ZeroTolerance ? 0.0 : mirrorScore / mirrorScale;
        }

        private static Vector3d Normalize(Vector3d vector)
        {
            if (!vector.Unitize())
                return Vector3d.Unset;

            return vector;
        }

        private struct SamplePointKey : IEquatable<SamplePointKey>
        {
            private readonly long _x;
            private readonly long _y;
            private readonly long _z;

            private SamplePointKey(long x, long y, long z)
            {
                _x = x;
                _y = y;
                _z = z;
            }

            public static SamplePointKey FromPoint(Point3d point, double tolerance)
            {
                return new SamplePointKey(
                    Quantize(point.X, tolerance),
                    Quantize(point.Y, tolerance),
                    Quantize(point.Z, tolerance));
            }

            public bool Equals(SamplePointKey other)
            {
                return _x == other._x && _y == other._y && _z == other._z;
            }

            public override bool Equals(object obj)
            {
                return obj is SamplePointKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + _x.GetHashCode();
                    hash = hash * 31 + _y.GetHashCode();
                    hash = hash * 31 + _z.GetHashCode();
                    return hash;
                }
            }

            private static long Quantize(double value, double tolerance)
            {
                return (long)Math.Round(value / tolerance, MidpointRounding.AwayFromZero);
            }
        }
    }

    internal sealed class HoleFeature
    {
        public HoleFeature(int count, double perimeterSum, double perimeterRootSum, double radiusU, double radiusV, double radiusW)
        {
            Count = count;
            PerimeterSum = perimeterSum;
            PerimeterRootSum = perimeterRootSum;
            RadiusU = radiusU;
            RadiusV = radiusV;
            RadiusW = radiusW;
        }

        public int Count { get; }

        public double PerimeterSum { get; }

        public double PerimeterRootSum { get; }

        public double RadiusU { get; }

        public double RadiusV { get; }

        public double RadiusW { get; }

        public static bool Matches(List<HoleFeature> a, List<HoleFeature> b, double absoluteTolerance)
        {
            a = a ?? new List<HoleFeature>();
            b = b ?? new List<HoleFeature>();
            if (a.Count != b.Count)
                return false;

            if (a.Count == 0)
                return true;

            absoluteTolerance = Math.Abs(absoluteTolerance);
            HoleFeature left = a[0];
            HoleFeature right = b[0];

            return left.Count == right.Count &&
                WithinAbsolute(left.PerimeterSum, right.PerimeterSum, absoluteTolerance) &&
                WithinAbsolute(left.PerimeterRootSum, right.PerimeterRootSum, absoluteTolerance) &&
                WithinAbsolute(left.RadiusU, right.RadiusU, absoluteTolerance) &&
                WithinAbsolute(left.RadiusV, right.RadiusV, absoluteTolerance) &&
                WithinAbsolute(left.RadiusW, right.RadiusW, absoluteTolerance);
        }

        private static bool WithinAbsolute(double a, double b, double absoluteTolerance)
        {
            return Math.Abs(a - b) <= absoluteTolerance;
        }

        public static string ToSummaryString(List<HoleFeature> holes)
        {
            if (holes == null || holes.Count == 0)
                return "无";

            HoleFeature hole = holes[0];
            return string.Format(
                CultureInfo.InvariantCulture,
                "数量={0}; 周长和={1:0.###}; 周长特征={2:0.###}; 孔回转半径=({3:0.###},{4:0.###},{5:0.###})",
                hole.Count,
                hole.PerimeterSum,
                hole.PerimeterRootSum,
                hole.RadiusU,
                hole.RadiusV,
                hole.RadiusW);
        }

        public static string ToStorageString(List<HoleFeature> holes)
        {
            if (holes == null || holes.Count == 0)
                return string.Empty;

            HoleFeature hole = holes[0];
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1:R},{2:R},{3:R},{4:R},{5:R}",
                hole.Count,
                hole.PerimeterSum,
                hole.PerimeterRootSum,
                hole.RadiusU,
                hole.RadiusV,
                hole.RadiusW);
        }

        public static List<HoleFeature> FromStorageString(string text)
        {
            List<HoleFeature> holes = new List<HoleFeature>();
            if (string.IsNullOrWhiteSpace(text))
                return holes;

            string[] values = text.Split(',');
            if (values.Length == 6)
            {
                if (int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) &&
                    double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double perimeterSum) &&
                    double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double perimeterRootSum) &&
                    double.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double radiusU) &&
                    double.TryParse(values[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double radiusV) &&
                    double.TryParse(values[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double radiusW))
                {
                    holes.Add(new HoleFeature(count, perimeterSum, perimeterRootSum, radiusU, radiusV, radiusW));
                }
            }

            return holes;
        }
    }

    internal sealed class ShapeData
    {
        public double Area { get; set; }

        public double Volume { get; set; }

        public double EdgeSum { get; set; }

        public List<Point3d> SamplePoints { get; set; } = new List<Point3d>();

        public List<HoleFeature> HoleFeatures { get; set; } = new List<HoleFeature>();

        public Point3d RhinoCentroid { get; set; }

        public double[] RhinoInertia { get; set; }

        public Vector3d[] RhinoAxes { get; set; }
    }

    internal static class JacobiEigenSolver
    {
        public static void SolveSymmetric(double[,] matrix, out double[] eigenValues, out Vector3d[] eigenVectors)
        {
            double[,] a = (double[,])matrix.Clone();
            double[,] v = new double[3, 3];
            for (int i = 0; i < 3; i++)
            {
                v[i, i] = 1.0;
            }

            for (int iteration = 0; iteration < 50; iteration++)
            {
                GetLargestOffDiagonal(a, out int p, out int q, out double max);
                if (max < 1e-12)
                    break;

                double theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
                double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                double c = 1.0 / Math.Sqrt(t * t + 1.0);
                double s = t * c;

                Rotate(a, v, p, q, c, s);
            }

            eigenValues = new[] { a[0, 0], a[1, 1], a[2, 2] };
            eigenVectors = new[]
            {
                Normalize(new Vector3d(v[0,0], v[1,0], v[2,0])),
                Normalize(new Vector3d(v[0,1], v[1,1], v[2,1])),
                Normalize(new Vector3d(v[0,2], v[1,2], v[2,2]))
            };
        }

        private static void GetLargestOffDiagonal(double[,] a, out int p, out int q, out double max)
        {
            p = 0;
            q = 1;
            max = Math.Abs(a[0, 1]);

            double value = Math.Abs(a[0, 2]);
            if (value > max)
            {
                p = 0;
                q = 2;
                max = value;
            }

            value = Math.Abs(a[1, 2]);
            if (value > max)
            {
                p = 1;
                q = 2;
                max = value;
            }
        }

        private static void Rotate(double[,] a, double[,] v, int p, int q, double c, double s)
        {
            double app = a[p, p];
            double aqq = a[q, q];
            double apq = a[p, q];

            a[p, p] = c * c * app - 2.0 * s * c * apq + s * s * aqq;
            a[q, q] = s * s * app + 2.0 * s * c * apq + c * c * aqq;
            a[p, q] = 0.0;
            a[q, p] = 0.0;

            for (int r = 0; r < 3; r++)
            {
                if (r == p || r == q)
                    continue;

                double arp = a[r, p];
                double arq = a[r, q];
                a[r, p] = c * arp - s * arq;
                a[p, r] = a[r, p];
                a[r, q] = s * arp + c * arq;
                a[q, r] = a[r, q];
            }

            for (int r = 0; r < 3; r++)
            {
                double vrp = v[r, p];
                double vrq = v[r, q];
                v[r, p] = c * vrp - s * vrq;
                v[r, q] = s * vrp + c * vrq;
            }
        }

        private static Vector3d Normalize(Vector3d vector)
        {
            if (!vector.Unitize())
                return Vector3d.XAxis;

            return vector;
        }
    }
}
