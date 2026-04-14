using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using System;
using System.Collections.Generic;

namespace NS_Parrot
{
    public class ToSolidWorks : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ToSolidWorks class.
        /// </summary>
        public ToSolidWorks()
          : base("ToSolidWorks", "ToSolidWorks",
              "存为x_t，并用SolidWorks打开",
              "Parrot", "ExcelCAD")
        {
         
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "几何体", GH_ParamAccess.list);
            pManager.AddTextParameter("路径", "路径", "输出路径", GH_ParamAccess.list);
            pManager.AddBooleanParameter("SW", "SW", "在solidworks中打开", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("x_t", "x_t", "导出为x_t格式", GH_ParamAccess.item);
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<GH_Guid> guid_list = new List<GH_Guid>();
            if (!DA.GetDataList(0, guid_list)) { return; }


            List<string> fileName = new List<string>();
            //string fileName = "";
            if (!DA.GetDataList(1, fileName)) { return; }

            bool flag_SW = true;
            if (!DA.GetData(2, ref flag_SW)) { return; }

            bool flag_stp = true;
            if (!DA.GetData(3, ref flag_stp)) { return; }

            if (!flag_stp) { return; }

       

            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            int count = guid_list.Count;
            //if (flag_one == false)
            //int i = 0;
            Rhino.RhinoApp.SetFocusToMainWindow();//获得焦点
            int count_guidList = guid_list.Count;
            for(int i=0; i<count_guidList;i++)
            {
                doc.Objects.UnselectAll();
                RhinoObject rh_obj = doc.Objects.FindId(guid_list[i].Value);
                rh_obj.Select(true, true);
                if (fileName[i].Length > 4)
                {
                    string str_ext = fileName[i].Substring(fileName[i].Length - 4, 4);
                    if (str_ext != ".x_t")//如果后缀不是.stp就增加后缀
                    {
                        fileName[i] += ".x_t";
                    }
                }
                else
                {
                    fileName[i] += ".x_t";
                }

                Rhino.RhinoApp.SendKeystrokes(" ", true);//提前发送空格
                doc.ExportSelected(fileName[i]);
            }

            //foreach (GH_Guid guid in guid_list)
            //{
            //    doc.Objects.UnselectAll();
            //    RhinoObject rh_obj = doc.Objects.FindId(guid.Value);
            //    rh_obj.Select(true, true);
            //    if (fileName[i].Length > 4)
            //    {
            //        string str_ext = fileName[i].Substring(fileName[i].Length - 4, 4);
            //        if (str_ext != ".x_t")//如果后缀不是.stp就增加后缀
            //        {
            //            fileName[i] += ".x_t";
            //        }
            //    }
            //    else
            //    {
            //        fileName[i] += ".x_t";
            //    }

            //    Rhino.RhinoApp.SendKeystrokes(" ", true);//提前发送空格
            //    doc.ExportSelected(fileName[i]);
            //    i++;
            //}

            Thread.Sleep(2000);

            if (flag_SW)
            {
                bool SWok =SolidworksFunction.Common4SW.ConnectSolidworks();
                if (SWok && fileName.Count == 1) 
                {
                    SolidworksFunction.Common4SW.OpenTheFile(fileName[0]);
                }
            }
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
            get { return new Guid("A88AB3D3-1D68-4AF3-A45D-D073E49DC49E"); }
        }
    }
}