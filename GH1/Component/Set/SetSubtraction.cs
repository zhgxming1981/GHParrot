using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.Kernel.Types;
using CommonFunction.Hardware;
using CommonFunction.Transform;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class SetSubtraction : GH_Component, IGH_VariableParameterComponent
    {
        /// <summary>
        /// Initializes a new instance of the SetSubtraction class.
        /// </summary>
        public SetSubtraction()
          : base("SetSubtraction", "差集", "求差集", "Parrot", "数据")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("选择集", "S1", "选择集", GH_ParamAccess.list);
            pManager.AddGenericParameter("选择集", "S2", "要去除的选择集", GH_ParamAccess.list);
            pManager[1].Optional = true;//该参数可有可无
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("选择集", "S", "选择集", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            int params_count = this.Params.Input.Count;

            List<GH_Guid> s1 = new List<GH_Guid>();
            if (!DA.GetDataList(0, s1)) { return; }

            List<GH_Guid> s2 = new List<GH_Guid>();
            for (int i = 1; i < params_count; i++)
            {
                List<GH_Guid> s3 = new List<GH_Guid>();
                DA.GetDataList(i, s3);
                s2 = s2.Union(s3, new RhionObjectCompare()).ToList();
            }
            s1 = s1.Except(s2, new RhionObjectCompare()).ToList();//求差集 S1-S2
            DA.SetDataList(0, s1);
        }

        public bool CanInsertParameter(GH_ParameterSide side, int index)
        {
            if (side == GH_ParameterSide.Input && index > 1)
            {

                return true;
            }
            else
            {
                return false;
            }
        }

        public bool CanRemoveParameter(GH_ParameterSide side, int index)
        {
            if (side == GH_ParameterSide.Input && index > 1)
            {

                return true;
            }
            else
            {
                return false;
            }
        }

        public IGH_Param CreateParameter(GH_ParameterSide side, int index)
        {
            var p = new Grasshopper.Kernel.Parameters.Param_GenericObject();
            p.NickName = String.Format("S{0}", this.Params.Input.Count + 1);
            p.Description = "要去除的选择集";
            p.Access = GH_ParamAccess.list;
            p.Optional = true;
            return p;
        }

        public bool DestroyParameter(GH_ParameterSide side, int index)
        {
            return true;
        }

        public void VariableParameterMaintenance()
        {
            //throw new NotImplementedException();
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
            get { return new Guid("3FB14615-1B32-47F5-9776-B528F3159F00"); }
        }
    }
}