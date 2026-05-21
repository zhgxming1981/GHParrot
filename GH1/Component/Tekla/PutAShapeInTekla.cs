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
using CommonFunction;

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
          : base("������״", "������״",
            "�����������������",
            "Parrot", "Tekla")
        {
        }

        private List<TSM.Brep> TeklaModelObjectList = new List<TSM.Brep>();

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("ShapeName", "SN", "��״��", GH_ParamAccess.item);
            pManager.AddPointParameter("StartPoint", "SP", "���", GH_ParamAccess.item);
            pManager.AddPointParameter("EndPoint", "EP", "�յ�", GH_ParamAccess.item);
            pManager.AddGenericParameter("Position", "P", "��λ", GH_ParamAccess.item);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Shape", "S", "��״", GH_ParamAccess.item);
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
            if (!DA.GetData(0, ref shapeName)) { return; }//��һ���������

            Point3d startPoint = new Point3d();
            if (!DA.GetData(1, ref startPoint)) { return; }//�ڶ����������


            Point3d endPoint = new Point3d();
            if (!DA.GetData(2, ref endPoint)) { return; }//�������������

            TSM.Position position;
            PositionGoo positionGoo = null;
            if (!DA.GetData(3, ref positionGoo))//���ĸ��������
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
            DA.SetData(0, brep);//��һ���������
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
                return GeneratedIcon.Get("gen_PutAShapeInTekla");
                //return null;

            }
        }
        //protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_PutAShapeInTekla");

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("C6474703-7609-4B6E-BC25-90392B7128A5");


        ///<summary>
        ///����ر������ĵ�ʱ�����¼�����ִ��
        /// </summary>
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            SolutionExpired += (sender, args) =>//����Ҫ���¼���ʱ�����¼�����ִ��
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
                return "�ҵĵ�һ��GH�����";
            }
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            ToolStripMenuItem item1 = new ToolStripMenuItem();
            item1.Text = "Bake to Tekla";
            menu.Items.Add(item1);
            item1.Click += new EventHandler((o, e) =>
            {
                TeklaModelObjectList.Clear();//�ж�GH��Tekla֮��Ĺ���
            });

            ToolStripMenuItem item2 = new ToolStripMenuItem();
            item2.Text = "ѡ��Tekla����";
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
            item3.Text = "ɾ��Tekla����";
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