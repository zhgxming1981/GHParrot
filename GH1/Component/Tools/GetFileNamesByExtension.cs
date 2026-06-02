using CommonFunction;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace NS_Parrot
{
    public class GetFileNamesByExtension : GH_Component
    {
        public GetFileNamesByExtension()
          : base("GetFileNamesByExtension", "FileNames",
              "获取指定文件夹中指定后缀的文件名",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件夹", "Folder", "要搜索的文件夹路径", GH_ParamAccess.item);
            pManager.AddTextParameter("后缀名", "Ext", "要筛选的文件后缀，可为空，例如 .xlsx 或 xlsx", GH_ParamAccess.item, string.Empty);
            pManager.AddBooleanParameter("搜索子文件夹", "Recursive", "是否搜索子文件夹，默认不搜索", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("文件名", "Name", "匹配到的文件名，不包含完整路径", GH_ParamAccess.list);
            pManager.AddTextParameter("无后缀名", "BaseName", "匹配到的文件名，不包含后缀名", GH_ParamAccess.list);
            pManager.AddTextParameter("完整路径", "Path", "匹配到的文件完整路径", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string folder = string.Empty;
            string extension = string.Empty;
            bool recursive = false;

            if (!DA.GetData(0, ref folder))
                return;

            DA.GetData(1, ref extension);
            DA.GetData(2, ref recursive);

            if (string.IsNullOrWhiteSpace(folder))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "文件夹路径为空。");
                return;
            }

            if (!Directory.Exists(folder))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "找不到指定文件夹：" + folder);
                return;
            }

            try
            {
                string normalizedExtension = NormalizeExtension(extension);
                SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                IEnumerable<string> files = Directory.EnumerateFiles(folder, "*", option);
                if (!string.IsNullOrEmpty(normalizedExtension))
                {
                    files = files.Where(file =>
                        string.Equals(Path.GetExtension(file), normalizedExtension, StringComparison.OrdinalIgnoreCase));
                }

                List<string> fullPaths = files
                    .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                List<string> fileNames = fullPaths
                    .Select(Path.GetFileName)
                    .ToList();
                List<string> baseNames = fileNames
                    .Select(Path.GetFileNameWithoutExtension)
                    .ToList();

                DA.SetDataList(0, fileNames);
                DA.SetDataList(1, baseNames);
                DA.SetDataList(2, fullPaths);
            }
            catch (UnauthorizedAccessException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "没有权限读取某些文件夹：" + ex.Message);
            }
            catch (IOException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "读取文件夹失败：" + ex.Message);
            }
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return string.Empty;

            string result = extension.Trim();
            return result.StartsWith(".", StringComparison.Ordinal) ? result : "." + result;
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ai_GetFileNamesByExtension"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("CB0EB355-F634-4A22-84C9-E087F8CF15E3"); }
        }
    }
}
