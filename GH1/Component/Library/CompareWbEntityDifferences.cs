using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NS_Parrot
{
    /// <summary>对比 WB 分类库实体，并输出可在 Rhino 视图中预览的差异孔圆圈。</summary>
    public sealed class CompareWbEntityDifferences : GH_Component
    {
        private const string FullOutputChunk = "CompareWbEntityDifferencesSettings";
        private bool _fullOutput = true;

        public CompareWbEntityDifferences()
            : base("核查实体差异（WB）", "核查差异_WB",
                "对比两个数据库编号，或将第二对象替换为完整特征摘要；提供待标记模型后，在模型上圈出未配对或超出数据库容差的孔。",
                "Parrot", "Library") { }

        public override Guid ComponentGuid => new Guid("D75A1B9C-8A7F-4E2C-94D2-6F9A531C0E7B");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("数据库地址", "数据库", "WB 分类库 SQLite 文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("基准编号", "基准", "数据库中的基准实体编号。", GH_ParamAccess.item);
            p.AddTextParameter("对比编号或特征数据", "对比", "数据库编号，或电池“特征摘要”输出的一项完整文本。", GH_ParamAccess.item);
            p.AddBrepParameter("待标记模型", "模型", "可选。对应对比对象的 Brep；提供后在该模型上圈出未配对孔。", GH_ParamAccess.item);
            p.AddTextParameter("误差设置", "设置", "可选。连接“实体分类_WB”的“误差设置”输出后，优先按该设置核查；未连接时读取数据库保存的设置。", GH_ParamAccess.item);
            p.AddBrepParameter("基准模型", "基准模型", "可选。仅用于“拓扑诊断”输出；连接数据库编号对应的原始 Brep 后，可逐面比较两侧环和边的连接状态。", GH_ParamAccess.item);
            p[2].Optional = true;
            p[3].Optional = true;
            p[4].Optional = true;
            p[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("对比", "对比", "整体特征及孔的完整对比说明。", GH_ParamAccess.list);
            p.AddTextParameter("差异", "差异", "超过当前误差限度、会影响编号复用的指标。", GH_ParamAccess.list);
            p.AddCurveParameter("差异圈", "圈", "模型上未配对或超出数据库容差孔的标记圆圈；可直接预览。", GH_ParamAccess.list);
            p.AddTextParameter("拓扑诊断", "拓扑", "基准/对比 Brep 的逐面环、边及曲线连接状态；数据库只保存特征，需连接“基准模型”才能诊断基准侧原始拓扑。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string database = string.Empty, baselineNumber = string.Empty, comparison = string.Empty;
            Brep markerBrep = null;
            string settingsText = string.Empty;
            Brep baselineBrep = null;
            if (!da.GetData(0, ref database) || !da.GetData(1, ref baselineNumber)) return;
            da.GetData(2, ref comparison);
            da.GetData(3, ref markerBrep);
            da.GetData(4, ref settingsText);
            da.GetData(5, ref baselineBrep);

            var messages = new List<string>();
            var influentialDifferences = new List<string>();
            var circles = new List<Curve>();
            var topologyDiagnostics = new List<string>();
            try
            {
                if (!File.Exists(database)) throw new FileNotFoundException("数据库文件不存在：" + database);
                WbClassificationSettings settings = LoadEffectiveSettings(database, settingsText);
                WbFeature a = FindDatabaseFeature(database, baselineNumber);
                topologyDiagnostics.AddRange(BuildTopologyDiagnostics(baselineBrep, markerBrep, settings.FallbackTolerance));
                WbFeature b;
                if (markerBrep != null)
                {
                    // 已连接模型时，模型是唯一的对比来源，避免编号/文本与模型不一致。
                    b = WbFeature.Create(a.Material, a.Color, markerBrep,
                        settings.InertiaDegenerateRatio, settings.FallbackTolerance, 0);
                    if (!string.IsNullOrWhiteSpace(comparison))
                        messages.Add("已连接模型，已忽略“对比”端口输入，使用模型特征进行核查。");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(comparison))
                        throw new ArgumentException("未提供待标记模型时，必须输入对比编号或特征数据。");
                    if (!TryReadFeatureText(comparison, out b)) b = FindDatabaseFeature(database, comparison);
                }

                CompareOverall(a, b, settings, messages);
                List<WbHole> missingFromB;
                List<WbHole> extraInB;
                List<WbHole> toleranceExceededInB;
                CompareHoles(a, b, settings, messages, out missingFromB, out extraInB,
                    out toleranceExceededInB);
                messages.Add("孔数：基准=" + a.Holes.Count + "；对比=" + b.Holes.Count + "。未配对：基准独有=" + missingFromB.Count + "；对比独有=" + extraInB.Count + "；超出当前误差设置=" + toleranceExceededInB.Count + "。");
                foreach (WbHole h in missingFromB) messages.Add("基准独有孔：" + HoleText(h));
                foreach (WbHole h in extraInB) messages.Add("对比独有孔：" + HoleText(h));

                influentialDifferences.AddRange(CollectInfluentialDifferences(
                    messages, missingFromB, extraInB, settings.StrictMaterialColor));
                if (influentialDifferences.Count == 0)
                    influentialDifferences.Add("未发现超过当前误差限度、会影响编号复用的指标。");

                if (markerBrep != null)
                {
                    foreach (WbHole h in missingFromB.Concat(extraInB).Concat(toleranceExceededInB).Distinct())
                        circles.Add(CreateMarker(b, h));
                    messages.Add("已在待标记模型上圈出 " + circles.Count + " 个未配对或超差孔。");
                }
                else if (missingFromB.Count + extraInB.Count > 0)
                {
                    messages.Add("未提供待标记模型，无法将局部孔坐标定位到 Rhino 模型。已输出文字差异。");
                }
            }
            catch (Exception ex)
            {
                messages.Add("核查失败：" + ex.Message);
                influentialDifferences.Add("核查失败：" + ex.Message);
                topologyDiagnostics.Add("拓扑诊断未完成：" + ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
            if (!_fullOutput)
                messages = messages.Where(IsDifferenceOnlyMessage).ToList();
            da.SetDataList(0, messages);
            da.SetDataList(1, influentialDifferences);
            da.SetDataList(2, circles);
            da.SetDataList(3, topologyDiagnostics);
        }

        private static IEnumerable<string> BuildTopologyDiagnostics(
            Brep baselineBrep, Brep comparisonBrep, double fallbackTolerance)
        {
            double tolerance = Rhino.RhinoDoc.ActiveDoc == null
                ? fallbackTolerance
                : Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            string toleranceText = tolerance.ToString("G17", CultureInfo.InvariantCulture);

            if (baselineBrep == null)
                yield return "基准侧：未连接“基准模型”。数据库仅保存分类特征，无法从编号还原原始面环拓扑。";
            else
            {
                foreach (string line in DescribeTopology("基准侧", baselineBrep, tolerance, toleranceText))
                    yield return line;
            }

            if (comparisonBrep == null)
                yield return "对比侧：未连接“待标记模型”，无法输出原始面环拓扑。";
            else
            {
                foreach (string line in DescribeTopology("对比侧", comparisonBrep, tolerance, toleranceText))
                    yield return line;
            }
        }

        private static IEnumerable<string> DescribeTopology(
            string side, Brep brep, double tolerance, string toleranceText)
        {
            yield return side + "：面数=" + brep.Faces.Count + "；边数=" + brep.Edges.Count +
                "；顶点数=" + brep.Vertices.Count + "；有效=" + brep.IsValid +
                "；曲线连接容差=" + toleranceText + "。";

            for (int faceIndex = 0; faceIndex < brep.Faces.Count; faceIndex++)
            {
                BrepFace face = brep.Faces[faceIndex];
                yield return side + " 面[" + faceIndex + "]：环数=" + face.Loops.Count + "。";

                for (int loopIndex = 0; loopIndex < face.Loops.Count; loopIndex++)
                {
                    BrepLoop loop = face.Loops[loopIndex];
                    var curves = new List<Curve>();
                    try
                    {
                        foreach (BrepTrim trim in loop.Trims)
                        {
                            if (trim.Edge != null)
                            {
                                Curve curve = trim.Edge.DuplicateCurve();
                                if (curve != null)
                                    curves.Add(curve);
                            }
                        }

                        Curve[] joined = curves.Count == 0 ? null : Curve.JoinCurves(curves, tolerance);
                        int joinedCount = joined == null ? 0 : joined.Length;
                        int closedCount = joined == null ? 0 : joined.Count(curve => curve != null && curve.IsClosed);
                        bool singleClosed = joinedCount == 1 && closedCount == 1;
                        yield return side + " 面[" + faceIndex + "] 环[" + loopIndex + "]：类型=" +
                            loop.LoopType + "；修剪数=" + loop.Trims.Count + "；有效边数=" + curves.Count +
                            "；连接后曲线数=" + joinedCount + "；闭合曲线数=" + closedCount +
                            "；单一闭合=" + singleClosed + "。";

                        if (joined != null)
                            foreach (Curve curve in joined)
                                curve?.Dispose();
                    }
                    finally
                    {
                        foreach (Curve curve in curves)
                            curve.Dispose();
                    }
                }
            }
        }

        private static IEnumerable<string> CollectInfluentialDifferences(
            IEnumerable<string> messages, IEnumerable<WbHole> missingFromB,
            IEnumerable<WbHole> extraInB, bool strictMaterialColor)
        {
            foreach (string message in messages)
            {
                if (!message.StartsWith("**", StringComparison.Ordinal))
                    continue;
                if (!strictMaterialColor &&
                    (message.StartsWith("**材质不同：", StringComparison.Ordinal) ||
                     message.StartsWith("**颜色不同：", StringComparison.Ordinal)))
                    continue;
                yield return message.Trim('*');
            }

            foreach (WbHole hole in missingFromB)
                yield return "基准独有孔：" + HoleText(hole);
            foreach (WbHole hole in extraInB)
                yield return "对比独有孔：" + HoleText(hole);
        }

        private static WbFeature FindDatabaseFeature(string database, string number)
        {
            WbMigrationRecord item = WbMigrationStore.LoadAllParts(database)
                .FirstOrDefault(x => string.Equals(x.Number, number == null ? string.Empty : number.Trim(), StringComparison.OrdinalIgnoreCase));
            if (item == null) throw new ArgumentException("数据库中未找到编号：" + number);
            return item.Feature;
        }

        private static WbClassificationSettings LoadEffectiveSettings(string database, string settingsText)
        {
            if (!string.IsNullOrWhiteSpace(settingsText))
            {
                if (WbClassificationSettings.TryParse(settingsText, out WbClassificationSettings inputSettings))
                    return inputSettings;
                throw new ArgumentException("“误差设置”输入无法解析；请连接实体分类_WB的“误差设置”输出，或断开该输入后使用数据库设置。");
            }

            string text = WbMigrationStore.LoadProfileSettings(database);
            if (!WbClassificationSettings.TryParse(text, out WbClassificationSettings settings))
                throw new ArgumentException("数据库缺少可用的 classification_profile，无法按数据库容差核查。");
            return settings;
        }

        private static bool IsDifferenceOnlyMessage(string message)
        {
            return message.StartsWith("**", StringComparison.Ordinal) ||
                message.StartsWith("孔数：", StringComparison.Ordinal) ||
                message.StartsWith("基准独有孔：", StringComparison.Ordinal) ||
                message.StartsWith("对比独有孔：", StringComparison.Ordinal) ||
                message.StartsWith("已在待标记模型上圈出", StringComparison.Ordinal) ||
                message.StartsWith("未提供待标记模型", StringComparison.Ordinal) ||
                message.StartsWith("核查失败：", StringComparison.Ordinal);
        }

        protected override void AppendAdditionalComponentMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);
            var item = new System.Windows.Forms.ToolStripMenuItem("完整模式（输出容差内明细）")
            {
                Checked = _fullOutput,
                CheckOnClick = true
            };
            item.CheckedChanged += (sender, e) =>
            {
                _fullOutput = item.Checked;
                ExpireSolution(true);
            };
            menu.Items.Add(item);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetBoolean(FullOutputChunk, _fullOutput);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists(FullOutputChunk))
                _fullOutput = reader.GetBoolean(FullOutputChunk);
            return base.Read(reader);
        }

        private static bool TryReadFeatureText(string text, out WbFeature feature)
        {
            feature = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.StartsWith(WbFeature.Header + ";", StringComparison.Ordinal))
            {
                feature = WbFeature.Deserialize(text);
                return true;
            }
            if (!text.StartsWith("编号=", StringComparison.Ordinal)) return false;

            Match head = Regex.Match(text,
                @"^编号=.*?; 材质=(.*?); 颜色=(.*?); 包围盒=\(([^,]+),([^,]+),([^\)]+)\); 惯量=\(([^,]+),([^,]+),([^\)]+)\); 表面积=([^;]+); 体积=([^;]+);",
                RegexOptions.Multiline);
            if (!head.Success) return false;
            feature = new WbFeature
            {
                Material = head.Groups[1].Value,
                Color = head.Groups[2].Value,
                BoundingBox = new[] { D(head.Groups[3].Value), D(head.Groups[4].Value), D(head.Groups[5].Value) },
                Inertia = new[] { D(head.Groups[6].Value), D(head.Groups[7].Value), D(head.Groups[8].Value) },
                Area = D(head.Groups[9].Value), Volume = D(head.Groups[10].Value)
            };
            foreach (Match m in Regex.Matches(text,
                @"孔\[\d+\] 局部=\(([^,]+),([^,]+),([^\)]+)\); 周长=([^;]+); 面积=([^;]+); 法向=\(([^,]+),([^,]+),([^\)]+)\)(?:; 退化类型=([^;]+); 投影面积=([^;]+); 圆形度=([^;]+); 纵横比=([^;]+); 拟合法向=\(([^,]+),([^,]+),([^\)]+)\))?"))
            {
                bool deg = m.Groups[9].Success;
                var h = new WbHole(D(m.Groups[4].Value), D(m.Groups[5].Value), D(m.Groups[1].Value), D(m.Groups[2].Value), D(m.Groups[3].Value), D(m.Groups[6].Value), D(m.Groups[7].Value), D(m.Groups[8].Value), isDegenerate: deg, degenerateType: deg ? m.Groups[9].Value : string.Empty);
                if (deg) { h.DegenerateProjectedArea = D(m.Groups[10].Value); h.DegenerateCircularity = D(m.Groups[11].Value); h.DegenerateAspectRatio = D(m.Groups[12].Value); h.DegenerateNormalU = D(m.Groups[13].Value); h.DegenerateNormalV = D(m.Groups[14].Value); h.DegenerateNormalW = D(m.Groups[15].Value); }
                feature.Holes.Add(h);
            }
            return true;
        }

        private static double D(string value) => double.Parse(value, CultureInfo.InvariantCulture);

        private static void CompareOverall(WbFeature a, WbFeature b,
            WbClassificationSettings settings, List<string> output)
        {
            if (!string.Equals(a.Material, b.Material, StringComparison.Ordinal))
                output.Add("**材质不同：" + a.Material + " / " + b.Material + "**");
            if (!string.Equals(a.Color, b.Color, StringComparison.Ordinal))
                output.Add("**颜色不同：" + a.Color + " / " + b.Color + "**");
            AddDiff(output, "包围盒", a.BoundingBox, b.BoundingBox,
                settings.Tolerances[V5Criterion.Length], settings.IsRelativeTolerance[V5Criterion.Length], true);
            AddDiff(output, "惯量", a.Inertia, b.Inertia,
                settings.Tolerances[V5Criterion.Inertia], settings.IsRelativeTolerance[V5Criterion.Inertia],
                settings.Enabled[V5Criterion.Inertia]);
            AddInformationDiff(output, "表面积", a.Area, b.Area);
            AddDiff(output, "体积", a.Volume, b.Volume,
                settings.Tolerances[V5Criterion.Volume], settings.IsRelativeTolerance[V5Criterion.Volume],
                settings.Enabled[V5Criterion.Volume]);
        }

        private static void AddDiff(List<string> output, string name, double a, double b,
            double tolerance, bool isRelative, bool enabled)
        {
            if (Math.Abs(a - b) > 1e-6)
            {
                string percent = Math.Abs(a) <= 1e-12
                    ? "不适用（基准为零）"
                    : ((b - a) / a * 100.0).ToString("G8", CultureInfo.InvariantCulture) + "%";
                bool exceeded = enabled && !WbUtil.WithinTolerance(a, b, tolerance, isRelative);
                string text = name + "：基准=" + a.ToString("G17", CultureInfo.InvariantCulture) +
                    "；对比=" + b.ToString("G17", CultureInfo.InvariantCulture) +
                    "；差=" + (b - a).ToString("G17", CultureInfo.InvariantCulture) +
                    "；相差=" + percent + "；容差=" + FormatTolerance(tolerance, isRelative, enabled);
                output.Add(exceeded ? "**" + text + "；超差**" : text);
            }
        }

        private static void AddInformationDiff(List<string> output, string name, double a, double b)
        {
            if (Math.Abs(a - b) <= 1e-6) return;
            output.Add(name + "：基准=" + a.ToString("G17", CultureInfo.InvariantCulture) +
                "；对比=" + b.ToString("G17", CultureInfo.InvariantCulture) +
                "；差=" + (b - a).ToString("G17", CultureInfo.InvariantCulture) +
                "；仅信息，不参与分类容差判定。");
        }

        private static void AddDiff(List<string> output, string name, double[] a, double[] b,
            double tolerance, bool isRelative, bool enabled)
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                AddDiff(output, name + "[" + i + "]", a[i], b[i], tolerance, isRelative, enabled);
        }

        private static void CompareHoles(WbFeature a, WbFeature b, WbClassificationSettings settings,
            List<string> output, out List<WbHole> missingFromB, out List<WbHole> extraInB,
            out List<WbHole> toleranceExceededInB)
        {
            if (WbMigrationAudit.TryGetHoleAssignment(a, b, settings.Enabled, settings.Tolerances,
                settings.IsRelativeTolerance, settings.HoleNormalTolerance,
                settings.DegeneratePositionMultiplier, settings.EnableDegenerateMatching,
                out WbHoleAssignment assignment))
            {
                missingFromB = new List<WbHole>();
                extraInB = new List<WbHole>();
                toleranceExceededInB = new List<WbHole>();
                output.Add(FormatHoleTransform(assignment));
                for (int aIndex = 0; aIndex < a.Holes.Count; aIndex++)
                {
                    int bIndex = assignment.ComparisonIndexByBaseline[aIndex];
                    if (AppendHoleDifferences(output, a.Holes[aIndex],
                        assignment.TransformedComparisonHoles[bIndex], settings,
                        "孔[基准 " + aIndex + " / 对比 " + bIndex + "]"))
                    {
                        toleranceExceededInB.Add(b.Holes[bIndex]);
                    }
                }
                return;
            }

            // 未形成完整指派时保留宽松的周长/面积配对，以便列出未配对孔和候选孔位差。
            var available = new List<WbHole>(a.Holes);
            extraInB = new List<WbHole>();
            toleranceExceededInB = new List<WbHole>();
            for (int bIndex = 0; bIndex < b.Holes.Count; bIndex++)
            {
                WbHole hb = b.Holes[bIndex];
                WbHole best = available.Where(ha => ha.IsDegenerate == hb.IsDegenerate)
                    .OrderBy(ha => Math.Abs(ha.Perimeter - hb.Perimeter) + Math.Abs((ha.IsDegenerate ? ha.DegenerateProjectedArea : ha.Area) - (hb.IsDegenerate ? hb.DegenerateProjectedArea : hb.Area)))
                    .ThenBy(ha => HolePositionDistance(ha, hb))
                    .FirstOrDefault();
                double areaB = hb.IsDegenerate ? hb.DegenerateProjectedArea : hb.Area;
                if (best == null || Math.Abs(best.Perimeter - hb.Perimeter) > 0.001 || Math.Abs((best.IsDegenerate ? best.DegenerateProjectedArea : best.Area) - areaB) > 0.001)
                {
                    extraInB.Add(hb);
                    continue;
                }

                int aIndex = a.Holes.IndexOf(best);
                available.Remove(best);
                if (AppendHoleDifferences(output, best, hb, settings,
                    "孔[基准 " + aIndex + " / 对比 " + bIndex + "]"))
                    toleranceExceededInB.Add(hb);
            }
            missingFromB = available;
        }

        private static string FormatHoleTransform(WbHoleAssignment assignment)
        {
            if (!string.IsNullOrEmpty(assignment.AlignmentDescription))
                return "分类器采用局部坐标变换：" + assignment.AlignmentDescription + "。";
            string[] axes = { "U", "V", "W" };
            string[] terms = new string[3];
            for (int i = 0; i < terms.Length; i++)
                terms[i] = (assignment.Sign[i] < 0 ? "-" : string.Empty) +
                    axes[assignment.Permutation[i]];
            return "分类器采用局部坐标变换：对比(U,V,W)→(" +
                string.Join(",", terms) + ")。";
        }

        private static bool AppendHoleDifferences(List<string> output, WbHole a, WbHole b,
            WbClassificationSettings settings, string label)
        {
            bool exceeded = false;
            exceeded |= AddHoleMetric(output, label + " 周长", a.Perimeter, b.Perimeter,
                settings.Tolerances[V5Criterion.EdgeSum], settings.IsRelativeTolerance[V5Criterion.EdgeSum],
                settings.Enabled[V5Criterion.EdgeSum]);
            double areaA = a.IsDegenerate ? a.DegenerateProjectedArea : a.Area;
            double areaB = b.IsDegenerate ? b.DegenerateProjectedArea : b.Area;
            exceeded |= AddHoleMetric(output, label + (a.IsDegenerate ? " 投影面积" : " 面积"), areaA, areaB,
                settings.Tolerances[V5Criterion.HoleArea], settings.IsRelativeTolerance[V5Criterion.HoleArea],
                settings.Enabled[V5Criterion.HoleArea]);

            double du = a.U - b.U, dv = a.V - b.V, dw = a.W - b.W;
            double distance = Math.Sqrt(du * du + dv * dv + dw * dw);
            double positionTolerance = settings.Tolerances[V5Criterion.HolePosition];
            bool positionExceeded = settings.Enabled[V5Criterion.HolePosition] && distance >= positionTolerance;
            if (distance > 1e-6 || positionExceeded)
            {
                string text = label + " 孔位距离=" + distance.ToString("G17", CultureInfo.InvariantCulture) +
                    "；容差=" + FormatTolerance(positionTolerance, false, settings.Enabled[V5Criterion.HolePosition]);
                output.Add(positionExceeded ? "**" + text + "；超差**" : text);
            }
            exceeded |= positionExceeded;

            double lengthA = Math.Sqrt(a.NU * a.NU + a.NV * a.NV + a.NW * a.NW);
            double lengthB = Math.Sqrt(b.NU * b.NU + b.NV * b.NV + b.NW * b.NW);
            // 与分类核心一致：零长度法向不参与法向判定，避免无有效法向的旧特征误报。
            if (lengthA > 1e-12 && lengthB > 1e-12)
            {
                double dot = Math.Abs((a.NU * b.NU + a.NV * b.NV + a.NW * b.NW) / (lengthA * lengthB));
                bool normalExceeded = dot < settings.HoleNormalTolerance;
                if (1.0 - dot > 1e-6 || normalExceeded)
                {
                    string text = label + " 孔法向点积=" + dot.ToString("G17", CultureInfo.InvariantCulture) +
                        "；容差≥" + settings.HoleNormalTolerance.ToString("G17", CultureInfo.InvariantCulture);
                    output.Add(normalExceeded ? "**" + text + "；超差**" : text);
                }
                exceeded |= normalExceeded;
            }
            return exceeded;
        }

        private static double HolePositionDistance(WbHole a, WbHole b)
        {
            double du = a.U - b.U, dv = a.V - b.V, dw = a.W - b.W;
            return Math.Sqrt(du * du + dv * dv + dw * dw);
        }

        private static bool AddHoleMetric(List<string> output, string name, double a, double b,
            double tolerance, bool isRelative, bool enabled)
        {
            if (Math.Abs(a - b) <= 1e-6) return false;
            bool exceeded = enabled && !WbUtil.WithinTolerance(a, b, tolerance, isRelative);
            string text = name + "：基准=" + a.ToString("G17", CultureInfo.InvariantCulture) +
                "；对比=" + b.ToString("G17", CultureInfo.InvariantCulture) +
                "；容差=" + FormatTolerance(tolerance, isRelative, enabled);
            output.Add(exceeded ? "**" + text + "；超差**" : text);
            return exceeded;
        }

        private static string FormatTolerance(double tolerance, bool isRelative, bool enabled) =>
            enabled ? tolerance.ToString("G17", CultureInfo.InvariantCulture) + (isRelative ? "%" : string.Empty) : "未启用";

        private static string HoleText(WbHole h) => string.Format(CultureInfo.InvariantCulture, "局部=({0:G6},{1:G6},{2:G6})；周长={3:G6}；面积={4:G6}{5}", h.U, h.V, h.W, h.Perimeter, h.Area, h.IsDegenerate ? "；退化孔" : string.Empty);

        private static Curve CreateMarker(WbFeature marker, WbHole h)
        {
            Point3d center = marker.LocalOrigin + marker.Axes[0] * h.U + marker.Axes[1] * h.V + marker.Axes[2] * h.W;
            Vector3d normal = marker.Axes[0];
            Plane plane = new Plane(center, normal);
            double radius = Math.Max(10.0, h.Perimeter / (2.0 * Math.PI) * 1.5);
            return new Circle(plane, radius).ToNurbsCurve();
        }
    }
}
