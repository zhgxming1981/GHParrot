using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using rd = Rhino.NodeInCode;
using System.Collections.Specialized;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;
namespace NS_Parrot
{
    public class GetUserString : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GetUserString class.
        /// </summary>
        public GetUserString()
          : base("GetUserString", "获取UserStr",
              "获取用定义的户字符串",
              "Parrot", "文本")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("物件", "geo", "Rhino物件", GH_ParamAccess.item);
            //pManager.AddGenericParameter("物件", "geo", "Rhino物件", GH_ParamAccess.item);
            //pManager.AddGenericParameter("Guid", "Guid", "Guid", GH_ParamAccess.item);
            pManager.AddTextParameter("属性名称", "key", "属性名称", GH_ParamAccess.item);
            pManager[1].Optional = true;//该参数可有可无
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("属性值", "uStr", "属性值", GH_ParamAccess.item);
            pManager.AddTextParameter("属性值", "uStrs", "属性值", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            Grasshopper.Kernel.Types.GH_Guid guid = null;
            if (!DA.GetData(0, ref guid)) { return; }

            string key = "";
            DA.GetData(1, ref key);

            System.Guid guid2 = new Guid(guid.ToString());
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            var obj = doc.Objects.FindId(guid2);
            string userStr = GetFunctionValue(obj.Attributes.GetUserString(key));

            NameValueCollection allUserStrings = obj.Attributes.GetUserStrings();
            List<string> userStrings = new List<string>();
            int cnt = allUserStrings.Count;
            for (int i = 0; i < cnt; i++)
            {
                userStrings.Add(allUserStrings.AllKeys[i] + ":" + GetFunctionValue(allUserStrings[i]));
            }

            DA.SetData(0, userStr);
            DA.SetDataList(1, userStrings);
        }

        private string GetFunctionValue(string text)
        {
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            string retVal = text;
            Guid guid = new Guid();
            string guidText = GetGUIDText(text);
            if (guidText != "")
            {
                guid = new Guid(guidText);
                var obj = doc.Objects.FindId(guid);
                if (text.Contains("%<CurveLength(\""))
                {
                    if (obj.GetType().ToString() == "Rhino.DocObjects.CurveObject")
                    {
                        Rhino.Geometry.Curve curveGeo = (Rhino.Geometry.Curve)obj.Geometry;
                        retVal = curveGeo.GetLength().ToString();
                    }
                }
                else if (text.Contains("%<Area(\""))
                {
                    if (obj.GetType().ToString() == "Rhino.DocObjects.CurveObject")
                    {
                        Rhino.Geometry.Curve curveGeo = (Rhino.Geometry.Curve)obj.Geometry;
                        Rhino.Geometry.Brep planeBrep = Brep.CreatePlanarBreps(curveGeo, 0.0001)[0];

                        retVal = planeBrep.GetArea().ToString();
                    }
                    else if(obj.GetType().ToString() == "Rhino.DocObjects.BrepObject")
                    {
                        Rhino.Geometry.Brep brepGeo = (Rhino.Geometry.Brep)obj.Geometry;
                        retVal = brepGeo.GetArea().ToString();
                    }
                }
            }
            return retVal;
        }

        //private Guid GetGUID(string text)
        //{
        //    int start = text.IndexOf("(\"", 0);
        //    int end = text.IndexOf("\")", 0);
        //    string guidText = text.Substring(start + 2, end - start - 2);
        //    Guid guid = new Guid(guidText);
        //    return guid;
        //}

        private string GetGUIDText(string text)
        {
            string guidText = "";
            if (text != null)
            {
                int start = text.IndexOf("(\"", 0);
                int end = text.IndexOf("\")", 0);
                if (text != null && start > 0 && end == start + 38)
                {
                    guidText = text.Substring(start + 2, 36);
                }
            }
            return guidText;
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
            get { return new Guid("C1B80266-FF0C-49C0-A7BE-21554324D3C1"); }
        }
    }
}