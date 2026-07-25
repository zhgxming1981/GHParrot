using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class Excel2Read : GH_Component
    {
        private long _lastSequence = -1;
        private List<string> _rows = new List<string>();
        public Excel2Read() : base("读取Excel V2", "读取V2", "从当前 Sheet 读取区域", "Parrot", "Excel2") { }
        protected override void RegisterInputParams(GH_InputParamManager p) { p.AddGenericParameter("执行引用", "执行", "上游 Excel2 电池输出的执行引用", GH_ParamAccess.item); p.AddTextParameter("读取区域", "区域", "例如 A1:C10", GH_ParamAccess.item); }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddTextParameter("结果", "结果", "每行格式为 A|B|C", GH_ParamAccess.list); p.AddGenericParameter("执行引用", "执行", "读取后的执行引用", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess DA) { object reference = null; string range = string.Empty; DA.GetData(0, ref reference); DA.GetData(1, ref range); try { var context = Excel2SheetTools.GetContext(reference); if (context.Sequence == _lastSequence) return; _lastSequence = context.Sequence; _rows = Excel2SheetTools.ReadRows(Excel2SheetTools.GetSheet(context), range); DA.SetData(1, context.Next()); Message = "完成"; } catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "读取失败: " + ex.Message); } DA.SetDataList(0, _rows); }
        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2Read"); public override Guid ComponentGuid => new Guid("CBC5B8D5-B5C6-4564-A672-17C0B4871AA4");
    }

    public class Excel2Write : GH_Component
    {
        private long _lastSequence = -1;
        public Excel2Write() : base("写入Excel V2", "写入V2", "向当前 Sheet 写入数据", "Parrot", "Excel2") { }
        protected override void RegisterInputParams(GH_InputParamManager p) { p.AddGenericParameter("执行引用", "执行", "上游 Excel2 电池输出的执行引用", GH_ParamAccess.item); p.AddTextParameter("位置", "位置", "起始位置，例如 A1", GH_ParamAccess.item); p.AddTextParameter("数据", "数据", "每行用 | 分隔列", GH_ParamAccess.list); p.AddBooleanParameter("覆盖", "覆盖", "True 覆盖区域；False 从该列下一空行追加", GH_ParamAccess.item, true); p[3].Optional = true; }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddTextParameter("日志", "日志", "写入结果", GH_ParamAccess.item); p.AddBooleanParameter("完成", "完成", "写入完成", GH_ParamAccess.item); p.AddGenericParameter("执行引用", "执行", "写入后的执行引用", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess DA) { object reference = null; string cell = string.Empty; var data = new List<string>(); bool overwrite = true; DA.GetData(0, ref reference); DA.GetData(1, ref cell); DA.GetDataList(2, data); DA.GetData(3, ref overwrite); try { var context = Excel2SheetTools.GetContext(reference); if (context.Sequence == _lastSequence) return; _lastSequence = context.Sequence; Excel2SheetTools.WriteRows(Excel2SheetTools.GetSheet(context), cell, data, overwrite); DA.SetData(0, "写入完成"); DA.SetData(1, true); DA.SetData(2, context.Next()); } catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "写入失败: " + ex.Message); DA.SetData(0, "错误: " + ex.Message); DA.SetData(1, false); } }
        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2Write"); public override Guid ComponentGuid => new Guid("7E393B7B-10F4-4EBF-999D-9697BA374E87");
    }

    public class Excel2ClearRange : GH_Component
    {
        private long _lastSequence = -1;
        public Excel2ClearRange() : base("清空Excel区域 V2", "清空V2", "清空当前 Sheet 的单元格区域", "Parrot", "Excel2") { }
        protected override void RegisterInputParams(GH_InputParamManager p) { p.AddGenericParameter("执行引用", "执行", "上游 Excel2 电池输出的执行引用", GH_ParamAccess.item); p.AddTextParameter("单元格范围", "Range", "例如 A1:D20", GH_ParamAccess.item); }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddBooleanParameter("完成", "Done", "清空完成", GH_ParamAccess.item); p.AddTextParameter("消息", "M", "执行结果", GH_ParamAccess.item); p.AddGenericParameter("执行引用", "执行", "清空后的执行引用", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess DA) { object reference = null; string range = string.Empty; DA.GetData(0, ref reference); DA.GetData(1, ref range); Excel.Range target = null; try { var context = Excel2SheetTools.GetContext(reference); if (context.Sequence == _lastSequence) return; _lastSequence = context.Sequence; var sheet = Excel2SheetTools.GetSheet(context); target = sheet.Range[range]; target.ClearContents(); (sheet.Parent as Excel.Workbook)?.Save(); DA.SetData(0, true); DA.SetData(1, "清空完成"); DA.SetData(2, context.Next()); } catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "清空失败: " + ex.Message); DA.SetData(0, false); DA.SetData(1, "清空失败: " + ex.Message); } finally { Excel2SheetTools.Release(target); } }
        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2ClearRange"); public override Guid ComponentGuid => new Guid("03E611E7-3E13-4A1C-9C90-C0D29BEE6EEA");
    }

    public class Excel2NextAppendRow : GH_Component
    {
        private long _lastSequence = -1;
        public Excel2NextAppendRow() : base("Excel下一追加行 V2", "下一行V2", "查找当前 Sheet 指定列的下一空行", "Parrot", "Excel2") { }
        protected override void RegisterInputParams(GH_InputParamManager p) { p.AddGenericParameter("执行引用", "执行", "上游 Excel2 电池输出的执行引用", GH_ParamAccess.item); p.AddTextParameter("列号", "Col", "列号，例如 A、B", GH_ParamAccess.list); p.AddIntegerParameter("起始行", "StartRow", "默认 1", GH_ParamAccess.item, 1); p[2].Optional = true; }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddIntegerParameter("行号", "Row", "下一空行行号", GH_ParamAccess.item); p.AddGenericParameter("执行引用", "执行", "查询后的执行引用", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess DA) { object reference = null; var columns = new List<string>(); int start = 1; DA.GetData(0, ref reference); DA.GetDataList(1, columns); DA.GetData(2, ref start); try { var context = Excel2SheetTools.GetContext(reference); if (context.Sequence == _lastSequence) return; _lastSequence = context.Sequence; DA.SetData(0, Excel2SheetTools.GetNextAppendRow(Excel2SheetTools.GetSheet(context), columns, start)); DA.SetData(1, context.Next()); } catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "查询失败: " + ex.Message); } }
        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2NextAppendRow"); public override Guid ComponentGuid => new Guid("D3C48F90-4CB3-4302-9F2B-89D7CC49D11A");
    }
}
