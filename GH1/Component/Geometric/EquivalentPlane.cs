using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class EquivalentPlane : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the EquivalentPlane class.
        /// </summary>
        public EquivalentPlane()
          : base("等效平面", "等效平面",
              "判断2个平面是否是等效的，在一个平面内，只是原点不同",
              "Parrot", "几何")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("PL1", "PL1", "平面1", GH_ParamAccess.item);
            pManager.AddPlaneParameter("PL2", "PL2", "平面2", GH_ParamAccess.item);
            //pManager.AddBooleanParameter("反向", "反向", "可以反向", GH_ParamAccess.item);
            pManager.AddNumberParameter("容差", "容差", "容差", GH_ParamAccess.item, 0.001);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("结果", "结果", "是等效，否表示不等效", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Plane PL1 = new Plane();
            if (!DA.GetData(0, ref PL1)) { return; }

            Plane PL2 = new Plane();
            if (!DA.GetData(1, ref PL2)) { return; }

            //bool flip = true;
            //if (!DA.GetData(2, ref flip)) { return; }

            double tolerance = 0;
            if (!DA.GetData(2, ref tolerance)) { return; }

            string retVal;
            double distance = 0;
            if (CMath.IsEqPlane(PL1, PL2, tolerance, out distance) == 1)
            {
                retVal = "相同";
            }
            else if (CMath.IsEqPlane(PL1, PL2, tolerance, out distance) == -1)
            {
                retVal = "相反";
            }
            else if (CMath.IsEqPlane(PL1, PL2, tolerance, out distance) == 2)
            {
                retVal = "不同，距离" + distance.ToString();
            }
            else
            {
                retVal = "不同，方向不同";
            }

            DA.SetData(0, retVal);
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
            get { return new Guid("DDDECCF0-C87D-48F9-A1DE-A7B11DD8791E"); }
        }
    }
}