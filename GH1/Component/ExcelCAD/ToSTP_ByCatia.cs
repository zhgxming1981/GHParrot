using CatiaFunction;
using Grasshopper.Kernel;
using INFITF;
using System;
using System.IO;

namespace NS_Parrot
{
    public class ToSTP_ByCatia : GH_Component
    {
        private string _lastInputSignature;
        private string _lastStatus;

        /// <summary>
        /// Initializes a new instance of the ToSTP_ByCatia class.
        /// </summary>
        public ToSTP_ByCatia()
          : base("ToSTP_ByCatia", "ToCatiaSTP",
              "通过Catia转STP",
              "Parrot", "ExcelCAD")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("源文件夹", "源文件夹", "源文件夹", GH_ParamAccess.item);
            pManager.AddTextParameter("目标文件夹", "目标文件夹", "目标文件夹", GH_ParamAccess.item);
            pManager.AddBooleanParameter("转换", "转换", "通过CATIA转换", GH_ParamAccess.item);
            pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status", "导出状态", GH_ParamAccess.item);
        }




        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
       


        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string source = "";
            string target = "";
            bool run = false;
            if (!DA.GetData(0, ref source))
                return;
            DA.GetData(1, ref target);
            string inputSignature = BuildInputSignature(source, target);
            if (_lastInputSignature != inputSignature)
            {
                _lastInputSignature = inputSignature;
                _lastStatus = null;
            }

            DA.GetData(2, ref run);
            if (!run)
            {
                DA.SetData(0, _lastStatus ?? "未执行：转换开关未开启");
                return;
            }
            if (!System.IO.Directory.Exists(source))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "源文件夹不存在");
                return;
            }
            //============================ // 🔵 连接 CATIA（只做一次） //============================ 
            if (Common4Catia.CATIA == null)
            {
                Common4Catia.CATIA = Common4Catia.ConnectCatia();
                if (Common4Catia.CATIA == null)
                {
                    const string message = "未执行：未检测到正在运行的 CATIA。请先启动 CATIA，再开启转换。";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message);
                    _lastStatus = message;
                    DA.SetData(0, _lastStatus);
                    return;
                }
            }
            var files = System.IO.Directory.GetFiles(source, "*.stp", System.IO.SearchOption.AllDirectories);
            int success = 0; 
            int fail = 0;

            foreach (var file in files)
            {
                var log = new System.Text.StringBuilder();
                log.AppendLine("STEP转换日志");
                log.AppendLine("时间: " + DateTime.Now);
                log.AppendLine("----------------------------------");
                string dstDir="";
                try
                {
                    //============================ // 1️⃣ 当前文件夹 //============================ 
                    string srcDir = System.IO.Path.GetDirectoryName(file);
                    string parentDir = System.IO.Directory.GetParent(srcDir).FullName;
                    string folderName = System.IO.Path.GetFileName(srcDir);
                    if (folderName.Contains("_catia"))
                        continue;
                    //============================ // 2️⃣ 目标目录 //============================ 
                
                    if (string.IsNullOrEmpty(target))
                    {
                        // 👉 每个目录生成 _catia
                        dstDir = System.IO.Path.Combine(parentDir, folderName + "_catia");
                    }
                    else
                    {
                        // 👉 指定目标目录（集中输出） 
                        string relative = srcDir.Substring(source.Length).TrimStart('\\');
                        dstDir = System.IO.Path.Combine(target, relative + "_catia");
                    }
                    if (!System.IO.Directory.Exists(dstDir)) 
                        System.IO.Directory.CreateDirectory(dstDir);
                    //============================ // 3️⃣ 目标文件 //============================ 
                    string dstFile = System.IO.Path.Combine(dstDir, System.IO.Path.GetFileName(file));
                    //============================ // 4️⃣ 转换 //============================ 
                    string result = Common4Catia.ConvertOneSTEP_WithLog(file, srcDir, dstDir);
                    if (result == "OK")
                    {
                        success++;
                    }
                    else
                    {
                        fail++;
                        log.AppendLine(file + " 失败原因: " + result);
                    }

                }
                catch (Exception ex)
                {
                    fail++;
                    log.AppendLine(file + " 异常: " + ex.Message);
                }

                string logPath = Path.Combine(dstDir, "转换日志.txt");
                System.IO.File.WriteAllText(logPath, log.ToString());
            }


            //============================
            // 🔥 日志放在 source 根目录
            //============================
            //string logPath = Path.Combine(source, "转换日志.txt");
           

            _lastStatus = $"完成：成功 {success}，失败 {fail}\n";
            DA.SetData(0, _lastStatus);
        }

        private string BuildInputSignature(string source, string target)
        {
            return string.Join("|",
                "SourceSources=" + Params.Input[0].Sources.Count,
                "TargetSources=" + Params.Input[1].Sources.Count,
                source ?? "",
                target ?? "");
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
                return GeneratedIcon.Get("gen_ToSTP_ByCatia");
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("633DFB25-57E5-4DF3-AE56-25F915078D1D"); }
        }
    }
}
