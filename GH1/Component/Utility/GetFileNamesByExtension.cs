using CommonFunction;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NS_Parrot
{
    public class GetFileNamesByExtension : GH_Component
    {
        public GetFileNamesByExtension()
          : base("按扩展名取文件", "取文件名",
              "获取指定文件夹中指定后缀的文件名",
              "Parrot", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件夹", "文件夹", "要搜索的文件夹路径", GH_ParamAccess.item);
            pManager.AddTextParameter("后缀名", "后缀名", "要筛选的文件后缀，可为空，例如 .xlsx 或 xlsx", GH_ParamAccess.item, string.Empty);
            pManager.AddBooleanParameter("搜索子文件夹", "搜索子文件夹", "是否搜索子文件夹，默认为否", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("包含快捷方式", "包含快捷方式", "是否包含 .lnk 快捷方式，并输出其实际指向路径，默认为否", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("文件名", "文件名", "匹配到的文件名，不包含完整路径", GH_ParamAccess.list);
            pManager.AddTextParameter("无后缀名", "无后缀名", "匹配到的文件名，不包含后缀名", GH_ParamAccess.list);
            pManager.AddTextParameter("完整路径", "完整路径", "匹配到的文件完整路径；快捷方式输出实际指向路径", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string folder = string.Empty;
            string extension = string.Empty;
            bool recursive = false;
            bool includeShortcuts = false;

            if (!DA.GetData(0, ref folder))
                return;

            DA.GetData(1, ref extension);
            DA.GetData(2, ref recursive);
            DA.GetData(3, ref includeShortcuts);

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
                IEnumerable<string> paths = Directory.EnumerateFiles(folder, "*", option)
                    .Select(file => ResolvePath(file, includeShortcuts))
                    .Where(path => !string.IsNullOrWhiteSpace(path));

                if (!string.IsNullOrEmpty(normalizedExtension))
                {
                    paths = paths.Where(path =>
                        string.Equals(Path.GetExtension(path), normalizedExtension, StringComparison.OrdinalIgnoreCase));
                }

                List<string> fullPaths = paths
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
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

        private string ResolvePath(string file, bool includeShortcuts)
        {
            if (!includeShortcuts || !string.Equals(Path.GetExtension(file), ".lnk", StringComparison.OrdinalIgnoreCase))
                return file;

            string targetPath = GetShortcutTargetPath(file);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "无法解析快捷方式，已跳过：" + file);
                return null;
            }

            return targetPath;
        }

        private static string GetShortcutTargetPath(string shortcutPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return null;

            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                return shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
            }
            finally
            {
                ReleaseComObject(shortcut);
                ReleaseComObject(shell);
            }
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
                Marshal.FinalReleaseComObject(comObject);
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
