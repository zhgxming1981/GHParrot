using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using GTLink.Types;
using TSM = Tekla.Structures.Model;
using TSG = Tekla.Structures.Geometry3d;
using System.Collections;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class GetTeklaConnectionInfo : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GetTeklaComponentInfo class.
        /// </summary>
        public GetTeklaConnectionInfo()
          : base("GetTeklaConnectionInfo", "GetTeklaConnectionInfo",
              "获取节点名称、位置等信息",
              "Parrot", "Tekla")
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("tekla节点", "tekla节点", "tekla节点", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "Name", "节点名称", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Plane", "Plane", "局部坐标", GH_ParamAccess.item);
            //pManager.AddGenericParameter("子对象", "子对象", "子对象", GH_ParamAccess.list);
            pManager.AddGenericParameter("主零件", "主零件", "Returns the primary object of the connection", GH_ParamAccess.item);
            pManager.AddGenericParameter("次零件", "次零件", "Returns the secondary objects", GH_ParamAccess.list);
            //pManager.AddGenericParameter("所有元素", "所有元素", "Returns an enumerator of all the connected hierarchic objects", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            GTLink.Types.TeklaModelObjectGoo modelObj = null;

            if (!DA.GetData(0, ref modelObj)) { return; }

            if (modelObj.Value.GetType().Name == "Connection")
            {
                Tekla.Structures.Model.Connection connection = (Tekla.Structures.Model.Connection)modelObj.Value;
                string name = connection.Name;

                Tekla.Structures.Geometry3d.CoordinateSystem coordinate = modelObj.Value.GetCoordinateSystem();
                Point3d origin = new Point3d(coordinate.Origin.X, coordinate.Origin.Y, coordinate.Origin.Z);
                Vector3d vx = new Vector3d(coordinate.AxisX.X, coordinate.AxisX.Y, coordinate.AxisX.Z);
                Vector3d vy = new Vector3d(coordinate.AxisY.X, coordinate.AxisY.Y, coordinate.AxisY.Z);
                Plane pl = new Plane(origin, vx, vy);

                //List<GTLink.Types.TeklaModelObjectGoo> childrenList = new List<TeklaModelObjectGoo>();
                //GTLink.Types.TeklaModelObjectGoo goo1 = new TeklaModelObjectGoo();
                //TSM.ModelObjectEnumerator mo_child = connection.GetChildren();
                //while (mo_child.MoveNext())
                //{
                //    goo1.Value = mo_child.Current;
                //    childrenList.Add(goo1);
                //}

                GTLink.Types.TeklaModelObjectGoo mo_Primary = new TeklaModelObjectGoo();
                mo_Primary.Value = connection.GetPrimaryObject();

                List<GTLink.Types.TeklaModelObjectGoo> secendList = new List<TeklaModelObjectGoo>();
                ArrayList mo_secend = connection.GetSecondaryObjects();
                int count1 = mo_secend.Count;
                for (int i = 0; i < count1; i++)
                {
                    GTLink.Types.TeklaModelObjectGoo goo2 = new TeklaModelObjectGoo();
                    goo2.Value = (Tekla.Structures.Model.ModelObject)mo_secend[i];
                    secendList.Add(goo2);
                }


                //List<GTLink.Types.TeklaModelObjectGoo> allList = new List<TeklaModelObjectGoo>();
                //GTLink.Types.TeklaModelObjectGoo goo3 = new TeklaModelObjectGoo();
                //TSM.ModelObjectEnumerator mo_all = connection.GetHierarchicObjects();
                //while (mo_all.MoveNext())
                //{
                //    goo3.Value = mo_all.Current;
                //    allList.Add(goo3);
                //}



                DA.SetData(0, name);
                DA.SetData(1, pl);
                //DA.SetDataList(2, childrenList);
                DA.SetData(2, mo_Primary);
                DA.SetDataList(3, secendList);
                //DA.SetDataList(5, allList);
            }
            else
            {
                DA.SetData(0, null);
                DA.SetData(1, null);
                //DA.SetDataList(2, null);
                DA.SetData(2, null);
                DA.SetDataList(3, null);
                //DA.SetDataList(5, null);
            }







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
            get { return new Guid("818C559E-C6EF-4A10-912A-DFA4A0BDB82C"); }
        }
    }
}