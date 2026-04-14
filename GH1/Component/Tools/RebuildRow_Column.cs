using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper;
using Grasshopper.Kernel.Types;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class RebuildRow_Column : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the 行列重组 class.
        /// </summary>
        public RebuildRow_Column()
          : base("行列重组", "行列重组",
              "行列重组",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("曲面", "曲面", "曲面", GH_ParamAccess.tree);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("曲面", "曲面", "曲面", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            DataTree<GH_Brep> retVal = new DataTree<GH_Brep>();

            GH_Structure<GH_Brep> br_input = new GH_Structure<GH_Brep>();

            if (!DA.GetDataTree(0, out br_input)) { return; }
            int m = br_input.Branches.Count;

            int n = br_input.get_Branch(0).Count;
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    GH_Path path_j = new GH_Path(j);
                    GH_Brep brep = br_input.Branches[i][j];
                    retVal.Add(brep, path_j);
                    //GH_Path path_j = new GH_Path(j);
                    //GH_Path path_i = new GH_Path(0,i);
                   
                    //retVal.Add(brep, path_j);
                }
            }
            DA.SetDataTree(0, retVal);
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
            get { return new Guid("C6528333-BDDF-4442-8026-B58F9466A390"); }
        }
    }
}