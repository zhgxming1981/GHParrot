using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NS_Parrot
{
    public sealed class HaoChen2GH : GH_Component
    {
        private const string PipeName = "Parrot.HaoChen2GH.v1";
        private readonly List<GeometryBase> geometries = new List<GeometryBase>();
        private readonly List<string> handles = new List<string>();
        private readonly List<string> blockNames = new List<string>();
        private readonly List<string> layers = new List<string>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<string> lineTypes = new List<string>();
        private readonly List<string> fileNames = new List<string>();
        private string lastError = string.Empty;
        private readonly HashSet<string> handleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> suppressedHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool writeFirstPointToPt;
        private Point3d originalPt;
        private bool hasOriginalPt;
        private string bakeLayerName = "AutoCAD";

        public HaoChen2GH() : base("浩辰转GH", "浩辰转GH", "使用浩辰 ObjectARX 导入块内面域", "Parrot", "CAD") { }
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("pt", "pt", "CAD中的基点", GH_ParamAccess.item);
            pManager.AddPlaneParameter("PL", "PL", "Rhino中的局部坐标平面", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddTextParameter("Layer", "La", "Bake的目标图层", GH_ParamAccess.item, "AutoCAD");
            pManager[0].Optional = true; pManager[1].Optional = true; pManager[2].Optional = true;
        }
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("实体", "实体", "实体", GH_ParamAccess.list);
            pManager.AddTextParameter("图层", "图层", "图层", GH_ParamAccess.list);
            pManager.AddColourParameter("颜色", "颜色", "颜色", GH_ParamAccess.list);
            pManager.AddTextParameter("线型", "线型", "线型", GH_ParamAccess.list);
            pManager.AddTextParameter("句柄", "句柄", "句柄", GH_ParamAccess.list);
            pManager.AddTextParameter("块名", "块名", "块名", GH_ParamAccess.list);
            pManager.AddTextParameter("文件名", "文件名", "导入对象所在的CAD文件名", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Point3d insert = Point3d.Origin; Plane plane = Plane.WorldXY;
            DA.GetData(0, ref insert); DA.GetData(1, ref plane); DA.GetData(2, ref bakeLayerName);
            if (!string.IsNullOrWhiteSpace(lastError)) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, lastError);
            Transform xform = Transform.PlaneToPlane(new Plane(insert, Vector3d.XAxis, Vector3d.YAxis), plane);
            List<int> visibleIndices = Enumerable.Range(0, geometries.Count).Where(index => !suppressedHandles.Contains(handles[index])).ToList();
            List<GeometryBase> output = visibleIndices.Select(index => { GeometryBase copy = geometries[index].Duplicate(); copy.Transform(xform); return copy; }).ToList();
            DA.SetDataList(0, output); DA.SetDataList(1, visibleIndices.Select(index => layers[index])); DA.SetDataList(2, visibleIndices.Select(index => colors[index])); DA.SetDataList(3, visibleIndices.Select(index => lineTypes[index]));
            DA.SetDataList(4, visibleIndices.Select(index => handles[index])); DA.SetDataList(5, visibleIndices.Select(index => blockNames[index])); DA.SetData(6, visibleIndices.Select(index => fileNames[index]).FirstOrDefault() ?? string.Empty);
        }
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            AddMenuItem(menu, "连接浩辰", (sender, args) => CheckConnection());
            AddMenuItem(menu, "获取CAD元素", (sender, args) => Import(true, false));
            AddMenuItem(menu, "加选", (sender, args) => Import(false, false));
            AddMenuItem(menu, "减选", (sender, args) => Import(false, true));
            AddMenuItem(menu, "清空", (sender, args) => Clear());
            AddMenuItem(menu, "选中并放大", (sender, args) => SelectAndZoomInHaoChen());
            AddMenuItem(menu, "Bake", (sender, args) => Bake());
            AddMenuItem(menu, "整理导入列表", (sender, args) => EditImportedList());
            var writePoint = new ToolStripMenuItem("首点写入 pt") { Checked = writeFirstPointToPt, CheckOnClick = true };
            writePoint.CheckedChanged += (sender, args) => { SetWriteFirstPointToPt(writePoint.Checked); ExpireSolution(true); };
            menu.Items.Add(writePoint);
        }
        private static void AddMenuItem(ToolStripDropDown menu, string text, EventHandler action) { var item = new ToolStripMenuItem(text); item.Click += action; menu.Items.Add(item); }
        private void Import(bool replace, bool remove)
        {
            bool responseApplied = false;
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    pipe.Connect(1500);
                    using (var writer = new StreamWriter(pipe) { AutoFlush = true })
                    using (var reader = new StreamReader(pipe))
                    {
                        writer.WriteLine("PICK");
                        string json = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("浩辰未返回导入结果。");
                        HaoChenImportResponse response = new JavaScriptSerializer().Deserialize<HaoChenImportResponse>(json);
                        if (response == null) throw new InvalidOperationException("浩辰返回的数据无法解析。");
                        ApplyResponse(response, replace, remove);
                        responseApplied = true;
                        writer.WriteLine("ACK");
                    }
                }
            }
            catch (Exception ex) when (responseApplied)
            {
                // 浩辰收到 ACK 后会关闭服务端管道；此时客户端释放流可能抛出关闭异常，结果已有效。
            }
            catch (Exception ex)
            {
                lastError = "未连接到浩辰：" + ex.Message + "。请确认浩辰已启动，并已安装 HaoChen2GH.bundle。";
                MessageBox.Show(lastError, "浩辰转GH", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { ExpireSolution(true); }
        }
        private void ApplyResponse(HaoChenImportResponse response, bool replace, bool remove)
        {
            lastError = response.Error ?? string.Empty;
            if (replace) { Clear(); suppressedHandles.Clear(); }
            if (remove)
            {
                foreach (HaoChenRegionDto item in response.Regions ?? new List<HaoChenRegionDto>()) RemoveByHandle(item.Handle);
                return;
            }
            foreach (HaoChenRegionDto item in response.Regions ?? new List<HaoChenRegionDto>())
            {
                if (string.IsNullOrWhiteSpace(item.Handle) || !handleSet.Add(item.Handle)) continue;
                if (string.Equals(item.Kind, "Point", StringComparison.OrdinalIgnoreCase) && item.Position != null)
                {
                    AddGeometry(new Rhino.Geometry.Point(new Point3d(item.Position.X, item.Position.Y, item.Position.Z)), item);
                    continue;
                }
                if (string.Equals(item.Kind, "Text", StringComparison.OrdinalIgnoreCase) && item.Position != null)
                {
                    var textPlane = new Plane(new Point3d(item.Position.X, item.Position.Y, item.Position.Z), Vector3d.XAxis, Vector3d.YAxis);
                    var text = new TextEntity { Plane = textPlane, Text = item.Text, TextHeight = item.TextHeight <= 0 ? 1.0 : item.TextHeight };
                    text.Rotate(item.Rotation, textPlane.ZAxis, textPlane.Origin);
                    AddGeometry(text, item);
                    continue;
                }
                var edges = new List<Curve>();
                foreach (HaoChenLoopDto edge in item.Loops ?? new List<HaoChenLoopDto>())
                {
                    if (edge.Points == null || edge.Points.Count < 2) continue;
                    var polyline = new Polyline();
                    foreach (HaoChenPointDto point in edge.Points) polyline.Add(new Point3d(point.X, point.Y, point.Z));
                    edges.Add(new PolylineCurve(polyline));
                }
                if (string.Equals(item.Kind, "Curve", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (Curve edge in edges) AddGeometry(edge, item);
                    continue;
                }
                Curve[] joined = Curve.JoinCurves(edges, 0.01);
                List<Curve> closedLoops = joined == null ? new List<Curve>() : joined.Where(curve => curve != null && curve.IsClosed).ToList();
                if (closedLoops.Count == 0) { lastError += "\n块 " + item.Handle + " 的面域边界无法合并为闭合环。"; continue; }

                Plane plane;
                if (!closedLoops[0].TryGetPlane(out plane)) plane = Plane.WorldXY;
                closedLoops.Sort((a, b) => Math.Abs(AreaMassProperties.Compute(b).Area).CompareTo(Math.Abs(AreaMassProperties.Compute(a).Area)));
                var boundary = new List<Curve>();
                var used = new HashSet<Curve>();
                foreach (Curve outer in closedLoops)
                {
                    if (used.Contains(outer)) continue;
                    if (outer.ClosedCurveOrientation(plane) == CurveOrientation.Clockwise) outer.Reverse();
                    boundary.Add(outer); used.Add(outer);
                    foreach (Curve inner in closedLoops)
                    {
                        if (used.Contains(inner)) continue;
                        if (Curve.PlanarClosedCurveRelationship(inner, outer, plane, 0.01) != RegionContainment.AInsideB) continue;
                        if (inner.ClosedCurveOrientation(plane) == CurveOrientation.CounterClockwise) inner.Reverse();
                        boundary.Add(inner); used.Add(inner);
                    }
                }
                Brep[] breps = Brep.CreatePlanarBreps(boundary, Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);
                if (breps == null) { lastError += "\n块 " + item.Handle + " 的面域无法建面。"; continue; }
                foreach (Brep brep in breps) AddGeometry(brep, item);
            }
            SynchronizeFirstPointToInput();
        }
        private void AddGeometry(GeometryBase geometry, HaoChenRegionDto item) { geometries.Add(geometry); handles.Add(item.Handle); blockNames.Add(item.BlockName); layers.Add(item.Layer); colors.Add(Color.FromArgb(item.Color)); lineTypes.Add(item.LineType); fileNames.Add(item.FileName); }
        private void Clear() { geometries.Clear(); handles.Clear(); blockNames.Clear(); layers.Clear(); colors.Clear(); lineTypes.Clear(); fileNames.Clear(); handleSet.Clear(); lastError = string.Empty; ExpireSolution(true); }
        private void RemoveByHandle(string handle) { for (int i = handles.Count - 1; i >= 0; i--) if (string.Equals(handles[i], handle, StringComparison.OrdinalIgnoreCase)) { geometries.RemoveAt(i); handles.RemoveAt(i); blockNames.RemoveAt(i); layers.RemoveAt(i); colors.RemoveAt(i); lineTypes.RemoveAt(i); fileNames.RemoveAt(i); } handleSet.Remove(handle); }
        private void CheckConnection()
        {
            bool responseReceived = false;
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    pipe.Connect(1000);
                    using (var writer = new StreamWriter(pipe) { AutoFlush = true })
                    using (var reader = new StreamReader(pipe))
                    {
                        writer.WriteLine("PING");
                        if (string.IsNullOrWhiteSpace(reader.ReadLine())) throw new InvalidOperationException("浩辰未返回服务状态。");
                        responseReceived = true;
                        writer.WriteLine("ACK");
                    }
                }
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "浩辰服务已连接。");
            }
            catch (Exception) when (responseReceived)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "浩辰服务已连接。");
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "未连接浩辰：" + ex.Message); }
        }
        private void SelectAndZoomInHaoChen()
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    pipe.Connect(1500);
                    using (var writer = new StreamWriter(pipe) { AutoFlush = true })
                    using (var reader = new StreamReader(pipe))
                    {
                        writer.WriteLine("SELECT:" + string.Join(",", handles.Distinct(StringComparer.OrdinalIgnoreCase)));
                        string json = reader.ReadLine();
                        HaoChenImportResponse response = string.IsNullOrWhiteSpace(json) ? null : new JavaScriptSerializer().Deserialize<HaoChenImportResponse>(json);
                        writer.WriteLine("ACK");
                        if (response == null || !string.IsNullOrWhiteSpace(response.Error)) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, response == null ? "浩辰未返回选中结果。" : response.Error);
                    }
                }
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "选中并放大浩辰对象失败: " + ex.Message); }
        }
        private void Bake() { var doc = Rhino.RhinoDoc.ActiveDoc; if (doc == null) return; int layer = doc.Layers.FindByFullPath(bakeLayerName, -1); if (layer < 0) layer = doc.Layers.Add(bakeLayerName, Color.Black); for (int i = 0; i < geometries.Count; i++) { if (suppressedHandles.Contains(handles[i])) continue; Guid id = doc.Objects.Add(geometries[i]); if (id != Guid.Empty) { var obj = doc.Objects.FindId(id); obj.Attributes.LayerIndex = layer; obj.CommitChanges(); } } doc.Views.Redraw(); }
        private void EditImportedList()
        {
            List<HaoChenCachedItem> items = Enumerable.Range(0, geometries.Count).Select(index => new HaoChenCachedItem { Geometry = geometries[index], Handle = handles[index], BlockName = blockNames[index], Layer = layers[index], Color = colors[index], LineType = lineTypes[index], FileName = fileNames[index] }).ToList();
            using (var dialog = new HaoChenImportListEditor(items, suppressedHandles))
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                Clear(); suppressedHandles = dialog.SuppressedHandles;
                foreach (HaoChenCachedItem item in dialog.OrderedItems) { geometries.Add(item.Geometry); handles.Add(item.Handle); blockNames.Add(item.BlockName); layers.Add(item.Layer); colors.Add(item.Color); lineTypes.Add(item.LineType); fileNames.Add(item.FileName); handleSet.Add(item.Handle); }
                SynchronizeFirstPointToInput(); ExpireSolution(true);
            }
        }
        private void SynchronizeFirstPointToInput()
        {
            if (!writeFirstPointToPt || Params.Input.Count == 0) return;
            int index = Enumerable.Range(0, geometries.Count).FirstOrDefault(i => !suppressedHandles.Contains(handles[i]));
            if (geometries.Count == 0) return;
            Point3d firstPoint;
            if (geometries[index] is Brep brep && brep.Vertices.Count > 0) firstPoint = brep.Vertices[0].Location;
            else if (geometries[index] is Curve curve) firstPoint = curve.PointAtStart;
            else if (geometries[index] is TextEntity text) firstPoint = text.Plane.Origin;
            else return;
            var pointParam = Params.Input[0] as Param_Point;
            if (pointParam == null || pointParam.SourceCount > 0) return;
            pointParam.PersistentData.Clear(); pointParam.PersistentData.Append(new GH_Point(firstPoint)); pointParam.OnObjectChanged(GH_ObjectEventType.PersistentData);
            ScheduleSolutionAfterPtWrite();
        }
        private void SetWriteFirstPointToPt(bool enabled)
        {
            if (writeFirstPointToPt == enabled) return;
            var pointParam = Params.Input.Count > 0 ? Params.Input[0] as Param_Point : null;
            if (enabled && pointParam != null && pointParam.SourceCount == 0) { hasOriginalPt = false; if (pointParam.PersistentData.DataCount > 0 && pointParam.PersistentData.get_Branch(0)[0] is GH_Point point) { originalPt = point.Value; hasOriginalPt = true; } }
            writeFirstPointToPt = enabled;
            if (!enabled && pointParam != null && pointParam.SourceCount == 0) { pointParam.PersistentData.Clear(); if (hasOriginalPt) pointParam.PersistentData.Append(new GH_Point(originalPt)); pointParam.OnObjectChanged(GH_ObjectEventType.PersistentData); ScheduleSolutionAfterPtWrite(); }
            SynchronizeFirstPointToInput();
        }
        private void ScheduleSolutionAfterPtWrite() { GH_Document document = OnPingDocument(); if (document != null) document.ScheduleSolution(1, doc => ExpireSolution(false)); }
        protected override Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("7C0835F1-4B6B-4350-B1BC-BF4A1A2B1460"); } }
    }
    public sealed class HaoChenImportResponse { public List<HaoChenRegionDto> Regions { get; set; } = new List<HaoChenRegionDto>(); public string Error { get; set; } }
    public sealed class HaoChenRegionDto { public string Kind { get; set; } public string Handle { get; set; } public string BlockName { get; set; } public string Layer { get; set; } public int Color { get; set; } public string LineType { get; set; } public string FileName { get; set; } public HaoChenPointDto Position { get; set; } public string Text { get; set; } public double TextHeight { get; set; } public double Rotation { get; set; } public List<HaoChenLoopDto> Loops { get; set; } = new List<HaoChenLoopDto>(); }
    public sealed class HaoChenLoopDto { public List<HaoChenPointDto> Points { get; set; } = new List<HaoChenPointDto>(); }
    public sealed class HaoChenPointDto { public double X { get; set; } public double Y { get; set; } public double Z { get; set; } }
    internal sealed class HaoChenCachedItem { public GeometryBase Geometry; public string Handle; public string BlockName; public string Layer; public Color Color; public string LineType; public string FileName; }
    internal sealed class HaoChenImportListEditor : Form
    {
        private readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true };
        public List<HaoChenCachedItem> OrderedItems { get; private set; } = new List<HaoChenCachedItem>();
        public HashSet<string> SuppressedHandles { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HaoChenImportListEditor(IEnumerable<HaoChenCachedItem> items, ISet<string> suppressed)
        {
            Text = "整理浩辰导入列表"; StartPosition = FormStartPosition.CenterParent; Width = 700; Height = 460; list.Columns.Add("序号", 55); list.Columns.Add("导入项（取消勾选即抑制）", 570);
            foreach (HaoChenCachedItem item in items) { var row = new ListViewItem((list.Items.Count + 1).ToString()) { Tag = item, Checked = !suppressed.Contains(item.Handle) }; row.SubItems.Add(string.Format("{0}    图层: {1}    句柄: {2}", item.BlockName, item.Layer, item.Handle)); list.Items.Add(row); }
            var up = new Button { Text = "上移", Width = 70 }; up.Click += (s, e) => Move(-1); var down = new Button { Text = "下移", Width = 70 }; down.Click += (s, e) => Move(1); var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 70 }; ok.Click += (s, e) => Apply(); var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 70 };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) }; buttons.Controls.Add(cancel); buttons.Controls.Add(ok); buttons.Controls.Add(down); buttons.Controls.Add(up); Controls.Add(list); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
        }
        private void Move(int offset) { if (list.SelectedItems.Count != 1) return; var selected = list.SelectedItems[0]; int index = selected.Index + offset; if (index < 0 || index >= list.Items.Count) return; list.Items.Remove(selected); list.Items.Insert(index, selected); for (int i = 0; i < list.Items.Count; i++) list.Items[i].Text = (i + 1).ToString(); selected.Selected = true; }
        private void Apply() { foreach (ListViewItem row in list.Items) { var item = row.Tag as HaoChenCachedItem; if (item == null) continue; OrderedItems.Add(item); if (!row.Checked) SuppressedHandles.Add(item.Handle); } }
    }
}
