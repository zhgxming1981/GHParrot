using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class Excel2InsertPicture : GH_Component
    {
        private long _lastSequence = -1;
        public Excel2InsertPicture() : base("Excel插入图片 V2", "插图V2", "向当前 Sheet 插入图片", "Parrot", "Excel2") { }
        protected override void RegisterInputParams(GH_InputParamManager p) { p.AddGenericParameter("执行引用", "执行", "上游 Excel2 电池输出的执行引用", GH_ParamAccess.item); p.AddTextParameter("图片名单元格", "图片单元格", "例如 D21、D22", GH_ParamAccess.list); p.AddTextParameter("图片文件夹", "文件夹", "图片目录", GH_ParamAccess.item); p.AddNumberParameter("高度扣除", "高度扣除", "图片高度 = 行高 - 此值", GH_ParamAccess.item, 0.0); p.AddTextParameter("插入单元格", "插入单元格", "例如 F21、F22", GH_ParamAccess.list); p[3].Optional = true; }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddIntegerParameter("数量", "数量", "插入图片数量", GH_ParamAccess.item); p.AddBooleanParameter("完成", "完成", "插入完成", GH_ParamAccess.item); p.AddTextParameter("日志", "日志", "执行结果", GH_ParamAccess.item); p.AddGenericParameter("执行引用", "执行", "插入后的执行引用", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess DA) { object reference = null; List<string> pictures = new List<string>(), cells = new List<string>(); string folder = string.Empty; double minus = 0; DA.GetData(0, ref reference); DA.GetDataList(1, pictures); DA.GetData(2, ref folder); DA.GetData(3, ref minus); DA.GetDataList(4, cells); try { var context = Excel2SheetTools.GetContext(reference); if (context.Sequence == _lastSequence) return; _lastSequence = context.Sequence; Excel.Worksheet sheet = Excel2SheetTools.GetSheet(context); List<string> messages; int count = ExcelPulseTools.InsertPictures(context.Workbook.FullName, sheet.Name, pictures, folder, minus, cells, out messages); DA.SetData(0, count); DA.SetData(1, true); DA.SetDataList(2, messages); DA.SetData(3, context.Next()); } catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "插入图片失败: " + ex.Message); DA.SetData(1, false); DA.SetData(2, "插入图片失败: " + ex.Message); } }
        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2InsertPicture"); public override Guid ComponentGuid => new Guid("75A6E965-84D0-418A-BC3F-3E9539F0B11A");
    }

    public class Excel2MergeRanges : GH_Component
    {
        private long _lastSequence = -1;
        public Excel2MergeRanges() : base("合并Excel区域 V2", "合并V2", "读取工作簿内各工作表的指定区域并汇总为文本数据", "Parrot", "Excel2") { }
        protected override void RegisterInputParams(GH_InputParamManager p) { p.AddGenericParameter("执行引用", "执行", "执行引用所属工作簿将作为数据源", GH_ParamAccess.item); p.AddTextParameter("排除工作表", "排除", "不参与读取的工作表名称，可为空", GH_ParamAccess.list); p.AddTextParameter("读取区域", "区域", "例如 A1:D10", GH_ParamAccess.item); p[1].Optional = true; }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddTextParameter("数据", "数据", "汇总结果；每行格式为 A|B|C", GH_ParamAccess.list); p.AddTextParameter("日志", "日志", "读取结果或错误", GH_ParamAccess.item); p.AddGenericParameter("执行引用", "执行", "读取后的执行引用", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object reference = null;
            List<string> ignoredNames = new List<string>();
            string range = string.Empty;
            DA.GetData(0, ref reference);
            DA.GetDataList(1, ignoredNames);
            DA.GetData(2, ref range);
            try
            {
                Excel2ExecutionContext context = Excel2SheetTools.GetContext(reference);
                if (context.Sequence == _lastSequence) return;
                _lastSequence = context.Sequence;

                HashSet<string> ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in ignoredNames)
                    if (!string.IsNullOrWhiteSpace(name)) ignored.Add(name.Trim());

                List<string> data = new List<string>();
                int readSheetCount = 0;
                int sheetCount = context.Workbook.Worksheets.Count;
                for (int index = 1; index <= sheetCount; index++)
                {
                    Excel.Worksheet sheet = null;
                    try
                    {
                        sheet = context.Workbook.Worksheets[index] as Excel.Worksheet;
                        if (sheet == null || ignored.Contains(sheet.Name)) continue;
                        List<string> rows = Excel2SheetTools.ReadRows(sheet, range);
                        foreach (string row in rows)
                            if (!string.IsNullOrWhiteSpace((row ?? string.Empty).Replace("|", string.Empty))) data.Add(row);
                        readSheetCount++;
                    }
                    finally
                    {
                        if (sheet != null && (context.Sheet == null || !string.Equals(sheet.Name, context.Sheet.Name, StringComparison.OrdinalIgnoreCase)))
                            Excel2SheetTools.Release(sheet);
                    }
                }

                DA.SetDataList(0, data);
                DA.SetData(1, "已读取 " + readSheetCount + " 个工作表，共 " + data.Count + " 行数据。");
                DA.SetData(2, context.Next());
                Message = data.Count + " 行";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "读取失败: " + ex.Message);
                DA.SetData(1, "读取失败: " + ex.Message);
            }
        }
        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2MergeRanges"); public override Guid ComponentGuid => new Guid("392E18ED-5A19-4504-85E6-9D98A65CB7EE");
    }
}
