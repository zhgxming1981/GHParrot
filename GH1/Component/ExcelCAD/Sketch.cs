using CatiaFunction;
using CommonFunction.Algorithm;
using CommonFunction.Hardware;
using CommonFunction.Transform;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Collections;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.ModelInternal;

namespace NS_Parrot
{
    public class Sketch : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Sketch class.
        /// </summary>
        public Sketch()
          : base("草图", "草图",
              "将Rhino中的草图导入到catia中",
              "Parrot", "ExcelCAD")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("曲线或点", "曲线或点", "曲线或点", GH_ParamAccess.list);
            pManager.AddTextParameter("草图名", "草图名", "草图名", GH_ParamAccess.item);
            pManager.AddBooleanParameter("切线", "切线", "如果曲线是NurbsCurve，是否启用切线命令", GH_ParamAccess.item, true);
            //pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("类型", "类型", "类型", GH_ParamAccess.list);
        }
        private CatiaFunction.Common4Catia catia;
        private bool hasTangent = true;
        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<GeometryBase> geometry = new List<GeometryBase>();

            DA.GetDataList(0, geometry);

            string sketchName = "";
            DA.GetData(1, ref sketchName);

            DA.GetData(2, ref hasTangent);

            catia = new Common4Catia();//连接catia

            List<string> typeName = new List<string>();
            int count = geometry.Count;
            for (int i = 0; i < count; i++)
            {
                if (geometry[i] is GeometryBase)
                {
                    typeName.Add(geometry[i].GetType().Name);
                }
            }

            DA.SetDataList(0, typeName);
            catia.OpenEdit(sketchName);

            DrawGeos(geometry);
            catia.CloseEdit();
        }

        private void DrawGeos(List<Rhino.Geometry.GeometryBase> geometry)
        {
            int count = geometry.Count;
            for (int i = 0; i < count; i++)
            {
                if (geometry[i] is Rhino.Geometry.Point)
                {
                    DrawPoint(((Point)geometry[i]).Location);
                }
                else if (geometry[i] is Rhino.Geometry.Curve)
                {
                    DrawCurve((Curve)geometry[i]);
                }
            }
        }

   

        private void DrawPoint(Rhino.Geometry.Point3d point)
        {
            catia.AddPoint(point);
        }


        private void DrawCurve(Curve curve)
        {
            if (curve is LineCurve)
            {
                catia.AddLine((LineCurve)curve);
            }
            else if (curve is ArcCurve)
            {
                NiShiZhen_ArcCurve((ArcCurve)curve);
                catia.AddArc((ArcCurve)curve);
            }
            else if (curve is PolylineCurve)
            {
                catia.AddPolyline((PolylineCurve)curve);
            }
            else if (curve is PolyCurve)
            {
                List<Curve> c_list = ((PolyCurve)curve).Explode().ToList();
                DrawCurves(c_list);
            }
            else if (curve is NurbsCurve)
            {
                if (curve.IsArc())
                {
                    Rhino.Geometry.Arc ar;
                    curve.TryGetArc(out ar);
                    ArcCurve arc_crv = new ArcCurve(ar);
                    NiShiZhen_ArcCurve(arc_crv);
                    catia.AddArc(arc_crv);
                }
                else if (curve.IsEllipse())
                {
                    Ellipse el;
                    Curve crv = curve;
                    curve.TryGetEllipse(out el);
                    double start_angle, end_angle;
                    GetEllipseAngle(crv, out start_angle, out end_angle);
                    NiShiZhen_Ellipse((NurbsCurve)crv);
                    catia.AddEllipse(el, crv.PointAtStart, crv.PointAtEnd, start_angle, end_angle);
                }
                else if (curve.IsPolyline())
                {
                    Polyline po;
                    curve.TryGetPolyline(out po);
                    catia.AddPolyline(po.ToPolylineCurve());
                }
                else
                {
                    catia.AddNurbsCurve((NurbsCurve)curve, hasTangent);
                }
            }
        }

        private void DrawCurves(List<Curve> curve_list)
        {
            int count = curve_list.Count;
            for (int i = 0; i < count; i++)
            {
                if (curve_list[i] is LineCurve)
                {
                    catia.AddLine((LineCurve)curve_list[i]);
                }
                else if (curve_list[i] is ArcCurve)
                {
                    NiShiZhen_ArcCurve((ArcCurve)curve_list[i]);
                    catia.AddArc((ArcCurve)curve_list[i]);
                    continue;
                }
                else if (curve_list[i] is PolylineCurve)
                {
                    catia.AddPolyline((PolylineCurve)curve_list[i]);
                }
                else if (curve_list[i] is PolyCurve)
                {
                    List<Curve> c_list = ((PolyCurve)curve_list[i]).Explode().ToList();
                    DrawCurves(c_list);
                }
                else if (curve_list[i] is NurbsCurve)
                {
                    if (curve_list[i].IsArc())
                    {
                        Rhino.Geometry.Arc ar;
                        curve_list[i].TryGetArc(out ar);
                        ArcCurve arc_crv = new ArcCurve(ar);
                        NiShiZhen_ArcCurve(arc_crv);
                        catia.AddArc(arc_crv);
                    }
                    else if (curve_list[i].IsEllipse())
                    {
                        Ellipse el;
                        Curve crv = curve_list[i];
                        curve_list[i].TryGetEllipse(out el);
                        double start_angle, end_angle;
                        GetEllipseAngle(crv, out start_angle, out end_angle);
                        NiShiZhen_Ellipse((NurbsCurve)crv);
                        catia.AddEllipse(el, crv.PointAtStart, crv.PointAtEnd, start_angle, end_angle);
                    }
                    else if (curve_list[i].IsPolyline())
                    {
                        Polyline po;
                        curve_list[i].TryGetPolyline(out po);
                        catia.AddPolyline(po.ToPolylineCurve());
                    }
                    else
                    {
                        catia.AddNurbsCurve((NurbsCurve)curve_list[i], hasTangent);
                    }
                }
            }
        }

        private void GetEllipseAngle(Curve curve, out double start_angle, out double end_angle)
        {
            Ellipse el;
            curve.TryGetEllipse(out el);

            Plane plane = el.Plane;
            Point3d p1 = curve.PointAtStart;
            Point3d p2 = curve.PointAtEnd;

            Point3d ps = MyTransform.PointToUCS(p1, plane);
            Point3d pe = MyTransform.PointToUCS(p2, plane);

            start_angle = Math.Atan2(ps.Y, ps.X);
            end_angle = Math.Atan2(pe.Y, pe.X);
        }


        /// <summary>
        /// 将圆弧方向改为逆时针
        /// </summary>
        /// <param name="arc_crv"></param>
        private void NiShiZhen_ArcCurve(ArcCurve arc_crv)
        {
            Point3d p1 = arc_crv.PointAtStart;
            Point3d p2 = arc_crv.PointAtEnd;
            Point3d p3 = ((ArcCurve)arc_crv).Arc.Center;

            double angle = arc_crv.Arc.Angle;
            if (Math.Abs(angle - Math.PI) < 0.001)//当接近直线时，换一点
            {
                p2 = arc_crv.PointAtNormalizedLength(0.5);
            }

            Plane plane = new Plane(p3, new Vector3d(0, 0, 1));
            bool? A = CMath.ClockwiseOrAnticlockwise(p1, p2, plane);
            if (A != null && (bool)A)
            {
                arc_crv.Reverse();
            }
        }


        private void NiShiZhen_Ellipse(NurbsCurve arc_crv)
        {
            if (!arc_crv.IsEllipse()) return;
            Point3d p1 = arc_crv.PointAtStart;
            Point3d p2 = arc_crv.PointAtEnd;

            Ellipse el;
            arc_crv.TryGetEllipse(out el);

            Point3d p3 = el.Center;

            double start_angle, end_angle;
            GetEllipseAngle(arc_crv, out start_angle, out end_angle);
            if (Math.Abs((end_angle - start_angle) - Math.PI) < 0.001)//当接近直线时，换一点
            {
                p2 = arc_crv.PointAtNormalizedLength(0.5);
            }

            Plane plane = new Plane(p3, new Vector3d(0, 0, 1));
            bool? A = CMath.ClockwiseOrAnticlockwise(p1, p2, plane);
            if (A != null && (bool)A)
            {
                arc_crv.Reverse();
                GetEllipseAngle(arc_crv, out start_angle, out end_angle);//重新求起点角度和终点角度
            }
        }


        private void AddNurbsCurve(NurbsCurve curve)
        {
            NurbsCurvePointList pt_list = curve.Points;
            int count = pt_list.Count;
            Point3d[] controlPoint = new Point3d[count];
            Vector3d[] tangent = new Vector3d[count];
            for (int i = 0; i < count; i++)
            {
                double t;
                Point3d pt_tem = new Point3d(pt_list[i].X, pt_list[i].Y, pt_list[i].Z);
                curve.ClosestPoint(pt_tem, out t);
                controlPoint[i] = curve.PointAt(t);
                tangent[i] = curve.TangentAt(t);
            }
            catia.AddNurbsCurve2(controlPoint, tangent);
        }

        private void AddNurbsCurve2(NurbsCurve curve)
        {
            NurbsCurvePointList pt_list = curve.Points;
            int count = pt_list.Count;
            Point3d[] controlPoint = new Point3d[count];
            for (int i = 0; i < count; i++)
            {
                double t;
                Point3d pt_tem = new Point3d(pt_list[i].X, pt_list[i].Y, pt_list[i].Z);
                curve.ClosestPoint(pt_tem, out t);
                controlPoint[i] = curve.PointAt(t);
            }
            catia.AddNurbsCurve2(controlPoint);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("187B3337-911A-4951-A706-77C27520F2CC"); }
        }
    }
}