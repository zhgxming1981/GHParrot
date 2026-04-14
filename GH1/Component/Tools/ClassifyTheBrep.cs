using System;
using System.Collections.Generic;
using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Rhino.Geometry;
using rd = Rhino.NodeInCode;


namespace Parrot
{
    public class ClassifyTheBrep : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ClassifyTheBrep class.
        /// </summary>
        public ClassifyTheBrep()
          : base("Brep分类", "Brep分类",
              "Brep分类",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Brep", "Brep", "Brep", GH_ParamAccess.list);
            pManager.AddTextParameter("Key", "Key", "Key", GH_ParamAccess.list);
            //pManager.AddTextParameter("Value", "Value", "Value", GH_ParamAccess.list);
            pManager.AddTextParameter("前缀", "前缀", "前缀", GH_ParamAccess.item);
            pManager.AddIntegerParameter("开始序号", "开始序号", "开始序号", GH_ParamAccess.item, 1);
            //pManager.AddNumberParameter("序号长度", "序号长度", "序号长度", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Brep", "Brep", "Brep", GH_ParamAccess.list);
            pManager.AddTextParameter("序号", "序号", "序号", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<Brep> breps = new List<Brep>();
            DA.GetDataList(0, breps);

            List<string> keys = new List<string>();
            DA.GetDataList(1, keys);

            string qz = "";
            DA.GetData(2, ref qz);

            int start = 0;
            DA.GetData(3, ref start);




        }



        private string GetData(Brep br)
        {
            var func_info1 = rd.Components.FindComponent("AreaProperties");
            var func1 = func_info1.Delegate as dynamic;
            dynamic area = func1(br)[0];
            dynamic center_of_gravity = func1(br)[1];

            var func_info2 = rd.Components.FindComponent("DeconstructBrep");
            var func2 = func_info2.Delegate as dynamic;
            dynamic faces = func2(br)[0];
            dynamic edges = func2(br)[1];
            dynamic vertices = func2(br)[2];


            var func_info3 = rd.Components.FindComponent("Plane3Pt");
            var func3 = func_info3.Delegate as dynamic;
            dynamic plane= func3(br)[0];
            return "";

            //var func_info1 = rd.Components.FindComponent("DeconstructBrep");//分解曲面
            //var func1 = func_info1.Delegate as dynamic;
            //Surface[] surfaces = func1(brep)[0];
            //var func_info2 = rd.Components.FindComponent("DivideCurve");//等分曲线
            //var func2 = func_info2.Delegate as dynamic;
            //var point = func2(grid, count, false)[0];
            //dynamic center_of_gravity = func2(br)[1];
            //Point3d center_of_gravity=br.
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
            get { return new Guid("D307C7E4-BB0B-43AB-96E9-6C3CDB94A190"); }
        }
    }
}