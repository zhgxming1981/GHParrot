using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using CommonFunction.Hardware;
using Rhino.DocObjects;
using Rhino;

namespace NS_Parrot
{
    public class DeconstructRhinoObjectAttributes : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DeconstructRhinoObjectAttributes class.
        /// </summary>
        public DeconstructRhinoObjectAttributes()
          : base("解析Rhino对象属性", "解析属性",
              "解析Rhino对象属性",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("属性", "属性", "属性", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("图层", "图层", "图层", GH_ParamAccess.item);
            pManager.AddTextParameter("颜色名", "颜色名", "颜色名", GH_ParamAccess.item);
            pManager.AddTextParameter("颜色值", "颜色值", "颜色值", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;
            //Rhino.
            Rhino.DocObjects.ObjectAttributes att = new Rhino.DocObjects.ObjectAttributes();
            if (!DA.GetData(0, ref att)) { return; }
            Rhino.DocObjects.Layer layer;
            int layerIndex = att.LayerIndex;
            layer = Rhino.RhinoDoc.ActiveDoc.Layers.FindIndex(layerIndex);
            var color = att.ObjectColor;
            string colorName = "";
            string colorValue = "";
            if (color.A == 255 && color.R == 0 && color.G == 0 && color.B == 0)
            {
                colorName = "ByLayer";
                colorValue = "{" + layer.Color.A.ToString() + "," + layer.Color.R.ToString() + "," + layer.Color.G.ToString() + "," + layer.Color.B.ToString() + "}";
            }
            else
            {
                colorName = color.Name;
                colorValue = "{" + color.A.ToString() + "," + color.R.ToString() + "," + color.G.ToString() + "," + color.B.ToString() + "}";
            }
            DA.SetData(0, layer.Name);
            DA.SetData(1, colorName);
            DA.SetData(2, colorValue);
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
            get { return new Guid("3013D0D2-6628-4E3A-9B11-8D7C137B441E"); }
        }
    }
}