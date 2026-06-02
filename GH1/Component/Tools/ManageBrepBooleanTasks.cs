using CommonFunction;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace NS_Parrot
{
    public class ManageBrepBooleanTasks : GH_Component
    {
        private const string UserStringKey = "Parrot.BooleanTasks";

        private bool _lastRunInput;
        private readonly List<GeometryBase> _previewGeometry = new List<GeometryBase>();
        private BoundingBox _previewBox = BoundingBox.Empty;

        public ManageBrepBooleanTasks()
          : base("ManageBrepBooleanTasks", "布尔任务绑定",
              "在主实体上记录块颜色布尔任务，并预览参与布尔运算的对象",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("实体Guid", "实体Guid", "要记录布尔任务的主实体Guid", GH_ParamAccess.item);
            pManager.AddTextParameter("操作", "操作", "Add/Update/Delete/Clear/List", GH_ParamAccess.item, "List");
            pManager.AddTextParameter("任务ID", "任务ID", "修改或删除指定任务；新增为空时自动生成", GH_ParamAccess.item, string.Empty);
            pManager.AddGenericParameter("块Guid", "块Guid", "参与布尔的块实例Guid列表", GH_ParamAccess.list);
            pManager.AddTextParameter("颜色条件", "颜色条件", "块定义中参与布尔对象的颜色条件，如 R=50、G=120、B=200", GH_ParamAccess.list);
            pManager.AddTextParameter("模式", "模式", "Difference/Union/Intersection", GH_ParamAccess.item, "Difference");
            pManager.AddBooleanParameter("启用", "启用", "是否启用该任务", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("递归", "递归", "是否递归查找嵌套块", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("运行", "运行", "由False变为True时执行增删改操作；List无需运行", GH_ParamAccess.item, false);

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("任务ID", "任务ID", "本次影响的任务ID", GH_ParamAccess.item);
            pManager.AddTextParameter("任务清单", "任务清单", "当前实体上的全部布尔任务摘要", GH_ParamAccess.list);
            pManager.AddGeometryParameter("预览对象", "预览对象", "当前已绑定任务对应的实际布尔对象", GH_ParamAccess.list);
            pManager.AddTextParameter("状态", "状态", "操作状态", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            object entityInput = null;
            string operation = "List";
            string taskId = string.Empty;
            List<object> blockInputs = new List<object>();
            List<string> colorRules = new List<string>();
            string mode = "Difference";
            bool enabled = true;
            bool recursive = true;
            bool runInput = false;

            if (!DA.GetData(0, ref entityInput))
                return;

            DA.GetData(1, ref operation);
            DA.GetData(2, ref taskId);
            DA.GetDataList(3, blockInputs);
            DA.GetDataList(4, colorRules);
            DA.GetData(5, ref mode);
            DA.GetData(6, ref enabled);
            DA.GetData(7, ref recursive);
            DA.GetData(8, ref runInput);

            bool runTriggered = runInput && !_lastRunInput;
            _lastRunInput = runInput;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                DA.SetData(3, "当前没有可用的Rhino文档。");
                return;
            }

            if (!TryGetGuid(entityInput, out Guid entityId) || entityId == Guid.Empty)
            {
                DA.SetData(3, "实体Guid无效。");
                return;
            }

            RhinoObject entityObject = doc.Objects.FindId(entityId);
            if (entityObject == null)
            {
                DA.SetData(3, "找不到实体Guid对应的Rhino对象。");
                return;
            }

            List<BooleanTaskRecord> tasks = BooleanTaskRecord.ReadFrom(entityObject);
            string normalizedOperation = (operation ?? "List").Trim();
            string affectedTaskId = taskId ?? string.Empty;
            string status = "已读取任务。";

            try
            {
                if (runTriggered)
                {
                    List<Guid> blockIds = blockInputs
                        .Select(input => TryGetGuid(input, out Guid guid) ? guid : Guid.Empty)
                        .Where(guid => guid != Guid.Empty)
                        .Distinct()
                        .ToList();
                    List<string> cleanColorRules = colorRules
                        .Where(rule => !string.IsNullOrWhiteSpace(rule))
                        .Select(rule => rule.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    ApplyOperation(tasks, normalizedOperation, ref affectedTaskId, blockIds, cleanColorRules, mode, enabled, recursive, out status);
                    BooleanTaskRecord.WriteTo(entityObject, tasks);
                    if (!entityObject.CommitChanges())
                        status = "任务已更新，但提交Rhino对象属性失败。";
                }
                else if (!IsListOperation(normalizedOperation))
                {
                    status = "等待运行。";
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                status = ex.Message;
            }

            UpdatePreview(doc, tasks);

            DA.SetData(0, affectedTaskId);
            DA.SetDataList(1, tasks.Select(task => task.ToSummary()));
            DA.SetDataList(2, _previewGeometry);
            DA.SetData(3, status);
        }

        private static void ApplyOperation(List<BooleanTaskRecord> tasks, string operation, ref string taskId, List<Guid> blockIds, List<string> colorRules, string mode, bool enabled, bool recursive, out string status)
        {
            operation = (operation ?? "List").Trim();
            if (IsListOperation(operation))
            {
                status = "已读取任务。";
                return;
            }

            if (string.Equals(operation, "Clear", StringComparison.OrdinalIgnoreCase) || string.Equals(operation, "清空", StringComparison.OrdinalIgnoreCase))
            {
                int count = tasks.Count;
                tasks.Clear();
                status = "已清空任务 " + count + " 条。";
                taskId = string.Empty;
                return;
            }

            if (string.Equals(operation, "Delete", StringComparison.OrdinalIgnoreCase) || string.Equals(operation, "删除", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(taskId))
                    throw new ArgumentException("删除任务需要提供任务ID。");

                string targetTaskId = taskId;
                int removed = tasks.RemoveAll(task => string.Equals(task.Id, targetTaskId, StringComparison.OrdinalIgnoreCase));
                status = removed > 0 ? "已删除任务：" + taskId : "未找到任务：" + taskId;
                return;
            }

            if (string.Equals(operation, "Add", StringComparison.OrdinalIgnoreCase) || string.Equals(operation, "新增", StringComparison.OrdinalIgnoreCase))
            {
                if (blockIds.Count == 0)
                    throw new ArgumentException("新增任务需要至少一个块Guid。");
                if (colorRules.Count == 0)
                    throw new ArgumentException("新增任务需要至少一个颜色条件。");

                if (string.IsNullOrWhiteSpace(taskId))
                    taskId = "BT-" + Guid.NewGuid().ToString("N").Substring(0, 8);

                string targetTaskId = taskId;
                if (tasks.Any(task => string.Equals(task.Id, targetTaskId, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("任务ID已存在：" + taskId);

                tasks.Add(new BooleanTaskRecord(taskId, NormalizeMode(mode), blockIds, colorRules, recursive, enabled));
                status = "已新增任务：" + taskId;
                return;
            }

            if (string.Equals(operation, "Update", StringComparison.OrdinalIgnoreCase) || string.Equals(operation, "修改", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(taskId))
                    throw new ArgumentException("修改任务需要提供任务ID。");
                if (blockIds.Count == 0)
                    throw new ArgumentException("修改任务需要至少一个块Guid。");
                if (colorRules.Count == 0)
                    throw new ArgumentException("修改任务需要至少一个颜色条件。");

                string targetTaskId = taskId;
                int index = tasks.FindIndex(task => string.Equals(task.Id, targetTaskId, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    throw new ArgumentException("未找到任务：" + taskId);

                tasks[index] = new BooleanTaskRecord(taskId, NormalizeMode(mode), blockIds, colorRules, recursive, enabled);
                status = "已修改任务：" + taskId;
                return;
            }

            throw new ArgumentException("未知操作：" + operation);
        }

        private static bool IsListOperation(string operation)
        {
            return string.IsNullOrWhiteSpace(operation) ||
                string.Equals(operation, "List", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "清单", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "读取", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeMode(string mode)
        {
            mode = (mode ?? "Difference").Trim();
            if (string.Equals(mode, "差集", StringComparison.OrdinalIgnoreCase))
                return "Difference";
            if (string.Equals(mode, "并集", StringComparison.OrdinalIgnoreCase))
                return "Union";
            if (string.Equals(mode, "交集", StringComparison.OrdinalIgnoreCase))
                return "Intersection";

            return mode;
        }

        private void UpdatePreview(RhinoDoc doc, List<BooleanTaskRecord> tasks)
        {
            _previewGeometry.Clear();
            _previewBox = BoundingBox.Empty;

            foreach (BooleanTaskRecord task in tasks.Where(task => task.Enabled))
            {
                foreach (Guid blockId in task.BlockInstanceIds)
                {
                    RhinoObject obj = doc.Objects.FindId(blockId);
                    if (!(obj is InstanceObject instanceObject))
                        continue;

                    foreach (GeometryBase geometry in ExtractColorMatchedGeometry(doc, instanceObject.InstanceDefinition, instanceObject.InstanceXform, task.ColorRules, task.Recursive, new HashSet<Guid>()))
                    {
                        if (geometry == null)
                            continue;

                        _previewGeometry.Add(geometry);
                        BoundingBox box = geometry.GetBoundingBox(true);
                        if (box.IsValid)
                            _previewBox.Union(box);
                    }
                }
            }
        }

        internal static IEnumerable<GeometryBase> ExtractColorMatchedGeometry(RhinoDoc doc, InstanceDefinition definition, Transform transform, List<string> colorRules, bool recursive, HashSet<Guid> visited)
        {
            if (doc == null || definition == null || colorRules == null || colorRules.Count == 0 || !visited.Add(definition.Id))
                yield break;

            foreach (RhinoObject child in definition.GetObjects())
            {
                if (child == null)
                    continue;

                GeometryBase geometry = child.Geometry;
                if (geometry == null)
                    continue;

                if (recursive && geometry is InstanceReferenceGeometry nestedReference)
                {
                    InstanceDefinition nestedDefinition = doc.InstanceDefinitions.FindId(nestedReference.ParentIdefId);
                    Transform nestedTransform = transform * nestedReference.Xform;
                    foreach (GeometryBase nested in ExtractColorMatchedGeometry(doc, nestedDefinition, nestedTransform, colorRules, true, visited))
                        yield return nested;
                    continue;
                }

                if (!ColorMatches(doc, child.Attributes, colorRules))
                    continue;

                GeometryBase duplicate = geometry.Duplicate();
                if (duplicate == null)
                    continue;

                duplicate.Transform(transform);
                yield return duplicate;
            }

            visited.Remove(definition.Id);
        }

        private static bool ColorMatches(RhinoDoc doc, ObjectAttributes attributes, List<string> colorRules)
        {
            if (attributes == null)
                return false;

            Color color = attributes.ObjectColor;
            if (attributes.ColorSource == ObjectColorSource.ColorFromLayer)
            {
                Layer layer = doc?.Layers.FindIndex(attributes.LayerIndex);
                if (layer == null)
                    return false;

                color = layer.Color;
            }

            return colorRules.Any(rule => ColorRuleMatches(color, rule));
        }

        private static bool ColorRuleMatches(Color color, string rule)
        {
            if (string.IsNullOrWhiteSpace(rule))
                return false;

            string[] parts = rule.Split('=');
            if (parts.Length != 2 || !int.TryParse(parts[1].Trim(), out int value))
                return false;

            value = Math.Max(0, Math.Min(255, value));
            string channel = parts[0].Trim();
            if (string.Equals(channel, "R", StringComparison.OrdinalIgnoreCase))
                return color.R == value;
            if (string.Equals(channel, "G", StringComparison.OrdinalIgnoreCase))
                return color.G == value;
            if (string.Equals(channel, "B", StringComparison.OrdinalIgnoreCase))
                return color.B == value;

            return false;
        }

        private static bool TryGetGuid(object input, out Guid guid)
        {
            guid = Guid.Empty;
            if (input == null)
                return false;

            if (input is Guid directGuid)
            {
                guid = directGuid;
                return true;
            }

            if (input is GH_Guid ghGuid)
            {
                guid = ghGuid.Value;
                return true;
            }

            if (input is GH_ObjectWrapper wrapper)
                return TryGetGuid(wrapper.Value, out guid);

            if (input is string text)
                return Guid.TryParse(text, out guid);

            return false;
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

            Color color = Attributes?.Selected == true ? args.WireColour_Selected : Color.FromArgb(220, 0, 180, 255);
            foreach (GeometryBase geometry in _previewGeometry)
            {
                if (geometry is Brep brep)
                    args.Display.DrawBrepWires(brep, color, 2);
                else if (geometry is Curve curve)
                    args.Display.DrawCurve(curve, color, 2);
                else if (geometry is Mesh mesh)
                    args.Display.DrawMeshWires(mesh, color);
                else if (geometry is Rhino.Geometry.Point point)
                    args.Display.DrawPoint(point.Location, color);
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);

            if (Hidden || Locked)
                return;

            Color color = Color.FromArgb(70, 0, 180, 255);
            foreach (GeometryBase geometry in _previewGeometry)
            {
                if (geometry is Brep brep)
                    args.Display.DrawBrepShaded(brep, new DisplayMaterial(color));
                else if (geometry is Mesh mesh)
                    args.Display.DrawMeshShaded(mesh, new DisplayMaterial(color));
            }
        }

        protected override Bitmap Icon => GeneratedIcon.Get("gen_Block");

        public override Guid ComponentGuid => new Guid("7F589435-0C79-49C7-8B1A-7D3C0D325721");

        internal sealed class BooleanTaskRecord
        {
            public BooleanTaskRecord(string id, string mode, List<Guid> blockInstanceIds, List<string> colorRules, bool recursive, bool enabled)
            {
                Id = id ?? string.Empty;
                Mode = mode ?? "Difference";
                BlockInstanceIds = blockInstanceIds ?? new List<Guid>();
                ColorRules = colorRules ?? new List<string>();
                Recursive = recursive;
                Enabled = enabled;
            }

            public string Id { get; }

            public string Mode { get; }

            public List<Guid> BlockInstanceIds { get; }

            public List<string> ColorRules { get; }

            public bool Recursive { get; }

            public bool Enabled { get; }

            public string ToSummary()
            {
                return string.Format(
                    "{0}; 模式={1}; 启用={2}; 块={3}; 颜色条件={4}; 递归={5}",
                    Id,
                    Mode,
                    Enabled,
                    BlockInstanceIds.Count,
                    string.Join(",", ColorRules),
                    Recursive);
            }

            public string Serialize()
            {
                return string.Join("\t",
                    Encode(Id),
                    Encode(Mode),
                    Enabled ? "1" : "0",
                    Recursive ? "1" : "0",
                    string.Join(",", BlockInstanceIds.Select(id => id.ToString("D"))),
                    string.Join(",", ColorRules.Select(Encode)));
            }

            public static List<BooleanTaskRecord> ReadFrom(RhinoObject obj)
            {
                string text = obj?.Attributes.GetUserString(UserStringKey) ?? string.Empty;
                List<BooleanTaskRecord> records = new List<BooleanTaskRecord>();
                if (string.IsNullOrWhiteSpace(text))
                    return records;

                string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    if (line == "v1")
                        continue;

                    string[] fields = line.Split('\t');
                    if (fields.Length != 6)
                        continue;

                    List<Guid> blockIds = fields[4]
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => Guid.TryParse(value, out Guid guid) ? guid : Guid.Empty)
                        .Where(guid => guid != Guid.Empty)
                        .ToList();
                    List<string> colorRules = fields[5]
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(Decode)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToList();

                    records.Add(new BooleanTaskRecord(
                        Decode(fields[0]),
                        Decode(fields[1]),
                        blockIds,
                        colorRules,
                        fields[3] == "1",
                        fields[2] == "1"));
                }

                return records;
            }

            public static void WriteTo(RhinoObject obj, List<BooleanTaskRecord> tasks)
            {
                if (obj == null)
                    return;

                string text = "v1\n" + string.Join("\n", (tasks ?? new List<BooleanTaskRecord>()).Select(task => task.Serialize()));
                obj.Attributes.SetUserString(UserStringKey, text);
            }

            private static string Encode(string value)
            {
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
            }

            private static string Decode(string value)
            {
                try
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
                }
                catch
                {
                    return string.Empty;
                }
            }
        }
    }
}
