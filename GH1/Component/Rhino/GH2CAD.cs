using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace NS_Parrot
{
    public class GH2CAD : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GH2CAD class.
        /// </summary>
        public GH2CAD()
          : base("GH2CAD", "GH2CAD",
              "将GH中的直线、圆弧、多段线导入到CAD中",
              "Parrot", "ExcelCAD")
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("pt", "pt", "CAD中的基点", GH_ParamAccess.item);

            Point3d origin = new Point3d(0, 0, 0);
            Vector3d X_axis = new Vector3d(1, 0, 0);
            Vector3d Y_axis = new Vector3d(0, 1, 0);
            Plane plane = new Plane(origin, X_axis, Y_axis);
            pManager.AddPlaneParameter("PL", "PL", "Rhion中的局部坐标平面", GH_ParamAccess.item, plane);
            pManager.AddTextParameter("Layer", "La", "Bake的目标图层", GH_ParamAccess.item, "AutoCAD");

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geo", "Geo", "Geo", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
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
            get { return new Guid("73DBDAEE-DECD-4C2B-BA08-C593E02393A6"); }
        }
    }
}