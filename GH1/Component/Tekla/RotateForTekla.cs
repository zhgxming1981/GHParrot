using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using GTLink.Types;
using System.Windows.Forms;
using TSM = Tekla.Structures.Model;
using TSG = Tekla.Structures.Geometry3d;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class RotateForTekla : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the RotateForTekla class.
        /// </summary>
        public RotateForTekla()
          : base("RotateForTekla", "RotateForTekla",
              "RotateForTekla",
              "Parrot", "Tekla")
        {
        }

        private List<Tekla.Structures.Model.ModelObject> TeklaModelObjectList = new List<Tekla.Structures.Model.ModelObject>();

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("tekla对象", "tekla对象", "tekla对象", GH_ParamAccess.item);
            pManager.AddPlaneParameter("平面", "平面", "旋转平面", GH_ParamAccess.item);
            pManager.AddNumberParameter("角度", "角度", "角度，单位:rad", GH_ParamAccess.item);
            pManager.AddBooleanParameter("复制", "复制", "是否要复制", GH_ParamAccess.item, true);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("tekla对象", "tekla对象", "tekla对象", GH_ParamAccess.item);
        }


      


        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            GTLink.Types.TeklaModelObjectGoo modelObject = null;
            if (!DA.GetData(0, ref modelObject)) { return; }

            Plane plane = new Plane();
            if (!DA.GetData(1, ref plane)) { return; }

            double angel = double.NaN;
            if (!DA.GetData(2, ref angel)) { return; }

            bool isCopy = true;
            if (!DA.GetData(3, ref isCopy)) { return; }

            TSM.Model myModel = new TSM.Model();

            Tekla.Structures.Geometry3d.Point o1 = new Tekla.Structures.Geometry3d.Point(plane.OriginX, plane.OriginY, plane.OriginZ);
            Tekla.Structures.Geometry3d.Vector v1x = new Tekla.Structures.Geometry3d.Vector(plane.XAxis.X, plane.XAxis.Y, plane.XAxis.Z);
            Tekla.Structures.Geometry3d.Vector v1y = new Tekla.Structures.Geometry3d.Vector(plane.YAxis.X, plane.YAxis.Y, plane.YAxis.Z);
            Tekla.Structures.Geometry3d.CoordinateSystem c1 = new Tekla.Structures.Geometry3d.CoordinateSystem(o1, v1x, v1y);

            Tekla.Structures.Geometry3d.Vector v2x = new Tekla.Structures.Geometry3d.Vector(plane.XAxis.X * Math.Cos(angel) - plane.XAxis.Y * Math.Sin(angel), plane.XAxis.X * Math.Sin(angel) + plane.XAxis.Y * Math.Cos(angel), plane.XAxis.Z);
            Tekla.Structures.Geometry3d.Vector v2y = new Tekla.Structures.Geometry3d.Vector(plane.YAxis.X * Math.Cos(angel) - plane.YAxis.Y * Math.Sin(angel), plane.YAxis.X * Math.Sin(angel) + plane.YAxis.Y * Math.Cos(angel), plane.YAxis.Z);
            Tekla.Structures.Geometry3d.CoordinateSystem c2 = new Tekla.Structures.Geometry3d.CoordinateSystem(o1, v2x, v2y);


            if (isCopy)
            {
                GTLink.Types.TeklaModelObjectGoo modelObject2 = new TeklaModelObjectGoo();
                modelObject2.Value = Tekla.Structures.Model.Operations.Operation.CopyObject(modelObject.Value, c1, c2);
                TeklaModelObjectList.Add(modelObject2.Value);//建立GH和Tekla之间的关联
                DA.SetData(0, modelObject2);
            }
            else
            {
                Tekla.Structures.Model.Operations.Operation.MoveObject(modelObject.Value, c1, c2);
                TeklaModelObjectList.Add(modelObject.Value);//建立GH和Tekla之间的关联
                DA.SetData(0, modelObject);
            }

            //Tekla.Structures.Model.UI.ModelViewEnumerator views = Tekla.Structures.Model.UI.ViewHandler.GetVisibleViews();
            //while (views.MoveNext())
            //{
            //    Tekla.Structures.Model.UI.ViewHandler.RedrawView(views.Current);//刷新视图
            //}

            myModel.CommitChanges();
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
            get { return new Guid("815D1FF2-60A8-482A-A1E8-88B43393BF01"); }
        }


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