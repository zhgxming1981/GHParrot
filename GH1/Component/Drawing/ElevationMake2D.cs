using CommonFunction;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class ElevationMake2D : GH_Component
    {
        private const double DefaultViewDepth = 500000.0;
        private const double RectangleHalfSize = 2000.0;
        private const int DirectionFront = 0;
        private static readonly Vector3d DefaultFrontDirection = new Vector3d(0, -1, 0);
        private static readonly string[] DirectionLabels = { "前", "后", "左", "右", "上", "下" };
        private static readonly string[] AllLineTypes =
        {
            "Silhouette, Crease",
            "Silhouette, Tangent",
            "Silhouette, Boundary",
            "Tangent",
            "Crease",
            "Boundary",
            "其它"
        };
        private static readonly string[] DefaultVisibleLineTypes =
        {
            "Silhouette, Crease",
            "Silhouette, Tangent",
            "Silhouette, Boundary",
            "Tangent",
            "Crease",
            "Boundary",
            "其它"
        };
        private static readonly string[] DefaultHiddenLineTypes =
        {
            "Boundary",
            "Crease",
            "Tangent",
            "其它"
        };

        private bool _lastRun;
        private ElevationCache _cache = new ElevationCache();
        private string _inputSignature = string.Empty;
        private string _directionSignature = string.Empty;
        private string _depthSignature = string.Empty;
        private string _rotationSignature = string.Empty;
        private int _selectedDirectionIndex = DirectionFront;
        private Vector3d _baseFrontDirection = DefaultFrontDirection;
        private Vector3d _currentDirection = DefaultFrontDirection;
        private BoundingBox _cachedBBox = BoundingBox.Empty;
        private HashSet<string> _visibleLineTypes = new HashSet<string>(DefaultVisibleLineTypes, StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _hiddenLineTypes = new HashSet<string>(DefaultHiddenLineTypes, StringComparer.OrdinalIgnoreCase);

        public ElevationMake2D()
          : base("ElevationMake2D", "正视图Make2D",
              "沿指定视图方向对几何做 Make2D 投影，生成可见线、隐藏线、类型和 Parallel View",
              "Parrot", "Drawing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("对象", "G", "要参与 Make2D 投影的几何对象。支持 Brep、Surface、Curve、Rhino 块、GH 块或 Rhino 对象 Guid。", GH_ParamAccess.list);
            pManager.AddVectorParameter("视图方向", "D", "正视图的前方向。D 接入时，右键前后左右上下以该方向为“前”相对切换；D 不接时使用默认前方向。", GH_ParamAccess.item, DefaultFrontDirection);
            pManager.AddNumberParameter("视图深度", "VD", "沿视图方向参与投影的几何深度。空或小于等于 0 时不限制。", GH_ParamAccess.item, DefaultViewDepth);
            pManager.AddNumberParameter("旋转角度", "A", "在当前视图平面内旋转矩形平面的角度，单位为度。", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("执行", "Run", "从 False 切换到 True 时重新计算；投影相关输入变化会清空旧输出。", GH_ParamAccess.item, false);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("可见线", "Vis", "Make2D 可见线，按右键“可见线类型”过滤。路径为 {对象序号}。", GH_ParamAccess.tree);
            pManager.AddTextParameter("可见线类型", "Vt", "与可见线一一对应的 Make2D 类型文字。", GH_ParamAccess.tree);
            pManager.AddCurveParameter("隐藏线", "Hid", "Make2D 隐藏线，按右键“隐藏线类型”过滤。路径为 {对象序号}。", GH_ParamAccess.tree);
            pManager.AddTextParameter("隐藏线类型", "Ht", "与隐藏线一一对应的 Make2D 类型文字。", GH_ParamAccess.tree);
            pManager.AddTextParameter("诊断信息", "Log", "计算过程、视图方向、视图矩形、Make2D 端口识别、警告和错误信息。", GH_ParamAccess.list);
            pManager.AddGenericParameter("Make2D Parallel View", "PV", "Make2D Parallel View 组件生成的视图对象。", GH_ParamAccess.item);
#if DEBUG
            pManager.AddRectangleParameter("视图矩形", "VR", "Make2D 投影用视图矩形（调试用）。", GH_ParamAccess.item);
            pManager.AddPlaneParameter("视图平面", "VP", "Make2D 投影用视图矩形平面（调试用）。", GH_ParamAccess.item);
#endif
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
            {
                ElevationCache illegalCache = new ElevationCache();
                illegalCache.Diagnostics.Add("停止：授权检查失败。");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "授权检查失败。");
                SetOutputs(DA, illegalCache);
                return;
            }

            List<IGH_Goo> inputObjects = new List<IGH_Goo>();
            Vector3d inputFrontDirection = DefaultFrontDirection;
            double viewDepth = DefaultViewDepth;
            double rotationAngle = 0.0;
            bool run = false;

            DA.GetDataList(0, inputObjects);
            bool hasDirectionInput = Params.Input[1].SourceCount > 0;
            if (hasDirectionInput)
                DA.GetData(1, ref inputFrontDirection);
            DA.GetData(2, ref viewDepth);
            DA.GetData(3, ref rotationAngle);
            DA.GetData(4, ref run);

            if (!inputFrontDirection.Unitize())
            {
                ElevationCache invalidCache = new ElevationCache();
                invalidCache.Diagnostics.Add("停止：视图方向向量为零向量。");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "视图方向为零向量。");
                SetOutputs(DA, invalidCache);
                return;
            }

            _baseFrontDirection = inputFrontDirection;
            Vector3d[] relativeDirections = CreateRelativeDirections(inputFrontDirection);
            _selectedDirectionIndex = ClampDirectionIndex(_selectedDirectionIndex);
            Vector3d viewDirection = relativeDirections[_selectedDirectionIndex];
            viewDirection.Unitize();
            _currentDirection = viewDirection;
            _cachedBBox = ComputeCombinedBBox(inputObjects);

            string newInputSignature = CreateInputSignature(inputObjects);
            string newDirectionSignature = CreateVectorSignature(inputFrontDirection) + "|" + _selectedDirectionIndex.ToString(CultureInfo.InvariantCulture);
            string newDepthSignature = viewDepth.ToString("0.###", CultureInfo.InvariantCulture);
            string newRotationSignature = rotationAngle.ToString("0.###", CultureInfo.InvariantCulture);
            bool projectionDataChanged =
                !string.Equals(newInputSignature, _inputSignature, StringComparison.Ordinal) ||
                !string.Equals(newDirectionSignature, _directionSignature, StringComparison.Ordinal) ||
                !string.Equals(newDepthSignature, _depthSignature, StringComparison.Ordinal) ||
                !string.Equals(newRotationSignature, _rotationSignature, StringComparison.Ordinal);

            if (projectionDataChanged)
            {
                _cache = new ElevationCache();
                _inputSignature = newInputSignature;
                _directionSignature = newDirectionSignature;
                _depthSignature = newDepthSignature;
                _rotationSignature = newRotationSignature;
            }

            if (!run)
            {
                _lastRun = false;
                if (_cache.Diagnostics.Count == 0)
                    _cache.Diagnostics.Add("尚未执行：请将 Run 从 False 切换到 True。");
                SetOutputs(DA, _cache);
                return;
            }

            if (_lastRun)
            {
                if (projectionDataChanged && _cache.Diagnostics.Count == 0)
                    _cache.Diagnostics.Add("对象、视图方向、视图深度或旋转角度已变化，旧输出已清空，请重新执行。");
                else
                    _cache.Diagnostics.Add("当前输出来自缓存；保持 Run=True 不会重复运行 Make2D。");
                SetOutputs(DA, _cache);
                return;
            }

            _lastRun = true;

            if (inputObjects.Count == 0)
            {
                ElevationCache invalidCache = new ElevationCache();
                invalidCache.Diagnostics.Add("停止：对象输入为空。");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "对象输入为空。");
                SetOutputs(DA, invalidCache);
                return;
            }

            _cache = new ElevationCache();
            List<string> collectLogs = new List<string>();
            List<SectionMake2DV2.SourceGeometry> source = SectionMake2DV2.CollectSourceGeometry(inputObjects, collectLogs);
            _cache.Diagnostics.Add("成功解析几何数量: " + source.Count);
            _cache.Diagnostics.AddRange(collectLogs);

            if (source.Count == 0)
            {
                _cache.Diagnostics.Add("停止：没有可参与 Make2D 的几何。");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "没有可参与 Make2D 的几何。");
                SetOutputs(DA, _cache);
                return;
            }

            BoundingBox sourceBBox = SectionMake2DV2.GetSourceBoundingBox(source);
            SectionMake2DV2.SectionInfo section = CreateSectionInfoFromDirection(viewDirection, DirectionLabels[_selectedDirectionIndex], sourceBBox);
            _cache.Diagnostics.Add("基准前方向: " + SectionMake2DV2.FormatVector(inputFrontDirection));
            _cache.Diagnostics.Add("当前方向: " + DirectionLabels[_selectedDirectionIndex] + " " + SectionMake2DV2.FormatVector(section.ViewDirection));
            double automaticRotationAngle = GetAutomaticRotationAngle(viewDirection);
            double finalRotationAngle = automaticRotationAngle + rotationAngle;
            _cache.Diagnostics.Add("自动旋转角度: " + SectionMake2DV2.FormatDouble(automaticRotationAngle) +
                "，附加旋转角度: " + SectionMake2DV2.FormatDouble(rotationAngle) +
                "，最终旋转角度: " + SectionMake2DV2.FormatDouble(finalRotationAngle));
            _cache.Diagnostics.Add("视图平面点: " + SectionMake2DV2.FormatPoint(section.Line.From));

            List<SectionMake2DV2.SourceGeometry> visibleSource;
            if (viewDepth > 0.0)
            {
                visibleSource = source
                    .Where(item => SectionMake2DV2.IsInsideViewDepth(item.Geometry, section, viewDepth))
                    .ToList();
                _cache.Diagnostics.Add("视图深度范围内对象: " + visibleSource.Count + "/" + source.Count);
            }
            else
            {
                visibleSource = source;
                _cache.Diagnostics.Add("视图深度: 不限制，全部参与投影。");
            }

            if (visibleSource.Count == 0)
            {
                _cache.Diagnostics.Add("停止：视图深度内没有几何。");
                SetOutputs(DA, _cache);
                return;
            }

            using (SectionMake2DV2.NativeMake2DViewContext viewContext = CreateElevationMake2DViewContext(visibleSource, section, finalRotationAngle, _cache))
            {
                if (viewContext == null)
                {
                    SetOutputs(DA, _cache);
                    return;
                }

                _cache.Diagnostics.Add("视图矩形中心: " + SectionMake2DV2.FormatPoint(viewContext.ViewRectangle.Center) +
                    "，宽: " + SectionMake2DV2.FormatDouble(viewContext.ViewRectangle.Width) +
                    "，高: " + SectionMake2DV2.FormatDouble(viewContext.ViewRectangle.Height) +
                    "，法向: " + SectionMake2DV2.FormatVector(viewContext.ViewRectangle.Plane.Normal));

#if DEBUG
                _cache.DebugRectangle = viewContext.ViewRectangle;
                _cache.DebugPlane = viewContext.ViewRectangle.Plane;
#endif

                if (TryReadParallelViewOutput(viewContext, out IGH_Goo parallelViewOutput))
                    _cache.ParallelViewOutput = parallelViewOutput;

                if (RunMake2D(visibleSource, viewContext, section, _cache,
                    out List<Curve> visible, out List<int> visibleIndex, out List<string> visibleTypes,
                    out List<Curve> hidden, out List<int> hiddenIndex, out List<string> hiddenTypes))
                {
                    for (int i = 0; i < visible.Count; i++)
                    {
                        int objectIndex = i < visibleIndex.Count ? visibleIndex[i] : 0;
                        string type = i < visibleTypes.Count ? visibleTypes[i] : "其它";
                        _cache.RawVisibleCurves.Add(new SectionMake2DV2.TypedCurveRecord(visible[i], objectIndex, type));
                    }
                    for (int i = 0; i < hidden.Count; i++)
                    {
                        int objectIndex = i < hiddenIndex.Count ? hiddenIndex[i] : 0;
                        string type = i < hiddenTypes.Count ? hiddenTypes[i] : "其它";
                        _cache.RawHiddenCurves.Add(new SectionMake2DV2.TypedCurveRecord(hidden[i], objectIndex, type));
                    }
                }

                _cache.Diagnostics.Add("可见线缓存: " + _cache.RawVisibleCurves.Count +
                    "，隐藏线缓存: " + _cache.RawHiddenCurves.Count);
            }

            SetOutputs(DA, _cache);
        }

        private bool RunMake2D(List<SectionMake2DV2.SourceGeometry> source, SectionMake2DV2.NativeMake2DViewContext viewContext, SectionMake2DV2.SectionInfo section,
            ElevationCache result,
            out List<Curve> visible, out List<int> visibleIndex, out List<string> visibleTypes,
            out List<Curve> hidden, out List<int> hiddenIndex, out List<string> hiddenTypes)
        {
            visible = new List<Curve>();
            visibleIndex = new List<int>();
            visibleTypes = new List<string>();
            hidden = new List<Curve>();
            hiddenIndex = new List<int>();
            hiddenTypes = new List<string>();

            if (viewContext?.Document == null || viewContext.ParallelView == null)
            {
                result.Diagnostics.Add("Make2D 失败：共享视图无效。");
                return false;
            }

            GH_Component make2D = SectionMake2DV2.CreateMake2DComponent(out string createError);
            if (make2D == null)
            {
                result.Diagnostics.Add("Make2D 失败：未找到原生 Make2D 组件。" + createError);
                return false;
            }

            try
            {
                viewContext.Document.AddObject(make2D, false);
                result.Diagnostics.Add("Make2D 组件: " + make2D.GetType().FullName);
                result.Diagnostics.Add("Make2D 输入端口: " + SectionMake2DV2.DescribeParams(make2D.Params.Input));
                result.Diagnostics.Add("Make2D 输出端口: " + SectionMake2DV2.DescribeParams(make2D.Params.Output));

                SectionMake2DV2.SectionCache logCache = new SectionMake2DV2.SectionCache();
                SectionMake2DV2.TrySetMake2DAutomatic(make2D, logCache, "ElevationMake2D");
                result.Diagnostics.AddRange(logCache.Diagnostics);

                for (int i = 0; i < source.Count; i++)
                {
                    IGH_GeometricGoo goo = SectionMake2DV2.CreateGeometryGoo(source[i].Geometry);
                    if (goo != null)
                        SectionMake2DV2.SetVolatileInput(make2D, "G", 0, i, goo);
                }

                result.Diagnostics.Add("正视图：不连接裁剪平面 C。");
                SectionMake2DV2.ConnectComponentOutput(viewContext.ParallelView, 0, make2D, "V", 2);
                SectionMake2DV2.SetVolatileInput(make2D, "Te", 3, 0, new GH_Boolean(true));
                SectionMake2DV2.SetVolatileInput(make2D, "Ts", 4, 0, new GH_Boolean(false));

                SectionMake2DV2.ForceCompute(make2D);
                viewContext.Document.NewSolution(true);
                SectionMake2DV2.AppendMake2DRuntimeMessages(make2D, result.Diagnostics, "ElevationMake2D");
                result.Diagnostics.Add("Make2D 输入数据: " + SectionMake2DV2.DescribeParamDataCounts(make2D.Params.Input));
                result.Diagnostics.Add("Make2D 输出数据: " + SectionMake2DV2.DescribeParamDataCounts(make2D.Params.Output));

                int visibleOutput = SectionMake2DV2.FindParamIndex(make2D.Params.Output, "V", 0);
                int visibleIndexOutput = SectionMake2DV2.FindParamIndex(make2D.Params.Output, "Vi", 1);
                int visibleTypeOutput = SectionMake2DV2.FindParamIndex(make2D.Params.Output, "Vt", 2);
                int hiddenOutput = SectionMake2DV2.FindParamIndex(make2D.Params.Output, "H", 3);
                int hiddenIndexOutput = SectionMake2DV2.FindParamIndex(make2D.Params.Output, "Hi", 4);
                int hiddenTypeOutput = SectionMake2DV2.FindParamIndex(make2D.Params.Output, "Ht", 5);

                visible = SectionMake2DV2.ReadCurveOutput(make2D, visibleOutput);
                visibleIndex = SectionMake2DV2.MapMake2DIndices(SectionMake2DV2.ReadIntegerOutput(make2D, visibleIndexOutput), source);
                visibleTypes = SectionMake2DV2.ReadTextOutput(make2D, visibleTypeOutput);
                hidden = SectionMake2DV2.ReadCurveOutput(make2D, hiddenOutput);
                hiddenIndex = SectionMake2DV2.MapMake2DIndices(SectionMake2DV2.ReadIntegerOutput(make2D, hiddenIndexOutput), source);
                hiddenTypes = SectionMake2DV2.ReadTextOutput(make2D, hiddenTypeOutput);

                result.Diagnostics.Add("Make2D 完成：输入 " + source.Count +
                    "，可见线 " + visible.Count + "，隐藏线 " + hidden.Count + "。");
                return true;
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add("Make2D 执行失败：" + ex.Message);
                return false;
            }
        }

        private SectionMake2DV2.NativeMake2DViewContext CreateElevationMake2DViewContext(
            List<SectionMake2DV2.SourceGeometry> viewSource, SectionMake2DV2.SectionInfo section, double rotationAngle, ElevationCache result)
        {
            GH_Document document = new GH_Document();
            try
            {
                GH_Component rectangleComponent = SectionMake2DV2.CreateRectangleComponent(out string rectangleError);
                if (rectangleComponent == null)
                {
                    result.Diagnostics.Add("视图创建失败：未找到原生矩形组件。" + rectangleError);
                    document.Dispose();
                    return null;
                }

                GH_Component parallelView = SectionMake2DV2.CreateMake2DParallelViewComponent(out string viewError);
                if (parallelView == null)
                {
                    result.Diagnostics.Add("视图创建失败：未找到 Make2D Parallel View 组件。" + viewError);
                    document.Dispose();
                    return null;
                }

                document.AddObject(rectangleComponent, false);
                document.AddObject(parallelView, false);

                Vector3d viewDir = section.ViewDirection;
                viewDir.Unitize();
                Vector3d up = Math.Abs(viewDir * Vector3d.ZAxis) < 0.999 ? Vector3d.ZAxis : Vector3d.XAxis;
                Vector3d xAxis = Vector3d.CrossProduct(up, viewDir);
                if (!xAxis.Unitize())
                    xAxis = Vector3d.XAxis;
                Vector3d yAxis = Vector3d.CrossProduct(viewDir, xAxis);
                yAxis.Unitize();
                Vector3d rotatedXAxis = -yAxis;
                Vector3d rotatedYAxis = xAxis;
                xAxis = rotatedXAxis;
                yAxis = rotatedYAxis;
                if (Math.Abs(rotationAngle) > 1e-9)
                {
                    Transform rotation = Transform.Rotation(RhinoMath.ToRadians(rotationAngle), viewDir, Point3d.Origin);
                    xAxis.Transform(rotation);
                    yAxis.Transform(rotation);
                    xAxis.Unitize();
                    yAxis.Unitize();
                }

                double halfSize = RectangleHalfSize;
                Point3d rectOrigin = section.Line.From;

                Plane plane = new Plane(rectOrigin, xAxis, yAxis);
                if (plane.Normal * viewDir < 0.0)
                    plane.Flip();

                Rectangle3d viewRectangle = new Rectangle3d(plane,
                    new Interval(-halfSize, halfSize),
                    new Interval(-halfSize, halfSize));

                SectionMake2DV2.SetVolatileInput(rectangleComponent, "P", 0, 0, new GH_Plane(viewRectangle.Plane));
                SectionMake2DV2.SetVolatileInput(rectangleComponent, "X", 1, 0, new GH_Interval(viewRectangle.X));
                SectionMake2DV2.SetVolatileInput(rectangleComponent, "Y", 2, 0, new GH_Interval(viewRectangle.Y));
                SectionMake2DV2.SetVolatileInput(rectangleComponent, "R", 3, 0, new GH_Number(0.0));
                SectionMake2DV2.ConnectComponentOutput(rectangleComponent, 0, parallelView, "P", 0);

                SectionMake2DV2.ForceCompute(rectangleComponent);
                SectionMake2DV2.ForceCompute(parallelView);
                document.NewSolution(true);

                SectionMake2DV2.AppendMake2DRuntimeMessages(rectangleComponent, result.Diagnostics, "ElevationMake2D rectangle");
                SectionMake2DV2.AppendMake2DRuntimeMessages(parallelView, result.Diagnostics, "ElevationMake2D view");
                result.Diagnostics.Add("矩形输入数据: " + SectionMake2DV2.DescribeParamDataCounts(rectangleComponent.Params.Input));
                result.Diagnostics.Add("矩形输出数据: " + SectionMake2DV2.DescribeParamDataCounts(rectangleComponent.Params.Output));
                result.Diagnostics.Add("视图输入数据: " + SectionMake2DV2.DescribeParamDataCounts(parallelView.Params.Input));
                result.Diagnostics.Add("视图输出数据: " + SectionMake2DV2.DescribeParamDataCounts(parallelView.Params.Output));

                return new SectionMake2DV2.NativeMake2DViewContext(document, rectangleComponent, parallelView, viewRectangle);
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add("视图创建失败：" + ex.Message);
                document.Dispose();
                return null;
            }
        }

        private static SectionMake2DV2.SectionInfo CreateSectionInfoFromDirection(Vector3d viewDirection, string name, BoundingBox bbox)
        {
            Vector3d actualViewDirection = viewDirection;
            actualViewDirection.Unitize();

            Vector3d up = Math.Abs(actualViewDirection * Vector3d.ZAxis) < 0.999 ? Vector3d.ZAxis : Vector3d.XAxis;
            Vector3d xAxis = Vector3d.CrossProduct(up, actualViewDirection);
            if (!xAxis.Unitize())
                xAxis = Vector3d.XAxis;
            Vector3d yAxis = Vector3d.CrossProduct(actualViewDirection, xAxis);
            yAxis.Unitize();

            Point3d origin = GetViewPlaneOrigin(bbox, actualViewDirection);
            Plane plane = new Plane(origin, xAxis, yAxis);
            Line line = new Line(origin, origin + xAxis * 1000.0);
            return new SectionMake2DV2.SectionInfo(line, plane, actualViewDirection, name);
        }

        private void SetOutputs(IGH_DataAccess DA, ElevationCache cache)
        {
            DataTree<Curve> visibleTree = new DataTree<Curve>();
            DataTree<string> visibleTypeTree = new DataTree<string>();
            DataTree<Curve> hiddenTree = new DataTree<Curve>();
            DataTree<string> hiddenTypeTree = new DataTree<string>();

            foreach (SectionMake2DV2.TypedCurveRecord record in cache.RawVisibleCurves)
            {
                if (!_visibleLineTypes.Contains(record.Type))
                    continue;

                GH_Path path = new GH_Path(record.ObjectIndex);
                visibleTree.Add(record.Curve?.DuplicateCurve(), path);
                visibleTypeTree.Add(record.Type, path);
            }

            foreach (SectionMake2DV2.TypedCurveRecord record in cache.RawHiddenCurves)
            {
                if (!_hiddenLineTypes.Contains(record.Type))
                    continue;

                GH_Path path = new GH_Path(record.ObjectIndex);
                hiddenTree.Add(record.Curve?.DuplicateCurve(), path);
                hiddenTypeTree.Add(record.Type, path);
            }

            List<string> diagnostics = cache.Diagnostics
                .Where(msg => !SectionMake2DV2.LooksLikeMojibake(msg))
                .ToList();
            diagnostics.Insert(0, "ElevationMake2D 计算完成。");

            DA.SetDataTree(0, visibleTree);
            DA.SetDataTree(1, visibleTypeTree);
            DA.SetDataTree(2, hiddenTree);
            DA.SetDataTree(3, hiddenTypeTree);
            DA.SetDataList(4, diagnostics);
            DA.SetData(5, cache.ParallelViewOutput);
#if DEBUG
            DA.SetData(6, cache.DebugRectangle);
            DA.SetData(7, cache.DebugPlane);
#endif
        }

        public override bool IsPreviewCapable
        {
            get { return true; }
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox box = base.ClippingBox;
                if (_cachedBBox.IsValid)
                {
                    box.Union(_cachedBBox);
                    box.Inflate(Math.Max(_cachedBBox.Diagonal.Length, 1.0));
                }
                return box;
            }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (!_cachedBBox.IsValid || Hidden || Locked)
                return;

            Point3d center = _cachedBBox.Center;
            double diag = Math.Max(_cachedBBox.Diagonal.Length, 1.0);
            double arrowLength = Math.Max(diag * 0.45, 20.0);
            Vector3d[] directions = CreateRelativeDirections(_baseFrontDirection);

            for (int i = 0; i < directions.Length; i++)
            {
                Vector3d dir = directions[i];
                dir.Unitize();

                double radius = GetProjectedHalfExtent(_cachedBBox, dir);
                Point3d start = center - dir * (radius + arrowLength);
                Point3d end = center - dir * (radius + arrowLength * 0.25);
                Color color = i == _selectedDirectionIndex ? Color.Blue : Color.Red;

                args.Display.DrawLine(new Line(start, end), color, i == _selectedDirectionIndex ? 4 : 2);
                args.Display.DrawArrowHead(end, dir, color, 15.0, 30.0);
                args.Display.DrawDot(start, DirectionLabels[i]);
            }
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            for (int i = 0; i < DirectionLabels.Length; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem(DirectionLabels[i], null, (s, e) =>
                {
                    _selectedDirectionIndex = index;
                    ExpireSolution(true);
                })
                {
                    Checked = i == _selectedDirectionIndex,
                    Enabled = true
                };
                menu.Items.Add(item);
            }

            Menu_AppendSeparator(menu);
            AppendLineTypeFilterMenu(menu);
        }

        private void AppendLineTypeFilterMenu(ToolStripDropDown menu)
        {
            ToolStripMenuItem filterMenu = new ToolStripMenuItem("线型过滤");
            filterMenu.DropDown.AutoClose = true;
            filterMenu.DropDownItems.Add(CreateLineTypeHost("可见线类型", AllLineTypes, _visibleLineTypes));
            filterMenu.DropDownItems.Add(CreateLineTypeHost("隐藏线类型", AllLineTypes, _hiddenLineTypes));
            menu.Items.Add(filterMenu);
        }

        private ToolStripControlHost CreateLineTypeHost(string title, string[] allTypes, HashSet<string> selectedTypes)
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
                Font = new Font(SystemFonts.MenuFont, FontStyle.Bold),
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
                    ExpireSolution(true);
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

        public override bool Write(GH_IWriter writer)
        {
            writer.SetString("VisibleLineTypes", string.Join("\n", _visibleLineTypes));
            writer.SetString("HiddenLineTypes", string.Join("\n", _hiddenLineTypes));
            writer.SetInt32("SelectedDirectionIndex", _selectedDirectionIndex);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            if (reader.ItemExists("VisibleLineTypes"))
                _visibleLineTypes = ReadLineTypeSet(reader.GetString("VisibleLineTypes"), DefaultVisibleLineTypes);
            if (reader.ItemExists("HiddenLineTypes"))
                _hiddenLineTypes = ReadLineTypeSet(reader.GetString("HiddenLineTypes"), DefaultHiddenLineTypes);
            if (reader.ItemExists("SelectedDirectionIndex"))
                _selectedDirectionIndex = ClampDirectionIndex(reader.GetInt32("SelectedDirectionIndex"));
            else if (reader.ItemExists("PreviewDirection"))
                _selectedDirectionIndex = ClosestDirectionIndex(ReadLegacyPreviewDirection(reader.GetString("PreviewDirection")));
            return base.Read(reader);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.GetSectionMake2DV2(); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("B7E89024-4F73-45E8-AD86-64FC3014E629"); }
        }

        private static bool TryReadParallelViewOutput(SectionMake2DV2.NativeMake2DViewContext viewContext, out IGH_Goo output)
        {
            output = null;
            if (viewContext?.ParallelView == null || viewContext.ParallelView.Params.Output.Count == 0)
                return false;

            List<IGH_Goo> allData = viewContext.ParallelView.Params.Output[0].VolatileData.AllData(true).ToList();
            if (allData.Count == 0)
                return false;

            output = allData[0];
            return true;
        }

        private static Vector3d[] CreateRelativeDirections(Vector3d front)
        {
            if (!front.Unitize())
                front = DefaultFrontDirection;

            Vector3d referenceUp = Math.Abs(front * Vector3d.ZAxis) < 0.999 ? Vector3d.ZAxis : Vector3d.XAxis;
            Vector3d right = Vector3d.CrossProduct(referenceUp, front);
            if (!right.Unitize())
                right = Vector3d.XAxis;
            Vector3d up = Vector3d.CrossProduct(front, right);
            if (!up.Unitize())
                up = Vector3d.ZAxis;

            return new[]
            {
                front,
                -front,
                -right,
                right,
                up,
                -up
            };
        }

        private static BoundingBox ComputeCombinedBBox(List<IGH_Goo> inputObjects)
        {
            BoundingBox result = BoundingBox.Empty;
            foreach (IGH_Goo goo in inputObjects)
            {
                if (goo is IGH_GeometricGoo geo)
                {
                    BoundingBox bbox = geo.Boundingbox;
                    if (bbox.IsValid)
                        result.Union(bbox);
                }
            }
            return result;
        }

        private static double GetProjectedHalfExtent(BoundingBox bbox, Vector3d direction)
        {
            Vector3d dir = direction;
            if (!dir.Unitize())
                return 1.0;

            Point3d center = bbox.Center;
            double max = 0.0;
            foreach (Point3d corner in bbox.GetCorners())
            {
                double value = Math.Abs((corner - center) * dir);
                if (value > max)
                    max = value;
            }
            return Math.Max(max, 1.0);
        }

        private static Point3d GetViewPlaneOrigin(BoundingBox bbox, Vector3d viewDirection)
        {
            Vector3d dir = viewDirection;
            if (!dir.Unitize())
                dir = DefaultFrontDirection;

            if (!bbox.IsValid)
                return Point3d.Origin - dir * (RectangleHalfSize + 100.0);

            double minProjection = double.MaxValue;
            double maxProjection = double.MinValue;
            foreach (Point3d corner in bbox.GetCorners())
            {
                Vector3d cornerVector = new Vector3d(corner.X, corner.Y, corner.Z);
                double projection = cornerVector * dir;
                if (projection < minProjection)
                    minProjection = projection;
                if (projection > maxProjection)
                    maxProjection = projection;
            }

            double distance = Math.Max(100.0, -minProjection + 100.0);
            return Point3d.Origin - dir * distance;
        }

        private static double GetAutomaticRotationAngle(Vector3d viewDirection)
        {
            Vector3d dir = viewDirection;
            if (!dir.Unitize())
                dir = DefaultFrontDirection;

            double xyLength = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (xyLength > 1e-9)
            {
                double angle = RhinoMath.ToDegrees(Math.Atan2(dir.Y, dir.X));
                while (angle < 0.0)
                    angle += 360.0;
                while (angle >= 360.0)
                    angle -= 360.0;
                return angle;
            }

            return dir.Z >= 0.0 ? 180.0 : 0.0;
        }

        private static int ClampDirectionIndex(int index)
        {
            if (index < 0 || index >= DirectionLabels.Length)
                return DirectionFront;
            return index;
        }

        private static int ClosestDirectionIndex(Vector3d direction)
        {
            Vector3d[] directions = CreateRelativeDirections(DefaultFrontDirection);
            if (!direction.Unitize())
                return DirectionFront;

            int best = DirectionFront;
            double bestDot = double.MinValue;
            for (int i = 0; i < directions.Length; i++)
            {
                double dot = direction * directions[i];
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }
            return best;
        }

        private static Vector3d ReadLegacyPreviewDirection(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DefaultFrontDirection;

            string[] parts = value.Split(',');
            if (parts.Length != 3)
                return DefaultFrontDirection;

            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                return new Vector3d(x, y, z);

            return DefaultFrontDirection;
        }

        private static string CreateInputSignature(List<IGH_Goo> inputObjects)
        {
            if (inputObjects == null || inputObjects.Count == 0)
                return "empty";

            List<string> parts = new List<string>();
            foreach (IGH_Goo goo in inputObjects)
            {
                if (goo == null)
                {
                    parts.Add("null");
                    continue;
                }

                string part = goo.TypeName + ":" + goo.GetHashCode().ToString(CultureInfo.InvariantCulture);
                if (goo is IGH_GeometricGoo geo)
                {
                    BoundingBox bbox = geo.Boundingbox;
                    if (bbox.IsValid)
                        part += ":" + SectionMake2DV2.FormatPoint(bbox.Min) + ":" + SectionMake2DV2.FormatPoint(bbox.Max);
                }
                parts.Add(part);
            }
            return string.Join("|", parts);
        }

        private static string CreateVectorSignature(Vector3d v)
        {
            return v.X.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                v.Y.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                v.Z.ToString("0.000", CultureInfo.InvariantCulture);
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

        private class ElevationCache
        {
            public List<string> Diagnostics { get; } = new List<string>();
            public List<SectionMake2DV2.TypedCurveRecord> RawVisibleCurves { get; } = new List<SectionMake2DV2.TypedCurveRecord>();
            public List<SectionMake2DV2.TypedCurveRecord> RawHiddenCurves { get; } = new List<SectionMake2DV2.TypedCurveRecord>();
            public IGH_Goo ParallelViewOutput { get; set; }
#if DEBUG
            public Rectangle3d DebugRectangle { get; set; }
            public Plane DebugPlane { get; set; }
#endif
        }
    }
}
