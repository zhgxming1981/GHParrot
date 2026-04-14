using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using rd = Rhino.NodeInCode;
using Grasshopper;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;
using parrot.Properties;

namespace NS_Parrot
{
    public class MoveWithUCS : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MoveWithUCS class.
        /// </summary>
        public MoveWithUCS()
          : base("MoveWithUCS", "MoveUCS",
              "沿用户坐标轴移动",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("几何体", "G1", "要移动的几何体", GH_ParamAccess.list);
            pManager.AddNumberParameter("ux", "ux", "用户坐标系中x方向移动量", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("uy", "uy", "用户坐标系中y方向移动量", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("uz", "uz", "用户坐标系中z方向移动量", GH_ParamAccess.item, 0);
            pManager.AddPlaneParameter("平面", "UCS", "用户坐标系", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("几何体", "G2", "移动后的几何体", GH_ParamAccess.list);
            pManager.AddVectorParameter("几何体", "X", "变换向量", GH_ParamAccess.item);
        }

       
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;


            //GeometryBase geo1 = null;不能用此类型，否则无法运用于平面这类非实体
            List<Grasshopper.Kernel.Types.GH_GeometricGooWrapper> geo1 = new List<Grasshopper.Kernel.Types.GH_GeometricGooWrapper>();//可以包含平面类型  
            if (!DA.GetDataList(0, geo1)) { return; }

            double x = 0, y = 0, z = 0;
            if (!DA.GetData(1, ref x)) { return; }
            if (!DA.GetData(2, ref y)) { return; }
            if (!DA.GetData(3, ref z)) { return; }

            Plane UCS = new Plane();
            if (!DA.GetData(4, ref UCS)) { return; }

            Vector3d V1 = new Vector3d(x, y, z);
            Vector3d V2 = MyTransform.VectorToWCS(V1, UCS);

            var func_info1 = rd.Components.FindComponent("Move");//生成平面
            var func1 = func_info1.Delegate as dynamic;
            var result = func1(geo1, V2);

            DA.SetDataList(0, result[0]);
            DA.SetData(1, V2);
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
                return Resources.moveUCS;
                //return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("AB440610-524C-4822-8824-D8ADDCDDD4F9"); }
        }
    }
}