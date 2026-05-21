using CommonFunction;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
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
        private const int FeatureVersion = 1;

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
            { ComparisonCriterion.EdgeSum, false }
        };

        private readonly Dictionary<string, FeatureDbRecord> _lastSaveCandidates = new Dictionary<string, FeatureDbRecord>(StringComparer.Ordinal);
        private readonly List<EntityFeature> _lastComputedFeatures = new List<EntityFeature>();

        private bool _runRequested;
        private bool _saveRequested;

        public ButtonVisual RunButtonColor { get; set; } = ButtonVisual.Black;
        public ButtonVisual SaveButtonColor { get; set; } = ButtonVisual.Black;

        public EntityFeatureClassifier()
          : base("实体分类编号", "实体分类",
              "根据字符串和几何特征对实体分类、编号，并可保存到 SQLite 数据库",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("字符串列表", "Txt", "与实体一一对应的字符串列表", GH_ParamAccess.list);
            pManager.AddGeometryParameter("实体列表", "Geo", "待分类的实体列表", GH_ParamAccess.list);
            pManager.AddTextParameter("数据库地址", "DB", "SQLite 数据库文件地址，文件必须已存在", GH_ParamAccess.item);
            pManager.AddTextParameter("误差限度", "Tol", "百分比。单值用于所有已选条件，多值按所选条件顺序对应", GH_ParamAccess.item, "1");
            pManager.AddBooleanParameter("运行", "Run", "True 或点击按钮后执行分类", GH_ParamAccess.item, false);
            pManager.AddTextParameter("编号前缀", "Pre", "编号前缀", GH_ParamAccess.item, string.Empty);
            pManager.AddIntegerParameter("数字长度", "Len", "数字部分长度，例如 2 -> 01, 02", GH_ParamAccess.item, 2);
            pManager.AddTextParameter("连字符", "Sep", "连接前缀和数字部分的分隔符，可为空", GH_ParamAccess.item, "-");
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("编号", "No", "与输入实体一一对应的编号", GH_ParamAccess.list);
            pManager.AddIntegerParameter("基础号", "Base", "不含镜像后缀的基础数字编号", GH_ParamAccess.list);
            pManager.AddTextParameter("镜像侧", "Side", "镜像方向标识：A、B 或空", GH_ParamAccess.list);
            pManager.AddTextParameter("特征摘要", "Feature", "实体特征摘要", GH_ParamAccess.list);
            pManager.AddTextParameter("状态", "Msg", "运行状态信息", GH_ParamAccess.item);
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
        }

        private void AppendCriterionMenuItem(ToolStripDropDown menu, ComparisonCriterion criterion, string text)
        {
            ToolStripMenuItem item = Menu_AppendItem(menu, text, ToggleCriterionClicked, true, _criterionStates[criterion]);
            item.Tag = criterion;
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

            List<string> names = new List<string>();
            if (!DA.GetDataList(0, names))
                return;

            List<GeometryBase> geometries = new List<GeometryBase>();
            if (!DA.GetDataList(1, geometries))
                return;

            string databasePath = string.Empty;
            if (!DA.GetData(2, ref databasePath))
                return;

            string toleranceText = "1";
            DA.GetData(3, ref toleranceText);

            bool runInput = false;
            DA.GetData(4, ref runInput);

            string prefix = string.Empty;
            DA.GetData(5, ref prefix);

            int numberLength = 2;
            DA.GetData(6, ref numberLength);

            string separator = "-";
            DA.GetData(7, ref separator);

            bool shouldRun = runInput || _runRequested || _saveRequested;
            if (!shouldRun)
            {
                DA.SetData(4, "等待运行。");
                return;
            }

            _runRequested = false;

            List<string> numbers = new List<string>();
            List<int> baseNumbers = new List<int>();
            List<string> mirrorSides = new List<string>();
            List<string> featureSummaries = new List<string>();
            string statusMessage = string.Empty;

            try
            {
                ValidateInputs(names, geometries, databasePath, numberLength);

                List<ComparisonCriterion> activeCriteria = GetActiveCriteria();
                double[] tolerances = ParseTolerances(toleranceText, activeCriteria.Count);

                SQLiteFeatureStore store = new SQLiteFeatureStore(databasePath);
                store.EnsureSchema();
                List<FeatureDbRecord> databaseRecords = store.LoadRecords();

                List<EntityFeature> features = BuildFeatures(names, geometries, activeCriteria);
                _lastComputedFeatures.Clear();
                _lastComputedFeatures.AddRange(features);

                ClassificationResult result = Classify(features, activeCriteria, tolerances, databaseRecords);

                for (int i = 0; i < features.Count; i++)
                {
                    EntityFeature feature = features[i];
                    ClassificationItem item = result.Items[i];
                    string finalSide = item.UseSuffix ? item.NormalizedMirrorCode : string.Empty;
                    string finalNumber = FormatNumber(prefix, separator, item.BaseNumber, numberLength, finalSide);

                    numbers.Add(finalNumber);
                    baseNumbers.Add(item.BaseNumber);
                    mirrorSides.Add(finalSide);
                    featureSummaries.Add(feature.ToSummaryString());
                }

                if (_saveRequested)
                {
                    int savedCount = store.SaveRecords(result.SaveCandidates.Values.ToList());
                    statusMessage = $"分类完成，共 {features.Count} 个实体；保存 {savedCount} 条特征记录。";
                    _lastSaveCandidates.Clear();
                    foreach (KeyValuePair<string, FeatureDbRecord> pair in result.SaveCandidates)
                    {
                        _lastSaveCandidates[pair.Key] = pair.Value;
                    }
                }
                else
                {
                    statusMessage = $"分类完成，共 {features.Count} 个实体；命中数据库基础类 {result.DatabaseMatchedBaseNumbers.Count} 个。";
                    _lastSaveCandidates.Clear();
                    foreach (KeyValuePair<string, FeatureDbRecord> pair in result.SaveCandidates)
                    {
                        _lastSaveCandidates[pair.Key] = pair.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                statusMessage = ex.Message;
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

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter chunk = writer.CreateChunk(SettingsChunk);
            chunk.SetBoolean(nameof(ComparisonCriterion.Inertia), _criterionStates[ComparisonCriterion.Inertia]);
            chunk.SetBoolean(nameof(ComparisonCriterion.Area), _criterionStates[ComparisonCriterion.Area]);
            chunk.SetBoolean(nameof(ComparisonCriterion.Volume), _criterionStates[ComparisonCriterion.Volume]);
            chunk.SetBoolean(nameof(ComparisonCriterion.EdgeSum), _criterionStates[ComparisonCriterion.EdgeSum]);
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

        public string GetConditionDisplayText()
        {
            List<string> text = new List<string> { "字符串" };
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
                }
            }

            text.Add("镜像区分");
            return string.Join(" + ", text);
        }

        protected override Bitmap Icon => GeneratedIcon.Get("gen_EntityFeatureClassifier");

        public override Guid ComponentGuid => new Guid("F1F780F3-7A3D-44B8-92CC-31B91536E6A0");

        private static void ValidateInputs(List<string> names, List<GeometryBase> geometries, string databasePath, int numberLength)
        {
            if (names == null || names.Count == 0)
                throw new ArgumentException("字符串列表不能为空。");

            if (geometries == null || geometries.Count == 0)
                throw new ArgumentException("实体列表不能为空。");

            if (names.Count != geometries.Count)
                throw new ArgumentException("字符串列表和实体列表数量必须一致。");

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
            return criteria;
        }

        private static double[] ParseTolerances(string text, int activeCriterionCount)
        {
            if (activeCriterionCount == 0)
                return Array.Empty<double>();

            string[] parts = (text ?? string.Empty)
                .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                throw new ArgumentException("误差限度不能为空。");

            if (parts.Length != 1 && parts.Length != activeCriterionCount)
                throw new ArgumentException("误差限度数量必须为 1，或与当前所选几何条件数一致。");

            List<double> values = new List<double>();
            foreach (string part in parts)
            {
                if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                        throw new ArgumentException("误差限度包含无法识别的数字。");
                }

                if (value < 0)
                    throw new ArgumentException("误差限度不能为负数。");

                values.Add(value);
            }

            if (values.Count == 1)
                return Enumerable.Repeat(values[0], activeCriterionCount).ToArray();

            return values.ToArray();
        }

        private static List<EntityFeature> BuildFeatures(List<string> names, List<GeometryBase> geometries, List<ComparisonCriterion> activeCriteria)
        {
            List<EntityFeature> features = new List<EntityFeature>(geometries.Count);
            for (int i = 0; i < geometries.Count; i++)
            {
                if (geometries[i] == null)
                    throw new ArgumentException($"第 {i + 1} 个实体为空。");

                EntityFeature feature = EntityFeature.Create(names[i], geometries[i], activeCriteria, i);
                features.Add(feature);
            }

            return features;
        }

        private static ClassificationResult Classify(List<EntityFeature> features, List<ComparisonCriterion> activeCriteria, double[] tolerances, List<FeatureDbRecord> databaseRecords)
        {
            ClassificationResult result = new ClassificationResult(features.Count);
            Dictionary<int, HashSet<string>> databaseSidesByBase = databaseRecords
                .GroupBy(x => x.BaseNumber)
                .ToDictionary(
                    x => x.Key,
                    x => new HashSet<string>(x.Select(y => y.MirrorCode).Where(y => !string.IsNullOrWhiteSpace(y)), StringComparer.Ordinal));

            List<WorkingBaseGroup> groups = new List<WorkingBaseGroup>();
            int nextBaseNumber = databaseRecords.Count == 0 ? 1 : databaseRecords.Max(x => x.BaseNumber) + 1;

            for (int i = 0; i < features.Count; i++)
            {
                EntityFeature feature = features[i];
                WorkingBaseGroup localGroup = groups.FirstOrDefault(x => x.CanAccept(feature, activeCriteria, tolerances));
                if (localGroup == null)
                {
                    int baseNumber = FindMatchingDatabaseBaseNumber(feature, activeCriteria, tolerances, databaseRecords);
                    bool matchedDatabase = baseNumber > 0;
                    if (!matchedDatabase)
                    {
                        baseNumber = nextBaseNumber;
                        nextBaseNumber++;
                    }
                    else
                    {
                        result.DatabaseMatchedBaseNumbers.Add(baseNumber);
                    }

                    localGroup = new WorkingBaseGroup(feature, baseNumber, matchedDatabase);
                    groups.Add(localGroup);
                }

                localGroup.Add(feature.Index);
            }

            foreach (WorkingBaseGroup group in groups)
            {
                HashSet<string> currentSides = new HashSet<string>(
                    group.MemberIndexes
                        .Select(index => features[index].NormalizedMirrorCode)
                        .Where(side => !string.IsNullOrWhiteSpace(side)),
                    StringComparer.Ordinal);

                HashSet<string> databaseSides = databaseSidesByBase.TryGetValue(group.BaseNumber, out HashSet<string> sideSet)
                    ? sideSet
                    : new HashSet<string>(StringComparer.Ordinal);

                bool useSuffix = currentSides.Count >= 2 || databaseSides.Count >= 2;

                foreach (int index in group.MemberIndexes)
                {
                    result.Items[index] = new ClassificationItem
                    {
                        BaseNumber = group.BaseNumber,
                        NormalizedMirrorCode = features[index].NormalizedMirrorCode,
                        UseSuffix = useSuffix
                    };
                }

                foreach (IGrouping<string, EntityFeature> orientationGroup in group.MemberIndexes
                    .Select(index => features[index])
                    .GroupBy(x => x.NormalizedMirrorCode, StringComparer.Ordinal))
                {
                    EntityFeature representative = orientationGroup.First();
                    string storeMirrorCode = representative.NormalizedMirrorCode;
                    string saveKey = FeatureDbRecord.BuildKey(group.BaseNumber, representative.NameText, storeMirrorCode, representative);

                    if (!result.SaveCandidates.ContainsKey(saveKey))
                    {
                        result.SaveCandidates.Add(saveKey, FeatureDbRecord.FromFeature(group.BaseNumber, storeMirrorCode, representative));
                    }
                }
            }

            return result;
        }

        private static int FindMatchingDatabaseBaseNumber(EntityFeature feature, List<ComparisonCriterion> activeCriteria, double[] tolerances, List<FeatureDbRecord> databaseRecords)
        {
            FeatureDbRecord match = databaseRecords.FirstOrDefault(record => record.MatchesBase(feature, activeCriteria, tolerances));
            return match?.BaseNumber ?? -1;
        }

        private static string FormatNumber(string prefix, string separator, int baseNumber, int numberLength, string suffix)
        {
            string numeric = baseNumber.ToString().PadLeft(numberLength, '0');
            string head = string.IsNullOrEmpty(prefix) ? numeric : prefix + (separator ?? string.Empty) + numeric;
            return string.IsNullOrEmpty(suffix) ? head : head + suffix;
        }

        internal enum ComparisonCriterion
        {
            Inertia,
            Area,
            Volume,
            EdgeSum
        }
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

        public HashSet<int> DatabaseMatchedBaseNumbers { get; } = new HashSet<int>();
    }

    internal sealed class ClassificationItem
    {
        public int BaseNumber { get; set; }

        public string NormalizedMirrorCode { get; set; } = string.Empty;

        public bool UseSuffix { get; set; }
    }

    internal sealed class WorkingBaseGroup
    {
        public WorkingBaseGroup(EntityFeature representative, int baseNumber, bool matchedDatabase)
        {
            Representative = representative;
            BaseNumber = baseNumber;
            MatchedDatabase = matchedDatabase;
        }

        public EntityFeature Representative { get; }

        public int BaseNumber { get; }

        public bool MatchedDatabase { get; }

        public List<int> MemberIndexes { get; } = new List<int>();

        public void Add(int index)
        {
            MemberIndexes.Add(index);
        }

        public bool CanAccept(EntityFeature feature, List<EntityFeatureClassifier.ComparisonCriterion> activeCriteria, double[] tolerances)
        {
            return Representative.MatchesBase(feature, activeCriteria, tolerances);
        }
    }

    internal sealed class FeatureDbRecord
    {
        public int BaseNumber { get; set; }

        public string NameText { get; set; } = string.Empty;

        public double Area { get; set; }

        public double Volume { get; set; }

        public double EdgeSum { get; set; }

        public double Inertia1 { get; set; }

        public double Inertia2 { get; set; }

        public double Inertia3 { get; set; }

        public string MirrorCode { get; set; } = string.Empty;

        public double MirrorScore { get; set; }

        public static FeatureDbRecord FromFeature(int baseNumber, string mirrorCode, EntityFeature feature)
        {
            return new FeatureDbRecord
            {
                BaseNumber = baseNumber,
                NameText = feature.NameText,
                Area = feature.Area,
                Volume = feature.Volume,
                EdgeSum = feature.EdgeSum,
                Inertia1 = feature.Inertia[0],
                Inertia2 = feature.Inertia[1],
                Inertia3 = feature.Inertia[2],
                MirrorCode = mirrorCode ?? string.Empty,
                MirrorScore = feature.MirrorScore
            };
        }

        public bool MatchesBase(EntityFeature feature, List<EntityFeatureClassifier.ComparisonCriterion> activeCriteria, double[] tolerances)
        {
            if (!string.Equals(NameText, feature.NameText, StringComparison.Ordinal))
                return false;

            for (int i = 0; i < activeCriteria.Count; i++)
            {
                double tolerance = tolerances[i];
                switch (activeCriteria[i])
                {
                    case EntityFeatureClassifier.ComparisonCriterion.Inertia:
                        if (!EntityFeature.WithinPercent(Inertia1, feature.Inertia[0], tolerance) ||
                            !EntityFeature.WithinPercent(Inertia2, feature.Inertia[1], tolerance) ||
                            !EntityFeature.WithinPercent(Inertia3, feature.Inertia[2], tolerance))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.Area:
                        if (!EntityFeature.WithinPercent(Area, feature.Area, tolerance))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.Volume:
                        if (!EntityFeature.WithinPercent(Volume, feature.Volume, tolerance))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.EdgeSum:
                        if (!EntityFeature.WithinPercent(EdgeSum, feature.EdgeSum, tolerance))
                            return false;
                        break;
                }
            }

            return true;
        }

        public static string BuildKey(int baseNumber, string nameText, string mirrorCode, EntityFeature feature)
        {
            return string.Join("|",
                baseNumber.ToString(CultureInfo.InvariantCulture),
                nameText ?? string.Empty,
                mirrorCode ?? string.Empty,
                feature.Volume.ToString("R", CultureInfo.InvariantCulture),
                feature.Area.ToString("R", CultureInfo.InvariantCulture),
                feature.EdgeSum.ToString("R", CultureInfo.InvariantCulture),
                feature.Inertia[0].ToString("R", CultureInfo.InvariantCulture),
                feature.Inertia[1].ToString("R", CultureInfo.InvariantCulture),
                feature.Inertia[2].ToString("R", CultureInfo.InvariantCulture));
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
                        name_text TEXT NOT NULL,
                        base_number INTEGER NOT NULL,
                        area REAL NOT NULL,
                        volume REAL NOT NULL,
                        edge_sum REAL NOT NULL,
                        inertia_1 REAL NOT NULL,
                        inertia_2 REAL NOT NULL,
                        inertia_3 REAL NOT NULL,
                        mirror_code TEXT NOT NULL,
                        mirror_score REAL NOT NULL,
                        feature_version INTEGER NOT NULL,
                        created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );
                    CREATE INDEX IF NOT EXISTS idx_entity_feature_classes_name_number
                    ON entity_feature_classes(name_text, base_number);";
                command.ExecuteNonQuery();
            }
        }

        public List<FeatureDbRecord> LoadRecords()
        {
            List<FeatureDbRecord> records = new List<FeatureDbRecord>();
            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    @"SELECT base_number, name_text, area, volume, edge_sum, inertia_1, inertia_2, inertia_3, mirror_code, mirror_score
                      FROM entity_feature_classes
                      WHERE feature_version = @feature_version;";
                command.Parameters.AddWithValue("@feature_version", 1);

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new FeatureDbRecord
                        {
                            BaseNumber = reader.GetInt32(0),
                            NameText = reader.GetString(1),
                            Area = reader.GetDouble(2),
                            Volume = reader.GetDouble(3),
                            EdgeSum = reader.GetDouble(4),
                            Inertia1 = reader.GetDouble(5),
                            Inertia2 = reader.GetDouble(6),
                            Inertia3 = reader.GetDouble(7),
                            MirrorCode = reader.GetString(8),
                            MirrorScore = reader.GetDouble(9)
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
                              (name_text, base_number, area, volume, edge_sum, inertia_1, inertia_2, inertia_3, mirror_code, mirror_score, feature_version)
                              VALUES
                              (@name_text, @base_number, @area, @volume, @edge_sum, @inertia_1, @inertia_2, @inertia_3, @mirror_code, @mirror_score, @feature_version);";
                        command.Parameters.AddWithValue("@name_text", record.NameText);
                        command.Parameters.AddWithValue("@base_number", record.BaseNumber);
                        command.Parameters.AddWithValue("@area", record.Area);
                        command.Parameters.AddWithValue("@volume", record.Volume);
                        command.Parameters.AddWithValue("@edge_sum", record.EdgeSum);
                        command.Parameters.AddWithValue("@inertia_1", record.Inertia1);
                        command.Parameters.AddWithValue("@inertia_2", record.Inertia2);
                        command.Parameters.AddWithValue("@inertia_3", record.Inertia3);
                        command.Parameters.AddWithValue("@mirror_code", record.MirrorCode ?? string.Empty);
                        command.Parameters.AddWithValue("@mirror_score", record.MirrorScore);
                        command.Parameters.AddWithValue("@feature_version", 1);
                        inserted += command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            return inserted;
        }

        private bool RecordExists(SQLiteConnection connection, SQLiteTransaction transaction, FeatureDbRecord record)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    @"SELECT COUNT(1)
                      FROM entity_feature_classes
                      WHERE name_text = @name_text
                        AND base_number = @base_number
                        AND mirror_code = @mirror_code
                        AND feature_version = @feature_version
                        AND ABS(area - @area) < 1e-9
                        AND ABS(volume - @volume) < 1e-9
                        AND ABS(edge_sum - @edge_sum) < 1e-9
                        AND ABS(inertia_1 - @inertia_1) < 1e-9
                        AND ABS(inertia_2 - @inertia_2) < 1e-9
                        AND ABS(inertia_3 - @inertia_3) < 1e-9;";
                command.Parameters.AddWithValue("@name_text", record.NameText);
                command.Parameters.AddWithValue("@base_number", record.BaseNumber);
                command.Parameters.AddWithValue("@mirror_code", record.MirrorCode ?? string.Empty);
                command.Parameters.AddWithValue("@area", record.Area);
                command.Parameters.AddWithValue("@volume", record.Volume);
                command.Parameters.AddWithValue("@edge_sum", record.EdgeSum);
                command.Parameters.AddWithValue("@inertia_1", record.Inertia1);
                command.Parameters.AddWithValue("@inertia_2", record.Inertia2);
                command.Parameters.AddWithValue("@inertia_3", record.Inertia3);
                command.Parameters.AddWithValue("@feature_version", 1);

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

        private EntityFeature()
        {
        }

        public int Index { get; private set; }

        public string NameText { get; private set; } = string.Empty;

        public double Area { get; private set; }

        public double Volume { get; private set; }

        public double EdgeSum { get; private set; }

        public double[] Inertia { get; private set; } = new double[3];

        public double MirrorScore { get; private set; }

        public string NormalizedMirrorCode { get; private set; } = string.Empty;

        public static EntityFeature Create(string nameText, GeometryBase geometry, List<EntityFeatureClassifier.ComparisonCriterion> activeCriteria, int index)
        {
            EntityFeature feature = new EntityFeature
            {
                Index = index,
                NameText = nameText ?? string.Empty
            };

            if (!TryGetShapeData(geometry, out ShapeData shapeData))
                throw new ArgumentException($"第 {index + 1} 个实体类型暂不支持，当前仅支持 Brep、Extrusion、Surface 与 Mesh。");

            feature.Area = shapeData.Area;
            feature.Volume = shapeData.Volume;
            feature.EdgeSum = shapeData.EdgeSum;

            List<Point3d> samplePoints = shapeData.SamplePoints;
            if (samplePoints.Count < 4)
                throw new ArgumentException($"第 {index + 1} 个实体可用于分析的采样点不足。");

            ComputeInertiaAndMirror(samplePoints, out double[] inertia, out double mirrorScore);
            feature.Inertia = inertia;
            feature.MirrorScore = mirrorScore;
            feature.NormalizedMirrorCode = Math.Abs(mirrorScore) < MirrorEpsilon ? string.Empty : (mirrorScore >= 0.0 ? "A" : "B");

            if (activeCriteria.Contains(EntityFeatureClassifier.ComparisonCriterion.Volume) && feature.Volume <= 0.0)
                throw new ArgumentException($"第 {index + 1} 个实体无法计算有效体积，请输入封闭实体或取消“体积”条件。");

            return feature;
        }

        public bool MatchesBase(EntityFeature other, List<EntityFeatureClassifier.ComparisonCriterion> activeCriteria, double[] tolerances)
        {
            if (!string.Equals(NameText, other.NameText, StringComparison.Ordinal))
                return false;

            for (int i = 0; i < activeCriteria.Count; i++)
            {
                double tolerance = tolerances[i];
                switch (activeCriteria[i])
                {
                    case EntityFeatureClassifier.ComparisonCriterion.Inertia:
                        if (!WithinPercent(Inertia[0], other.Inertia[0], tolerance) ||
                            !WithinPercent(Inertia[1], other.Inertia[1], tolerance) ||
                            !WithinPercent(Inertia[2], other.Inertia[2], tolerance))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.Area:
                        if (!WithinPercent(Area, other.Area, tolerance))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.Volume:
                        if (!WithinPercent(Volume, other.Volume, tolerance))
                            return false;
                        break;

                    case EntityFeatureClassifier.ComparisonCriterion.EdgeSum:
                        if (!WithinPercent(EdgeSum, other.EdgeSum, tolerance))
                            return false;
                        break;
                }
            }

            return true;
        }

        public string ToSummaryString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "文本={0}; 体积={1:0.###}; 表面积={2:0.###}; 边周长={3:0.###}; 惯量=({4:0.###},{5:0.###},{6:0.###}); 镜像值={7:0.######}",
                NameText,
                Volume,
                Area,
                EdgeSum,
                Inertia[0],
                Inertia[1],
                Inertia[2],
                MirrorScore);
        }

        public static bool WithinPercent(double a, double b, double tolerancePercent)
        {
            if (Math.Abs(a) < 1e-12 && Math.Abs(b) < 1e-12)
                return true;

            double scale = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-9);
            double limit = scale * tolerancePercent / 100.0;
            return Math.Abs(a - b) <= limit;
        }

        private static bool TryGetShapeData(GeometryBase geometry, out ShapeData shapeData)
        {
            if (geometry is Brep brep)
            {
                return TryBuildFromBrep(brep, out shapeData);
            }

            if (geometry is Extrusion extrusion)
            {
                return TryBuildFromBrep(extrusion.ToBrep(), out shapeData);
            }

            if (geometry is Surface surface)
            {
                return TryBuildFromBrep(surface.ToBrep(), out shapeData);
            }

            if (geometry is Mesh mesh)
            {
                return TryBuildFromMesh(mesh, out shapeData);
            }

            shapeData = null;
            return false;
        }

        private static bool TryBuildFromBrep(Brep brep, out ShapeData shapeData)
        {
            shapeData = null;
            if (brep == null)
                return false;

            AreaMassProperties areaProps = AreaMassProperties.Compute(brep);
            if (areaProps == null)
                return false;

            VolumeMassProperties volumeProps = VolumeMassProperties.Compute(brep);
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

            if (points.Count < 4)
            {
                foreach (BrepVertex vertex in brep.Vertices)
                {
                    points.Add(vertex.Location);
                }
            }

            shapeData = new ShapeData
            {
                Area = areaProps.Area,
                Volume = volume,
                EdgeSum = edgeSum,
                SamplePoints = points
            };
            return true;
        }

        private static bool TryBuildFromMesh(Mesh mesh, out ShapeData shapeData)
        {
            shapeData = null;
            if (mesh == null)
                return false;

            Mesh working = mesh.DuplicateMesh();
            working.Normals.ComputeNormals();
            working.Compact();

            AreaMassProperties areaProps = AreaMassProperties.Compute(working);
            if (areaProps == null)
                return false;

            VolumeMassProperties volumeProps = working.IsClosed ? VolumeMassProperties.Compute(working) : null;

            HashSet<string> edgeKeys = new HashSet<string>(StringComparer.Ordinal);
            double edgeSum = 0.0;
            for (int i = 0; i < working.TopologyEdges.Count; i++)
            {
                var pair = working.TopologyEdges.GetTopologyVertices(i);
                int a = Math.Min(pair.I, pair.J);
                int b = Math.Max(pair.I, pair.J);
                string key = a.ToString(CultureInfo.InvariantCulture) + "_" + b.ToString(CultureInfo.InvariantCulture);
                if (!edgeKeys.Add(key))
                    continue;

                Point3d p0 = working.TopologyVertices[pair.I];
                Point3d p1 = working.TopologyVertices[pair.J];
                edgeSum += p0.DistanceTo(p1);
            }

            List<Point3d> points = new List<Point3d>(working.Vertices.Count);
            for (int i = 0; i < working.Vertices.Count; i++)
            {
                points.Add(working.Vertices.Point3dAt(i));
            }

            shapeData = new ShapeData
            {
                Area = areaProps.Area,
                Volume = volumeProps?.Volume ?? 0.0,
                EdgeSum = edgeSum,
                SamplePoints = points
            };
            return true;
        }

        private static void ComputeInertiaAndMirror(List<Point3d> points, out double[] eigenValues, out double mirrorScore)
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

            mirrorScore = 0.0;
            foreach (Point3d point in points)
            {
                Vector3d vector = point - centroid;
                double x = vector * axes[0];
                double y = vector * axes[1];
                double z = vector * axes[2];
                mirrorScore += x * y * z;
            }

            mirrorScore /= points.Count;
        }
    }

    internal sealed class ShapeData
    {
        public double Area { get; set; }

        public double Volume { get; set; }

        public double EdgeSum { get; set; }

        public List<Point3d> SamplePoints { get; set; } = new List<Point3d>();
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
