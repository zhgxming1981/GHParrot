using Grasshopper.Kernel;
using GH_IO.Serialization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NS_Parrot
{
    public class ScrewLineArcArray : GH_Component
    {
        private const string SourceKey = "来源";
        private const string SourceValue = "ScrewLineArcArray";
        private const string BatchIdKey = "ScrewLineArcArrayId";

        private string _bakeId = Guid.NewGuid().ToString("D");
        private bool _lastBake;
        private bool _lastDelete;
        private string _lastMessage = string.Empty;
        private readonly List<Guid> _lastBakedGuids = new List<Guid>();

        public ScrewLineArcArray()
          : base("ScrewLineArcArray", "ScrewLineArcArray",
              "根据一条模板螺栓线生成参数化弧形螺栓线阵列，并展开模板线上的规格、材质、孔类型",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("螺栓线", "螺栓线", "一条或多条模板螺栓线；如果是 Rhino 中 Bake 的线，会尝试读取其 UserString", GH_ParamAccess.list);
            pManager.AddIntegerParameter("数量", "数量", "生成螺栓线数量", GH_ParamAccess.item, 1);
            pManager.AddPlaneParameter("平面", "平面", "用平面原点作为旋转中心，平面 Z 轴作为旋转轴", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("角度", "角度", "相邻螺栓线之间的旋转角度，单位为度", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("初始角度", "初始角度", "模板螺栓线先绕平面旋转的角度，单位为度；旋转后的线作为阵列源线", GH_ParamAccess.item, 0.0);
            pManager.AddTextParameter("规格", "规格", "可选，覆盖模板线 UserString：螺栓规格/规格/孔规格/Spec，例如 ST4.8*16", GH_ParamAccess.item);
            pManager.AddTextParameter("材质", "材质", "可选，覆盖模板线 UserString：材质，例如 铝材、钢材", GH_ParamAccess.item);
            pManager.AddTextParameter("孔类型", "孔类型", "可选，覆盖模板线 UserString：孔类型，例如 gk,dk 或 过孔,底孔", GH_ParamAccess.item);
            pManager.AddIntegerParameter("穿透层数", "穿透层数", "可选，统一写入本批螺栓线；为空时使用模板线 UserString，模板线也为空时默认为 2", GH_ParamAccess.item);
            pManager.AddBooleanParameter("处理方式", "处理方式", "穿透层数与预期不符时的统一处理方式。True=默认，继续按默认规则开孔；False=无效，此螺丝无效并跳过。", GH_ParamAccess.item, true);
            pManager.AddTextParameter("使用位置", "使用位置", "可选，统一写入本批螺栓线；为空时使用模板线 UserString", GH_ParamAccess.item);
            pManager.AddTextParameter("图层", "图层", "可选，Bake 到 Rhino 的目标图层；为空时使用当前图层", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Bake", "Bake", "True 时将当前螺栓线 Bake 到 Rhino，并写入 UserString；保持 True 不会重复 Bake", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("删除", "删除", "True 时删除本电池上次 Bake 出来的螺栓线；保持 True 不会重复删除", GH_ParamAccess.item, false);

            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
            pManager[12].Optional = true;
            pManager[13].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("螺栓线", "螺栓线", "生成后的螺栓代表线", GH_ParamAccess.list);
            pManager.AddTextParameter("规格", "规格", "与螺栓线一一对应的螺栓规格", GH_ParamAccess.list);
            pManager.AddTextParameter("材质", "材质", "与螺栓线一一对应的材质", GH_ParamAccess.list);
            pManager.AddTextParameter("孔类型", "孔类型", "与螺栓线一一对应的孔类型", GH_ParamAccess.list);
            pManager.AddTextParameter("GUID", "GUID", "本次 Bake 出来的 Rhino 螺栓线对象 GUID", GH_ParamAccess.list);
            pManager.AddTextParameter("消息", "消息", "Bake/Delete 执行消息", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Line> templateLines = new List<Line>();
            int count = 1;
            Plane plane = Plane.WorldXY;
            double angle = 0.0;
            double initialAngle = 0.0;
            string inputSpec = string.Empty;
            string inputMaterial = string.Empty;
            string inputHoleTypes = string.Empty;
            int inputPenetrationCount = 0;
            bool inputMismatchAction = true;
            string inputUsePosition = string.Empty;
            string layerName = string.Empty;
            bool bake = false;
            bool delete = false;

            if (!DA.GetDataList(0, templateLines) || templateLines.Count == 0)
                return;
            DA.GetData(1, ref count);
            DA.GetData(2, ref plane);
            DA.GetData(3, ref angle);
            DA.GetData(4, ref initialAngle);
            DA.GetData(5, ref inputSpec);
            DA.GetData(6, ref inputMaterial);
            DA.GetData(7, ref inputHoleTypes);
            DA.GetData(8, ref inputPenetrationCount);
            bool hasMismatchActionInput = DA.GetData(9, ref inputMismatchAction);
            DA.GetData(10, ref inputUsePosition);
            DA.GetData(11, ref layerName);
            DA.GetData(12, ref bake);
            DA.GetData(13, ref delete);

            for (int templateIndex = 0; templateIndex < templateLines.Count; templateIndex++)
            {
                Line templateLine = templateLines[templateIndex];
                if (!templateLine.IsValid || templateLine.Length <= RhinoMath.ZeroTolerance)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"第 {templateIndex + 1} 条模板螺栓线无效。");
                    return;
                }
            }

            if (count < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "数量必须大于等于 1。");
                return;
            }

            if (!plane.IsValid || !plane.ZAxis.IsValid || plane.ZAxis.Length <= RhinoMath.ZeroTolerance)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "平面无效。");
                return;
            }

            Vector3d axis = plane.ZAxis;
            axis.Unitize();
            TemplateUserStrings userStrings = ReadTemplateUserStrings(templateLines[0], RhinoDoc.ActiveDoc);
            string spec = FirstNonEmpty(inputSpec, userStrings.Spec);
            string material = FirstNonEmpty(inputMaterial, userStrings.Material);
            string holeTypes = FirstNonEmpty(inputHoleTypes, userStrings.HoleTypes);
            if (!TryNormalizeHoleTypes(holeTypes, out string normalizedHoleTypes, out string holeTypeError))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, holeTypeError);
                return;
            }
            holeTypes = normalizedHoleTypes;

            List<Line> lines = new List<Line>();
            List<string> specs = new List<string>();
            List<string> materials = new List<string>();
            List<string> holeTypeList = new List<string>();
            int penetrationCount = ResolvePenetrationCount(inputPenetrationCount, userStrings.PenetrationCount);
            string mismatchAction = ResolveMismatchAction(hasMismatchActionInput, inputMismatchAction, userStrings.MismatchAction);
            string usePosition = ResolveUsePosition(inputUsePosition, userStrings.UsePosition);

            foreach (Line templateLine in templateLines)
            {
                for (int i = 0; i < count; i++)
                {
                    double currentAngle = RhinoMath.ToRadians(initialAngle + angle * i);
                    Transform rotation = Transform.Rotation(currentAngle, axis, plane.Origin);
                    Line line = templateLine;
                    line.Transform(rotation);
                    lines.Add(line);
                    specs.Add(spec);
                    materials.Add(material);
                    holeTypeList.Add(holeTypes);
                }
            }

            DA.SetDataList(0, lines);
            DA.SetDataList(1, specs);
            DA.SetDataList(2, materials);
            DA.SetDataList(3, holeTypeList);
            if (delete && !_lastDelete)
                _lastMessage = DeleteBakedLines(RhinoDoc.ActiveDoc);
            if (bake && !_lastBake)
            {
                DeleteBakedLines(RhinoDoc.ActiveDoc);
                _lastMessage = BakeLines(RhinoDoc.ActiveDoc, lines, spec, material, holeTypes, penetrationCount, mismatchAction, usePosition, layerName, userStrings.AllUserStrings);
            }

            _lastBake = bake;
            _lastDelete = delete;

            DA.SetDataList(4, _lastBakedGuids.ConvertAll(id => id.ToString("D")));
            DA.SetData(5, _lastMessage);
        }

        private string BakeLines(RhinoDoc doc, List<Line> lines, string spec, string material, string holeTypes, int penetrationCount, string mismatchAction, string usePosition, string layerName, Dictionary<string, string> templateUserStrings)
        {
            if (doc == null)
                return "Bake 失败：当前没有 Rhino 文档。";

            int layerIndex = ResolveLayer(doc, layerName);
            int count = 0;
            _lastBakedGuids.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i];
                ObjectAttributes attributes = new ObjectAttributes();
                CopyUserStrings(attributes, templateUserStrings);
                attributes.SetUserString(SourceKey, SourceValue);
                attributes.SetUserString(BatchIdKey, _bakeId);
                attributes.SetUserString("螺栓规格", spec ?? string.Empty);
                attributes.SetUserString("材质", material ?? string.Empty);
                attributes.SetUserString("孔类型", holeTypes ?? string.Empty);
                attributes.SetUserString("穿透层数", penetrationCount.ToString());
                attributes.SetUserString("处理方式", mismatchAction ?? ScrewPenetrationMismatchActionParam.DefaultAction);
                attributes.SetUserString("使用位置", usePosition ?? string.Empty);
                if (layerIndex >= 0)
                    attributes.LayerIndex = layerIndex;

                Guid id = doc.Objects.AddLine(line, attributes);
                if (id != Guid.Empty)
                {
                    _lastBakedGuids.Add(id);
                    count++;
                }
            }

            doc.Views.Redraw();
            return $"已 Bake {count} 条螺栓线。";
        }

        private string DeleteBakedLines(RhinoDoc doc)
        {
            if (doc == null)
                return "删除失败：当前没有 Rhino 文档。";

            List<RhinoObject> objects = new List<RhinoObject>();
            ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings
            {
                ActiveObjects = true,
                HiddenObjects = true,
                LockedObjects = true,
                ObjectTypeFilter = ObjectType.Curve
            };

            foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
            {
                string source = obj.Attributes.GetUserString(SourceKey);
                string bakeId = obj.Attributes.GetUserString(BatchIdKey);
                if (string.Equals(source, SourceValue, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(bakeId, _bakeId, StringComparison.OrdinalIgnoreCase))
                    objects.Add(obj);
            }

            int count = 0;
            foreach (RhinoObject obj in objects)
            {
                if (doc.Objects.Delete(obj, true))
                    count++;
            }

            _lastBakedGuids.Clear();
            doc.Views.Redraw();
            return $"已删除 {count} 条本电池 Bake 的螺栓线。";
        }

        private static void CopyUserStrings(ObjectAttributes attributes, Dictionary<string, string> source)
        {
            if (attributes == null || source == null)
                return;

            foreach (KeyValuePair<string, string> item in source)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                    continue;

                attributes.SetUserString(item.Key, item.Value ?? string.Empty);
            }
        }

        private static int ResolveLayer(RhinoDoc doc, string layerName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(layerName))
                return -1;

            int index = doc.Layers.FindByFullPath(layerName.Trim(), RhinoMath.UnsetIntIndex);
            if (index >= 0)
                return index;

            index = doc.Layers.FindName(layerName.Trim())?.Index ?? -1;
            if (index >= 0)
                return index;

            return doc.Layers.Add(layerName.Trim(), System.Drawing.Color.Black);
        }

        private static TemplateUserStrings ReadTemplateUserStrings(Line line, RhinoDoc doc)
        {
            TemplateUserStrings result = new TemplateUserStrings();
            if (doc == null || !line.IsValid)
                return result;

            ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings
            {
                ActiveObjects = true,
                HiddenObjects = true,
                LockedObjects = true,
                ObjectTypeFilter = ObjectType.Curve
            };

            foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
            {
                CurveObject curveObject = obj as CurveObject;
                Curve curve = curveObject?.CurveGeometry;
                if (curve == null || !curve.IsLinear(RhinoMath.ZeroTolerance))
                    continue;

                Line candidate = new Line(curve.PointAtStart, curve.PointAtEnd);
                if (!IsSameLine(candidate, line, doc.ModelAbsoluteTolerance))
                    continue;

                result.AllUserStrings = GetAllUserStrings(obj);
                result.Spec = GetUserString(obj, "螺栓规格", "规格", "孔规格", "Spec");
                result.Material = GetUserString(obj, "材质", "Material", "Mat");
                result.HoleTypes = GetUserString(obj, "孔类型", "孔角色", "Role", "HoleTypes");
                result.PenetrationCount = GetUserString(obj, "穿透层数", "穿透实体数", "穿透实体数量", "PenetrationCount", "Penetration");
                result.MismatchAction = GetUserString(obj, "处理方式", "MismatchAction", "PenetrationMismatchAction");
                result.UsePosition = GetUserString(obj, "使用位置", "UsePosition", "Position");
                return result;
            }

            return result;
        }

        private static Dictionary<string, string> GetAllUserStrings(RhinoObject obj)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (obj == null)
                return result;

            string[] keys = obj.Attributes.GetUserStrings().AllKeys;
            if (keys == null)
                return result;

            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                result[key] = obj.Attributes.GetUserString(key) ?? string.Empty;
            }

            return result;
        }

        private static string GetUserString(RhinoObject obj, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = obj.Attributes.GetUserString(key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static bool IsSameLine(Line a, Line b, double tolerance)
        {
            double tol = Math.Max(tolerance, 0.001);
            bool sameDirection = a.From.DistanceTo(b.From) <= tol && a.To.DistanceTo(b.To) <= tol;
            bool reverseDirection = a.From.DistanceTo(b.To) <= tol && a.To.DistanceTo(b.From) <= tol;
            return sameDirection || reverseDirection;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static int ResolvePenetrationCount(int inputValue, string templateValue)
        {
            if (inputValue > 0)
                return inputValue;

            if (!string.IsNullOrWhiteSpace(templateValue) && int.TryParse(templateValue.Trim(), out int parsed) && parsed > 0)
                return parsed;

            return 2;
        }

        private static string ResolveMismatchAction(bool hasInputValue, bool inputValue, string templateValue)
        {
            return hasInputValue
                ? (inputValue ? ScrewPenetrationMismatchActionParam.DefaultAction : ScrewPenetrationMismatchActionParam.InvalidAction)
                : NormalizeMismatchAction(templateValue);
        }

        private static string NormalizeMismatchAction(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ScrewPenetrationMismatchActionParam.DefaultAction;

            string text = value.Trim();
            if (string.Equals(text, "无效", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "invalid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "跳过", StringComparison.OrdinalIgnoreCase))
                return ScrewPenetrationMismatchActionParam.InvalidAction;

            return ScrewPenetrationMismatchActionParam.DefaultAction;
        }

        private static bool TryNormalizeHoleTypes(string value, out string normalized, out string error)
        {
            normalized = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return true;

            List<string> result = new List<string>();
            string[] tokens = value.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                string text = token.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                string type = NormalizeHoleTypeToken(text);
                if (string.IsNullOrWhiteSpace(type))
                {
                    error = $"孔类型输入不合法：{text}。允许输入：gk/过孔、dk/底孔、gy/工艺孔、tg/跳过，可用逗号分隔，例如 gk,dk。";
                    return false;
                }

                result.Add(type);
            }

            normalized = string.Join(",", result);
            return true;
        }

        private static string NormalizeHoleTypeToken(string token)
        {
            if (string.Equals(token, "gk", StringComparison.OrdinalIgnoreCase) || token == "过孔")
                return "过孔";
            if (string.Equals(token, "dk", StringComparison.OrdinalIgnoreCase) || token == "底孔")
                return "底孔";
            if (string.Equals(token, "gy", StringComparison.OrdinalIgnoreCase) || token == "工艺孔")
                return "工艺孔";
            if (string.Equals(token, "tg", StringComparison.OrdinalIgnoreCase) || token == "跳过")
                return "跳过";

            return string.Empty;
        }

        private static string ResolveUsePosition(string inputValue, string templateValue)
        {
            if (!string.IsNullOrWhiteSpace(inputValue))
                return inputValue.Trim();

            return string.IsNullOrWhiteSpace(templateValue) ? string.Empty : templateValue.Trim();
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.GetScrewLineArcArray();

        public override Guid ComponentGuid => new Guid("93C64B28-239A-4EF7-91F3-E32B93FB43B4");

        public override bool Write(GH_IWriter writer)
        {
            writer.SetString("BakeId", _bakeId);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            _bakeId = reader.ItemExists("BakeId") ? reader.GetString("BakeId") : Guid.NewGuid().ToString("D");
            return base.Read(reader);
        }

        private class TemplateUserStrings
        {
            public string Spec { get; set; } = string.Empty;
            public string Material { get; set; } = string.Empty;
            public string HoleTypes { get; set; } = string.Empty;
            public string PenetrationCount { get; set; } = string.Empty;
            public string MismatchAction { get; set; } = string.Empty;
            public string UsePosition { get; set; } = string.Empty;
            public Dictionary<string, string> AllUserStrings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
