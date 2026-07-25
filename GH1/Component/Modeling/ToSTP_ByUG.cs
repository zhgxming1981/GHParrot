using Grasshopper.Kernel;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NS_Parrot
{
    public class ToSTP_ByUG : GH_Component
    {
        private string _lastInputSignature;
        private string _lastStatus;

        public ToSTP_ByUG()
          : base("通过UG NX导出STP", "UG导出STP",
              "通过 NX Journal 打开并重新导出源目录中的 STP 文件。",
              "Parrot", "Modeling")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("源文件夹", "源文件夹", "包含 STP 文件的源文件夹，会递归处理子文件夹。", GH_ParamAccess.item);
            pManager.AddTextParameter("目标文件夹", "目标文件夹", "可选。为空时输出到源文件同级的 _UG 文件夹。", GH_ParamAccess.item);
            pManager.AddBooleanParameter("转换", "转换", "开启后通过 UG NX 导出 STP。", GH_ParamAccess.item, false);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("状态", "状态", "导出状态。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string source = null;
            string target = null;
            bool run = false;
            if (!DA.GetData(0, ref source))
                return;

            DA.GetData(1, ref target);
            DA.GetData(2, ref run);
            string inputSignature = string.Join("|", source ?? string.Empty, target ?? string.Empty);
            if (!string.Equals(_lastInputSignature, inputSignature, StringComparison.Ordinal))
            {
                _lastInputSignature = inputSignature;
                _lastStatus = null;
            }

            if (!run)
            {
                DA.SetData(0, _lastStatus ?? "未执行：转换开关未开启。");
                return;
            }

            if (!Directory.Exists(source))
            {
                const string message = "源文件夹不存在。";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
                DA.SetData(0, message);
                return;
            }

            string nxRunner = FindNxRunner();
            string journal = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "UGNxStepExport.exe");
            if (string.IsNullOrWhiteSpace(nxRunner) || !File.Exists(journal))
            {
                string message = string.IsNullOrWhiteSpace(nxRunner)
                    ? "未找到 NX 的 run_dotnet_nxopen.exe。"
                    : "未找到 UGNxStepExport.exe，请使用包含该文件的最新构建输出。";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
                DA.SetData(0, message);
                return;
            }

            string[] files = Directory.GetFiles(source, "*.stp", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(source, "*.step", SearchOption.AllDirectories))
                .Where(file => !IsUgOutputFolder(Path.GetDirectoryName(file)))
                .ToArray();

            int success = 0;
            int failure = 0;
            string logPath = Path.Combine(source, "UG导出日志.txt");
            using (StreamWriter log = new StreamWriter(logPath, false, System.Text.Encoding.UTF8))
            foreach (string file in files)
            {
                string destinationDirectory = GetDestinationDirectory(source, target, file);
                string destinationFile = Path.Combine(destinationDirectory, Path.GetFileNameWithoutExtension(file) + ".stp");
                try
                {
                    Directory.CreateDirectory(destinationDirectory);
                    RunNxJournal(nxRunner, journal, file, destinationFile);
                    success++;
                    log.WriteLine("成功：" + file);
                }
                catch (Exception ex)
                {
                    failure++;
                    string detail = Path.GetFileName(file) + " 导出失败：" + ex.Message;
                    log.WriteLine(detail);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, detail);
                }
            }

            _lastStatus = string.Format("完成：成功 {0}，失败 {1}。\n日志：{2}", success, failure, logPath);
            DA.SetData(0, _lastStatus);
        }

        private static void RunNxJournal(string nxRunner, string journal, string sourceFile, string destinationFile)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = nxRunner,
                Arguments = Quote(journal) + " " + Quote(sourceFile) + " " + Quote(destinationFile),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            string nxBinDirectory = Path.GetDirectoryName(nxRunner);
            string nxBaseDirectory = Directory.GetParent(nxBinDirectory).FullName;
            startInfo.EnvironmentVariables["UGII_ROOT_DIR"] = nxBinDirectory;
            startInfo.EnvironmentVariables["UGII_BASE_DIR"] = nxBaseDirectory;
            startInfo.EnvironmentVariables["UGII_TEMPLATE_DIR"] = Path.Combine(nxBaseDirectory, "UGII", "templates");
            startInfo.EnvironmentVariables["STEP214UG_DIR"] = Path.Combine(nxBaseDirectory, "STEP214UG");

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || !File.Exists(destinationFile))
                {
                    string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                    if (string.IsNullOrWhiteSpace(detail))
                        detail = "NX Journal 未生成 STP 文件，且未返回错误文本。退出码：" + process.ExitCode;
                    throw new InvalidOperationException(detail.Trim());
                }
            }
        }

        private static string FindNxRunner()
        {
            string[] roots =
            {
                Environment.GetEnvironmentVariable("UGII_BASE_DIR"),
                @"D:\Program Files\Siemens\DesigncenterNX2512",
                @"C:\Program Files\Siemens\DesigncenterNX2512"
            };
            foreach (string root in roots.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string runner = Path.Combine(root, "NXBIN", "run_dotnet_nxopen.exe");
                if (File.Exists(runner))
                    return runner;
            }
            return null;
        }

        private static string GetDestinationDirectory(string sourceRoot, string targetRoot, string sourceFile)
        {
            string sourceDirectory = Path.GetDirectoryName(sourceFile);
            if (string.IsNullOrWhiteSpace(targetRoot))
                return sourceDirectory + "_UG";
            string relative = sourceDirectory.Substring(sourceRoot.TrimEnd('\\').Length).TrimStart('\\');
            return Path.Combine(targetRoot, relative + "_UG");
        }

        private static bool IsUgOutputFolder(string directory)
        {
            return !string.IsNullOrWhiteSpace(directory) && directory.EndsWith("_UG", StringComparison.OrdinalIgnoreCase);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_ToSTP_ByCatia"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("E5A41B49-8D1D-4D73-9DF4-8E372314D8F4"); }
        }
    }
}
