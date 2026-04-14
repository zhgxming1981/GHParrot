using AutoCADFunction;
using CommonFunction.Hardware;
using Grasshopper.Kernel;
//using NS_Parrot.Properties;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using rd = Rhino.NodeInCode;
using parrot.Properties;

namespace NS_Parrot
{
    public class CADTxt2Rhino : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CADTxt2Rhino class.
        /// </summary>
        public CADTxt2Rhino()
          : base("CADTxt2Rhino", "Nickname",
              "把CAD中的文字导入到Rhino中",
              "Parrot", "ExcelCAD")
        {
        }


        private List<object> theObjectList = new List<object>();//从CAD中导入的对象
        public List<object> theBakeGeoList = new List<object>();//将要bake的对象


        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("pt", "pt", "CAD中的基点", GH_ParamAccess.item);

            Point3d origin = new Point3d(0, 0, 0);
            Vector3d X_axis = new Vector3d(1, 0, 0);
            Vector3d Y_axis = new Vector3d(0, 1, 0);
            Plane plane = new Plane(origin, X_axis, Y_axis);
            pManager.AddPlaneParameter("PL", "PL", "Rhion中的局部坐标平面", GH_ParamAccess.item, plane);

            pManager.AddNumberParameter("fa", "fa", "文字放大倍数，默认为1", GH_ParamAccess.item, 1);
            //pManager.AddTextParameter("Layer", "La", "Bake的目标图层", GH_ParamAccess.item, "AutoCADText");
            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Geo", "Geo", "Geo", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            theBakeGeoList.Clear();
            Point3d insert = new Point3d(0, 0, 0);
            DA.GetData(0, ref insert);

            Vector3d X_axis = new Vector3d(1, 0, 0);
            Vector3d Y_axis = new Vector3d(0, 1, 0);

            Plane plane1 = new Plane(insert, X_axis, Y_axis);
            DA.GetData(1, ref plane1);

            if (insert == null || plane1 == null) { return; }//如果输入数据错误，退出

            Plane plane2 = new Plane(insert, X_axis, Y_axis);

            double factor = 1;
            DA.GetData(2, ref factor);

            //string layerName = "";
            //if (!DA.GetData(2, ref layerName)) return;

            List<object> theObj_not_text = new List<object>();
            List<TextEntity> theObj_text = new List<TextEntity>();
            List<Plane> theObj_text_plane = new List<Plane>();
            foreach (var item in theObjectList)
            {
                if (item is TextEntity)
                {
                    TextEntity textEntity = (TextEntity)item;
                    theObj_text.Add(textEntity);
                    theObj_text_plane.Add(textEntity.Plane);
                }
                //else
                //{
                //    TextEntity textEntity = (TextEntity)item;
                //    theObj_text.Add(textEntity);
                //    theObj_text_plane.Add(textEntity.Plane);
                //}
            }

            var func_info1 = rd.Components.FindComponent("Orient");//将截面对齐到法线
            var func1 = func_info1.Delegate as dynamic;
            var plane_orient = func1(theObj_text_plane, plane2, plane1)[0];





            if (plane_orient != null)
            {
                for (int i = 0; i < theObj_text.Count; i++)
                {
                    //theObj_text[i].Plane = geo2[i];
                    string text = theObj_text[i].PlainText;
                    //Plane plane = theObj_text[i].Plane;
                    double height = factor * theObj_text[i].TextHeight;

                    var func_info2 = rd.Components.FindComponent("RotatePlane");//旋转平面
                    var func2 = func_info2.Delegate as dynamic;
                    var plane_rotate = func2(plane_orient[i], theObj_text[i].TextRotationRadians)[0];

                    Text3d t3d = new Text3d(text, plane_rotate, height);
                    t3d.HorizontalAlignment = theObj_text[i].TextHorizontalAlignment;
                    t3d.VerticalAlignment = theObj_text[i].TextVerticalAlignment;
                    Vector3d rotateAxis = theObj_text[i].Plane.Normal;
                    t3d.TextPlane.Rotate(theObj_text[i].TextRotationRadians, rotateAxis);


                    Text3dGoo tg = new Text3dGoo(t3d);

                    theBakeGeoList.Add(tg);
                }
            }

            DA.SetDataList(0, theBakeGeoList);
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


        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            ToolStripMenuItem item0 = new ToolStripMenuItem();
            item0.Text = "连接CAD";
            item0.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item0);
            item0.Click += ConnectAutoCAD;

            ToolStripMenuItem item1 = new ToolStripMenuItem();
            item1.Text = "获取CAD元素";
            item1.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item1);
            item1.Click += GetEntityFromAutoCAD;
        }

        void ConnectAutoCAD(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        {
            AutoCADTool.ConnectCAD();
        }

        void GetEntityFromAutoCAD(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        {
            theObjectList = AutoCADTool.CAD2Rhino().;
            ExpireSolution(true);//告诉系统，电池需要重新计算
        }


        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("33749F3E-7250-44FA-BEE6-850790F5245F"); }
        }
    }
}