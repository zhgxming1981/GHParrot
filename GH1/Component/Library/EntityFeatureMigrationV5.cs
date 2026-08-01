using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.FileIO;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>
    /// 迁移专用的 V5 特征计算器。它只生成可保存的特征文本，不读取或写入数据库。
    /// </summary>
    internal sealed class EntityFeatureMigrationV5 : GH_Component
    {
        private const string SettingsChunk = "EntityFeatureMigrationV5Settings";
        private readonly Dictionary<V5Criterion, int> _decimals = new Dictionary<V5Criterion, int>
        {
            { V5Criterion.Length, 2 }, { V5Criterion.Inertia, 2 }, { V5Criterion.Area, 2 }, { V5Criterion.Volume, 2 },
            { V5Criterion.EdgeSum, 2 }, { V5Criterion.HolePosition, 4 }
        };

        public EntityFeatureMigrationV5()
            : base("实体分类特征计算（迁移版）", "实体特征V5", "计算迁移专用的 V5 实体分类特征；不写入数据库。", "Parrot", "Library") { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("材质", "材质", "与实体一一对应的材质，可为空。", GH_ParamAccess.list);
            p.AddTextParameter("颜色", "颜色", "与实体一一对应的颜色，可为空。", GH_ParamAccess.list);
            p.AddTextParameter("包围盒三边", "包围盒", "与实体一一对应。输入“最长边”或“最长边,中边,短边”；后两边可空。", GH_ParamAccess.list);
            p.AddBrepParameter("实体列表", "实体", "待计算的 Brep 实体。", GH_ParamAccess.list);
            p[0].Optional = true; p[1].Optional = true; p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("V5分类特征", "特征", "可直接输入“分类数据库迁移（V5）”的标准特征文本。", GH_ParamAccess.list);
            p.AddTextParameter("特征摘要", "摘要", "便于检查的实体特征摘要。", GH_ParamAccess.list);
            p.AddTextParameter("状态", "状态", "计算状态。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var materials = new List<string>(); var colors = new List<string>(); var lengths = new List<string>(); var breps = new List<Brep>();
            da.GetDataList(0, materials); da.GetDataList(1, colors); da.GetDataList(2, lengths);
            if (!da.GetDataList(3, breps) || breps.Count == 0) return;
            ValidateOptionalCount(materials, breps.Count, "材质");
            ValidateOptionalCount(colors, breps.Count, "颜色");
            ValidateOptionalCount(lengths, breps.Count, "包围盒三边");
            var features = new List<string>(); var summaries = new List<string>();
            for (int i = 0; i < breps.Count; i++)
            {
                try
                {
                    V5Feature feature = V5Feature.Create(TextAt(materials, i), TextAt(colors, i), TextAt(lengths, i), breps[i], _decimals, i);
                    features.Add(feature.Serialize()); summaries.Add(feature.ToSummary());
                }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
            }
            da.SetDataList(0, features); da.SetDataList(1, summaries);
            da.SetData(2, "已计算 " + features.Count.ToString(CultureInfo.InvariantCulture) + " 个 V5 分类特征。");
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu); Menu_AppendSeparator(menu);
            AddDecimalItem(menu, V5Criterion.Length, "包围盒入库精度"); AddDecimalItem(menu, V5Criterion.Inertia, "惯量入库精度"); AddDecimalItem(menu, V5Criterion.Area, "表面积入库精度");
            AddDecimalItem(menu, V5Criterion.Volume, "体积入库精度"); AddDecimalItem(menu, V5Criterion.EdgeSum, "所有边周长入库精度");
            AddDecimalItem(menu, V5Criterion.HolePosition, "孔位入库精度");
        }

        private void AddDecimalItem(ToolStripDropDown menu, V5Criterion criterion, string name)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(name + "：" + _decimals[criterion].ToString(CultureInfo.InvariantCulture) + " 位");
            item.Click += (s, e) =>
            {
                using (var dialog = new Form { Text = name, Width = 240, Height = 125, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false })
                using (var box = new NumericUpDown { Minimum = 0, Maximum = 12, Value = _decimals[criterion], Left = 20, Top = 15, Width = 180 })
                using (var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 125, Top = 48, Width = 75 })
                {
                    dialog.Controls.Add(box); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                    if (dialog.ShowDialog() == DialogResult.OK) { _decimals[criterion] = (int)box.Value; ExpireSolution(true); }
                }
            };
            menu.Items.Add(item);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetInt32(SettingsChunk + "Length", _decimals[V5Criterion.Length]); writer.SetInt32(SettingsChunk + "Inertia", _decimals[V5Criterion.Inertia]); writer.SetInt32(SettingsChunk + "Area", _decimals[V5Criterion.Area]);
            writer.SetInt32(SettingsChunk + "Volume", _decimals[V5Criterion.Volume]); writer.SetInt32(SettingsChunk + "Edge", _decimals[V5Criterion.EdgeSum]); writer.SetInt32(SettingsChunk + "Hole", _decimals[V5Criterion.HolePosition]);
            return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            ReadDecimal(reader, "Length", V5Criterion.Length); ReadDecimal(reader, "Inertia", V5Criterion.Inertia); ReadDecimal(reader, "Area", V5Criterion.Area); ReadDecimal(reader, "Volume", V5Criterion.Volume); ReadDecimal(reader, "Edge", V5Criterion.EdgeSum); ReadDecimal(reader, "Hole", V5Criterion.HolePosition);
            return base.Read(reader);
        }
        private void ReadDecimal(GH_IReader reader, string key, V5Criterion criterion) { int value = 0; if (reader.TryGetInt32(SettingsChunk + key, ref value)) _decimals[criterion] = Math.Max(0, Math.Min(12, value)); }
        private static void ValidateOptionalCount(List<string> list, int count, string name) { if (list != null && list.Count != 0 && list.Count != count) throw new ArgumentException(name + "数量为空时允许省略；不为空时必须与实体数量一致。"); }
        private static string TextAt(List<string> list, int index) { return list == null || list.Count == 0 ? string.Empty : (list[index] ?? string.Empty); }
        public override Guid ComponentGuid => new Guid("B1D97B01-3A4A-4F3C-91F0-4AC1D59EE801");
    }

    /// <summary>将 V5 特征和旧编号写入新的迁移数据库，并报告旧编号与真实特征的冲突。</summary>
    internal sealed class EntityFeatureDatabaseMigrationV5 : GH_Component
    {
        private const string SettingsChunk = "EntityFeatureDatabaseMigrationV5Settings";
        private readonly Dictionary<V5Criterion, bool> _enabled = new Dictionary<V5Criterion, bool>
        {
            { V5Criterion.Length, true }, { V5Criterion.Inertia, true }, { V5Criterion.Area, false },
            { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, false }, { V5Criterion.HolePosition, true }
        };
        private readonly Dictionary<V5Criterion, double> _tolerances = new Dictionary<V5Criterion, double>
        {
            { V5Criterion.Length, .5 }, { V5Criterion.Inertia, .2 }, { V5Criterion.Area, 1 },
            { V5Criterion.Volume, .5 }, { V5Criterion.EdgeSum, 1 }, { V5Criterion.HolePosition, .5 }
        };
        private double _mirrorTolerance = .001;
        private bool _rebuildRequested;

        public EntityFeatureDatabaseMigrationV5()
            : base("分类数据库迁移（V5）", "分类库迁移V5", "按 V5 分类特征重建迁移数据库，并校核同件异号与异件同号。", "Parrot", "Library") { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("新版数据库地址", "数据库", "迁移专用 SQLite 数据库文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("旧编号", "旧编号", "与分类特征一一对应的完整旧编号。", GH_ParamAccess.list);
            p.AddTextParameter("分类特征", "特征", "由“实体分类特征计算（迁移版）”输出的 V5 分类特征。", GH_ParamAccess.list);
        }
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("日志", "日志", "校核和迁移日志。", GH_ParamAccess.list);
            p.AddTextParameter("状态", "状态", "迁移状态。", GH_ParamAccess.item);
            p.AddBooleanParameter("重建完成", "完成", "成功重建新版数据库时为 True。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string path = string.Empty; var numbers = new List<string>(); var texts = new List<string>();
            if (!da.GetData(0, ref path) || !da.GetDataList(1, numbers) || !da.GetDataList(2, texts)) return;
            var logs = new List<string>(); bool saved = false;
            try
            {
                if (numbers.Count != texts.Count || numbers.Count == 0) throw new ArgumentException("旧编号和分类特征必须为数量相同的非空列表。");
                var records = new List<V5MigrationRecord>();
                for (int i = 0; i < texts.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(numbers[i])) throw new ArgumentException("第 " + (i + 1).ToString(CultureInfo.InvariantCulture) + " 个旧编号为空。");
                    records.Add(new V5MigrationRecord(i, numbers[i].Trim(), V5Feature.Deserialize(texts[i])));
                }
                V5MigrationAudit audit = V5MigrationAudit.Create(records, _enabled, _tolerances, _mirrorTolerance);
                logs.AddRange(audit.ToLogs());
                if (_rebuildRequested)
                {
                    V5MigrationStore.Rebuild(path, records, audit); saved = true; _rebuildRequested = false;
                    logs.Insert(0, "已重建 V5 迁移数据库：" + path);
                }
                else logs.Insert(0, "校核完成；右键点击“重建新版数据库”后才会写入数据库。");
            }
            catch (Exception ex) { logs.Insert(0, "错误：" + ex.Message); AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); _rebuildRequested = false; }
            da.SetDataList(0, logs); da.SetData(1, logs[0]); da.SetData(2, saved);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu); Menu_AppendSeparator(menu);
            foreach (V5Criterion criterion in Enum.GetValues(typeof(V5Criterion)).Cast<V5Criterion>().Where(x => x != V5Criterion.Mirror)) AddToleranceItem(menu, criterion);
            ToolStripMenuItem mirror = new ToolStripMenuItem("镜像区分容差：" + _mirrorTolerance.ToString("G6", CultureInfo.InvariantCulture));
            mirror.Click += (s, e) => EditDouble("镜像区分容差", _mirrorTolerance, value => _mirrorTolerance = Math.Abs(value)); menu.Items.Add(mirror);
            Menu_AppendSeparator(menu);
            ToolStripMenuItem rebuild = Menu_AppendItem(menu, "重建新版数据库", (s, e) => { _rebuildRequested = true; ExpireSolution(true); });
            rebuild.ToolTipText = "先备份目标文件，再重建 V5 迁移表。";
        }

        private void AddToleranceItem(ToolStripDropDown menu, V5Criterion criterion)
        {
            string name = V5Feature.CriterionName(criterion);
            ToolStripMenuItem enabled = Menu_AppendItem(menu, name + "（参与校核）", (s, e) => { _enabled[criterion] = !_enabled[criterion]; ExpireSolution(true); }, true, _enabled[criterion]);
            ToolStripMenuItem value = new ToolStripMenuItem(name + "容差：" + _tolerances[criterion].ToString("G6", CultureInfo.InvariantCulture));
            value.Click += (s, e) => EditDouble(name + "容差", _tolerances[criterion], x => _tolerances[criterion] = Math.Abs(x)); menu.Items.Add(value);
        }
        private void EditDouble(string title, double current, Action<double> set)
        {
            using (var form = new Form { Text = title, Width = 250, Height = 130, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false })
            using (var box = new TextBox { Text = current.ToString("G17", CultureInfo.InvariantCulture), Left = 18, Top = 15, Width = 195 })
            using (var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 138, Top = 48, Width = 75 })
            {
                form.Controls.Add(box); form.Controls.Add(ok); form.AcceptButton = ok;
                if (form.ShowDialog() == DialogResult.OK && double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) { set(value); ExpireSolution(true); }
            }
        }
        public override bool Write(GH_IWriter writer)
        {
            foreach (V5Criterion c in _enabled.Keys) { writer.SetBoolean(SettingsChunk + c + "Enabled", _enabled[c]); writer.SetDouble(SettingsChunk + c + "Tolerance", _tolerances[c]); }
            writer.SetDouble(SettingsChunk + "Mirror", _mirrorTolerance); return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            foreach (V5Criterion c in _enabled.Keys.ToList()) { bool e = _enabled[c]; double t = _tolerances[c]; if (reader.TryGetBoolean(SettingsChunk + c + "Enabled", ref e)) _enabled[c] = e; if (reader.TryGetDouble(SettingsChunk + c + "Tolerance", ref t)) _tolerances[c] = Math.Abs(t); }
            double m = _mirrorTolerance; if (reader.TryGetDouble(SettingsChunk + "Mirror", ref m)) _mirrorTolerance = Math.Abs(m); return base.Read(reader);
        }
        public override Guid ComponentGuid => new Guid("B1D97B02-3A4A-4F3C-91F0-4AC1D59EE802");
    }

    internal enum V5Criterion { Length, Inertia, Area, Volume, EdgeSum, HolePosition, Mirror, HoleArea }
    // 分类库迁移V8 已移除

    internal sealed class V5Hole
    {
        public double Perimeter; public double U; public double V; public double W;
        public V5Hole(double perimeter, double u, double v, double w) { Perimeter = perimeter; U = u; V = v; W = w; }
    }

    internal sealed class V5Feature
    {
        internal const string Header = "ENTITY_FEATURE_V5";
        public string Material = string.Empty; public string Color = string.Empty; public string Length = string.Empty;
        public double Area; public double Volume; public double EdgeSum; public double[] Inertia = new double[3]; public double MirrorScore; public double HoleMirrorScore;
        public List<V5Hole> Holes = new List<V5Hole>();

        public static V5Feature Create(string material, string color, string length, Brep brep, Dictionary<V5Criterion, int> decimals, int index)
        {
            if (brep == null) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 Brep 为空。");
            AreaMassProperties area = AreaMassProperties.Compute(brep, true, true, true, true);
            if (area == null) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 Brep 无法计算面积属性。");
            VolumeMassProperties volume = brep.IsSolid ? VolumeMassProperties.Compute(brep, true, true, true, true) : null;
            Point3d centroid; double[] inertia; Vector3d[] axes;
            if (!TryPrincipal(volume, out centroid, out inertia, out axes) && !TryPrincipal(area, out centroid, out inertia, out axes)) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 Brep 无法计算主惯量轴。");
            var samples = SamplePoints(brep); OrientAxes(samples, centroid, axes);
            var feature = new V5Feature { Material = material ?? string.Empty, Color = color ?? string.Empty, Length = NormalizeBoundingBox(length, decimals[V5Criterion.Length], index), Area = Round(area.Area, decimals[V5Criterion.Area]), Volume = Round(volume == null ? 0 : volume.Volume, decimals[V5Criterion.Volume]), Inertia = inertia.Select(x => Round(x, decimals[V5Criterion.Inertia])).ToArray() };
            feature.EdgeSum = Round(brep.Edges.Sum(edge => edge.GetLength()), decimals[V5Criterion.EdgeSum]);
            feature.MirrorScore = ComputeMirror(samples, centroid, axes);
            feature.Holes = ExtractHoles(brep, centroid, axes, decimals[V5Criterion.HolePosition], out double holeMirror);
            feature.HoleMirrorScore = holeMirror;
            return feature;
        }

        public string Serialize()
        {
            string holes = string.Join("/", Holes.Select(h => F(h.Perimeter) + "," + F(h.U) + "," + F(h.V) + "," + F(h.W)));
            return string.Join(";", new[] { Header, B(Material), B(Color), B(Length), F(Area), F(Volume), F(EdgeSum), F(Inertia[0]), F(Inertia[1]), F(Inertia[2]), F(MirrorScore), F(HoleMirrorScore), holes });
        }
        public static V5Feature Deserialize(string text)
        {
            string[] p = (text ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.None);
            if (p.Length != 13 || p[0] != Header) throw new ArgumentException("分类特征不是有效的 V5 格式。");
            var f = new V5Feature { Material = UB(p[1]), Color = UB(p[2]), Length = UB(p[3]), Area = D(p[4]), Volume = D(p[5]), EdgeSum = D(p[6]), Inertia = new[] { D(p[7]), D(p[8]), D(p[9]) }, MirrorScore = D(p[10]), HoleMirrorScore = D(p[11]) };
            if (!string.IsNullOrWhiteSpace(p[12])) foreach (string row in p[12].Split('/')) { string[] h = row.Split(','); if (h.Length != 4) throw new ArgumentException("V5 孔位数据格式无效。"); f.Holes.Add(new V5Hole(D(h[0]), D(h[1]), D(h[2]), D(h[3]))); }
            return f;
        }
        public string ToSummary() { return string.Format(CultureInfo.InvariantCulture, "材质={0}; 颜色={1}; 包围盒={2}; 惯量=({3:0.###},{4:0.###},{5:0.###}); 体积={6:0.###}; 孔位=数量={7}; 镜像值={8:0.######}; 孔镜像值={9:0.######}", Material, Color, string.IsNullOrWhiteSpace(Length) ? "" : "(" + Length + ")", Inertia[0], Inertia[1], Inertia[2], Volume, Holes.Count, MirrorScore, HoleMirrorScore); }
        public static string CriterionName(V5Criterion c) { switch (c) { case V5Criterion.Length: return "包围盒"; case V5Criterion.Inertia: return "转动惯量"; case V5Criterion.Area: return "表面积"; case V5Criterion.Volume: return "体积"; case V5Criterion.EdgeSum: return "孔周长"; case V5Criterion.HoleArea: return "孔面积"; case V5Criterion.HolePosition: return "孔位"; default: return "镜像区分"; } }
        private static string F(double x) { return x.ToString("R", CultureInfo.InvariantCulture); } private static double D(string x) { return double.Parse(x, CultureInfo.InvariantCulture); }
        private static string B(string x) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(x ?? string.Empty)); } private static string UB(string x) { return Encoding.UTF8.GetString(Convert.FromBase64String(x)); }
        private static double Round(double value, int decimals) { return Math.Round(value, Math.Max(0, Math.Min(12, decimals)), MidpointRounding.AwayFromZero); }
        internal static bool TryReadBoundingBox(string text, out double[] sizes)
        {
            sizes = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] values = text.Split(new[] { ',', '，', ';', '；', 'x', 'X', '×' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 1 && values.Length != 3) return false;
            var parsed = new List<double>();
            foreach (string value in values)
            {
                double size;
                if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out size) || double.IsNaN(size) || double.IsInfinity(size) || size <= 0) return false;
                parsed.Add(size);
            }
            sizes = parsed.OrderByDescending(x => x).ToArray();
            return true;
        }
        private static string NormalizeBoundingBox(string text, int decimals, int index)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            double[] sizes;
            if (!TryReadBoundingBox(text, out sizes)) throw new ArgumentException("第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 个包围盒必须输入 1 条最长边，或 3 条正数边长（格式：最长边,中边,短边）。");
            return string.Join(",", sizes.Select(x => F(Round(x, decimals))));
        }
        private static bool TryPrincipal(VolumeMassProperties props, out Point3d center, out double[] inertia, out Vector3d[] axes) { center = Point3d.Unset; inertia = null; axes = null; if (props == null) return false; center = props.Centroid; if (!props.CentroidCoordinatesPrincipalMomentsOfInertia(out double a, out Vector3d av, out double b, out Vector3d bv, out double c, out Vector3d cv)) return false; return Order(new[] { a, b, c }, new[] { av, bv, cv }, out inertia, out axes); }
        private static bool TryPrincipal(AreaMassProperties props, out Point3d center, out double[] inertia, out Vector3d[] axes) { center = Point3d.Unset; inertia = null; axes = null; if (props == null) return false; center = props.Centroid; if (!props.CentroidCoordinatesPrincipalMomentsOfInertia(out double a, out Vector3d av, out double b, out Vector3d bv, out double c, out Vector3d cv)) return false; return Order(new[] { a, b, c }, new[] { av, bv, cv }, out inertia, out axes); }
        private static bool Order(double[] values, Vector3d[] raw, out double[] inertia, out Vector3d[] axes) { int[] order = Enumerable.Range(0, 3).OrderByDescending(i => values[i]).ToArray(); inertia = order.Select(i => values[i]).ToArray(); axes = order.Select(i => { Vector3d x = raw[i]; return x.Unitize() ? x : Vector3d.Unset; }).ToArray(); return axes.All(x => x.IsValid); }
        private static List<Point3d> SamplePoints(Brep brep) { var p = brep.Vertices.Select(x => x.Location).ToList(); foreach (BrepEdge e in brep.Edges) { if (e == null) continue; p.Add(e.PointAtStart); p.Add(e.PointAtEnd); p.Add(e.PointAtNormalizedLength(.5)); } return p; }
        private static void OrientAxes(List<Point3d> points, Point3d center, Vector3d[] axes) { for (int i = 0; i < 3; i++) { double third = points.Sum(p => { double x = (p - center) * axes[i]; return x * x * x; }); if (third < 0) axes[i] = -axes[i]; } if (Vector3d.CrossProduct(axes[0], axes[1]) * axes[2] < 0) axes[2] = -axes[2]; }
        private static double ComputeMirror(List<Point3d> points, Point3d center, Vector3d[] axes) { double s = 0, scale = 0; foreach (Point3d p in points) { Vector3d q = p - center; double v = (q * axes[0]) * (q * axes[1]) * (q * axes[2]); s += v; scale += Math.Abs(v); } return scale <= RhinoMath.ZeroTolerance ? 0 : s / scale; }
        private static List<V5Hole> ExtractHoles(Brep brep, Point3d center, Vector3d[] axes, int decimals, out double holeMirror)
        {
            var result = new List<V5Hole>(); double signed = 0, scale = 0;
            foreach (BrepFace face in brep.Faces) foreach (BrepLoop loop in face.Loops) if (loop.LoopType == BrepLoopType.Inner)
            {
                var points = new List<Point3d>(); double perimeter = 0;
                foreach (BrepTrim trim in loop.Trims) { BrepEdge edge = trim.Edge; if (edge == null) continue; perimeter += edge.GetLength(); points.Add(edge.PointAtStart); points.Add(edge.PointAtEnd); points.Add(edge.PointAtNormalizedLength(.5)); }
                if (perimeter <= RhinoMath.ZeroTolerance || points.Count == 0) continue;
                Point3d c = new Point3d(points.Average(x => x.X), points.Average(x => x.Y), points.Average(x => x.Z)); Vector3d v = c - center;
                double u = v * axes[0], vv = v * axes[1], w = v * axes[2]; result.Add(new V5Hole(Round(perimeter, decimals), Round(u, decimals), Round(vv, decimals), Round(w, decimals)));
                signed += perimeter * vv; scale += perimeter * Math.Sqrt(vv * vv + w * w);
            }
            holeMirror = scale <= RhinoMath.ZeroTolerance ? 0 : Math.Max(-1, Math.Min(1, signed / scale));
            return result.OrderBy(x => x.Perimeter).ThenBy(x => x.U).ThenBy(x => x.V).ThenBy(x => x.W).ToList();
        }
    }

    internal sealed class V5MigrationRecord { public int Index; public string Number; public V5Feature Feature; public string SourceFile; public V5MigrationRecord(int index, string number, V5Feature feature, string sourceFile = "") { Index = index; Number = number; Feature = feature; SourceFile = sourceFile ?? string.Empty; } }

    internal sealed class V5MigrationAudit
    {
        public List<List<V5MigrationRecord>> Classes = new List<List<V5MigrationRecord>>(); public List<string> SamePartDifferentNumbers = new List<string>(); public List<string> DifferentPartsSameNumber = new List<string>();
        public static V5MigrationAudit Create(List<V5MigrationRecord> records, Dictionary<V5Criterion, bool> enabled, Dictionary<V5Criterion, double> tolerances, double mirrorTolerance)
        {
            // 容差匹配不是严格的传递关系；不能用 A≈B、B≈C 就把 A/C 强行合并。
            // 只有与同一类内全部记录均匹配，才放入该实际零件类。
            var audit = new V5MigrationAudit();
            foreach (V5MigrationRecord record in records)
            {
                List<V5MigrationRecord> target = audit.Classes.FirstOrDefault(group => group.All(existing => Matches(record.Feature, existing.Feature, enabled, tolerances, mirrorTolerance)));
                if (target == null) { target = new List<V5MigrationRecord>(); audit.Classes.Add(target); }
                target.Add(record);
            }
            foreach (var c in audit.Classes) { var numbers = c.Select(x => x.Number).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToList(); if (numbers.Count > 1) audit.SamePartDifferentNumbers.Add("实际相同、编号不同：" + string.Join("、", numbers) + "；输入序号：" + string.Join("、", c.Select(x => (x.Index + 1).ToString(CultureInfo.InvariantCulture)))); }
            foreach (var group in records.GroupBy(x => x.Number, StringComparer.Ordinal)) { int count = audit.Classes.Count(c => c.Any(x => x.Number == group.Key)); if (count > 1) audit.DifferentPartsSameNumber.Add("实际不同、编号相同：" + group.Key + "；分为 " + count.ToString(CultureInfo.InvariantCulture) + " 类；输入序号：" + string.Join("、", group.Select(x => (x.Index + 1).ToString(CultureInfo.InvariantCulture)))); }
            return audit;
        }
        public IEnumerable<string> ToLogs() { yield return "共识别 " + Classes.Count.ToString(CultureInfo.InvariantCulture) + " 个实际零件类。"; if (SamePartDifferentNumbers.Count == 0) yield return "未发现实际相同但编号不同。"; else foreach (string s in SamePartDifferentNumbers) yield return s; if (DifferentPartsSameNumber.Count == 0) yield return "未发现实际不同但编号相同。"; else foreach (string s in DifferentPartsSameNumber) yield return s; }
        private static bool Matches(V5Feature a, V5Feature b, Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t, double mirror)
        {
            if (!string.Equals(a.Material, b.Material, StringComparison.Ordinal) || !string.Equals(a.Color, b.Color, StringComparison.Ordinal)) return false;
            if (MirrorSide(a, mirror) != MirrorSide(b, mirror)) return false;
            if (e[V5Criterion.Length] && !BoundingBoxMatches(a.Length, b.Length, t[V5Criterion.Length])) return false;
            if (e[V5Criterion.Inertia] && !a.Inertia.Zip(b.Inertia, (x, y) => Percent(x, y, t[V5Criterion.Inertia])).All(x => x)) return false;
            if (e[V5Criterion.Area] && !Percent(a.Area, b.Area, t[V5Criterion.Area])) return false; if (e[V5Criterion.Volume] && !Percent(a.Volume, b.Volume, t[V5Criterion.Volume])) return false; if (e[V5Criterion.EdgeSum] && !Percent(a.EdgeSum, b.EdgeSum, t[V5Criterion.EdgeSum])) return false;
            return !e[V5Criterion.HolePosition] || HoleMatches(a.Holes, b.Holes, t[V5Criterion.HolePosition]);
        }
        private static bool Percent(double a, double b, double p) { double s = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-9); return Math.Abs(a - b) <= s * Math.Abs(p) / 100.0; }
        private static bool BoundingBoxMatches(string a, string b, double tolerance)
        {
            double[] aa, bb;
            if (!V5Feature.TryReadBoundingBox(a, out aa) || !V5Feature.TryReadBoundingBox(b, out bb)) return false;
            if (!Percent(aa[0], bb[0], tolerance)) return false;
            // 只提供最长边时，按最长边校核；双方均提供三边时才增加其余两边的约束。
            return aa.Length != 3 || bb.Length != 3 || (Percent(aa[1], bb[1], tolerance) && Percent(aa[2], bb[2], tolerance));
        }
        private static string MirrorSide(V5Feature f, double t) { double s = Math.Abs(f.HoleMirrorScore) > t ? f.HoleMirrorScore : f.MirrorScore; return Math.Abs(s) <= t ? string.Empty : (s >= 0 ? "A" : "B"); }
        private static bool HoleMatches(List<V5Hole> a, List<V5Hole> b, double tolerance)
        {
            if (a.Count != b.Count) return false;
            int n = a.Count;
            double[][] signs = { new[] { 1d, 1d, 1d }, new[] { -1d, -1d, 1d }, new[] { -1d, 1d, -1d }, new[] { 1d, -1d, -1d } };
            foreach (double[] sign in signs)
            {
                int[] rightToLeft = Enumerable.Repeat(-1, n).ToArray();
                bool complete = true;
                for (int left = 0; left < n; left++)
                {
                    bool[] visited = new bool[n];
                    if (!TryAssign(left, a, b, sign, tolerance, rightToLeft, visited)) { complete = false; break; }
                }
                if (complete) return true;
            }
            return false;
        }
        private static bool TryAssign(int left, List<V5Hole> a, List<V5Hole> b, double[] s, double t, int[] rightToLeft, bool[] visited)
        {
            for (int right = 0; right < b.Count; right++)
            {
                if (visited[right]) continue;
                double du = a[left].U - b[right].U * s[0], dv = a[left].V - b[right].V * s[1], dw = a[left].W - b[right].W * s[2];
                bool edge = Math.Abs(a[left].Perimeter - b[right].Perimeter) < t && Math.Sqrt(du * du + dv * dv + dw * dw) < t;
                if (!edge) continue;
                visited[right] = true;
                if (rightToLeft[right] < 0 || TryAssign(rightToLeft[right], a, b, s, t, rightToLeft, visited)) { rightToLeft[right] = left; return true; }
            }
            return false;
        }
    }

    internal static class V5MigrationStore
    {
        public static void EnsureSchema(string path)
        {
            using (var connection = new SQLiteConnection("Data Source=" + path + ";Version=3;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE IF NOT EXISTS entity_feature_migration_v5 (id INTEGER PRIMARY KEY AUTOINCREMENT, old_number TEXT NOT NULL, feature_text TEXT NOT NULL, source_index INTEGER NOT NULL, source_file TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP); CREATE INDEX IF NOT EXISTS idx_entity_feature_migration_v5_number ON entity_feature_migration_v5(old_number); CREATE TABLE IF NOT EXISTS entity_feature_migration_v5_audit (id INTEGER PRIMARY KEY AUTOINCREMENT, issue_type TEXT NOT NULL, detail TEXT NOT NULL);";
                    command.ExecuteNonQuery();
                }
            }
        }
        public static void Rebuild(string path, List<V5MigrationRecord> records, V5MigrationAudit audit)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("新版数据库地址不能为空。"); string full = Path.GetFullPath(path); string dir = Path.GetDirectoryName(full); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(full)) File.Copy(full, full + ".v5backup_" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), true);
            using (var connection = new SQLiteConnection("Data Source=" + full + ";Version=3;")) { connection.Open(); using (var command = connection.CreateCommand()) { command.CommandText = "DROP TABLE IF EXISTS entity_feature_migration_v5; DROP TABLE IF EXISTS entity_feature_migration_v5_audit; CREATE TABLE entity_feature_migration_v5 (id INTEGER PRIMARY KEY AUTOINCREMENT, old_number TEXT NOT NULL, feature_text TEXT NOT NULL, source_index INTEGER NOT NULL, source_file TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP); CREATE TABLE entity_feature_migration_v5_audit (id INTEGER PRIMARY KEY AUTOINCREMENT, issue_type TEXT NOT NULL, detail TEXT NOT NULL);"; command.ExecuteNonQuery(); using (var tx = connection.BeginTransaction()) { foreach (var r in records) { command.CommandText = "INSERT INTO entity_feature_migration_v5 (old_number, feature_text, source_index, source_file) VALUES (@n,@f,@i,@s);"; command.Parameters.Clear(); command.Parameters.AddWithValue("@n", r.Number); command.Parameters.AddWithValue("@f", r.Feature.Serialize()); command.Parameters.AddWithValue("@i", r.Index + 1); command.Parameters.AddWithValue("@s", r.SourceFile); command.ExecuteNonQuery(); } foreach (string x in audit.SamePartDifferentNumbers.Select(y => "同件异号|" + y).Concat(audit.DifferentPartsSameNumber.Select(y => "异件同号|" + y))) { string[] pair = x.Split(new[] { '|' }, 2); command.CommandText = "INSERT INTO entity_feature_migration_v5_audit (issue_type, detail) VALUES (@t,@d);"; command.Parameters.Clear(); command.Parameters.AddWithValue("@t", pair[0]); command.Parameters.AddWithValue("@d", pair[1]); command.ExecuteNonQuery(); } tx.Commit(); } } }
        }
        public static void Verify(string path, List<V5MigrationRecord> records)
        {
            string full = Path.GetFullPath(path);
            using (var connection = new SQLiteConnection("Data Source=" + full + ";Version=3;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT old_number, feature_text, source_index, source_file FROM entity_feature_migration_v5 ORDER BY source_index;";
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        int row = 0;
                        while (reader.Read())
                        {
                            if (row >= records.Count) throw new InvalidOperationException("数据库回读记录数多于待写入记录数。");
                            V5MigrationRecord expected = records[row];
                            string number = Convert.ToString(reader["old_number"], CultureInfo.InvariantCulture) ?? string.Empty;
                            string feature = Convert.ToString(reader["feature_text"], CultureInfo.InvariantCulture) ?? string.Empty;
                            int sourceIndex = Convert.ToInt32(reader["source_index"], CultureInfo.InvariantCulture);
                            string sourceFile = Convert.ToString(reader["source_file"], CultureInfo.InvariantCulture) ?? string.Empty;
                            if (!string.Equals(number, expected.Number, StringComparison.Ordinal) || !string.Equals(feature, expected.Feature.Serialize(), StringComparison.Ordinal) || sourceIndex != expected.Index + 1 || !string.Equals(Path.GetFullPath(sourceFile), Path.GetFullPath(expected.SourceFile), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("数据库回读校核失败，记录序号：" + (row + 1).ToString(CultureInfo.InvariantCulture));
                            row++;
                        }
                        if (row != records.Count) throw new InvalidOperationException("数据库回读记录数与待写入记录数不一致：" + row.ToString(CultureInfo.InvariantCulture) + "/" + records.Count.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
        }
    }

}
