using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>为空间对应的一组 Brep 求解带数值余量的 WB 可行误差设置。</summary>
    public sealed class FindWbClassificationTolerance : GH_Component
    {
        private enum FileState { Idle, OpeningFile, AwaitingGuid }

        private bool _lastFileRun;
        private FileState _fileState;
        private readonly List<string> _pendingFiles = new List<string>();
        private readonly List<Brep> _fileBreps = new List<Brep>();
        private int _fileIndex;
        private string _lastGuidSignature = string.Empty;
        private int _stableGuidCount;
        private int _guidWaitCount;
        private Timer _fileTimer;
        internal static readonly V5Criterion[] OptimizedCriteria =
        {
            V5Criterion.Length,
            V5Criterion.Inertia,
            V5Criterion.Volume,
            V5Criterion.EdgeSum,
            V5Criterion.HoleArea,
            V5Criterion.HolePosition,
            V5Criterion.DegenerateCircularity,
            V5Criterion.DegenerateAspectRatio
        };

        public FindWbClassificationTolerance()
            : base("求分类误差（WB）", "分类误差_WB",
                "依次打开文件并读取 Guid 对应实体，优先保持孔位对应，求解可按“分类库迁移_WB”规则归为同一类的误差设置。",
                "Parrot", "Library") { }

        public override Guid ComponentGuid => new Guid("5A7D0C9E-8B42-49D8-8E17-7CB61D33A271");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("文件路径", "文件", "需要依次打开并测试的 .3dm 文件路径列表。", GH_ParamAccess.list);
            p.AddGenericParameter("Guid", "Guid", "当前已打开文件中需要读取的 Brep 对象 Guid。", GH_ParamAccess.item);
            p.AddBooleanParameter("执行", "执行", "False 变为 True 时启动文件批处理。", GH_ParamAccess.item, false);
            // 首个文件尚未打开时 Guid 无法由上游提供；打开后再在求解循环中等待其刷新。
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("误差设置", "设置", "优先孔位对应、带数值余量的 WB 可行误差设置；各数值可填入“分类库迁移_WB”的右键设置。", GH_ParamAccess.item);
            p.AddBooleanParameter("可归为一组", "成功", "按输出设置复核后，全部 Brep 是否两两可按 WB 规则匹配。", GH_ParamAccess.item);
            p.AddTextParameter("设置明细（WB）", "设置明细", "按“分类库迁移_WB”右键菜单的名称与顺序逐行显示。", GH_ParamAccess.list);
            p.AddTextParameter("诊断", "诊断", "无法归为一组时的硬性不匹配原因；成功时说明求解方式。", GH_ParamAccess.list);
            p.AddCurveParameter("差异圈", "圈", "成功时圈出孔位临界孔对；失败时圈出未配齐的孔或实体中心。孔编号见诊断。", GH_ParamAccess.list);
            p.AddBrepParameter("分组", "分组", "按当前 WB 匹配规则分组的 Brep 树；每个分支内的实体两两可匹配。", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var breps = new List<Brep>();
            var diagnostics = new List<string>();
            var details = new List<string>();
            var markers = new List<Curve>();
            var groups = new GH_Structure<GH_Brep>();
            try
            {
                if (!TryCollectFileModeBreps(da, out breps, out string fileStatus))
                {
                    da.SetDataList(3, new[] { fileStatus });
                    da.SetDataList(4, markers);
                    da.SetDataTree(5, groups);
                    return;
                }

                if (breps.Count < 2)
                    throw new ArgumentException("至少需要输入两个 Brep 才能求分类误差。" );

                double fallbackTolerance = RhinoDoc.ActiveDoc == null
                    ? 0.001
                    : RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
                var settings = new WbClassificationSettings
                {
                    // 本电池没有材质、颜色输入，所有对象均按空文本处理；严格开关不影响结果。
                    StrictMaterialColor = true,
                    FallbackTolerance = fallbackTolerance
                };
                var features = new List<WbFeature>();
                for (int i = 0; i < breps.Count; i++)
                    features.Add(WbFeature.Create(string.Empty, string.Empty, breps[i],
                        settings.InertiaDegenerateRatio, fallbackTolerance, i));

                SetMostPermissive(settings, breps);
                if (!AllPairsMatch(features, settings))
                {
                    diagnostics.Add("即使所有数值误差均放宽到极大值，仍无法按 WB 规则归为一组。");
                    diagnostics.AddRange(FindHardMismatchDiagnostics(features, settings));
                    markers = BuildMismatchMarkers(features, breps, settings);
                    diagnostics.Add("已圈出 " + markers.Count.ToString(CultureInfo.InvariantCulture) + " 个可能不一致的位置。");
                    groups = BuildGroups(features, breps, settings);
                    diagnostics.Add("已按当前 WB 匹配规则拆分为 " + groups.PathCount.ToString(CultureInfo.InvariantCulture) + " 组输出。");
                    da.SetData(0, string.Empty);
                    da.SetData(1, false);
                    da.SetDataList(2, new List<string>());
                    da.SetDataList(3, diagnostics);
                    da.SetDataList(4, markers);
                    da.SetDataTree(5, groups);
                    return;
                }

                OptimizeSpatialTolerances(features, settings, diagnostics);

                bool success = AllPairsMatch(features, settings);
                details.AddRange(FormatDetails(settings));
                diagnostics.Add(success
                    ? "已优先确定孔位对应，再收紧其它判据，并完成全部 Brep 的两两复核。"
                    : "误差求解后复核失败；请将诊断信息反馈以便排查。" );
                if (success) markers = BuildCriticalHoleMarkers(features, breps, settings, diagnostics);
                groups = BuildGroups(features, breps, settings);
                da.SetData(0, settings.SettingsText());
                da.SetData(1, success);
                da.SetDataList(2, details);
                da.SetDataList(3, diagnostics);
                da.SetDataList(4, markers);
                da.SetDataTree(5, groups);
            }
            catch (Exception ex)
            {
                diagnostics.Add("求解失败：" + ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                da.SetData(1, false);
                da.SetDataList(3, diagnostics);
                da.SetDataList(4, markers);
                da.SetDataTree(5, groups);
            }
        }

        private bool TryCollectFileModeBreps(IGH_DataAccess da, out List<Brep> breps, out string status)
        {
            breps = null;
            status = string.Empty;
            var filePaths = new List<string>();
            bool run = false;
            da.GetDataList(0, filePaths);
            da.GetData(2, ref run);

            if (run && !_lastFileRun)
            {
                StartFileRun(filePaths);
                _lastFileRun = run;
                status = "已启动文件模式，准备打开第 1 / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。";
                return false;
            }
            _lastFileRun = run;

            if (_fileState == FileState.Idle)
            {
                status = "文件模式等待“执行”的上升沿。";
                return false;
            }
            if (_fileState == FileState.OpeningFile)
            {
                status = "正在打开第 " + (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " +
                    _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。";
                return false;
            }

            GH_Guid id = null;
            if (!da.GetData(1, ref id) || id == null || id.Value == Guid.Empty)
            {
                WaitForGuid("等待当前文件的 Guid 输入。");
                status = "已打开第 " + (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " +
                    _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件；等待 Guid。";
                return false;
            }

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null || !IsActiveRhinoFile(_pendingFiles[_fileIndex]) || doc.Objects.FindId(id.Value) == null)
            {
                WaitForGuid("Guid 尚未随当前文件刷新。");
                status = "已打开第 " + (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " +
                    _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件；等待 Guid 刷新。";
                return false;
            }

            string signature = NormalizePath(doc.Path) + "|" + id.Value.ToString("D");
            _stableGuidCount = string.Equals(signature, _lastGuidSignature, StringComparison.Ordinal)
                ? _stableGuidCount + 1 : 1;
            _lastGuidSignature = signature;
            if (_stableGuidCount < 3)
            {
                WaitForGuid("等待 Guid 稳定。");
                status = "已打开第 " + (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " +
                    _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件；Guid 稳定=" +
                    _stableGuidCount.ToString(CultureInfo.InvariantCulture) + " / 3。";
                return false;
            }

            _fileBreps.Add(ReadBrepFromActiveDocument(id.Value, _fileIndex));
            _fileIndex++;
            if (_fileIndex < _pendingFiles.Count)
            {
                _fileState = FileState.OpeningFile;
                _stableGuidCount = 0;
                _lastGuidSignature = string.Empty;
                _guidWaitCount = 0;
                ScheduleOpenCurrentFile();
                status = "已读取当前文件，准备打开下一文件。";
                return false;
            }

            StopFileTimer();
            _fileState = FileState.Idle;
            breps = new List<Brep>(_fileBreps);
            _fileBreps.Clear();
            status = "已读取全部文件。";
            return true;
        }

        private void StartFileRun(List<string> filePaths)
        {
            StopFileTimer();
            foreach (Brep brep in _fileBreps) brep.Dispose();
            _fileBreps.Clear();
            _pendingFiles.Clear();
            foreach (string rawPath in filePaths ?? new List<string>())
            {
                string path = NormalizePath(rawPath);
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    throw new ArgumentException("文件不存在：" + (rawPath ?? string.Empty));
                _pendingFiles.Add(path);
            }
            if (_pendingFiles.Count == 0)
                throw new ArgumentException("文件模式需要输入至少一个文件路径。" );
            _fileIndex = 0;
            _stableGuidCount = 0;
            _guidWaitCount = 0;
            _lastGuidSignature = string.Empty;
            _fileState = FileState.OpeningFile;
            ScheduleOpenCurrentFile();
        }

        private void ScheduleOpenCurrentFile()
        {
            StopFileTimer();
            _fileTimer = new Timer { Interval = 100 };
            _fileTimer.Tick += (sender, args) =>
            {
                StopFileTimer();
                if (_fileState != FileState.OpeningFile) return;
                try
                {
                    OpenCurrentFile();
                    _fileState = FileState.AwaitingGuid;
                    ScheduleRefresh(500);
                }
                catch (Exception ex)
                {
                    _fileState = FileState.Idle;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    ExpireSolution(true);
                }
            };
            _fileTimer.Start();
        }

        private void WaitForGuid(string reason)
        {
            _guidWaitCount++;
            if (_guidWaitCount > 40)
                throw new InvalidOperationException("等待 Guid 输入稳定超时：" + reason);
            ScheduleRefresh(500);
        }

        private void ScheduleRefresh(int interval)
        {
            StopFileTimer();
            _fileTimer = new Timer { Interval = interval };
            _fileTimer.Tick += (sender, args) =>
            {
                StopFileTimer();
                if (_fileState == FileState.AwaitingGuid)
                    ExpireSolution(true);
            };
            _fileTimer.Start();
        }

        private void StopFileTimer()
        {
            if (_fileTimer == null) return;
            _fileTimer.Stop();
            _fileTimer.Dispose();
            _fileTimer = null;
        }

        private void OpenCurrentFile()
        {
            string path = _pendingFiles[_fileIndex];
            if (IsActiveRhinoFile(path)) return;
            RhinoDoc current = RhinoDoc.ActiveDoc;
            if (current != null)
            {
                current.Modified = false;
                RhinoApp.RunScript("_-Close _No", false);
                RhinoApp.Wait();
            }
            string escaped = path.Replace("\"", "\"\"");
            if (!RhinoApp.RunScript("_-Open \"" + escaped + "\"", false))
                throw new InvalidOperationException("无法打开文件：" + path);
            RhinoApp.Wait();
            if (!IsActiveRhinoFile(path))
                throw new InvalidOperationException("当前文档未切换到：" + path);
        }

        private static bool IsActiveRhinoFile(string path)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            return doc != null && string.Equals(NormalizePath(doc.Path), NormalizePath(path),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return System.IO.Path.GetFullPath(path.Trim()); }
            catch { return path.Trim(); }
        }

        private static Brep ReadBrepFromActiveDocument(Guid id, int index)
        {
            Rhino.DocObjects.RhinoObject obj = RhinoDoc.ActiveDoc?.Objects.FindId(id);
            if (obj == null)
                throw new InvalidOperationException("找不到第 " + (index + 1).ToString(CultureInfo.InvariantCulture) +
                    " 个文件中的 Guid：" + id);
            if (obj.Geometry is Brep brep) return brep.DuplicateBrep();
            if (obj.Geometry is Surface surface) return surface.ToBrep();
            if (obj.Geometry is Extrusion extrusion) return extrusion.ToBrep();
            throw new InvalidOperationException("Guid " + id + " 对应的对象不是 Brep、曲面或拉伸体。" );
        }

        internal static void SetMostPermissive(WbClassificationSettings settings, List<Brep> breps)
        {
            foreach (V5Criterion criterion in OptimizedCriteria)
                settings.Tolerances[criterion] = criterion == V5Criterion.HolePosition
                    ? GetLargeDistance(breps)
                    : 1000000.0;
            settings.HoleNormalTolerance = 0.0;
        }

        private static double GetLargeDistance(IEnumerable<Brep> breps)
        {
            BoundingBox box = BoundingBox.Empty;
            foreach (Brep brep in breps)
                if (brep != null) box.Union(brep.GetBoundingBox(true));
            double diagonal = box.IsValid ? box.Diagonal.Length : 1.0;
            return Math.Max(1.0, diagonal) * 1000000.0;
        }

        internal static void TightenCriterion(List<WbFeature> features, WbClassificationSettings settings,
            V5Criterion criterion)
        {
            double high = settings.Tolerances[criterion];
            double low = 0.0;
            for (int i = 0; i < 60; i++)
            {
                double middle = (low + high) * 0.5;
                settings.Tolerances[criterion] = middle;
                if (AllPairsMatch(features, settings)) high = middle;
                else low = middle;
            }
            // 不让极小计算噪声成为强迫孔配错的约束。相对值单位为百分比。
            double numericalFloor = settings.IsRelativeTolerance[criterion]
                ? 0.000001 : Math.Max(1e-9, settings.FallbackTolerance * 0.001);
            if (criterion == V5Criterion.DegenerateCircularity || criterion == V5Criterion.DegenerateAspectRatio)
                numericalFloor = 1e-9;
            settings.Tolerances[criterion] = Math.Max(high, numericalFloor);
        }

        internal static void OptimizeSpatialTolerances(List<WbFeature> features,
            WbClassificationSettings settings, List<string> diagnostics)
        {
            var defaults = new WbClassificationSettings();
            var descriptors = new[] { V5Criterion.EdgeSum, V5Criterion.HoleArea,
                V5Criterion.DegenerateCircularity, V5Criterion.DegenerateAspectRatio };
            foreach (V5Criterion criterion in descriptors)
            {
                double permissive = settings.Tolerances[criterion];
                settings.Tolerances[criterion] = defaults.Tolerances[criterion];
                if (AllPairsMatch(features, settings)) continue;
                settings.Tolerances[criterion] = permissive;
                diagnostics.Add(CriterionName(criterion) + "默认容差不足，已保留放宽值寻找空间对应。");
            }
            TightenCriterion(features, settings, V5Criterion.HolePosition);
            foreach (V5Criterion criterion in OptimizedCriteria)
                if (criterion != V5Criterion.HolePosition) TightenCriterion(features, settings, criterion);
            TightenNormalTolerance(features, settings);
            diagnostics.Add("求解优先保持孔的空间对应；相对容差下限为0.000001%，孔位数值下限为回退容差×0.001（至少1e-9）。输出为带数值余量的可行设置。");
        }

        internal static List<Curve> BuildCriticalHoleMarkers(List<WbFeature> features,
            List<Brep> sourceBreps, WbClassificationSettings settings, List<string> diagnostics)
        {
            double maximum = -1;
            int leftFeature = -1, rightFeature = -1, leftHole = -1, rightHole = -1;
            WbHole alignedRight = null;
            string description = string.Empty;
            for (int i = 0; i < features.Count; i++)
            for (int j = i + 1; j < features.Count; j++)
            {
                if (!WbMigrationAudit.TryGetClosestHoleAssignment(features[i], features[j], settings,
                    out WbHoleAssignment assignment)) continue;
                for (int k = 0; k < features[i].Holes.Count; k++)
                {
                    int comparisonIndex = assignment.ComparisonIndexByBaseline[k];
                    WbHole a = features[i].Holes[k];
                    WbHole b = assignment.TransformedComparisonHoles[comparisonIndex];
                    double du = a.U - b.U, dv = a.V - b.V, dw = a.W - b.W;
                    double distance = Math.Sqrt(du * du + dv * dv + dw * dw);
                    if (distance <= maximum) continue;
                    maximum = distance;
                    leftFeature = i; rightFeature = j; leftHole = k; rightHole = comparisonIndex;
                    alignedRight = b;
                    description = assignment.AlignmentDescription;
                }
            }
            var markers = new List<Curve>();
            if (leftFeature < 0)
            {
                diagnostics.Add("未提取到可报告的孔对，孔位容差不代表实际开孔偏差。");
                return markers;
            }
            WbHole left = features[leftFeature].Holes[leftHole];
            WbHole right = features[rightFeature].Holes[rightHole];
            double worldDistance = new Point3d(left.X, left.Y, left.Z).DistanceTo(new Point3d(right.X, right.Y, right.Z));
            double leftArea = left.IsDegenerate ? left.DegenerateProjectedArea : left.Area;
            double rightArea = right.IsDegenerate ? right.DegenerateProjectedArea : right.Area;
            diagnostics.Add("孔位临界实体对：第" + (leftFeature + 1) + "与第" + (rightFeature + 1) +
                "个Brep；孔编号（从0开始）=" + leftHole + " / " + rightHole);
            diagnostics.Add("该复核配对的最大对齐距离=" + maximum.ToString("G17", CultureInfo.InvariantCulture) +
                "；输出孔位容差=" + settings.Tolerances[V5Criterion.HolePosition].ToString("G17", CultureInfo.InvariantCulture) +
                "；原始世界坐标距离=" + worldDistance.ToString("G17", CultureInfo.InvariantCulture) + "（仅供同位置模型参考）");
            diagnostics.Add("临界孔周长差=" + Math.Abs(left.Perimeter - right.Perimeter).ToString("G17", CultureInfo.InvariantCulture) +
                "；面积差=" + Math.Abs(leftArea - rightArea).ToString("G17", CultureInfo.InvariantCulture));
            diagnostics.Add("临界孔局部坐标：A=(" + WbUtil.FR(left.U) + "," + WbUtil.FR(left.V) + "," + WbUtil.FR(left.W) +
                ")；对齐B=(" + WbUtil.FR(alignedRight.U) + "," + WbUtil.FR(alignedRight.V) + "," + WbUtil.FR(alignedRight.W) + ")");
            diagnostics.Add("采用对齐：" + description + "；差异圈标记该临界孔对，即使误差为零也输出定位圈。");
            markers.Add(CreateHoleMarker(features[leftFeature], left, GetMarkerRadius(sourceBreps, leftFeature)));
            markers.Add(CreateHoleMarker(features[rightFeature], right, GetMarkerRadius(sourceBreps, rightFeature)));
            return markers;
        }

        internal static void TightenNormalTolerance(List<WbFeature> features, WbClassificationSettings settings)
        {
            double low = 0.0;
            double high = 1.0;
            for (int i = 0; i < 60; i++)
            {
                double middle = (low + high) * 0.5;
                settings.HoleNormalTolerance = middle;
                if (AllPairsMatch(features, settings)) low = middle;
                else high = middle;
            }
            settings.HoleNormalTolerance = low;
        }

        internal static bool AllPairsMatch(List<WbFeature> features, WbClassificationSettings settings)
        {
            for (int i = 0; i < features.Count; i++)
            for (int j = i + 1; j < features.Count; j++)
                if (!FeaturesMatch(features[i], features[j], settings))
                    return false;
            return true;
        }

        internal static GH_Structure<GH_Brep> BuildGroups(List<WbFeature> features,
            List<Brep> sourceBreps, WbClassificationSettings settings)
        {
            var groups = new GH_Structure<GH_Brep>();
            var remaining = new List<int>(Enumerable.Range(0, features.Count));
            int groupIndex = 0;
            while (remaining.Count > 0)
            {
                int seed = remaining[0];
                var group = new List<int> { seed };
                remaining.RemoveAt(0);
                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    int candidate = remaining[i];
                    if (!group.All(member => FeaturesMatch(features[member], features[candidate], settings)))
                        continue;
                    group.Add(candidate);
                    remaining.RemoveAt(i);
                }

                group.Sort();
                var path = new GH_Path(groupIndex++);
                foreach (int index in group)
                    if (index >= 0 && index < sourceBreps.Count && sourceBreps[index] != null)
                        groups.Append(new GH_Brep(sourceBreps[index]), path);
            }
            return groups;
        }

        internal static List<Curve> BuildMismatchMarkers(List<WbFeature> features,
            List<Brep> sourceBreps, WbClassificationSettings settings)
        {
            var marked = new Dictionary<int, HashSet<int>>();
            var globalMarkers = new HashSet<int>();
            for (int i = 0; i < features.Count; i++)
            for (int j = i + 1; j < features.Count; j++)
                if (!FeaturesMatch(features[i], features[j], settings))
                {
                    WbMigrationAudit.FindUnmatchedHoles(features[i], features[j], settings.Enabled,
                        settings.Tolerances, settings.IsRelativeTolerance, settings.HoleNormalTolerance,
                        settings.DegeneratePositionMultiplier, settings.EnableDegenerateMatching,
                        out List<int> left, out List<int> right, out List<string> conflictDetails);
                    if (!marked.ContainsKey(i)) marked[i] = new HashSet<int>();
                    if (!marked.ContainsKey(j)) marked[j] = new HashSet<int>();
                    marked[i].UnionWith(left);
                    marked[j].UnionWith(right);
                    if (left.Count == 0 && right.Count == 0)
                    { globalMarkers.Add(i); globalMarkers.Add(j); }
                }

            var markers = new List<Curve>();
            foreach (var entry in marked)
            {
                int index = entry.Key;
                WbFeature feature = features[index];
                double radius = GetMarkerRadius(sourceBreps, index);
                if (globalMarkers.Contains(index) || feature.Holes.Count == 0)
                {
                    markers.Add(CreateFeatureMarker(feature, radius));
                }
                foreach (int holeIndex in entry.Value.OrderBy(x => x))
                    markers.Add(CreateHoleMarker(feature, feature.Holes[holeIndex], radius));
            }
            return markers;
        }

        private static bool FeaturesMatch(WbFeature left, WbFeature right,
            WbClassificationSettings settings)
        {
            return WbMigrationAudit.Matches(left, right, settings.Enabled,
                settings.Tolerances, settings.IsRelativeTolerance, settings.StrictMaterialColor,
                settings.HoleNormalTolerance, settings.OuterProfileAreaRatio,
                settings.OuterProfilePerimeterMin, settings.DegeneratePositionMultiplier,
                settings.EnableDegenerateMatching, settings.FallbackTolerance);
        }

        private static double GetMarkerRadius(List<Brep> breps, int index)
        {
            if (breps != null && index >= 0 && index < breps.Count && breps[index] != null)
            {
                double diagonal = breps[index].GetBoundingBox(true).Diagonal.Length;
                if (diagonal > RhinoMath.ZeroTolerance) return Math.Max(diagonal * 0.03, 1.0);
            }
            return 10.0;
        }

        private static Curve CreateHoleMarker(WbFeature feature, WbHole hole, double minimumRadius)
        {
            Point3d center = new Point3d(hole.X, hole.Y, hole.Z);
            Vector3d normal = feature.Axes[0] * hole.NU + feature.Axes[1] * hole.NV + feature.Axes[2] * hole.NW;
            if (hole.IsDegenerate)
                normal = feature.Axes[0] * hole.DegenerateNormalU + feature.Axes[1] * hole.DegenerateNormalV +
                    feature.Axes[2] * hole.DegenerateNormalW;
            if (!normal.Unitize()) normal = feature.Axes[0];
            double radius = Math.Max(minimumRadius, hole.Perimeter / (2.0 * Math.PI) * 1.5);
            return new Circle(new Plane(center, normal), radius).ToNurbsCurve();
        }

        private static Curve CreateFeatureMarker(WbFeature feature, double radius)
        {
            Point3d center = feature.Centroid.IsValid ? feature.Centroid : feature.LocalOrigin;
            Vector3d normal = feature.Axes[0];
            if (!normal.Unitize()) normal = Vector3d.ZAxis;
            return new Circle(new Plane(center, normal), radius * 2.0).ToNurbsCurve();
        }

        internal static IEnumerable<string> FindHardMismatchDiagnostics(
            List<WbFeature> features, WbClassificationSettings settings)
        {
            for (int i = 0; i < features.Count; i++)
            for (int j = i + 1; j < features.Count; j++)
            {
                WbFeature left = features[i];
                WbFeature right = features[j];
                if (FeaturesMatch(left, right, settings)) continue;
                foreach (string reason in WbMigrationAudit.DiagnoseFeatureMismatch(
                    left, right, settings.Enabled, settings.Tolerances, settings.IsRelativeTolerance,
                    settings.StrictMaterialColor, settings.HoleNormalTolerance,
                    settings.DegeneratePositionMultiplier, settings.EnableDegenerateMatching))
                    yield return "第 " + (i + 1).ToString(CultureInfo.InvariantCulture) + " 与第 " +
                        (j + 1).ToString(CultureInfo.InvariantCulture) + " 个 Brep：" + reason;
            }
        }

        internal static IEnumerable<string> FormatDetails(WbClassificationSettings settings)
        {
            foreach (V5Criterion criterion in OptimizedCriteria)
            {
                string value = settings.Tolerances[criterion].ToString("G17", CultureInfo.InvariantCulture);
                if (settings.IsRelativeTolerance[criterion]) value += "%";
                bool enabled = criterion == V5Criterion.Length || settings.Enabled[criterion];
                yield return CriterionName(criterion) + "：" + (enabled ? "启用" : "关闭") + "；容差=" + value;
            }
            yield return "回退容差：" + settings.FallbackTolerance.ToString("G17", CultureInfo.InvariantCulture);
            yield return "退化判定阈值：" + settings.InertiaDegenerateRatio.ToString("G17", CultureInfo.InvariantCulture);
            yield return "材质/颜色严格模式：" + settings.StrictMaterialColor.ToString();
            yield return "外轮廓面积容差（仅诊断）：" + settings.OuterProfileAreaRatio.ToString("G17", CultureInfo.InvariantCulture);
            yield return "外轮廓周长最小容差（仅诊断）：" + settings.OuterProfilePerimeterMin.ToString("G17", CultureInfo.InvariantCulture);
            yield return "退化孔容差倍数：" + settings.DegeneratePositionMultiplier.ToString("G17", CultureInfo.InvariantCulture);
            yield return "退化孔容差放宽：" + settings.EnableDegenerateMatching.ToString();
            yield return "法向兼容阈值：" + settings.HoleNormalTolerance.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string CriterionName(V5Criterion criterion)
        {
            return WbClassificationMigration.CriterionDisplayName(criterion);
        }
    }
}
