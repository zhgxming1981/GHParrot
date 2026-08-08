using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>
    /// 比较两个文件夹中的文件，将文件夹2中有但文件夹1中没有的文件复制到目标文件夹。
    /// 比较依据为文件名（不含路径），不检查文件内容。
    /// </summary>
    public class CompareAndCopyMissingFiles : GH_Component
    {
        public bool RunNow { get; set; }
        private List<string> _lastOnlyIn1 = new List<string>();
        private List<string> _lastOnlyIn2 = new List<string>();
        private string _lastSummary = string.Empty;

        public CompareAndCopyMissingFiles()
          : base("比较并复制缺失文件", "文件比较",
              "比较文件夹1和文件夹2中的文件（按文件名），将文件夹2中有但文件夹1中没有的文件复制到目标文件夹。支持后缀过滤和子文件夹搜索。",
              "Parrot", "Library")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件夹1", "文件夹1",
                "参照文件夹（以其文件列表为基准）", GH_ParamAccess.item);
            pManager.AddTextParameter("文件夹2", "文件夹2",
                "比较文件夹（其中的新文件将被复制）", GH_ParamAccess.item);
            pManager.AddTextParameter("目标文件夹", "目标文件夹",
                "复制目标路径（自动创建目录）", GH_ParamAccess.item);
            pManager.AddTextParameter("后缀名", "后缀名",
                "仅搜索此类型的文件，例如 .3dm、.txt。留空表示不过滤。", GH_ParamAccess.item);
            pManager[3].Optional = true;
            pManager.AddBooleanParameter("搜索子文件夹", "搜索子文件夹",
                "是否递归搜索子文件夹，默认否。", GH_ParamAccess.item);
            pManager[4].Optional = true;
            pManager.AddBooleanParameter("执行", "执行",
                "设为 True 或按下按钮执行比较与复制。", GH_ParamAccess.item);
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("文件夹1独有", "文件夹1独有",
                "文件夹1中有但文件夹2中没有的文件路径列表", GH_ParamAccess.list);
            pManager.AddTextParameter("文件夹2独有", "文件夹2独有",
                "文件夹2中有但文件夹1中没有的文件路径列表（这些文件将被复制到目标文件夹）", GH_ParamAccess.list);
            pManager.AddTextParameter("日志", "日志",
                "操作摘要与错误信息", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string folder1 = string.Empty;
            string folder2 = string.Empty;
            string targetFolder = string.Empty;
            string extension = string.Empty;
            bool searchSubfolders = false;
            bool execute = false;

            if (!DA.GetData(0, ref folder1)) return;
            if (!DA.GetData(1, ref folder2)) return;
            if (!DA.GetData(2, ref targetFolder)) return;
            DA.GetData(3, ref extension);
            DA.GetData(4, ref searchSubfolders);
            DA.GetData(5, ref execute);

            // 未触发时输出上次缓存结果
            if (!RunNow && !execute)
            {
                DA.SetDataList(0, _lastOnlyIn1);
                DA.SetDataList(1, _lastOnlyIn2);
                DA.SetData(2, _lastSummary);
                return;
            }

            RunNow = false;

            // 校验路径
            if (string.IsNullOrWhiteSpace(folder1))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "文件夹1 路径为空。");
                return;
            }
            if (string.IsNullOrWhiteSpace(folder2))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "文件夹2 路径为空。");
                return;
            }
            if (string.IsNullOrWhiteSpace(targetFolder))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "目标文件夹 路径为空。");
                return;
            }

            if (!Directory.Exists(folder1))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "文件夹1 不存在：" + folder1);
                return;
            }
            if (!Directory.Exists(folder2))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "文件夹2 不存在：" + folder2);
                return;
            }

            // 标准化后缀名
            extension = (extension ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(extension) && !extension.StartsWith(".", StringComparison.Ordinal))
                extension = "." + extension;

            // 构建文件名过滤器：无通配符 → 精确后缀匹配；含通配符 → glob 转正则
            Func<string, bool> nameFilter;
            if (string.IsNullOrEmpty(extension))
            {
                nameFilter = _ => true;
            }
            else if (extension.IndexOf('*') < 0 && extension.IndexOf('?') < 0)
            {
                // 无通配符：精确匹配后缀。输入 ".3dm" 只匹配 .3dm，不匹配 .3dmbak
                string ext = extension;
                nameFilter = f => string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // 含通配符：glob → 正则，对完整文件名匹配。输入 ".3dm*" 匹配 .3dm 和 .3dmbak
                var regex = new System.Text.RegularExpressions.Regex(
                    GlobToRegex(extension),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                nameFilter = f => regex.IsMatch(f);
            }

            try
            {
                // 收集文件夹1和文件夹2中的文件名集合
                SearchOption searchOption = searchSubfolders
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                string[] files1;
                string[] files2;
                try
                {
                    files1 = Directory.GetFiles(folder1, "*", searchOption)
                        .Where(f => nameFilter(Path.GetFileName(f)))
                        .ToArray();
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "读取文件夹1失败：" + ex.Message);
                    return;
                }
                try
                {
                    files2 = Directory.GetFiles(folder2, "*", searchOption)
                        .Where(f => nameFilter(Path.GetFileName(f)))
                        .ToArray();
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "读取文件夹2失败：" + ex.Message);
                    return;
                }

                // 建立两边的文件名集合（仅文件名，不含路径）
                var namesInFolder1 = new HashSet<string>(
                    files1.Select(f => Path.GetFileName(f)),
                    StringComparer.OrdinalIgnoreCase);
                var namesInFolder2 = new HashSet<string>(
                    files2.Select(f => Path.GetFileName(f)),
                    StringComparer.OrdinalIgnoreCase);

                // 文件夹1独有、文件夹2独有
                var onlyInFolder1 = files1
                    .Where(f => !namesInFolder2.Contains(Path.GetFileName(f)))
                    .ToList();
                var onlyInFolder2 = files2
                    .Where(f => !namesInFolder1.Contains(Path.GetFileName(f)))
                    .ToList();

                // 按文件名去重（子文件夹搜索时，不同子文件夹可能有同名文件）
                int dup1 = onlyInFolder1.Count;
                onlyInFolder1 = DeduplicateByFileName(onlyInFolder1);
                dup1 -= onlyInFolder1.Count;

                int dup2 = onlyInFolder2.Count;
                onlyInFolder2 = DeduplicateByFileName(onlyInFolder2);
                dup2 -= onlyInFolder2.Count;

                // 确保目标文件夹存在
                Directory.CreateDirectory(targetFolder);

                // 将文件夹2独有的文件复制到目标文件夹（已去重）
                var copiedFiles = new List<string>();
                var errors = new List<string>();
                int skippedDuplicate = 0;
                var copiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file2 in onlyInFolder2)
                {
                    string fileName = Path.GetFileName(file2);
                    string destPath = Path.Combine(targetFolder, fileName);

                    // 本轮已复制过同名文件（去重保护）
                    if (!copiedNames.Add(fileName))
                        continue;

                    // 目标已存在同名文件时跳过（避免覆盖）
                    if (File.Exists(destPath))
                    {
                        skippedDuplicate++;
                        continue;
                    }

                    try
                    {
                        File.Copy(file2, destPath, overwrite: false);
                        copiedFiles.Add(destPath);
                    }
                    catch (Exception ex)
                    {
                        errors.Add("复制失败：" + fileName + " — " + ex.Message);
                    }
                }

                // 缓存并输出
                _lastOnlyIn1 = onlyInFolder1;
                _lastOnlyIn2 = onlyInFolder2;

                _lastSummary = "文件夹1: " + files1.Length.ToString() + " 个文件（" +
                    (searchSubfolders ? "含子文件夹" : "仅顶层") + "）" +
                    "；文件夹2: " + files2.Length.ToString() + " 个文件" +
                    "；文件夹1独有: " + onlyInFolder1.Count.ToString() + " 个" +
                    (dup1 > 0 ? "（去重移除 " + dup1.ToString() + " 个同名文件）" : "") +
                    "；文件夹2独有: " + onlyInFolder2.Count.ToString() + " 个" +
                    (dup2 > 0 ? "（去重移除 " + dup2.ToString() + " 个同名文件）" : "") +
                    "；成功复制: " + copiedFiles.Count.ToString() + " 个" +
                    "；目标已存在跳过: " + skippedDuplicate.ToString() + " 个";
                if (errors.Count > 0)
                    _lastSummary += "；错误: " + string.Join("; ", errors);

                DA.SetDataList(0, _lastOnlyIn1);
                DA.SetDataList(1, _lastOnlyIn2);
                DA.SetData(2, _lastSummary);

                if (errors.Count > 0)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "部分文件复制失败：" + string.Join("; ", errors));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "操作失败：" + ex.Message);
            }
        }

        public override void CreateAttributes()
        {
            Attributes = new CompareAndCopyMissingFilesAttributes(this);
        }

        /// <summary>
        /// 将简单 glob 模式转为正则表达式，用于文件名后缀匹配。
        /// 支持 *（任意字符序列）和 ?（单个字符），其余字符原义匹配。
        /// </summary>
        private static string GlobToRegex(string glob)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('^');
            foreach (char c in glob)
            {
                switch (c)
                {
                    case '*': sb.Append(".*"); break;
                    case '?': sb.Append('.'); break;
                    case '.': sb.Append(@"\."); break;
                    default:
                        if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '_' || c == '-')
                            sb.Append(c);
                        else
                            sb.Append(@"\" + c);
                        break;
                }
            }
            sb.Append('$');
            return sb.ToString();
        }

        /// <summary>
        /// 按文件名去重，保留每个文件名的第一个出现（维持原始顺序）。
        /// </summary>
        private static List<string> DeduplicateByFileName(List<string> paths)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(paths.Count);
            foreach (string p in paths)
            {
                if (seen.Add(Path.GetFileName(p)))
                    result.Add(p);
            }
            return result;
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_CompareAndCopyMissingFiles");

        public override Guid ComponentGuid => new Guid("8A3F7D2C-9E61-4B52-A1D0-6F8E4C2B7A95");
    }

    internal class CompareAndCopyMissingFilesAttributes : GH_ComponentAttributes
    {
        public CompareAndCopyMissingFilesAttributes(CompareAndCopyMissingFiles owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 22.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 22, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, GH_Palette.Black))
            {
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            }

            using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("执行", font, Brushes.White, buttonRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 22, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                CompareAndCopyMissingFiles component = (CompareAndCopyMissingFiles)Owner;
                component.RunNow = true;
                component.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return base.RespondToMouseDown(sender, e);
        }
    }
}
