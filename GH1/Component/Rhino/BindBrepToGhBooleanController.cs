using CommonFunction;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class BindBrepToGhBooleanController : GH_Component
    {
        private const string UserStringKey = "Parrot.GHBrepBinding";
        private const string SettingsChunk = "GHBrepBindingSettings";

        private string _mode = "Difference";
        private bool _lastRunInput;
        private readonly List<GeometryBase> _previewGeometry = new List<GeometryBase>();
        private BoundingBox _previewBox = BoundingBox.Empty;

        public BindBrepToGhBooleanController()
          : base("BindBrepToGhBooleanController", "实体绑定",
              "把Rhino实体绑定到当前GH电池，并按右键菜单中的布尔模式记录绑定关系",
              "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("实体Guid", "实体Guid", "要绑定到当前电池的Rhino Brep实体Guid", GH_ParamAccess.item);
            pManager.AddGenericParameter("对象", "对象", "参与布尔预览的GH生成对象，支持Brep/Surface/Extrusion", GH_ParamAccess.list);
            pManager.AddBooleanParameter("启用", "启用", "是否启用绑定记录", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("自动更新", "自动更新", "为True时，输入变化会自动写回Rhino实体绑定数据", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("运行", "运行", "由False变为True时手动写入绑定数据", GH_ParamAccess.item, false);

            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("绑定ID", "绑定ID", "写入Rhino实体的绑定ID", GH_ParamAccess.item);
            pManager.AddTextParameter("状态", "状态", "绑定状态", GH_ParamAccess.item);
            pManager.AddGeometryParameter("预览对象", "预览对象", "按当前模式计算得到的布尔预览对象", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            object entityInput = null;
            List<object> booleanInputs = new List<object>();
            bool enabled = true;
            bool autoUpdate = false;
            bool runInput = false;

            if (!DA.GetData(0, ref entityInput))
                return;

            DA.GetDataList(1, booleanInputs);
            DA.GetData(2, ref enabled);
            DA.GetData(3, ref autoUpdate);
            DA.GetData(4, ref runInput);

            bool runTriggered = runInput && !_lastRunInput;
            _lastRunInput = runInput;

            _previewGeometry.Clear();
            _previewBox = BoundingBox.Empty;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                DA.SetData(1, "当前没有可用的Rhino文档。");
                DA.SetDataList(2, _previewGeometry);
                return;
            }

            if (!TryGetRhinoObject(doc, entityInput, out RhinoObject entityObject) || entityObject == null)
            {
                DA.SetData(1, "实体Guid无效，或找不到对应的Rhino对象。");
                DA.SetDataList(2, _previewGeometry);
                return;
            }

            Brep entityBrep = GetBrepGeometry(entityObject.Geometry);
            if (entityBrep == null)
            {
                DA.SetData(1, "输入1对应的Rhino对象不是可用的Brep实体。");
                DA.SetDataList(2, _previewGeometry);
                return;
            }

            List<Brep> toolBreps = ExtractBreps(doc, booleanInputs);
            UpdatePreview(doc, entityBrep, toolBreps);

            BindingRecord record = BindingRecord.ReadFrom(entityObject);
            bool shouldWrite = autoUpdate || runTriggered;
            if (shouldWrite && string.IsNullOrWhiteSpace(record.BindingId))
                record.BindingId = "GB-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            record.ComponentInstanceGuid = InstanceGuid;
            record.ComponentGuid = ComponentGuid;
            record.Mode = _mode;
            record.Enabled = enabled;
            record.ToolCount = toolBreps.Count;
            record.UpdatedAt = DateTime.Now;

            string status = shouldWrite ? "绑定数据已写入。 " : "已读取绑定，等待运行或自动更新。 ";
            if (!shouldWrite && string.IsNullOrWhiteSpace(record.BindingId))
                status = "尚未写入绑定，等待运行或自动更新。 ";

            if (shouldWrite)
            {
                BindingRecord.WriteTo(entityObject, record);
                if (!entityObject.CommitChanges())
                    status = "绑定数据已更新，但提交Rhino对象属性失败。 ";
            }

            status += "模式：" + GetModeDisplayName(_mode) + "；对象：" + toolBreps.Count;

            DA.SetData(0, record.BindingId);
            DA.SetData(1, status);
            DA.SetDataList(2, _previewGeometry);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "差集", (sender, args) => SetMode("Difference"), true, string.Equals(_mode, "Difference", StringComparison.OrdinalIgnoreCase));
            Menu_AppendItem(menu, "并集", (sender, args) => SetMode("Union"), true, string.Equals(_mode, "Union", StringComparison.OrdinalIgnoreCase));
            Menu_AppendItem(menu, "交集", (sender, args) => SetMode("Intersection"), true, string.Equals(_mode, "Intersection", StringComparison.OrdinalIgnoreCase));
        }

        private void SetMode(string mode)
        {
            _mode = NormalizeMode(mode);
            ExpireSolution(true);
        }

        public string ModeDisplayText
        {
            get { return "模式：" + GetModeDisplayName(_mode); }
        }

        private void UpdatePreview(RhinoDoc doc, Brep entityBrep, List<Brep> toolBreps)
        {
            if (entityBrep == null)
                return;

            double tolerance = doc?.ModelAbsoluteTolerance ?? RhinoMath.SqrtEpsilon;
            Brep[] result = null;
            Brep target = entityBrep.DuplicateBrep();
            Brep[] tools = toolBreps.Select(item => item?.DuplicateBrep()).Where(item => item != null).ToArray();

            try
            {
                if (tools.Length == 0)
                {
                    result = new[] { target };
                }
                else if (string.Equals(_mode, "Union", StringComparison.OrdinalIgnoreCase))
                {
                    result = Brep.CreateBooleanUnion(new[] { target }.Concat(tools), tolerance);
                }
                else if (string.Equals(_mode, "Intersection", StringComparison.OrdinalIgnoreCase))
                {
                    result = Brep.CreateBooleanIntersection(new[] { target }, tools, tolerance);
                }
                else
                {
                    result = Brep.CreateBooleanDifference(new[] { target }, tools, tolerance);
                }
            }
            catch
            {
                result = null;
            }

            if (result == null || result.Length == 0)
                result = new[] { target };

            foreach (Brep brep in result)
            {
                if (brep == null)
                    continue;

                _previewGeometry.Add(brep);
                BoundingBox box = brep.GetBoundingBox(true);
                if (box.IsValid)
                    _previewBox.Union(box);
            }
        }

        private static List<Brep> ExtractBreps(RhinoDoc doc, IEnumerable<object> inputs)
        {
            List<Brep> result = new List<Brep>();
            if (inputs == null)
                return result;

            foreach (object input in inputs)
            {
                Brep brep = ExtractBrep(doc, input, 0);
                if (brep != null)
                    result.Add(brep);
            }

            return result;
        }

        private static Brep ExtractBrep(RhinoDoc doc, object input, int depth)
        {
            if (input == null || depth > 8)
                return null;

            if (input is GH_ObjectWrapper wrapper)
                return ExtractBrep(doc, wrapper.Value, depth + 1);

            if (input is GH_Brep ghBrep)
                return ghBrep.Value?.DuplicateBrep();

            if (input is IGH_GeometricGoo geometricGoo)
            {
                if (geometricGoo.ReferenceID != Guid.Empty)
                {
                    RhinoObject obj = doc?.Objects.FindId(geometricGoo.ReferenceID);
                    Brep referenced = GetBrepGeometry(obj?.Geometry);
                    if (referenced != null)
                        return referenced;
                }

                if (geometricGoo.ScriptVariable() is object scriptValue && !ReferenceEquals(scriptValue, input))
                    return ExtractBrep(doc, scriptValue, depth + 1);
            }

            if (input is IGH_Goo goo)
            {
                object scriptValue = goo.ScriptVariable();
                if (!ReferenceEquals(scriptValue, input))
                    return ExtractBrep(doc, scriptValue, depth + 1);
            }

            if (input is RhinoObject rhinoObject)
                return GetBrepGeometry(rhinoObject.Geometry);

            if (input is Guid guid)
                return GetBrepGeometry(doc?.Objects.FindId(guid)?.Geometry);

            if (input is string text && Guid.TryParse(text, out Guid textGuid))
                return GetBrepGeometry(doc?.Objects.FindId(textGuid)?.Geometry);

            if (input is GeometryBase geometry)
                return GetBrepGeometry(geometry);

            return null;
        }

        private static Brep GetBrepGeometry(GeometryBase geometry)
        {
            if (geometry is Brep brep)
                return brep.DuplicateBrep();
            if (geometry is Surface surface)
                return surface.ToBrep();
            if (geometry is Extrusion extrusion)
                return extrusion.ToBrep();

            return null;
        }

        private static bool TryGetRhinoObject(RhinoDoc doc, object input, out RhinoObject obj, int depth = 0)
        {
            obj = null;
            if (doc == null || input == null || depth > 8)
                return false;

            if (input is GH_ObjectWrapper wrapper)
                return TryGetRhinoObject(doc, wrapper.Value, out obj, depth + 1);

            if (input is RhinoObject rhinoObject)
            {
                obj = rhinoObject;
                return true;
            }

            if (input is GH_Guid ghGuid)
            {
                obj = doc.Objects.FindId(ghGuid.Value);
                return obj != null;
            }

            if (input is Guid guid)
            {
                obj = doc.Objects.FindId(guid);
                return obj != null;
            }

            if (input is IGH_GeometricGoo geometricGoo && geometricGoo.ReferenceID != Guid.Empty)
            {
                obj = doc.Objects.FindId(geometricGoo.ReferenceID);
                return obj != null;
            }

            if (input is IGH_Goo goo)
            {
                object scriptValue = goo.ScriptVariable();
                if (!ReferenceEquals(scriptValue, input))
                    return TryGetRhinoObject(doc, scriptValue, out obj, depth + 1);
            }

            if (input is string text && Guid.TryParse(text, out Guid textGuid))
            {
                obj = doc.Objects.FindId(textGuid);
                return obj != null;
            }

            return false;
        }

        private static string NormalizeMode(string mode)
        {
            if (string.Equals(mode, "并集", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Union", StringComparison.OrdinalIgnoreCase))
                return "Union";
            if (string.Equals(mode, "交集", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Intersection", StringComparison.OrdinalIgnoreCase))
                return "Intersection";

            return "Difference";
        }

        private static string GetModeDisplayName(string mode)
        {
            if (string.Equals(mode, "Union", StringComparison.OrdinalIgnoreCase))
                return "并集";
            if (string.Equals(mode, "Intersection", StringComparison.OrdinalIgnoreCase))
                return "交集";

            return "差集";
        }

        public override bool Write(GH_IWriter writer)
        {
            GH_IWriter chunk = writer.CreateChunk(SettingsChunk);
            chunk.SetString("Mode", _mode);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            GH_IReader chunk = reader.FindChunk(SettingsChunk);
            if (chunk != null)
            {
                string mode = _mode;
                if (chunk.TryGetString("Mode", ref mode))
                    _mode = NormalizeMode(mode);
            }

            return base.Read(reader);
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox box = base.ClippingBox;
                if (_previewBox.IsValid)
                    box.Union(_previewBox);
                return box;
            }
        }

        public override bool IsPreviewCapable => true;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (Hidden || Locked)
                return;

            Color color = Attributes?.Selected == true ? args.WireColour_Selected : Color.FromArgb(220, 0, 160, 220);
            foreach (GeometryBase geometry in _previewGeometry)
            {
                if (geometry is Brep brep)
                    args.Display.DrawBrepWires(brep, color, 2);
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);

            if (Hidden || Locked)
                return;

            Color color = Color.FromArgb(70, 0, 160, 220);
            foreach (GeometryBase geometry in _previewGeometry)
            {
                if (geometry is Brep brep)
                    args.Display.DrawBrepShaded(brep, new DisplayMaterial(color));
            }
        }

        public override void CreateAttributes()
        {
            Attributes = new CAttributes_BindBrepToGhBooleanController(this);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_BindBrepToGhBooleanController"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("4C22B9AE-9F04-48C0-BED6-67C65CA2963C"); }
        }

        private sealed class BindingRecord
        {
            public string BindingId { get; set; } = string.Empty;
            public Guid ComponentGuid { get; set; } = Guid.Empty;
            public Guid ComponentInstanceGuid { get; set; } = Guid.Empty;
            public string Mode { get; set; } = "Difference";
            public bool Enabled { get; set; } = true;
            public int ToolCount { get; set; }
            public DateTime UpdatedAt { get; set; } = DateTime.Now;

            public static BindingRecord ReadFrom(RhinoObject obj)
            {
                BindingRecord record = new BindingRecord();
                string text = obj?.Attributes.GetUserString(UserStringKey) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    return record;

                string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (string line in lines)
                {
                    int separator = line.IndexOf('=');
                    if (separator < 0)
                        continue;

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();

                    if (string.Equals(key, "BindingId", StringComparison.OrdinalIgnoreCase))
                        record.BindingId = value;
                    else if (string.Equals(key, "ComponentGuid", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value, out Guid componentGuid))
                        record.ComponentGuid = componentGuid;
                    else if (string.Equals(key, "ComponentInstanceGuid", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value, out Guid instanceGuid))
                        record.ComponentInstanceGuid = instanceGuid;
                    else if (string.Equals(key, "Mode", StringComparison.OrdinalIgnoreCase))
                        record.Mode = NormalizeMode(value);
                    else if (string.Equals(key, "Enabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out bool enabled))
                        record.Enabled = enabled;
                    else if (string.Equals(key, "ToolCount", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int toolCount))
                        record.ToolCount = toolCount;
                    else if (string.Equals(key, "UpdatedAt", StringComparison.OrdinalIgnoreCase) && DateTime.TryParse(value, out DateTime updatedAt))
                        record.UpdatedAt = updatedAt;
                }

                return record;
            }

            public static void WriteTo(RhinoObject obj, BindingRecord record)
            {
                if (obj == null || record == null)
                    return;

                string text =
                    "v1\n" +
                    "BindingId=" + record.BindingId + "\n" +
                    "ComponentGuid=" + record.ComponentGuid + "\n" +
                    "ComponentInstanceGuid=" + record.ComponentInstanceGuid + "\n" +
                    "Mode=" + NormalizeMode(record.Mode) + "\n" +
                    "Enabled=" + record.Enabled + "\n" +
                    "ToolCount=" + record.ToolCount + "\n" +
                    "UpdatedAt=" + record.UpdatedAt.ToString("O");

                obj.Attributes.SetUserString(UserStringKey, text);
            }
        }
    }

    internal sealed class CAttributes_BindBrepToGhBooleanController : GH_ComponentAttributes
    {
        private const float TextHeight = 18.0f;

        public CAttributes_BindBrepToGhBooleanController(BindBrepToGhBooleanController owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + TextHeight);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            BindBrepToGhBooleanController owner = (BindBrepToGhBooleanController)Owner;
            RectangleF textRect = new RectangleF(Bounds.X + 3.0f, Bounds.Bottom - TextHeight + 1.0f, Bounds.Width - 6.0f, TextHeight - 2.0f);

            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(owner.ModeDisplayText, GH_FontServer.Small, Brushes.DimGray, textRect, format);
            }
        }
    }
}
