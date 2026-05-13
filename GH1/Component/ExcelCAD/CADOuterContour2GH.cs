using AutoCADFunction;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using parrot.Properties;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using Rhino.Runtime;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class CADOuterContour2GH : GH_Component
    {
        private const string PersistenceChunk = "CadOuterContourCache";
        private const int PersistenceVersion = 1;

        private List<CADOuterContourResult> _cadResults = new List<CADOuterContourResult>();
        private HashSet<string> _handleSet = new HashSet<string>();
        private int _pendingUiRefresh = 0;

        public enum ButtonColor { Black, Grey }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        public string LayerName { get; private set; } = "AutoCADOuterContour";
        public List<OuterContourBakeItem> BakeItems { get; } = new List<OuterContourBakeItem>();

        public CADOuterContour2GH()
          : base("CADOuterContour2GH", "CADOuterContour",
              "从CAD面域、多段线或图块中提取闭合外轮廓并导入为GH Surface",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("pt", "pt", "CAD中的基点", GH_ParamAccess.item, Point3d.Origin);

            Plane plane = new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis);
            pManager.AddPlaneParameter("PL", "PL", "Rhino中的局部坐标平面", GH_ParamAccess.item, plane);
            pManager.AddTextParameter("Layer", "La", "Bake的目标图层", GH_ParamAccess.item, "AutoCADOuterContour");

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Surface", "S", "由CAD外轮廓生成的Surface", GH_ParamAccess.list);
            pManager.AddTextParameter("图层", "图层", "CAD图层", GH_ParamAccess.list);
            pManager.AddColourParameter("颜色", "颜色", "CAD颜色", GH_ParamAccess.list);
            pManager.AddTextParameter("线型", "线型", "CAD线型", GH_ParamAccess.list);
            pManager.AddTextParameter("句柄", "句柄", "CAD句柄", GH_ParamAccess.list);
            pManager.AddNumberParameter("长", "长", "外轮廓长度，保留1位小数", GH_ParamAccess.list);
            pManager.AddNumberParameter("宽", "宽", "外轮廓宽度，保留1位小数", GH_ParamAccess.list);
            pManager.AddTextParameter("文件路径", "文件路径", "导入对象所在的CAD完整文件路径", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            BakeItems.Clear();

            Point3d insert = Point3d.Origin;
            DA.GetData(0, ref insert);

            Plane targetPlane = new Plane(insert, Vector3d.XAxis, Vector3d.YAxis);
            DA.GetData(1, ref targetPlane);

            string layerName = LayerName;
            DA.GetData(2, ref layerName);
            LayerName = layerName;

            Plane cadPlane = new Plane(insert, Vector3d.XAxis, Vector3d.YAxis);
            Transform xform = Transform.PlaneToPlane(cadPlane, targetPlane);

            List<Brep> surfaces = new List<Brep>();
            List<string> layers = new List<string>();
            List<Color> colors = new List<Color>();
            List<string> lineTypes = new List<string>();
            List<string> handles = new List<string>();
            List<double> lengths = new List<double>();
            List<double> widths = new List<double>();
            List<string> errors = new List<string>();

            foreach (CADOuterContourResult result in _cadResults)
            {
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                    errors.Add($"Handle={result.Handle} : {result.ErrorMessage}");

                if (result.Surface == null || !result.Surface.IsValid)
                    continue;

                Brep brep = result.Surface.DuplicateBrep();
                brep.Transform(xform);

                double length;
                double width;
                GetLengthWidth(brep, targetPlane, out length, out width);

                surfaces.Add(brep);
                layers.Add(result.Layer ?? string.Empty);
                colors.Add(result.Color.IsEmpty ? Color.White : result.Color);
                lineTypes.Add(result.LineType ?? string.Empty);
                handles.Add(result.Handle ?? string.Empty);
                lengths.Add(length);
                widths.Add(width);

                BakeItems.Add(new OuterContourBakeItem
                {
                    Surface = brep,
                    Layer = result.Layer ?? string.Empty,
                    LineType = result.LineType ?? string.Empty,
                    Handle = result.Handle ?? string.Empty,
                    Color = result.Color,
                    Length = length,
                    Width = width
                });
            }

            if (errors.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", errors));

            DA.SetDataList(0, surfaces);
            DA.SetDataList(1, layers);
            DA.SetDataList(2, colors);
            DA.SetDataList(3, lineTypes);
            DA.SetDataList(4, handles);
            DA.SetDataList(5, lengths);
            DA.SetDataList(6, widths);
            DA.SetData(7, GetFilePath(_cadResults));
        }

        private static void GetLengthWidth(Brep brep, Plane targetPlane, out double length, out double width)
        {
            Brep copy = brep.DuplicateBrep();
            Transform toWorldXY = Transform.PlaneToPlane(targetPlane, Plane.WorldXY);
            copy.Transform(toWorldXY);

            BoundingBox box = copy.GetBoundingBox(true);
            double dx = Math.Abs(box.Max.X - box.Min.X);
            double dy = Math.Abs(box.Max.Y - box.Min.Y);

            length = Math.Round(Math.Max(dx, dy), 1);
            width = Math.Round(Math.Min(dx, dy), 1);
        }

        protected override Bitmap Icon => Resources.Cad2Rhino;

        public override void CreateAttributes()
        {
            Attributes = new CButton_CADOuterContourBake(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            ToolStripMenuItem item0 = new ToolStripMenuItem();
            item0.Text = "连接CAD";
            item0.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero);
            menu.Items.Add(item0);
            item0.Click += ConnectAutoCAD;

            ToolStripMenuItem item1 = new ToolStripMenuItem();
            item1.Text = "获取CAD外轮廓";
            item1.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero);
            menu.Items.Add(item1);
            item1.Click += GetEntityFromAutoCAD;

            ToolStripMenuItem item2 = new ToolStripMenuItem();
            item2.Text = "加选";
            item2.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero);
            menu.Items.Add(item2);
            item2.Click += AddEntity;

            ToolStripMenuItem item3 = new ToolStripMenuItem();
            item3.Text = "减选";
            item3.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero);
            menu.Items.Add(item3);
            item3.Click += RemoveEntity;

            ToolStripMenuItem item4 = new ToolStripMenuItem();
            item4.Text = "清空";
            item4.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero);
            menu.Items.Add(item4);
            item4.Click += ClearEntity;
        }

        private void ConnectAutoCAD(object sender, EventArgs e)
        {
            AutoCADTool.ConnectCAD();
        }

        private void GetEntityFromAutoCAD(object sender, EventArgs e)
        {
            AutoCADTool.CADOuterContour2GH((res) =>
            {
                SetCadResults(res);
                RequestSafeUiRefresh();
            });
        }

        private void AddEntity(object sender, EventArgs e)
        {
            AutoCADTool.CADOuterContour2GH((value) =>
            {
                foreach (CADOuterContourResult v in value)
                {
                    string handle = v.Handle;
                    if (string.IsNullOrWhiteSpace(handle))
                        continue;

                    if (_handleSet.Add(handle))
                        _cadResults.Add(v);
                }

                RequestSafeUiRefresh();
            });
        }

        private void RemoveEntity(object sender, EventArgs e)
        {
            AutoCADTool.CADOuterContour2GH((value) =>
            {
                HashSet<string> removeSet = new HashSet<string>(
                    value.Select(v => v.Handle).Where(h => !string.IsNullOrWhiteSpace(h)));

                _cadResults = _cadResults
                    .Where(r => !removeSet.Contains(r.Handle))
                    .ToList();

                RebuildHandleSet();
                RequestSafeUiRefresh();
            });
        }

        private void ClearEntity(object sender, EventArgs e)
        {
            _cadResults.Clear();
            _handleSet.Clear();
            ExpireSolution(true);
        }

        private void RequestSafeUiRefresh()
        {
            if (Interlocked.Exchange(ref _pendingUiRefresh, 1) == 1)
                return;

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = 80;
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    timer.Dispose();

                    try
                    {
                        Rhino.RhinoApp.Wait();
                        Application.DoEvents();
                        Rhino.RhinoDoc.ActiveDoc?.Views.Redraw();
                        Rhino.RhinoApp.Wait();
                        ExpireSolution(true);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _pendingUiRefresh, 0);
                    }
                };
                timer.Start();
            }));
        }

        private void SetCadResults(IEnumerable<CADOuterContourResult> results)
        {
            _cadResults = results?.ToList() ?? new List<CADOuterContourResult>();
            RebuildHandleSet();
        }

        private void RebuildHandleSet()
        {
            _handleSet = new HashSet<string>(
                _cadResults
                    .Select(r => r?.Handle)
                    .Where(h => !string.IsNullOrWhiteSpace(h)));
        }

        private static string GetFilePath(IEnumerable<CADOuterContourResult> results)
        {
            return results?
                .Select(r => r?.FileName)
                .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f)) ?? string.Empty;
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetInt32("PersistenceVersion", PersistenceVersion);

            GH_IWriter cacheChunk = writer.CreateChunk(PersistenceChunk);
            cacheChunk.SetInt32("Count", _cadResults.Count);

            for (int i = 0; i < _cadResults.Count; i++)
            {
                GH_IWriter itemChunk = cacheChunk.CreateChunk("Item", i);
                WriteOuterContourResult(itemChunk, _cadResults[i]);
            }

            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            _cadResults.Clear();
            _handleSet.Clear();

            if (reader.FindChunk(PersistenceChunk) is GH_IReader cacheChunk)
            {
                int count = 0;
                cacheChunk.TryGetInt32("Count", ref count);

                List<CADOuterContourResult> restored = new List<CADOuterContourResult>();
                for (int i = 0; i < count; i++)
                {
                    GH_IReader itemChunk = cacheChunk.FindChunk("Item", i);
                    if (itemChunk == null)
                        continue;

                    CADOuterContourResult item = ReadOuterContourResult(itemChunk);
                    if (item != null)
                        restored.Add(item);
                }

                SetCadResults(restored);
            }

            return base.Read(reader);
        }

        private static void WriteOuterContourResult(GH_IWriter writer, CADOuterContourResult result)
        {
            writer.SetString("Layer", result.Layer ?? string.Empty);
            writer.SetDrawingColor("Color", result.Color.IsEmpty ? Color.White : result.Color);
            writer.SetString("LineType", result.LineType ?? string.Empty);
            writer.SetString("Handle", result.Handle ?? string.Empty);
            writer.SetString("ErrorMessage", result.ErrorMessage ?? string.Empty);
            writer.SetString("FileName", result.FileName ?? string.Empty);

            if (result.Surface != null)
            {
                var options = new SerializationOptions();
                writer.SetString("SurfaceJson", result.Surface.ToJSON(options));
            }
        }

        private static CADOuterContourResult ReadOuterContourResult(GH_IReader reader)
        {
            string layer = string.Empty;
            string lineType = string.Empty;
            string handle = string.Empty;
            string errorMessage = string.Empty;
            string fileName = string.Empty;
            string surfaceJson = string.Empty;
            Color color = Color.White;

            reader.TryGetString("Layer", ref layer);
            reader.TryGetDrawingColor("Color", ref color);
            reader.TryGetString("LineType", ref lineType);
            reader.TryGetString("Handle", ref handle);
            reader.TryGetString("ErrorMessage", ref errorMessage);
            reader.TryGetString("FileName", ref fileName);
            reader.TryGetString("SurfaceJson", ref surfaceJson);

            Brep brep = null;
            if (!string.IsNullOrWhiteSpace(surfaceJson))
            {
                CommonObject commonObject = CommonObject.FromJSON(surfaceJson);
                brep = commonObject as Brep;
            }

            return new CADOuterContourResult(brep, layer, color, lineType, handle, errorMessage, fileName);
        }

        public override Guid ComponentGuid => new Guid("05C1B590-C52B-4D2E-A282-3C8FF8CE540C");
    }

    public class OuterContourBakeItem
    {
        public Brep Surface { get; set; }
        public string Layer { get; set; }
        public string LineType { get; set; }
        public string Handle { get; set; }
        public Color Color { get; set; }
        public double Length { get; set; }
        public double Width { get; set; }
    }

    internal class CButton_CADOuterContourBake : GH_ComponentAttributes
    {
        public CButton_CADOuterContourBake(CADOuterContour2GH component) : base(component) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 20.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);

            if (channel == GH_CanvasChannel.Objects)
            {
                GH_Palette palette = ((CADOuterContour2GH)Owner).CurrentButtonColor == CADOuterContour2GH.ButtonColor.Black
                    ? GH_Palette.Black
                    : GH_Palette.Grey;

                using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                {
                    capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                }
            }

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat stringFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("Bake", font, Brushes.White, buttonRect, stringFormat);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                CADOuterContour2GH info = (CADOuterContour2GH)Owner;
                info.CurrentButtonColor = CADOuterContour2GH.ButtonColor.Grey;
                info.ExpireSolution(true);
                Thread.Sleep(50);
                info.CurrentButtonColor = CADOuterContour2GH.ButtonColor.Black;
                Bake(info);
                info.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }

        private static void Bake(CADOuterContour2GH info)
        {
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            if (doc == null)
                return;

            int layerIndex = doc.Layers.FindByFullPath(info.LayerName, -1);
            if (layerIndex == -1)
                layerIndex = doc.Layers.Add(info.LayerName, Color.Black);

            List<string> errors = new List<string>();

            for (int i = 0; i < info.BakeItems.Count; i++)
            {
                OuterContourBakeItem item = info.BakeItems[i];

                try
                {
                    ObjectAttributes attributes = doc.CreateDefaultAttributes();
                    attributes.LayerIndex = layerIndex;
                    attributes.SetUserString("CADHandle", item.Handle ?? string.Empty);
                    attributes.SetUserString("长", item.Length.ToString("0.0"));
                    attributes.SetUserString("宽", item.Width.ToString("0.0"));

                    Guid id = doc.Objects.AddBrep(item.Surface, attributes);
                    if (id == Guid.Empty)
                        errors.Add($"[{i}] Bake失败（ID为空）");
                }
                catch (Exception ex)
                {
                    errors.Add($"[{i}] 异常: {ex.Message}");
                }
            }

            if (errors.Count > 0)
                info.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", errors));

            doc.Views.Redraw();
        }
    }
}
