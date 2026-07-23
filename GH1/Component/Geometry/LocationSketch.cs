using CatiaFunction;
using CommonFunction;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NS_Parrot
{
    public class LocationSketch : GH_Component
    {
        public LocationSketch()
          : base("定位草图", "定位草图",
              "将 Rhino 平面曲线按原位置和方向导入 CATIA，创建定位草图。",
              "Parrot", "Geometry")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("曲线", "C", "必须共面的 Rhino 曲线。", GH_ParamAccess.list);
            pManager.AddPlaneParameter("平面", "P", "定位平面；未输入时从第一条曲线取得。", GH_ParamAccess.item);
            pManager[1].Optional = true;
            pManager.AddTextParameter("名称", "N", "CATIA 草图名称。", GH_ParamAccess.item, "定位草图");
            pManager.AddBooleanParameter("切线", "T", "NURBS 曲线是否写入切线信息。", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("执行", "R", "为 True 时创建 CATIA 定位草图。", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("状态", "S", "执行结果或错误信息。", GH_ParamAccess.item);
            pManager.AddTextParameter("草图名", "K", "已创建的 CATIA 草图名称。", GH_ParamAccess.item);
            pManager.AddPlaneParameter("平面", "P", "实际使用的定位平面。", GH_ParamAccess.item);
            pManager.AddIntegerParameter("数量", "N", "已导入曲线数量。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality()) return;

            var curves = new List<Curve>();
            if (!DA.GetDataList(0, curves) || curves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "请至少输入一条曲线。");
                return;
            }

            Plane plane = Plane.Unset;
            bool hasPlane = DA.GetData(1, ref plane);
            string name = "定位草图";
            bool hasTangent = true;
            bool execute = false;
            DA.GetData(2, ref name);
            DA.GetData(3, ref hasTangent);
            DA.GetData(4, ref execute);

            const double fallbackTolerance = 0.001;
            double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? fallbackTolerance;
            if (!hasPlane && !curves[0].TryGetPlane(out plane, tolerance))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "第一条曲线不是平面曲线，无法自动取得定位平面。");
                return;
            }

            if (!ValidateCurves(curves, plane, tolerance)) return;

            DA.SetData(2, plane);
            if (!execute)
            {
                DA.SetData(0, "未执行：将“执行”设为 True 后创建 CATIA 定位草图。");
                return;
            }

            try
            {
                var catia = new Common4Catia();
                catia.OpenEditOnPlane(name, plane);
                foreach (Curve curve in curves)
                {
                    Curve localCurve = curve.DuplicateCurve();
                    localCurve.Transform(Transform.PlaneToPlane(plane, Plane.WorldXY));
                    AddCurve(catia, localCurve, hasTangent);
                }
                catia.CloseEdit();

                DA.SetData(0, "已在 CATIA 中创建并刷新定位草图。");
                DA.SetData(1, name);
                DA.SetData(3, curves.Count);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(0, "创建失败：" + ex.Message);
            }
        }

        private bool ValidateCurves(List<Curve> curves, Plane plane, double tolerance)
        {
            for (int i = 0; i < curves.Count; i++)
            {
                if (curves[i] == null || !curves[i].IsValid || !curves[i].TryGetPlane(out Plane curvePlane, tolerance))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"第 {i + 1} 条曲线不是有效的平面曲线。");
                    return false;
                }

                if (curvePlane.Normal.IsParallelTo(plane.Normal, RhinoMath.ToRadians(0.01)) == 0 ||
                    Math.Abs(plane.DistanceTo(curves[i].PointAtStart)) > tolerance ||
                    Math.Abs(plane.DistanceTo(curves[i].PointAtEnd)) > tolerance)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"第 {i + 1} 条曲线不在定位平面上。");
                    return false;
                }
            }
            return true;
        }

        private static void AddCurve(Common4Catia catia, Curve curve, bool hasTangent)
        {
            if (curve is LineCurve line) catia.AddLine(line);
            else if (curve is ArcCurve arc) catia.AddArc(arc);
            else if (curve is PolylineCurve polyline) catia.AddPolyline(polyline);
            else if (curve is PolyCurve polyCurve)
            {
                foreach (Curve segment in polyCurve.Explode()) AddCurve(catia, segment, hasTangent);
            }
            else if (curve is NurbsCurve nurbs)
            {
                if (nurbs.IsArc() && nurbs.TryGetArc(out Arc nurbsArc)) catia.AddArc(new ArcCurve(nurbsArc));
                else if (nurbs.IsPolyline() && nurbs.TryGetPolyline(out Polyline nurbsPolyline)) catia.AddPolyline(nurbsPolyline.ToPolylineCurve());
                else catia.AddNurbsCurve(nurbs, hasTangent);
            }
            else throw new NotSupportedException("不支持的曲线类型：" + curve.GetType().Name);
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Sketch");

        public override Guid ComponentGuid => new Guid("8E4A4C1D-1536-4DE8-B67F-2C18ACB69B5E");
    }
}
