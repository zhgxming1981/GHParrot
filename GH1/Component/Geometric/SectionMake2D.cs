using CommonFunction;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class SectionMake2D : GH_Component
    {
        private const string ViewSideKey = "ViewSide";
        private const double DefaultViewDepth = 500000.0;
        private const double ZeroTolerance = 1e-9;
        private bool _viewLeft = true;
        private bool _lastRun;
        private SectionCache _cache = new SectionCache();

        public SectionMake2D()
          : base("SectionMake2D", "剖切Make2D",
              "按剖切线批量生成剖切符号、截面线和类似Make2D的实线/虚线剖视图",
              "Parrot", "几何")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("对象", "G", "要剖切和投影的对象，可输入Brep、Surface、Curve、Rhino块或GH块", GH_ParamAccess.list);
            pManager.AddGenericParameter("剖切位置", "L", "XY平面上的剖切位置线，可输入Line或直线Curve", GH_ParamAccess.list);
            pManager.AddTextParameter("剖切号", "N", "剖切号，数量通常与剖切位置一致；重复剖切面使用第一次出现的剖切号", GH_ParamAccess.list);
            pManager.AddPointParameter("剖切面插入点", "P", "第一张剖视图在XY平面上的插入点", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("剖切面放大倍数", "S", "剖视图放大倍数", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("剖切面间距", "D", "多个剖视图之间的水平间距", GH_ParamAccess.item, 1000.0);
            pManager.AddNumberParameter("文字大小", "TH", "图名文字大小", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("文字偏移", "TO", "图名距离图形下方的偏移量", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("剖视图范围", "VD", "从剖切面开始沿剖切方向的范围，默认500000；小于等于0时不限制", GH_ParamAccess.item, DefaultViewDepth);
            pManager.AddBooleanParameter("执行", "Run", "为True且从False切换到True时开始计算，否则输出上一次缓存结果", GH_ParamAccess.item, false);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("截面线点", "Sec", "剖切面直接切到的截面曲线或点", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("截面索引", "SecI", "每条截面曲线或点对应的输入对象索引", GH_ParamAccess.tree);
            pManager.AddCurveParameter("实线", "Vis", "类似Make2D的可见线", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("实线索引", "VisI", "每条实线对应的输入对象索引", GH_ParamAccess.tree);
            pManager.AddCurveParameter("虚线", "Hid", "类似Make2D的隐藏线", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("虚线索引", "HidI", "每条虚线对应的输入对象索引", GH_ParamAccess.tree);
            pManager.AddGenericParameter("剖切符号", "Sym", "剖切线、方向箭头和剖切号文字", GH_ParamAccess.tree);
            pManager.AddGenericParameter("图名文字", "Title", "剖视图下方的图名文字对象", GH_ParamAccess.tree);
            pManager.AddTextParameter("剖切号", "N", "实际使用的剖切号", GH_ParamAccess.list);
            pManager.AddTextParameter("诊断信息", "Log", "本次计算的诊断信息", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
            {
                SectionCache illegalCache = new SectionCache();
                illegalCache.Diagnostics.Add("停止: CHardware.CheckLegality() 未通过。");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "授权检查未通过，组件未执行。");
                SetOutputs(DA, illegalCache);
                return;
            }

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
            bool shouldRun = run && !_lastRun;
            _lastRun = run;
            if (!shouldRun)
            {
                if (_cache.Diagnostics.Count == 0)
                {
                    _cache.Diagnostics.Add("未执行: 请将执行端口从 False 切换到 True。");
                    _cache.Diagnostics.Add("当前对象输入数量: " + inputObjects.Count);
                    _cache.Diagnostics.Add("当前剖切位置输入数量: " + sectionLineGoo.Count);
                    _cache.Diagnostics.AddRange(validation);
                }
                SetOutputs(DA, _cache);
                return;
            }

            if (inputObjects.Count == 0)
            {
                SectionCache invalidCache = new SectionCache();
                invalidCache.Diagnostics.Add("停止: 对象输入为空。");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "对象输入为空。");
                SetOutputs(DA, invalidCache);
                return;
            }

            if (sectionLineGoo.Count == 0)
            {
                SectionCache invalidCache = new SectionCache();
                invalidCache.Diagnostics.Add("停止: 剖切位置输入为空。");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "剖切位置输入为空。");
                SetOutputs(DA, invalidCache);
                return;
            }

            if (validation.Count > 0)
            {
                SectionCache invalidCache = new SectionCache();
                invalidCache.Diagnostics.Add("停止: 剖切位置输入不合法。");
                invalidCache.Diagnostics.AddRange(validation);
                foreach (string message in validation)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
                SetOutputs(DA, invalidCache);
                return;
            }

            try
            {
                double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
                _cache = Compute(inputObjects, sectionLines, sectionNames, insertPoint, hasInsertPoint, scale, spacing, textHeight, textOffset, viewDepth, tolerance);
                SetOutputs(DA, _cache);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                SetOutputs(DA, _cache);
            }
        }

        private SectionCache Compute(List<IGH_Goo> inputObjects, List<Line> sectionLines, List<string> sectionNames, Point3d insertPoint, bool hasInsertPoint, double scale, double spacing, double textHeight, double textOffset, double viewDepth, double tolerance)
        {
            SectionCache result = new SectionCache();
            result.Diagnostics.Add("输入对象数量: " + inputObjects.Count);
            result.Diagnostics.Add("输入剖切线数量: " + sectionLines.Count);
            result.Diagnostics.Add("插入点模式: " + (hasInsertPoint ? "使用输入插入点" : "按对象包围盒自动定位"));
            if (sectionLines.Count == 0)
            {
                result.Diagnostics.Add("停止: 没有输入剖切线。");
                return result;
            }

            List<string> collectLogs = new List<string>();
            List<SourceGeometry> source = CollectSourceGeometry(inputObjects, collectLogs);
            result.Diagnostics.Add("成功解析几何数量: " + source.Count);
            result.Diagnostics.AddRange(collectLogs);
            List<SectionInfo> sections = BuildUniqueSections(sectionLines, sectionNames, tolerance);
            result.Diagnostics.Add("有效去重后剖面数量: " + sections.Count);
            if (sections.Count == 0)
            {
                result.Diagnostics.Add("停止: 没有有效剖面。");
                return result;
            }

            double safeScale = Math.Abs(scale) < ZeroTolerance ? 1.0 : scale;
            BoundingBox sourceBox = GetSourceBoundingBox(source);
            Point3d autoBasePoint = GetAutoBasePoint(sourceBox, spacing, _viewLeft);
            for (int i = 0; i < sections.Count; i++)
            {
                SectionInfo section = sections[i];
                Point3d target = GetSectionTarget(insertPoint, hasInsertPoint, autoBasePoint, i, spacing, _viewLeft);
                Transform layout = Transform.Translation(target - Point3d.Origin) * Transform.Scale(Point3d.Origin, safeScale);
                Transform sectionToWorldXY = Transform.PlaneToPlane(section.Plane, Plane.WorldXY);
                Transform toLayout = layout * sectionToWorldXY;

                GH_Path path = new GH_Path(i);
                List<SourceGeometry> visibleSource = source.Where(item => IsInsideViewDepth(item.Geometry, section, viewDepth)).ToList();
                result.Diagnostics.Add("剖面 " + section.Name + ": 范围内对象 " + visibleSource.Count + "/" + source.Count);

                int beforeSection = result.SectionGeometry.DataCount;
                int beforeVisible = result.VisibleCurves.DataCount;
                int beforeHidden = result.HiddenCurves.DataCount;
                AddSectionIntersections(result, path, visibleSource, section.Plane, toLayout, tolerance);
                AddHiddenLine(result, path, visibleSource, section, layout, tolerance);
                result.Diagnostics.Add("剖面 " + section.Name + ": 截面 " + (result.SectionGeometry.DataCount - beforeSection) + "，实线 " + (result.VisibleCurves.DataCount - beforeVisible) + "，虚线 " + (result.HiddenCurves.DataCount - beforeHidden));
                AddSectionSymbol(result, path, section, section.Name, textHeight, _viewLeft);
                AddTitle(result, path, section.Name, target, textHeight, textOffset);
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
                    diagnostics.Add("剖切位置 " + i + ": 输入为空。");
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
                    diagnostics.Add("剖切位置 " + i + ": 不是直线Curve或Line，类型=" + goo.GetType().Name);
                    continue;
                }

                if (!line.IsValid || line.Length <= ZeroTolerance)
                {
                    diagnostics.Add("剖切位置 " + i + ": 直线无效或长度过短。");
                    continue;
                }

                line = new Line(
                    new Point3d(line.From.X, line.From.Y, 0.0),
                    new Point3d(line.To.X, line.To.Y, 0.0));
                result.Add(line);
            }

            return result;
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

        private static List<SourceGeometry> CollectSourceGeometry(List<IGH_Goo> inputObjects, List<string> diagnostics)
        {
            List<SourceGeometry> result = new List<SourceGeometry>();
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            for (int i = 0; i < inputObjects.Count; i++)
            {
                IGH_Goo goo = inputObjects[i];
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
                        AddRhinoObjectGeometry(doc, wrappedObject, Transform.Identity, i, result, new HashSet<Guid>());
                        handled = true;
                    }
                }
                else if (goo.CastTo(out GeometryBase castGeometry))
                    geometry = castGeometry;
                else if (goo.CastTo(out Guid castId))
                    id = castId;

                if (handled)
                    continue;

                if (id != Guid.Empty && doc != null)
                {
                    int before = result.Count;
                    RhinoObject obj = doc.Objects.FindId(id);
                    AddRhinoObjectGeometry(doc, obj, Transform.Identity, i, result, new HashSet<Guid>());
                    if (result.Count == before)
                        diagnostics.Add("对象 " + i + ": Guid未找到可用Rhino对象或对象无几何。");
                    continue;
                }

                int beforeAdd = result.Count;
                AddGeometry(doc, geometry, Transform.Identity, i, result, new HashSet<Guid>());
                if (result.Count == beforeAdd)
                    diagnostics.Add("对象 " + i + ": 未能解析为Brep/Surface/Curve/Guid/块，类型=" + goo.GetType().Name);
            }

            return result;
        }

        private static void AddRhinoObjectGeometry(RhinoDoc doc, RhinoObject obj, Transform transform, int index, List<SourceGeometry> result, HashSet<Guid> visited)
        {
            if (obj?.Geometry == null)
                return;

            if (obj is InstanceObject instance && instance.InstanceDefinition != null)
            {
                AddInstanceDefinition(doc, instance.InstanceDefinition, transform * instance.InstanceXform, index, result, visited);
                return;
            }

            AddGeometry(doc, obj.Geometry, transform, index, result, visited);
        }

        private static void AddInstanceDefinition(RhinoDoc doc, InstanceDefinition definition, Transform transform, int index, List<SourceGeometry> result, HashSet<Guid> visited)
        {
            if (definition == null || !visited.Add(definition.Id))
                return;

            foreach (RhinoObject obj in definition.GetObjects())
                AddRhinoObjectGeometry(doc, obj, transform, index, result, visited);

            visited.Remove(definition.Id);
        }

        private static void AddGeometry(RhinoDoc doc, GeometryBase geometry, Transform transform, int index, List<SourceGeometry> result, HashSet<Guid> visited)
        {
            if (geometry == null)
                return;

            if (geometry is InstanceReferenceGeometry reference && doc != null)
            {
                InstanceDefinition definition = doc.InstanceDefinitions.FindId(reference.ParentIdefId);
                AddInstanceDefinition(doc, definition, transform * reference.Xform, index, result, visited);
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
            result.Add(new SourceGeometry(duplicate, index));
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

        private static Point3d GetAutoBasePoint(BoundingBox sourceBox, double spacing, bool viewLeft)
        {
            if (!sourceBox.IsValid)
                return Point3d.Origin;

            double offset = Math.Abs(spacing);
            if (offset < ZeroTolerance)
                offset = Math.Max(1.0, sourceBox.Diagonal.Length * 0.25);

            double x = viewLeft ? sourceBox.Min.X - offset : sourceBox.Max.X + offset;
            return new Point3d(x, sourceBox.Center.Y, 0.0);
        }

        private static Point3d GetSectionTarget(Point3d insertPoint, bool hasInsertPoint, Point3d autoBasePoint, int sectionIndex, double spacing, bool viewLeft)
        {
            if (hasInsertPoint)
                return insertPoint + new Vector3d(sectionIndex * spacing, 0.0, 0.0);

            double sign = viewLeft ? -1.0 : 1.0;
            return autoBasePoint + new Vector3d(sign * sectionIndex * Math.Abs(spacing), 0.0, 0.0);
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

                Vector3d cutDirection = _viewLeft ? Vector3d.CrossProduct(Vector3d.ZAxis, xAxis) : Vector3d.CrossProduct(xAxis, Vector3d.ZAxis);
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

        private void AddHiddenLine(SectionCache result, GH_Path path, List<SourceGeometry> source, SectionInfo section, Transform layout, double tolerance)
        {
            if (source.Count == 0)
            {
                result.Diagnostics.Add("剖面 " + section.Name + ": HiddenLine跳过，范围内没有对象。");
                return;
            }

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
                result.Diagnostics.Add("剖面 " + section.Name + ": HiddenLine跳过，对象包围盒无效。");
                return;
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

            using (HiddenLineDrawing drawing = HiddenLineDrawing.Compute(parameters, true))
            {
                if (drawing == null)
                {
                    result.Diagnostics.Add("剖面 " + section.Name + ": HiddenLine返回空。");
                    return;
                }

                Transform hiddenToLayout = layout * drawing.WorldToHiddenLine;
                drawing.RejoinCompatibleVisible();
                int segmentCount = 0;
                foreach (HiddenLineDrawingSegment segment in drawing.Segments)
                {
                    segmentCount++;
                    Curve curve = segment.CurveGeometry?.DuplicateCurve();
                    if (curve == null)
                        continue;

                    curve.Transform(hiddenToLayout);
                    int index = GetSegmentIndex(segment);
                    if (IsVisible(segment))
                    {
                        result.VisibleCurves.Add(curve, path);
                        result.VisibleIndex.Add(index, path);
                    }
                    else
                    {
                        result.HiddenCurves.Add(curve, path);
                        result.HiddenIndex.Add(index, path);
                    }
                }
                result.Diagnostics.Add("剖面 " + section.Name + ": HiddenLine原始线段 " + segmentCount);
            }
        }

        private static int GetSegmentIndex(HiddenLineDrawingSegment segment)
        {
            object tag = segment.ParentCurve?.SourceObject?.Tag;
            if (tag is int index)
                return index;

            return -1;
        }

        private static bool IsVisible(HiddenLineDrawingSegment segment)
        {
            return segment.SegmentVisibility == HiddenLineDrawingSegment.Visibility.Visible;
        }

        private static void AddSectionSymbol(SectionCache result, GH_Path path, SectionInfo section, string name, double textHeight, bool viewLeft)
        {
            Line line = section.Line;
            Vector3d dir = line.Direction;
            if (!dir.Unitize())
                return;

            double arrowSize = Math.Max(textHeight, line.Length * 0.08);
            Vector3d view = section.ViewDirection;
            LineCurve baseLine = new LineCurve(line);
            result.Symbols.Append(new GH_Curve(baseLine), path);

            AddArrow(result.Symbols, path, line.From, view, dir, arrowSize);
            AddArrow(result.Symbols, path, line.To, view, -dir, arrowSize);

            Plane textPlaneA = Plane.WorldXY;
            textPlaneA.Origin = line.From + view * arrowSize * 0.8;
            Plane textPlaneB = Plane.WorldXY;
            textPlaneB.Origin = line.To + view * arrowSize * 0.8;
            result.Symbols.Append(new GH_ObjectWrapper(new TextEntity { Plane = textPlaneA, PlainText = name, TextHeight = textHeight, Justification = TextJustification.MiddleCenter }), path);
            result.Symbols.Append(new GH_ObjectWrapper(new TextEntity { Plane = textPlaneB, PlainText = name, TextHeight = textHeight, Justification = TextJustification.MiddleCenter }), path);
        }

        private static void AddArrow(GH_Structure<IGH_Goo> symbols, GH_Path path, Point3d tip, Vector3d view, Vector3d along, double size)
        {
            Point3d back = tip + view * size;
            Point3d p1 = back + along * size * 0.35;
            Point3d p2 = back - along * size * 0.35;
            Polyline polyline = new Polyline(new[] { p1, tip, p2 });
            symbols.Append(new GH_Curve(new PolylineCurve(polyline)), path);
        }

        private static void AddTitle(SectionCache result, GH_Path path, string name, Point3d target, double textHeight, double textOffset)
        {
            Plane plane = Plane.WorldXY;
            plane.Origin = target - new Vector3d(0.0, Math.Abs(textOffset), 0.0);
            string text = name + " 剖面";
            result.Titles.Append(new GH_ObjectWrapper(new TextEntity { Plane = plane, PlainText = text, TextHeight = textHeight, Justification = TextJustification.MiddleCenter }), path);
        }

        private static void SetOutputs(IGH_DataAccess DA, SectionCache cache)
        {
            DA.SetDataTree(0, cache.SectionGeometry);
            DA.SetDataTree(1, cache.SectionIndex);
            DA.SetDataTree(2, cache.VisibleCurves);
            DA.SetDataTree(3, cache.VisibleIndex);
            DA.SetDataTree(4, cache.HiddenCurves);
            DA.SetDataTree(5, cache.HiddenIndex);
            DA.SetDataTree(6, cache.Symbols);
            DA.SetDataTree(7, cache.Titles);
            DA.SetDataList(8, cache.Names);
            DA.SetDataList(9, cache.Diagnostics);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "左剖", (sender, e) => SetViewSide(true), true, _viewLeft);
            Menu_AppendItem(menu, "右剖", (sender, e) => SetViewSide(false), true, !_viewLeft);
        }

        private void SetViewSide(bool viewLeft)
        {
            if (_viewLeft == viewLeft)
                return;

            RecordUndoEvent("剖切方向");
            _viewLeft = viewLeft;
            Message = _viewLeft ? "左剖" : "右剖";
            ExpireSolution(true);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean(ViewSideKey, _viewLeft);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            bool viewLeft = true;
            if (reader.TryGetBoolean(ViewSideKey, ref viewLeft))
                _viewLeft = viewLeft;
            Message = _viewLeft ? "左剖" : "右剖";
            return base.Read(reader);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_CurvesGroupByPlane"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("D1E6F2B0-0B48-4C3C-8C2A-3AE39BFEF0F8"); }
        }

        private class SectionCache
        {
            public GH_Structure<IGH_GeometricGoo> SectionGeometry { get; } = new GH_Structure<IGH_GeometricGoo>();
            public DataTree<int> SectionIndex { get; } = new DataTree<int>();
            public DataTree<Curve> VisibleCurves { get; } = new DataTree<Curve>();
            public DataTree<int> VisibleIndex { get; } = new DataTree<int>();
            public DataTree<Curve> HiddenCurves { get; } = new DataTree<Curve>();
            public DataTree<int> HiddenIndex { get; } = new DataTree<int>();
            public GH_Structure<IGH_Goo> Symbols { get; } = new GH_Structure<IGH_Goo>();
            public GH_Structure<IGH_Goo> Titles { get; } = new GH_Structure<IGH_Goo>();
            public List<string> Names { get; } = new List<string>();
            public List<string> Diagnostics { get; } = new List<string>();
        }

        private class SourceGeometry
        {
            public SourceGeometry(GeometryBase geometry, int index)
            {
                Geometry = geometry;
                Index = index;
            }

            public GeometryBase Geometry { get; }
            public int Index { get; }
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
}
