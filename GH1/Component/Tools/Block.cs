using System;
using System.Collections.Generic;
using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using Rhino;
using Rhino.Geometry;
using Grasshopper.Kernel.Special;

namespace NS_Parrot
{
    public class Block : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Block class.
        /// </summary>
        public Block()
          : base("Block", "Block",
              "解析block",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "块的Guid", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("块名", "块名", "块名", GH_ParamAccess.item);
            pManager.AddPointParameter("插入点", "插入点", "块的插入点", GH_ParamAccess.item);
            pManager.AddGeometryParameter("炸开块", "炸开块", "彻底炸开块", GH_ParamAccess.list);
            pManager.AddGenericParameter("属性", "属性", "属性", GH_ParamAccess.list);
            pManager.AddTransformParameter("变换", "变换", "变换", GH_ParamAccess.list);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            if (!CHardware.CheckLegality())
                return;

            GH_Guid guid = null;
            if (!DA.GetData(0, ref guid)) { return; }

            Rhino.DocObjects.RhinoObject obj = RhinoDoc.ActiveDoc.Objects.Find(guid.Value);
            if (obj is Rhino.DocObjects.InstanceObject)
            {
                InstanceObject blockObject = (InstanceObject)obj;
                DA.SetData(0, blockObject.InstanceDefinition.Name);
                DA.SetData(1, blockObject.InsertionPoint);
                RhinoObject[] rhino_obj;
                ObjectAttributes[] obj_att;
                Transform[] transform;

                blockObject.Explode(true, out rhino_obj, out obj_att, out transform);
                int count = rhino_obj.Length;
                List<GeometryBase> geo = new List<GeometryBase>(count);
                //List<Transform> trans = new List<Transform>(count);
                for (int i = 0; i < count; i++)
                {
                    geo.Add(rhino_obj[i].Geometry);
                    //trans.Add(transform[i]);
                }

                DA.SetDataList(2, geo);
                DA.SetDataList(3, obj_att);
                DA.SetDataList(4, transform);
              
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
            get { return new Guid("D25A50F9-072F-448A-86BA-D780201DC53E"); }
        }
    }
}