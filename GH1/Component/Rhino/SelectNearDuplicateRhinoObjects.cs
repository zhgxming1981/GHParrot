using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace NS_Parrot
{
    public sealed class SelectNearDuplicateRhinoObjects : GH_Component
    {
        internal bool ButtonRun;
        private bool _lastRun;
        private uint _documentSerial;
        private readonly List<Guid> _selected = new List<Guid>();
        private string _status = "点击按钮开始筛选。";

        public SelectNearDuplicateRhinoObjects() : base("筛选近似重叠", "近似重叠",
            "筛选同一位置、外形范围一致且表面大部分重合的物体，允许孔洞等局部差异。采用表面积采样近似判断，只选中，不删除。支持曲面、多重曲面、挤出和网格；不处理曲线和块。",
            "Parrot", "Rhino") { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("对象ID", "ID", "可选的 Rhino 对象 GUID 列表；不连接时扫描当前文档可选物体，连接但为空时不扫描。", GH_ParamAccess.list);
            p[0].Optional = true;
            p[0].DataMapping = GH_DataMapping.Flatten;
            p.AddNumberParameter("距离容差", "距离", "重合距离及外形范围容差，使用模型单位；0 使用文档绝对容差。", GH_ParamAccess.item, 0);
            p.AddNumberParameter("允许差异", "差异%", "双方各自允许的不重合表面积百分比，范围 0～50；默认 10。孔壁也计入表面积。", GH_ParamAccess.item, 10);
            p.AddIntegerParameter("采样数量", "采样", "每个物体按表面积分布的采样点数，100～100000；增加可提高小孔识别稳定性，但会变慢。", GH_ParamAccess.item, 4000);
            p.AddBooleanParameter("执行", "Run", "False 变为 True 时执行，也可点击组件按钮。", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("选中ID", "ID", "本次成功选中的全部匹配物体 GUID，包含每对的双方。", GH_ParamAccess.list);
            p.AddTextParameter("状态", "状态", "筛选结果及跳过数量。参数或模型变化后需重新执行。", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (!CHardware.CheckLegality()) return;
            bool run = false;
            da.GetData(4, ref run);
            bool execute = ButtonRun || (run && !_lastRun);
            ButtonRun = false;
            _lastRun = run;
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null || doc.RuntimeSerialNumber != _documentSerial)
            {
                _selected.Clear();
                _status = "点击按钮开始筛选。";
                _documentSerial = doc == null ? 0 : doc.RuntimeSerialNumber;
            }
            if (execute)
            {
                _selected.Clear();
                double distance = 0, difference = 10;
                int samples = 4000;
                var ids = new List<string>();
                da.GetDataList(0, ids);
                da.GetData(1, ref distance);
                da.GetData(2, ref difference);
                da.GetData(3, ref samples);
                try
                {
                    if (doc == null) throw new InvalidOperationException("当前没有 Rhino 文档。");
                    if (!Finite(distance) || distance < 0 || !Finite(difference) || difference < 0 || difference > 50 || samples < 100 || samples > 100000)
                        throw new ArgumentException("距离必须为非负有限数，允许差异必须在 0～50 之间，采样数量必须在 100～100000 之间。");
                    if (distance == 0) distance = doc.ModelAbsoluteTolerance;
                    Scan(doc, ids, Params.Input[0].SourceCount > 0 || ids.Count > 0, distance, difference / 100, samples);
                }
                catch (Exception ex)
                {
                    _status = "筛选失败：" + ex.Message;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
                }
            }
            da.SetDataList(0, _selected.Select(id => id.ToString()));
            da.SetData(1, _status);
            Message = "已选 " + _selected.Count;
        }
        private void Scan(RhinoDoc doc, List<string> ids, bool restricted, double tolerance, double difference, int samples)
        {
            var objects = new List<RhinoObject>();
            int skipped = 0;
            if (restricted)
            {
                var seen = new HashSet<Guid>();
                foreach (string text in ids)
                {
                    Guid id;
                    if (!Guid.TryParse(text, out id)) { skipped++; continue; }
                    if (!seen.Add(id)) continue;
                    RhinoObject obj = doc.Objects.FindId(id);
                    if (obj == null) { skipped++; continue; }
                    objects.Add(obj);
                }
            }
            else objects.AddRange(doc.Objects.GetObjectList(new ObjectEnumeratorSettings
            {
                NormalObjects = true, HiddenObjects = false, LockedObjects = false,
                IncludeLights = false, IncludeGrips = false
            }));
            var candidates = new List<Candidate>();
            try
            {
                foreach (RhinoObject obj in objects)
                {
                    if (obj.IsHidden || obj.IsLocked || !obj.IsSelectable(true, false, false, false)) { skipped++; continue; }
                    GeometryBase geometry = obj.Geometry;
                    if (!(geometry is Brep) && !(geometry is Extrusion) && !(geometry is Surface) && !(geometry is Mesh)) { skipped++; continue; }
                    BoundingBox box = geometry.GetBoundingBox(true);
                    if (!geometry.IsValid || !box.IsValid) { skipped++; continue; }
                    candidates.Add(new Candidate { Object = obj, Box = box });
                }
                // 先排除不同位置的物体，再延迟生成网格。
                candidates.Sort((a, b) => a.Box.Min.X.CompareTo(b.Box.Min.X));
                var matches = new HashSet<Guid>();
                int pairs = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    CheckCancel();
                    Candidate a = candidates[i];
                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        Candidate b = candidates[j];
                        if (b.Box.Min.X - a.Box.Min.X > tolerance) break;
                        if (!SameBounds(a.Box, b.Box, tolerance)) continue;
                        a.Prepare(tolerance, samples);
                        b.Prepare(tolerance, samples);
                        if (a.Mesh == null || b.Mesh == null) continue;
                        if (Math.Min(a.Area, b.Area) / Math.Max(a.Area, b.Area) < 1 - difference) continue;
                        // 双向判断，防止只重合一小部分的物体被误选。
                        if (!Covered(a, b, tolerance, difference) || !Covered(b, a, tolerance, difference)) continue;
                        matches.Add(a.Object.Id);
                        matches.Add(b.Object.Id);
                        pairs++;
                    }
                }
                int failedMesh = candidates.Count(c => c.Prepared && c.Mesh == null);
                // 全部计算成功后才改变选择；无匹配时保留原有选择。
                if (matches.Count > 0)
                {
                    doc.Objects.UnselectAll();
                    foreach (Candidate candidate in candidates)
                        if (matches.Contains(candidate.Object.Id) && candidate.Object.Select(true) != 0)
                            _selected.Add(candidate.Object.Id);
                    doc.Views.Redraw();
                }
                _status = $"候选 {candidates.Count} 个，匹配 {pairs} 对，选中 {_selected.Count} 个；跳过 {skipped} 个，网格失败 {failedMesh} 个。结果为表面积采样近似判断。";
                if (matches.Count > _selected.Count) _status += "部分匹配物体无法选中。";
                if (skipped > 0 || failedMesh > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _status);
            }
            finally { foreach (Candidate candidate in candidates) candidate.Dispose(); }
        }
        private static void CheckCancel()
        {
            if (GH_Document.IsEscapeKeyDown()) throw new OperationCanceledException("用户已取消，未改变选择。");
        }
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        private static bool SameBounds(BoundingBox a, BoundingBox b, double t) =>
            Math.Abs(a.Min.X - b.Min.X) <= t && Math.Abs(a.Min.Y - b.Min.Y) <= t && Math.Abs(a.Min.Z - b.Min.Z) <= t &&
            Math.Abs(a.Max.X - b.Max.X) <= t && Math.Abs(a.Max.Y - b.Max.Y) <= t && Math.Abs(a.Max.Z - b.Max.Z) <= t;
        private static bool Covered(Candidate source, Candidate target, double tolerance, double difference)
        {
            int misses = 0;
            int allowed = (int)Math.Floor(source.Points.Length * difference + 1e-9);
            for (int i = 0; i < source.Points.Length; i++)
            {
                if (i % 256 == 0) CheckCancel();
                Point3d point;
                Vector3d normal;
                int face = target.Mesh.ClosestPoint(source.Points[i], out point, out normal, tolerance);
                if (face < 0 || point.DistanceTo(source.Points[i]) > tolerance)
                    if (++misses > allowed) return false;
            }
            return true;
        }
        private sealed class Candidate : IDisposable
        {
            internal RhinoObject Object;
            internal BoundingBox Box;
            internal Mesh Mesh;
            internal Point3d[] Points;
            internal double Area;
            internal bool Prepared;
            internal void Prepare(double tolerance, int count)
            {
                if (Prepared) return;
                Prepared = true;
                try
                {
                    Mesh original = Object.Geometry as Mesh;
                    if (original != null) Mesh = original.DuplicateMesh();
                    else
                    {
                        Brep brep = Object.Geometry as Brep;
                        bool owned = brep == null;
                        if (brep == null && Object.Geometry is Extrusion extrusion) brep = extrusion.ToBrep();
                        if (brep == null && Object.Geometry is Surface surface) brep = surface.ToBrep();
                        if (brep == null) return;
                        try
                        {
                            using (var settings = new MeshingParameters
                            {
                                Tolerance = tolerance * 0.1, MinimumTolerance = tolerance * 0.01,
                                RelativeTolerance = 0, RefineGrid = true, SimplePlanes = true
                            })
                            {
                                Mesh[] parts = Rhino.Geometry.Mesh.CreateFromBrep(brep, settings);
                                if (parts == null) return;
                                Mesh = new Mesh();
                                try { foreach (Mesh part in parts) if (part != null) Mesh.Append(part); }
                                finally { foreach (Mesh part in parts) part?.Dispose(); }
                            }
                        }
                        finally { if (owned) brep.Dispose(); }
                    }
                    Mesh.Faces.ConvertQuadsToTriangles();
                    if (!Mesh.IsValid || Mesh.Faces.Count == 0) { Dispose(); return; }
                    var cumulative = new double[Mesh.Faces.Count];
                    for (int i = 0; i < cumulative.Length; i++)
                    {
                        MeshFace f = Mesh.Faces[i];
                        Point3d a = Mesh.Vertices[f.A], b = Mesh.Vertices[f.B], c = Mesh.Vertices[f.C];
                        Area += Vector3d.CrossProduct(b - a, c - a).Length * 0.5;
                        cumulative[i] = Area;
                    }
                    if (!Finite(Area) || Area <= 0) { Dispose(); return; }
                    Points = new Point3d[count];
                    int triangle = 0;
                    // 面积加权采样避免孔壁细碎三角面获得过大权重。
                    for (int i = 0; i < count; i++)
                    {
                        double location = Area * (i + 0.5) / count;
                        while (triangle < cumulative.Length - 1 && cumulative[triangle] < location) triangle++;
                        MeshFace f = Mesh.Faces[triangle];
                        Point3d a = Mesh.Vertices[f.A], b = Mesh.Vertices[f.B], c = Mesh.Vertices[f.C];
                        double u = Math.Sqrt(RadicalInverse(i + 1, 2)), v = RadicalInverse(i + 1, 3);
                        Points[i] = a + (b - a) * (u * (1 - v)) + (c - a) * (u * v);
                    }
                }
                catch { Dispose(); }
            }
            private static double RadicalInverse(int number, int basis)
            {
                double result = 0, weight = 1.0 / basis;
                while (number > 0) { result += number % basis * weight; number /= basis; weight /= basis; }
                return result;
            }
            public void Dispose() { Mesh?.Dispose(); Mesh = null; Points = null; }
        }
        public override void CreateAttributes() { Attributes = new NearDuplicateAttributes(this); }
        protected override Bitmap Icon => GeneratedIcon.Get("gen_SelectRhinoObjectByLayerRun");
        public override Guid ComponentGuid => new Guid("E9A5DB73-95E7-4E9B-8704-9E84A1DFCE28");
    }
    internal sealed class NearDuplicateAttributes : GH_ComponentAttributes
    {
        internal NearDuplicateAttributes(SelectNearDuplicateRhinoObjects owner) : base(owner) { }
        private RectangleF Button => new RectangleF(Bounds.X + 3, Bounds.Bottom - 23, Bounds.Width - 6, 20);
        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 26);
        }
        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;
            using (var capsule = GH_Capsule.CreateCapsule(Button, GH_Palette.Black))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("筛选并选中", GH_FontServer.Small, Brushes.White, Button, format);
        }
        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (!Owner.Locked && e.Button == MouseButtons.Left && Button.Contains(e.CanvasLocation))
            {
                ((SelectNearDuplicateRhinoObjects)Owner).ButtonRun = true;
                Owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }
    }
}