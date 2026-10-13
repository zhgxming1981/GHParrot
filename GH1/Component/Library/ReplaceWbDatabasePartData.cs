using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>用输入 Brep 的最新特征替换 WB 分类库中已有编号的数据。</summary>
    public sealed class ReplaceWbDatabasePartData : GH_Component
    {
        private bool _lastExecute;
        private bool _buttonPulse;
        private bool _lastReplaced;
        private string _lastMessage = "等待修改触发。";

        public ReplaceWbDatabasePartData()
            : base("修改分类库实体属性（WB）", "修改实体属性_WB",
                "以输入实体重新提取特征，并替换 WB 分类库中对应编号的几何、孔、外轮廓和完整特征。材质/颜色为空时保留原值。替换后若与其他编号特征相同，后续分类可能出现多候选。编号与实体必须严格一一对应；执行前会要求确认。",
                "Parrot", "Library")
        {
        }

        public override void CreateAttributes()
        {
            m_attributes = new ReplaceWbDatabasePartDataAttributes(this);
        }

        public override Guid ComponentGuid => new Guid("4E916FC2-58DA-4D7C-A993-1BB6E3F0B4AC");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("数据库路径", "数据库", "WB 分类库 SQLite 文件路径。", GH_ParamAccess.item);
            p.AddTextParameter("编号", "编号", "要修改的已有完整编号；多个编号时必须与 Brep 一一对应。", GH_ParamAccess.list);
            p.AddBrepParameter("Brep", "Brep", "用于更新数据库特征的 Brep；必须与编号一一对应。", GH_ParamAccess.list);
            p.AddTextParameter("材质", "材质", "与编号一一对应；对应项为空时保留数据库原材质。", GH_ParamAccess.list);
            p.AddTextParameter("颜色", "颜色", "与编号一一对应；对应项为空时保留数据库原颜色。", GH_ParamAccess.list);
            p.AddBooleanParameter("修改", "修改", "False→True 时弹出确认，确认后修改；也可点击电池底部的修改按钮。", GH_ParamAccess.item, false);
            p[3].Optional = true;
            p[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddBooleanParameter("修改成功", "成功", "最近一次操作是否已成功修改全部指定编号的数据。", GH_ParamAccess.item);
            p.AddTextParameter("结果", "结果", "替换结果、取消信息或错误信息。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string database = string.Empty;
            var numbers = new List<string>();
            var breps = new List<Brep>();
            var materials = new List<string>();
            var colors = new List<string>();
            bool execute = false;
            da.GetData(0, ref database);
            da.GetDataList(1, numbers);
            da.GetDataList(2, breps);
            da.GetDataList(3, materials);
            da.GetDataList(4, colors);
            da.GetData(5, ref execute);

            bool fromButton = _buttonPulse;
            _buttonPulse = false;
            bool rising = fromButton || (execute && !_lastExecute);
            _lastExecute = execute;
            if (!rising)
            {
                da.SetData(0, _lastReplaced);
                da.SetData(1, _lastMessage);
                return;
            }

            _lastReplaced = false;
            try
            {
                database = (database ?? string.Empty).Trim();
                ValidateInput(database, numbers, breps, materials, colors);
                // 后续预检、确认显示和写库统一使用已清理的完整编号。
                for (int i = 0; i < numbers.Count; i++)
                    numbers[i] = numbers[i].Trim();
                if (!WbMigrationStore.TryValidateWbDatabase(database, out string databaseMessage))
                    throw new InvalidOperationException(databaseMessage);
                if (!WbMigrationStore.TryValidateReplacementTargets(database, numbers, out string targetMessage))
                    throw new InvalidOperationException(targetMessage);

                WbClassificationSettings settings = LoadDatabaseSettings(database);
                RhinoDoc activeDoc = RhinoDoc.ActiveDoc;
                double curveJoinTolerance = activeDoc == null
                    ? settings.FallbackTolerance
                    : activeDoc.ModelAbsoluteTolerance;
                bool curveJoinUsesFallback = activeDoc == null;
                var records = new List<WbMigrationRecord>();
                for (int i = 0; i < breps.Count; i++)
                {
                    Brep copy = breps[i].DuplicateBrep();
                    try
                    {
                        WbFeature feature = WbFeature.Create(TextAt(materials, i), TextAt(colors, i), copy,
                            settings.InertiaDegenerateRatio, settings.FallbackTolerance, i,
                            curveJoinTolerance, curveJoinUsesFallback);
                        records.Add(new WbMigrationRecord(i, numbers[i], feature, string.Empty));
                    }
                    finally
                    {
                        copy.Dispose();
                    }
                }

                List<string> duplicateLines = FindDuplicateMatches(database, records, settings);
                string duplicateWarning = duplicateLines.Count == 0
                    ? string.Empty
                    : "\n\n注意：按当前数据库误差设置，替换后将与下列其它编号匹配：\n" +
                      FormatLinesForConfirmation(duplicateLines) +
                      "\n继续替换会使后续分类出现多候选。";

                DialogResult confirmation = MessageBox.Show(
                    "确定要用输入实体的数据替换分类数据库中的 " + records.Count + " 个编号吗？\n\n" +
                    "数据库：" + Path.GetFullPath(database) + "\n\n" +
                    FormatNumbersForConfirmation(numbers) + "\n\n" +
                    "将更新几何、孔、外轮廓及完整特征；材质/颜色的空输入保持原值。" +
                    duplicateWarning + "\n\n" +
                    "此操作不可自动撤销。",
                    "确认修改分类库实体属性（WB）",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes)
                {
                    _lastMessage = "已取消替换 " + records.Count + " 个编号。";
                    da.SetData(0, false);
                    da.SetData(1, _lastMessage);
                    return;
                }

                // 预检填入的旧值仅用于比较；空输入交由写入事务读取并保留最新原值。
                for (int i = 0; i < records.Count; i++)
                {
                    records[i].Feature.Material = TextAt(materials, i);
                    records[i].Feature.Color = TextAt(colors, i);
                }
                WbMigrationStore.ReplacePartsBatch(database, records);
                _lastReplaced = true;
                _lastMessage = "已替换 " + records.Count + " 个编号的数据：" + string.Join("、", numbers);
            }
            catch (Exception ex)
            {
                _lastMessage = "替换失败：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastMessage);
            }

            da.SetData(0, _lastReplaced);
            da.SetData(1, _lastMessage);
        }

        private static void ValidateInput(string database, List<string> numbers, List<Brep> breps,
            List<string> materials, List<string> colors)
        {
            if (string.IsNullOrWhiteSpace(database))
                throw new ArgumentException("数据库地址不能为空。");
            if (!File.Exists(database))
                throw new FileNotFoundException("数据库文件不存在：" + database);
            if (numbers.Count == 0)
                throw new ArgumentException("编号列表不能为空。");
            if (numbers.Count != breps.Count)
                throw new ArgumentException("编号列表与实体列表数量不一致，未执行替换。");
            if (materials.Count != 0 && materials.Count != numbers.Count)
                throw new ArgumentException("材质列表为空或必须与编号列表数量一致。");
            if (colors.Count != 0 && colors.Count != numbers.Count)
                throw new ArgumentException("颜色列表为空或必须与编号列表数量一致。");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < numbers.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(numbers[i]))
                    throw new ArgumentException("编号列表第 " + (i + 1) + " 项为空。");
                if (!seen.Add(numbers[i].Trim()))
                    throw new ArgumentException("编号列表包含重复编号：" + numbers[i].Trim());
                if (breps[i] == null)
                    throw new ArgumentException("实体列表第 " + (i + 1) + " 项为空。");
                if (!breps[i].IsValid)
                    throw new ArgumentException("实体列表第 " + (i + 1) + " 项不是有效的 Brep。");
            }
        }

        private static string TextAt(List<string> values, int index) =>
            values.Count == 0 ? string.Empty : (values[index] ?? string.Empty).Trim();

        private static string FormatNumbersForConfirmation(List<string> numbers)
        {
            const int displayLimit = 12;
            if (numbers.Count <= displayLimit)
                return string.Join("\n", numbers);
            return string.Join("\n", numbers.GetRange(0, displayLimit)) +
                "\n……其余 " + (numbers.Count - displayLimit) + " 个，共 " + numbers.Count + " 个。";
        }

        /// <summary>以实体分类完全相同的非镜像口径，预检替换后会出现的多候选。</summary>
        private static List<string> FindDuplicateMatches(string database,
            List<WbMigrationRecord> replacements, WbClassificationSettings settings)
        {
            var result = new List<string>();
            var replacementNumbers = new HashSet<string>(
                replacements.ConvertAll(x => x.Number), StringComparer.OrdinalIgnoreCase);
            List<WbMigrationRecord> existing = WbMigrationStore.LoadAllParts(database);

            // 空材质/颜色在真正写库时会保留目标记录原值；预检也必须先带入相同值，
            // 否则严格材质/颜色匹配时会与最终写入后的分类口径不一致。
            var targetsByNumber = new Dictionary<string, WbMigrationRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (WbMigrationRecord target in existing)
                if (replacementNumbers.Contains(target.Number))
                    targetsByNumber[target.Number] = target;
            foreach (WbMigrationRecord replacement in replacements)
            {
                if (!targetsByNumber.TryGetValue(replacement.Number, out WbMigrationRecord target))
                    throw new InvalidOperationException("预检时未找到编号：" + replacement.Number);
                if (string.IsNullOrWhiteSpace(replacement.Feature.Material))
                    replacement.Feature.Material = target.Feature.Material;
                if (string.IsNullOrWhiteSpace(replacement.Feature.Color))
                    replacement.Feature.Color = target.Feature.Color;
            }

            var existingBuckets = WbCandidateBuckets.BuildCandidateBuckets(existing, settings.StrictMaterialColor);
            foreach (WbMigrationRecord replacement in replacements)
            {
                if (!existingBuckets.TryGetValue(
                    WbCandidateBuckets.CreateCandidateBucketKey(replacement.Feature, settings.StrictMaterialColor),
                    out List<WbMigrationRecord> candidates))
                    continue;
                foreach (WbMigrationRecord candidate in candidates)
                {
                    // 本批目标会被新实体替换，不能拿其旧特征作为重复依据。
                    if (replacementNumbers.Contains(candidate.Number))
                        continue;
                    if (WbMigrationAudit.Matches(replacement.Feature, candidate.Feature,
                        settings.Enabled, settings.Tolerances, settings.IsRelativeTolerance,
                        settings.StrictMaterialColor, settings.HoleNormalTolerance,
                        settings.OuterProfileAreaRatio, settings.OuterProfilePerimeterMin,
                        settings.DegeneratePositionMultiplier, settings.EnableDegenerateMatching,
                        settings.FallbackTolerance))
                    {
                        result.Add(replacement.Number + " ↔ " + candidate.Number);
                    }
                }
            }

            // 同一批内的两个新实体也可能互相匹配。
            for (int i = 0; i < replacements.Count; i++)
            for (int j = i + 1; j < replacements.Count; j++)
            {
                if (WbMigrationAudit.Matches(replacements[i].Feature, replacements[j].Feature,
                    settings.Enabled, settings.Tolerances, settings.IsRelativeTolerance,
                    settings.StrictMaterialColor, settings.HoleNormalTolerance,
                    settings.OuterProfileAreaRatio, settings.OuterProfilePerimeterMin,
                    settings.DegeneratePositionMultiplier, settings.EnableDegenerateMatching,
                    settings.FallbackTolerance))
                {
                    result.Add(replacements[i].Number + " ↔ " + replacements[j].Number);
                }
            }
            return result;
        }

        private static string FormatLinesForConfirmation(List<string> lines)
        {
            const int displayLimit = 12;
            if (lines.Count <= displayLimit)
                return string.Join("\n", lines);
            return string.Join("\n", lines.GetRange(0, displayLimit)) +
                "\n……其余 " + (lines.Count - displayLimit) + " 条，共 " + lines.Count + " 条。";
        }

        private static WbClassificationSettings LoadDatabaseSettings(string database)
        {
            string text = WbMigrationStore.LoadProfileSettings(database);
            if (string.IsNullOrWhiteSpace(text))
                return new WbClassificationSettings();
            if (!WbClassificationSettings.TryParse(text, out WbClassificationSettings settings))
                throw new InvalidOperationException("数据库中的分类参数快照无效，未执行替换。");
            return settings;
        }

        public void ExecuteFromButton()
        {
            if (Locked) return;
            _buttonPulse = true;
            ExpireSolution(true);
        }
    }

    /// <summary>替换实体数据电池的单独执行按钮。</summary>
    internal sealed class ReplaceWbDatabasePartDataAttributes : GH_ComponentAttributes
    {
        public ReplaceWbDatabasePartDataAttributes(ReplaceWbDatabasePartData owner)
            : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 28f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            RectangleF button = GetButtonRect();
            using (GH_Capsule cap = GH_Capsule.CreateCapsule(button, GH_Palette.Black))
                cap.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("修改", font, Brushes.White, button, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(
            GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && GetButtonRect().Contains(e.CanvasLocation))
            {
                ((ReplaceWbDatabasePartData)Owner).ExecuteFromButton();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }

        private RectangleF GetButtonRect() =>
            new RectangleF(Bounds.X + 5f, Bounds.Bottom - 24f, Bounds.Width - 10f, 20f);
    }
}
