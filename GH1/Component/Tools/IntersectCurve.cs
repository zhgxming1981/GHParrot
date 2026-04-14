using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using rd = Rhino.NodeInCode;
using Grasshopper;
using Rhino.Geometry.Intersect;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;
using parrot.Properties;


namespace NS_Parrot
{
    public class GroupTheIntersectingCurves : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the XiangJiaoQuXian class.
        /// </summary>
        public GroupTheIntersectingCurves()
          : base("相交曲线分组", "相交曲线",
              "将曲线按相交来分组",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("曲线", "C1", "曲线1", GH_ParamAccess.list);
            pManager.AddCurveParameter("曲线", "C2", "与曲线1相交的曲线", GH_ParamAccess.list);
            pManager.AddNumberParameter("误差", "D", "端点到曲线的最大距离", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("曲线", "C3", "曲线3（主）", GH_ParamAccess.tree);
            pManager.AddCurveParameter("曲线", "C4", "与曲线3相交的曲线（次）", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected void SolveInstance2(IGH_DataAccess DA)//被优化掉的方法
        {
            if (!CHardware.CheckLegality())
                return;

            List<Curve> c1 = new List<Curve>(), c2 = new List<Curve>();

            if (!DA.GetDataList(0, c1)) { return; }
            if (!DA.GetDataList(1, c2)) { return; }

            double D = 0;//读取误差
            if (!DA.GetData(2, ref D)) { return; }

            DataTree<Curve> c3 = new DataTree<Curve>();
            DataTree<Curve> c4 = new DataTree<Curve>();
            int i = 0;

            foreach (var item1 in c1)
            {
                Grasshopper.Kernel.Data.GH_Path path = new Grasshopper.Kernel.Data.GH_Path(i++);
                c3.Add(item1, path);
                foreach (var item2 in c2)
                {
                    var func_info = rd.Components.FindComponent("Curve|Curve");//这个XXX从步骤1中查找原名称，忽略空格
                    var func = func_info.Delegate as dynamic;
                    var pts = func(item1, item2)[0];//这个a,b是原本电池输入端，注意设置好输入端属性
                    if (pts != null || XiangJiao(item1, item2, D))
                    {
                        c4.Add(item2, path);
                    }
                    else
                    {
                        c4.Add((Curve)null, path);
                    }
                }
            }

            DA.SetDataTree(0, c3);
            DA.SetDataTree(1, c4);

        }


        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<Curve> c1 = new List<Curve>(), c2 = new List<Curve>();

            if (!DA.GetDataList(0, c1)) { return; }
            if (!DA.GetDataList(1, c2)) { return; }

            double tolerance = 0;//读取误差
            if (!DA.GetData(2, ref tolerance)) { return; }

            DataTree<Curve> c3 = new DataTree<Curve>();
            DataTree<Curve> c4 = new DataTree<Curve>();
            int i = 0;

            foreach (var item1 in c1)
            {
                Grasshopper.Kernel.Data.GH_Path path = new Grasshopper.Kernel.Data.GH_Path(i++);
                c3.Add(item1, path);
                foreach (var item2 in c2)
                {
                    CurveIntersections crvInt = Intersection.CurveCurve(item1, item2, tolerance, 0);

                    if (crvInt.Count > 0)//没有交点时，crvInt==0
                    {
                        c4.Add(item2, path);
                    }
                    else
                    {
                        c4.Add((Curve)null, path);//为了和c3一一匹配，即便是没有交点，也要用空值占位
                    }
                }
            }

            DA.SetDataTree(0, c3);
            DA.SetDataTree(1, c4);
        }


        /// <summary>
        /// 虽然没有真正相交，但是只要任意一个端点到另一个直线的距离小于d，则认为它们依然是相交的
        /// </summary>
        /// <param name="c1"></param>主线
        /// <param name="c2"></param>次线
        /// <param name="d"></param>误差
        /// <returns></returns>
        private bool XiangJiao(Curve c1, Curve c2, double d)
        {
            Point3d ps = c2.PointAtStart;
            Point3d pe = c2.PointAtEnd;
            double ds, de;//最近点的参数值
            c1.ClosestPoint(ps, out ds);
            c1.ClosestPoint(pe, out de);
            Point3d ps1 = c1.PointAt(ds);//通过参数值求点的坐标
            Point3d pe1 = c1.PointAt(de);//通过参数值求点的坐标
            double distance_s = ps1.DistanceTo(ps);
            double distance_e = pe1.DistanceTo(pe);
            if (distance_s <= d || distance_e <= d)
                return true;
            return false;
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
                return Resources.相交曲线;
                //return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("7B189696-0435-404E-BD57-03DCFF44E04A"); }
        }
    }
}