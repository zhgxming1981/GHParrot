using CommonFunction;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace NS_Parrot
{
    public class CopyUniqueFilesByPattern : GH_Component
    {
        private readonly List<string> _cachedNames = new List<string>();
        private readonly List<int> _cachedCounts = new List<int>();
        private bool _lastRun;

        public CopyUniqueFilesByPattern()
          : base("去重复制文件", "去重复制文件",
              "递归搜索指定文件夹中匹配通配符的文件，按文件名去重统计，并复制一份到新文件夹。",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("源文件夹", "源文件夹", "要搜索的源文件夹路径，会包含子文件夹。", GH_ParamAccess.item);
            pManager.AddTextParameter("后缀名", "后缀名", "要查找的文件名或后缀通配符，例如 *.stp、M*、stp。", GH_ParamAccess.item, "*");
            pManager.AddTextParameter("新文件夹", "新文件夹", "复制去重文件的目标文件夹路径。", GH_ParamAccess.item);
            pManager.AddBooleanParameter("执行", "执行", "点击按钮或从 False 变为 True 时执行；复位后保留上一次输出。", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("去重后的文件名", "文件名", "去重后的文件名。", GH_ParamAccess.list);
            pManager.AddIntegerParameter("每个文件对应的数量", "数量", "每个去重文件名在源文件夹中出现的数量。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string sourceFolder = string.Empty;
            string pattern = "*";
            string targetFolder = string.Empty;
            bool run = false;

            DA.GetData(0, ref sourceFolder);
            DA.GetData(1, ref pattern);
            DA.GetData(2, ref targetFolder);
            DA.GetData(3, ref run);

            if (run && !_lastRun)
                Execute(sourceFolder, pattern, targetFolder);

            _lastRun = run;

            DA.SetDataList(0, _cachedNames);
            DA.SetDataList(1, _cachedCounts);
        }

        private void Execute(string sourceFolder, string pattern, string targetFolder)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "源文件夹不能为空。");
                return;
            }

            if (!Directory.Exists(sourceFolder))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "找不到源文件夹：" + sourceFolder);
                return;
            }

            if (string.IsNullOrWhiteSpace(targetFolder))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "新文件夹不能为空。");
                return;
            }

            try
            {
                string searchPattern = NormalizePattern(pattern);
                string fullSourceFolder = Path.GetFullPath(sourceFolder);
                string fullTargetFolder = Path.GetFullPath(targetFolder);

                Directory.CreateDirectory(fullTargetFolder);

                List<string> files = Directory.EnumerateFiles(fullSourceFolder, searchPattern, SearchOption.AllDirectories)
                    .Where(file => !IsInDirectory(file, fullTargetFolder))
                    .ToList();

                List<FileGroup> groups = files
                    .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new FileGroup
                    {
                        FileName = group.Key,
                        Count = group.Count(),
                        SourcePath = group.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).First()
                    })
                    .OrderBy(group => group.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (FileGroup group in groups)
                {
                    string targetPath = Path.Combine(fullTargetFolder, group.FileName);
                    if (!PathsEqual(group.SourcePath, targetPath))
                        File.Copy(group.SourcePath, targetPath, true);
                }

                _cachedNames.Clear();
                _cachedNames.AddRange(groups.Select(group => group.FileName));
                _cachedCounts.Clear();
                _cachedCounts.AddRange(groups.Select(group => group.Count));
            }
            catch (UnauthorizedAccessException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "没有权限读取或复制文件：" + ex.Message);
            }
            catch (IOException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "读取或复制文件失败：" + ex.Message);
            }
            catch (ArgumentException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "文件夹路径或通配符无效：" + ex.Message);
            }
        }

        private static string NormalizePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return "*";

            string result = pattern.Trim();
            if (result.IndexOfAny(new[] { '*', '?' }) >= 0)
                return result;

            if (result.StartsWith(".", StringComparison.Ordinal))
                return "*" + result;

            if (result.IndexOf('.') < 0)
                return "*." + result;

            return result;
        }

        private static bool IsInDirectory(string filePath, string directory)
        {
            string fullFilePath = Path.GetFullPath(filePath);
            string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return fullFilePath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_CopyUniqueFilesByPattern"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("67B5C331-E7B9-49F5-8D87-4FA2B69BA8BC"); }
        }

        private class FileGroup
        {
            public string FileName { get; set; }
            public int Count { get; set; }
            public string SourcePath { get; set; }
        }
    }
}
