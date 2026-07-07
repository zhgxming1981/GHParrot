using CommonFunction;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace NS_Parrot
{
    public class MergeFolderNewFiles : GH_Component
    {
        private readonly List<string> _newFileNames = new List<string>();
        private string _status = string.Empty;
        private bool _lastRun;

        public MergeFolderNewFiles()
          : base("合并文件夹新增文件", "合并新增文件",
              "将新文件夹中源文件夹没有的文件复制到源文件夹，并另存一份到新增文件夹。",
              "Parrot", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("源文件夹", "源文件夹", "原文件夹路径，会被复制新增文件后更新。", GH_ParamAccess.item);
            pManager.AddTextParameter("新文件夹", "新文件夹", "用于合并的新文件夹路径，会递归包含子文件夹。", GH_ParamAccess.item);
            pManager.AddTextParameter("新增文件夹", "新增文件夹", "只保存本次新增文件的目标文件夹路径。", GH_ParamAccess.item);
            pManager.AddTextParameter("后缀名", "后缀名", "文件后缀名，例如 *、stp、.dwg、*.xlsx。", GH_ParamAccess.item, "*");
            pManager.AddBooleanParameter("执行", "执行", "点击按钮或从 False 变为 True 时执行；复位后保留上一次输出。", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("新增文件名", "新增文件名", "本次新增的文件名。", GH_ParamAccess.list);
            pManager.AddTextParameter("状态", "状态", "执行结果或错误信息。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string sourceFolder = string.Empty;
            string newFolder = string.Empty;
            string addedFolder = string.Empty;
            string pattern = "*";
            bool run = false;

            DA.GetData(0, ref sourceFolder);
            DA.GetData(1, ref newFolder);
            DA.GetData(2, ref addedFolder);
            DA.GetData(3, ref pattern);
            DA.GetData(4, ref run);

            if (run && !_lastRun)
                Execute(sourceFolder, newFolder, addedFolder, pattern);

            _lastRun = run;

            DA.SetDataList(0, _newFileNames);
            DA.SetData(1, _status);
        }

        private void Execute(string sourceFolder, string newFolder, string addedFolder, string pattern)
        {
            _newFileNames.Clear();
            _status = string.Empty;

            if (!ValidateInputFolder(sourceFolder, "源文件夹", out string fullSourceFolder))
                return;

            if (!ValidateInputFolder(newFolder, "新文件夹", out string fullNewFolder))
                return;

            if (string.IsNullOrWhiteSpace(addedFolder))
            {
                SetError("新增文件夹不能为空。");
                return;
            }

            try
            {
                string fullAddedFolder = Path.GetFullPath(addedFolder);
                string searchPattern = NormalizePattern(pattern);

                Directory.CreateDirectory(fullSourceFolder);
                Directory.CreateDirectory(fullAddedFolder);

                List<string> files = Directory.EnumerateFiles(fullNewFolder, searchPattern, SearchOption.AllDirectories)
                    .Where(file => MatchesSuffix(file, pattern))
                    .Where(file => !IsInDirectory(file, fullSourceFolder))
                    .Where(file => !IsInDirectory(file, fullAddedFolder))
                    .OrderBy(file => GetRelativePath(fullNewFolder, file), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (string file in files)
                {
                    string relativePath = GetRelativePath(fullNewFolder, file);
                    string sourceTargetPath = Path.Combine(fullSourceFolder, relativePath);

                    if (File.Exists(sourceTargetPath))
                        continue;

                    string addedTargetPath = Path.Combine(fullAddedFolder, relativePath);
                    CopyFile(file, sourceTargetPath);
                    CopyFile(file, addedTargetPath);

                    _newFileNames.Add(Path.GetFileName(file));
                }

                _status = $"完成：新增 {_newFileNames.Count} 个文件。";
            }
            catch (UnauthorizedAccessException ex)
            {
                SetError("没有权限读取或复制文件：" + ex.Message);
            }
            catch (IOException ex)
            {
                SetError("读取或复制文件失败：" + ex.Message);
            }
            catch (ArgumentException ex)
            {
                SetError("文件夹路径或匹配规则无效：" + ex.Message);
            }
        }

        private bool ValidateInputFolder(string folder, string name, out string fullFolder)
        {
            fullFolder = string.Empty;

            if (string.IsNullOrWhiteSpace(folder))
            {
                SetError(name + "不能为空。");
                return false;
            }

            fullFolder = Path.GetFullPath(folder);
            if (!Directory.Exists(fullFolder))
            {
                SetError("找不到" + name + "：" + fullFolder);
                return false;
            }

            return true;
        }

        private void SetError(string message)
        {
            _status = message;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
        }

        private static void CopyFile(string sourcePath, string targetPath)
        {
            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.Copy(sourcePath, targetPath, false);
        }

        private static string NormalizePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return "*";

            string result = pattern.Trim();
            if (result.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                if (result.StartsWith(".", StringComparison.Ordinal))
                    return "*" + result;

                if (result.IndexOf('.') < 0)
                    return "*." + result;

                return result;
            }

            if (result.StartsWith(".", StringComparison.Ordinal))
                return "*";

            if (result.IndexOf('.') < 0)
                return "*";

            return result;
        }

        private static bool MatchesSuffix(string filePath, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return true;

            string result = pattern.Trim();
            if (result == "*")
                return true;

            if (result.IndexOfAny(new[] { '*', '?' }) >= 0)
                return true;

            string extension = result.StartsWith(".", StringComparison.Ordinal)
                ? result
                : "." + result;

            return string.Equals(Path.GetExtension(filePath), extension, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInDirectory(string filePath, string directory)
        {
            string fullFilePath = Path.GetFullPath(filePath);
            string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return fullFilePath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRelativePath(string baseFolder, string filePath)
        {
            Uri baseUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(baseFolder)));
            Uri fileUri = new Uri(Path.GetFullPath(filePath));
            string relativePath = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString());
            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return path;

            return path + Path.DirectorySeparatorChar;
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_CopyUniqueFilesByPattern"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("B498CF8E-A68F-4E7C-8CCF-91D41586B81E"); }
        }
    }
}
