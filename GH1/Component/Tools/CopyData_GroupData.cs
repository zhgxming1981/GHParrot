using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using rd = Rhino.NodeInCode;
using Grasshopper;
using Rhino.Geometry.Intersect;
using Rhino.Render;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;


namespace NS_Parrot
{
    public class CopyData_GroupData : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CopyData_GroupData class.
        /// </summary>
        public CopyData_GroupData()
          : base("数据复制分组", "数据复制分组",
              "数据复制分组",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("", "", "", GH_ParamAccess.item);
            pManager.AddPlaneParameter("", "", "", GH_ParamAccess.tree);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPlaneParameter("", "", "", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            int x = 0;
            if (!DA.GetData(0, ref x)) { return; }

            DataTree<GH_Plane> retVal = new DataTree<GH_Plane>();

            GH_Structure<GH_Plane> y = new GH_Structure<GH_Plane>();

            if (!DA.GetDataTree(1, out y)) { return; }
            int cnt_y = y.Branches.Count;

            for (int i = 0; i < x; i++)
            {
                GH_Path path_i = new GH_Path(i);
                for (int j = 0; j < cnt_y; j++)
                {
                    GH_Path path_j = path_i.AppendElement(j);
                    foreach (var item in y.Branches[j])
                    {
                        retVal.Add(item, path_j);
                    }
                }
            }
            DA.SetDataTree (0, retVal);
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
            get { return new Guid("0C65B990-FFEE-4AA1-BED6-99316D1E1926"); }
        }
    }
}