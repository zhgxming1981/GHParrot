using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Rhino.Geometry;
using GTLink.Types;
using TSM = Tekla.Structures.Model;
using TSG = Tekla.Structures.Geometry3d;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class MoveForTekla : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MoveForTekla class.
        /// </summary>
        public MoveForTekla()
          : base("MoveForTekla", "MoveForTekla",
              "MoveForTekla",
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
            pManager.AddVectorParameter("向量", "向量", "向量", GH_ParamAccess.item);
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

            Vector3d vec_g = new Vector3d();
            if (!DA.GetData(1, ref vec_g)) { return; }

            bool isCopy = true;
            if (!DA.GetData(2, ref isCopy)) { return; }

            TSM.Model myModel = new TSM.Model();

            Tekla.Structures.Geometry3d.Vector vec_t = new Tekla.Structures.Geometry3d.Vector(vec_g.X, vec_g.Y, vec_g.Z);
            if (isCopy)
            {
                GTLink.Types.TeklaModelObjectGoo modelObject2 = new TeklaModelObjectGoo();
                modelObject2.Value = Tekla.Structures.Model.Operations.Operation.CopyObject(modelObject.Value, vec_t);
                TeklaModelObjectList.Add(modelObject2.Value);//建立GH和Tekla之间的关联
                DA.SetData(0, modelObject2);
            }
            else
            {
                Tekla.Structures.Model.Operations.Operation.MoveObject(modelObject.Value, vec_t);
                TeklaModelObjectList.Add(modelObject.Value);//建立GH和Tekla之间的关联
                DA.SetData(0, modelObject);
            }

            //modelObject.
            myModel.CommitChanges();

            //Tekla.Structures.Model.UI.ModelViewEnumerator views = Tekla.Structures.Model.UI.ViewHandler.GetVisibleViews();
            //while (views.MoveNext())
            //{
            //    Tekla.Structures.Model.UI.ViewHandler.RedrawView(views.Current);//刷新视图
            //}
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
            get { return new Guid("57CF41E4-BB27-40FB-B695-11BB9C67BED4"); }
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
