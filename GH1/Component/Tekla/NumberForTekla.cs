using System;
using System.Collections.Generic;
using rd = Rhino.NodeInCode;
using Grasshopper.Kernel;
using Rhino.Geometry;
using TSM = Tekla.Structures.Model;
using TSG = Tekla.Structures.Geometry3d;
//using GTLink.Types;
using Tekla;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class NumberForTekla : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the NumberForTekla class.
        /// </summary>
        public NumberForTekla()
          : base("NumberForTekla", "tekla编号",
              "Description",
              "Parrot", "Tekla")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("text", "text", "text", GH_ParamAccess.item);
            pManager.AddCurveParameter("curve", "curve", "curve", GH_ParamAccess.list);
            pManager.AddNumberParameter("num", "num", "num", GH_ParamAccess.item);
            pManager.AddPointParameter("point", "point", "point", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("curve", "curve", "curve", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string txt = "";
            if (!DA.GetData(0, ref txt)) { return; }

            List<Curve> curve = new List<Curve>();
            if (!DA.GetDataList(1, curve)) { return; }

            int num = 0;
            if (!DA.GetData(2, ref num)) { return; }

            List<Point3d> point = new List<Point3d>();
            if (!DA.GetDataList(3, point)) { return; }

            int count_t = txt.Length;
            int count_c = curve.Count;
            List<Curve> ret = new List<Curve>();//或取编号对应的曲线
            for (int i = 0; i < count_t; i++)
            {
                int m = Convert.ToInt32(txt[i]);
                ret.Add(curve[m]);
            }

            //获取字高和字宽

            //var func_info1 = rd.Components.FindComponent("Vector2Pt");//生成向量
            //var func1 = func_info1.Delegate as dynamic;

            //var func_info2 = rd.Components.FindComponent("Move");//移动
            //var func2 = func_info2.Delegate as dynamic;

            //var func_info3 = rd.Components.FindComponent("Move");//移动
            //var func3 = func_info3.Delegate as dynamic;


            //for (int i = 0; i < count_t; i++)
            //{

            //    Point3d p = GetLeftDownPoint(ret[i]);

            //    var vector = func1(p, point[i], true);
            //    var result = func2(ret[i], vector);

            //}






            TSM.Model myModel = new TSM.Model();
            //Tekla.Structures.app
            //TSG.Point p1 = new TSG.Point(startPoint.X, startPoint.Y, startPoint.Z);
            //TSG.Point p2 = new TSG.Point(endPoint.X, endPoint.Y, endPoint.Z);
            //Tekla.Structures.Model.Brep brep = new TSM.Brep(p1, p2);
            //brep.Profile = new TSM.Profile { ProfileString = shapeName };
            //brep.Position = position;
            //brep.Insert();
            //myModel.CommitChanges();
            //DA.SetData(0, brep);//第一个输出参数
            //TeklaBrep.Add(brep);
            //var app=Tekla.Structures.Model.Beam.






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
            get { return new Guid("C77EA0E4-4470-4ECF-AC7A-68C52F64D0E6"); }
        }

        protected Vector2d GetWidthHeightOfChar(Curve c)
        {
            Plane oxy = new Plane(0, 0, 0, 0);

            var func_info1 = rd.Components.FindComponent("PlaneThroughShape");//拆解曲面
            var func1 = func_info1.Delegate as dynamic;
            var b = func1(oxy, c, 0);

            var func_info2 = rd.Components.FindComponent("Rectangle");//拆解曲面
            var func2 = func_info2.Delegate as dynamic;
            Rectangle3d rec = func2(b);
            double width = rec.Width;
            double height = rec.Height;
            return new Vector2d(width, height);
        }
            //double ww=rec.

            //var func_info2 = rd.Components.FindComponent("Deconstruct");//拆解点
            //var func2 = func_info2.Delegate as dynamic;
            //var value = func2(b);
            //var x = value[0];
            //var y = value[1];

            //var func_info3 = rd.Components.FindComponent("SortList");//拆解曲面
            //var func3 = func_info3.Delegate as dynamic;
            //var v2 = func3(x + y, v)[2];

            //return v2[0];


        //    int n = v.Count;
        //    double min = v[0].X + v[0].Y;
        //    int min_index = 0;
        //    for (int i = 1; i < n; i++)
        //    {
        //        if (v[i].X + v[i].Y < min)
        //        {
        //            min = v[i].X + v[i].Y;
        //            min_index = i;
        //        }
        //    }
        //    return v[min_index];
        //}

    }
}