using Grasshopper;
using Grasshopper.Kernel;
using GH_IO.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using TSM = Tekla.Structures.Model;
using TSG = Tekla.Structures.Geometry3d;
using GTLink.Types;
using System.Windows.Forms;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class PutAShapeInTekla : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public PutAShapeInTekla()
          : base("放置形状", "放置形状",
            "可以用来放置埋件等",
            "Parrot", "Tekla")
        {
        }

        private List<TSM.Brep> TeklaModelObjectList = new List<TSM.Brep>();

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("ShapeName", "SN", "形状名", GH_ParamAccess.item);
            pManager.AddPointParameter("StartPoint", "SP", "起点", GH_ParamAccess.item);
            pManager.AddPointParameter("EndPoint", "EP", "终点", GH_ParamAccess.item);
            pManager.AddGenericParameter("Position", "P", "方位", GH_ParamAccess.item);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Shape", "S", "形状", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string shapeName = null;
            if (!DA.GetData(0, ref shapeName)) { return; }//第一个输入参数

            Point3d startPoint = new Point3d();
            if (!DA.GetData(1, ref startPoint)) { return; }//第二个输入参数


            Point3d endPoint = new Point3d();
            if (!DA.GetData(2, ref endPoint)) { return; }//第三个输入参数

            TSM.Position position;
            PositionGoo positionGoo = null;
            if (!DA.GetData(3, ref positionGoo))//第四个输入参数
            {
                return;
            }
            else
            {
                position = positionGoo.Value;
            }

            TSM.Model myModel = new TSM.Model();
            TSG.Point p1 = new TSG.Point(startPoint.X, startPoint.Y, startPoint.Z);
            TSG.Point p2 = new TSG.Point(endPoint.X, endPoint.Y, endPoint.Z);
            Tekla.Structures.Model.Brep brep = new TSM.Brep(p1, p2);
            brep.Profile = new TSM.Profile { ProfileString = shapeName };
            brep.Position = position;
            brep.Insert();
            myModel.CommitChanges();
            DA.SetData(0, brep);//第一个输出参数
            TeklaModelObjectList.Add(brep);


        }


        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null;
                //return null;

            }
        }
        //protected override System.Drawing.Bitmap Icon => null;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("C6474703-7609-4B6E-BC25-90392B7128A5");


        ///<summary>
        ///当电池被插入文档时，该事件将被执行
        /// </summary>
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            SolutionExpired += (sender, args) =>//当需要重新计算时，该事件将被执行
            {
                foreach (var item in TeklaModelObjectList)
                {
                    if (item != null)
                    {
                        item.Delete();
                    }
                }

                TeklaModelObjectList.Clear();
            };
        }



        public override string InstanceDescription
        {
            get
            {
                return "我的第一个GH组件！";
            }
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            ToolStripMenuItem item1 = new ToolStripMenuItem();
            item1.Text = "Bake to Tekla";
            menu.Items.Add(item1);
            item1.Click += new EventHandler((o, e) =>
            {
                TeklaModelObjectList.Clear();//切断GH和Tekla之间的关联
            });

            ToolStripMenuItem item2 = new ToolStripMenuItem();
            item2.Text = "选择Tekla对象";
            menu.Items.Add(item2);
            TSM.UI.ModelObjectSelector modelObjectSelector = new TSM.UI.ModelObjectSelector();
            item2.Click += new EventHandler((o, e) =>
            {
                TSM.Model myModel = new TSM.Model();
                System.Collections.ArrayList arrayList = new System.Collections.ArrayList(TeklaModelObjectList);
                modelObjectSelector.Select(arrayList);
                myModel.CommitChanges();
            });


            ToolStripMenuItem item3 = new ToolStripMenuItem();
            item3.Text = "删除Tekla对象";
            menu.Items.Add(item3);
            item3.Click += new EventHandler((o, e) =>
            {
                TSM.Model myModel = new TSM.Model();
                int count = TeklaModelObjectList.Count;
                for (int i = 0; i < count; i++)
                {
                    TeklaModelObjectList[i].Delete();
                    TeklaModelObjectList.Clear();
                }
                myModel.CommitChanges();
            });


        }
    }
}