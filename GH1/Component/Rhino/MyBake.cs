using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using parrot.Properties;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace NS_Parrot
{
    public class MyBake : GH_Component
    {
        public MyBake()
          : base("MyBake", "MyBake",
              "带自定义信息的bake",
              "Parrot", "建模")
        {
        }

        private List<Guid> _lastResult = new List<Guid>();
        private bool _triggerBake = false;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("对象", "Obj", "支持几何/文字/标注/块", GH_ParamAccess.list);

            pManager.AddTextParameter("图层", "Layer", "图层", GH_ParamAccess.item);
            pManager.AddColourParameter("颜色", "Color", "颜色", GH_ParamAccess.item);

            pManager.AddTextParameter("Key", "Key", "键名", GH_ParamAccess.list);
            pManager.AddTextParameter("KeyValue", "Value", "键值", GH_ParamAccess.list);

            pManager.AddBooleanParameter("IsGroup", "Group", "是否成组", GH_ParamAccess.item, false);
            pManager.AddTextParameter("GroupName", "GN", "组名", GH_ParamAccess.item);

            pManager.AddBooleanParameter("IsBake", "Bake", "执行", GH_ParamAccess.item, false);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("GUID", "GUID", "Bake结果的GUID", GH_ParamAccess.list);
        }

        public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendItem(menu, "运行 Bake", (s, e) =>
            {
                _triggerBake = true;
                ExpireSolution(true);
            });
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox box = base.ClippingBox;
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null || _lastResult.Count == 0)
                    return box;

                foreach (Guid id in _lastResult)
                {
                    RhinoObject obj = doc.Objects.FindId(id);
                    BoundingBox objectBox = obj?.Geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;
                    if (objectBox.IsValid)
                        box.Union(objectBox);
                }

                return box;
            }
        }

        public override bool IsPreviewCapable
        {
            get { return true; }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (Hidden || Locked || Attributes?.Selected != true || _lastResult.Count == 0)
                return;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
                return;

            foreach (Guid id in _lastResult)
            {
                RhinoObject obj = doc.Objects.FindId(id);
                if (obj != null)
                    DrawBakedObjectPreview(args, doc, obj, args.WireColour_Selected);
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<object> objects = new List<object>();
            string layerName = string.Empty;
            Color color = Color.Empty;
            List<string> keys = new List<string>();
            List<string> values = new List<string>();
            bool isGroup = false;
            string groupName = string.Empty;
            bool run = false;

            if (!DA.GetDataList(0, objects)) return;
            DA.GetData(1, ref layerName);
            DA.GetData(2, ref color);
            DA.GetDataList(3, keys);
            DA.GetDataList(4, values);
            DA.GetData(5, ref isGroup);
            DA.GetData(6, ref groupName);
            DA.GetData(7, ref run);

            if (!run && !_triggerBake)
            {
                DA.SetDataList(0, _lastResult);
                return;
            }
            _triggerBake = false;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                DA.SetDataList(0, _lastResult);
                return;
            }

            List<Guid> result = new List<Guid>();

            try
            {
                for (int i = 0; i < objects.Count; i++)
                {
                    if (TryBakeObject(doc, objects[i], layerName, color, keys, values, out Guid id))
                        result.Add(id);
                    else
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"第 {i} 项不是可 Bake 类型：{DescribeObject(objects[i])}");
                }

                AddGroup(doc, result, isGroup, groupName);
                doc.Views.Redraw();

                _lastResult = result;
                DA.SetDataList(0, result);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetDataList(0, _lastResult);
            }
        }

        private static bool TryBakeObject(RhinoDoc doc, object data, string layerName, Color color, List<string> keys, List<string> values, out Guid id, int depth = 0)
        {
            id = Guid.Empty;
            if (doc == null || data == null || depth > 8)
                return false;

            if (data is GH_ObjectWrapper wrapper)
                return TryBakeObject(doc, wrapper.Value, layerName, color, keys, values, out id, depth + 1);

            if (data is GH_Guid ghGuid)
                return TryBakeGuid(doc, ghGuid.Value, layerName, color, keys, values, out id);

            if (data is Guid guid)
                return TryBakeGuid(doc, guid, layerName, color, keys, values, out id);

            if (data is RhinoObject rhinoObject)
                return TryBakeRhinoObject(doc, rhinoObject, layerName, color, keys, values, out id);

            if (data is InstanceReferenceGeometry instanceReference)
                return TryBakeInstanceReference(doc, instanceReference, BuildAttributes(doc, null, layerName, color, keys, values), out id);

            if (data is IGH_GeometricGoo geometricGoo)
            {
                if (geometricGoo.ReferenceID != Guid.Empty && TryBakeGuid(doc, geometricGoo.ReferenceID, layerName, color, keys, values, out id))
                    return true;
            }

            if (data is IGH_BakeAwareData bakeAware)
            {
                ObjectAttributes attributes = BuildAttributes(doc, null, layerName, color, keys, values);
                return bakeAware.BakeGeometry(doc, attributes, out id) && id != Guid.Empty;
            }

            if (data is IGH_Goo goo)
            {
                object scriptValue = goo.ScriptVariable();
                if (!ReferenceEquals(scriptValue, data) && TryBakeObject(doc, scriptValue, layerName, color, keys, values, out id, depth + 1))
                    return true;
            }

            ObjectAttributes directAttributes = BuildAttributes(doc, null, layerName, color, keys, values);

            if (data is GeometryBase geometry)
                return TryBakeGeometry(doc, geometry, directAttributes, out id);

            if (data is Point3d point)
            {
                id = doc.Objects.AddPoint(point, directAttributes);
                return id != Guid.Empty;
            }

            if (data is Line line)
            {
                id = doc.Objects.AddLine(line, directAttributes);
                return id != Guid.Empty;
            }

            if (data is Polyline polyline)
            {
                id = doc.Objects.AddPolyline(polyline, directAttributes);
                return id != Guid.Empty;
            }

            if (data is Arc arc)
            {
                id = doc.Objects.AddArc(arc, directAttributes);
                return id != Guid.Empty;
            }

            if (data is Circle circle)
            {
                id = doc.Objects.AddCircle(circle, directAttributes);
                return id != Guid.Empty;
            }

            if (data is Rectangle3d rectangle)
            {
                id = doc.Objects.AddPolyline(rectangle.ToPolyline(), directAttributes);
                return id != Guid.Empty;
            }

            if (data is Box box)
                return TryBakeGeometry(doc, box.ToBrep(), directAttributes, out id);

            return false;
        }

        private static bool TryBakeGuid(RhinoDoc doc, Guid sourceId, string layerName, Color color, List<string> keys, List<string> values, out Guid id)
        {
            id = Guid.Empty;
            if (sourceId == Guid.Empty)
                return false;

            RhinoObject obj = doc.Objects.FindId(sourceId);
            return obj != null && TryBakeRhinoObject(doc, obj, layerName, color, keys, values, out id);
        }

        private static bool TryBakeRhinoObject(RhinoDoc doc, RhinoObject obj, string layerName, Color color, List<string> keys, List<string> values, out Guid id)
        {
            id = Guid.Empty;
            if (obj == null)
                return false;

            ObjectAttributes attributes = BuildAttributes(doc, obj.Attributes, layerName, color, keys, values);

            if (obj is InstanceObject instanceObject)
            {
                InstanceDefinition definition = instanceObject.InstanceDefinition;
                if (definition == null)
                    return false;

                id = doc.Objects.AddInstanceObject(definition.Index, instanceObject.InstanceXform, attributes);
                return id != Guid.Empty;
            }

            GeometryBase geometry = obj.Geometry?.Duplicate();
            return TryBakeGeometry(doc, geometry, attributes, out id);
        }

        private static bool TryBakeInstanceReference(RhinoDoc doc, InstanceReferenceGeometry instanceReference, ObjectAttributes attributes, out Guid id)
        {
            id = Guid.Empty;
            if (instanceReference == null)
                return false;

            InstanceDefinition definition = doc.InstanceDefinitions.FindId(instanceReference.ParentIdefId);
            if (definition == null)
                return false;

            id = doc.Objects.AddInstanceObject(definition.Index, instanceReference.Xform, attributes);
            return id != Guid.Empty;
        }

        private static bool TryBakeGeometry(RhinoDoc doc, GeometryBase geometry, ObjectAttributes attributes, out Guid id)
        {
            id = Guid.Empty;
            if (geometry == null)
                return false;

            if (geometry is InstanceReferenceGeometry instanceReference)
                return TryBakeInstanceReference(doc, instanceReference, attributes, out id);

            GeometryBase duplicate = geometry.Duplicate();
            if (duplicate == null)
                return false;

            id = doc.Objects.Add(duplicate, attributes);
            return id != Guid.Empty;
        }

        private static ObjectAttributes BuildAttributes(RhinoDoc doc, ObjectAttributes source, string layerName, Color color, List<string> keys, List<string> values)
        {
            ObjectAttributes attributes = source?.Duplicate() ?? doc.CreateDefaultAttributes();

            if (!string.IsNullOrWhiteSpace(layerName))
            {
                int layerIndex = doc.Layers.FindByFullPath(layerName, -1);
                if (layerIndex < 0)
                    layerIndex = doc.Layers.Add(layerName, color.IsEmpty ? Color.Black : color);

                if (layerIndex >= 0)
                    attributes.LayerIndex = layerIndex;
            }

            if (!color.IsEmpty)
            {
                attributes.ObjectColor = color;
                attributes.ColorSource = ObjectColorSource.ColorFromObject;
            }

            int count = Math.Min(keys?.Count ?? 0, values?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                if (!string.IsNullOrEmpty(keys[i]))
                    attributes.SetUserString(keys[i], values[i] ?? string.Empty);
            }

            return attributes;
        }

        private static void AddGroup(RhinoDoc doc, List<Guid> ids, bool isGroup, string groupName)
        {
            if (!isGroup || ids == null || ids.Count == 0)
                return;

            int groupIndex = string.IsNullOrWhiteSpace(groupName)
                ? doc.Groups.Add()
                : doc.Groups.Add(groupName);

            if (groupIndex >= 0)
                doc.Groups.AddToGroup(groupIndex, ids);
        }

        private static string DescribeObject(object data)
        {
            if (data == null)
                return "<null>";

            if (data is IGH_Goo goo)
                return goo.TypeName;

            return data.GetType().Name;
        }

        private static void DrawBakedObjectPreview(IGH_PreviewArgs args, RhinoDoc doc, RhinoObject obj, Color color)
        {
            if (obj is InstanceObject instanceObject)
            {
                InstanceDefinition definition = instanceObject.InstanceDefinition;
                if (definition != null)
                    DrawInstanceDefinitionPreview(args, doc, definition, instanceObject.InstanceXform, color, new HashSet<Guid>());
                return;
            }

            DrawGeometryPreview(args, doc, obj.Geometry, Transform.Identity, color, new HashSet<Guid>());
        }

        private static void DrawInstanceDefinitionPreview(IGH_PreviewArgs args, RhinoDoc doc, InstanceDefinition definition, Transform transform, Color color, HashSet<Guid> visited)
        {
            if (definition == null || !visited.Add(definition.Id))
                return;

            foreach (RhinoObject child in definition.GetObjects())
            {
                if (child?.Geometry == null)
                    continue;

                DrawGeometryPreview(args, doc, child.Geometry, transform, color, visited);
            }

            visited.Remove(definition.Id);
        }

        private static void DrawGeometryPreview(IGH_PreviewArgs args, RhinoDoc doc, GeometryBase geometry, Transform transform, Color color, HashSet<Guid> visited)
        {
            if (geometry == null)
                return;

            if (geometry is InstanceReferenceGeometry instanceReference)
            {
                InstanceDefinition nestedDefinition = doc.InstanceDefinitions.FindId(instanceReference.ParentIdefId);
                Transform nestedTransform = transform * instanceReference.Xform;
                DrawInstanceDefinitionPreview(args, doc, nestedDefinition, nestedTransform, color, visited);
                return;
            }

            GeometryBase previewGeometry = geometry.Duplicate();
            if (previewGeometry == null)
                return;

            previewGeometry.Transform(transform);

            if (previewGeometry is Brep brep)
                args.Display.DrawBrepWires(brep, color, 2);
            else if (previewGeometry is Curve curve)
                args.Display.DrawCurve(curve, color, 2);
            else if (previewGeometry is Mesh mesh)
                args.Display.DrawMeshWires(mesh, color);
            else if (previewGeometry is Rhino.Geometry.Point point)
                args.Display.DrawPoint(point.Location, color);
            else if (previewGeometry is PointCloud cloud)
            {
                foreach (PointCloudItem item in cloud)
                    args.Display.DrawPoint(item.Location, color);
            }
            else if (previewGeometry is TextEntity text)
                args.Display.DrawText(text, color);
            else if (previewGeometry is TextDot textDot)
            {
                args.Display.DrawPoint(textDot.Point, color);
                args.Display.Draw2dText(textDot.Text, color, textDot.Point, false, 12);
            }
            else if (previewGeometry is Extrusion extrusion)
                args.Display.DrawBrepWires(extrusion.ToBrep(), color, 2);
            else
            {
                BoundingBox box = previewGeometry.GetBoundingBox(true);
                if (box.IsValid)
                    args.Display.DrawBox(box, color);
            }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Resources.烘焙;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("3F8C24F7-BA7E-4018-AD9D-BF821B878693"); }
        }
    }
}
