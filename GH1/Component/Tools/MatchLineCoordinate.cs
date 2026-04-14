using System;
using System.Collections.Generic;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class MatchLineCoordinate : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MatchLineCoordinate class.
        /// </summary>
        public MatchLineCoordinate()
          : base("MatchLineCoordinate", "坐标匹配",
              "坐标匹配，x1,y1,z1,x2,y2,z2",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("参考对象", "参考线", "参考线", GH_ParamAccess.item);
            pManager.AddCurveParameter("修改对象", "修改线", "要修改的对象", GH_ParamAccess.item);
            Point3d origin = new Point3d(0, 0, 0);
            Vector3d Z = new Vector3d(0, 0, 1);
            pManager.AddPlaneParameter("工作平面", "工作平面", "局部坐标平面", GH_ParamAccess.item, new Plane(origin, Z));
            pManager.AddTextParameter("指令", "指令", "如：x1=r.x1", GH_ParamAccess.item);
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //pManager.AddLineParameter("修改后对象", "结果", "修改后对象", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;


            Line a = new Line();
            if (!DA.GetData(0, ref a)) { return; }


            GH_Guid guid = null;//此类型可包容rhino几何体
            if (!DA.GetData(1, ref guid)) { return; }

            PolylineCurve b = new PolylineCurve();
            var b1 = Rhino.RhinoDoc.ActiveDoc.Objects.FindId(guid.Value);
            b = (PolylineCurve)b1.Geometry;



            Plane ucs = new Plane();
            if (!DA.GetData(2, ref ucs)) { return; }

            string MatchLineCoordinate_AxisName = "";
            if (!DA.GetData(3, ref MatchLineCoordinate_AxisName)) { return; }
            MatchLineCoordinate_AxisName = MatchLineCoordinate_AxisName.ToLower();
            string[] code = MatchLineCoordinate_AxisName.Split('=');

            Rhino.Geometry.Point3d Pa_s0 = a.From;
            Rhino.Geometry.Point3d Pa_e0 = a.To;
            Rhino.Geometry.Point3d Pb_s0 = b.PointAtStart;
            Rhino.Geometry.Point3d Pb_e0 = b.PointAtEnd;

            Point3d Pa_s = MyTransform.PointToUCS(Pa_s0, ucs);
            Point3d Pa_e = MyTransform.PointToUCS(Pa_e0, ucs);

            Line myLine = new Line();
            double coord = double.NaN;
            if (code[1].Contains("r."))
            {
                myLine = new Line(Pa_s, Pa_e);
                coord = GetPointByName(code[1], ref myLine);
            }
            else if (code[1].Contains("m."))
            {
                Point3d Pb_s = MyTransform.PointToUCS(Pb_s0, ucs);
                Point3d Pb_e = MyTransform.PointToUCS(Pb_e0, ucs);
                myLine = new Line(Pb_s, Pb_e);
                coord = GetPointByName(code[1], ref myLine);
            }
            else if (double.TryParse(code[1], out coord))
            {
                //啥都不做
            }
            else
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "非法输入");
                return;
            }


            SetPointByName(code[0], ref b, coord, ucs);
            Rhino.RhinoDoc.ActiveDoc.Objects.Replace(guid.Value, b);//非常重要，否则没有任何修改效果

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


        void SetPointByName(string name, ref PolylineCurve b, double coord)
        {
            name = name.ToLower();
            switch (name)
            {
                case "m.x1"://起点
                    b.SetStartPoint(new Point3d(coord, b.PointAtStart.Y, b.PointAtStart.Z));
                    break;
                case "m.y1":
                    b.SetStartPoint(new Point3d(b.PointAtStart.X, coord, b.PointAtStart.Z));
                    break;
                case "m.z1":
                    b.SetStartPoint(new Point3d(b.PointAtStart.X, b.PointAtStart.Y, coord));
                    break;


                case "m.x2"://终点
                    b.SetEndPoint(new Point3d(coord, b.PointAtEnd.Y, b.PointAtEnd.Z));
                    break;
                case "m.y2":
                    b.SetEndPoint(new Point3d(b.PointAtEnd.X, coord, b.PointAtEnd.Z));
                    break;
                case "m.z2":
                    b.SetEndPoint(new Point3d(b.PointAtEnd.X, b.PointAtEnd.Y, coord));
                    break;


                case "m.x3"://中点
                    b.Translate(coord - 0.5 * (b.PointAtStart.X + b.PointAtEnd.X), 0, 0);
                    break;
                case "m.y3":
                    b.Translate(0, coord - 0.5 * (b.PointAtStart.Y + b.PointAtEnd.Y), 0);
                    break;
                case "m.z3":
                    b.Translate(0, 0, coord - 0.5 * (b.PointAtStart.Z + b.PointAtEnd.Z));
                    break;
                default:
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "非法输入");
                    break;
            }
        }

        void SetPointByName(string name, ref PolylineCurve b, double coord, Plane ucs)
        {
            Point3d Pb_s = MyTransform.PointToUCS(b.PointAtStart, ucs);
            Point3d Pb_e = MyTransform.PointToUCS(b.PointAtEnd, ucs);
            Point3d pt = new Point3d();
            Vector3d vct = new Vector3d();
            switch (name)
            {
                case "x1"://起点
                    pt = MyTransform.PointToWCS(new Point3d(coord, Pb_s.Y, Pb_s.Z), ucs);
                    b.SetStartPoint(pt);
                    break;
                case "y1":
                    pt = MyTransform.PointToWCS(new Point3d(Pb_s.X, coord, Pb_s.Z), ucs);
                    b.SetStartPoint(pt);
                    break;
                case "z1":
                    pt = MyTransform.PointToWCS(new Point3d(Pb_s.X, Pb_s.Y, coord), ucs);
                    b.SetStartPoint(pt);
                    break;


                case "x2"://终点
                    pt = MyTransform.PointToWCS(new Point3d(coord, Pb_e.Y, Pb_e.Z), ucs);
                    b.SetEndPoint(pt);
                    break;
                case "y2":
                    pt = MyTransform.PointToWCS(new Point3d(Pb_e.X, coord, Pb_e.Z), ucs);
                    b.SetEndPoint(pt);
                    break;
                case "z2":
                    pt = MyTransform.PointToWCS(new Point3d(Pb_e.X, Pb_e.Y, coord), ucs);
                    b.SetEndPoint(pt);
                    break;


                case "x3"://中点
                    vct = MyTransform.VectorToWCS(new Vector3d(coord - 0.5 * (Pb_s.X + Pb_e.X), 0, 0), ucs);
                    b.Translate(vct);
                    break;
                case "y3":
                    vct = MyTransform.VectorToWCS(new Vector3d(0, coord - 0.5 * (Pb_s.Y + Pb_e.Y), 0), ucs);
                    b.Translate(vct);
                    break;
                case "z3":
                    vct = MyTransform.VectorToWCS(new Vector3d(0, 0, coord - 0.5 * (Pb_s.Z + Pb_e.Z)), ucs);
                    b.Translate(vct);
                    break;
                default:
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "非法输入");
                    break;
            }
        }

        double GetPointByName(string name, ref Line a)
        {
            switch (name)
            {
                case "r.x1"://起点
                    return a.FromX;
                case "r.y1":
                    return a.FromY;
                case "r.z1":
                    return a.FromZ;


                case "r.x2"://终点
                    return a.ToX;
                case "r.y2":
                    return a.ToY;
                case "r.z2":
                    return a.ToZ;


                case "r.x3"://中点
                    return 0.5 * (a.FromX + a.ToX);
                case "r.y3":
                    return 0.5 * (a.FromY + a.ToY);
                case "r.z3":
                    return 0.5 * (a.FromZ + a.ToZ);


                case "m.x1"://起点
                    return a.FromX;
                case "m.y1":
                    return a.FromY;
                case "m.z1":
                    return a.FromZ;


                case "m.x2"://终点
                    return a.ToX;
                case "m.y2":
                    return a.ToY;
                case "m.z2":
                    return a.ToZ;


                case "m.x3"://中点
                    return 0.5 * (a.FromX + a.ToX);
                case "m.y3":
                    return 0.5 * (a.FromY + a.ToY);
                case "m.z3":
                    return 0.5 * (a.FromZ + a.ToZ);

                default:
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "非法输入");
                    return double.NaN;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("E6098078-36B6-44E5-8955-B65A935F4C1E"); }
        }
    }
}