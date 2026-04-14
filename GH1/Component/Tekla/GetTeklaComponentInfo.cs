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
    public class GetTeklaComponentInfo : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GetTeklaComponentInfo class.
        /// </summary>
        public GetTeklaComponentInfo()
          : base("GetTeklaComponentInfo", "GetTeklaComponentInfo",
              "获取组件信息",
              "Parrot", "Tekla")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("tekla组件", "tekla组件", "tekla组件", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "Name", "节点名称", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Plane", "Plane", "局部坐标", GH_ParamAccess.item);
            pManager.AddGenericParameter("子对象", "子对象", "子对象", GH_ParamAccess.list);
            pManager.AddGenericParameter("所有元素", "所有元素", "Returns an enumerator of all the connected hierarchic objects", GH_ParamAccess.list);
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

            if (modelObj.Value.GetType().Name == "Component")
            {
                Tekla.Structures.Model.Component component = (Tekla.Structures.Model.Component)modelObj.Value;
                string name = component.Name;

                Tekla.Structures.Geometry3d.CoordinateSystem coordinate = modelObj.Value.GetCoordinateSystem();
                Point3d origin = new Point3d(coordinate.Origin.X, coordinate.Origin.Y, coordinate.Origin.Z);
                Vector3d vx = new Vector3d(coordinate.AxisX.X, coordinate.AxisX.Y, coordinate.AxisX.Z);
                Vector3d vy = new Vector3d(coordinate.AxisY.X, coordinate.AxisY.Y, coordinate.AxisY.Z);
                Plane pl = new Plane(origin, vx, vy);

                List<GTLink.Types.TeklaModelObjectGoo> childrenList = new List<TeklaModelObjectGoo>();
                GTLink.Types.TeklaModelObjectGoo goo1 = new TeklaModelObjectGoo();
                TSM.ModelObjectEnumerator mo_child = component.GetChildren();
                while (mo_child.MoveNext())
                {
                    goo1.Value = mo_child.Current;
                    childrenList.Add(goo1);
                }


                List<GTLink.Types.TeklaModelObjectGoo> allList = new List<TeklaModelObjectGoo>();
                GTLink.Types.TeklaModelObjectGoo goo3 = new TeklaModelObjectGoo();
                TSM.ModelObjectEnumerator mo_all = component.GetComponents();
                while (mo_all.MoveNext())
                {
                    goo3.Value = mo_all.Current;
                    allList.Add(goo3);
                }



                DA.SetData(0, name);
                DA.SetData(1, pl);
                DA.SetDataList(2, childrenList);
                DA.SetDataList(3, allList);
            }
            else
            {
                DA.SetData(0, null);
                DA.SetData(1, null);
                DA.SetDataList(2, null);
                DA.SetDataList(3, null);
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
            get { return new Guid("BE9D651E-9FB3-41A4-9072-ADC041BE9570"); }
        }
    }
}