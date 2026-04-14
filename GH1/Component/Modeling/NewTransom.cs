using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using rd = Rhino.NodeInCode;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;
using parrot.Properties;

namespace NS_Parrot
{
    public class NewTransom : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the NewTransom class.
        /// </summary>
        public NewTransom()
          : base("NewTransom", "生成横梁",
              "Description",
              "Parrot", "建模")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("截面", "sec", "横梁截面", GH_ParamAccess.list);
            pManager.AddPointParameter("插入点", "int", "截面插入点", GH_ParamAccess.item);
            pManager.AddCurveParameter("分格线", "grd", "横梁分格线", GH_ParamAccess.item);
            pManager.AddIntegerParameter("拟合数量", "cnt", "大于等于1", GH_ParamAccess.item, 2);
            pManager.AddBrepParameter("完成面", "brp", "幕墙完成面", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Transom", "T", "横梁", GH_ParamAccess.item);
            //pManager.AddGenericParameter("Transom", "T", "横梁", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;


            List<Curve> section = new List<Curve>();
            if (!DA.GetDataList(0, section)) { return; }

            Point3d insert = new Point3d();
            if (!DA.GetData(1, ref insert)) { return; }

            Curve grid = null;
            if (!DA.GetData(2, ref grid)) { return; }

            int count = 0;
            if (!DA.GetData(3, ref count)) { return; }

            Brep brep = null;
            if (!DA.GetData(4, ref brep)) { return; }

            var func_info1 = rd.Components.FindComponent("XYPlane");//生成平面
            var func1 = func_info1.Delegate as dynamic;
            var plane = func1(insert)[0];

            var func_info2 = rd.Components.FindComponent("DivideCurve");//等分曲线
            var func2 = func_info2.Delegate as dynamic;
            var point = func2(grid, count, false)[0];

            var func_info3 = rd.Components.FindComponent("BrepClosestPoint");//求解曲面的法线
            var func3 = func_info3.Delegate as dynamic;
            var normal = func3(point, brep)[1];//第二个输出参数


            var func_info4 = rd.Components.FindComponent("Reverse");//反向法线
            var func4 = func_info4.Delegate as dynamic;
            var normal2 = func4(normal);//第一个输出参数


            var func_info5 = rd.Components.FindComponent("PerpFrames");//求解曲面的法线
            var func5 = func_info5.Delegate as dynamic;
            var frames = func5(grid, count, true)[0];

            var func_info6 = rd.Components.FindComponent("AlignPlane");//对齐坐标平面
            var func6 = func_info6.Delegate as dynamic;
            var plane2 = func6(frames, normal2)[0];

            var func_info7 = rd.Components.FindComponent("LoftOptions");//将截面对齐到法线
            var func7 = func_info7.Delegate as dynamic;
            var option = func7(false, false, 0, 0.001, 0)[0];

            var func_info8 = rd.Components.FindComponent("Orient");//将截面对齐到法线
            var func8 = func_info8.Delegate as dynamic;
            var geo = func8(section, plane, plane2)[0];

            var func_info9 = rd.Components.FindComponent("Loft");//放样
            var func9 = func_info9.Delegate as dynamic;
            var loft = func9(geo, option)[0];

            DA.SetData(0, loft);
        }





        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            SolutionExpired += (sender, args) =>
            {
                ((GH_Component)sender).Params.Input[0].DataMapping = GH_DataMapping.Graft;
            };
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
                //return null;
                return Resources.Transom;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("CBC09FA0-9CDA-4B13-89FE-C65604D11222"); }
        }
    }
}