using CatiaFunction;
using Grasshopper.Kernel;
using INFITF;
using System;
using System.IO;

namespace parrot.Component
{
    public class ToSTP_ByCatia : GH_Component
    {
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
        //protected override void SolveInstance(IGH_DataAccess DA)
        //{
        //    string source = "";
        //    string target = "";
        //    bool run = false;

        //    if (!DA.GetData(0, ref source)) return;
        //    DA.GetData(1, ref target);
        //    DA.GetData(2, ref run);

        //    if (!run)
        //    {
        //        DA.SetData(0, "未执行");
        //        return;
        //    }

        //    if (!System.IO.Directory.Exists(source))
        //    {
        //        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "源文件夹不存在");
        //        return;
        //    }

        //    //============================
        //    // 🔵 连接 CATIA（只做一次）
        //    //============================
        //    if (Common4Catia.CATIA == null)
        //    {
        //        Common4Catia.CATIA = Common4Catia.ConnectCatia();

        //        if (Common4Catia.CATIA == null)
        //        {
        //            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法连接 CATIA");
        //            return;
        //        }
        //    }

        //    try
        //    {
        //        //============================
        //        // 🔵 目标根目录
        //        //============================
        //        string finalTarget;

        //        if (string.IsNullOrEmpty(target))
        //        {
        //            // 👉 A → A_catia（同级目录）
        //            string parent = System.IO.Directory.GetParent(source).FullName;
        //            //parent= System.IO.Directory.GetParent(parent).FullName;
        //            string folderName = System.IO.Path.GetFileName(source);

        //            finalTarget = System.IO.Path.Combine(parent, folderName + "STP_catia");
        //        }
        //        else
        //        {
        //            finalTarget = target;
        //        }

        //        if (!System.IO.Directory.Exists(finalTarget))
        //            System.IO.Directory.CreateDirectory(finalTarget);

        //        //============================
        //        // 🔥 调用批处理（核心）
        //        //============================
        //        Common4Catia.BatchConvertSTEP(source, finalTarget);

        //        //============================
        //        // 🔥 提示日志位置
        //        //============================
        //        string logPath = System.IO.Path.Combine(finalTarget, "转换日志.txt");

        //        if (System.IO.File.Exists(logPath))
        //        {
        //            DA.SetData(0, $"完成（含日志）\n日志路径：{logPath}");
        //        }
        //        else
        //        {
        //            DA.SetData(0, "完成（未生成日志）");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        //    }
        //}



        //protected override void SolveInstance(IGH_DataAccess DA)
        //{
        //    string source = "";
        //    bool run = false;

        //    if (!DA.GetData(0, ref source)) return;
        //    DA.GetData(2, ref run);

        //    if (!run)
        //    {
        //        DA.SetData(0, "未执行");
        //        return;
        //    }

        //    if (!Directory.Exists(source))
        //    {
        //        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "源文件夹不存在");
        //        return;
        //    }

        //    //============================
        //    // 🔵 连接 CATIA
        //    //============================
        //    if (Common4Catia.CATIA == null)
        //    {
        //        Common4Catia.CATIA = Common4Catia.ConnectCatia();
        //        if (Common4Catia.CATIA == null)
        //        {
        //            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法连接 CATIA");
        //            return;
        //        }
        //    }

        //    var files = Directory.GetFiles(source, "*.stp", SearchOption.AllDirectories);

        //    int success = 0;
        //    int fail = 0;

        //    var log = new System.Text.StringBuilder();
        //    log.AppendLine("STEP转换日志");
        //    log.AppendLine("时间: " + DateTime.Now);
        //    log.AppendLine("----------------------------------");

        //    foreach (var file in files)
        //    {
        //        try
        //        {
        //            //============================
        //            // 🔥 当前文件夹
        //            //============================
        //            string srcDir = Path.GetDirectoryName(file);

        //            //============================
        //            // 🔥 正确目标目录（关键）
        //            //============================
        //            string parentDir = Directory.GetParent(srcDir).FullName;
        //            string folderName = Path.GetFileName(srcDir);

        //            string dstDir = Path.Combine(parentDir, folderName + "_catia");

        //            if (!Directory.Exists(dstDir))
        //                Directory.CreateDirectory(dstDir);

        //            //============================
        //            // 🔥 目标文件
        //            //============================
        //            string dstFile = Path.Combine(dstDir, Path.GetFileName(file));

        //            //============================
        //            // 🔥 转换
        //            //============================
        //            string result = Common4Catia.ConvertOneSTEP_WithLog(file, srcDir, dstDir);

        //            if (result == "OK")
        //            {
        //                success++;
        //            }
        //            else
        //            {
        //                fail++;
        //                log.AppendLine(file + " 失败原因: " + result);
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            fail++;
        //            log.AppendLine(file + " 异常: " + ex.Message);
        //        }
        //    }

        //    //============================
        //    // 🔥 日志放在 source 根目录
        //    //============================
        //    //string logPath = Path.Combine(source, "转换日志.txt");
        //    string logPath = Path.Combine(Directory.GetParent(source).FullName, Path.GetFileName(source) + "转换日志.txt");
        //    System.IO.File.WriteAllText(logPath, log.ToString());

        //    DA.SetData(0, $"完成：成功 {success}，失败 {fail}\n日志：{logPath}");
        //}


        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string source = "";
            string target = "";
            bool run = false;
            if (!DA.GetData(0, ref source))
                return;
            DA.GetData(1, ref target);
            DA.GetData(2, ref run);
            if (!run)
            {
                DA.SetData(0, "未执行");
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
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法连接 CATIA");
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
           

            DA.SetData(0, $"完成：成功 {success}，失败 {fail}\n");
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
            get { return new Guid("633DFB25-57E5-4DF3-AE56-25F915078D1D"); }
        }
    }
}