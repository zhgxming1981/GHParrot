using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Collections;
//using ExcelFunction;
using System.IO;
using System.Text;
using CommonFunction.Hardware;

namespace NS_Parrot
{
    public class FormatText4Excel : GH_Component, IGH_VariableParameterComponent
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public FormatText4Excel()
          : base("文本2excel预处理", "文本2excel预处理",
              "文本2excel预处理，将一列数据变换为一行数据，中间用|分格",
              "Parrot", "ExcelCAD")
        {
        }

        //private int iCountOfParameter = 1;

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Data", "D1", "数据", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("文本列表", "L1", "文本列表", GH_ParamAccess.item);
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

            StringBuilder strb = new StringBuilder();
            for (int i = 0; i < params_count; i++)
            {
                string str = "";
                if (!DA.GetData(i, ref str)) { return; }
                strb.Append(str);
                strb.Append("|");
            }
            if (strb[strb.Length - 1] == '|')
            {
                strb.Remove(strb.Length - 1, 1);
            }


            DA.SetData(0, strb);
        }

        public bool CanInsertParameter(GH_ParameterSide side, int index)
        {
            if (side == GH_ParameterSide.Input)
            {
                if (index > 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public bool CanRemoveParameter(GH_ParameterSide side, int index)
        {
            if (side == GH_ParameterSide.Input && index > 0)
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
            var p = new Grasshopper.Kernel.Parameters.Param_String();
            p.NickName = String.Format("D{0}", index + 1);
            p.Access = GH_ParamAccess.item;
            p.Optional = true;
            return p;
        }

        public bool DestroyParameter(GH_ParameterSide side, int index)
        {
            //throw new NotImplementedException();
            //iCountOfParameter--;
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
            get { return new Guid("D4C9A966-180E-48E5-8A43-7382E00FF09B"); }
        }
    }
}