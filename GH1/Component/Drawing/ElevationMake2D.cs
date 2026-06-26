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
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class ElevationMake2D : GH_Component
    {
        private const double DefaultViewDepth = 500000.0;
        private const double RectangleHalfSize = 100000.0;
        private static readonly string[] AllLineTypes =
        {
            "Silhouette, Crease",
            "Silhouette, Tangent",
            "Silhouette, Boundary",
            "Tangent",
            "Crease",
            "Boundary",
            "\u5176\u4ed6"
        };
        private static readonly string[] DefaultVisibleLineTypes =
        {
            "Silhouette, Crease",
            "Silhouette, Tangent",
            "Silhouette, Boundary",
            "Tangent",
            "Crease",
            "Boundary",
            "\u5176\u4ed6"
        };
        private static readonly string[] DefaultHiddenLineTypes =
        {
            "Boundary",
            "Crease",
            "Tangent",
            "\u5176\u4ed6"
        };

        private bool _lastRun;
        private ElevationCache _cache = new ElevationCache();
        private string _inputSignature = string.Empty;
        private string _directionSignature = string.Empty;
        private string _depthSignature = string.Empty;
        private HashSet<string> _visibleLineTypes = new HashSet<string>(DefaultVisibleLineTypes, StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _hiddenLineTypes = new HashSet<string>(DefaultHiddenLineTypes, StringComparer.OrdinalIgnoreCase);

        private Vector3d _previewDirection = new Vector3d(0, -1, 0);
        private Vector3d _currentDirection = new Vector3d(0, -1, 0);
        private BoundingBox _cachedBBox = BoundingBox.Empty;
        private static readonly Vector3d[] DirectionVectors = new Vector3d[]
        {
            new Vector3d(0, -1, 0),
            new Vector3d(0, 1, 0),
            new Vector3d(-1, 0, 0),
            new Vector3d(1, 0, 0),
            new Vector3d(0, 0, 1),
            new Vector3d(0, 0, -1),
        };
        private static readonly string[] DirectionLabels = { "前", "后", "左", "右", "上", "下" };

        public ElevationMake2D()
          : base("ElevationMake2D", "\u6b63\u89c6\u56feMake2D",
              "\u6cbf\u6307\u5b9a\u89c6\u56fe\u65b9\u5411\u5bf9\u51e0\u4f55\u505a Make2D \u6295\u5f71\uff0c\u751f\u6210\u53ef\u89c1\u7ebf/\u9690\u85cf\u7ebf\u53ca\u7c7b\u578b",
              "Parrot", "Drawing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("\u5bf9\u8c61", "G", "\u8981\u53c2\u4e0e Make2D \u6295\u5f71\u7684\u51e0\u4f55\u5bf9\u8c61\u3002\u652f\u6301 Brep\u3001Surface\u3001Curve\u3001Rhino \u5757\u3001GH \u5757\u6216 Rhino \u5bf9\u8c61 Guid\u3002", GH_ParamAccess.list);
            pManager.AddVectorParameter("\u89c6\u56fe\u65b9\u5411", "D", "\u6b63\u89c6\u56fe\u89c6\u7ebf\u65b9\u5411\u3002\u4f8b\u5982 (0, -1, 0) \u4e3a\u5357\u7acb\u9762\u3002", GH_ParamAccess.item, new Vector3d(0, -1, 0));
            pManager.AddNumberParameter("\u89c6\u56fe\u6df1\u5ea6", "VD", "\u6cbf\u89c6\u7ebf\u65b9\u5411\u53c2\u4e0e\u6295\u5f71\u7684\u51e0\u4f55\u6df1\u5ea6\u3002\u7a7a\u6216\u22640 \u65f6\u4e0d\u9650\u5236\u3002", GH_ParamAccess.item, DefaultViewDepth);
            pManager.AddBooleanParameter("\u6267\u884c", "Run", "\u4ece False \u5207\u6362\u5230 True \u65f6\u91cd\u65b0\u8ba1\u7b97\uff1b\u4fdd\u6301 False \u65f6\u8f93\u51fa\u4e0a\u4e00\u6b21\u7f13\u5b58\u3002\u53f3\u952e\u7ebf\u578b\u8fc7\u6ee4\u53ea\u7b5b\u9009\u7f13\u5b58\u7ed3\u679c\u3002", GH_ParamAccess.item, false);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("\u53ef\u89c1\u7ebf", "Vis", "Make2D \u53ef\u89c1\u7ebf\uff0c\u6309\u53f3\u952e\u300c\u53ef\u89c1\u7ebf\u7c7b\u578b\u300d\u8fc7\u6ee4\u3002\u8def\u5f84\u4e3a {\u5bf9\u8c61\u5e8f\u53f7}\u3002", GH_ParamAccess.tree);
            pManager.AddTextParameter("\u53ef\u89c1\u7ebf\u7c7b\u578b", "Vt", "\u4e0e\u53ef\u89c1\u7ebf\u4e00\u4e00\u5bf9\u5e94\u7684 Make2D \u7c7b\u578b\u6587\u5b57\u3002", GH_ParamAccess.tree);
            pManager.AddCurveParameter("\u9690\u85cf\u7ebf", "Hid", "Make2D \u9690\u85cf\u7ebf\uff0c\u6309\u53f3\u952e\u300c\u9690\u85cf\u7ebf\u7c7b\u578b\u300d\u8fc7\u6ee4\u3002\u8def\u5f84\u4e3a {\u5bf9\u8c61\u5e8f\u53f7}\u3002", GH_ParamAccess.tree);
            pManager.AddTextParameter("\u9690\u85cf\u7ebf\u7c7b\u578b", "Ht", "\u4e0e\u9690\u85cf\u7ebf\u4e00\u4e00\u5bf9\u5e94\u7684 Make2D \u7c7b\u578b\u6587\u5b57\u3002", GH_ParamAccess.tree);
            pManager.AddTextParameter("\u8bca\u65ad\u4fe1\u606f", "Log", "\u8ba1\u7b97\u8fc7\u7a0b\u3001\u89c6\u56fe\u65b9\u5411\u3001\u77e9\u5f62\u5c3a\u5bf8\u3001Make2D \u7aef\u53e3\u8bc6\u522b\u3001\u8b66\u544a\u548c\u9519\u8bef\u4fe1\u606f\u3002", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
            {
                _cache.Diagnostics.Add("\u505c\u6b62\uff1a\u6388\u6743\u68c0\u67e5\u5931\u8d25\u3002");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "\u6388\u6743\u68c0\u67e5\u5931\u8d25\u3002");
                SetOutputs(DA, _cache);
                return;
            }

            List<IGH_Goo> inputObjects = new List<IGH_Goo>();
            Vector3d viewDirection = _previewDirection;
            double viewDepth = DefaultViewDepth;
            bool run = false;

            DA.GetDataList(0, inputObjects);
            if (Params.Input[1].SourceCount > 0)
                DA.GetData(1, ref viewDirection);
            DA.GetData(2, ref viewDepth);
            DA.GetData(3, ref run);

            if (!viewDirection.Unitize())
            {
                _cache.Diagnostics.Clear();
                _cache.Diagnostics.Add("\u505c\u6b62\uff1a\u89c6\u56fe\u65b9\u5411\u5411\u91cf\u4e3a\u96f6\u5411\u91cf\u3002");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "\u89c6\u56fe\u65b9\u5411\u4e3a\u96f6\u5411\u91cf\u3002");
                SetOutputs(DA, _cache);
                return;
            }

            string newInputSignature = CreateInputSignature(inputObjects);
            string newDirectionSignature = CreateVectorSignature(viewDirection);
            string newDepthSignature = viewDepth.ToString("0.###");

            if (newInputSignature != _inputSignature || newDirectionSignature != _directionSignature || newDepthSignature != _depthSignature)
            {
                _cache = new ElevationCache();
                _inputSignature = newInputSignature;
                _directionSignature = newDirectionSignature;
                _depthSignature = newDepthSignature;
            }

            if (!run)
            {
                _lastRun = false;
                SetOutputs(DA, _cache);
                return;
            }

            if (_lastRun && run)
            {
                _cache.Diagnostics.Add("\u5f53\u524d\u8f93\u51fa\u6765\u81ea\u7f13\u5b58\uff1b\u4fee\u6539\u89c6\u56fe\u65b9\u5411\u3001\u89c6\u56fe\u6df1\u5ea6\u3001\u7ebf\u578b\u8fc7\u6ee4\u4e0d\u4f1a\u91cd\u65b0\u8fd0\u884c Make2D\u3002");
                SetOutputs(DA, _cache);
                return;
            }

            _cache = new ElevationCache();
            _lastRun = true;

            List<string> collectLogs = new List<string>();
            List<SectionMake2DV2.SourceGeometry> source = SectionMake2DV2.CollectSourceGeometry(inputObjects, collectLogs);
            _cache.Diagnostics.Add("\u6210\u529f\u89e3\u6790\u51e0\u4f55\u6570\u91cf: " + source.Count);
            _cache.Diagnostics.AddRange(collectLogs);

            if (source.Count == 0)
            {
                _cache.Diagnostics.Add("\u505c\u6b62\uff1a\u6ca1\u6709\u53ef\u53c2\u4e0e Make2D \u7684\u51e0\u4f55\u3002");
                SetOutputs(DA, _cache);
                return;
            }

            SectionMake2DV2.SectionInfo section = CreateSectionInfoFromDirection(viewDirection, "\u6b63\u89c6\u56fe");
            _cache.Diagnostics.Add("\u89c6\u56fe\u65b9\u5411: " + SectionMake2DV2.FormatVector(section.ViewDirection));
            _cache.Diagnostics.Add("\u5e73\u9762\u539f\u70b9: " + SectionMake2DV2.FormatPoint(section.Plane.Origin) +
                " X\u8f74: " + SectionMake2DV2.FormatVector(section.Plane.XAxis) +
                " Y\u8f74: " + SectionMake2DV2.FormatVector(section.Plane.YAxis));

            List<SectionMake2DV2.SourceGeometry> visibleSource;
            if (viewDepth > 0.0)
            {
                visibleSource = source
                    .Where(item => SectionMake2DV2.IsInsideViewDepth(item.Geometry, section, viewDepth))
                    .ToList();
                _cache.Diagnostics.Add("\u89c6\u56fe\u6df1\u5ea6\u8303\u56f4\u5185\u5bf9\u8c61: " + visibleSource.Count + "/" + source.Count);
            }
            else
            {
                visibleSource = source;
                _cache.Diagnostics.Add("\u89c6\u56fe\u6df1\u5ea6: \u4e0d\u9650\u5236\uff0c\u5168\u90e8\u53c2\u4e0e\u6295\u5f71\u3002");
            }

            if (visibleSource.Count == 0)
            {
                _cache.Diagnostics.Add("\u505c\u6b62\uff1a\u89c6\u56fe\u6df1\u5ea6\u5185\u6ca1\u6709\u51e0\u4f55\u3002");
                SetOutputs(DA, _cache);
                return;
            }

            using (var viewContext = CreateElevationMake2DViewContext(visibleSource, section, _cache))
            {
                if (viewContext == null)
                {
                    SetOutputs(DA, _cache);
                    return;
                }

                _cache.Diagnostics.Add("\u89c6\u56fe\u77e9\u5f62\u4e2d\u5fc3: " + SectionMake2DV2.FormatPoint(viewContext.ViewRectangle.Center) +
                    " \u5bbd: " + SectionMake2DV2.FormatDouble(viewContext.ViewRectangle.Width) +
                    " \u9ad8: " + SectionMake2DV2.FormatDouble(viewContext.ViewRectangle.Height));

                List<Curve> visible = new List<Curve>();
                List<int> visibleIndex = new List<int>();
                List<string> visibleTypes = new List<string>();
                List<Curve> hidden = new List<Curve>();
                List<int> hiddenIndex = new List<int>();
                List<string> hiddenTypes = new List<string>();

                if (RunMake2D(visibleSource, viewContext, section, _cache,
                    out visible, out visibleIndex, out visibleTypes,
                    out hidden, out hiddenIndex, out hiddenTypes))
                {
                    for (int i = 0; i < visible.Count; i++)
                        _cache.RawVisibleCurves.Add(new SectionMake2DV2.TypedCurveRecord(visible[i], visibleIndex[i], visibleTypes[i]));
                    for (int i = 0; i < hidden.Count; i++)
                        _cache.RawHiddenCurves.Add(new SectionMake2DV2.TypedCurveRecord(hidden[i], hiddenIndex[i], hiddenTypes[i]));
                }

                _cache.Diagnostics.Add("\u53ef\u89c1\u7ebf\u7f13\u5b58: " + _cache.RawVisibleCurves.Count +
                    "\uff0c\u9690\u85cf\u7ebf\u7f13\u5b58: " + _cache.RawHiddenCurves.Count);
            }

            SetOutputs(DA, _cache);
        }

            _currentDirection = viewDirection;
            _cachedBBox = ComputeCombinedBBox(inputObjects);

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

            if (viewContext?.Document == null || viewContext?.ParallelView == null)
            {
                result.Diagnostics.Add("Make2D \u5931\u8d25\uff1a\u5171\u4eab\u89c6\u56fe\u65e0\u6548\u3002");
                return false;
            }

            GH_Component make2D = SectionMake2DV2.CreateMake2DComponent(out string createError);
            if (make2D == null)
            {
                result.Diagnostics.Add("Make2D \u5931\u8d25\uff1a\u672a\u627e\u5230\u539f\u751f Make2D \u7ec4\u4ef6\u3002" + createError);
                return false;
            }

            try
            {
                viewContext.Document.AddObject(make2D, false);
                result.Diagnostics.Add("Make2D \u7ec4\u4ef6: " + make2D.GetType().FullName);
                result.Diagnostics.Add("Make2D \u8f93\u5165\u7aef\u53e3: " + SectionMake2DV2.DescribeParams(make2D.Params.Input));
                result.Diagnostics.Add("Make2D \u8f93\u51fa\u7aef\u53e3: " + SectionMake2DV2.DescribeParams(make2D.Params.Output));

                SectionMake2DV2.SectionCache logCache = new SectionMake2DV2.SectionCache();
                SectionMake2DV2.TrySetMake2DAutomatic(make2D, logCache, "ElevationMake2D");

                for (int i = 0; i < source.Count; i++)
                {
                    IGH_GeometricGoo goo = SectionMake2DV2.CreateGeometryGoo(source[i].Geometry);
                    if (goo != null)
                        SectionMake2DV2.SetVolatileInput(make2D, "G", 0, i, goo);
                }

                result.Diagnostics.Add("\u6b63\u89c6\u56fe\uff1a\u4e0d\u8fde\u63a5\u88c1\u526a\u5e73\u9762 C\u3002");

                SectionMake2DV2.ConnectComponentOutput(viewContext.ParallelView, 0, make2D, "V", 2);
                SectionMake2DV2.SetVolatileInput(make2D, "Te", 3, 0, new GH_Boolean(true));
                SectionMake2DV2.SetVolatileInput(make2D, "Ts", 4, 0, new GH_Boolean(false));

                SectionMake2DV2.ForceCompute(make2D);
                viewContext.Document.NewSolution(true);
                SectionMake2DV2.AppendMake2DRuntimeMessages(make2D, result.Diagnostics, "ElevationMake2D");
                result.Diagnostics.Add("Make2D \u8f93\u5165\u6570\u636e: " + SectionMake2DV2.DescribeParamDataCounts(make2D.Params.Input));
                result.Diagnostics.Add("Make2D \u8f93\u51fa\u6570\u636e: " + SectionMake2DV2.DescribeParamDataCounts(make2D.Params.Output));

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

                result.Diagnostics.Add("Make2D \u5b8c\u6210\uff1a\u8f93\u5165 " + source.Count +
                    "\uff0c\u53ef\u89c1\u7ebf " + visible.Count + "\uff0c\u9690\u85cf\u7ebf " + hidden.Count + "\u3002");
                return true;
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add("Make2D \u6267\u884c\u5931\u8d25\uff1a" + ex.Message);
                return false;
            }
        }

        private SectionMake2DV2.NativeMake2DViewContext CreateElevationMake2DViewContext(
            List<SectionMake2DV2.SourceGeometry> viewSource, SectionMake2DV2.SectionInfo section, ElevationCache result)
        {
            const double halfSize = RectangleHalfSize;

            GH_Document document = new GH_Document();
            try
            {
                GH_Component rectangleComponent = SectionMake2DV2.CreateRectangleComponent(out string rectangleError);
                if (rectangleComponent == null)
                {
                    result.Diagnostics.Add("\u89c6\u56fe\u521b\u5efa\u5931\u8d25\uff1a\u672a\u627e\u5230\u539f\u751f\u77e9\u5f62\u7ec4\u4ef6\u3002" + rectangleError);
                    document.Dispose();
                    return null;
                }

                GH_Component parallelView = SectionMake2DV2.CreateMake2DParallelViewComponent(out string viewError);
                if (parallelView == null)
                {
                    result.Diagnostics.Add("\u89c6\u56fe\u521b\u5efa\u5931\u8d25\uff1a\u672a\u627e\u5230 Make2D Parallel View \u7ec4\u4ef6\u3002" + viewError);
                    document.Dispose();
                    return null;
                }

                document.AddObject(rectangleComponent, false);
                document.AddObject(parallelView, false);

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
                    if (lineDirection.X < 0.0) { xAxis = -xAxis; yAxis = -yAxis; }
                }
                else
                {
                    xAxis = -Vector3d.ZAxis;
                    yAxis = -lineDirection;
                    if (lineDirection.Y > 0.0) { xAxis = -xAxis; yAxis = -yAxis; }
                }

                Plane plane = new Plane(Point3d.Origin, xAxis, yAxis);
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

                return new SectionMake2DV2.NativeMake2DViewContext(document, rectangleComponent, parallelView, viewRectangle);
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add("\u89c6\u56fe\u521b\u5efa\u5931\u8d25\uff1a" + ex.Message);
                document.Dispose();
                return null;
            }
        }

        private static SectionMake2DV2.SectionInfo CreateSectionInfoFromDirection(Vector3d viewDirection, string name)
        {
            Vector3d lineDirection = viewDirection;
            lineDirection.Z = 0.0;
            if (!lineDirection.Unitize())
                lineDirection = Vector3d.XAxis;

            Point3d lineFrom = Point3d.Origin;
            Point3d lineTo = lineFrom + lineDirection * 1000.0;
            Line line = new Line(lineFrom, lineTo);

            Plane plane = new Plane(Point3d.Origin, lineDirection, Vector3d.ZAxis);

            Vector3d actualViewDirection = viewDirection;
            actualViewDirection.Unitize();

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
                Curve output = record.Curve?.DuplicateCurve();
                visibleTree.Add(output, path);
                visibleTypeTree.Add(record.Type, path);
            }

            foreach (SectionMake2DV2.TypedCurveRecord record in cache.RawHiddenCurves)
            {
                if (!_hiddenLineTypes.Contains(record.Type))
                    continue;

                GH_Path path = new GH_Path(record.ObjectIndex);
                Curve output = record.Curve?.DuplicateCurve();
                hiddenTree.Add(output, path);
                hiddenTypeTree.Add(record.Type, path);
            }

            List<string> diagnostics = cache.Diagnostics
                .Where(msg => !SectionMake2DV2.LooksLikeMojibake(msg))
                .ToList();
            diagnostics.Insert(0, "ElevationMake2D \u8ba1\u7b97\u5b8c\u6210\u3002");

            DA.SetDataTree(0, visibleTree);
            DA.SetDataTree(1, visibleTypeTree);
            DA.SetDataTree(2, hiddenTree);
            DA.SetDataTree(3, hiddenTypeTree);
            DA.SetDataList(4, diagnostics);
        }

        private static string CreateInputSignature(List<IGH_Goo> inputObjects)
        {
            if (inputObjects == null || inputObjects.Count == 0)
                return "empty";

            List<string> parts = new List<string>();
            foreach (IGH_Goo goo in inputObjects)
            {
                if (goo == null)
                    parts.Add("null");
                else
                    parts.Add(goo.TypeName + ":" + goo.GetHashCode());
            }
            return string.Join("|", parts);
        }

        private static string CreateVectorSignature(Vector3d v)
        {
            return v.X.ToString("0.000") + "," + v.Y.ToString("0.000") + "," + v.Z.ToString("0.000");
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
                    box.Union(_cachedBBox);
                return box;
            }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (!_cachedBBox.IsValid || Hidden || Locked)
                return;

            Point3d center = new Point3d(
                (_cachedBBox.Min.X + _cachedBBox.Max.X) * 0.5,
                (_cachedBBox.Min.Y + _cachedBBox.Max.Y) * 0.5,
                (_cachedBBox.Min.Z + _cachedBBox.Max.Z) * 0.5);
            Vector3d half = new Vector3d(
                (_cachedBBox.Max.X - _cachedBBox.Min.X) * 0.5,
                (_cachedBBox.Max.Y - _cachedBBox.Min.Y) * 0.5,
                (_cachedBBox.Max.Z - _cachedBBox.Min.Z) * 0.5);

            Color selColor = Color.Blue;
            Color otherColor = Color.Red;

            for (int i = 0; i < DirectionVectors.Length; i++)
            {
                Vector3d dir = DirectionVectors[i];
                double halfSize = 0;
                if (Math.Abs(dir.X) > 0.5) halfSize = half.X;
                else if (Math.Abs(dir.Y) > 0.5) halfSize = half.Y;
                else if (Math.Abs(dir.Z) > 0.5) halfSize = half.Z;
                else continue;

                Point3d inner = center + dir * halfSize * 0.85;
                Point3d outer = center + dir * halfSize * 2.0;

                bool isSelected = (Math.Abs(dir.X - _currentDirection.X) < 0.01 &&
                                    Math.Abs(dir.Y - _currentDirection.Y) < 0.01 &&
                                    Math.Abs(dir.Z - _currentDirection.Z) < 0.01);
                Color color = isSelected ? selColor : otherColor;

                Line line = new Line(outer, inner);
                args.Display.DrawLine(line, color, 3);
                args.Display.DrawArrowHead(inner, dir, color, 15.0, 30.0);
                args.Display.DrawDot(outer, DirectionLabels[i]);
            }
        }

        private static BoundingBox ComputeCombinedBBox(List<IGH_Goo> inputObjects)
        {
            BoundingBox result = BoundingBox.Empty;
            foreach (IGH_Goo goo in inputObjects)
            {
                if (goo == null) continue;
                if (goo is IGH_GeometricGoo geo)
                {
                    BoundingBox bbox = geo.Boundingbox;
                    if (bbox.IsValid)
                        result.Union(bbox);
                }
            }
            return result;
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            bool hasSource = Params.Input[1].SourceCount > 0;
            ToolStripMenuItem dirMenu = new ToolStripMenuItem("投影方向");
            dirMenu.Enabled = !hasSource;
            for (int i = 0; i < DirectionVectors.Length; i++)
            {
                int index = i;
                Vector3d dir = DirectionVectors[i];
                bool isCurrent = Math.Abs(dir.X - _previewDirection.X) < 0.01 &&
                                  Math.Abs(dir.Y - _previewDirection.Y) < 0.01 &&
                                  Math.Abs(dir.Z - _previewDirection.Z) < 0.01;
                ToolStripMenuItem item = new ToolStripMenuItem(DirectionLabels[i], null, (s, e) =>
                {
                    _previewDirection = DirectionVectors[index];
                    _currentDirection = _previewDirection;
                    ExpireSolution(true);
                });
                item.Checked = isCurrent;
                dirMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(dirMenu);
            Menu_AppendSeparator(menu);
