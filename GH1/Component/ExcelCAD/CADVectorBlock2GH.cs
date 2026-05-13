using AutoCADFunction;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using parrot.Properties;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class CADVectorBlock2GH : GH_Component
    {
        private const string PersistenceChunk = "CadVectorBlockCache";
        private const int PersistenceVersion = 1;

        private List<CADVectorBlockResult> _cadResults = new List<CADVectorBlockResult>();
        private HashSet<string> _handleSet = new HashSet<string>();
        private int _pendingUiRefresh = 0;

        public enum ButtonColor { Black, Grey }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        public string LayerName { get; private set; } = "AutoCADVector";
        public List<VectorBlockBakeItem> BakeItems { get; } = new List<VectorBlockBakeItem>();

        public CADVectorBlock2GH()
          : base("CADVectorBlock2GH", "CADVectorBlock",
              "导入CAD属性块中的代表向量线，并读取规格、间距、材质",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("pt", "pt", "CAD中的基点", GH_ParamAccess.item, Point3d.Origin);

            Plane plane = new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis);
            pManager.AddPlaneParameter("PL", "PL", "Rhino中的局部坐标平面", GH_ParamAccess.item, plane);
            pManager.AddTextParameter("Layer", "La", "Bake的目标图层", GH_ParamAccess.item, "AutoCADVector");

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("代表向量", "Line", "块中直线的起点到终点代表向量位置、方向和长度", GH_ParamAccess.list);
            pManager.AddTextParameter("规格", "规格", "属性块中的规格", GH_ParamAccess.list);
            pManager.AddTextParameter("间距", "间距", "属性块中的间距", GH_ParamAccess.list);
            pManager.AddTextParameter("材质", "材质", "属性块中的材质", GH_ParamAccess.list);
            pManager.AddTextParameter("句柄", "句柄", "CAD块句柄", GH_ParamAccess.list);
            pManager.AddTextParameter("文件名", "文件名", "导入对象所在的CAD文件名", GH_ParamAccess.item);
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

            List<Line> lines = new List<Line>();
            List<string> specs = new List<string>();
            List<string> spacings = new List<string>();
            List<string> materials = new List<string>();
            List<string> handles = new List<string>();
            List<string> errors = new List<string>();

            foreach (CADVectorBlockResult result in _cadResults)
            {
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                    errors.Add($"Handle={result.Handle} : {result.ErrorMessage}");

                if (!result.VectorLine.IsValid || result.VectorLine.Length <= Rhino.RhinoMath.ZeroTolerance)
                    continue;

                Line line = result.VectorLine;
                line.Transform(xform);

                lines.Add(line);
                specs.Add(result.Spec ?? string.Empty);
                spacings.Add(result.Spacing ?? string.Empty);
                materials.Add(result.Material ?? string.Empty);
                handles.Add(result.Handle ?? string.Empty);

                BakeItems.Add(new VectorBlockBakeItem
                {
                    Line = line,
                    Spec = result.Spec ?? string.Empty,
                    Spacing = result.Spacing ?? string.Empty,
                    Material = result.Material ?? string.Empty,
                    Handle = result.Handle ?? string.Empty,
                    BlockName = result.BlockName ?? string.Empty,
                    Layer = result.Layer ?? string.Empty,
                    LineType = result.LineType ?? string.Empty,
                    Color = result.Color
                });
            }

            if (errors.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", errors));

            DA.SetDataList(0, lines);
            DA.SetDataList(1, specs);
            DA.SetDataList(2, spacings);
            DA.SetDataList(3, materials);
            DA.SetDataList(4, handles);
            DA.SetData(5, GetFileName(_cadResults));
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            foreach (VectorBlockBakeItem item in BakeItems)
            {
                DrawArrowPreview(args, item.Line, item.Color.IsEmpty ? Color.DarkOrange : item.Color);
                DrawAttributePreview(args, item);
            }
        }

        private static void DrawAttributePreview(IGH_PreviewArgs args, VectorBlockBakeItem item)
        {
            if (!item.Line.IsValid)
                return;

            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.Spec))
                parts.Add("规格: " + item.Spec);
            if (!string.IsNullOrWhiteSpace(item.Spacing))
                parts.Add("间距: " + item.Spacing);
            if (!string.IsNullOrWhiteSpace(item.Material))
                parts.Add("材质: " + item.Material);

            if (parts.Count == 0)
                return;

            Point3d location = item.Line.PointAt(0.5);
            string text = string.Join("  ", parts);
            args.Display.Draw2dText(text, Color.DarkOrange, location, false, 14);
        }

        private static void DrawArrowPreview(IGH_PreviewArgs args, Line line, Color color)
        {
            if (!line.IsValid || line.Length <= Rhino.RhinoMath.ZeroTolerance)
                return;

            args.Display.DrawLine(line, color, 2);

            Vector3d direction = line.Direction;
            if (!direction.Unitize())
                return;

            Vector3d side = Vector3d.CrossProduct(Vector3d.ZAxis, direction);
            if (!side.Unitize())
            {
                side = Vector3d.CrossProduct(Vector3d.XAxis, direction);
                if (!side.Unitize())
                    return;
            }

            double size = Math.Max(line.Length * 0.12, 1.0);
            size = Math.Min(size, Math.Max(line.Length * 0.35, 1.0));

            Point3d tip = line.To;
            Point3d left = tip - direction * size + side * size * 0.45;
            Point3d right = tip - direction * size - side * size * 0.45;

            args.Display.DrawLine(new Line(tip, left), color, 2);
            args.Display.DrawLine(new Line(tip, right), color, 2);
        }

        protected override Bitmap Icon => Resources.Cad2Rhino;

        public override void CreateAttributes()
        {
            Attributes = new CButton_CADVectorBlockBake(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            ToolStripMenuItem item0 = new ToolStripMenuItem();
            item0.Text = "连接CAD";
            item0.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero);
            menu.Items.Add(item0);
            item0.Click += ConnectAutoCAD;

            ToolStripMenuItem item1 = new ToolStripMenuItem();
            item1.Text = "获取CAD属性块";
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
            AutoCADTool.CADVectorBlock2GH((res) =>
            {
                SetCadResults(res);
                RequestSafeUiRefresh();
            });
        }

        private void AddEntity(object sender, EventArgs e)
        {
            AutoCADTool.CADVectorBlock2GH((value) =>
            {
                foreach (CADVectorBlockResult v in value)
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
            AutoCADTool.CADVectorBlock2GH((value) =>
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

        private void SetCadResults(IEnumerable<CADVectorBlockResult> results)
        {
            _cadResults = results?.ToList() ?? new List<CADVectorBlockResult>();
            RebuildHandleSet();
        }

        private void RebuildHandleSet()
        {
            _handleSet = new HashSet<string>(
                _cadResults
                    .Select(r => r?.Handle)
                    .Where(h => !string.IsNullOrWhiteSpace(h)));
        }

        private static string GetFileName(IEnumerable<CADVectorBlockResult> results)
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
                WriteVectorBlockResult(itemChunk, _cadResults[i]);
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

                List<CADVectorBlockResult> restored = new List<CADVectorBlockResult>();
                for (int i = 0; i < count; i++)
                {
                    GH_IReader itemChunk = cacheChunk.FindChunk("Item", i);
                    if (itemChunk == null)
                        continue;

                    restored.Add(ReadVectorBlockResult(itemChunk));
                }

                SetCadResults(restored);
            }

            return base.Read(reader);
        }

        private static void WriteVectorBlockResult(GH_IWriter writer, CADVectorBlockResult result)
        {
            writer.SetDouble("FromX", result.VectorLine.From.X);
            writer.SetDouble("FromY", result.VectorLine.From.Y);
            writer.SetDouble("FromZ", result.VectorLine.From.Z);
            writer.SetDouble("ToX", result.VectorLine.To.X);
            writer.SetDouble("ToY", result.VectorLine.To.Y);
            writer.SetDouble("ToZ", result.VectorLine.To.Z);
            writer.SetString("Spec", result.Spec ?? string.Empty);
            writer.SetString("Spacing", result.Spacing ?? string.Empty);
            writer.SetString("Material", result.Material ?? string.Empty);
            writer.SetString("Layer", result.Layer ?? string.Empty);
            writer.SetDrawingColor("Color", result.Color.IsEmpty ? Color.White : result.Color);
            writer.SetString("LineType", result.LineType ?? string.Empty);
            writer.SetString("Handle", result.Handle ?? string.Empty);
            writer.SetString("BlockName", result.BlockName ?? string.Empty);
            writer.SetString("ErrorMessage", result.ErrorMessage ?? string.Empty);
            writer.SetString("FileName", result.FileName ?? string.Empty);
        }

        private static CADVectorBlockResult ReadVectorBlockResult(GH_IReader reader)
        {
            Line line = new Line(
                new Point3d(reader.GetDouble("FromX"), reader.GetDouble("FromY"), reader.GetDouble("FromZ")),
                new Point3d(reader.GetDouble("ToX"), reader.GetDouble("ToY"), reader.GetDouble("ToZ")));

            string spec = string.Empty;
            string spacing = string.Empty;
            string material = string.Empty;
            string layer = string.Empty;
            string lineType = string.Empty;
            string handle = string.Empty;
            string blockName = string.Empty;
            string errorMessage = string.Empty;
            string fileName = string.Empty;
            Color color = Color.White;

            reader.TryGetString("Spec", ref spec);
            reader.TryGetString("Spacing", ref spacing);
            reader.TryGetString("Material", ref material);
            reader.TryGetString("Layer", ref layer);
            reader.TryGetDrawingColor("Color", ref color);
            reader.TryGetString("LineType", ref lineType);
            reader.TryGetString("Handle", ref handle);
            reader.TryGetString("BlockName", ref blockName);
            reader.TryGetString("ErrorMessage", ref errorMessage);
            reader.TryGetString("FileName", ref fileName);

            return new CADVectorBlockResult(line, spec, spacing, material, layer, color, lineType, handle, blockName, errorMessage, fileName);
        }

        public override Guid ComponentGuid => new Guid("2F3124AF-471B-4C14-BFD0-413813C7FE36");
    }

    public class VectorBlockBakeItem
    {
        public Line Line { get; set; }
        public string Spec { get; set; }
        public string Spacing { get; set; }
        public string Material { get; set; }
        public string Handle { get; set; }
        public string BlockName { get; set; }
        public string Layer { get; set; }
        public string LineType { get; set; }
        public Color Color { get; set; }
    }

    internal class CButton_CADVectorBlockBake : GH_ComponentAttributes
    {
        public CButton_CADVectorBlockBake(CADVectorBlock2GH component) : base(component) { }

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
                GH_Palette palette = ((CADVectorBlock2GH)Owner).CurrentButtonColor == CADVectorBlock2GH.ButtonColor.Black
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
                CADVectorBlock2GH info = (CADVectorBlock2GH)Owner;
                info.CurrentButtonColor = CADVectorBlock2GH.ButtonColor.Grey;
                info.ExpireSolution(true);
                Thread.Sleep(50);
                info.CurrentButtonColor = CADVectorBlock2GH.ButtonColor.Black;
                Bake(info);
                info.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }

        private static void Bake(CADVectorBlock2GH info)
        {
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            if (doc == null)
                return;

            string layerName = info.LayerName;
            int layerIndex = doc.Layers.FindByFullPath(layerName, -1);
            if (layerIndex == -1)
                layerIndex = doc.Layers.Add(layerName, Color.Black);

            List<string> errors = new List<string>();

            for (int i = 0; i < info.BakeItems.Count; i++)
            {
                VectorBlockBakeItem item = info.BakeItems[i];

                try
                {
                    ObjectAttributes attributes = doc.CreateDefaultAttributes();
                    attributes.LayerIndex = layerIndex;
                    attributes.SetUserString("规格", item.Spec ?? string.Empty);
                    attributes.SetUserString("间距", item.Spacing ?? string.Empty);
                    attributes.SetUserString("材质", item.Material ?? string.Empty);
                    attributes.SetUserString("CADHandle", item.Handle ?? string.Empty);
                    attributes.SetUserString("BlockName", item.BlockName ?? string.Empty);

                    Guid id = doc.Objects.AddLine(item.Line, attributes);
                    if (id == Guid.Empty)
                        errors.Add($"[{i}] Bake失败（ID为空）");
                }
                catch (Exception ex)
                {
                    errors.Add($"[{i}] 异常: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                info.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", errors));
            }

            doc.Views.Redraw();
        }
    }
}
