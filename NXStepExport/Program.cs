using NXOpen;
using System;
using System.IO;
using System.Threading;

namespace NXStepExport
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            if (args == null || args.Length != 2)
            {
                Console.Error.WriteLine("用法：UGNxStepExport.exe <源STP路径> <目标STP路径>");
                return 2;
            }

            string sourceFile = args[0];
            string targetFile = args[1];
            if (!File.Exists(sourceFile))
            {
                Console.Error.WriteLine("源文件不存在：" + sourceFile);
                return 3;
            }

            try
            {
                string targetDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);
                if (File.Exists(targetFile))
                    File.Delete(targetFile);

                Session session = Session.GetSession();
                string temporaryPartFile = Path.Combine(
                    targetDirectory,
                    "." + Path.GetFileNameWithoutExtension(targetFile) + ".ugimport.prt");
                if (File.Exists(temporaryPartFile))
                    File.Delete(temporaryPartFile);

                string templateFile = Path.Combine(
                    session.GetEnvironmentVariableValue("UGII_TEMPLATE_DIR"),
                    "model-plain-1-mm-template.prt");
                if (!File.Exists(templateFile))
                    throw new FileNotFoundException("未找到 NX 毫米零件模板。", templateFile);

                FileNew fileNew = session.Parts.FileNew();
                fileNew.TemplateFileName = "model-plain-1-mm-template.prt";
                fileNew.ApplicationName = "ModelTemplate";
                fileNew.Units = Part.Units.Millimeters;
                fileNew.UsesMasterModel = "No";
                fileNew.TemplatePresentationName = "Model";
                fileNew.SetCanCreateAltrep(false);
                fileNew.NewFileName = temporaryPartFile;
                fileNew.MakeDisplayedPart = true;
                Part part = (Part)fileNew.Commit();
                fileNew.Destroy();
                session.Parts.SetWork(part);
                session.ApplicationSwitchImmediate("UG_APP_MODELING");

                try
                {
                    Exception importCommitError = null;
                    Step214Importer importer = session.DexManager.CreateStep214Importer();
                    try
                    {
                        importer.SimplifyGeometry = true;
                        importer.LayerDefault = 1;
                        importer.ObjectTypes.Curves = true;
                        importer.ObjectTypes.Surfaces = true;
                        importer.ObjectTypes.Solids = true;
                        importer.ObjectTypes.PmiData = true;
                        importer.SettingsFile = session.GetEnvironmentVariableValue("STEP214UG_DIR") + "\\step214ug.def";
                        importer.InputFile = sourceFile;
                        importer.OutputFile = temporaryPartFile;
                        importer.FileOpenFlag = false;
                        try
                        {
                            importer.Commit();
                        }
                        catch (Exception ex)
                        {
                            importCommitError = ex;
                        }
                    }
                    finally
                    {
                        importer.Destroy();
                    }

                    try
                    {
                        WaitForStableFile(temporaryPartFile, "NX 未生成导入后的临时 PRT。");
                    }
                    catch (Exception)
                    {
                        if (importCommitError != null)
                            throw new InvalidOperationException("STEP 导入阶段失败。", importCommitError);
                        throw;
                    }

                    part.Close(
                        BasePart.CloseWholeTree.False,
                        BasePart.CloseModified.CloseModified,
                        null);

                    PartLoadStatus loadStatus;
                    part = session.Parts.OpenBaseDisplay(temporaryPartFile, out loadStatus) as Part;
                    if (loadStatus != null)
                        loadStatus.Dispose();
                    if (part == null)
                        throw new InvalidOperationException("NX 未打开导入生成的临时 PRT。");

                    session.Parts.SetWork(part);
                    session.ApplicationSwitchImmediate("UG_APP_MODELING");

                    Exception exportCommitError = null;
                    StepCreator stepCreator = session.DexManager.CreateStepCreator();
                    try
                    {
                        stepCreator.ExportFrom = StepCreator.ExportFromOption.DisplayPart;
                        stepCreator.SettingsFile = session.GetEnvironmentVariableValue("STEP214UG_DIR") + "\\step214ug.def";
                        stepCreator.OutputFile = targetFile;
                        try
                        {
                            stepCreator.Commit();
                        }
                        catch (Exception ex)
                        {
                            exportCommitError = ex;
                        }
                    }
                    finally
                    {
                        stepCreator.Destroy();
                    }

                    try
                    {
                        WaitForStableFile(targetFile, "NX 未生成 STP 文件。");
                    }
                    catch (Exception)
                    {
                        if (exportCommitError != null)
                            throw new InvalidOperationException("STEP 导出阶段失败。", exportCommitError);
                        throw;
                    }
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temporaryPartFile))
                            File.Delete(temporaryPartFile);
                    }
                    catch
                    {
                        // NX 仍占用临时零件时，交由下次运行开始前清理。
                    }
                }

                Console.WriteLine("OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static void WaitForStableFile(string path, string errorMessage)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(120);
            long previousLength = -1;
            int stableChecks = 0;

            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path))
                {
                    long currentLength = new FileInfo(path).Length;
                    if (currentLength > 0 && currentLength == previousLength)
                    {
                        stableChecks++;
                        if (stableChecks >= 4)
                            return;
                    }
                    else
                    {
                        stableChecks = 0;
                        previousLength = currentLength;
                    }
                }

                Thread.Sleep(250);
            }

            throw new IOException(errorMessage + " 路径：" + path);
        }
    }
}
