using Excel = Microsoft.Office.Interop.Excel;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using parrot.Properties;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class ScrewHoleByVector : GH_Component
    {
        public enum ButtonColor { Black, Grey }
        public enum HoleMode { ByScrewSpec, Round, Countersink }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        public bool ButtonRun { get; set; }
        public string DialogSpec { get; set; } = string.Empty;
        public string DisplaySpec { get; private set; } = string.Empty;
        public string LastTablePath { get; private set; } = string.Empty;
        public bool SpecInputHasValue { get; private set; }
        public HoleMode CurrentHoleMode { get; private set; } = HoleMode.ByScrewSpec;

        private readonly ScrewHoleOutputs _lastOutputs = new ScrewHoleOutputs();
        private bool _hasLastOutputs;
        private bool _lastRunInput;
        private string _lastInputSignature = string.Empty;

        public ScrewHoleByVector()
          : this("ScrewHoleByVector", "ScrewHole",
              "按一种规格，根据螺钉代表直线和孔数据库自动生成底孔、过孔、工艺孔")
        {
        }

        protected ScrewHoleByVector(string name, string nickname, string description)
          : base(name, nickname, description, "Parrot", "ExcelCAD")
        {
        }

        protected virtual bool UseLineInfo => false;
        protected virtual bool ShowSpecButton => true;
        public bool HasSpecButton => ShowSpecButton;
        public int DatabaseInputIndex => 3;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("开孔实体", "开孔实体", "需要被开孔的 Brep 实体列表。螺钉线会与这些实体求交并生成底孔、过孔或工艺孔。例：输入两块叠放板件时，会按螺钉线方向判断前一层过孔、后一层底孔。", GH_ParamAccess.list);
            pManager.AddGenericParameter("螺钉线", "螺钉线", "螺钉代表线列表，支持直接输入 Line，也支持输入 Rhino 直线对象 GUID。方向很重要：Line.From 为螺钉头/过孔侧，Line.To 为螺钉尾/底孔侧。例：从外盖板指向内衬板。", GH_ParamAccess.list);
            pManager.AddTextParameter("规格", "规格", UseLineInfo ? "逐条螺钉线的规格列表，与螺钉线一一对应。为空时从螺钉线对象 UserString 读取。必须包含长度，例如 ST4.8*16；没有长度会报警并跳过主孔。" : "统一螺钉规格，本电池只按一种规格处理。必须包含长度，例如 ST4.8*16；16 会作为主孔切削体总长度，沉头孔时也包含锥头深度。", UseLineInfo ? GH_ParamAccess.list : GH_ParamAccess.item);
            pManager.AddTextParameter("孔数据库", "孔数据库", "孔数据库 Excel 文件路径。圆孔模式读取“圆孔”sheet，从第 3 行开始，A:G 列依次为：规格、底孔直径、底孔名称、过孔直径、过孔名称、工艺孔直径、工艺孔名称。沉头孔模式读取“沉头孔”sheet 的过孔规则，A:E 列依次为：规格、D1、D2、t、名称；底孔和工艺孔仍从“圆孔”sheet 读取。", GH_ParamAccess.item);
            pManager.AddTextParameter("孔类型", "孔类型", UseLineInfo ? "可选，逐条螺钉线的孔类型序列；为空时从螺钉线对象 UserString 读取。例：第一条线输入 gk,dk，第二条线输入 gy,gk,dk。类型可写：跳过/tg/0，底孔/dk/1，过孔/gk/2，工艺孔/gy/3。" : "可选，用于人工覆盖自动判断。为空时自动判断：单层默认过孔；多层按命中顺序最后一层为底孔，其余为过孔。普通列表按命中顺序对应，例：gk,dk。精确覆盖写法：线索引:实体索引=类型，例：0:2=底孔、1:0=过孔。", GH_ParamAccess.list);
            pManager.AddIntegerParameter("穿透层数", "穿透层数", "预期穿透层数。若实际检测值不一致，会弹气泡警告，并按“处理方式”决定继续开孔或把此螺丝判为无效。<=0 时不检查；按线信息版为空时会尝试读取螺钉线 UserString。", GH_ParamAccess.item, 0);
            pManager.AddParameter(new ScrewPenetrationMismatchActionParam(), "处理方式", "处理方式", "穿透层数与预期不符时的统一处理方式。端口右键可选：默认=只警告并继续按默认规则开孔；无效=螺丝线加入无效螺栓线并跳过开孔。", GH_ParamAccess.item);
            pManager.AddNumberParameter("容差", "容差", "相交和布尔计算容差。<=0 时使用当前 Rhino 文件绝对容差。模型尺寸较大或相交不稳定时可手动输入，例如 0.01 或 0.1。", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("执行", "执行", "是否执行布尔差集。True 时输出开孔后实体；False 时只输出切削体、孔中心、孔轴线、明细和错误，方便预览检查。也可点击组件底部 Run 按钮执行一次。", GH_ParamAccess.item, false);

            pManager[2].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("开孔后实体", "开孔后实体", "执行=True 时输出布尔差集后的实体；执行=False 时不输出实体。用于最终建模结果。", GH_ParamAccess.list);
            pManager.AddBrepParameter("全部切削体", "全部切削体", "本次生成的全部孔切削体，包含底孔、过孔和工艺孔。用于预览孔是否落在正确位置。", GH_ParamAccess.list);
            pManager.AddBrepParameter("底孔切削体", "底孔切削体", "仅输出底孔切削体。例：多层板最后一层通常生成底孔。", GH_ParamAccess.list);
            pManager.AddBrepParameter("过孔切削体", "过孔切削体", "仅输出过孔切削体。例：螺钉头侧或中间层通常生成过孔。", GH_ParamAccess.list);
            pManager.AddBrepParameter("工艺孔切削体", "工艺孔切削体", "仅输出工艺孔切削体。用于反方向搜索到的工艺孔或人工指定的工艺孔。", GH_ParamAccess.list);
            pManager.AddPointParameter("孔中心", "孔中心", "实际生成孔的中心点列表，可用于标注、检查或与其它构件对齐。", GH_ParamAccess.list);
            pManager.AddLineParameter("孔轴线", "孔轴线", "实际生成孔的轴线列表，可用于检查孔方向和深度。", GH_ParamAccess.list);
            pManager.AddTextParameter("孔名称", "孔名称", "每个孔对应的名称，来自孔数据库中的底孔名称、过孔名称或工艺孔名称。", GH_ParamAccess.list);
            pManager.AddTextParameter("孔类别", "孔类别", "每个孔的类别：底孔、过孔或工艺孔。可用于分组、统计或分层显示。", GH_ParamAccess.list);
            pManager.AddTextParameter("明细", "明细", "匹配和判断过程说明。例：某条线按单层默认过孔、某个实体按孔类型跳过等。用于排查规则是否符合预期。", GH_ParamAccess.list);
            pManager.AddTextParameter("错误", "错误", "错误或警告列表。例：规格缺失、孔数据库找不到规格、穿透层数不一致、未命中实体等。", GH_ParamAccess.list);
            pManager.AddLineParameter("有效螺栓线", "有效螺栓线", "至少成功生成一个底孔或过孔切削体的螺栓线。用于后续统计或复查。", GH_ParamAccess.list);
            pManager.AddLineParameter("无效螺栓线", "无效螺栓线", "没有成功生成切削体或被处理方式判定为无效的螺栓线。用于定位异常线。", GH_ParamAccess.list);
            pManager.AddTextParameter("规格统计", "规格统计", "只统计有效螺栓线，格式为：规格 x 数量。例：ST4.8 x 12、M6(铝材) x 4。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Brep> parts = new List<Brep>();
            List<IGH_Goo> screwLineInputs = new List<IGH_Goo>();
            string spec = "";
            string tablePath = "";
            bool run = false;
            List<string> roles = new List<string>();
            List<string> lineSpecs = new List<string>();
            string mismatchActionInput = string.Empty;
            int expectedPenetrationInput = 0;
            double tolerance = 0.0;

            DA.GetDataList(0, parts);
            DA.GetDataList(1, screwLineInputs);

            bool hasSpecInput = UseLineInfo
                ? DA.GetDataList(2, lineSpecs) && lineSpecs.Any(item => !string.IsNullOrWhiteSpace(item))
                : DA.GetData(2, ref spec) && !string.IsNullOrWhiteSpace(spec);
            SpecInputHasValue = hasSpecInput;
            UpdateDisplaySpec(hasSpecInput ? spec : DialogSpec);

            bool hasTablePath = DA.GetData(3, ref tablePath);
            if (hasTablePath)
                LastTablePath = tablePath ?? string.Empty;

            DA.GetDataList(4, roles);
            DA.GetData(5, ref expectedPenetrationInput);
            DA.GetData(6, ref mismatchActionInput);
            DA.GetData(7, ref tolerance);

            string inputSignature = BuildInputSignature(parts, screwLineInputs, spec, lineSpecs, tablePath, roles, expectedPenetrationInput, mismatchActionInput, tolerance);
            bool inputChanged = _hasLastOutputs && !string.Equals(_lastInputSignature, inputSignature, StringComparison.Ordinal);

            DA.GetData(8, ref run);
            bool shouldRun = ButtonRun || (run && !_lastRunInput);
            ButtonRun = false;
            _lastRunInput = run;

            if (!shouldRun)
            {
                SetCachedOutputs(DA, inputChanged);
                return;
            }

            if (parts.Count == 0)
                return;
            if (screwLineInputs.Count == 0)
                return;
            if (!hasTablePath)
                return;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (tolerance <= 0)
                tolerance = doc?.ModelAbsoluteTolerance ?? 0.01;

            List<string> errors = new List<string>();
            List<ScrewLineInput> screwLines = ResolveScrewLineInputs(screwLineInputs, doc, tolerance, errors);
            if (screwLines.Count == 0)
            {
                SetEmptyOutputs(DA, errors);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", errors));
                return;
            }

            List<Line> screwLineGeometries = screwLines.Select(item => item.Line).ToList();
            double processLength = EstimateProcessSearchLength(parts, screwLineGeometries, tolerance);

            HoleRuleTables holeTables = ReadHoleTables(tablePath, 3, CurrentHoleMode, errors, GetDocumentDirectory());

            List<Brep> allCutters = new List<Brep>();
            List<Brep> tapCutters = new List<Brep>();
            List<Brep> clearanceCutters = new List<Brep>();
            List<Brep> processCutters = new List<Brep>();
            List<Point3d> holePoints = new List<Point3d>();
            List<Line> holeAxes = new List<Line>();
            List<string> holeNames = new List<string>();
            List<string> holeTypes = new List<string>();
            List<string> report = new List<string>();
            List<Line> validLines = new List<Line>();
            List<Line> invalidLines = new List<Line>();
            Dictionary<string, int> validSpecCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<List<Brep>> cuttersByPart = parts.Select(_ => new List<Brep>()).ToList();

            for (int lineIndex = 0; lineIndex < screwLines.Count; lineIndex++)
            {
                ScrewLineInput lineInput = screwLines[lineIndex];
                Line screwLine = lineInput.Line;
                int inputIndex = lineInput.InputIndex;
                if (!screwLine.IsValid || screwLine.Length <= tolerance)
                {
                    errors.Add($"Line {inputIndex}: 螺钉代表线无效。");
                    invalidLines.Add(screwLine);
                    continue;
                }

                string rawSpec = UseLineInfo
                    ? GetLineInfoSpec(GetIndexedListValue(lineSpecs, inputIndex), screwLine, lineInput.Object, doc, tolerance)
                    : GetSingleSpec(spec, DialogSpec);
                if (string.IsNullOrWhiteSpace(DisplaySpec) && !string.IsNullOrWhiteSpace(rawSpec))
                    UpdateDisplaySpec(rawSpec);

                HoleMode lineHoleMode = ResolveLineHoleMode(rawSpec, CurrentHoleMode);
                string tableSpec = ResolveTableSpec(rawSpec);
                double screwLength = ParseSpecLength(rawSpec);
                if (string.IsNullOrWhiteSpace(tableSpec))
                {
                    errors.Add($"Line {inputIndex}: 缺少规格，且未能从 Spec、规格选择对话框或 Line 对象 UserString 读取。");
                    invalidLines.Add(screwLine);
                    continue;
                }

                if (lineHoleMode == HoleMode.ByScrewSpec)
                {
                    errors.Add($"Line {inputIndex}: 规格“{rawSpec}”缺少 PH- 或 FH- 前缀，无法按螺钉线规格判断圆孔/沉头孔。");
                    invalidLines.Add(screwLine);
                    continue;
                }

                Dictionary<string, HoleRuleSet> table = holeTables.GetTable(lineHoleMode);
                if (!table.TryGetValue(tableSpec, out HoleRuleSet ruleSet))
                {
                    errors.Add($"Line {inputIndex}: 孔数据库的{GetHoleModeDisplayName(lineHoleMode)}规则中找不到规格 {tableSpec}。");
                    invalidLines.Add(screwLine);
                    continue;
                }

                List<PartHit> bodyHits = GetBodyHits(parts, screwLine, tolerance);
                int expectedPenetrationCount = GetExpectedPenetrationCount(expectedPenetrationInput, UseLineInfo, screwLine, lineInput.Object, doc, tolerance);
                if (expectedPenetrationCount > 0 && bodyHits.Count != expectedPenetrationCount)
                {
                    errors.Add($"Line {inputIndex}: 检测到穿透层数 {bodyHits.Count}，与要求的穿透层数 {expectedPenetrationCount} 不一致。");
                    string mismatchAction = GetMismatchAction(mismatchActionInput, screwLine, lineInput.Object, doc, tolerance);
                    if (mismatchAction == ScrewPenetrationMismatchActionParam.InvalidAction)
                    {
                        invalidLines.Add(screwLine);
                        report.Add($"Line {inputIndex}: 穿透层数不一致，处理方式为无效，已跳过此螺丝。");
                        continue;
                    }
                }

                List<PartHit> processHits = ruleSet.Process != null
                    ? GetProcessHits(parts, screwLine, processLength, tolerance)
                    : new List<PartHit>();
                if (bodyHits.Count == 0 && processHits.Count == 0)
                {
                    errors.Add($"Line {inputIndex}: 正向螺钉代表线和反向工艺孔搜索线都没有命中实体。");
                    invalidLines.Add(screwLine);
                    continue;
                }

                bodyHits.Sort((a, b) => a.Parameter.CompareTo(b.Parameter));
                processHits.Sort((a, b) => a.Parameter.CompareTo(b.Parameter));
                List<string> roleOverrides = UseLineInfo ? new List<string>() : roles;
                List<string> lineRoleTokens = UseLineInfo
                    ? GetLineRoleTokens(GetIndexedListValue(roles, inputIndex), true, screwLine, lineInput.Object, doc, tolerance)
                    : new List<string>();

                int tapCountBeforeLine = tapCutters.Count;
                int clearanceCountBeforeLine = clearanceCutters.Count;
                HashSet<int> bodyHitPartIndices = new HashSet<int>(bodyHits.Select(hit => hit.PartIndex));
                HashSet<int> processedProcessPartIndices = new HashSet<int>();
                int processRoleCount = 0;
                for (int hitIndex = 0; hitIndex < processHits.Count; hitIndex++)
                {
                    PartHit processHit = processHits[hitIndex];
                    string processRole = GetRole(roleOverrides, inputIndex, processHit.PartIndex, hitIndex, lineRoleTokens);
                    bool autoProcess = bodyHitPartIndices.Contains(processHit.PartIndex);
                    if (processRole != "工艺孔" && !autoProcess)
                        continue;

                    if (processRole == "工艺孔")
                        processRoleCount++;
                    if (!processedProcessPartIndices.Add(processHit.PartIndex))
                        continue;

                    AddHole(inputIndex, processHit.PartIndex, parts[processHit.PartIndex], ReverseExtensionLine(screwLine, processLength), processHit.Point, ruleSet.Process, tolerance, 0.0, 0.0, false, Point3d.Unset,
                        cuttersByPart, allCutters, tapCutters, clearanceCutters, processCutters, holePoints, holeAxes, holeNames, holeTypes, report);
                }

                List<PartHit> mainHits = bodyHits;
                Point3d screwStartPoint = mainHits.Count > 0 ? mainHits[0].Point : Point3d.Unset;
                for (int hitIndex = 0; hitIndex < mainHits.Count; hitIndex++)
                {
                    PartHit hit = mainHits[hitIndex];
                    string role = GetRole(roleOverrides, inputIndex, hit.PartIndex, processRoleCount + hitIndex, lineRoleTokens);
                    if (role == "跳过")
                    {
                        report.Add($"Line {inputIndex}, Part {hit.PartIndex}: 按孔角色跳过。");
                        continue;
                    }

                    if (role == "工艺孔")
                    {
                        if (ruleSet.Process == null)
                        {
                            errors.Add($"Line {inputIndex}, Part {hit.PartIndex}: 指定为工艺孔，但规格缺少工艺孔规则。");
                            continue;
                        }

                        AddHole(inputIndex, hit.PartIndex, parts[hit.PartIndex], screwLine, hit.Point, ruleSet.Process, tolerance, 0.0, 0.0, false, Point3d.Unset,
                            cuttersByPart, allCutters, tapCutters, clearanceCutters, processCutters, holePoints, holeAxes, holeNames, holeTypes, report);
                        continue;
                    }

                    HoleRule mainRule = GetMainRule(role, hitIndex, mainHits.Count, ruleSet, inputIndex, hit.PartIndex, errors, report);
                    if (mainRule != null)
                    {
                        if (screwLength <= tolerance)
                        {
                            errors.Add($"Line {inputIndex}: 规格“{rawSpec}”缺少螺栓长度。请使用类似 ST4.8*16 的写法；没有长度时不生成主孔。");
                            continue;
                        }

                        AddHole(inputIndex, hit.PartIndex, parts[hit.PartIndex], screwLine, hit.Point, mainRule, tolerance, 0.0, screwLength, true, screwStartPoint,
                            cuttersByPart, allCutters, tapCutters, clearanceCutters, processCutters, holePoints, holeAxes, holeNames, holeTypes, report);
                    }
                }

                if (tapCutters.Count > tapCountBeforeLine || clearanceCutters.Count > clearanceCountBeforeLine)
                {
                    validLines.Add(screwLine);
                    if (!validSpecCounts.ContainsKey(tableSpec))
                        validSpecCounts[tableSpec] = 0;
                    validSpecCounts[tableSpec]++;
                }
                else
                {
                    invalidLines.Add(screwLine);
                    report.Add($"Line {inputIndex}: 没有成功生成底孔或过孔，按无效螺栓线统计。");
                }
            }

            List<string> specCountReport = validSpecCounts
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.Key} x {item.Value}")
                .ToList();

            if (allCutters.Count == 0)
                errors.Add("本次没有生成任何切削体。请检查：螺钉线是否穿过开孔实体、规格是否能在孔数据库中匹配、孔类型是否被设置为跳过。");

            List<Brep> resultBreps = BuildResultBreps(parts, cuttersByPart, shouldRun, tolerance, errors);
            if (shouldRun)
            {
                _lastOutputs.Set(resultBreps, allCutters, tapCutters, clearanceCutters, processCutters, holePoints, holeAxes, holeNames, holeTypes, report, errors);
                _hasLastOutputs = true;
                _lastInputSignature = inputSignature;
            }
            else if (_hasLastOutputs)
            {
                _lastOutputs.CopyTo(resultBreps, allCutters, tapCutters, clearanceCutters, processCutters, holePoints, holeAxes, holeNames, holeTypes, report, errors);
            }

            DA.SetDataList(0, resultBreps);
            DA.SetDataList(1, allCutters);
            DA.SetDataList(2, tapCutters);
            DA.SetDataList(3, clearanceCutters);
            DA.SetDataList(4, processCutters);
            DA.SetDataList(5, holePoints);
            DA.SetDataList(6, holeAxes);
            DA.SetDataList(7, holeNames);
            DA.SetDataList(8, holeTypes);
            DA.SetDataList(9, report);
            DA.SetDataList(10, errors);
            DA.SetDataList(11, validLines);
            DA.SetDataList(12, invalidLines);
            DA.SetDataList(13, specCountReport);

            if (errors.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", errors));
        }

        private static HoleRule GetMainRule(string role, int hitIndex, int hitCount, HoleRuleSet ruleSet, int lineIndex, int partIndex, List<string> errors, List<string> report)
        {
            if (role == "底孔")
            {
                if (ruleSet.Tap != null)
                    return ruleSet.Tap;

                errors.Add($"Line {lineIndex}, Part {partIndex}: 指定为底孔，但规格缺少底孔规则。");
                return null;
            }

            if (role == "过孔")
            {
                if (ruleSet.Clearance != null)
                    return ruleSet.Clearance;

                errors.Add($"Line {lineIndex}, Part {partIndex}: 指定为过孔，但规格缺少过孔规则。");
                return null;
            }

            if (hitCount == 1)
            {
                report.Add($"Line {lineIndex}, Part {partIndex}: 单层相交默认按过孔处理；如需底孔，请输入孔角色。");
                if (ruleSet.Clearance != null)
                    return ruleSet.Clearance;

                errors.Add($"Line {lineIndex}, Part {partIndex}: 单层默认过孔，但规格缺少过孔规则。");
                return null;
            }

            if (hitIndex == hitCount - 1)
            {
                if (ruleSet.Tap != null)
                    return ruleSet.Tap;

                errors.Add($"Line {lineIndex}, Part {partIndex}: 自动判定为底孔，但规格缺少底孔规则。");
                return null;
            }

            if (ruleSet.Clearance != null)
                return ruleSet.Clearance;

            errors.Add($"Line {lineIndex}, Part {partIndex}: 自动判定为过孔，但规格缺少过孔规则。");
            return null;
        }

        private static Point3d GetScrewStartPoint(List<PartHit> bodyHits, List<string> hitRoles)
        {
            for (int i = 0; i < bodyHits.Count; i++)
            {
                string role = i < hitRoles.Count ? hitRoles[i] : string.Empty;
                if (role == "工艺孔")
                    continue;

                return bodyHits[i].Point;
            }

            return Point3d.Unset;
        }

        private static void AddHole(
            int lineIndex,
            int partIndex,
            Brep part,
            Line axisSource,
            Point3d center,
            HoleRule rule,
            double tolerance,
            double inputCutterLength,
            double screwLength,
            bool useScrewStartPoint,
            Point3d screwStartPoint,
            List<List<Brep>> cuttersByPart,
            List<Brep> allCutters,
            List<Brep> tapCutters,
            List<Brep> clearanceCutters,
            List<Brep> processCutters,
            List<Point3d> holePoints,
            List<Line> holeAxes,
            List<string> holeNames,
            List<string> holeTypes,
            List<string> report)
        {
            double length = GetCutterLength(part, axisSource, rule, tolerance, inputCutterLength, screwLength);
            bool useStartPoint = useScrewStartPoint && screwStartPoint.IsValid && (inputCutterLength > tolerance || screwLength > tolerance);
            Brep cutter = CreateHoleCutter(center, axisSource.Direction, rule, length, tolerance, useStartPoint, screwStartPoint);
            if (cutter == null)
            {
                report.Add($"Line {lineIndex}, Part {partIndex}: {rule.Type} 切削体生成失败。");
                return;
            }

            Line axis = useStartPoint
                ? CreateAxisLineFromStart(screwStartPoint, axisSource.Direction, length)
                : CreateAxisLine(center, axisSource.Direction, length);
            cuttersByPart[partIndex].Add(cutter);
            allCutters.Add(cutter);
            holePoints.Add(center);
            holeAxes.Add(axis);
            holeNames.Add(rule.Name);
            holeTypes.Add(rule.Type);
            if (rule.IsCountersink)
            {
                report.Add($"Line {lineIndex}, Part {partIndex}: {rule.Type} {rule.Name}，沉头 D1={rule.HeadDiameter.ToString("G17", CultureInfo.InvariantCulture)}，D2={rule.Diameter.ToString("G17", CultureInfo.InvariantCulture)}，D1直段t={rule.CountersinkDepth.ToString("G17", CultureInfo.InvariantCulture)}，90°过渡，总长 {length.ToString("G17", CultureInfo.InvariantCulture)}。");
            }
            else
            {
                report.Add($"Line {lineIndex}, Part {partIndex}: {rule.Type} {rule.Name}，直径 {rule.Diameter.ToString("G17", CultureInfo.InvariantCulture)}。");
            }

            if (rule.Type == "底孔")
                tapCutters.Add(cutter);
            else if (rule.Type == "过孔")
                clearanceCutters.Add(cutter);
            else if (rule.Type == "工艺孔")
                processCutters.Add(cutter);
        }

        private static double GetCutterLength(Brep part, Line axis, HoleRule rule, double tolerance, double inputCutterLength, double screwLength)
        {
            if (inputCutterLength > tolerance)
                return inputCutterLength;

            if ((rule.Type == "底孔" || rule.Type == "过孔") && screwLength > tolerance)
                return screwLength;

            return EstimateCutterLength(part, axis, rule.Diameter, tolerance);
        }

        private static List<Brep> BuildResultBreps(List<Brep> parts, List<List<Brep>> cuttersByPart, bool run, double tolerance, List<string> errors)
        {
            List<Brep> result = new List<Brep>();
            for (int i = 0; i < parts.Count; i++)
            {
                Brep source = parts[i];
                if (source == null)
                    continue;

                if (!run)
                    continue;

                if (cuttersByPart[i].Count == 0)
                {
                    result.Add(source.DuplicateBrep());
                    continue;
                }

                Brep[] diff = null;
                try
                {
                    diff = Brep.CreateBooleanDifference(new[] { source.DuplicateBrep() }, cuttersByPart[i], tolerance);
                }
                catch (Exception ex)
                {
                    errors.Add($"Part {i}: 布尔差集异常：{ex.Message}");
                }

                if (diff == null || diff.Length == 0)
                {
                    errors.Add($"Part {i}: 布尔差集失败，输出原实体。");
                    result.Add(source.DuplicateBrep());
                }
                else
                {
                    double sourceVolume = GetBrepVolume(source);
                    double resultVolume = diff.Sum(GetBrepVolume);
                    if (sourceVolume > tolerance && Math.Abs(sourceVolume - resultVolume) <= Math.Max(tolerance, sourceVolume * 1e-6))
                        errors.Add($"Part {i}: 布尔差集完成，但实体体积几乎没有变化。请检查切削体是否真正穿过实体、规格长度是否足够，或调整容差。");

                    result.AddRange(diff);
                }
            }

            return result;
        }

        private static double GetBrepVolume(Brep brep)
        {
            if (brep == null)
                return 0.0;

            try
            {
                return Math.Abs(brep.GetVolume());
            }
            catch
            {
                return 0.0;
            }
        }

        private static List<PartHit> GetBodyHits(List<Brep> parts, Line screwLine, double tolerance)
        {
            List<PartHit> hits = new List<PartHit>();
            LineCurve curve = new LineCurve(screwLine);

            for (int i = 0; i < parts.Count; i++)
            {
                Point3d point;
                if (TryGetFirstIntersection(parts[i], curve, screwLine, tolerance, out point, out double parameter))
                    hits.Add(new PartHit(i, parameter, point));
            }

            return hits;
        }

        private static List<PartHit> GetProcessHits(List<Brep> parts, Line screwLine, double processLength, double tolerance)
        {
            Line extension = ReverseExtensionLine(screwLine, processLength);
            LineCurve curve = new LineCurve(extension);
            List<PartHit> hits = new List<PartHit>();

            for (int i = 0; i < parts.Count; i++)
            {
                Point3d point;
                if (TryGetFirstIntersection(parts[i], curve, extension, tolerance, out point, out double parameter))
                    hits.Add(new PartHit(i, parameter, point));
            }

            return hits;
        }

        private static Point3d GetProcessPoint(Brep part, Line screwLine, double processLength, double tolerance)
        {
            Line extension = ReverseExtensionLine(screwLine, processLength);
            LineCurve curve = new LineCurve(extension);
            if (TryGetFirstIntersection(part, curve, extension, tolerance, out Point3d point, out double _))
                return point;

            return extension.From;
        }

        private static bool TryGetFirstIntersection(Brep brep, LineCurve curve, Line line, double tolerance, out Point3d point, out double lineParameter)
        {
            point = Point3d.Unset;
            lineParameter = 0.0;
            if (brep == null)
                return false;

            Curve[] overlapCurves;
            Point3d[] points;
            if (!Intersection.CurveBrep(curve, brep, tolerance, out overlapCurves, out points))
                return false;

            List<Point3d> candidates = new List<Point3d>();
            if (points != null)
                candidates.AddRange(points);
            if (overlapCurves != null)
            {
                foreach (Curve overlap in overlapCurves)
                {
                    if (overlap == null)
                        continue;
                    candidates.Add(overlap.PointAtStart);
                    candidates.Add(overlap.PointAtEnd);
                }
            }

            if (candidates.Count == 0)
                return false;

            double best = double.MaxValue;
            Point3d bestPoint = candidates[0];
            foreach (Point3d candidate in candidates)
            {
                double t = NormalizedLineParameter(line, candidate);
                if (t < best)
                {
                    best = t;
                    bestPoint = candidate;
                }
            }

            point = bestPoint;
            lineParameter = best;
            return true;
        }

        private static double NormalizedLineParameter(Line line, Point3d point)
        {
            Vector3d direction = line.Direction;
            double lengthSquared = direction.SquareLength;
            if (lengthSquared <= RhinoMath.ZeroTolerance)
                return 0.0;

            return ((point - line.From) * direction) / lengthSquared;
        }

        private static Line ReverseExtensionLine(Line screwLine, double length)
        {
            Vector3d direction = screwLine.Direction;
            direction.Unitize();
            return new Line(screwLine.From, screwLine.From - direction * length);
        }

        private static Brep CreateHoleCutter(Point3d center, Vector3d direction, HoleRule rule, double length, double tolerance, bool useStartPoint, Point3d startPoint)
        {
            if (!direction.Unitize())
                return null;

            if (!rule.IsCountersink)
            {
                return useStartPoint
                    ? CreateCylinderCutterFromStart(startPoint, direction, rule.Diameter, length, tolerance)
                    : CreateCylinderCutter(center, direction, rule.Diameter, length, tolerance);
            }

            Point3d start = useStartPoint
                ? startPoint
                : center - direction * (length * 0.5);
            return CreateCountersinkCutterFromStart(start, direction, rule.HeadDiameter, rule.Diameter, rule.CountersinkDepth, length, tolerance);
        }

        private static Brep CreateCylinderCutter(Point3d center, Vector3d direction, double diameter, double length, double tolerance)
        {
            if (!direction.Unitize() || diameter <= tolerance || length <= tolerance)
                return null;

            Point3d baseCenter = center - direction * (length * 0.5);
            Plane plane = new Plane(baseCenter, direction);
            Circle circle = new Circle(plane, diameter * 0.5);
            Cylinder cylinder = new Cylinder(circle, length);
            return cylinder.ToBrep(true, true);
        }

        private static Brep CreateCylinderCutterFromStart(Point3d start, Vector3d direction, double diameter, double length, double tolerance)
        {
            if (!direction.Unitize() || diameter <= tolerance || length <= tolerance)
                return null;

            Plane plane = new Plane(start, direction);
            Circle circle = new Circle(plane, diameter * 0.5);
            Cylinder cylinder = new Cylinder(circle, length);
            return cylinder.ToBrep(true, true);
        }

        private static Brep CreateCountersinkCutterFromStart(Point3d start, Vector3d direction, double headDiameter, double shankDiameter, double countersinkDepth, double totalLength, double tolerance)
        {
            if (!direction.Unitize() || headDiameter <= tolerance || shankDiameter <= tolerance || countersinkDepth < 0 || totalLength <= tolerance)
                return null;

            double straightDepth = Math.Min(countersinkDepth, Math.Max(totalLength - tolerance, 0.0));
            Brep cylinder = CreateCylinderCutterFromStart(start, direction, shankDiameter, totalLength, tolerance);
            if (straightDepth <= tolerance || headDiameter <= shankDiameter + tolerance)
                return cylinder;

            double transitionDepth = (headDiameter - shankDiameter) * 0.5;
            double headDepth = Math.Min(straightDepth + transitionDepth, Math.Max(totalLength - tolerance, straightDepth));
            Brep head = CreateCountersinkHead(start, direction, headDiameter, shankDiameter, straightDepth, headDepth, tolerance);
            if (head == null)
                return cylinder;

            Brep[] union = null;
            try
            {
                union = Brep.CreateBooleanUnion(new[] { cylinder, head }, tolerance);
            }
            catch
            {
                union = null;
            }

            if (union != null && union.Length > 0)
                return union[0];

            Brep combined = cylinder.DuplicateBrep();
            combined.Append(head);
            return combined;
        }

        private static Brep CreateCountersinkHead(Point3d start, Vector3d direction, double headDiameter, double shankDiameter, double straightDepth, double headDepth, double tolerance)
        {
            Vector3d xAxis = Vector3d.CrossProduct(direction, Vector3d.ZAxis);
            if (xAxis.SquareLength <= RhinoMath.ZeroTolerance)
                xAxis = Vector3d.CrossProduct(direction, Vector3d.XAxis);
            if (!xAxis.Unitize())
                return null;

            double headRadius = headDiameter * 0.5;
            double shankRadius = shankDiameter * 0.5;
            Point3d p0 = start;
            Point3d p1 = start + xAxis * headRadius;
            Point3d p2 = start + direction * straightDepth + xAxis * headRadius;
            Point3d p3 = start + direction * headDepth + xAxis * shankRadius;
            Point3d p4 = start + direction * headDepth;

            Polyline profile = new Polyline(new[] { p0, p1, p2, p3, p4, p0 });
            if (!profile.IsValid)
                return null;

            Line revolveAxis = new Line(start, start + direction);
            using (Curve profileCurve = profile.ToNurbsCurve())
            {
                RevSurface surface = RevSurface.Create(profileCurve, revolveAxis, 0.0, Math.PI * 2.0);
                return surface?.ToBrep();
            }
        }

        private static Line CreateAxisLine(Point3d center, Vector3d direction, double length)
        {
            direction.Unitize();
            return new Line(center - direction * (length * 0.5), center + direction * (length * 0.5));
        }

        private static Line CreateAxisLineFromStart(Point3d start, Vector3d direction, double length)
        {
            direction.Unitize();
            return new Line(start, start + direction * length);
        }

        private static double EstimateCutterLength(Brep part, Line axis, double diameter, double tolerance)
        {
            BoundingBox box = part?.GetBoundingBox(true) ?? BoundingBox.Empty;
            if (!box.IsValid)
                return Math.Max(axis.Length * 1.5, Math.Max(diameter * 4.0, tolerance * 100.0));

            Vector3d direction = axis.Direction;
            if (!direction.Unitize())
                return Math.Max(box.Diagonal.Length, Math.Max(diameter * 4.0, tolerance * 100.0));

            Point3d[] corners = box.GetCorners();
            double min = double.MaxValue;
            double max = double.MinValue;
            Point3d origin = axis.From;
            foreach (Point3d corner in corners)
            {
                double value = (corner - origin) * direction;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            double projectedLength = Math.Max(0.0, max - min);
            double margin = Math.Max(diameter * 2.0, tolerance * 20.0);
            return Math.Max(projectedLength + margin, Math.Max(diameter * 4.0, tolerance * 100.0));
        }

        private static double EstimateProcessSearchLength(IEnumerable<Brep> parts, IEnumerable<Line> screwLines, double tolerance)
        {
            BoundingBox box = BoundingBox.Empty;
            if (parts != null)
            {
                foreach (Brep part in parts)
                {
                    if (part == null)
                        continue;

                    BoundingBox partBox = part.GetBoundingBox(true);
                    if (partBox.IsValid)
                        box.Union(partBox);
                }
            }

            double maxLineLength = 0.0;
            if (screwLines != null)
            {
                foreach (Line line in screwLines)
                {
                    if (line.IsValid)
                        maxLineLength = Math.Max(maxLineLength, line.Length);
                }
            }

            double boxLength = box.IsValid ? box.Diagonal.Length * 1.2 : 0.0;
            return Math.Max(Math.Max(boxLength, maxLineLength * 3.0), tolerance * 100.0);
        }

        private static string NormalizeSpec(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
                return string.Empty;

            string value = StripScrewHeadPrefix(spec.Trim());
            int xIndex = value.IndexOfAny(new[] { 'x', 'X', '*', '×' });
            if (xIndex > 0)
                value = value.Substring(0, xIndex).Trim();

            return value;
        }

        private static string StripScrewHeadPrefix(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
                return string.Empty;

            string value = spec.Trim();
            if (value.StartsWith("PH-", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("FH-", StringComparison.OrdinalIgnoreCase))
                return value.Substring(3).Trim();

            return value;
        }

        private static HoleMode ResolveLineHoleMode(string rawSpec, HoleMode currentMode)
        {
            if (currentMode != HoleMode.ByScrewSpec)
                return currentMode;

            if (string.IsNullOrWhiteSpace(rawSpec))
                return HoleMode.ByScrewSpec;

            string value = rawSpec.Trim();
            if (value.StartsWith("PH-", StringComparison.OrdinalIgnoreCase))
                return HoleMode.Round;
            if (value.StartsWith("FH-", StringComparison.OrdinalIgnoreCase))
                return HoleMode.Countersink;

            return HoleMode.ByScrewSpec;
        }

        private static string GetHoleModeDisplayName(HoleMode mode)
        {
            if (mode == HoleMode.Round)
                return "圆孔";
            if (mode == HoleMode.Countersink)
                return "沉头孔";
            return "按螺钉线规格";
        }

        private static double ParseSpecLength(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
                return 0.0;

            string value = spec.Trim();
            int xIndex = value.IndexOfAny(new[] { 'x', 'X', '*', '×' });
            if (xIndex < 0 || xIndex >= value.Length - 1)
                return 0.0;

            string lengthText = value.Substring(xIndex + 1).Trim();
            int end = 0;
            while (end < lengthText.Length)
            {
                char c = lengthText[end];
                if (char.IsDigit(c) || c == '.' || c == ',')
                    end++;
                else
                    break;
            }

            if (end <= 0)
                return 0.0;

            lengthText = lengthText.Substring(0, end).Replace(',', '.');
            if (double.TryParse(lengthText, NumberStyles.Float, CultureInfo.InvariantCulture, out double length))
                return length;

            return 0.0;
        }

        private void UpdateDisplaySpec(string spec)
        {
            DisplaySpec = string.IsNullOrWhiteSpace(spec) ? string.Empty : spec.Trim();
        }

        private string BuildInputSignature(
            List<Brep> parts,
            List<IGH_Goo> screwLineInputs,
            string spec,
            List<string> lineSpecs,
            string tablePath,
            List<string> roles,
            int expectedPenetrationInput,
            string mismatchActionInput,
            double tolerance)
        {
            return string.Join("\n", new[]
            {
                UseLineInfo ? "LineInfo" : "SingleSpec",
                CurrentHoleMode.ToString(),
                NormalizeSignatureText(spec),
                NormalizeSignatureText(tablePath),
                expectedPenetrationInput.ToString(CultureInfo.InvariantCulture),
                NormalizeSignatureText(mismatchActionInput),
                tolerance.ToString("R", CultureInfo.InvariantCulture),
                string.Join("|", (lineSpecs ?? new List<string>()).Select(NormalizeSignatureText)),
                string.Join("|", (roles ?? new List<string>()).Select(NormalizeSignatureText)),
                string.Join("|", (parts ?? new List<Brep>()).Select(GetBrepSignature)),
                string.Join("|", (screwLineInputs ?? new List<IGH_Goo>()).Select(GetGooSignature))
            });
        }

        private static string NormalizeSignatureText(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }

        private static string GetBrepSignature(Brep brep)
        {
            if (brep == null)
                return "<null>";

            BoundingBox box = brep.GetBoundingBox(false);
            if (!box.IsValid)
                return "bbox:<invalid>";

            return string.Format(
                CultureInfo.InvariantCulture,
                "bbox:{0:R},{1:R},{2:R}-{3:R},{4:R},{5:R}",
                box.Min.X,
                box.Min.Y,
                box.Min.Z,
                box.Max.X,
                box.Max.Y,
                box.Max.Z);
        }

        private static string GetGooSignature(IGH_Goo goo)
        {
            if (goo == null)
                return "<null>";

            if (goo.CastTo(out Line line))
                return GetLineSignature(line);

            if (goo.CastTo(out Guid guid))
                return "guid:" + guid.ToString("D");

            if (goo.CastTo(out string text))
                return "text:" + NormalizeSignatureText(text);

            if (goo.CastTo(out Curve curve))
            {
                if (curve.IsLinear(0.0))
                    return GetLineSignature(new Line(curve.PointAtStart, curve.PointAtEnd));

                BoundingBox box = curve.GetBoundingBox(false);
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "curve:{0}:{1:R},{2:R},{3:R}-{4:R},{5:R},{6:R}",
                    curve.GetType().FullName,
                    box.Min.X,
                    box.Min.Y,
                    box.Min.Z,
                    box.Max.X,
                    box.Max.Y,
                    box.Max.Z);
            }

            object value = goo.ScriptVariable();
            return value == null
                ? goo.TypeName
                : goo.TypeName + ":" + NormalizeSignatureText(value.ToString());
        }

        private static string GetLineSignature(Line line)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "line:{0:R},{1:R},{2:R}-{3:R},{4:R},{5:R}",
                line.From.X,
                line.From.Y,
                line.From.Z,
                line.To.X,
                line.To.Y,
                line.To.Z);
        }

        private void SetCachedOutputs(IGH_DataAccess DA, bool inputChanged)
        {
            List<string> errors = new List<string>();
            if (inputChanged)
            {
                _hasLastOutputs = false;
                _lastInputSignature = string.Empty;
                errors.Add("输入已变化，请点击组件底部 Run 重新计算。");
                SetEmptyOutputs(DA, errors);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", errors));
                return;
            }

            if (_hasLastOutputs)
                SetOutputsFromCache(DA, errors);
            else
            {
                errors.Add("未点击 Run，本次未计算，也没有可用的上一次缓存结果。");
                SetEmptyOutputs(DA, errors);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", errors));
            }
        }

        private void SetOutputsFromCache(IGH_DataAccess DA, List<string> errors)
        {
            List<Brep> resultBreps = new List<Brep>();
            List<Brep> allCutters = new List<Brep>();
            List<Brep> tapCutters = new List<Brep>();
            List<Brep> clearanceCutters = new List<Brep>();
            List<Brep> processCutters = new List<Brep>();
            List<Point3d> holePoints = new List<Point3d>();
            List<Line> holeAxes = new List<Line>();
            List<string> holeNames = new List<string>();
            List<string> holeTypes = new List<string>();
            List<string> report = new List<string>();
            List<Line> validLines = new List<Line>();
            List<Line> invalidLines = new List<Line>();
            List<string> specCountReport = new List<string>();

            _lastOutputs.CopyTo(resultBreps, allCutters, tapCutters, clearanceCutters, processCutters, holePoints, holeAxes, holeNames, holeTypes, report, errors);
            report.Add("未点击 Run，本次未重新计算；当前输出为上一次缓存结果。");
            errors.Add("未点击 Run，本次未重新计算；如已修改参数，请点击组件底部 Run。");
            SetOutputs(DA, resultBreps, allCutters, tapCutters, clearanceCutters, processCutters, holePoints, holeAxes, holeNames, holeTypes, report, errors, validLines, invalidLines, specCountReport);
        }

        private static void SetEmptyOutputs(IGH_DataAccess DA, List<string> errors)
        {
            SetOutputs(
                DA,
                new List<Brep>(),
                new List<Brep>(),
                new List<Brep>(),
                new List<Brep>(),
                new List<Brep>(),
                new List<Point3d>(),
                new List<Line>(),
                new List<string>(),
                new List<string>(),
                new List<string>(),
                errors ?? new List<string>(),
                new List<Line>(),
                new List<Line>(),
                new List<string>());
        }

        private static void SetOutputs(
            IGH_DataAccess DA,
            List<Brep> resultBreps,
            List<Brep> allCutters,
            List<Brep> tapCutters,
            List<Brep> clearanceCutters,
            List<Brep> processCutters,
            List<Point3d> holePoints,
            List<Line> holeAxes,
            List<string> holeNames,
            List<string> holeTypes,
            List<string> report,
            List<string> errors,
            List<Line> validLines,
            List<Line> invalidLines,
            List<string> specCountReport)
        {
            DA.SetDataList(0, resultBreps);
            DA.SetDataList(1, allCutters);
            DA.SetDataList(2, tapCutters);
            DA.SetDataList(3, clearanceCutters);
            DA.SetDataList(4, processCutters);
            DA.SetDataList(5, holePoints);
            DA.SetDataList(6, holeAxes);
            DA.SetDataList(7, holeNames);
            DA.SetDataList(8, holeTypes);
            DA.SetDataList(9, report);
            DA.SetDataList(10, errors);
            DA.SetDataList(11, validLines);
            DA.SetDataList(12, invalidLines);
            DA.SetDataList(13, specCountReport);
        }

        private static List<ScrewLineInput> ResolveScrewLineInputs(List<IGH_Goo> inputs, RhinoDoc doc, double tolerance, List<string> errors)
        {
            List<ScrewLineInput> result = new List<ScrewLineInput>();
            if (inputs == null)
                return result;

            for (int i = 0; i < inputs.Count; i++)
            {
                if (TryResolveScrewLineInput(inputs[i], doc, tolerance, out ScrewLineInput lineInput, out string error))
                {
                    lineInput.InputIndex = i;
                    result.Add(lineInput);
                }
                else
                {
                    errors.Add($"Line {i}: {error}");
                }
            }

            return result;
        }

        private static bool TryResolveScrewLineInput(IGH_Goo input, RhinoDoc doc, double tolerance, out ScrewLineInput lineInput, out string error)
        {
            lineInput = null;
            error = string.Empty;
            if (input == null)
            {
                error = "螺钉线为空。";
                return false;
            }

            Line line = Line.Unset;
            if (input.CastTo(out line))
            {
                if (!line.IsValid || line.Length <= tolerance)
                {
                    error = "螺钉代表线无效。";
                    return false;
                }

                lineInput = new ScrewLineInput { Line = line };
                return true;
            }

            Curve inputCurve = null;
            if (input.CastTo(out inputCurve))
            {
                if (!TryCurveToLine(inputCurve, tolerance, out line, out error))
                    return false;

                lineInput = new ScrewLineInput { Line = line };
                return true;
            }

            RhinoObject obj = ResolveRhinoObject(input, doc);
            if (obj == null)
            {
                error = "输入不是有效 Line、直线型 Curve，也不是可找到的 Rhino 对象 GUID。";
                return false;
            }

            CurveObject curveObject = obj as CurveObject;
            Curve curve = curveObject?.CurveGeometry;
            if (curve == null)
            {
                error = "GUID 对应的 Rhino 对象不是曲线。";
                return false;
            }

            if (!TryCurveToLine(curve, tolerance, out line, out error))
                return false;

            lineInput = new ScrewLineInput { Line = line, Object = obj };
            return true;
        }

        private static bool TryCurveToLine(Curve curve, double tolerance, out Line line, out string error)
        {
            line = Line.Unset;
            error = string.Empty;
            if (curve == null)
            {
                error = "曲线为空。";
                return false;
            }

            if (!curve.IsLinear(tolerance))
            {
                error = "曲线不是直线。";
                return false;
            }

            line = new Line(curve.PointAtStart, curve.PointAtEnd);
            if (!line.IsValid || line.Length <= tolerance)
            {
                error = "直线长度无效。";
                return false;
            }

            return true;
        }

        private static RhinoObject ResolveRhinoObject(IGH_Goo input, RhinoDoc doc)
        {
            if (doc == null || input == null)
                return null;

            Guid id = Guid.Empty;
            if (input.CastTo(out id) && id != Guid.Empty)
                return doc.Objects.FindId(id);

            string text = null;
            if (input.CastTo(out text) && Guid.TryParse(text, out id) && id != Guid.Empty)
                return doc.Objects.FindId(id);

            object value = input.ScriptVariable();
            if (value is Guid guid && guid != Guid.Empty)
                return doc.Objects.FindId(guid);
            if (value is string guidText && Guid.TryParse(guidText, out id) && id != Guid.Empty)
                return doc.Objects.FindId(id);
            if (value is RhinoObject rhinoObject)
                return rhinoObject;
            if (value is ObjRef objRef)
                return objRef.Object();

            return null;
        }

        private static string GetSingleSpec(string spec, string dialogSpec)
        {
            if (!string.IsNullOrWhiteSpace(spec))
                return spec;

            return dialogSpec;
        }

        private static string GetLineInfoSpec(string lineSpec, Line screwLine, RhinoObject lineObject, RhinoDoc doc, double tolerance)
        {
            if (!string.IsNullOrWhiteSpace(lineSpec))
                return lineSpec;

            return GetLineUserStringSpec(lineObject, doc, screwLine, tolerance);
        }

        private static string GetLineUserStringSpec(RhinoObject lineObject, RhinoDoc doc, Line screwLine, double tolerance)
        {
            return GetLineUserString(lineObject, doc, screwLine, tolerance, "螺栓规格", "规格", "孔规格", "Spec");
        }

        private static List<string> GetLineRoleTokens(string holeTypes, bool allowUserStringFallback, Line screwLine, RhinoObject lineObject, RhinoDoc doc, double tolerance)
        {
            string text = holeTypes;
            if (string.IsNullOrWhiteSpace(text) && allowUserStringFallback)
                text = GetLineUserString(lineObject, doc, screwLine, tolerance, "孔类型", "孔角色", "Role", "HoleTypes");

            return SplitRoleTokens(text);
        }

        private static int GetExpectedPenetrationCount(int inputCount, bool allowUserStringFallback, Line screwLine, RhinoObject lineObject, RhinoDoc doc, double tolerance)
        {
            if (inputCount > 0)
                return inputCount;

            if (allowUserStringFallback)
            {
                string value = GetLineUserString(lineObject, doc, screwLine, tolerance, "穿透层数", "穿透实体数", "穿透实体数量", "PenetrationCount", "Penetration");
                if (int.TryParse(value, out int count) && count > 0)
                    return count;
            }

            return 0;
        }

        private static string GetMismatchAction(string inputAction, Line screwLine, RhinoObject lineObject, RhinoDoc doc, double tolerance)
        {
            if (!string.IsNullOrWhiteSpace(inputAction))
                return NormalizeMismatchAction(inputAction);

            string value = GetLineUserString(lineObject, doc, screwLine, tolerance, "处理方式", "MismatchAction", "PenetrationMismatchAction");
            return NormalizeMismatchAction(value);
        }

        private static string NormalizeMismatchAction(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ScrewPenetrationMismatchActionParam.DefaultAction;

            string text = value.Trim();
            if (string.Equals(text, "无效", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "invalid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "跳过", StringComparison.OrdinalIgnoreCase))
                return ScrewPenetrationMismatchActionParam.InvalidAction;

            return ScrewPenetrationMismatchActionParam.DefaultAction;
        }

        private static string GetLineUserString(RhinoObject lineObject, RhinoDoc doc, Line screwLine, double tolerance, params string[] keys)
        {
            RhinoObject obj = lineObject ?? FindLineObject(doc, screwLine, tolerance);
            if (obj == null)
                return string.Empty;

            foreach (string key in keys)
            {
                string value = obj.Attributes.GetUserString(key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static RhinoObject FindLineObject(RhinoDoc doc, Line screwLine, double tolerance)
        {
            if (doc == null || !screwLine.IsValid)
                return null;

            ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings
            {
                ActiveObjects = true,
                HiddenObjects = true,
                LockedObjects = true,
                ObjectTypeFilter = ObjectType.Curve
            };

            foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
            {
                CurveObject curveObject = obj as CurveObject;
                Curve curve = curveObject?.CurveGeometry;
                if (curve == null || !curve.IsLinear(tolerance))
                    continue;

                Line candidate = new Line(curve.PointAtStart, curve.PointAtEnd);
                if (IsSameLine(candidate, screwLine, tolerance))
                    return obj;
            }

            return null;
        }

        private static string ResolveTableSpec(string rawSpec)
        {
            return NormalizeSpec(rawSpec);
        }

        private static bool IsSameLine(Line a, Line b, double tolerance)
        {
            double tol = Math.Max(tolerance, 0.001);
            bool sameDirection = a.From.DistanceTo(b.From) <= tol && a.To.DistanceTo(b.To) <= tol;
            bool reverseDirection = a.From.DistanceTo(b.To) <= tol && a.To.DistanceTo(b.From) <= tol;
            return sameDirection || reverseDirection;
        }

        private static string NormalizeRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return "自动";

            string value = role.Trim();
            if (string.Equals(value, "Tap", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "dk", StringComparison.OrdinalIgnoreCase) ||
                value == "1" ||
                value.Contains("底"))
                return "底孔";
            if (string.Equals(value, "Clearance", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "gk", StringComparison.OrdinalIgnoreCase) ||
                value == "2" ||
                value.Contains("过"))
                return "过孔";
            if (string.Equals(value, "Process", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "gy", StringComparison.OrdinalIgnoreCase) ||
                value == "3" ||
                value.Contains("工艺"))
                return "工艺孔";
            if (string.Equals(value, "Skip", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "tg", StringComparison.OrdinalIgnoreCase) ||
                value == "0" ||
                value.Contains("跳"))
                return "跳过";

            return "自动";
        }

        private static string GetRole(List<string> roles, int lineIndex, int partIndex, int roleIndex, List<string> lineRoleTokens)
        {
            bool exactMode = roles != null && roles.Any(item => !string.IsNullOrWhiteSpace(item) && item.Contains("="));
            string exactPrefix = lineIndex.ToString(CultureInfo.InvariantCulture) + ":" + partIndex.ToString(CultureInfo.InvariantCulture);
            if (roles != null)
            {
                foreach (string item in roles)
                {
                    if (string.IsNullOrWhiteSpace(item))
                        continue;

                    string text = item.Trim();
                    int eqIndex = text.IndexOf('=');
                    if (eqIndex < 0)
                        continue;

                    string key = text.Substring(0, eqIndex).Trim();
                    string value = text.Substring(eqIndex + 1).Trim();
                    if (string.Equals(key, exactPrefix, StringComparison.OrdinalIgnoreCase))
                        return NormalizeRole(value);
                }
            }

            if (lineRoleTokens != null && lineRoleTokens.Count > 0)
                return NormalizeRole(GetListValue(lineRoleTokens, roleIndex));

            if (exactMode)
                return "自动";

            return NormalizeRole(GetListValue(roles, roleIndex));
        }

        private static List<string> SplitRoleTokens(string text)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            char[] separators = { ',', '，', ';', '；', '|', '/', '\\', ' ', '\t', '\r', '\n' };
            foreach (string item in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = item.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }

            return result;
        }

        private static string GetListValue(List<string> values, int index)
        {
            if (values == null || values.Count == 0)
                return string.Empty;
            if (index >= 0 && index < values.Count)
                return values[index] ?? string.Empty;
            return values[values.Count - 1] ?? string.Empty;
        }

        private static string GetIndexedListValue(List<string> values, int index)
        {
            if (values == null || index < 0 || index >= values.Count)
                return string.Empty;

            return values[index] ?? string.Empty;
        }

        private static string ResolveDatabasePath(string rawPath, string baseDirectory)
        {
            string cleanedPath = CleanDatabasePath(rawPath);
            if (string.IsNullOrWhiteSpace(cleanedPath))
                return string.Empty;

            try
            {
                string expandedPath = System.Environment.ExpandEnvironmentVariables(cleanedPath);
                if (Path.IsPathRooted(expandedPath))
                    return Path.GetFullPath(expandedPath);

                if (!string.IsNullOrWhiteSpace(baseDirectory))
                    return Path.GetFullPath(Path.Combine(baseDirectory, expandedPath));

                return Path.GetFullPath(expandedPath);
            }
            catch
            {
                return cleanedPath;
            }
        }

        private static string CleanDatabasePath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return string.Empty;

            string value = rawPath.Trim();
            while (value.Length >= 2 &&
                   ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                    (value.StartsWith("'") && value.EndsWith("'"))))
            {
                value = value.Substring(1, value.Length - 2).Trim();
            }

            return value;
        }

        private static string FormatDatabaseNotFoundMessage(string rawPath, string resolvedPath, string baseDirectory)
        {
            string raw = string.IsNullOrWhiteSpace(rawPath) ? "<空>" : rawPath;
            string cleaned = CleanDatabasePath(rawPath);
            string resolved = string.IsNullOrWhiteSpace(resolvedPath) ? "<空>" : resolvedPath;
            string baseDir = string.IsNullOrWhiteSpace(baseDirectory) ? "<无，可能 GH 文件尚未保存>" : baseDirectory;
            return $"孔数据库不存在或路径无法访问。\n原始输入：{raw}\n清理后路径：{(string.IsNullOrWhiteSpace(cleaned) ? "<空>" : cleaned)}\n实际检查路径：{resolved}\n相对路径基准目录：{baseDir}\n请确认 .xls 文件路径真实存在，且路径没有多余引号、空格或不可见字符。";
        }

        private static bool IsExistingDatabasePath(string rawPath, string baseDirectory)
        {
            string resolvedPath = ResolveDatabasePath(rawPath, baseDirectory);
            return !string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath);
        }

        internal string GetCurrentDatabasePath()
        {
            string inputPath = GetInputText(DatabaseInputIndex);
            return string.IsNullOrWhiteSpace(inputPath) ? LastTablePath : inputPath;
        }

        private string GetInputText(int inputIndex)
        {
            if (inputIndex < 0 || inputIndex >= Params.Input.Count)
                return string.Empty;

            IGH_Param param = Params.Input[inputIndex];
            if (param == null || param.VolatileDataCount == 0)
                return string.Empty;

            foreach (IGH_Goo item in param.VolatileData.AllData(true))
            {
                if (item == null)
                    continue;

                string text = null;
                if (item.CastTo(out text) && !string.IsNullOrWhiteSpace(text))
                    return text;

                object value = item.ScriptVariable();
                if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                    return value.ToString();
            }

            return string.Empty;
        }

        private string GetDocumentDirectory()
        {
            string documentPath = OnPingDocument()?.FilePath;
            if (string.IsNullOrWhiteSpace(documentPath))
                return string.Empty;

            try
            {
                return Path.GetDirectoryName(documentPath) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static HoleRuleTables ReadHoleTables(string path, int startRow, HoleMode mode, List<string> errors, string baseDirectory)
        {
            HoleRuleTables result = new HoleRuleTables();
            string resolvedPath = ResolveDatabasePath(path, baseDirectory);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                errors.Add(FormatDatabaseNotFoundMessage(path, resolvedPath, baseDirectory));
                return result;
            }

            Excel.Application app = null;
            Excel.Workbook workbook = null;
            Excel.Worksheet sheet = null;
            bool appCreated = false;

            try
            {
                try
                {
                    app = (Excel.Application)Marshal.GetActiveObject("Excel.Application");
                }
                catch
                {
                    app = new Excel.Application();
                    appCreated = true;
                }

                workbook = app.Workbooks.Open(resolvedPath, ReadOnly: true);
                sheet = GetWorksheetOrFirst(workbook, "圆孔", errors);
                if (sheet != null)
                    ReadRoundHoleSheet(sheet, startRow, result.Round);

                if (mode == HoleMode.Countersink || mode == HoleMode.ByScrewSpec)
                {
                    ReleaseCom(sheet);
                    sheet = GetWorksheet(workbook, "沉头孔", errors);
                    if (sheet != null)
                        ReadCountersinkHoleSheet(sheet, startRow, result.Countersink, result.Round, errors);
                }
            }
            catch (Exception ex)
            {
                errors.Add("读取孔数据库失败：" + ex.Message);
            }
            finally
            {
                if (workbook != null)
                    workbook.Close(false);
                ReleaseCom(sheet);
                ReleaseCom(workbook);
                if (app != null && appCreated)
                    app.Quit();
                ReleaseCom(app);
            }

            return result;
        }

        private static void ReadRoundHoleSheet(Excel.Worksheet sheet, int startRow, Dictionary<string, HoleRuleSet> result)
        {
            Excel.Range usedRange = sheet?.UsedRange;
            int lastRow = usedRange?.Rows.Count ?? 0;
            ReleaseCom(usedRange);

            for (int row = startRow; row <= lastRow; row++)
            {
                string spec = ReadCell(sheet, row, 1);
                if (string.IsNullOrWhiteSpace(spec))
                    continue;

                HoleRuleSet set = GetOrCreateRuleSet(result, spec);
                double tapDiameter = ReadDoubleCell(sheet, row, 2);
                string tapName = ReadCell(sheet, row, 3);
                double clearanceDiameter = ReadDoubleCell(sheet, row, 4);
                string clearanceName = ReadCell(sheet, row, 5);
                double processDiameter = ReadDoubleCell(sheet, row, 6);
                string processName = ReadCell(sheet, row, 7);

                if (tapDiameter > 0)
                    set.Tap = new HoleRule("底孔", tapDiameter, string.IsNullOrWhiteSpace(tapName) ? $"Φ{tapDiameter:G}底孔" : tapName);
                if (clearanceDiameter > 0)
                    set.Clearance = new HoleRule("过孔", clearanceDiameter, string.IsNullOrWhiteSpace(clearanceName) ? $"Φ{clearanceDiameter:G}过孔" : clearanceName);
                if (processDiameter > 0)
                    set.Process = new HoleRule("工艺孔", processDiameter, string.IsNullOrWhiteSpace(processName) ? $"Φ{processDiameter:G}工艺孔" : processName);
            }
        }

        private static void ReadCountersinkHoleSheet(Excel.Worksheet sheet, int startRow, Dictionary<string, HoleRuleSet> result, Dictionary<string, HoleRuleSet> roundRules, List<string> errors)
        {
            Excel.Range usedRange = sheet?.UsedRange;
            int lastRow = usedRange?.Rows.Count ?? 0;
            ReleaseCom(usedRange);

            for (int row = startRow; row <= lastRow; row++)
            {
                string spec = ReadCell(sheet, row, 1);
                if (string.IsNullOrWhiteSpace(spec))
                    continue;

                double headDiameter = ReadDoubleCell(sheet, row, 2);
                double shankDiameter = ReadDoubleCell(sheet, row, 3);
                double countersinkDepth = ReadDoubleCell(sheet, row, 4);
                string name = ReadCell(sheet, row, 5);

                if (headDiameter <= 0 || shankDiameter <= 0 || countersinkDepth <= 0)
                {
                    errors.Add($"沉头孔 sheet 第 {row} 行规格 {spec} 的 D1、D2、t 不能空缺或小于等于 0。");
                    continue;
                }

                HoleRuleSet set = GetOrCreateRuleSet(result, spec);
                string normalizedSpec = NormalizeSpec(spec);
                if (roundRules != null && roundRules.TryGetValue(normalizedSpec, out HoleRuleSet roundSet))
                {
                    set.Tap = roundSet.Tap;
                    set.Process = roundSet.Process;
                }

                string holeName = string.IsNullOrWhiteSpace(name) ? $"{spec}沉头孔" : name;
                set.Clearance = HoleRule.CreateCountersinkClearance(shankDiameter, headDiameter, countersinkDepth, holeName);
            }
        }

        private static HoleRuleSet GetOrCreateRuleSet(Dictionary<string, HoleRuleSet> result, string spec)
        {
            string normalizedSpec = NormalizeSpec(spec);
            if (!result.TryGetValue(normalizedSpec, out HoleRuleSet set))
            {
                set = new HoleRuleSet(normalizedSpec);
                result[normalizedSpec] = set;
            }

            return set;
        }

        private static Excel.Worksheet GetWorksheet(Excel.Workbook workbook, string sheetName, List<string> errors)
        {
            try
            {
                return workbook.Worksheets[sheetName] as Excel.Worksheet;
            }
            catch
            {
                    errors?.Add($"孔数据库缺少“{sheetName}”sheet。");
                return null;
            }
        }

        private static Excel.Worksheet GetWorksheetOrFirst(Excel.Workbook workbook, string sheetName, List<string> errors)
        {
            Excel.Worksheet sheet = GetWorksheet(workbook, sheetName, null);
            if (sheet != null)
                return sheet;

            try
            {
                errors?.Add($"孔数据库缺少“{sheetName}”sheet，已回退读取第一个 sheet。建议把圆孔规则 sheet 命名为“{sheetName}”。");
                return workbook.Worksheets[1] as Excel.Worksheet;
            }
            catch
            {
                errors?.Add($"孔数据库缺少“{sheetName}”sheet，也无法读取第一个 sheet。");
                return null;
            }
        }

        private static string ReadCell(Excel.Worksheet sheet, int row, int column)
        {
            Excel.Range cell = null;
            try
            {
                cell = sheet.Cells[row, column] as Excel.Range;
                object value = cell?.Value2;
                return value?.ToString()?.Trim() ?? string.Empty;
            }
            finally
            {
                ReleaseCom(cell);
            }
        }

        private static double ReadDoubleCell(Excel.Worksheet sheet, int row, int column)
        {
            string text = ReadCell(sheet, row, column);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return value;
            return 0.0;
        }

        internal List<string> ReadHoleSpecs(string path, int startRow, HoleMode mode, List<string> errors)
        {
            List<string> specs = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string resolvedPath = ResolveDatabasePath(path, GetDocumentDirectory());
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                errors.Add(FormatDatabaseNotFoundMessage(path, resolvedPath, GetDocumentDirectory()));
                return specs;
            }

            Excel.Application app = null;
            Excel.Workbook workbook = null;
            Excel.Worksheet sheet = null;
            bool appCreated = false;

            try
            {
                try
                {
                    app = (Excel.Application)Marshal.GetActiveObject("Excel.Application");
                }
                catch
                {
                    app = new Excel.Application();
                    appCreated = true;
                }

                workbook = app.Workbooks.Open(resolvedPath, ReadOnly: true);
                if (mode == HoleMode.ByScrewSpec)
                {
                    sheet = GetWorksheetOrFirst(workbook, "圆孔", errors);
                    if (sheet != null)
                        ReadSpecNames(sheet, startRow, "PH-", seen, specs);

                    ReleaseCom(sheet);
                    sheet = GetWorksheet(workbook, "沉头孔", errors);
                    if (sheet != null)
                        ReadSpecNames(sheet, startRow, "FH-", seen, specs);
                }
                else
                {
                    sheet = mode == HoleMode.Countersink
                        ? GetWorksheet(workbook, "沉头孔", errors)
                        : GetWorksheetOrFirst(workbook, "圆孔", errors);
                    if (sheet == null)
                        return specs;

                    ReadSpecNames(sheet, startRow, string.Empty, seen, specs);
                }
            }
            catch (Exception ex)
            {
                errors.Add("读取螺丝规格失败：" + ex.Message);
            }
            finally
            {
                if (workbook != null)
                    workbook.Close(false);
                ReleaseCom(sheet);
                ReleaseCom(workbook);
                if (app != null && appCreated)
                    app.Quit();
                ReleaseCom(app);
            }

            return specs;
        }

        private static void ReadSpecNames(Excel.Worksheet sheet, int startRow, string prefix, HashSet<string> seen, List<string> specs)
        {
            Excel.Range usedRange = sheet?.UsedRange;
            int lastRow = usedRange?.Rows.Count ?? 0;
            ReleaseCom(usedRange);

            for (int row = startRow; row <= lastRow; row++)
            {
                string spec = ReadCell(sheet, row, 1);
                if (string.IsNullOrWhiteSpace(spec))
                    continue;

                string displaySpec = prefix + spec;
                if (!seen.Add(displaySpec))
                    continue;

                specs.Add(displaySpec);
            }
        }

        private static void ReleaseCom(object obj)
        {
            if (obj == null)
                return;

            try
            {
                if (Marshal.IsComObject(obj))
                    Marshal.ReleaseComObject(obj);
            }
            catch
            {
            }
        }

        protected override Bitmap Icon => GeneratedIcon.Get("gen_MyHoles");

        public override void CreateAttributes()
        {
            Attributes = new CButton_ScrewHoleByVector(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "按螺钉线规格", (sender, args) => SetHoleMode(HoleMode.ByScrewSpec), true, CurrentHoleMode == HoleMode.ByScrewSpec);
            Menu_AppendItem(menu, "圆孔", (sender, args) => SetHoleMode(HoleMode.Round), true, CurrentHoleMode == HoleMode.Round);
            Menu_AppendItem(menu, "沉头孔", (sender, args) => SetHoleMode(HoleMode.Countersink), true, CurrentHoleMode == HoleMode.Countersink);
        }

        private void SetHoleMode(HoleMode mode)
        {
            if (CurrentHoleMode == mode)
                return;

            CurrentHoleMode = mode;
            _hasLastOutputs = false;
            ExpireSolution(true);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetString("DialogSpec", DialogSpec ?? string.Empty);
            writer.SetString("HoleMode", CurrentHoleMode.ToString());
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            DialogSpec = reader.ItemExists("DialogSpec") ? reader.GetString("DialogSpec") : string.Empty;
            if (reader.ItemExists("HoleMode") && Enum.TryParse(reader.GetString("HoleMode"), out HoleMode holeMode))
                CurrentHoleMode = holeMode;
            UpdateDisplaySpec(DialogSpec);
            return base.Read(reader);
        }

        public override Guid ComponentGuid => new Guid("C87B9D2D-06D9-4E21-B6B1-3DAECDE0183B");
    }

    public class ScrewHoleByLineInfo : ScrewHoleByVector
    {
        public ScrewHoleByLineInfo()
            : base("ScrewHoleByLineInfo", "ScrewHoleByLine",
                "按螺钉线输入或 UserString 中的规格、孔类型批量生成底孔、过孔、工艺孔")
        {
        }

        protected override bool UseLineInfo => true;
        protected override bool ShowSpecButton => false;

        public override Guid ComponentGuid => new Guid("9B6499D7-D2F6-4D7D-AF14-165615C6BFB4");
    }

    internal class CButton_ScrewHoleByVector : GH_ComponentAttributes
    {
        private const float ButtonHeight = 20.0f;

        public CButton_ScrewHoleByVector(ScrewHoleByVector component) : base(component) { }

        protected override void Layout()
        {
            base.Layout();
            ScrewHoleByVector owner = (ScrewHoleByVector)Owner;
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + ButtonHeight * (owner.HasSpecButton ? 2.0f : 1.0f));
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            ScrewHoleByVector owner = (ScrewHoleByVector)Owner;
            RectangleF specButtonRect = GetSpecButtonRect();
            RectangleF runButtonRect = GetRunButtonRect();

            if (owner.HasSpecButton)
            {
                GH_Palette specPalette = owner.SpecInputHasValue ? GH_Palette.Grey : GH_Palette.Black;
                using (GH_Capsule capsule = GH_Capsule.CreateCapsule(specButtonRect, specPalette))
                    capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

                string specText = string.IsNullOrWhiteSpace(owner.DisplaySpec) ? "选规格" : owner.DisplaySpec;
                using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    graphics.DrawString(specText, font, Brushes.White, specButtonRect, format);
            }

            GH_Palette runPalette = owner.CurrentButtonColor == ScrewHoleByVector.ButtonColor.Black
                ? GH_Palette.Black
                : GH_Palette.Grey;

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(runButtonRect, runPalette))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("Run", font, Brushes.White, runButtonRect, format);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != MouseButtons.Left)
                return GH_ObjectResponse.Ignore;

            ScrewHoleByVector owner = (ScrewHoleByVector)Owner;
            if (owner.HasSpecButton && GetSpecButtonRect().Contains(e.CanvasLocation))
            {
                if (owner.SpecInputHasValue)
                    return GH_ObjectResponse.Handled;

                List<string> errors = new List<string>();
                List<string> specs = owner.ReadHoleSpecs(owner.GetCurrentDatabasePath(), 3, owner.CurrentHoleMode, errors);
                if (specs.Count == 0)
                {
                    string message = errors.Count > 0 ? string.Join(System.Environment.NewLine, errors) : "请先在孔数据库输入端指定 Excel 文件。";
                    MessageBox.Show(message, "选择螺丝规格", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return GH_ObjectResponse.Handled;
                }

                string currentSpec = string.IsNullOrWhiteSpace(owner.DialogSpec) ? owner.DisplaySpec : owner.DialogSpec;
                using (ScrewSpecSelectionForm form = new ScrewSpecSelectionForm(specs, currentSpec))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        owner.DialogSpec = form.SelectedSpec;
                        owner.ExpireSolution(true);
                    }
                }

                return GH_ObjectResponse.Handled;
            }

            if (GetRunButtonRect().Contains(e.CanvasLocation))
            {
                owner.CurrentButtonColor = ScrewHoleByVector.ButtonColor.Grey;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                Thread.Sleep(50);
                owner.CurrentButtonColor = ScrewHoleByVector.ButtonColor.Black;
                sender.Invalidate();
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }

        private RectangleF GetSpecButtonRect()
        {
            ScrewHoleByVector owner = (ScrewHoleByVector)Owner;
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - ButtonHeight * (owner.HasSpecButton ? 2.0f : 1.0f), Bounds.Width, ButtonHeight);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }

        private RectangleF GetRunButtonRect()
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - ButtonHeight, Bounds.Width, ButtonHeight);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }
    }

    internal class ScrewSpecSelectionForm : Form
    {
        private readonly ListBox _listBox;
        private readonly TextBox _specTextBox;

        public ScrewSpecSelectionForm(List<string> specs, string currentSpec)
        {
            Text = "选择螺丝规格";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(300, 420);

            _listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false
            };
            _listBox.Items.AddRange(specs.Cast<object>().ToArray());
            _listBox.DoubleClick += (sender, args) => AcceptSelection();
            _listBox.SelectedIndexChanged += (sender, args) =>
            {
                if (_listBox.SelectedItem != null)
                    _specTextBox.Text = _listBox.SelectedItem.ToString();
            };

            _specTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 24
            };

            if (!string.IsNullOrWhiteSpace(currentSpec))
            {
                _specTextBox.Text = currentSpec.Trim();
                string currentTableSpec = NormalizeDialogSpec(currentSpec);
                int index = specs.FindIndex(item => string.Equals(NormalizeDialogSpec(item), currentTableSpec, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    _listBox.SelectedIndex = index;
                _specTextBox.Text = currentSpec.Trim();
            }
            if (_listBox.SelectedIndex < 0 && _listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;

            Button okButton = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                Width = 80,
                Height = 28
            };
            okButton.Click += (sender, args) => AcceptSelection();

            Button cancelButton = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Width = 80,
                Height = 28
            };

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 8),
                WrapContents = false
            };
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(okButton);

            Label specLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = "螺丝规格",
                TextAlign = ContentAlignment.MiddleLeft
            };

            Panel inputPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(8, 4, 8, 6)
            };
            inputPanel.Controls.Add(_specTextBox);
            inputPanel.Controls.Add(specLabel);

            Controls.Add(_listBox);
            Controls.Add(inputPanel);
            Controls.Add(buttonPanel);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        public string SelectedSpec { get; private set; } = string.Empty;

        private void AcceptSelection()
        {
            string spec = _specTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(spec))
                return;

            SelectedSpec = spec;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string NormalizeDialogSpec(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
                return string.Empty;

            string value = spec.Trim();
            int xIndex = value.IndexOfAny(new[] { 'x', 'X', '*', '×' });
            if (xIndex > 0)
                value = value.Substring(0, xIndex).Trim();

            return value;
        }
    }

    internal class HoleRuleSet
    {
        public HoleRuleSet(string spec)
        {
            Spec = spec;
        }

        public string Spec { get; }
        public HoleRule Tap { get; set; }
        public HoleRule Clearance { get; set; }
        public HoleRule Process { get; set; }
    }

    internal class HoleRuleTables
    {
        public Dictionary<string, HoleRuleSet> Round { get; } = new Dictionary<string, HoleRuleSet>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HoleRuleSet> Countersink { get; } = new Dictionary<string, HoleRuleSet>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, HoleRuleSet> GetTable(ScrewHoleByVector.HoleMode mode)
        {
            return mode == ScrewHoleByVector.HoleMode.Countersink ? Countersink : Round;
        }
    }

    internal class HoleRule
    {
        public HoleRule(string type, double diameter, string name)
            : this(type, diameter, name, 0.0, 0.0)
        {
        }

        private HoleRule(string type, double diameter, string name, double headDiameter, double countersinkDepth)
        {
            Type = type;
            Diameter = diameter;
            Name = name;
            HeadDiameter = headDiameter;
            CountersinkDepth = countersinkDepth;
        }

        public string Type { get; }
        public double Diameter { get; }
        public string Name { get; }
        public double HeadDiameter { get; }
        public double CountersinkDepth { get; }
        public bool IsCountersink => Type == "过孔" && HeadDiameter > Diameter && CountersinkDepth > 0.0;

        public static HoleRule CreateCountersinkClearance(double shankDiameter, double headDiameter, double countersinkDepth, string name)
        {
            return new HoleRule("过孔", shankDiameter, name, headDiameter, countersinkDepth);
        }
    }

    internal class PartHit
    {
        public PartHit(int partIndex, double parameter, Point3d point)
        {
            PartIndex = partIndex;
            Parameter = parameter;
            Point = point;
        }

        public int PartIndex { get; }
        public double Parameter { get; }
        public Point3d Point { get; }
    }

    internal class ScrewLineInput
    {
        public int InputIndex { get; set; }
        public Line Line { get; set; }
        public RhinoObject Object { get; set; }
    }

    internal class ScrewHoleOutputs
    {
        private List<Brep> _resultBreps = new List<Brep>();
        private List<Brep> _allCutters = new List<Brep>();
        private List<Brep> _tapCutters = new List<Brep>();
        private List<Brep> _clearanceCutters = new List<Brep>();
        private List<Brep> _processCutters = new List<Brep>();
        private List<Point3d> _holePoints = new List<Point3d>();
        private List<Line> _holeAxes = new List<Line>();
        private List<string> _holeNames = new List<string>();
        private List<string> _holeTypes = new List<string>();
        private List<string> _report = new List<string>();
        private List<string> _errors = new List<string>();

        public void Set(
            List<Brep> resultBreps,
            List<Brep> allCutters,
            List<Brep> tapCutters,
            List<Brep> clearanceCutters,
            List<Brep> processCutters,
            List<Point3d> holePoints,
            List<Line> holeAxes,
            List<string> holeNames,
            List<string> holeTypes,
            List<string> report,
            List<string> errors)
        {
            _resultBreps = DuplicateBreps(resultBreps);
            _allCutters = DuplicateBreps(allCutters);
            _tapCutters = DuplicateBreps(tapCutters);
            _clearanceCutters = DuplicateBreps(clearanceCutters);
            _processCutters = DuplicateBreps(processCutters);
            _holePoints = new List<Point3d>(holePoints);
            _holeAxes = new List<Line>(holeAxes);
            _holeNames = new List<string>(holeNames);
            _holeTypes = new List<string>(holeTypes);
            _report = new List<string>(report);
            _errors = new List<string>(errors);
        }

        public void CopyTo(
            List<Brep> resultBreps,
            List<Brep> allCutters,
            List<Brep> tapCutters,
            List<Brep> clearanceCutters,
            List<Brep> processCutters,
            List<Point3d> holePoints,
            List<Line> holeAxes,
            List<string> holeNames,
            List<string> holeTypes,
            List<string> report,
            List<string> errors)
        {
            ReplaceBreps(resultBreps, _resultBreps);
            ReplaceBreps(allCutters, _allCutters);
            ReplaceBreps(tapCutters, _tapCutters);
            ReplaceBreps(clearanceCutters, _clearanceCutters);
            ReplaceBreps(processCutters, _processCutters);
            ReplaceValues(holePoints, _holePoints);
            ReplaceValues(holeAxes, _holeAxes);
            ReplaceValues(holeNames, _holeNames);
            ReplaceValues(holeTypes, _holeTypes);
            ReplaceValues(report, _report);
            ReplaceValues(errors, _errors);
        }

        private static List<Brep> DuplicateBreps(IEnumerable<Brep> breps)
        {
            List<Brep> result = new List<Brep>();
            if (breps == null)
                return result;

            foreach (Brep brep in breps)
            {
                if (brep != null)
                    result.Add(brep.DuplicateBrep());
            }

            return result;
        }

        private static void ReplaceBreps(List<Brep> target, IEnumerable<Brep> source)
        {
            target.Clear();
            target.AddRange(DuplicateBreps(source));
        }

        private static void ReplaceValues<T>(List<T> target, IEnumerable<T> source)
        {
            target.Clear();
            if (source != null)
                target.AddRange(source);
        }
    }
}
