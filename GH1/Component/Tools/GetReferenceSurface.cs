using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using rd = Rhino.NodeInCode;
using System.Collections.Specialized;
using System.IO;
using Rhino.Geometry.Collections;
using System.Linq;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class GetReferenceSurface : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GetReferenceSurface class.
        /// </summary>
        public GetReferenceSurface()
          : base("获取参考面", "获取参考面",
              "获取参考面",
              "Parrot", "工具")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("铝板", "铝板", "带折边的铝板", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("参考面", "参考面", "铝板参考面", GH_ParamAccess.item);
            //pManager.AddNumberParameter("参考面", "参考面", "铝板参考面", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Brep brep = new Brep();
            if (!DA.GetData(0, ref brep)) { return; }

            Grasshopper.Kernel.Types.GH_Guid guid = null;
            if (!DA.GetData(0, ref guid)) { return; }


            System.Guid guid2 = new Guid(guid.ToString());
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            var obj = doc.Objects.FindId(guid2);


            NameValueCollection allUserStrings = obj.Attributes.GetUserStrings();
            //NameValueCollection allUserStrings1 = brep.GetUserStrings();//此语句获取不到userString
            List<double> area_list = new List<double>();
            int cnt = allUserStrings.Count;
            for (int i = 0; i < cnt; i++)
            {
                if (allUserStrings.AllKeys[i].Contains("RefArea"))
                {
                    double area = Convert.ToDouble(allUserStrings[i]);
                    area_list.Add(area);
                }
            }

            BrepFaceList bf_list = brep.Faces;
            List<Brep> brep_list = new List<Brep>();
            List<double> retVal_area = new List<double>();
            foreach (double num in area_list)
            {
                foreach (BrepFace bf in bf_list)
                {
                    retVal_area.Add(bf.ToBrep().GetArea());
                    Brep temp = bf.ToBrep();
                    if (Math.Abs(temp.GetArea() - num) < 0.01)
                    {
                        brep_list.Add(temp);
                    }
                }

            }
            Brep retVal_brep = Rhino.Geometry.Brep.MergeBreps(brep_list, 0.001);
            DA.SetData(0, retVal_brep);
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
            get { return new Guid("9455D222-5B79-43E2-9DA9-66550AB2E930"); }
        }
    }
}