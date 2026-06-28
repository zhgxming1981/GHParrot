using CommonFunction;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NS_Parrot
{
    public class LayoutDrawingV2 : GH_Component
    {
        public LayoutDrawingV2()
          : base("LayoutDrawingV2", "布局图纸V2",
              "自动布局三视图图纸，自动计算图框比例并放置在合适位置",
              "Parrot", "工具")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("正视图曲线", "Front", "正视图的曲线列表", GH_ParamAccess.list);
            pManager.AddCurveParameter("左视图曲线", "Left", "左视图的曲线列表", GH_ParamAccess.list);
            pManager.AddCurveParameter("俯视图曲线", "Top", "俯视图的曲线列表", GH_ParamAccess.list);
            pManager.AddGenericParameter("图框", "Frame", "图框属性块实例的Guid", GH_ParamAccess.item);
            pManager.AddTextParameter("图框属性", "Attrs", "属性块中每个属性的值（按顺序）", GH_ParamAccess.list);
            pManager.AddNumberParameter("视图间距", "Gap", "视图之间的间距", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("标注偏移", "DimOffset", "尺寸标注距包围盒的偏移", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("图框边距", "FrameMargin", "图框与视图包围盒的边距", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("图框缩放", "FrameScale", "图框缩放比例（0=自动计算）", GH_ParamAccess.item, 0.0);
            pManager.AddPointParameter("插入点", "InsertPt", "图框左下角插入点", GH_ParamAccess.item);
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("正视图标注", "FrontDim", "正视图尺寸标注", GH_ParamAccess.list);
            pManager.AddGeometryParameter("左视图标注", "LeftDim", "左视图尺寸标注", GH_ParamAccess.list);
            pManager.AddGeometryParameter("俯视图标注", "TopDim", "俯视图尺寸标注", GH_ParamAccess.list);
            pManager.AddCurveParameter("正视图框线", "FrontBox", "正视图包围盒线框", GH_ParamAccess.list);
            pManager.AddCurveParameter("左视图框线", "LeftBox", "左视图包围盒线框", GH_ParamAccess.list);
            pManager.AddCurveParameter("俯视图框线", "TopBox", "俯视图包围盒线框", GH_ParamAccess.list);
            pManager.AddGenericParameter("图框实例", "Frame", "插入的图框块实例", GH_ParamAccess.item);
            pManager.AddNumberParameter("图框比例", "Scale", "实际使用的图框缩放比例", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<Curve> frontCurves = new List<Curve>();
            List<Curve> leftCurves = new List<Curve>();
            List<Curve> topCurves = new List<Curve>();

            if (!DA.GetDataList(0, frontCurves)) return;
            if (!DA.GetDataList(1, leftCurves)) return;
            if (!DA.GetDataList(2, topCurves)) return;

            GH_Guid frameGuid = null;
            if (!DA.GetData(3, ref frameGuid)) return;

            List<string> attrs = new List<string>();
            if (!DA.GetDataList(4, attrs)) return;

            double gap = 100.0;
            double dimOffset = 50.0;
            double frameMargin = 50.0;
            double frameScale = 0.0;
            Point3d insertPt = Point3d.Origin;
            DA.GetData(5, ref gap);
            DA.GetData(6, ref dimOffset);
            DA.GetData(7, ref frameMargin);
            DA.GetData(8, ref frameScale);
            DA.GetData(9, ref insertPt);

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null) return;

            double tol = doc.ModelAbsoluteTolerance;

            BoundingBox frontBb = ComputeBox(frontCurves);
            BoundingBox leftBb = ComputeBox(leftCurves);
            BoundingBox topBb = ComputeBox(topCurves);

            if (!frontBb.IsValid || !leftBb.IsValid || !topBb.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "输入曲线无法计算包围盒");
                return;
            }

            double maxH = Math.Max(frontBb.Max.Y - frontBb.Min.Y,
                          Math.Max(leftBb.Max.Y - leftBb.Min.Y,
                                   topBb.Max.Y - topBb.Min.Y));

            double fw = frontBb.Max.X - frontBb.Min.X;
            double lw = leftBb.Max.X - leftBb.Min.X;
            double tw = topBb.Max.X - topBb.Min.X;

            Point3d frontOrigin = new Point3d(0, 0, 0);
            Point3d leftOrigin = new Point3d(fw + gap, 0, 0);
            Point3d topOrigin = new Point3d(0, maxH + gap, 0);

            BoundingBox frontPlaced = TranslateBox(frontBb, frontOrigin);
            BoundingBox leftPlaced = TranslateBox(leftBb, leftOrigin);
            BoundingBox topPlaced = TranslateBox(topBb, topOrigin);

            BoundingBox combinedBb = BoundingBox.Empty;
            combinedBb.Union(frontPlaced);
            combinedBb.Union(leftPlaced);
            combinedBb.Union(topPlaced);

            List<Curve> frontFrame = BoxToRect(frontPlaced);
            List<Curve> leftFrame = BoxToRect(leftPlaced);
            List<Curve> topFrame = BoxToRect(topPlaced);

            InstanceObject frameObj = doc.Objects.FindId(frameGuid.Value) as InstanceObject;
            if (frameObj == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "找不到图框块实例");
                return;
            }

            InstanceDefinition def = frameObj.InstanceDefinition;
            Curve drawingAreaRect = GetDrawingAreaRectangle(doc, def);
            BoundingBox drawingAreaBb;
            if (drawingAreaRect != null)
            {
                drawingAreaBb = drawingAreaRect.GetBoundingBox(true);
            }
            else
            {
                drawingAreaBb = GetDefinitionBoundingBox(doc, def);
            }

            if (!drawingAreaBb.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法计算图框绘图区的包围盒");
                return;
            }

            double defW = drawingAreaBb.Max.X - drawingAreaBb.Min.X;
            double defH = drawingAreaBb.Max.Y - drawingAreaBb.Min.Y;
            if (defW < tol || defH < tol)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "图框绘图区尺寸无效");
                return;
            }

            double combinedW = combinedBb.Max.X - combinedBb.Min.X + 2 * frameMargin;
            double combinedH = combinedBb.Max.Y - combinedBb.Min.Y + 2 * frameMargin;

            double usedScale = frameScale;
            if (usedScale <= tol)
            {
                double scaleX = combinedW / defW;
                double scaleY = combinedH / defH;
                usedScale = Math.Min(scaleX, scaleY);
            }

            double scaledDimOffset = dimOffset * usedScale;
            Plane dimPlane = new Plane(Point3d.Origin, Vector3d.ZAxis);
            List<GeometryBase> frontDims = MakeDims(doc, frontPlaced, dimPlane, scaledDimOffset);
            List<GeometryBase> leftDims = MakeDims(doc, leftPlaced, dimPlane, scaledDimOffset);
            List<GeometryBase> topDims = MakeDims(doc, topPlaced, dimPlane, scaledDimOffset);

            Point3d frameCenter = combinedBb.Center;
            double scaledFrameW = combinedW * usedScale;
            double scaledFrameH = combinedH * usedScale;
            Point3d frameBottomLeft = new Point3d(frameCenter.X - scaledFrameW / 2.0, frameCenter.Y - scaledFrameH / 2.0, 0);
            Point3d frameTargetBottomLeft = insertPt;
            Vector3d frameOffset = frameTargetBottomLeft - frameBottomLeft;

            Transform scaleTransform = Transform.Scale(frameCenter, usedScale);
            Transform moveTransform = Transform.Translation(frameOffset);
            Transform combined = moveTransform * scaleTransform;

            Guid frameId = doc.Objects.AddInstanceObject(def.Index, combined, new ObjectAttributes());
            if (frameId == Guid.Empty)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "插入图框失败");
                return;
            }

            RhinoObject insertedFrame = doc.Objects.FindId(frameId);
            if (insertedFrame != null)
            {
                foreach (RhinoObject child in def.GetObjects())
                {
                    if (child is TextObject textObj && textObj.TextGeometry != null)
                    {
                        int idx = FindAttrIndex(def, textObj);
                        if (idx >= 0 && idx < attrs.Count)
                        {
                            textObj.TextGeometry.PlainText = attrs[idx];
                            textObj.CommitChanges();
                        }
                    }
                }
            }

            Transform moveAll = Transform.Translation(insertPt - new Point3d(0, 0, 0));
            frontDims = frontDims.Select(g => { g.Transform(moveAll); return g; }).ToList();
            leftDims = leftDims.Select(g => { g.Transform(moveAll); return g; }).ToList();
            topDims = topDims.Select(g => { g.Transform(moveAll); return g; }).ToList();
            frontFrame = frontFrame.Select(c => { c.Transform(moveAll); return c; }).ToList();
            leftFrame = leftFrame.Select(c => { c.Transform(moveAll); return c; }).ToList();
            topFrame = topFrame.Select(c => { c.Transform(moveAll); return c; }).ToList();

            DA.SetDataList(0, frontDims);
            DA.SetDataList(1, leftDims);
            DA.SetDataList(2, topDims);
            DA.SetDataList(3, frontFrame);
            DA.SetDataList(4, leftFrame);
            DA.SetDataList(5, topFrame);
            DA.SetData(6, insertedFrame);
            DA.SetData(7, usedScale);
        }

        private BoundingBox ComputeBox(List<Curve> curves)
        {
            BoundingBox bb = BoundingBox.Empty;
            foreach (Curve c in curves)
            {
                if (c != null)
                    bb.Union(c.GetBoundingBox(true));
            }
            return bb;
        }

        private BoundingBox TranslateBox(BoundingBox src, Point3d targetOrigin)
        {
            Point3d srcMin = src.Min;
            Vector3d offset = targetOrigin - srcMin;
            Transform xform = Transform.Translation(offset);
            BoundingBox result = src;
            result.Transform(xform);
            return result;
        }

        private List<Curve> BoxToRect(BoundingBox bb)
        {
            List<Curve> lines = new List<Curve>();
            if (!bb.IsValid) return lines;

            Point3d a = new Point3d(bb.Min.X, bb.Min.Y, 0);
            Point3d b = new Point3d(bb.Max.X, bb.Min.Y, 0);
            Point3d c = new Point3d(bb.Max.X, bb.Max.Y, 0);
            Point3d d = new Point3d(bb.Min.X, bb.Max.Y, 0);

            lines.Add(new LineCurve(a, b));
            lines.Add(new LineCurve(b, c));
            lines.Add(new LineCurve(c, d));
            lines.Add(new LineCurve(d, a));
            return lines;
        }

        private List<GeometryBase> MakeDims(RhinoDoc doc, BoundingBox bb, Plane plane, double offset)
        {
            List<GeometryBase> result = new List<GeometryBase>();
            if (!bb.IsValid) return result;

            Point3d min = bb.Min;
            Point3d max = bb.Max;

            Point2d extA = new Point2d(min.X, min.Y);
            Point2d extB = new Point2d(max.X, min.Y);
            Point2d extC = new Point2d(max.X, max.Y);

            Point2d txtH = new Point2d((min.X + max.X) / 2.0, min.Y - offset);
            Point2d txtV = new Point2d(max.X + offset, (min.Y + max.Y) / 2.0);

            LinearDimension hDim = new LinearDimension(plane, extA, extB, txtH);
            LinearDimension vDim = new LinearDimension(plane, extB, extC, txtV);

            result.Add(hDim);
            result.Add(vDim);
            return result;
        }

        private BoundingBox GetDefinitionBoundingBox(RhinoDoc doc, InstanceDefinition def)
        {
            BoundingBox bb = BoundingBox.Empty;
            foreach (RhinoObject obj in def.GetObjects())
            {
                bb.Union(obj.Geometry.GetBoundingBox(true));
            }
            return bb;
        }

        private Curve GetDrawingAreaRectangle(RhinoDoc doc, InstanceDefinition def)
        {
            int layerIndex = FindLayerIndex(doc, "图框绘图区");
            if (layerIndex < 0) return null;

            foreach (RhinoObject obj in def.GetObjects())
            {
                if (obj.Attributes.LayerIndex == layerIndex && obj.Geometry is Curve curve)
                {
                    return curve;
                }
            }
            return null;
        }

        private int FindLayerIndex(RhinoDoc doc, string layerName)
        {
            Layer layer = doc.Layers.FindName(layerName);
            if (layer != null)
                return layer.Index;
            return -1;
        }

        private int FindAttrIndex(InstanceDefinition def, TextObject textObj)
        {
            RhinoObject[] objs = def.GetObjects();
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i].Id == textObj.Id)
                    return i;
            }
            return -1;
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_Block"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("B2C3D4E5-F6A7-8901-BCDE-F12345678901"); }
        }
    }
}
