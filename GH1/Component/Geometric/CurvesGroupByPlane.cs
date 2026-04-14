using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;


namespace NS_Parrot
{
    class P_C//平面和在平面上的曲线
    {
        public Plane plane;
        public List<Curve> curveList = new List<Curve>();
    }

    public class CurvesGroupByPlane : GH_Component
    {

        /// <summary>
        /// Initializes a new instance of the GroupThePlanes class.
        /// </summary>
        public CurvesGroupByPlane()
          : base("曲线合并", "曲线合并",
              "合并后的曲线",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("C", "C", "曲线", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("C1", "C1", "合并后的曲线", GH_ParamAccess.list);
            pManager.AddCurveParameter("C2", "C2", "合并前的共面曲线", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;
            List<Curve> crvList = new List<Curve>();
            if (!DA.GetDataList(0, crvList)) { return; }

            if (crvList.Count == 0) { return; }//曲线为空，直接返回

            Plane pla0;//填充第一个pla0
            crvList[0].TryGetPlane(out pla0);

            List<P_C> pc_list = new List<P_C>();
            P_C pc0 = new P_C();
            pc0.plane = pla0;
            pc_list.Add(pc0);

            int count_CrvList = crvList.Count;
            for (int k = 1; k < count_CrvList; k++)//crvList[0]前面处理过，所以k从1开始
            {
                for (int j = 0; j < pc_list.Count; j++)//找出所有的不同平面
                {
                    Plane pla_temp;
                    crvList[k].TryGetPlane(out pla_temp);//获取crvList[k]的平面
                    if (!IsRepeat(pla_temp, pc_list))
                    {
                        P_C pc = new P_C();
                        pc.plane = pla_temp;
                        pc_list.Add(pc);
                    }
                }
            }



            double distance;
            foreach (P_C pc in pc_list)//把所有曲线装进对应的plane上
            {       
                foreach (Curve crv in crvList)
                {
                    Plane pla_temp;
                    crv.TryGetPlane(out pla_temp);
                    if (CMath.IsEqPlane(pla_temp, pc.plane, 0.001, out distance) == 1 || CMath.IsEqPlane(pla_temp, pc.plane, 0.001, out distance) == -1)//必须用CMath.IsEqPlane2去判断，CMath.IsEqPlane不准确
                    {
                        pc.curveList.Add(crv);
                    }
                }
            }

            int i = 0;
            DataTree<Curve> retVal_tree = new DataTree<Curve>();
            List<Curve> retVal_list = new List<Curve>();
            foreach (P_C pc in pc_list)
            {
                Curve[] crv_temp = Curve.CreateBooleanUnion(pc.curveList, 0.01);
                foreach (Curve crv in crv_temp)
                {
                    retVal_list.Add(crv);
                }

                Grasshopper.Kernel.Data.GH_Path path = new Grasshopper.Kernel.Data.GH_Path(i++);
                foreach (Curve crv in pc.curveList)
                {
                    retVal_tree.Add(crv, path);
                }

            }
            DA.SetDataList(0, retVal_list);
            DA.SetDataTree(1, retVal_tree);
        }


        /// <summary>
        /// 判断平面p是否已经在pc_list中了
        /// </summary>
        /// <param name="p"></param>
        /// <param name="pc_list"></param>
        /// <returns></returns>
        private bool IsRepeat(Plane p, List<P_C> pc_list)
        {
            bool result = false;
            int count = pc_list.Count;
            double distance;
            for (int i = 0; i < count; i++)
            {
                if (CMath.IsEqPlane(p, pc_list[i].plane, 0.001, out distance) == 1 || CMath.IsEqPlane(p, pc_list[i].plane, 0.001, out distance) == -1)
                {
                    result = true;
                    break;
                }
            }
            return result;
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
            get { return new Guid("026B9DF0-D915-43E1-9F37-02BC9FBBB1AB"); }
        }
    }
}