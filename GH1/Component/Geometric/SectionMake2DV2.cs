using CommonFunction;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class SectionMake2DV2 : GH_Component
    {
        private const double DefaultViewDepth = 500000.0;
        private const double DefaultPreviewGap = 0.0;
        private const double ZeroTolerance = 1e-9;
        private const string OtherLineType = "其它";
        private static readonly string[] VisibleLineTypes =
        {
            "Silhouette, Crease",
            "Silhouette, Tangent",
            "Silhouette, Boundary",
            "Tangent",
            "Crease",
            "Boundary",
            OtherLineType
        };
        private static readonly string[] HiddenLineTypes =
        {
            "Boundary",
            "Crease",
            "Tangent",
            OtherLineType
        };
        private bool _lastRun;
        private SectionCache _cache = new SectionCache();
        private string _geometryInputSignature = string.Empty;
        private double _currentPreviewGap = DefaultPreviewGap;
        private readonly List<SectionInfo> _previewSections = new List<SectionInfo>();
        private bool _keepEmptyBranches = true;
        private HashSet<string> _visibleLineTypes = new HashSet<string>(VisibleLineTypes, StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _hiddenLineTypes = new HashSet<string>(HiddenLineTypes, StringComparer.OrdinalIgnoreCase);
        internal bool ButtonRun { get; set; }

        public SectionMake2DV2()
          : base("SectionMake2D V2", "剖切Make2D V2",
              "按剖切线批量生成截面线、截面面和 Make2D 可见线/隐藏线",
              "Parrot", "几何")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("对象", "G", "要剖切并参与 Make2D 投影的几何对象。支持 Brep、Surface、Curve、Rhino 块、GH 块或 Rhino 对象 Guid。输出树的第二层对象序号与此输入列表顺序一致。", GH_ParamAccess.list);
            pManager.AddGenericParameter("参照", "R", "可选参照对象，用于下游定位。参照会和普通对象一起参与剖切与 Make2D 投影，并作为对象序号的最后一项输出；不输入时自动使用输入对象的世界 XY 轴向包围盒作为参照。", GH_ParamAccess.list);
            pManager.AddGenericParameter("剖切位置", "L", "XY 平面上的剖切位置线。可输入 Line 或直线 Curve；线方向作为剖面水平展开方向，剖切面竖向方向为世界 Z 轴。", GH_ParamAccess.list);
            pManager.AddTextParameter("剖切号", "N", "每条剖切位置线对应的剖切号。可输入 A、B、C 或 1、2、3 等文字；为空时自动生成。", GH_ParamAccess.list);
            pManager.AddNumberParameter("剖切深度", "VD", "从剖切面开始沿剖切方向参与投影的深度。小于等于 0 时不限制深度。", GH_ParamAccess.item, DefaultViewDepth);
            pManager.AddNumberParameter("预览间距", "PD", "剖面结果自动放到输入对象包围盒外侧时使用的最小预览净距，也用于相邻剖面预览结果之间的净距。为空或小于等于 0 时自动按模型尺寸取默认值。", GH_ParamAccess.item, DefaultPreviewGap);
            pManager.AddBooleanParameter("执行", "Run", "从 False 切换到 True 时重新计算；保持 False 时输出上一次缓存。右键线型过滤只筛选缓存结果，不重新执行耗时 Make2D。", GH_ParamAccess.item, false);
            pManager[1].Optional = true;
            pManager[3].Optional = true;
            pManager[5].Optional = true;
            if (DateTime.Now.Ticks >= 0)
                return;
            ;
            ;
            ;
            ;
            ;
            ;
            pManager[2].Optional = true;
            pManager[4].Optional = true;
            return;
        }
#if false
            pManager.AddGenericParameter("瀵硅薄", "G", "瑕佸墫鍒囧拰鎶曞奖鐨勫璞★紝鍙緭鍏rep銆丼urface銆丆urve銆丷hino鍧楁垨GH鍧?, GH_ParamAccess.list);
            pManager.AddGenericParameter("鍓栧垏浣嶇疆", "L", "XY骞抽潰涓婄殑鍓栧垏浣嶇疆绾匡紝鍙緭鍏ine鎴栫洿绾緾urve", GH_ParamAccess.list);
            pManager.AddTextParameter("鍓栧垏鍙?, "N", "鍓栧垏鍙凤紝鏁伴噺閫氬父涓庡墫鍒囦綅缃竴鑷达紱閲嶅鍓栧垏闈娇鐢ㄧ涓€娆″嚭鐜扮殑鍓栧垏鍙?, GH_ParamAccess.list);
            pManager.AddPointParameter("鍓栧垏闈㈡彃鍏ョ偣", "P", "绗竴寮犲墫瑙嗗浘鍦╔Y骞抽潰涓婄殑鎻掑叆鐐?, GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("鍓栧垏闈㈡斁澶у€嶆暟", "S", "鍓栬鍥炬斁澶у€嶆暟", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("鍓栧垏闈㈤棿璺?, "D", "澶氫釜鍓栬鍥句箣闂寸殑姘村钩闂磋窛", GH_ParamAccess.item, 1000.0);
            pManager.AddNumberParameter("鏂囧瓧澶у皬", "TH", "鍥惧悕鏂囧瓧澶у皬", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("鏂囧瓧鍋忕Щ", "TO", "鍥惧悕璺濈鍥惧舰涓嬫柟鐨勫亸绉婚噺", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("鍓栧垏娣卞害", "VD", "浠庡墫鍒囬潰寮€濮嬫部鍓栧垏鏂瑰悜鐨勬繁搴︼紝榛樿500000锛涘皬浜庣瓑浜?鏃朵笉闄愬埗", GH_ParamAccess.item, DefaultViewDepth);
            pManager.AddBooleanParameter("鎵ц", "Run", "涓篢rue鏃跺紑濮嬭绠楋紝鍚﹀垯杈撳嚭涓婁竴娆＄紦瀛樼粨鏋?, GH_ParamAccess.item, false);
            pManager[2].Optional = true;
        }

#endif
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("截面面", "SecS", "普通对象先与剖切平面求交，再使用本剖面共用的 Parallel View 投影；同一对象的闭合轮廓一次生成带孔平面 Brep。参考对象不建面，求交为点就输出点，求交为曲线就输出投影曲线。路径为 {剖切号序号; 对象序号}。", GH_ParamAccess.tree);
            pManager.AddCurveParameter("截面线", "Sec", "由实体、参考对象、裁剪平面 C 和本剖面共用的 Parallel View 送入 Make2D 后，从 Vt/Ht 类型为 Section 的曲线中提取。路径为 {剖切号序号; 对象序号}。", GH_ParamAccess.tree);
            pManager.AddCurveParameter("可见线", "Vis", "由筛选后的可投影对象、参考对象、裁剪平面 C 和本剖面共用的 Parallel View 生成，并按右键“可见线类型”过滤。路径为 {剖切号序号; 对象序号}。", GH_ParamAccess.tree);
            pManager.AddTextParameter("可见线类型", "Vt", "与“可见线”一一对应的 Make2D 可见线类型文字；被右键过滤掉的可见线类型不会输出。", GH_ParamAccess.tree);
            pManager.AddCurveParameter("隐藏线", "Hid", "由筛选后的可投影对象、参考对象、裁剪平面 C 和本剖面共用的 Parallel View 生成，并按右键“隐藏线类型”过滤。路径为 {剖切号序号; 对象序号}。", GH_ParamAccess.tree);
            pManager.AddTextParameter("隐藏线类型", "Ht", "与“隐藏线”一一对应的 Make2D 隐藏线类型文字；被右键过滤掉的隐藏线类型不会输出。", GH_ParamAccess.tree);
            pManager.AddTextParameter("剖切号", "N", "实际使用的剖切号列表。列表顺序与输出树第一层剖切号序号一一对应，用于还原 A、B、C 或 1、2、3 等原始剖切号。", GH_ParamAccess.list);
            pManager.AddTextParameter("诊断信息", "Log", "本次计算过程、输入解析、剖面去重、Make2D 端口识别、线型过滤数量、警告和错误信息。", GH_ParamAccess.list);
            if (DateTime.Now.Ticks >= 0)
                return;
            ;
            ;
            ;
            ;
            ;
            ;
            ;
            ;
            ;
            return;
        }
#if false
            pManager.AddGeometryParameter("鎴潰绾跨偣", "Sec", "鍓栧垏闈㈢洿鎺ュ垏鍒扮殑鎴潰鏇茬嚎鎴栫偣", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("鎴潰绱㈠紩", "SecI", "姣忔潯鎴潰鏇茬嚎鎴栫偣瀵瑰簲鐨勮緭鍏ュ璞＄储寮?, GH_ParamAccess.tree);
            pManager.AddCurveParameter("瀹炵嚎", "Vis", "绫讳技Make2D鐨勫彲瑙佺嚎", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("瀹炵嚎绱㈠紩", "VisI", "姣忔潯瀹炵嚎瀵瑰簲鐨勮緭鍏ュ璞＄储寮?, GH_ParamAccess.tree);
            pManager.AddCurveParameter("铏氱嚎", "Hid", "绫讳技Make2D鐨勯殣钘忕嚎", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("铏氱嚎绱㈠紩", "HidI", "姣忔潯铏氱嚎瀵瑰簲鐨勮緭鍏ュ璞＄储寮?, GH_ParamAccess.tree);
            pManager.AddGenericParameter("鍓栧垏绗﹀彿", "Sym", "鍓栧垏绾裤€佹柟鍚戠澶村拰鍓栧垏鍙锋枃瀛?, GH_ParamAccess.tree);
            pManager.AddGenericParameter("鍥惧悕鏂囧瓧", "Title", "鍓栬鍥句笅鏂圭殑鍥惧悕鏂囧瓧瀵硅薄", GH_ParamAccess.tree);
            pManager.AddTextParameter("鍓栧垏鍙?, "N", "瀹為檯浣跨敤鐨勫墫鍒囧彿", GH_ParamAccess.list);
            pManager.AddTextParameter("璇婃柇淇℃伅", "Log", "鏈璁＄畻鐨勮瘖鏂俊鎭?, GH_ParamAccess.list);
            pManager.AddBrepParameter("鎴潰闈?, "SecS", "鎸夎緭鍏ョ墿浣撶储寮曞垎缁勭殑鎴潰骞抽潰闈?, GH_ParamAccess.tree);
        }


#endif
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
            {
                SectionCache illegalCache = new SectionCache();
                illegalCache.Diagnostics.Add("停止：授权检查失败。");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "授权检查失败。");
                SetOutputs(DA, illegalCache);
                return;
            }

            {
                List<IGH_Goo> newInputObjects = new List<IGH_Goo>();
                List<IGH_Goo> newReferenceObjects = new List<IGH_Goo>();
                List<IGH_Goo> newSectionLineGoo = new List<IGH_Goo>();
                List<string> newSectionNames = new List<string>();
                double newViewDepth = DefaultViewDepth;
                double newPreviewGap = DefaultPreviewGap;
                bool newRun = false;

                DA.GetDataList(0, newInputObjects);
                DA.GetDataList(1, newReferenceObjects);
                DA.GetDataList(2, newSectionLineGoo);
                DA.GetDataList(3, newSectionNames);
                DA.GetData(4, ref newViewDepth);
                DA.GetData(5, ref newPreviewGap);
                DA.GetData(6, ref newRun);
                _currentPreviewGap = newPreviewGap;

                List<string> newValidation = new List<string>();
                List<Line> newSectionLines = ParseSectionLines(newSectionLineGoo, newValidation);
                double newTolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
                UpdatePreviewSections(newSectionLines, newSectionNames, newValidation, newTolerance);
                bool newButtonRun = ButtonRun;
                ButtonRun = false;
                bool newShouldRun = newButtonRun || (newRun && !_lastRun);
                _lastRun = newRun;
                string newGeometrySignature = CreateGeometryInputSignature(newInputObjects, newReferenceObjects, newSectionLines, newViewDepth, newTolerance);
                bool geometryChanged = !string.IsNullOrEmpty(_geometryInputSignature) &&
                    !string.Equals(_geometryInputSignature, newGeometrySignature, StringComparison.Ordinal);

                if (geometryChanged && !newShouldRun)
                {
                    _cache = new SectionCache();
                    _cache.Diagnostics.Add("对象、参照、剖切位置、剖切深度或文档容差已变化，旧输出已清空，请重新执行。");
                    _geometryInputSignature = newGeometrySignature;
                    SetOutputs(DA, _cache);
                    return;
                }

                if (!newShouldRun)
                {
                    UpdateCachedNames(_cache, newSectionNames);
                    if (_cache.Diagnostics.Count == 0)
                    {
                        _cache.Diagnostics.Add("尚未执行：请将 Run 从 False 切换到 True，或单击 Run 按钮。");
                        _cache.Diagnostics.Add("当前输出来自缓存；修改剖切号、预览间距、线型过滤或空分支设置不会重新运行 Make2D。");
                    }
                    SetOutputs(DA, _cache);
                    return;
                }

                if (newInputObjects.Count == 0)
                {
                    SectionCache invalidCache = new SectionCache();
                    invalidCache.Diagnostics.Add("停止：对象输入为空。");
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "对象输入为空。");
                    SetOutputs(DA, invalidCache);
                    return;
                }

                if (newSectionLineGoo.Count == 0)
                {
                    SectionCache invalidCache = new SectionCache();
                    invalidCache.Diagnostics.Add("停止：剖切位置输入为空。");
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "剖切位置输入为空。");
                    SetOutputs(DA, invalidCache);
                    return;
                }

                if (newValidation.Count > 0)
                {
                    SectionCache invalidCache = new SectionCache();
                    invalidCache.Diagnostics.Add("停止：剖切位置输入无效。");
                    invalidCache.Diagnostics.AddRange(newValidation);
                    foreach (string message in newValidation)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
                    SetOutputs(DA, invalidCache);
                    return;
                }

                try
                {
                    _cache = Compute(newInputObjects, newReferenceObjects, newSectionLines, newSectionNames, newViewDepth, newPreviewGap, newTolerance);
                    _geometryInputSignature = newGeometrySignature;
                    SetOutputs(DA, _cache);
                }
                catch (Exception ex)
                {
                    SectionCache errorCache = new SectionCache();
                    errorCache.Diagnostics.Add("执行中断: " + ex.Message);
                    errorCache.Diagnostics.Add("异常类型: " + ex.GetType().FullName);
                    AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Error, ex.Message);
                    SetOutputs(DA, errorCache);
                }

                return;
            }

        }

#if false
            List<IGH_Goo> inputObjects = new List<IGH_Goo>();
            List<IGH_Goo> sectionLineGoo = new List<IGH_Goo>();
            List<string> sectionNames = new List<string>();
            Point3d insertPoint = Point3d.Origin;
            double scale = 1.0;
            double spacing = 1000.0;
            double textHeight = 100.0;
            double textOffset = 100.0;
            double viewDepth = DefaultViewDepth;
            bool run = false;

            DA.GetDataList(0, inputObjects);
            DA.GetDataList(1, sectionLineGoo);
            DA.GetDataList(2, sectionNames);
            bool hasInsertPoint = Params.Input[3].SourceCount > 0 && DA.GetData(3, ref insertPoint);
            DA.GetData(4, ref scale);
            DA.GetData(5, ref spacing);
            DA.GetData(6, ref textHeight);
            DA.GetData(7, ref textOffset);
            DA.GetData(8, ref viewDepth);
            DA.GetData(9, ref run);

            List<string> validation = new List<string>();
            List<Line> sectionLines = ParseSectionLines(sectionLineGoo, validation);
            double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
            UpdatePreviewSections(sectionLines, sectionNames, validation, tolerance);
            bool buttonRun = ButtonRun;
            ButtonRun = false;
            bool shouldRun = run || buttonRun;
            _lastRun = run;
            if (!shouldRun)
            {
                if (_cache.Diagnostics.Count == 0)
                {
                    _cache.Diagnostics.Add("Not executed: toggle Run from False to True.");
                    _cache.Diagnostics.Add("鎵ц绔彛褰撳墠锟? " + run);
                    _cache.Diagnostics.Add("褰撳墠瀵硅薄杈撳叆鏁伴噺: " + inputObjects.Count);
                    _cache.Diagnostics.Add("褰撳墠鍓栧垏浣嶇疆杈撳叆鏁伴噺: " + sectionLineGoo.Count);
                    _cache.Diagnostics.AddRange(validation);
                }
                SetOutputs(DA, _cache);
                return;
            }

            if (inputObjects.Count == 0)
            {
                SectionCache invalidCache = new SectionCache();
                invalidCache.Diagnostics.Add("Stop: object input is empty.");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Object input is empty.");
                SetOutputs(DA, invalidCache);
                return;
            }

            if (sectionLineGoo.Count == 0)
            {
                SectionCache invalidCache = new SectionCache();
                invalidCache.Diagnostics.Add("Stop: section line input is empty.");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Section line input is empty.");
                SetOutputs(DA, invalidCache);
                return;
            }

            if (validation.Count > 0)
            {
                SectionCache invalidCache = new SectionCache();
                invalidCache.Diagnostics.Add("Stop: section line input is invalid.");
                invalidCache.Diagnostics.AddRange(validation);
                foreach (string message in validation)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
                SetOutputs(DA, invalidCache);
                return;
            }

            try
            {
                _cache = Compute(inputObjects, sectionLines, sectionNames, insertPoint, hasInsertPoint, scale, spacing, textHeight, textOffset, viewDepth, tolerance);
                SetOutputs(DA, _cache);
            }
            catch (Exception ex)
            {
                SectionCache errorCache = new SectionCache();
                errorCache.Diagnostics.Add("鎵ц涓柇: " + ex.Message);
                errorCache.Diagnostics.Add("寮傚父绫诲瀷: " + ex.GetType().FullName);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                SetOutputs(DA, errorCache);
            }
        }

#endif
        private SectionCache Compute(List<IGH_Goo> inputObjects, List<IGH_Goo> referenceObjects, List<Line> sectionLines, List<string> sectionNames, double viewDepth, double previewGap, double tolerance)
        {
            SectionCache result = new SectionCache();
            result.Diagnostics.Add("执行端口触发，开始重新计算。");
            result.Diagnostics.Add("输入对象数量: " + inputObjects.Count);
            result.ReferenceObjectIndex = referenceObjects.Count > 0 ? inputObjects.Count : -1;
            result.Diagnostics.Add("参考对象数量: " + referenceObjects.Count + (result.ReferenceObjectIndex >= 0 ? "，参考对象序号从 " + result.ReferenceObjectIndex + " 开始。" : "。"));
            result.Diagnostics.Add("输入剖切线数量: " + sectionLines.Count);

            List<string> collectLogs = new List<string>();
            List<SourceGeometry> source = CollectSourceGeometry(inputObjects, collectLogs);
            if (referenceObjects.Count > 0)
                source.AddRange(CollectSourceGeometry(referenceObjects, collectLogs, inputObjects.Count, true));
            result.Diagnostics.Add("成功解析几何数量: " + source.Count);
            result.Diagnostics.AddRange(collectLogs);
            if (source.Count == 0)
            {
                result.Diagnostics.Add("停止：没有可参与剖切和 Make2D 的几何。");
                return result;
            }

            if (referenceObjects.Count > 0)
            {
                result.ReferenceObjectIndex = inputObjects.Count;
                result.OutputObjectCount = inputObjects.Count + referenceObjects.Count;
            }
            else if (source.Count > 0)
            {
                BoundingBox inputBox = GetSourceBoundingBox(source);
                Brep referenceBox = CreateReferenceBoundingBoxGeometry(inputBox, tolerance);
                if (referenceBox != null)
                {
                    result.ReferenceObjectIndex = inputObjects.Count;
                    result.OutputObjectCount = inputObjects.Count + 1;
                    source.Add(new SourceGeometry(referenceBox, inputObjects.Count, true));
                    result.Diagnostics.Add("未输入参考对象，已使用输入对象的 XY 包围盒生成参考对象，序号为 " + result.ReferenceObjectIndex + "。");
                }
                else
                {
                    result.ReferenceObjectIndex = -1;
                    result.OutputObjectCount = inputObjects.Count;
                    result.Diagnostics.Add("未输入参考对象，且输入对象包围盒无效，无法生成自动参考对象。");
                }
            }

            List<SectionInfo> sections = BuildUniqueSections(sectionLines, sectionNames, tolerance);
            result.Diagnostics.Add("有效去重后剖面数量: " + sections.Count);
            if (sections.Count == 0)
            {
                result.Diagnostics.Add("停止：没有有效剖切面。");
                return result;
            }

            BoundingBox sourceBox = GetSourceBoundingBox(source);
            double gap = ResolvePreviewGap(previewGap, sourceBox, tolerance);
            result.Diagnostics.Add("预览间距: " + FormatDouble(gap));

            for (int i = 0; i < sections.Count; i++)
            {
                SectionInfo section = sections[i];
                ProjectionContext projectionContext = CreateProjectionContext(source, section);
                SectionInfo projectionSection = projectionContext.Section;

                GH_Path sectionPath = new GH_Path(i);
                InitializeObjectBranches(result, sectionPath, result.OutputObjectCount, _keepEmptyBranches);

                List<SourceGeometry> visibleSource = projectionContext.Source
                    .Where(item => item.IsReference || IsInsideViewDepth(item.Geometry, projectionSection, viewDepth))
                    .ToList();
                result.Diagnostics.Add("剖面 " + section.Name + ": 范围内对象 " + visibleSource.Count + "/" + source.Count);

                List<SourceGeometry> sectionSource = new List<SourceGeometry>();
                List<SourceGeometry> projectionSource = new List<SourceGeometry>();
                List<SectionPoint> sectionPoints = new List<SectionPoint>();
                SplitSectionAndProjectionSource(visibleSource, projectionSection, tolerance, result, sectionSource, sectionPoints, projectionSource);
                List<SourceGeometry> make2DSource = visibleSource;

                using (NativeMake2DViewContext viewContext = CreateNativeMake2DViewContext(make2DSource, projectionSection, projectionContext.LocalizationCenter, result, "剖面 " + section.Name + "：Make2D 视图"))
                {
                    if (viewContext == null)
                    {
                        result.Layouts.Add(new SectionLayoutInfo(sourceBox, BoundingBox.Empty, BoundingBox.Empty, BoundingBox.Empty, section.ViewDirection));
                        result.Names.Add(section.Name);
                        continue;
                    }

                    BoundingBox directReferenceBox;
                    BoundingBox directSectionBox = AddDirectSectionSurfaces(
                        result, i, sectionSource, sectionPoints, viewContext.ViewRectangle.Plane, tolerance, out directReferenceBox);

                    List<Curve> visible = new List<Curve>();
                    List<int> visibleIndex = new List<int>();
                    List<string> visibleTypes = new List<string>();
                    List<Curve> hidden = new List<Curve>();
                    List<int> hiddenIndex = new List<int>();
                    List<string> hiddenTypes = new List<string>();
                    if (RunNativeMake2D(make2DSource, viewContext, projectionSection, tolerance, result, "剖面 " + section.Name + "：对象 Make2D",
                        out visible, out visibleIndex, out visibleTypes, out hidden, out hiddenIndex, out hiddenTypes))
                    {
                        AddMake2DRecords(result, sectionPath, i, Transform.Identity,
                            visible, visibleIndex, visibleTypes, hidden, hiddenIndex, hiddenTypes, tolerance);
                    }

                    BoundingBox drawingBox = GetCurveBoundingBox(visible, hidden);
                    UnionBoundingBox(ref drawingBox, directSectionBox);
                    BoundingBox referenceSourceBox = GetSourceBoundingBox(source.Where(item => item.IsReference).ToList());
                    BoundingBox referenceDrawingBox = GetIndexedCurveBoundingBox(result.ReferenceObjectIndex,
                        visible, visibleIndex, hidden, hiddenIndex,
                        new List<Curve>(), new List<int>(), new List<Curve>(), new List<int>());
                    UnionBoundingBox(ref referenceDrawingBox, directReferenceBox);
                    result.Layouts.Add(new SectionLayoutInfo(sourceBox, drawingBox, referenceSourceBox, referenceDrawingBox, section.ViewDirection));
                }

                result.Names.Add(section.Name);
                result.Diagnostics.Add("剖面 " + section.Name + ": 截面面/参考截面几何 " + result.SectionSurfaces.DataCount + "，截面线 " + result.SectionGeometry.DataCount + "，可见线缓存 " + result.RawVisibleCurves.Count + "，隐藏线缓存 " + result.RawHiddenCurves.Count);
            }

            AppendLineTypeDiagnostics(result);
            return result;
        }

        private static double ResolvePreviewGap(double previewGap, BoundingBox sourceBox, double tolerance)
        {
            if (previewGap > ZeroTolerance)
                return previewGap;

            double modelSize = sourceBox.IsValid ? sourceBox.Diagonal.Length : 0.0;
            return Math.Max(Math.Max(modelSize * 0.15, 100.0), tolerance * 100.0);
        }

        private static void BuildSectionSurfaces(SectionBuildData data, List<SourceGeometry> source, Plane plane, double tolerance)
        {
            Dictionary<int, List<Curve>> closedCurvesByObject = new Dictionary<int, List<Curve>>();
            foreach (SourceGeometry item in source)
            {
                List<Curve> curves = new List<Curve>();
                List<Point3d> points = new List<Point3d>();
                if (!TryExtractSectionGeometry(item.Geometry, plane, tolerance, curves, points))
                    continue;

                foreach (Curve curve in curves)
                {
                    if (curve == null)
                        continue;

                    Curve duplicate = curve.DuplicateCurve();
                    if (duplicate == null)
                        continue;

                    if (duplicate.IsClosed)
                    {
                        if (!closedCurvesByObject.TryGetValue(item.Index, out List<Curve> list))
                        {
                            list = new List<Curve>();
                            closedCurvesByObject.Add(item.Index, list);
                        }
                        list.Add(duplicate.DuplicateCurve());
                    }
                }
            }

            foreach (KeyValuePair<int, List<Curve>> pair in closedCurvesByObject)
            {
                Brep[] breps = Brep.CreatePlanarBreps(pair.Value, tolerance);
                if (breps == null)
                    continue;

                foreach (Brep brep in breps)
                {
                    if (brep != null)
                        data.SectionSurfaces.Add(new BrepRecord(brep.DuplicateBrep(), pair.Key));
                }
            }
        }

        private static void AddMake2DRecords(SectionCache cache, GH_Path sectionPath, int sectionIndex, Transform toLayout, List<Curve> visible, List<int> visibleIndex, List<string> visibleTypes, List<Curve> hidden, List<int> hiddenIndex, List<string> hiddenTypes, double tolerance)
        {
            Dictionary<int, int> sectionCounters = new Dictionary<int, int>();
            Dictionary<int, List<Curve>> sectionCurvesByObject = new Dictionary<int, List<Curve>>();
            AddMake2DPortDiagnostics(cache, sectionIndex, visible.Count, visibleIndex.Count, visibleTypes, hidden.Count, hiddenIndex.Count, hiddenTypes);
            int visibleSectionCount = 0;
            int hiddenSectionCount = 0;

            for (int i = 0; i < visible.Count; i++)
            {
                Curve curve = visible[i]?.DuplicateCurve();
                if (curve == null)
                    continue;

                curve.Transform(toLayout);
                int objectIndex = i < visibleIndex.Count ? visibleIndex[i] : -1;
                string type = NormalizeLineType(i < visibleTypes.Count ? visibleTypes[i] : string.Empty);
                if (IsSectionLineType(type))
                {
                    GH_Path path = ElementPath(sectionIndex, objectIndex, NextCounter(sectionCounters, objectIndex));
                    AppendSectionCurve(cache, sectionCurvesByObject, path, objectIndex, curve);
                    visibleSectionCount++;
                }
                else
                {
                    cache.RawVisibleCurves.Add(new TypedCurveRecord(curve, sectionIndex, objectIndex, type));
                }
            }

            for (int i = 0; i < hidden.Count; i++)
            {
                Curve curve = hidden[i]?.DuplicateCurve();
                if (curve == null)
                    continue;

                curve.Transform(toLayout);
                int objectIndex = i < hiddenIndex.Count ? hiddenIndex[i] : -1;
                string type = NormalizeLineType(i < hiddenTypes.Count ? hiddenTypes[i] : string.Empty);
                if (IsSectionLineType(type))
                {
                    GH_Path path = ElementPath(sectionIndex, objectIndex, NextCounter(sectionCounters, objectIndex));
                    AppendSectionCurve(cache, sectionCurvesByObject, path, objectIndex, curve);
                    hiddenSectionCount++;
                }
                else
                {
                    cache.RawHiddenCurves.Add(new TypedCurveRecord(curve, sectionIndex, objectIndex, type));
                }
            }

            cache.Diagnostics.Add("剖面序号 " + sectionIndex + "：从 Vt 提取 Section " + visibleSectionCount + " 条，从 Ht 提取 Section " + hiddenSectionCount + " 条。");
        }

        private static void AddIntersectionSectionSurfaces(SectionCache cache, int sectionIndex, Transform sectionToLayout, List<SourceGeometry> sectionSource, List<SectionPoint> sectionPoints, double tolerance)
        {
            Dictionary<int, List<Curve>> sectionCurvesByObject = new Dictionary<int, List<Curve>>();

            foreach (SourceGeometry item in sectionSource)
            {
                Curve curve = item.Geometry as Curve;
                if (curve == null)
                    continue;

                Curve duplicate = curve.DuplicateCurve();
                if (duplicate == null)
                    continue;

                duplicate.Transform(sectionToLayout);

                if (!sectionCurvesByObject.TryGetValue(item.Index, out List<Curve> curves))
                {
                    curves = new List<Curve>();
                    sectionCurvesByObject.Add(item.Index, curves);
                }
                curves.Add(duplicate);
            }

            if (sectionPoints != null && sectionPoints.Count > 0)
                cache.Diagnostics.Add("剖面序号 " + sectionIndex + "：平面求交点 " + sectionPoints.Count + " 个；普通对象的 SecS 不输出点。");

            cache.Diagnostics.Add("剖面序号 " + sectionIndex + "：SecS 平面求交曲线 " + sectionSource.Count + " 条。");
            AddSectionSurfacesFromMake2DSections(cache, sectionIndex, sectionCurvesByObject, tolerance);
        }

        private static string CreateGeometryInputSignature(List<IGH_Goo> inputObjects, List<IGH_Goo> referenceObjects, List<Line> sectionLines, double viewDepth, double tolerance)
        {
            List<string> logs = new List<string>();
            List<SourceGeometry> source = CollectSourceGeometry(inputObjects, logs);
            source.AddRange(CollectSourceGeometry(referenceObjects, logs, inputObjects.Count, true));
            IEnumerable<string> geometryParts = source.Select(item =>
            {
                BoundingBox box = item.Geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;
                return item.Index + ":" + item.IsReference + ":" + item.Geometry?.GetType().FullName + ":" +
                    GetGeometryContentSignature(item.Geometry) + ":" +
                    FormatPoint(box.Min) + ":" + FormatPoint(box.Max);
            });
            IEnumerable<string> lineParts = sectionLines.Select(line => FormatPoint(line.From) + ">" + FormatPoint(line.To));
            return string.Join("|", geometryParts.Concat(lineParts)) + "|VD=" + FormatDouble(viewDepth) + "|T=" + FormatDouble(tolerance);
        }

        private static string GetGeometryContentSignature(GeometryBase geometry)
        {
            if (geometry == null)
                return "null";

            try
            {
                MethodInfo method = geometry.GetType().GetMethod(
                    "DataCRC",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(uint) },
                    null);
                if (method != null)
                {
                    object value = method.Invoke(geometry, new object[] { 0u });
                    if (value != null)
                        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }

            return geometry.ToString();
        }

        private static void UpdateCachedNames(SectionCache cache, List<string> names)
        {
            if (cache == null || cache.Names.Count == 0)
                return;
            for (int i = 0; i < cache.Names.Count; i++)
                cache.Names[i] = i < names.Count && !string.IsNullOrWhiteSpace(names[i]) ? names[i] : (i + 1).ToString();
        }

        private static BoundingBox AddDirectSectionSurfaces(SectionCache cache, int sectionIndex,
            List<SourceGeometry> sectionSource, List<SectionPoint> sectionPoints, Plane viewPlane, double tolerance,
            out BoundingBox referenceBox)
        {
            BoundingBox outputBox = BoundingBox.Empty;
            referenceBox = BoundingBox.Empty;
            Dictionary<int, List<Curve>> curvesByObject = new Dictionary<int, List<Curve>>();
            Transform toTop = Transform.PlaneToPlane(viewPlane, Plane.WorldXY);
            foreach (SourceGeometry item in sectionSource)
            {
                if (!(item.Geometry is Curve curve))
                    continue;
                Curve projected = curve.DuplicateCurve();
                if (projected == null)
                    continue;
                projected.Transform(toTop);
                projected = Curve.ProjectToPlane(projected, Plane.WorldXY);
                if (projected == null)
                    continue;
                if (!curvesByObject.TryGetValue(item.Index, out List<Curve> objectCurves))
                {
                    objectCurves = new List<Curve>();
                    curvesByObject.Add(item.Index, objectCurves);
                }
                objectCurves.Add(projected);
            }
            Dictionary<int, int> counters = new Dictionary<int, int>();

            foreach (KeyValuePair<int, List<Curve>> pair in curvesByObject)
            {
                bool isReference = cache.ReferenceObjectIndex >= 0 && pair.Key >= cache.ReferenceObjectIndex;
                if (isReference)
                {
                    foreach (Curve curve in pair.Value)
                    {
                        Curve output = curve?.DuplicateCurve();
                        if (output == null)
                            continue;
                        cache.SectionSurfaces.Append(new GH_Curve(output), ElementPath(sectionIndex, pair.Key, NextCounter(counters, pair.Key)));
                        UnionBoundingBox(ref outputBox, output.GetBoundingBox(true));
                        UnionBoundingBox(ref referenceBox, output.GetBoundingBox(true));
                    }
                    continue;
                }

                Curve[] joined = Curve.JoinCurves(pair.Value.Where(curve => curve != null), tolerance);
                List<Curve> closed = (joined ?? new Curve[0])
                    .Where(curve => curve != null && curve.IsClosed)
                    .Select(curve => curve.DuplicateCurve())
                    .Where(curve => curve != null)
                    .ToList();
                if (closed.Count == 0)
                    continue;

                Brep[] breps = CreatePlanarBrepsWithHoles(closed, tolerance);
                if (breps == null)
                    continue;

                foreach (Brep brep in breps)
                {
                    Brep output = brep?.DuplicateBrep();
                    if (output == null)
                        continue;
                    cache.SectionSurfaces.Append(new GH_Brep(output), ElementPath(sectionIndex, pair.Key, NextCounter(counters, pair.Key)));
                    UnionBoundingBox(ref outputBox, output.GetBoundingBox(true));
                }
            }

            if (sectionPoints == null || sectionPoints.Count == 0 || cache.ReferenceObjectIndex < 0)
                return outputBox;

            foreach (SectionPoint pointRecord in sectionPoints.Where(item => item.Index >= cache.ReferenceObjectIndex))
            {
                Point3d point = pointRecord.Point;
                point.Transform(toTop);
                point.Z = 0.0;
                cache.SectionSurfaces.Append(new GH_Point(point), ElementPath(sectionIndex, pointRecord.Index, NextCounter(counters, pointRecord.Index)));
                BoundingBox pointBox = new BoundingBox(point, point);
                UnionBoundingBox(ref outputBox, pointBox);
                UnionBoundingBox(ref referenceBox, pointBox);
            }

            return outputBox;
        }

        private static void UnionBoundingBox(ref BoundingBox target, BoundingBox addition)
        {
            if (!addition.IsValid)
                return;
            if (target.IsValid)
                target.Union(addition);
            else
                target = addition;
        }

        private static Brep[] CreatePlanarBrepsWithHoles(List<Curve> closedCurves, double tolerance)
        {
            List<SectionRegion> regions = closedCurves
                .Select(curve => new SectionRegion(curve, CreateSinglePlanarBrep(curve, tolerance), 0.0))
                .Where(region => region.Brep != null)
                .ToList();

            for (int i = 0; i < regions.Count; i++)
            {
                int parent = -1;
                double parentArea = double.MaxValue;
                double area = GetBrepArea(regions[i].Brep);
                for (int j = 0; j < regions.Count; j++)
                {
                    if (i == j || !CurveContainsCurve(regions[j].Curve, regions[i].Curve, tolerance))
                        continue;
                    double candidateArea = GetBrepArea(regions[j].Brep);
                    if (candidateArea > area && candidateArea < parentArea)
                    {
                        parent = j;
                        parentArea = candidateArea;
                    }
                }
                regions[i].ParentIndex = parent;
            }

            for (int i = 0; i < regions.Count; i++)
                regions[i].Depth = GetRegionDepth(regions, i);

            List<Brep> result = new List<Brep>();
            for (int i = 0; i < regions.Count; i++)
            {
                if ((regions[i].Depth & 1) != 0)
                    continue;

                List<Curve> boundaries = new List<Curve> { regions[i].Curve };
                boundaries.AddRange(regions
                    .Where((region, index) => region.ParentIndex == i && region.Depth == regions[i].Depth + 1)
                    .Select(region => region.Curve));
                Brep[] breps = Brep.CreatePlanarBreps(boundaries, tolerance);
                if (breps != null)
                    result.AddRange(breps.Where(brep => brep != null));
            }

            return result.ToArray();
        }

        private static void AddProjectedSectionCurves(Dictionary<int, List<Curve>> curvesByObject, List<Curve> curves, List<int> indices)
        {
            for (int i = 0; i < curves.Count; i++)
            {
                Curve curve = curves[i]?.DuplicateCurve();
                if (curve == null)
                    continue;

                int objectIndex = i < indices.Count ? indices[i] : -1;
                if (!curvesByObject.TryGetValue(objectIndex, out List<Curve> objectCurves))
                {
                    objectCurves = new List<Curve>();
                    curvesByObject.Add(objectIndex, objectCurves);
                }
                objectCurves.Add(curve);
            }
        }

        private static BoundingBox GetCurveBoundingBox(params List<Curve>[] curveLists)
        {
            BoundingBox box = BoundingBox.Empty;
            foreach (List<Curve> curves in curveLists)
            {
                if (curves == null)
                    continue;
                foreach (Curve curve in curves)
                {
                    BoundingBox curveBox = curve?.GetBoundingBox(true) ?? BoundingBox.Empty;
                    if (curveBox.IsValid)
                        box.Union(curveBox);
                }
            }
            return box;
        }

        private static BoundingBox GetIndexedCurveBoundingBox(int referenceIndex,
            List<Curve> curvesA, List<int> indicesA, List<Curve> curvesB, List<int> indicesB,
            List<Curve> curvesC, List<int> indicesC, List<Curve> curvesD, List<int> indicesD)
        {
            BoundingBox box = BoundingBox.Empty;
            AppendIndexedCurveBoundingBox(ref box, referenceIndex, curvesA, indicesA);
            AppendIndexedCurveBoundingBox(ref box, referenceIndex, curvesB, indicesB);
            AppendIndexedCurveBoundingBox(ref box, referenceIndex, curvesC, indicesC);
            AppendIndexedCurveBoundingBox(ref box, referenceIndex, curvesD, indicesD);
            return box;
        }

        private static void AppendIndexedCurveBoundingBox(ref BoundingBox box, int referenceIndex, List<Curve> curves, List<int> indices)
        {
            if (referenceIndex < 0 || curves == null || indices == null)
                return;
            for (int i = 0; i < curves.Count && i < indices.Count; i++)
            {
                if (indices[i] < referenceIndex)
                    continue;
                BoundingBox curveBox = curves[i]?.GetBoundingBox(true) ?? BoundingBox.Empty;
                if (curveBox.IsValid)
                    box.Union(curveBox);
            }
        }

        private static Vector3d ComputePreviewMove(BoundingBox sourceBox, BoundingBox drawingBox,
            BoundingBox referenceSourceBox, BoundingBox referenceDrawingBox,
            Vector3d viewDirection, double gap, int sectionIndex)
        {
            if (!drawingBox.IsValid)
                return Vector3d.Zero;

            Vector3d direction = viewDirection;
            direction.Z = 0.0;
            if (!direction.Unitize())
                direction = Vector3d.XAxis;

            double sourceMax = sourceBox.IsValid ? sourceBox.GetCorners().Max(point => (point - Point3d.Origin) * direction) : 0.0;
            double drawingMin = drawingBox.GetCorners().Min(point => (point - Point3d.Origin) * direction);
            double along = sourceMax + gap - drawingMin;
            Vector3d move = direction * along;
            move.Z = 0.0;
            return move;
        }

        private static void AppendSectionCurve(SectionCache cache, Dictionary<int, List<Curve>> sectionCurvesByObject, GH_Path path, int objectIndex, Curve curve)
        {
            Curve sectionCurve = curve?.DuplicateCurve();
            if (curve != null)
                cache.SectionGeometry.Append(new GH_Curve(curve), path);
            if (sectionCurve == null)
                return;

            if (!sectionCurvesByObject.TryGetValue(objectIndex, out List<Curve> sectionCurves))
            {
                sectionCurves = new List<Curve>();
                sectionCurvesByObject.Add(objectIndex, sectionCurves);
            }
            sectionCurves.Add(sectionCurve);
        }

        private static void AddMake2DPortDiagnostics(SectionCache cache, int sectionIndex, int visibleCount, int visibleIndexCount, List<string> visibleTypes, int hiddenCount, int hiddenIndexCount, List<string> hiddenTypes)
        {
            cache.Diagnostics.Add("剖面序号 " + sectionIndex + "：Make2D V/Vi/Vt 数量 = " + visibleCount + "/" + visibleIndexCount + "/" + visibleTypes.Count + "；H/Hi/Ht 数量 = " + hiddenCount + "/" + hiddenIndexCount + "/" + hiddenTypes.Count);
            string visibleSummary = string.Join(", ", visibleTypes.Select(NormalizeLineType).Where(type => !string.IsNullOrWhiteSpace(type)).Distinct(StringComparer.OrdinalIgnoreCase));
            string hiddenSummary = string.Join(", ", hiddenTypes.Select(NormalizeLineType).Where(type => !string.IsNullOrWhiteSpace(type)).Distinct(StringComparer.OrdinalIgnoreCase));
            cache.Diagnostics.Add("剖面序号 " + sectionIndex + "：Vt 类型 = " + (string.IsNullOrWhiteSpace(visibleSummary) ? "（空）" : visibleSummary));
            cache.Diagnostics.Add("剖面序号 " + sectionIndex + "：Ht 类型 = " + (string.IsNullOrWhiteSpace(hiddenSummary) ? "（空）" : hiddenSummary));
        }

        private static void AddSectionSurfacesFromMake2DSections(SectionCache cache, int sectionIndex, Dictionary<int, List<Curve>> sectionCurvesByObject, double tolerance)
        {
            Dictionary<int, int> counters = new Dictionary<int, int>();
            foreach (KeyValuePair<int, List<Curve>> pair in sectionCurvesByObject)
            {
                bool isReference = cache.ReferenceObjectIndex >= 0 && pair.Key >= cache.ReferenceObjectIndex;
                if (isReference)
                {
                    foreach (Curve curve in pair.Value.Where(item => item != null))
                    {
                        GH_Path path = ElementPath(sectionIndex, pair.Key, NextCounter(counters, pair.Key));
                        cache.SectionSurfaces.Append(new GH_Curve(curve), path);
                    }
                    continue;
                }

                Curve[] joinedCurves = Curve.JoinCurves(pair.Value.Where(item => item != null), tolerance);
                List<Curve> closedCurves = (joinedCurves ?? new Curve[0])
                    .Where(item => item != null && item.IsClosed)
                    .Select(item => item.DuplicateCurve())
                    .Where(item => item != null)
                    .ToList();
                if (closedCurves.Count == 0)
                    continue;

                Brep[] breps = CreateSectionRegionBrepsByAreaDifference(closedCurves, tolerance);

                foreach (Brep brep in breps)
                {
                    if (brep == null)
                        continue;

                    GH_Path path = ElementPath(sectionIndex, pair.Key, NextCounter(counters, pair.Key));
                    cache.SectionSurfaces.Append(new GH_Brep(brep), path);
                }
            }
        }

        private static Brep[] CreateSectionRegionBrepsByAreaDifference(List<Curve> closedCurves, double tolerance)
        {
            List<Brep> pending = new List<Brep>();
            foreach (Curve curve in closedCurves)
            {
                Brep brep = CreateSinglePlanarBrep(curve, tolerance);
                if (brep == null)
                    continue;

                double area = GetBrepArea(brep);
                if (area <= ZeroTolerance)
                    continue;

                pending.Add(brep);
            }

            List<Brep> result = new List<Brep>();
            while (pending.Count > 0)
            {
                int mainIndex = IndexOfLargestBrep(pending);
                Brep main = pending[mainIndex]?.DuplicateBrep();
                pending.RemoveAt(mainIndex);
                if (main == null)
                    continue;

                double mainArea = GetBrepArea(main);
                List<Brep> independent = new List<Brep>();

                foreach (Brep candidate in pending)
                {
                    if (candidate == null)
                        continue;

                    Brep[] difference = TryBooleanDifference(main, new List<Brep> { candidate }, tolerance);
                    double differenceArea = GetTotalBrepArea(difference);
                    double areaTolerance = Math.Max(tolerance * tolerance, Math.Abs(mainArea) * 1e-6);
                    if (difference != null && difference.Length > 0 && differenceArea < mainArea - areaTolerance)
                    {
                        main = MergeBooleanPieces(difference);
                        mainArea = GetBrepArea(main);
                    }
                    else
                    {
                        independent.Add(candidate);
                    }
                }

                result.Add(main);
                pending = independent;
            }

            return result.ToArray();
        }

        private static int IndexOfLargestBrep(List<Brep> breps)
        {
            int index = 0;
            double maxArea = double.MinValue;
            for (int i = 0; i < breps.Count; i++)
            {
                double area = GetBrepArea(breps[i]);
                if (area > maxArea)
                {
                    index = i;
                    maxArea = area;
                }
            }

            return index;
        }

        private static double GetTotalBrepArea(IEnumerable<Brep> breps)
        {
            if (breps == null)
                return 0.0;

            return breps.Sum(GetBrepArea);
        }

        private static Brep MergeBooleanPieces(Brep[] pieces)
        {
            if (pieces == null || pieces.Length == 0)
                return null;
            if (pieces.Length == 1)
                return pieces[0];

            Brep result = new Brep();
            foreach (Brep piece in pieces.Where(item => item != null))
                result.Append(piece);
            return result;
        }

        private static Brep CreateSinglePlanarBrep(Curve curve, double tolerance)
        {
            Brep[] breps = Brep.CreatePlanarBreps(new[] { curve }, tolerance);
            if (breps == null || breps.Length == 0)
                return null;

            return breps
                .Where(item => item != null)
                .OrderByDescending(GetBrepArea)
                .FirstOrDefault();
        }

        private static double GetBrepArea(Brep brep)
        {
            AreaMassProperties area = brep != null ? AreaMassProperties.Compute(brep) : null;
            return Math.Abs(area?.Area ?? 0.0);
        }

        private static bool CurveContainsCurve(Curve outer, Curve inner, double tolerance)
        {
            try
            {
                RegionContainment relationship = Curve.PlanarClosedCurveRelationship(outer, inner, Plane.WorldXY, tolerance);
                return relationship == RegionContainment.BInsideA;
            }
            catch
            {
                return false;
            }
        }

        private static int GetRegionDepth(List<SectionRegion> regions, int index)
        {
            int depth = 0;
            int parent = regions[index].ParentIndex;
            HashSet<int> visited = new HashSet<int>();
            while (parent >= 0 && parent < regions.Count && visited.Add(parent))
            {
                depth++;
                parent = regions[parent].ParentIndex;
            }

            return depth;
        }

        private static Brep[] TryBooleanDifference(Brep solid, List<Brep> holes, double tolerance)
        {
            try
            {
                Brep[] difference = Brep.CreateBooleanDifference(new[] { solid }, holes, tolerance);
                if (difference != null && difference.Length > 0)
                    return difference.Where(item => item != null).ToArray();
            }
            catch
            {
            }

            return null;
        }

        private static void AddMake2DRecords(SectionBuildData data, List<Curve> visible, List<int> visibleIndex, List<string> visibleTypes, List<Curve> hidden, List<int> hiddenIndex, List<string> hiddenTypes)
        {
            for (int i = 0; i < visible.Count; i++)
            {
                Curve curve = visible[i]?.DuplicateCurve();
                if (curve == null)
                    continue;

                int objectIndex = i < visibleIndex.Count ? visibleIndex[i] : -1;
                string type = NormalizeLineType(i < visibleTypes.Count ? visibleTypes[i] : string.Empty);
                if (IsSectionLineType(type))
                    data.SectionCurves.Add(new CurveRecord(curve, objectIndex));
                else
                    data.VisibleCurves.Add(new TypedCurveRecord(curve, objectIndex, type));
            }

            for (int i = 0; i < hidden.Count; i++)
            {
                Curve curve = hidden[i]?.DuplicateCurve();
                if (curve == null)
                    continue;

                int objectIndex = i < hiddenIndex.Count ? hiddenIndex[i] : -1;
                string type = NormalizeLineType(i < hiddenTypes.Count ? hiddenTypes[i] : string.Empty);
                data.HiddenCurves.Add(new TypedCurveRecord(curve, objectIndex, type));
            }
        }

        private static Transform CreatePreviewTransform(SectionBuildData data, BoundingBox sourceBox, BoundingBox previousPreviewBox, double gap)
        {
            BoundingBox localBox = data.GetBoundingBox();
            if (!localBox.IsValid)
                return Transform.Identity;

            double targetMinX;
            double targetCenterY;
            if (previousPreviewBox.IsValid)
            {
                targetMinX = previousPreviewBox.Max.X + gap;
                targetCenterY = previousPreviewBox.Center.Y;
            }
            else if (sourceBox.IsValid)
            {
                targetMinX = sourceBox.Max.X + gap;
                targetCenterY = sourceBox.Center.Y;
            }
            else
            {
                targetMinX = gap;
                targetCenterY = 0.0;
            }

            Vector3d move = new Vector3d(targetMinX - localBox.Min.X, targetCenterY - localBox.Center.Y, -localBox.Min.Z);
            return Transform.Translation(move);
        }

        private static BoundingBox AppendSectionBuildData(SectionCache cache, SectionBuildData data, int sectionIndex, Transform transform)
        {
            BoundingBox placedBox = BoundingBox.Empty;
            Dictionary<int, int> surfaceCounters = new Dictionary<int, int>();
            Dictionary<int, int> sectionCounters = new Dictionary<int, int>();

            foreach (BrepRecord record in data.SectionSurfaces)
            {
                Brep brep = record.Brep?.DuplicateBrep();
                if (brep == null)
                    continue;

                brep.Transform(transform);
                placedBox.Union(brep.GetBoundingBox(true));
                GH_Path path = ElementPath(sectionIndex, record.ObjectIndex, NextCounter(surfaceCounters, record.ObjectIndex));
                cache.SectionSurfaces.Append(new GH_Brep(brep), path);
            }

            foreach (CurveRecord record in data.SectionCurves)
            {
                Curve curve = record.Curve?.DuplicateCurve();
                if (curve == null)
                    continue;

                curve.Transform(transform);
                placedBox.Union(curve.GetBoundingBox(true));
                GH_Path path = ElementPath(sectionIndex, record.ObjectIndex, NextCounter(sectionCounters, record.ObjectIndex));
                cache.SectionGeometry.Append(new GH_Curve(curve), path);
            }

            foreach (TypedCurveRecord record in data.VisibleCurves)
            {
                Curve curve = record.Curve?.DuplicateCurve();
                if (curve == null)
                    continue;

                curve.Transform(transform);
                placedBox.Union(curve.GetBoundingBox(true));
                cache.RawVisibleCurves.Add(new TypedCurveRecord(curve, sectionIndex, record.ObjectIndex, record.Type));
            }

            foreach (TypedCurveRecord record in data.HiddenCurves)
            {
                Curve curve = record.Curve?.DuplicateCurve();
                if (curve == null)
                    continue;

                curve.Transform(transform);
                placedBox.Union(curve.GetBoundingBox(true));
                cache.RawHiddenCurves.Add(new TypedCurveRecord(curve, sectionIndex, record.ObjectIndex, record.Type));
            }

            return placedBox;
        }

        private static int NextCounter(Dictionary<int, int> counters, int objectIndex)
        {
            if (!counters.TryGetValue(objectIndex, out int value))
                value = 0;

            counters[objectIndex] = value + 1;
            return value;
        }

        private static int NextCounter(Dictionary<string, int> counters, int sectionIndex, int objectIndex)
        {
            string key = sectionIndex + ";" + objectIndex;
            if (!counters.TryGetValue(key, out int value))
                value = 0;

            counters[key] = value + 1;
            return value;
        }

        private static GH_Path ElementPath(int sectionIndex, int objectIndex, int elementIndex)
        {
            return new GH_Path(sectionIndex, objectIndex);
        }

        private static string NormalizeLineType(string type)
        {
            return string.IsNullOrWhiteSpace(type) ? OtherLineType : type.Trim();
        }

        private static bool IsSectionLineType(string type)
        {
            return !string.IsNullOrWhiteSpace(type) && type.IndexOf("Section", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsKnownLineType(string type, string[] knownTypes)
        {
            return knownTypes.Any(item => !string.Equals(item, OtherLineType, StringComparison.OrdinalIgnoreCase) && string.Equals(item, type, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsVisibleTypeEnabled(string type)
        {
            if (IsKnownLineType(type, VisibleLineTypes))
                return _visibleLineTypes.Contains(type);

            return _visibleLineTypes.Contains(OtherLineType);
        }

        private bool IsHiddenTypeEnabled(string type)
        {
            if (IsKnownLineType(type, HiddenLineTypes))
                return _hiddenLineTypes.Contains(type);

            return _hiddenLineTypes.Contains(OtherLineType);
        }

        private void AppendLineTypeDiagnostics(SectionCache cache)
        {
            List<string> unknownVisible = cache.RawVisibleCurves
                .Select(item => item.Type)
                .Where(type => !IsKnownLineType(type, VisibleLineTypes))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> unknownHidden = cache.RawHiddenCurves
                .Select(item => item.Type)
                .Where(type => !IsKnownLineType(type, HiddenLineTypes))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (unknownVisible.Count > 0)
                cache.Diagnostics.Add("鍙戠幇鏈垪鍑虹殑鍙绾跨被鍨? " + string.Join(", ", unknownVisible));
            if (unknownHidden.Count > 0)
                cache.Diagnostics.Add("鍙戠幇鏈垪鍑虹殑闅愯棌绾跨被鍨? " + string.Join(", ", unknownHidden));
        }

        private SectionCache Compute(List<IGH_Goo> inputObjects, List<Line> sectionLines, List<string> sectionNames, Point3d insertPoint, bool hasInsertPoint, double scale, double spacing, double textHeight, double textOffset, double viewDepth, double tolerance)
        {
            SectionCache result = new SectionCache();
            result.Diagnostics.Add("鎵ц绔彛褰撳墠锟? True");
            result.Diagnostics.Add("杈撳叆瀵硅薄鏁伴噺: " + inputObjects.Count);
            result.Diagnostics.Add("杈撳叆鍓栧垏绾挎暟锟? " + sectionLines.Count);
            result.Diagnostics.Add("Insert point mode: " + (hasInsertPoint ? "input insert point" : "auto by object bounding box"));
            if (sectionLines.Count == 0)
            {
                result.Diagnostics.Add("Stop: no section lines.");
                return result;
            }

            List<string> collectLogs = new List<string>();
            List<SourceGeometry> source = CollectSourceGeometry(inputObjects, collectLogs);
            result.Diagnostics.Add("鎴愬姛瑙ｆ瀽鍑犱綍鏁伴噺: " + source.Count);
            result.Diagnostics.AddRange(collectLogs);
            List<SectionInfo> sections = BuildUniqueSections(sectionLines, sectionNames, tolerance);
            result.Diagnostics.Add("鏈夋晥鍘婚噸鍚庡墫闈㈡暟锟? " + sections.Count);
            if (sections.Count == 0)
            {
                result.Diagnostics.Add("Stop: no valid sections.");
                return result;
            }

            double safeScale = Math.Abs(scale) < ZeroTolerance ? 1.0 : scale;
            BoundingBox sourceBox = GetSourceBoundingBox(source);
            for (int i = 0; i < sections.Count; i++)
            {
                SectionInfo section = sections[i];
                Point3d target = GetSectionTarget(insertPoint, hasInsertPoint, sourceBox, section.ViewDirection, i, spacing);
                Plane targetPlane = CreateTargetPlane(target, section);
                ProjectionContext projectionContext = CreateProjectionContext(source, section);
                SectionInfo projectionSection = projectionContext.Section;
                Transform toLayout = CreateMake2DToLayoutTransform(target, safeScale);

                GH_Path path = new GH_Path(i);
                InitializeObjectBranches(result, path, inputObjects.Count, _keepEmptyBranches);
                List<SourceGeometry> visibleSource = projectionContext.Source.Where(item => IsInsideViewDepth(item.Geometry, projectionSection, viewDepth)).ToList();
                result.Diagnostics.Add("鍓栭潰 " + section.Name + ": 鑼冨洿鍐呭锟?" + visibleSource.Count + "/" + source.Count);

                result.Diagnostics.Add("鍓栭潰 " + section.Name + ": 灞€閮ㄥ墫鍒囩嚎 From " + FormatPoint(projectionSection.Line.From) + " To " + FormatPoint(projectionSection.Line.To));
                result.Diagnostics.Add("鍓栭潰 " + section.Name + ": 灞€閮ㄥ墫鍒囬潰 Origin " + FormatPoint(projectionSection.Plane.Origin) + " Normal " + FormatVector(projectionSection.Plane.Normal) + " View " + FormatVector(projectionSection.ViewDirection));

                List<SourceGeometry> sectionSource = new List<SourceGeometry>();
                List<SourceGeometry> projectionSource = new List<SourceGeometry>();
                List<SectionPoint> sectionPoints = new List<SectionPoint>();
                SplitSectionAndProjectionSource(visibleSource, projectionSection, tolerance, result, sectionSource, sectionPoints, projectionSource);
                AddSectionSurfaces(result, path, sectionSource, projectionSection.Plane, toLayout, tolerance);

                using (NativeMake2DViewContext viewContext = CreateNativeMake2DViewContext(visibleSource, projectionSection, result, "Section " + section.Name + ": native Make2D view"))
                {
                    if (viewContext == null)
                        continue;

                int beforeSection = result.SectionGeometry.DataCount;
                int beforeVisible = result.VisibleCurves.DataCount;
                int beforeHidden = result.HiddenCurves.DataCount;
                AddProjectedSectionLines(result, path, sectionSource, sectionPoints, viewContext, projectionSection, toLayout, tolerance);
                AddHiddenLine(result, path, projectionSource, viewContext, projectionSection, toLayout, tolerance);
                result.Diagnostics.Add("鍓栭潰 " + section.Name + ": 鎴潰 " + (result.SectionGeometry.DataCount - beforeSection) + "锛屽疄锟?" + (result.VisibleCurves.DataCount - beforeVisible) + "锛岃櫄锟?" + (result.HiddenCurves.DataCount - beforeHidden));
                }
                AddSectionSymbol(result, path, section, section.Name, textHeight);
                AddTitle(result, path, section.Name, targetPlane, textHeight, textOffset);
                result.Names.Add(section.Name);
            }

            return result;
        }

        private static List<Line> ParseSectionLines(List<IGH_Goo> input, List<string> diagnostics)
        {
            List<Line> result = new List<Line>();
            for (int i = 0; i < input.Count; i++)
            {
                IGH_Goo goo = input[i];
                if (goo == null)
                {
                    diagnostics.Add("剖切位置 " + i + "：输入为空。");
                    continue;
                }

                Line line = Line.Unset;
                bool hasLine = false;
                if (goo is GH_Line ghLine)
                {
                    line = ghLine.Value;
                    hasLine = true;
                }
                else if (goo is GH_Curve ghCurve)
                {
                    hasLine = TryGetLineFromCurve(ghCurve.Value, out line);
                }
                else if (goo is GH_ObjectWrapper wrapper)
                {
                    if (wrapper.Value is Line wrappedLine)
                    {
                        line = wrappedLine;
                        hasLine = true;
                    }
                    else if (wrapper.Value is Curve wrappedCurve)
                    {
                        hasLine = TryGetLineFromCurve(wrappedCurve, out line);
                    }
                }
                else if (goo.CastTo(out Line castLine))
                {
                    line = castLine;
                    hasLine = true;
                }
                else if (goo.CastTo(out Curve castCurve))
                {
                    hasLine = TryGetLineFromCurve(castCurve, out line);
                }

                if (!hasLine)
                {
                    diagnostics.Add("剖切位置 " + i + "：不是直线 Curve 或 Line，类型为 " + goo.GetType().Name + "。");
                    continue;
                }

                if (!line.IsValid || line.Length <= ZeroTolerance)
                {
                    diagnostics.Add("剖切位置 " + i + "：直线无效或长度过短。");
                    continue;
                }

                line = new Line(
                    new Point3d(line.From.X, line.From.Y, 0.0),
                    new Point3d(line.To.X, line.To.Y, 0.0));
                result.Add(line);
            }

            return result;
        }

        private void UpdatePreviewSections(List<Line> sectionLines, List<string> sectionNames, List<string> validation, double tolerance)
        {
            _previewSections.Clear();
            if (validation.Count > 0 || sectionLines.Count == 0)
                return;

            _previewSections.AddRange(BuildUniqueSections(sectionLines, sectionNames, tolerance));
        }

        private static bool TryGetLineFromCurve(Curve curve, out Line line)
        {
            line = Line.Unset;
            if (curve == null)
                return false;

            LineCurve lineCurve = curve as LineCurve;
            if (lineCurve != null)
            {
                line = lineCurve.Line;
                return true;
            }

            if (!curve.IsLinear())
                return false;

            line = new Line(curve.PointAtStart, curve.PointAtEnd);
            return line.IsValid;
        }

        private static List<SourceGeometry> CollectSourceGeometry(List<IGH_Goo> inputObjects, List<string> diagnostics, int indexOffset = 0, bool isReference = false)
        {
            List<SourceGeometry> result = new List<SourceGeometry>();
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            for (int i = 0; i < inputObjects.Count; i++)
            {
                IGH_Goo goo = inputObjects[i];
                int sourceIndex = indexOffset + i;
                if (goo == null)
                    continue;

                GeometryBase geometry = null;
                Guid id = Guid.Empty;
                bool handled = false;

                if (goo is GH_Brep ghBrep)
                    geometry = ghBrep.Value;
                else if (goo is GH_Surface ghSurface)
                    geometry = ghSurface.Value;
                else if (goo is GH_Curve ghCurve)
                    geometry = ghCurve.Value;
                else if (goo is GH_Guid ghGuid)
                    id = ghGuid.Value;
                else if (goo is GH_ObjectWrapper wrapper)
                {
                    object value = wrapper.Value;
                    if (value is GeometryBase wrappedGeometry)
                        geometry = wrappedGeometry;
                    else if (value is Guid wrappedId)
                        id = wrappedId;
                    else if (value is RhinoObject wrappedObject)
                    {
                        AddRhinoObjectGeometry(doc, wrappedObject, Transform.Identity, sourceIndex, result, new HashSet<Guid>(), isReference);
                        handled = true;
                    }
                    else if (value is Plane wrappedPlane)
                        geometry = CreateReferencePlaneGeometry(wrappedPlane);
                    else if (value is Box wrappedBox)
                        geometry = wrappedBox.ToBrep();
                    else if (value is BoundingBox wrappedBoundingBox)
                        geometry = new Box(wrappedBoundingBox).ToBrep();
                }
                else if (goo.CastTo(out GeometryBase castGeometry))
                    geometry = castGeometry;
                else if (goo.CastTo(out Guid castId))
                    id = castId;
                else if (goo.CastTo(out Plane castPlane))
                    geometry = CreateReferencePlaneGeometry(castPlane);
                else if (goo.CastTo(out Box castBox))
                    geometry = castBox.ToBrep();
                else if (goo.CastTo(out BoundingBox castBoundingBox))
                    geometry = new Box(castBoundingBox).ToBrep();

                if (handled)
                    continue;

                if (id != Guid.Empty && doc != null)
                {
                    int before = result.Count;
                    RhinoObject obj = doc.Objects.FindId(id);
                    AddRhinoObjectGeometry(doc, obj, Transform.Identity, sourceIndex, result, new HashSet<Guid>(), isReference);
                    if (result.Count == before)
                        diagnostics.Add("对象 " + i + "：未找到 Guid 对应对象，或对象没有可用几何。");
                    continue;
                }

                int beforeAdd = result.Count;
                AddGeometry(doc, geometry, Transform.Identity, sourceIndex, result, new HashSet<Guid>(), isReference);
                if (result.Count == beforeAdd)
                    diagnostics.Add("对象 " + i + "：无法解析为 Brep、Surface、Curve、Guid 或块，类型为 " + goo.GetType().Name + "。");
            }

            return result;
        }

        private static void AddRhinoObjectGeometry(RhinoDoc doc, RhinoObject obj, Transform transform, int index, List<SourceGeometry> result, HashSet<Guid> visited, bool isReference = false)
        {
            if (obj?.Geometry == null)
                return;

            if (obj is InstanceObject instance && instance.InstanceDefinition != null)
            {
                AddInstanceDefinition(doc, instance.InstanceDefinition, transform * instance.InstanceXform, index, result, visited, isReference);
                return;
            }

            AddGeometry(doc, obj.Geometry, transform, index, result, visited, isReference);
        }

        private static void AddInstanceDefinition(RhinoDoc doc, InstanceDefinition definition, Transform transform, int index, List<SourceGeometry> result, HashSet<Guid> visited, bool isReference = false)
        {
            if (definition == null || !visited.Add(definition.Id))
                return;

            foreach (RhinoObject obj in definition.GetObjects())
                AddRhinoObjectGeometry(doc, obj, transform, index, result, visited, isReference);

            visited.Remove(definition.Id);
        }

        private static void AddGeometry(RhinoDoc doc, GeometryBase geometry, Transform transform, int index, List<SourceGeometry> result, HashSet<Guid> visited, bool isReference = false)
        {
            if (geometry == null)
                return;

            if (geometry is InstanceReferenceGeometry reference && doc != null)
            {
                InstanceDefinition definition = doc.InstanceDefinitions.FindId(reference.ParentIdefId);
                AddInstanceDefinition(doc, definition, transform * reference.Xform, index, result, visited, isReference);
                return;
            }

            GeometryBase duplicate = geometry.Duplicate();
            if (duplicate == null)
                return;

            if (duplicate is Surface surface)
                duplicate = surface.ToBrep();
            else if (duplicate is Extrusion extrusion)
                duplicate = extrusion.ToBrep();

            duplicate.Transform(transform);
            result.Add(new SourceGeometry(duplicate, index, isReference));
        }

        private static Brep CreateReferencePlaneGeometry(Plane plane)
        {
            const double halfSize = 500.0;
            PlaneSurface surface = new PlaneSurface(plane, new Interval(-halfSize, halfSize), new Interval(-halfSize, halfSize));
            return surface.ToBrep();
        }

        private static Brep CreateReferenceBoundingBoxGeometry(BoundingBox box, double tolerance)
        {
            if (!box.IsValid)
                return null;

            double minSize = Math.Max(tolerance, 0.001);
            Point3d min = box.Min;
            Point3d max = box.Max;
            if (Math.Abs(max.X - min.X) < minSize)
            {
                min.X -= minSize * 0.5;
                max.X += minSize * 0.5;
            }
            if (Math.Abs(max.Y - min.Y) < minSize)
            {
                min.Y -= minSize * 0.5;
                max.Y += minSize * 0.5;
            }
            if (Math.Abs(max.Z - min.Z) < minSize)
            {
                min.Z -= minSize * 0.5;
                max.Z += minSize * 0.5;
            }

            return new Box(new BoundingBox(min, max)).ToBrep();
        }

        private static BoundingBox GetSourceBoundingBox(List<SourceGeometry> source)
        {
            BoundingBox box = BoundingBox.Empty;
            foreach (SourceGeometry item in source)
            {
                BoundingBox itemBox = item.Geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;
                if (itemBox.IsValid)
                    box.Union(itemBox);
            }

            return box;
        }

        private static Point3d GetSectionTarget(Point3d insertPoint, bool hasInsertPoint, BoundingBox sourceBox, Vector3d viewDirection, int sectionIndex, double spacing)
        {
            if (hasInsertPoint)
                return insertPoint + new Vector3d(sectionIndex * spacing, 0.0, 0.0);

            if (!sourceBox.IsValid)
                return Point3d.Origin;

            Vector3d direction = viewDirection;
            direction.Z = 0.0;
            if (!direction.Unitize())
                direction = Vector3d.XAxis;

            double offset = Math.Abs(spacing);
            if (offset < ZeroTolerance)
                offset = Math.Max(1.0, sourceBox.Diagonal.Length * 0.25);

            Point3d center = sourceBox.Center;
            Point3d[] corners = sourceBox.GetCorners();
            double maxProjection = corners.Max(point => (point - center) * direction);
            Point3d target = center + direction * (maxProjection + offset + sectionIndex * offset);
            target.Z = 0.0;
            return target;
        }

        private static Plane CreateTargetPlane(Point3d origin, SectionInfo section)
        {
            Vector3d xAxis = section.Line.Direction;
            xAxis.Z = 0.0;
            if (!xAxis.Unitize())
                xAxis = Vector3d.XAxis;

            Vector3d yAxis = -section.ViewDirection;
            yAxis.Z = 0.0;
            if (!yAxis.Unitize())
                yAxis = Vector3d.YAxis;

            return new Plane(origin, xAxis, yAxis);
        }

        private static Transform CreateMake2DToLayoutTransform(Point3d target, double scale)
        {
            Point3d layoutTarget = new Point3d(target.X, target.Y, 0.0);
            return Transform.Translation(layoutTarget - Point3d.Origin) * Transform.Scale(Point3d.Origin, scale);
        }

        private static Plane CreateMake2DProjectionPlane(Line line, Point3d origin)
        {
            Vector3d lineDirection = line.Direction;
            lineDirection.Z = 0.0;
            if (!lineDirection.Unitize())
                lineDirection = Vector3d.XAxis;

            Vector3d xAxis;
            Vector3d yAxis;
            if (Math.Abs(lineDirection.X) >= Math.Abs(lineDirection.Y))
            {
                xAxis = -lineDirection;
                yAxis = Vector3d.ZAxis;
                if (lineDirection.X < 0.0)
                {
                    xAxis = -xAxis;
                    yAxis = -yAxis;
                }
            }
            else
            {
                xAxis = -Vector3d.ZAxis;
                yAxis = -lineDirection;
                if (lineDirection.Y > 0.0)
                {
                    xAxis = -xAxis;
                    yAxis = -yAxis;
                }
            }

            return new Plane(origin, xAxis, yAxis);
        }

        private static ProjectionContext CreateProjectionContext(List<SourceGeometry> source, SectionInfo section)
        {
            List<SourceGeometry> localSource = DuplicateTransformedSource(source, Transform.Identity);
            Line localLine = section.Line;
            Plane localPlane = section.Plane;
            Point3d localizationCenter = Point3d.Origin;

            BoundingBox localBox = GetSourceBoundingBox(localSource);
            if (localBox.IsValid)
            {
                Point3d center = localBox.Center;
                localizationCenter = center;
                Transform centerToOrigin = Transform.Translation(-center.X, -center.Y, -center.Z);
                foreach (SourceGeometry item in localSource)
                    item.Geometry.Transform(centerToOrigin);
                localLine.Transform(centerToOrigin);
                localPlane.Transform(centerToOrigin);
            }

            Vector3d localLineDirection = localLine.Direction;
            localLineDirection.Z = 0.0;
            if (!localLineDirection.Unitize())
                localLineDirection = Vector3d.XAxis;

            Vector3d localViewDirection = Vector3d.CrossProduct(Vector3d.ZAxis, localLineDirection);
            if (!localViewDirection.Unitize())
                localViewDirection = Vector3d.YAxis;

            SectionInfo localSection = new SectionInfo(localLine, localPlane, localViewDirection, section.Name);
            return new ProjectionContext(localSource, localSection, localizationCenter);
        }

        private static string FormatPoint(Point3d point)
        {
            return "(" + point.X.ToString("0.###") + ", " + point.Y.ToString("0.###") + ", " + point.Z.ToString("0.###") + ")";
        }

        private static string FormatVector(Vector3d vector)
        {
            return "(" + vector.X.ToString("0.###") + ", " + vector.Y.ToString("0.###") + ", " + vector.Z.ToString("0.###") + ")";
        }

        private static List<SourceGeometry> DuplicateTransformedSource(List<SourceGeometry> source, Transform transform)
        {
            List<SourceGeometry> result = new List<SourceGeometry>();
            foreach (SourceGeometry item in source)
            {
                GeometryBase duplicate = item.Geometry?.Duplicate();
                if (duplicate == null)
                    continue;

                duplicate.Transform(transform);
                result.Add(new SourceGeometry(duplicate, item.Index, item.IsReference));
            }

            return result;
        }

        private List<SectionInfo> BuildUniqueSections(List<Line> lines, List<string> names, double tolerance)
        {
            List<SectionInfo> result = new List<SectionInfo>();
            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i];
                if (!line.IsValid || line.Length <= tolerance)
                    continue;

                Vector3d xAxis = line.Direction;
                if (!xAxis.Unitize())
                    continue;

                Vector3d cutDirection = Vector3d.CrossProduct(Vector3d.ZAxis, xAxis);
                if (!cutDirection.Unitize())
                    continue;

                Plane plane = new Plane(line.From, xAxis, Vector3d.ZAxis);
                string name = i < names.Count && !string.IsNullOrWhiteSpace(names[i]) ? names[i] : (i + 1).ToString();
                SectionInfo info = new SectionInfo(line, plane, cutDirection, name);

                if (result.Any(existing => AreSameSection(existing, info, tolerance)))
                    continue;

                result.Add(info);
            }

            return result;
        }

        private static bool AreSameSection(SectionInfo a, SectionInfo b, double tolerance)
        {
            if (Math.Abs(a.Plane.Normal * b.Plane.Normal) < 1.0 - 1e-6)
                return false;

            double d = Math.Abs(a.Plane.DistanceTo(b.Line.From));
            return d <= tolerance;
        }

        private static bool IsInsideViewDepth(GeometryBase geometry, SectionInfo section, double viewDepth)
        {
            if (geometry == null)
                return false;

            BoundingBox box = geometry.GetBoundingBox(true);
            if (!box.IsValid)
                return false;

            Point3d[] corners = box.GetCorners();
            double max = corners.Max(pt => (pt - section.Line.From) * section.ViewDirection);
            if (max < -ZeroTolerance)
                return false;

            if (viewDepth > ZeroTolerance)
            {
                double min = corners.Min(pt => (pt - section.Line.From) * section.ViewDirection);
                if (min > viewDepth)
                    return false;
            }

            return true;
        }

        private void SplitSectionAndProjectionSource(List<SourceGeometry> source, SectionInfo section, double tolerance, SectionCache result, List<SourceGeometry> sectionSource, List<SectionPoint> sectionPoints, List<SourceGeometry> projectionSource)
        {
            foreach (SourceGeometry item in source)
            {
                List<Curve> curves = new List<Curve>();
                List<Point3d> points = new List<Point3d>();
                bool intersects = TryExtractSectionGeometry(item.Geometry, section.Plane, tolerance, curves, points);
                if (!intersects)
                {
                    projectionSource.Add(item);
                    continue;
                }

                foreach (Curve curve in curves)
                {
                    if (curve != null)
                        sectionSource.Add(new SourceGeometry(curve, item.Index, item.IsReference));
                }

                foreach (Point3d point in points)
                    sectionPoints.Add(new SectionPoint(point, item.Index));

                AddProjectionSideGeometry(item, section, tolerance, result, projectionSource);
            }
        }

        private static bool TryExtractSectionGeometry(GeometryBase geometry, Plane plane, double tolerance, List<Curve> curves, List<Point3d> points)
        {
            if (geometry is Brep brep)
            {
                bool hit = Intersection.BrepPlane(brep, plane, tolerance, out Curve[] sectionCurves, out Point3d[] sectionPoints);
                if (sectionCurves != null)
                    curves.AddRange(sectionCurves.Where(curve => curve != null));
                if (sectionPoints != null)
                    points.AddRange(sectionPoints);
                return hit && (curves.Count > 0 || points.Count > 0);
            }

            if (geometry is Curve curveGeometry)
            {
                CurveIntersections intersections = Intersection.CurvePlane(curveGeometry, plane, tolerance);
                if (intersections == null || intersections.Count == 0)
                    return false;

                foreach (IntersectionEvent ev in intersections)
                {
                    if (ev.IsOverlap)
                    {
                        Curve overlap = curveGeometry.Trim(ev.OverlapA);
                        if (overlap != null)
                            curves.Add(overlap);
                    }
                    else
                    {
                        points.Add(ev.PointA);
                    }
                }

                return curves.Count > 0 || points.Count > 0;
            }

            return false;
        }

        private void AddProjectionSideGeometry(SourceGeometry item, SectionInfo section, double tolerance, SectionCache result, List<SourceGeometry> projectionSource)
        {
            if (item.Geometry is Brep brep)
            {
                List<Brep> pieces = SplitBrepOnProjectionSide(brep, section, tolerance);
                if (pieces.Count > 0)
                {
                    foreach (Brep piece in pieces)
                        projectionSource.Add(new SourceGeometry(piece, item.Index, item.IsReference));
                    return;
                }

                WarnSplitFailure(result, section, item.Index, "Brep 已与剖切面相交但无法分割，将使用完整对象参与投影。");
                projectionSource.Add(item);
                return;
            }

            if (item.Geometry is Curve curve)
            {
                List<Curve> pieces = SplitCurveOnProjectionSide(curve, section, tolerance);
                if (pieces.Count > 0)
                {
                    foreach (Curve piece in pieces)
                        projectionSource.Add(new SourceGeometry(piece, item.Index, item.IsReference));
                    return;
                }

                if (IsGeometryOnProjectionSide(curve, section, tolerance))
                    projectionSource.Add(item);
                else
                    WarnSplitFailure(result, section, item.Index, "曲线已与剖切面相交但无法保留投影侧线段，已跳过该对象。");
            }
        }

        private static List<Brep> SplitBrepOnProjectionSide(Brep brep, SectionInfo section, double tolerance)
        {
            List<Brep> result = new List<Brep>();
            Brep cutter = CreatePlaneCutter(section.Plane, brep.GetBoundingBox(true));
            if (cutter == null)
                return result;

            Brep[] pieces = null;
            try
            {
                pieces = brep.Split(cutter, tolerance);
            }
            catch
            {
                pieces = null;
            }

            if (pieces == null || pieces.Length <= 1)
                return result;

            foreach (Brep piece in pieces)
            {
                if (piece != null && IsGeometryOnProjectionSide(piece, section, tolerance))
                    result.Add(piece);
            }

            return result;
        }

        private static Brep CreatePlaneCutter(Plane plane, BoundingBox box)
        {
            if (!box.IsValid)
                return null;

            double size = Math.Max(1.0, box.Diagonal.Length) * 4.0;
            PlaneSurface surface = new PlaneSurface(plane, new Interval(-size, size), new Interval(-size, size));
            return surface.ToBrep();
        }

        private static List<Curve> SplitCurveOnProjectionSide(Curve curve, SectionInfo section, double tolerance)
        {
            List<Curve> result = new List<Curve>();
            CurveIntersections intersections = Intersection.CurvePlane(curve, section.Plane, tolerance);
            if (intersections == null || intersections.Count == 0)
                return result;

            List<double> parameters = new List<double>();
            foreach (IntersectionEvent ev in intersections)
            {
                if (!ev.IsOverlap)
                    parameters.Add(ev.ParameterA);
            }

            parameters = parameters
                .Where(t => t > curve.Domain.T0 + tolerance && t < curve.Domain.T1 - tolerance)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            Curve[] pieces = parameters.Count > 0 ? curve.Split(parameters) : null;
            if (pieces == null || pieces.Length == 0)
                return result;

            foreach (Curve piece in pieces)
            {
                if (piece == null)
                    continue;

                double t = piece.Domain.Mid;
                Point3d midpoint = piece.PointAt(t);
                if (IsPointOnProjectionSide(midpoint, section, tolerance))
                    result.Add(piece);
            }

            return result;
        }

        private static bool IsGeometryOnProjectionSide(GeometryBase geometry, SectionInfo section, double tolerance)
        {
            BoundingBox box = geometry.GetBoundingBox(true);
            if (!box.IsValid)
                return false;

            Point3d center = box.Center;
            if (IsPointOnProjectionSide(center, section, tolerance))
                return true;

            return box.GetCorners().Any(point => IsPointOnProjectionSide(point, section, tolerance));
        }

        private static bool IsPointOnProjectionSide(Point3d point, SectionInfo section, double tolerance)
        {
            return (point - section.Line.From) * section.ViewDirection >= -tolerance;
        }

        private void WarnSplitFailure(SectionCache result, SectionInfo section, int index, string message)
        {
            string fullMessage = "剖面 " + section.Name + "：输入对象 " + index + "，" + message;
            result.Diagnostics.Add(fullMessage);
            AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, fullMessage);
        }

        private static void AddSectionIntersections(SectionCache result, GH_Path path, List<SourceGeometry> source, Plane plane, Transform toLayout, double tolerance)
        {
            foreach (SourceGeometry item in source)
            {
                if (item.Geometry is Brep brep)
                {
                    if (Intersection.BrepPlane(brep, plane, tolerance, out Curve[] curves, out Point3d[] points))
                    {
                        AddCurves(result.SectionGeometry, result.SectionIndex, path, curves, toLayout, item.Index);
                        AddPoints(result.SectionGeometry, result.SectionIndex, path, points, toLayout, item.Index);
                    }
                }
                else if (item.Geometry is Curve curve)
                {
                    CurveIntersections intersections = Intersection.CurvePlane(curve, plane, tolerance);
                    if (intersections == null)
                        continue;

                    foreach (IntersectionEvent ev in intersections)
                    {
                        if (ev.IsOverlap)
                        {
                            Curve overlap = curve.Trim(ev.OverlapA);
                            if (overlap != null)
                            {
                                overlap.Transform(toLayout);
                                result.SectionGeometry.Append(new GH_Curve(overlap), path);
                                result.SectionIndex.Add(item.Index, path);
                            }
                        }
                        else
                        {
                            Point3d point = ev.PointA;
                            point.Transform(toLayout);
                            result.SectionGeometry.Append(new GH_Point(point), path);
                            result.SectionIndex.Add(item.Index, path);
                        }
                    }
                }
            }
        }

        private static void AddCurves(GH_Structure<IGH_GeometricGoo> geometryTree, DataTree<int> indexTree, GH_Path path, IEnumerable<Curve> curves, Transform transform, int index)
        {
            if (curves == null)
                return;

            foreach (Curve curve in curves)
            {
                Curve duplicate = curve?.DuplicateCurve();
                if (duplicate == null)
                    continue;

                duplicate.Transform(transform);
                geometryTree.Append(new GH_Curve(duplicate), path);
                indexTree.Add(index, path);
            }
        }

        private static void AddPoints(GH_Structure<IGH_GeometricGoo> geometryTree, DataTree<int> indexTree, GH_Path path, IEnumerable<Point3d> points, Transform transform, int index)
        {
            if (points == null)
                return;

            foreach (Point3d point in points)
            {
                Point3d duplicate = point;
                duplicate.Transform(transform);
                geometryTree.Append(new GH_Point(duplicate), path);
                indexTree.Add(index, path);
            }
        }

        private static GH_Path ObjectPath(GH_Path sectionPath, int inputIndex)
        {
            return inputIndex >= 0 ? sectionPath.AppendElement(inputIndex) : sectionPath;
        }

        private static void InitializeObjectBranches(SectionCache result, GH_Path sectionPath, int inputCount, bool keepEmptyBranches)
        {
            if (!keepEmptyBranches)
                return;

            for (int i = 0; i < inputCount; i++)
            {
                GH_Path path = ObjectPath(sectionPath, i);
                result.SectionGeometry.EnsurePath(path);
                result.SectionIndex.EnsurePath(path);
                result.VisibleCurves.EnsurePath(path);
                result.VisibleIndex.EnsurePath(path);
                result.HiddenCurves.EnsurePath(path);
                result.HiddenIndex.EnsurePath(path);
                result.SectionSurfaces.EnsurePath(path);
            }
        }

        private static void AddSectionSurfaces(SectionCache result, GH_Path sectionPath, List<SourceGeometry> sectionSource, Plane plane, Transform toLayout, double tolerance)
        {
            int sectionIndex = sectionPath.Length > 0 ? sectionPath[0] : 0;
            Dictionary<int, int> counters = new Dictionary<int, int>();
            foreach (IGrouping<int, SourceGeometry> group in sectionSource.GroupBy(item => item.Index))
            {
                List<Curve> closedCurves = new List<Curve>();
                foreach (SourceGeometry item in group)
                {
                    Curve curve = item.Geometry as Curve;
                    if (curve == null || !curve.IsClosed)
                        continue;

                    Curve duplicate = curve.DuplicateCurve();
                    if (duplicate != null)
                        closedCurves.Add(duplicate);
                }

                if (closedCurves.Count == 0)
                    continue;

                Brep[] breps = Brep.CreatePlanarBreps(closedCurves, tolerance);
                if (breps == null || breps.Length == 0)
                    continue;

                GH_Path path = ObjectPath(sectionPath, group.Key);
                foreach (Brep brep in breps)
                {
                    if (brep == null)
                        continue;

                    Brep duplicate = brep.DuplicateBrep();
                    duplicate.Transform(toLayout);
                    GH_Path elementPath = ElementPath(sectionIndex, group.Key, NextCounter(counters, group.Key));
                    result.SectionSurfaces.Append(new GH_Brep(duplicate), elementPath);
                }
            }
        }

        private void AddProjectedSectionLines(SectionCache result, GH_Path path, List<SourceGeometry> sectionSource, List<SectionPoint> sectionPoints, NativeMake2DViewContext viewContext, SectionInfo section, Transform toLayout, double tolerance)
        {
            foreach (SectionPoint point in sectionPoints)
            {
                Point3d transformed = point.Point;
                transformed.Transform(toLayout);
                GH_Path objectPath = ObjectPath(path, point.Index);
                result.SectionGeometry.Append(new GH_Point(transformed), objectPath);
                result.SectionIndex.Add(point.Index, objectPath);
            }

            if (sectionSource.Count == 0)
            {
                result.Diagnostics.Add("Section " + section.Name + ": section Make2D skipped; no section curves.");
                return;
            }

            MoveSourceToViewPlaneFront(sectionSource, viewContext.ViewRectangle.Plane, section, 100.0, result, "鍓栭潰 " + section.Name + ": 鎴潰Make2D");
            DiagnoseSourceAgainstViewRectangle(sectionSource, viewContext.ViewRectangle, tolerance, result, "鍓栭潰 " + section.Name + ": 鎴潰Make2D");
            result.Diagnostics.Add("鍓栭潰 " + section.Name + ": 鎴潰Make2D鎸夊墫鍒囩嚎涓績璐村埌瑙嗗浘骞抽潰鍓嶆柟 100");

            if (!RunNativeMake2D(sectionSource, viewContext, section, tolerance, result, "鍓栭潰 " + section.Name + ": 鎴潰Make2D", out List<Curve> visible, out List<int> visibleIndex, out List<Curve> hidden, out List<int> hiddenIndex))
                return;

            for (int i = 0; i < visible.Count; i++)
            {
                Curve curve = visible[i];
                curve.Transform(toLayout);
                int index = i < visibleIndex.Count ? visibleIndex[i] : -1;
                GH_Path objectPath = ObjectPath(path, index);
                result.SectionGeometry.Append(new GH_Curve(curve), objectPath);
                result.SectionIndex.Add(index, objectPath);
            }

            for (int i = 0; i < hidden.Count; i++)
            {
                Curve curve = hidden[i];
                curve.Transform(toLayout);
                int index = i < hiddenIndex.Count ? hiddenIndex[i] : -1;
                GH_Path objectPath = ObjectPath(path, index);
                result.SectionGeometry.Append(new GH_Curve(curve), objectPath);
                result.SectionIndex.Add(index, objectPath);
            }

            result.Diagnostics.Add("鍓栭潰 " + section.Name + ": 鎴潰Make2D绾挎 " + (visible.Count + hidden.Count));
        }

        private static void MoveSourceToViewPlaneFront(List<SourceGeometry> source, Plane viewPlane, SectionInfo section, double frontOffset, SectionCache result, string logPrefix)
        {
            Vector3d front = viewPlane.Normal;
            if (front * section.ViewDirection < 0.0)
                front = -front;

            Point3d sample = section.Line.PointAt(0.5);
            double before = viewPlane.DistanceTo(sample);
            Vector3d move = front * frontOffset - viewPlane.Normal * before;
            Transform transform = Transform.Translation(move);
            int moved = 0;

            foreach (SourceGeometry item in source)
            {
                if (item.Geometry == null)
                    continue;

                item.Geometry.Transform(transform);
                moved++;
            }

            if (moved == 0)
            {
                result.Diagnostics.Add(logPrefix + "MoveToViewFront: no section curves can be moved.");
                return;
            }

            Point3d movedSample = sample;
            movedSample.Transform(transform);
            double after = viewPlane.DistanceTo(movedSample);
            result.Diagnostics.Add(logPrefix + "鎸夊墫鍒囩嚎涓績璐村埌瑙嗗浘骞抽潰鍓嶆柟 " + FormatDouble(frontOffset) + "锛岀Щ鍔ㄥ锟?" + moved + "锛岀Щ鍔ㄥ墠璺濈 " + FormatDouble(before) + "锛岀Щ鍔ㄥ悗璺濈 " + FormatDouble(after));
        }

        private static void DiagnoseSourceAgainstViewRectangle(List<SourceGeometry> source, Rectangle3d viewRectangle, double tolerance, SectionCache result, string logPrefix)
        {
            Plane plane = viewRectangle.Plane;
            double x0 = Math.Min(viewRectangle.X.T0, viewRectangle.X.T1);
            double x1 = Math.Max(viewRectangle.X.T0, viewRectangle.X.T1);
            double y0 = Math.Min(viewRectangle.Y.T0, viewRectangle.Y.T1);
            double y1 = Math.Max(viewRectangle.Y.T0, viewRectangle.Y.T1);

            bool hasPoint = false;
            double minU = double.MaxValue;
            double maxU = double.MinValue;
            double minV = double.MaxValue;
            double maxV = double.MinValue;
            int tested = 0;
            int outside = 0;

            foreach (SourceGeometry item in source)
            {
                BoundingBox box = item.Geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;
                if (!box.IsValid)
                    continue;

                foreach (Point3d point in box.GetCorners())
                {
                    tested++;
                    if (!plane.ClosestParameter(point, out double u, out double v))
                        continue;

                    hasPoint = true;
                    minU = Math.Min(minU, u);
                    maxU = Math.Max(maxU, u);
                    minV = Math.Min(minV, v);
                    maxV = Math.Max(maxV, v);
                    if (u < x0 - tolerance || u > x1 + tolerance || v < y0 - tolerance || v > y1 + tolerance)
                        outside++;
                }
            }

            result.Diagnostics.Add(logPrefix + "瑙嗗浘Plane Origin " + FormatPoint(plane.Origin) + " X " + FormatVector(plane.XAxis) + " Y " + FormatVector(plane.YAxis) + " Z " + FormatVector(plane.ZAxis));
            result.Diagnostics.Add(logPrefix + "瑙嗗浘Rectangle X [" + FormatDouble(x0) + ", " + FormatDouble(x1) + "] Y [" + FormatDouble(y0) + ", " + FormatDouble(y1) + "]");
            if (!hasPoint)
            {
                result.Diagnostics.Add(logPrefix + "Mirror range check: no section curve bbox points.");
                return;
            }

            result.Diagnostics.Add(logPrefix + "鎴潰鏇茬嚎Plane鍧愭爣鑼冨洿 U [" + FormatDouble(minU) + ", " + FormatDouble(maxU) + "] V [" + FormatDouble(minV) + ", " + FormatDouble(maxV) + "]");
            result.Diagnostics.Add(logPrefix + "瑙嗛敟鑼冨洿妫€锟? 娴嬭瘯锟?" + tested + "锛岃秴鍑篟ectangle " + outside);
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.###");
        }

        private void AddHiddenLine(SectionCache result, GH_Path path, List<SourceGeometry> source, NativeMake2DViewContext viewContext, SectionInfo section, Transform toLayout, double tolerance)
        {
            if (source.Count == 0)
            {
                result.Diagnostics.Add("Section " + section.Name + ": HiddenLine skipped; no objects in range.");
                return;
            }

            if (!RunNativeMake2D(source, viewContext, section, tolerance, result, "鍓栭潰 " + section.Name + ": Make2D", out List<Curve> visible, out List<int> visibleIndex, out List<Curve> hidden, out List<int> hiddenIndex))
                return;

            for (int i = 0; i < visible.Count; i++)
            {
                Curve curve = visible[i];
                curve.Transform(toLayout);
                int index = i < visibleIndex.Count ? visibleIndex[i] : -1;
                GH_Path objectPath = ObjectPath(path, index);
                result.VisibleCurves.Add(curve, objectPath);
                result.VisibleIndex.Add(index, objectPath);
            }

            for (int i = 0; i < hidden.Count; i++)
            {
                Curve curve = hidden[i];
                curve.Transform(toLayout);
                int index = i < hiddenIndex.Count ? hiddenIndex[i] : -1;
                GH_Path objectPath = ObjectPath(path, index);
                result.HiddenCurves.Add(curve, objectPath);
                result.HiddenIndex.Add(index, objectPath);
            }

            result.Diagnostics.Add("鍓栭潰 " + section.Name + ": Make2D瀹炵嚎 " + visible.Count + "锛岃櫄锟?" + hidden.Count);
        }

        private bool RunNativeMake2D(List<SourceGeometry> source, NativeMake2DViewContext viewContext, SectionInfo section, double tolerance, SectionCache result, string logPrefix, out List<Curve> visible, out List<int> visibleIndex, out List<string> visibleTypes, out List<Curve> hidden, out List<int> hiddenIndex, out List<string> hiddenTypes)
        {
            visible = new List<Curve>();
            visibleIndex = new List<int>();
            visibleTypes = new List<string>();
            hidden = new List<Curve>();
            hiddenIndex = new List<int>();
            hiddenTypes = new List<string>();

            return RunNativeMake2DShared(source, viewContext, section, result, logPrefix, true, out visible, out visibleIndex, out visibleTypes, out hidden, out hiddenIndex, out hiddenTypes);
#if false
            GH_Component make2D = CreateMake2DComponent(out string createError);
            if (make2D == null)
            {
                string message = logPrefix + " failed: native Make2D component not found. " + createError;
                result.Diagnostics.Add(message);
                AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, message);
                return false;
            }

            try
            {
                viewContext.Document.AddObject(make2D, false);
                result.Diagnostics.Add(logPrefix + "缁勪欢: " + make2D.GetType().FullName);
                result.Diagnostics.Add(logPrefix + "杈撳叆绔彛: " + DescribeParams(make2D.Params.Input));
                result.Diagnostics.Add(logPrefix + "杈撳嚭绔彛: " + DescribeParams(make2D.Params.Output));
                TrySetMake2DAutomatic(make2D, result, logPrefix);

                for (int i = 0; i < source.Count; i++)
                {
                    IGH_GeometricGoo goo = CreateGeometryGoo(source[i].Geometry);
                    if (goo != null)
                        SetVolatileInput(make2D, "G", 0, i, goo);
                }

                bool useClippingPlane = true;
                if (useClippingPlane)
                    SetVolatileInput(make2D, "C", 1, 0, new GH_Plane(section.Plane));
                else
                    result.Diagnostics.Add(logPrefix + " clipping plane C is not connected.");

                GH_Component parallelView = CreateMake2DParallelViewComponent(out string viewError);
                if (parallelView == null)
                {
                    string message = logPrefix + " failed: Make2D parallel view component not found. " + viewError;
                    result.Diagnostics.Add(message);
                    AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, message);
                    return false;
                }

                document.AddObject(parallelView, false);
                result.Diagnostics.Add(logPrefix + "瑙嗗浘缁勪欢: " + parallelView.GetType().FullName);
                result.Diagnostics.Add(logPrefix + "瑙嗗浘杈撳叆绔彛: " + DescribeParams(parallelView.Params.Input));
                result.Diagnostics.Add(logPrefix + "瑙嗗浘杈撳嚭绔彛: " + DescribeParams(parallelView.Params.Output));

                Rectangle3d viewRectangle = CreateMake2DViewRectangle(viewSource != null && viewSource.Count > 0 ? viewSource : source, section);
                result.Diagnostics.Add(logPrefix + "瑙嗗浘鑼冨洿瀵硅薄鏁伴噺: " + (viewSource != null ? viewSource.Count : 0));
                GH_Component rectangleComponent = CreateRectangleComponent(out string rectangleError);
                if (rectangleComponent == null)
                {
                    string message = logPrefix + " failed: native Rectangle component not found. " + rectangleError;
                    result.Diagnostics.Add(message);
                    AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, message);
                    return false;
                }

                document.AddObject(rectangleComponent, false);
                result.Diagnostics.Add(logPrefix + "鐭╁舰缁勪欢: " + rectangleComponent.GetType().FullName);
                result.Diagnostics.Add(logPrefix + "鐭╁舰杈撳叆绔彛: " + DescribeParams(rectangleComponent.Params.Input));
                result.Diagnostics.Add(logPrefix + "鐭╁舰杈撳嚭绔彛: " + DescribeParams(rectangleComponent.Params.Output));

                SetVolatileInput(rectangleComponent, "P", 0, 0, new GH_Plane(viewRectangle.Plane));
                SetVolatileInput(rectangleComponent, "X", 1, 0, new GH_Interval(viewRectangle.X));
                SetVolatileInput(rectangleComponent, "Y", 2, 0, new GH_Interval(viewRectangle.Y));
                SetVolatileInput(rectangleComponent, "R", 3, 0, new GH_Number(0.0));

                ConnectComponentOutput(rectangleComponent, 0, parallelView, "P", 0);
                ConnectComponentOutput(parallelView, 0, make2D, "V", 2);

                SetVolatileInput(make2D, "Te", 3, 0, new GH_Boolean(true));
                SetVolatileInput(make2D, "Ts", 4, 0, new GH_Boolean(false));

                ForceCompute(rectangleComponent);
                ForceCompute(parallelView);
                ForceCompute(make2D);
                document.NewSolution(true);
                AppendMake2DRuntimeMessages(rectangleComponent, result, logPrefix + " rectangle");
                result.Diagnostics.Add(logPrefix + "鐭╁舰杈撳叆鏁版嵁锟? " + DescribeParamDataCounts(rectangleComponent.Params.Input));
                result.Diagnostics.Add(logPrefix + "鐭╁舰杈撳嚭鏁版嵁锟? " + DescribeParamDataCounts(rectangleComponent.Params.Output));
                AppendMake2DRuntimeMessages(parallelView, result, logPrefix + " view");
                result.Diagnostics.Add(logPrefix + "瑙嗗浘杈撳叆鏁版嵁锟? " + DescribeParamDataCounts(parallelView.Params.Input));
                result.Diagnostics.Add(logPrefix + "瑙嗗浘杈撳嚭鏁版嵁锟? " + DescribeParamDataCounts(parallelView.Params.Output));
                AppendMake2DRuntimeMessages(make2D, result, logPrefix);
                result.Diagnostics.Add(logPrefix + "杈撳叆鏁版嵁锟? " + DescribeParamDataCounts(make2D.Params.Input));
                result.Diagnostics.Add(logPrefix + "杈撳嚭鏁版嵁锟? " + DescribeParamDataCounts(make2D.Params.Output));

                int visibleOutput = FindParamIndex(make2D.Params.Output, "V", 0);
                int visibleIndexOutput = FindParamIndex(make2D.Params.Output, "Vi", 1);
                int visibleTypeOutput = FindParamIndex(make2D.Params.Output, "Vt", 2);
                int visibleTypeOutput = FindParamIndex(make2D.Params.Output, "Vt", 2);
                int hiddenOutput = FindParamIndex(make2D.Params.Output, "H", 3);
                int hiddenIndexOutput = FindParamIndex(make2D.Params.Output, "Hi", 4);
                int hiddenTypeOutput = FindParamIndex(make2D.Params.Output, "Ht", 5);
                int hiddenTypeOutput = FindParamIndex(make2D.Params.Output, "Ht", 5);

                visible = ReadCurveOutput(make2D, visibleOutput);
                visibleIndex = MapMake2DIndices(ReadIntegerOutput(make2D, visibleIndexOutput), source);
                visibleTypes = ReadTextOutput(make2D, visibleTypeOutput);
                visibleTypes = ReadTextOutput(make2D, visibleTypeOutput);
                hidden = ReadCurveOutput(make2D, hiddenOutput);
                hiddenIndex = MapMake2DIndices(ReadIntegerOutput(make2D, hiddenIndexOutput), source);
                hiddenTypes = ReadTextOutput(make2D, hiddenTypeOutput);
                hiddenTypes = ReadTextOutput(make2D, hiddenTypeOutput);

                result.Diagnostics.Add(logPrefix + "浣跨敤鍘熺敓Make2D锛岃緭锟?" + source.Count + "锛屽疄锟?" + visible.Count + "锛岃櫄锟?" + hidden.Count);
                return true;
            }
            catch (Exception ex)
            {
                string message = logPrefix + " native Make2D execution failed. " + ex.Message;
                result.Diagnostics.Add(message);
                AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, message);
                return false;
            }
            finally
            {
                document.Dispose();
            }
#endif
        }

        private bool RunNativeMake2D(List<SourceGeometry> source, NativeMake2DViewContext viewContext, SectionInfo section, double tolerance, SectionCache result, string logPrefix, out List<Curve> visible, out List<int> visibleIndex, out List<Curve> hidden, out List<int> hiddenIndex)
        {
            List<string> visibleTypes;
            List<string> hiddenTypes;
            return RunNativeMake2D(source, viewContext, section, tolerance, result, logPrefix, out visible, out visibleIndex, out visibleTypes, out hidden, out hiddenIndex, out hiddenTypes);
        }

        private bool RunNativeMake2DWithoutClipping(List<SourceGeometry> source, NativeMake2DViewContext viewContext, SectionInfo section, SectionCache result, string logPrefix, out List<Curve> visible, out List<int> visibleIndex, out List<Curve> hidden, out List<int> hiddenIndex)
        {
            List<string> visibleTypes;
            List<string> hiddenTypes;
            return RunNativeMake2DShared(source, viewContext, section, result, logPrefix, false, out visible, out visibleIndex, out visibleTypes, out hidden, out hiddenIndex, out hiddenTypes);
        }

        private bool RunNativeMake2DShared(List<SourceGeometry> source, NativeMake2DViewContext viewContext, SectionInfo section, SectionCache result, string logPrefix, bool useClippingPlane, out List<Curve> visible, out List<int> visibleIndex, out List<string> visibleTypes, out List<Curve> hidden, out List<int> hiddenIndex, out List<string> hiddenTypes)
        {
            visible = new List<Curve>();
            visibleIndex = new List<int>();
            visibleTypes = new List<string>();
            hidden = new List<Curve>();
            hiddenIndex = new List<int>();
            hiddenTypes = new List<string>();

            if (viewContext == null || viewContext.Document == null || viewContext.ParallelView == null)
            {
                string message = logPrefix + " 失败：共用 Make2D 视图无效。";
                result.Diagnostics.Add(message);
                AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, message);
                return false;
            }

            GH_Component make2D = CreateMake2DComponent(out string createError);
            if (make2D == null)
            {
                string message = logPrefix + " 失败：未找到原生 Make2D 组件。" + createError;
                result.Diagnostics.Add(message);
                AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, message);
                return false;
            }

            try
            {
                viewContext.Document.AddObject(make2D, false);
                result.Diagnostics.Add(logPrefix + " 组件：" + make2D.GetType().FullName);
                result.Diagnostics.Add(logPrefix + " 输入端口：" + DescribeParams(make2D.Params.Input));
                result.Diagnostics.Add(logPrefix + " 输出端口：" + DescribeParams(make2D.Params.Output));
                TrySetMake2DAutomatic(make2D, result, logPrefix);

                for (int i = 0; i < source.Count; i++)
                {
                    IGH_GeometricGoo goo = CreateGeometryGoo(source[i].Geometry);
                    if (goo != null)
                        SetVolatileInput(make2D, "G", 0, i, goo);
                }

                if (useClippingPlane)
                    SetVolatileInput(make2D, "C", 1, 0, new GH_Plane(section.Plane));
                else
                    result.Diagnostics.Add(logPrefix + " 未连接裁剪平面 C。");

                ConnectComponentOutput(viewContext.ParallelView, 0, make2D, "V", 2);
                SetVolatileInput(make2D, "Te", 3, 0, new GH_Boolean(true));
                SetVolatileInput(make2D, "Ts", 4, 0, new GH_Boolean(false));

                ForceCompute(make2D);
                viewContext.Document.NewSolution(true);
                AppendMake2DRuntimeMessages(make2D, result, logPrefix);
                result.Diagnostics.Add(logPrefix + " 输入数据：" + DescribeParamDataCounts(make2D.Params.Input));
                result.Diagnostics.Add(logPrefix + " 输出数据：" + DescribeParamDataCounts(make2D.Params.Output));

                int visibleOutput = FindParamIndex(make2D.Params.Output, "V", 0);
                int visibleIndexOutput = FindParamIndex(make2D.Params.Output, "Vi", 1);
                int visibleTypeOutput = FindParamIndex(make2D.Params.Output, "Vt", 2);
                int hiddenOutput = FindParamIndex(make2D.Params.Output, "H", 3);
                int hiddenIndexOutput = FindParamIndex(make2D.Params.Output, "Hi", 4);
                int hiddenTypeOutput = FindParamIndex(make2D.Params.Output, "Ht", 5);

                visible = ReadCurveOutput(make2D, visibleOutput);
                visibleIndex = MapMake2DIndices(ReadIntegerOutput(make2D, visibleIndexOutput), source);
                visibleTypes = ReadTextOutput(make2D, visibleTypeOutput);
                hidden = ReadCurveOutput(make2D, hiddenOutput);
                hiddenIndex = MapMake2DIndices(ReadIntegerOutput(make2D, hiddenIndexOutput), source);
                hiddenTypes = ReadTextOutput(make2D, hiddenTypeOutput);

                result.Diagnostics.Add(logPrefix + " 使用原生 Make2D：输入 " + source.Count + "，可见线 " + visible.Count + "，隐藏线 " + hidden.Count + "。");
                return true;
            }
            catch (Exception ex)
            {
                string message = logPrefix + " 执行失败：" + ex.Message;
                result.Diagnostics.Add(message);
                AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, message);
                return false;
            }
        }

        private NativeMake2DViewContext CreateNativeMake2DViewContext(List<SourceGeometry> viewSource, SectionInfo section, SectionCache result, string logPrefix)
        {
            return CreateNativeMake2DViewContext(viewSource, section, Point3d.Origin, result, logPrefix);
        }

        private NativeMake2DViewContext CreateNativeMake2DViewContext(List<SourceGeometry> viewSource, SectionInfo section, Point3d localizationCenter, SectionCache result, string logPrefix)
        {
            GH_Document document = new GH_Document();
            try
            {
                GH_Component rectangleComponent = CreateRectangleComponent(out string rectangleError);
                if (rectangleComponent == null)
                {
                    string message = logPrefix + " 失败：未找到原生矩形组件。" + rectangleError;
                    result.Diagnostics.Add(message);
                    AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, message);
                    document.Dispose();
                    return null;
                }

                GH_Component parallelView = CreateMake2DParallelViewComponent(out string viewError);
                if (parallelView == null)
                {
                    string message = logPrefix + " 失败：未找到 Make2D Parallel View 组件。" + viewError;
                    result.Diagnostics.Add(message);
                    AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, message);
                    document.Dispose();
                    return null;
                }

                document.AddObject(rectangleComponent, false);
                document.AddObject(parallelView, false);
                result.Diagnostics.Add(logPrefix + " 矩形组件：" + rectangleComponent.GetType().FullName);
                result.Diagnostics.Add(logPrefix + " 矩形输入端口：" + DescribeParams(rectangleComponent.Params.Input));
                result.Diagnostics.Add(logPrefix + " 矩形输出端口：" + DescribeParams(rectangleComponent.Params.Output));
                result.Diagnostics.Add(logPrefix + " 视图组件：" + parallelView.GetType().FullName);
                result.Diagnostics.Add(logPrefix + " 视图输入端口：" + DescribeParams(parallelView.Params.Input));
                result.Diagnostics.Add(logPrefix + " 视图输出端口：" + DescribeParams(parallelView.Params.Output));

                Rectangle3d viewRectangle = CreateMake2DViewRectangle(viewSource, section, localizationCenter);
                result.Diagnostics.Add(logPrefix + " 视图范围对象数量：" + (viewSource != null ? viewSource.Count : 0));
                SetVolatileInput(rectangleComponent, "P", 0, 0, new GH_Plane(viewRectangle.Plane));
                SetVolatileInput(rectangleComponent, "X", 1, 0, new GH_Interval(viewRectangle.X));
                SetVolatileInput(rectangleComponent, "Y", 2, 0, new GH_Interval(viewRectangle.Y));
                SetVolatileInput(rectangleComponent, "R", 3, 0, new GH_Number(0.0));
                ConnectComponentOutput(rectangleComponent, 0, parallelView, "P", 0);

                ForceCompute(rectangleComponent);
                ForceCompute(parallelView);
                document.NewSolution(true);

                AppendMake2DRuntimeMessages(rectangleComponent, result, logPrefix + " rectangle");
                result.Diagnostics.Add(logPrefix + " 矩形输入数据：" + DescribeParamDataCounts(rectangleComponent.Params.Input));
                result.Diagnostics.Add(logPrefix + " 矩形输出数据：" + DescribeParamDataCounts(rectangleComponent.Params.Output));
                AppendMake2DRuntimeMessages(parallelView, result, logPrefix + " view");
                result.Diagnostics.Add(logPrefix + " 视图输入数据：" + DescribeParamDataCounts(parallelView.Params.Input));
                result.Diagnostics.Add(logPrefix + " 视图输出数据：" + DescribeParamDataCounts(parallelView.Params.Output));

                return new NativeMake2DViewContext(document, rectangleComponent, parallelView, viewRectangle);
            }
            catch (Exception ex)
            {
                string message = logPrefix + " 创建失败：" + ex.Message;
                result.Diagnostics.Add(message);
                AddRuntimeMessageSafe(GH_RuntimeMessageLevel.Warning, message);
                document.Dispose();
                return null;
            }
        }

        private static GH_Component CreateMake2DComponent(out string error)
        {
            error = string.Empty;
            Guid make2DGuid = new Guid("96e40f6b-ba46-4102-bf15-ebf90471f4a0");
            return CreateComponent(make2DGuid, "CurveComponents.Make2DComponent", out error);
        }

        private static GH_Component CreateMake2DParallelViewComponent(out string error)
        {
            Guid parallelViewGuid = new Guid("3fc08088-d75d-436c-83cc-7a654f156cb7");
            return CreateComponent(parallelViewGuid, "CurveComponents.Make2DParallelViewComponent", out error);
        }

        private static GH_Component CreateRectangleComponent(out string error)
        {
            Guid rectangleGuid = new Guid("d93100b6-d50b-40b2-831a-814659dc38e3");
            return CreateComponent(rectangleGuid, "CurveComponents.Component_Rectangle", out error);
        }

        private static GH_Component CreateComponent(Guid componentGuid, string typeName, out string error)
        {
            error = string.Empty;
            try
            {
                PropertyInfo componentServerProperty = typeof(Instances).GetProperty("ComponentServer", BindingFlags.Public | BindingFlags.Static);
                object server = componentServerProperty?.GetValue(null, null);
                if (server != null)
                {
                    foreach (MethodInfo method in server.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "EmitObject"))
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Guid))
                        {
                            object obj = method.Invoke(server, new object[] { componentGuid });
                            if (obj is GH_Component component)
                                return component;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = " GUID 创建失败：" + ex.Message + "。";
            }

            try
            {
                Type type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(typeName, false))
                    .FirstOrDefault(item => item != null);
                if (type != null && Activator.CreateInstance(type) is GH_Component component)
                    return component;
            }
            catch (Exception ex)
            {
                error += " 类型创建失败：" + ex.Message + "。";
            }

            return null;
        }

        private static Rectangle3d CreateMake2DViewRectangle(List<SourceGeometry> source, SectionInfo section)
        {
            return CreateMake2DViewRectangle(source, section, Point3d.Origin);
        }

        private static Rectangle3d CreateMake2DViewRectangle(List<SourceGeometry> source, SectionInfo section, Point3d localizationCenter)
        {
            BoundingBox box = GetSourceBoundingBox(source);
            double width = 1000.0;
            double height = 1000.0;

            Vector3d lineDirection = section.Line.Direction;
            lineDirection.Z = 0.0;
            if (!lineDirection.Unitize())
                lineDirection = Vector3d.XAxis;

            Vector3d xAxis;
            Vector3d yAxis;
            if (Math.Abs(lineDirection.X) >= Math.Abs(lineDirection.Y))
            {
                xAxis = -lineDirection;
                yAxis = Vector3d.ZAxis;
                if (lineDirection.X < 0.0)
                {
                    xAxis = -xAxis;
                    yAxis = -yAxis;
                }
            }
            else
            {
                xAxis = -Vector3d.ZAxis;
                yAxis = -lineDirection;
                if (lineDirection.Y > 0.0)
                {
                    xAxis = -xAxis;
                    yAxis = -yAxis;
                }
            }

            Vector3d viewDirection = section.ViewDirection;
            if (!viewDirection.Unitize())
                viewDirection = Vector3d.CrossProduct(lineDirection, Vector3d.ZAxis);
            if (!viewDirection.Unitize())
                viewDirection = Vector3d.YAxis;

            Plane originPlane = new Plane(Point3d.Origin, xAxis, yAxis);
            Point3d sectionMidpoint = section.Line.PointAt(0.5);
            double distanceToSection = Math.Abs(originPlane.DistanceTo(sectionMidpoint));
            Point3d origin = Point3d.Origin - viewDirection * (distanceToSection + 5.0);
            Plane plane = new Plane(origin, xAxis, yAxis);

            if (box.IsValid)
            {
                double sectionFromX = (section.Line.From - plane.Origin) * plane.XAxis;
                double sectionToX = (section.Line.To - plane.Origin) * plane.XAxis;
                double minX = Math.Min(sectionFromX, sectionToX);
                double maxX = Math.Max(sectionFromX, sectionToX);
                double minY = 0.0;
                double maxY = 0.0;
                foreach (Point3d corner in box.GetCorners())
                {
                    double x = (corner - plane.Origin) * plane.XAxis;
                    double y = (corner - plane.Origin) * plane.YAxis;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }

                double padding = Math.Max(box.Diagonal.Length * 0.05, 10.0);
                minX -= padding;
                maxX += padding;
                minY -= padding;
                maxY += padding;
                return new Rectangle3d(plane, new Interval(minX, maxX), new Interval(minY, maxY));
            }

            return new Rectangle3d(plane, new Interval(-width, width), new Interval(-height, height));
        }

        private static void ConnectInput(GH_Document document, GH_Component component, string nickName, int fallbackIndex, IGH_Param source)
        {
            int inputIndex = FindParamIndex(component.Params.Input, nickName, fallbackIndex);
            if (inputIndex < 0 || inputIndex >= component.Params.Input.Count)
                return;

            document.AddObject(source, false);
            component.Params.Input[inputIndex].AddSource(source);
        }

        private static void SetVolatileInput(GH_Component component, string nickName, int fallbackIndex, int itemIndex, IGH_Goo data)
        {
            int inputIndex = FindParamIndex(component.Params.Input, nickName, fallbackIndex);
            if (inputIndex < 0 || inputIndex >= component.Params.Input.Count || data == null)
                return;

            component.Params.Input[inputIndex].AddVolatileData(new GH_Path(0), itemIndex, data);
        }

        private static void ForceCompute(GH_Component component)
        {
            if (component == null)
                return;

            component.ClearRuntimeMessages();
            component.CollectData();
            component.ComputeData();
        }

        private static void ConnectComponentOutput(GH_Component sourceComponent, int sourceOutputIndex, GH_Component targetComponent, string targetNickName, int targetFallbackIndex)
        {
            int targetIndex = FindParamIndex(targetComponent.Params.Input, targetNickName, targetFallbackIndex);
            if (sourceOutputIndex < 0 || sourceOutputIndex >= sourceComponent.Params.Output.Count)
                return;
            if (targetIndex < 0 || targetIndex >= targetComponent.Params.Input.Count)
                return;

            targetComponent.Params.Input[targetIndex].AddSource(sourceComponent.Params.Output[sourceOutputIndex]);
        }

        private static int FindParamIndex(IList<IGH_Param> parameters, string nickName, int fallbackIndex)
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                IGH_Param parameter = parameters[i];
                if (string.Equals(parameter.NickName, nickName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parameter.Name, nickName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return fallbackIndex >= 0 && fallbackIndex < parameters.Count ? fallbackIndex : -1;
        }

        private static void TrySetMake2DAutomatic(GH_Component make2D, SectionCache result, string logPrefix)
        {
            Type type = make2D.GetType();
            List<string> touched = new List<string>();

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!property.CanWrite)
                    continue;

                string name = property.Name.ToLowerInvariant();
                bool looksLikeMode = name.Contains("auto") || name.Contains("manual") || name.Contains("mode");
                if (!looksLikeMode)
                    continue;

                try
                {
                    if (property.PropertyType == typeof(bool))
                    {
                        bool value = !name.Contains("manual");
                        property.SetValue(make2D, value, null);
                        touched.Add(property.Name + "=" + value);
                    }
                    else if (property.PropertyType.IsEnum)
                    {
                        object enumValue = Enum.GetValues(property.PropertyType)
                            .Cast<object>()
                            .FirstOrDefault(item => item.ToString().IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (enumValue != null)
                        {
                            property.SetValue(make2D, enumValue, null);
                            touched.Add(property.Name + "=" + enumValue);
                        }
                    }
                }
                catch
                {
                }
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string name = field.Name.ToLowerInvariant();
                bool looksLikeMode = name.Contains("auto") || name.Contains("manual") || name.Contains("mode");
                if (!looksLikeMode)
                    continue;

                try
                {
                    if (field.FieldType == typeof(bool))
                    {
                        bool value = !name.Contains("manual");
                        field.SetValue(make2D, value);
                        touched.Add(field.Name + "=" + value);
                    }
                    else if (field.FieldType.IsEnum)
                    {
                        object enumValue = Enum.GetValues(field.FieldType)
                            .Cast<object>()
                            .FirstOrDefault(item => item.ToString().IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (enumValue != null)
                        {
                            field.SetValue(make2D, enumValue);
                            touched.Add(field.Name + "=" + enumValue);
                        }
                    }
                }
                catch
                {
                }
            }

            result.Diagnostics.Add(logPrefix + " 自动模式设置：" + (touched.Count == 0 ? "未找到可设置的字段或属性。" : string.Join(", ", touched)));
        }

        private static string DescribeParams(IList<IGH_Param> parameters)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < parameters.Count; i++)
            {
                IGH_Param parameter = parameters[i];
                parts.Add(i + ":" + parameter.Name + "/" + parameter.NickName + "/" + parameter.GetType().Name);
            }

            return string.Join(" | ", parts);
        }

        private static string DescribeParamDataCounts(IList<IGH_Param> parameters)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < parameters.Count; i++)
            {
                IGH_Param parameter = parameters[i];
                parts.Add(i + ":" + parameter.NickName + " S" + parameter.SourceCount + " D" + parameter.VolatileDataCount);
            }

            return string.Join(" | ", parts);
        }

        private static void AppendMake2DRuntimeMessages(GH_Component make2D, SectionCache result, string logPrefix)
        {
            foreach (string message in make2D.RuntimeMessages(GH_RuntimeMessageLevel.Error))
                result.Diagnostics.Add(logPrefix + " Make2D 错误：" + SanitizeRuntimeMessage(message));
            foreach (string message in make2D.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
                result.Diagnostics.Add(logPrefix + " Make2D 警告：" + SanitizeRuntimeMessage(message));
            foreach (string message in make2D.RuntimeMessages(GH_RuntimeMessageLevel.Remark))
                result.Diagnostics.Add(logPrefix + " Make2D 提示：" + SanitizeRuntimeMessage(message));
        }

        private static string SanitizeRuntimeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "未提供详细信息。";
            return LooksLikeMojibake(message) ? "原生组件返回了无法正确解码的文本。" : message;
        }

        private static List<Curve> ReadCurveOutput(GH_Component component, int outputIndex)
        {
            List<Curve> result = new List<Curve>();
            if (outputIndex < 0 || outputIndex >= component.Params.Output.Count)
                return result;

            foreach (IGH_Goo goo in component.Params.Output[outputIndex].VolatileData.AllData(true))
            {
                if (goo == null)
                    continue;

                if (goo.CastTo(out Curve curve) && curve != null)
                    result.Add(curve.DuplicateCurve());
            }

            return result;
        }

        private static IGH_GeometricGoo CreateGeometryGoo(GeometryBase geometry)
        {
            if (geometry is Brep brep)
                return new GH_Brep(brep);
            if (geometry is Curve curve)
                return new GH_Curve(curve);
            if (geometry is Surface surface)
                return new GH_Surface(surface);
            if (geometry is Mesh mesh)
                return new GH_Mesh(mesh);
            if (geometry is Rhino.Geometry.Point point)
                return new GH_Point(point.Location);

            return null;
        }

        private static List<int> ReadIntegerOutput(GH_Component component, int outputIndex)
        {
            List<int> result = new List<int>();
            if (outputIndex < 0 || outputIndex >= component.Params.Output.Count)
                return result;

            foreach (IGH_Goo goo in component.Params.Output[outputIndex].VolatileData.AllData(true))
            {
                if (goo == null)
                    continue;

                if (goo.CastTo(out int value))
                    result.Add(value);
            }

            return result;
        }

        private static List<string> ReadTextOutput(GH_Component component, int outputIndex)
        {
            List<string> result = new List<string>();
            if (outputIndex < 0 || outputIndex >= component.Params.Output.Count)
                return result;

            foreach (IGH_Goo goo in component.Params.Output[outputIndex].VolatileData.AllData(true))
            {
                if (goo == null)
                    continue;

                if (goo.CastTo(out string value))
                    result.Add(NormalizeLineType(value));
                else
                    result.Add(NormalizeLineType(goo.ToString()));
            }

            return result;
        }

        private static List<int> MapMake2DIndices(List<int> indices, List<SourceGeometry> source)
        {
            if (indices.Count == 0)
                return indices;

            List<int> result = new List<int>();
            foreach (int index in indices)
            {
                if (index >= 0 && index < source.Count)
                    result.Add(source[index].Index);
                else
                    result.Add(index);
            }

            return result;
        }

        private HiddenLineDrawing ComputeHiddenLine(List<SourceGeometry> source, SectionInfo section, double tolerance, SectionCache result, string logPrefix)
        {
            HiddenLineDrawingParameters parameters = new HiddenLineDrawingParameters
            {
                AbsoluteTolerance = tolerance,
                Flatten = true,
                IncludeHiddenCurves = true,
                IncludeTangentEdges = true,
                IncludeTangentSeams = false
            };

            ViewportInfo viewport = new ViewportInfo();
            BoundingBox box = BoundingBox.Empty;
            foreach (SourceGeometry item in source)
            {
                BoundingBox itemBox = item.Geometry.GetBoundingBox(true);
                if (itemBox.IsValid)
                    box.Union(itemBox);
            }

            if (!box.IsValid)
            {
                result.Diagnostics.Add(logPrefix + " skipped: object bounding box invalid.");
                return null;
            }

            Point3d target = box.Center;
            double radius = Math.Max(1.0, box.Diagonal.Length);
            viewport.SetCameraLocation(target - section.ViewDirection * radius * 2.0);
            viewport.SetCameraDirection(section.ViewDirection);
            viewport.SetCameraUp(Vector3d.ZAxis);
            viewport.ChangeToParallelProjection(true);
            viewport.SetFrustum(-radius, radius, -radius, radius, 0.01, radius * 5.0);
            parameters.SetViewport(viewport);

            foreach (SourceGeometry item in source)
                parameters.AddGeometry(item.Geometry, item.Index);

            HiddenLineDrawing drawing = HiddenLineDrawing.Compute(parameters, true);
            if (drawing == null)
            {
                result.Diagnostics.Add(logPrefix + " returned empty.");
                return null;
            }

            return drawing;
        }

        private static Transform CreateHiddenLineToLayoutTransform(Transform worldToHidden, SectionInfo section, Plane targetPlane, Transform scaleToTarget)
        {
            Point3d origin = section.Line.From;
            Point3d along = section.Line.To;
            Point3d up = section.Line.From + Vector3d.ZAxis;
            origin.Transform(worldToHidden);
            along.Transform(worldToHidden);
            up.Transform(worldToHidden);

            Vector3d hiddenX = along - origin;
            hiddenX.Z = 0.0;
            if (!hiddenX.Unitize())
                hiddenX = Vector3d.XAxis;

            Vector3d hiddenY = up - origin;
            hiddenY.Z = 0.0;
            if (!hiddenY.Unitize())
                hiddenY = Vector3d.YAxis;

            if (Math.Abs(hiddenX * hiddenY) > 0.999)
                hiddenY = new Vector3d(-hiddenX.Y, hiddenX.X, 0.0);

            Plane hiddenPlane = new Plane(origin, hiddenX, hiddenY);
            return scaleToTarget * Transform.PlaneToPlane(hiddenPlane, targetPlane) * worldToHidden;
        }

        private static int GetSegmentIndex(HiddenLineDrawingSegment segment, SectionCache result, SectionInfo section)
        {
            try
            {
                object tag = segment.ParentCurve?.SourceObject?.Tag;
                if (tag is int index)
                    return index;
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add("Section " + section.Name + ": failed to read HiddenLine source index; using -1. " + ex.Message);
            }

            return -1;
        }

        private static bool IsVisible(HiddenLineDrawingSegment segment)
        {
            return segment.SegmentVisibility == HiddenLineDrawingSegment.Visibility.Visible;
        }

        private static void AddSectionSymbol(SectionCache result, GH_Path path, SectionInfo section, string name, double textHeight)
        {
            List<Line> markers = GetSectionSymbolLines(section).ToList();
            foreach (Line marker in markers)
                result.Symbols.Append(new GH_Curve(new LineCurve(marker)), path);

            double textDistance = Math.Max(textHeight * 1.2, section.Line.Length * 0.08);
            Vector3d view = section.ViewDirection;
            if (!view.Unitize())
                view = Vector3d.YAxis;

            Plane textPlaneA = Plane.WorldXY;
            textPlaneA.Origin = section.Line.From + view * textDistance;
            Plane textPlaneB = Plane.WorldXY;
            textPlaneB.Origin = section.Line.To + view * textDistance;
            result.Symbols.Append(new GH_ObjectWrapper(new TextEntity { Plane = textPlaneA, PlainText = name, TextHeight = textHeight, Justification = TextJustification.MiddleCenter }), path);
            result.Symbols.Append(new GH_ObjectWrapper(new TextEntity { Plane = textPlaneB, PlainText = name, TextHeight = textHeight, Justification = TextJustification.MiddleCenter }), path);
        }

        private static IEnumerable<Line> GetSectionSymbolLines(SectionInfo section)
        {
            Vector3d axis = section.Line.Direction;
            axis.Z = 0.0;
            if (!axis.Unitize())
                yield break;

            Vector3d view = section.ViewDirection;
            view.Z = 0.0;
            if (!view.Unitize())
                yield break;

            double length = section.Line.Length;
            double alongLength = Math.Max(length * 0.18, 80.0);
            alongLength = Math.Min(alongLength, length * 0.45);
            double viewLength = Math.Max(length * 0.12, 80.0);

            Point3d start = section.Line.From;
            Point3d end = section.Line.To;
            yield return new Line(start, start + axis * alongLength);
            yield return new Line(end - axis * alongLength, end);
            yield return new Line(start, start + view * viewLength);
            yield return new Line(end, end + view * viewLength);
        }

        private static void AddTitle(SectionCache result, GH_Path path, string name, Plane targetPlane, double textHeight, double textOffset)
        {
            Plane plane = Plane.WorldXY;
            Vector3d down = -targetPlane.YAxis;
            down.Z = 0.0;
            if (!down.Unitize())
                down = -Vector3d.YAxis;
            plane.Origin = targetPlane.Origin + down * Math.Abs(textOffset);
            string text = name + " section";
            result.Titles.Append(new GH_ObjectWrapper(new TextEntity { Plane = plane, PlainText = text, TextHeight = textHeight, Justification = TextJustification.MiddleCenter }), path);
        }

        private void SetOutputs(IGH_DataAccess DA, SectionCache cache)
        {
            List<Transform> layoutTransforms = GetLayoutTransforms(cache, _currentPreviewGap);
            DataTree<Curve> visible = new DataTree<Curve>();
            DataTree<string> visibleTypes = new DataTree<string>();
            DataTree<Curve> hidden = new DataTree<Curve>();
            DataTree<string> hiddenTypes = new DataTree<string>();
            Dictionary<string, int> visibleCounters = new Dictionary<string, int>();
            Dictionary<string, int> hiddenCounters = new Dictionary<string, int>();

            int visibleFiltered = 0;
            foreach (TypedCurveRecord record in cache.RawVisibleCurves)
            {
                if (!IsVisibleTypeEnabled(record.Type))
                {
                    visibleFiltered++;
                    continue;
                }

                GH_Path path = ElementPath(record.SectionIndex, record.ObjectIndex, NextCounter(visibleCounters, record.SectionIndex, record.ObjectIndex));
                Curve outputCurve = record.Curve?.DuplicateCurve();
                if (outputCurve != null && record.SectionIndex >= 0 && record.SectionIndex < layoutTransforms.Count)
                    outputCurve.Transform(layoutTransforms[record.SectionIndex]);
                visible.Add(outputCurve, path);
                visibleTypes.Add(record.Type, path);
            }

            int hiddenFiltered = 0;
            foreach (TypedCurveRecord record in cache.RawHiddenCurves)
            {
                if (!IsHiddenTypeEnabled(record.Type))
                {
                    hiddenFiltered++;
                    continue;
                }

                GH_Path path = ElementPath(record.SectionIndex, record.ObjectIndex, NextCounter(hiddenCounters, record.SectionIndex, record.ObjectIndex));
                Curve outputCurve = record.Curve?.DuplicateCurve();
                if (outputCurve != null && record.SectionIndex >= 0 && record.SectionIndex < layoutTransforms.Count)
                    outputCurve.Transform(layoutTransforms[record.SectionIndex]);
                hidden.Add(outputCurve, path);
                hiddenTypes.Add(record.Type, path);
            }

            List<string> diagnostics = cache.Diagnostics
                .Where(message => !LooksLikeMojibake(message))
                .ToList();
            diagnostics.Insert(0, "SectionMake2D V2 计算完成。");
            diagnostics.Add("剖面数量: " + cache.Names.Count);
            diagnostics.Add("输出对象数量: " + cache.OutputObjectCount);
            diagnostics.Add("参考对象序号: " + cache.ReferenceObjectIndex);
            diagnostics.Add("截面面/参考截面几何数量: " + cache.SectionSurfaces.DataCount);
            diagnostics.Add("截面线数量: " + cache.SectionGeometry.DataCount);
            diagnostics.Add("可见线缓存数量: " + cache.RawVisibleCurves.Count);
            diagnostics.Add("隐藏线缓存数量: " + cache.RawHiddenCurves.Count);
            diagnostics.Add("当前可见线类型: " + string.Join(", ", _visibleLineTypes.OrderBy(item => item)));
            diagnostics.Add("当前隐藏线类型: " + string.Join(", ", _hiddenLineTypes.OrderBy(item => item)));
            diagnostics.Add("过滤掉可见线: " + visibleFiltered + "；过滤掉隐藏线: " + hiddenFiltered);

            int sectionCount = cache.Names.Count;
            int objectCount = cache.OutputObjectCount;
            GH_Structure<IGH_GeometricGoo> sectionSurfaces = CreateGeometryOutputTree(cache.SectionSurfaces, sectionCount, objectCount, _keepEmptyBranches, layoutTransforms);
            GH_Structure<IGH_GeometricGoo> sectionGeometry = CreateGeometryOutputTree(cache.SectionGeometry, sectionCount, objectCount, _keepEmptyBranches, layoutTransforms);
            DataTree<Curve> visibleOutput = CreateDataOutputTree(visible, sectionCount, objectCount, _keepEmptyBranches);
            DataTree<string> visibleTypeOutput = CreateDataOutputTree(visibleTypes, sectionCount, objectCount, _keepEmptyBranches);
            DataTree<Curve> hiddenOutput = CreateDataOutputTree(hidden, sectionCount, objectCount, _keepEmptyBranches);
            DataTree<string> hiddenTypeOutput = CreateDataOutputTree(hiddenTypes, sectionCount, objectCount, _keepEmptyBranches);

            DA.SetDataTree(0, sectionSurfaces);
            DA.SetDataTree(1, sectionGeometry);
            DA.SetDataTree(2, visibleOutput);
            DA.SetDataTree(3, visibleTypeOutput);
            DA.SetDataTree(4, hiddenOutput);
            DA.SetDataTree(5, hiddenTypeOutput);
            DA.SetDataList(6, cache.Names);
            DA.SetDataList(7, diagnostics);
        }

        private static bool LooksLikeMojibake(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            string[] markers =
            {
                "閸", "鏉", "缁", "鐟", "娴", "鈧", "妫", "鍓", "鎴", "闂", "瀵", "绻"
            };
            return markers.Any(message.Contains);
        }

        private void AddRuntimeMessageSafe(GH_RuntimeMessageLevel level, string message)
        {
            string safeMessage = LooksLikeMojibake(message) ? "Make2D 计算出现警告，请查看 V2 诊断信息。" : message;
            AddRuntimeMessage(level, safeMessage);
        }

        private static List<Transform> GetLayoutTransforms(SectionCache cache, double previewGap)
        {
            List<Transform> transforms = new List<Transform>();
            for (int i = 0; i < cache.Layouts.Count; i++)
            {
                SectionLayoutInfo layout = cache.Layouts[i];
                double gap = ResolvePreviewGap(previewGap, layout.SourceBox, RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001);
                Vector3d move = ComputePreviewMove(layout.SourceBox, layout.DrawingBox, layout.ReferenceSourceBox, layout.ReferenceDrawingBox, layout.ViewDirection, gap, i);
                transforms.Add(Transform.Translation(move));
            }
            return transforms;
        }

        private static GH_Structure<IGH_GeometricGoo> CreateGeometryOutputTree(GH_Structure<IGH_GeometricGoo> source, int sectionCount, int objectCount, bool keepEmptyBranches, List<Transform> transforms)
        {
            GH_Structure<IGH_GeometricGoo> result = new GH_Structure<IGH_GeometricGoo>();
            foreach (GH_Path path in source.Paths)
            {
                System.Collections.IList branch = source.get_Branch(path);
                if (branch.Count == 0)
                {
                    if (keepEmptyBranches)
                        result.EnsurePath(path);
                    continue;
                }

                foreach (object item in branch)
                {
                    if (item is IGH_GeometricGoo goo)
                    {
                        int sectionIndex = path.Indices.Length > 0 ? path.Indices[0] : -1;
                        IGH_GeometricGoo output = goo.DuplicateGeometry();
                        if (sectionIndex >= 0 && sectionIndex < transforms.Count)
                            output = output.Transform(transforms[sectionIndex]);
                        result.Append(output, path);
                    }
                }
            }

            if (keepEmptyBranches)
                EnsureGeometryObjectBranches(result, sectionCount, objectCount);

            return result;
        }

        private static DataTree<T> CreateDataOutputTree<T>(DataTree<T> source, int sectionCount, int objectCount, bool keepEmptyBranches)
        {
            DataTree<T> result = new DataTree<T>();
            for (int i = 0; i < source.Paths.Count; i++)
            {
                GH_Path path = source.Paths[i];
                IList<T> branch = source.Branches[i];
                if (branch.Count == 0)
                {
                    if (keepEmptyBranches)
                        result.EnsurePath(path);
                    continue;
                }

                foreach (T item in branch)
                    result.Add(item, path);
            }

            if (keepEmptyBranches)
                EnsureDataObjectBranches(result, sectionCount, objectCount);

            return result;
        }

        private static void EnsureGeometryObjectBranches(GH_Structure<IGH_GeometricGoo> tree, int sectionCount, int objectCount)
        {
            for (int sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
            {
                GH_Path sectionPath = new GH_Path(sectionIndex);
                for (int objectIndex = 0; objectIndex < objectCount; objectIndex++)
                    tree.EnsurePath(ObjectPath(sectionPath, objectIndex));
            }
        }

        private static void EnsureDataObjectBranches<T>(DataTree<T> tree, int sectionCount, int objectCount)
        {
            for (int sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
            {
                GH_Path sectionPath = new GH_Path(sectionIndex);
                for (int objectIndex = 0; objectIndex < objectCount; objectIndex++)
                    tree.EnsurePath(ObjectPath(sectionPath, objectIndex));
            }
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_SectionMake2DV2(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            bool[] filterChanged = new[] { false };
            AppendLineTypeFilterMenu(menu, filterChanged);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "保留空分支", (sender, args) => SetKeepEmptyBranches(true), true, _keepEmptyBranches);
            Menu_AppendItem(menu, "剔除空分支", (sender, args) => SetKeepEmptyBranches(false), true, !_keepEmptyBranches);
            menu.Closed += (sender, args) =>
            {
                if (filterChanged[0])
                    ExpireSolution(true);
            };
            if (DateTime.Now.Ticks >= 0)
                return;
            Menu_AppendItem(menu, "保留空分支", (sender, args) => SetKeepEmptyBranches(true), true, _keepEmptyBranches);
            Menu_AppendItem(menu, "剔除空分支", (sender, args) => SetKeepEmptyBranches(false), true, !_keepEmptyBranches);
        }

        private void AppendLineTypeFilterMenu(ToolStripDropDown menu, bool[] filterChanged)
        {
            ToolStripMenuItem filterMenu = new ToolStripMenuItem("线型过滤");
            filterMenu.DropDown.AutoClose = true;
            filterMenu.DropDownItems.Add(CreateLineTypeHost("可见线类型", VisibleLineTypes, _visibleLineTypes, value => filterChanged[0] = value));
            filterMenu.DropDownItems.Add(CreateLineTypeHost("隐藏线类型", HiddenLineTypes, _hiddenLineTypes, value => filterChanged[0] = value));
            menu.Items.Add(filterMenu);
        }

        private ToolStripControlHost CreateLineTypeHost(string title, string[] allTypes, HashSet<string> selectedTypes, Action<bool> markChanged)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(8, 6, 8, 6),
                BackColor = SystemColors.Control
            };

            Label label = new Label
            {
                Text = title,
                AutoSize = true,
                Font = new System.Drawing.Font(SystemFonts.MenuFont, FontStyle.Bold),
                Margin = new Padding(2, 2, 2, 4)
            };
            panel.Controls.Add(label);

            foreach (string type in allTypes)
            {
                CheckBox checkBox = new CheckBox
                {
                    Text = type,
                    Checked = selectedTypes.Contains(type),
                    AutoSize = true,
                    Margin = new Padding(2, 1, 2, 1)
                };
                checkBox.CheckedChanged += (sender, args) =>
                {
                    if (checkBox.Checked)
                        selectedTypes.Add(type);
                    else
                        selectedTypes.Remove(type);
                    markChanged(true);
                };
                panel.Controls.Add(checkBox);
            }

            return new ToolStripControlHost(panel)
            {
                AutoSize = true,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
        }

        private void SetKeepEmptyBranches(bool keep)
        {
            if (_keepEmptyBranches == keep)
                return;

            _keepEmptyBranches = keep;
            ExpireSolution(true);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean("KeepEmptyBranches", _keepEmptyBranches);
            writer.SetString("VisibleLineTypes", string.Join("\n", _visibleLineTypes));
            writer.SetString("HiddenLineTypes", string.Join("\n", _hiddenLineTypes));
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            if (reader.ItemExists("KeepEmptyBranches"))
                _keepEmptyBranches = reader.GetBoolean("KeepEmptyBranches");
            if (reader.ItemExists("VisibleLineTypes"))
                _visibleLineTypes = ReadLineTypeSet(reader.GetString("VisibleLineTypes"), VisibleLineTypes);
            if (reader.ItemExists("HiddenLineTypes"))
                _hiddenLineTypes = ReadLineTypeSet(reader.GetString("HiddenLineTypes"), HiddenLineTypes);

            return base.Read(reader);
        }

        private static HashSet<string> ReadLineTypeSet(string value, string[] fallback)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(value))
            {
                foreach (string item in value.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    result.Add(item.Trim());
            }

            if (result.Count == 0)
                result.UnionWith(fallback);

            return result;
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox box = base.ClippingBox;
                foreach (SectionInfo section in _previewSections)
                {
                    BoundingBox previewBox = GetPreviewSectionBox(section);
                    if (previewBox.IsValid)
                        box.Union(previewBox);
                }

                return box;
            }
        }

        public override bool IsPreviewCapable
        {
            get { return true; }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (Hidden || Locked)
                return;

            Color color = Attributes?.Selected == true ? args.WireColour_Selected : args.WireColour;
            foreach (SectionInfo section in _previewSections)
                DrawPreviewSection(args, section, color);
        }

        private static BoundingBox GetPreviewSectionBox(SectionInfo section)
        {
            BoundingBox box = new BoundingBox(section.Line.From, section.Line.To);
            foreach (Line marker in GetPreviewMarkers(section))
            {
                box.Union(marker.From);
                box.Union(marker.To);
            }

            return box;
        }

        private static void DrawPreviewSection(IGH_PreviewArgs args, SectionInfo section, Color color)
        {
            args.Display.DrawLine(section.Line, color, 2);
            foreach (Line marker in GetPreviewMarkers(section))
                args.Display.DrawLine(marker, color, 2);
        }

        private static IEnumerable<Line> GetPreviewMarkers(SectionInfo section)
        {
            foreach (Line line in GetSectionSymbolLines(section))
                yield return line;
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_CurvesGroupByPlane"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("A5D78024-4F73-45E8-AD86-64FC3014E628"); }
        }

        private sealed class NativeMake2DViewContext : IDisposable
        {
            public NativeMake2DViewContext(GH_Document document, GH_Component rectangleComponent, GH_Component parallelView, Rectangle3d viewRectangle)
            {
                Document = document;
                RectangleComponent = rectangleComponent;
                ParallelView = parallelView;
                ViewRectangle = viewRectangle;
            }

            public GH_Document Document { get; }
            public GH_Component RectangleComponent { get; }
            public GH_Component ParallelView { get; }
            public Rectangle3d ViewRectangle { get; }

            public void Dispose()
            {
                Document?.Dispose();
            }
        }

        private class SectionCache
        {
            public GH_Structure<IGH_GeometricGoo> SectionGeometry { get; } = new GH_Structure<IGH_GeometricGoo>();
            public DataTree<int> SectionIndex { get; } = new DataTree<int>();
            public DataTree<Curve> VisibleCurves { get; } = new DataTree<Curve>();
            public DataTree<int> VisibleIndex { get; } = new DataTree<int>();
            public DataTree<Curve> HiddenCurves { get; } = new DataTree<Curve>();
            public DataTree<int> HiddenIndex { get; } = new DataTree<int>();
            public GH_Structure<IGH_GeometricGoo> SectionSurfaces { get; } = new GH_Structure<IGH_GeometricGoo>();
            public GH_Structure<IGH_Goo> Symbols { get; } = new GH_Structure<IGH_Goo>();
            public GH_Structure<IGH_Goo> Titles { get; } = new GH_Structure<IGH_Goo>();
            public List<string> Names { get; } = new List<string>();
            public List<string> Diagnostics { get; } = new List<string>();
            public List<TypedCurveRecord> RawVisibleCurves { get; } = new List<TypedCurveRecord>();
            public List<TypedCurveRecord> RawHiddenCurves { get; } = new List<TypedCurveRecord>();
            public List<SectionLayoutInfo> Layouts { get; } = new List<SectionLayoutInfo>();
            public int ReferenceObjectIndex { get; set; } = -1;
            public int OutputObjectCount { get; set; }
        }

        private class SectionLayoutInfo
        {
            public SectionLayoutInfo(BoundingBox sourceBox, BoundingBox drawingBox, BoundingBox referenceSourceBox, BoundingBox referenceDrawingBox, Vector3d viewDirection)
            {
                SourceBox = sourceBox;
                DrawingBox = drawingBox;
                ReferenceSourceBox = referenceSourceBox;
                ReferenceDrawingBox = referenceDrawingBox;
                ViewDirection = viewDirection;
            }

            public BoundingBox SourceBox { get; }
            public BoundingBox DrawingBox { get; }
            public BoundingBox ReferenceSourceBox { get; }
            public BoundingBox ReferenceDrawingBox { get; }
            public Vector3d ViewDirection { get; }
        }

        private class SectionBuildData
        {
            public List<BrepRecord> SectionSurfaces { get; } = new List<BrepRecord>();
            public List<CurveRecord> SectionCurves { get; } = new List<CurveRecord>();
            public List<TypedCurveRecord> VisibleCurves { get; } = new List<TypedCurveRecord>();
            public List<TypedCurveRecord> HiddenCurves { get; } = new List<TypedCurveRecord>();

            public BoundingBox GetBoundingBox()
            {
                BoundingBox box = BoundingBox.Empty;
                foreach (BrepRecord record in SectionSurfaces)
                {
                    BoundingBox itemBox = record.Brep?.GetBoundingBox(true) ?? BoundingBox.Empty;
                    if (itemBox.IsValid)
                        box.Union(itemBox);
                }

                foreach (CurveRecord record in SectionCurves)
                {
                    BoundingBox itemBox = record.Curve?.GetBoundingBox(true) ?? BoundingBox.Empty;
                    if (itemBox.IsValid)
                        box.Union(itemBox);
                }

                foreach (TypedCurveRecord record in VisibleCurves)
                {
                    BoundingBox itemBox = record.Curve?.GetBoundingBox(true) ?? BoundingBox.Empty;
                    if (itemBox.IsValid)
                        box.Union(itemBox);
                }

                foreach (TypedCurveRecord record in HiddenCurves)
                {
                    BoundingBox itemBox = record.Curve?.GetBoundingBox(true) ?? BoundingBox.Empty;
                    if (itemBox.IsValid)
                        box.Union(itemBox);
                }

                return box;
            }
        }

        private class BrepRecord
        {
            public BrepRecord(Brep brep, int objectIndex)
            {
                Brep = brep;
                ObjectIndex = objectIndex;
            }

            public Brep Brep { get; }
            public int ObjectIndex { get; }
        }

        private class SectionRegion
        {
            public SectionRegion(Curve curve, Brep brep, double area)
            {
                Curve = curve;
                Brep = brep;
                Area = area;
            }

            public Curve Curve { get; }
            public Brep Brep { get; }
            public double Area { get; }
            public int ParentIndex { get; set; } = -1;
            public int Depth { get; set; }
        }

        private class CurveRecord
        {
            public CurveRecord(Curve curve, int objectIndex)
            {
                Curve = curve;
                ObjectIndex = objectIndex;
            }

            public Curve Curve { get; }
            public int ObjectIndex { get; }
        }

        private class TypedCurveRecord : CurveRecord
        {
            public TypedCurveRecord(Curve curve, int objectIndex, string type)
                : this(curve, -1, objectIndex, type)
            {
            }

            public TypedCurveRecord(Curve curve, int sectionIndex, int objectIndex, string type)
                : base(curve, objectIndex)
            {
                SectionIndex = sectionIndex;
                Type = NormalizeLineType(type);
            }

            public int SectionIndex { get; }
            public string Type { get; }
        }

        private class SourceGeometry
        {
            public SourceGeometry(GeometryBase geometry, int index, bool isReference = false)
            {
                Geometry = geometry;
                Index = index;
                IsReference = isReference;
            }

            public GeometryBase Geometry { get; }
            public int Index { get; }
            public bool IsReference { get; }
        }

        private class SectionPoint
        {
            public SectionPoint(Point3d point, int index)
            {
                Point = point;
                Index = index;
            }

            public Point3d Point { get; }
            public int Index { get; }
        }

        private class ProjectionContext
        {
            public ProjectionContext(List<SourceGeometry> source, SectionInfo section, Point3d localizationCenter)
            {
                Source = source;
                Section = section;
                LocalizationCenter = localizationCenter;
            }

            public List<SourceGeometry> Source { get; }
            public SectionInfo Section { get; }
            public Point3d LocalizationCenter { get; }
        }

        private class SectionInfo
        {
            public SectionInfo(Line line, Plane plane, Vector3d viewDirection, string name)
            {
                Line = line;
                Plane = plane;
                ViewDirection = viewDirection;
                Name = name;
            }

            public Line Line { get; }
            public Plane Plane { get; }
            public Vector3d ViewDirection { get; }
            public string Name { get; }
        }
    }

    internal class CButton_SectionMake2DV2 : GH_ComponentAttributes
    {
        private const float ButtonHeight = 20.0f;

        public CButton_SectionMake2DV2(SectionMake2DV2 component) : base(component)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + ButtonHeight);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects)
                return;

            RectangleF buttonRect = GetButtonRect();
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, GH_Palette.Black))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("Run", font, Brushes.White, buttonRect, format);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && GetButtonRect().Contains(e.CanvasLocation))
            {
                SectionMake2DV2 owner = (SectionMake2DV2)Owner;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }

        private RectangleF GetButtonRect()
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - ButtonHeight, Bounds.Width, ButtonHeight);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }
    }
}
