using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NS_Parrot
{
    public class SetUnion : GH_Component, IGH_VariableParameterComponent
    {
        /// <summary>
        /// Initializes a new instance of the SetUnion class.
        /// </summary>
        public SetUnion()
            : base("SetUnion", "并集", "两个选择集的并集", "Parrot", "数据")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("选择集", "S1", "选择集", GH_ParamAccess.list);
            pManager.AddGenericParameter("选择集", "S2", "选择集", GH_ParamAccess.list);
            pManager[0].Optional = true;//该参数可有可无
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
            for (int i = 0; i < params_count; i++)
            {
                List<GH_Guid> s2 = new List<GH_Guid>();
                DA.GetDataList(i, s2);
                s1 = s1.Union(s2, new RhionObjectCompare()).ToList();//求并集
                //s1 = s1.Concat(s2).ToList();
            }
            //s1.Distinct(new RhionObjectCompare()).ToList(); //此语句无法正常工作
            DA.SetDataList(0, s1);
        }

        public bool CanInsertParameter(GH_ParameterSide side, int index)
        {
            if (side == GH_ParameterSide.Input)
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
            if (side == GH_ParameterSide.Input)
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
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("39A2674E-519F-4AEE-A0C1-A1B071097943"); }
        }
    }


}