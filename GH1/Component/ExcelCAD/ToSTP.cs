using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using System;
using System.Collections.Generic;
using System.IO;

namespace NS_Parrot
{
    public class ToSTP : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ToSTP class.
        /// </summary>
        public ToSTP()
          : base("存为Stp", "存为Stp",
              "存为Stp，并用Catia打开",
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
            //pManager.AddTextParameter("文件名", "文件名", "文件名", GH_ParamAccess.list);
            pManager.AddBooleanParameter("STP", "STP", "导出为stp格式", GH_ParamAccess.item);
            //pManager.AddBooleanParameter("CAT", "CAT", "在CATIA中打开", GH_ParamAccess.item, false);
            //pManager.AddBooleanParameter("Run", "Run", "Run", GH_ParamAccess.item);
            //pManager[3].Optional = true;
            //pManager[4].Optional = true;
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
            if (!DA.GetDataList(1,  fileName)) { return; }

            bool flag_stp = true;
            if (!DA.GetData(2, ref flag_stp)) { return; }

            if (!flag_stp) { return; }


            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            Rhino.RhinoApp.SetFocusToMainWindow();//获得焦点
            for (int i = 0; i < guid_list.Count; i++)
            {
                RhinoObject rh_obj = doc.Objects.FindId(guid_list[i].Value);
                if (rh_obj == null) continue; // 跳过无效对象

                // 取消所有选择并选择当前对象
                doc.Objects.UnselectAll();
                rh_obj.Select(true, true);

                // 确保文件名有 .stp 扩展
                string exportPath = fileName[i];
                if (Path.GetExtension(exportPath).ToLower() != ".stp")
                    exportPath = Path.ChangeExtension(exportPath, ".stp");

                // 检查路径是否存在，如果不存在就创建
                string dir = Path.GetDirectoryName(exportPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                //提前发送空格，用于回答Rhino命令行提问
                Rhino.RhinoApp.SendKeystrokes(" ", true);


                 // 导出选中对象
                bool success = doc.ExportSelected(exportPath);
                if (!success)
                {
                    RhinoApp.WriteLine($"导出失败: {exportPath}");
                    //this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"导出失败: {exportPath}");
                }
            }
            //foreach (GH_Guid guid in guid_list)
            //{
            //    doc.Objects.UnselectAll();
            //    RhinoObject rh_obj = doc.Objects.FindId(guid.Value);
            //    rh_obj.Select(true, true);
            //    string str_ext = fileName[i].Substring(fileName[i].Length - 4, 4);
            //    if (str_ext != ".stp")//如果后缀不是.stp就增加后缀
            //    {
            //        fileName[i] += ".stp";
            //    }

            //    Rhino.RhinoApp.SendKeystrokes(" ", true);//提前发送空格
            //    doc.ExportSelected(fileName[i]);
            //    i++;
            //}
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
            get { return new Guid("53E7F888-77FF-47ED-A2A4-8EEF454BB559"); }
        }
    }
}