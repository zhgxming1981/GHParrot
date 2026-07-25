using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class Excel2TemplateWrite : GH_Component
    {
        private long _lastSequence = -1;
        public Excel2TemplateWrite() : base("模板写入Excel V2", "模板写入V2", "将模板 Sheet 内容复制到当前 Sheet 后写入数据", "Parrot", "Excel2") { }
        protected override void RegisterInputParams(GH_InputParamManager p) { p.AddGenericParameter("执行引用", "执行", "主链执行引用；其当前 Sheet 为目标 Sheet", GH_ParamAccess.item); p.AddGenericParameter("模板Sheet引用", "模板Sheet", "作为模板的 Sheet 引用", GH_ParamAccess.item); p.AddTextParameter("位置", "位置", "起始位置，例如 A1", GH_ParamAccess.item); p.AddTextParameter("数据", "数据", "每行格式 A|B|C", GH_ParamAccess.list); p.AddBooleanParameter("复制模板", "模板", "写入前复制模板 Sheet 已用区域", GH_ParamAccess.item, true); p.AddBooleanParameter("覆盖", "覆盖", "True 覆盖；False 追加", GH_ParamAccess.item, true); p[4].Optional = p[5].Optional = true; }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddTextParameter("日志", "日志", "执行结果", GH_ParamAccess.item); p.AddBooleanParameter("完成", "完成", "写入完成", GH_ParamAccess.item); p.AddGenericParameter("执行引用", "执行", "以目标 Sheet 继续的执行引用", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess DA) { object reference = null, templateReference = null; string cell = string.Empty; var data = new List<string>(); bool copyTemplate = true, overwrite = true; DA.GetData(0, ref reference); DA.GetData(1, ref templateReference); DA.GetData(2, ref cell); DA.GetDataList(3, data); DA.GetData(4, ref copyTemplate); DA.GetData(5, ref overwrite); Excel.Range source = null, destination = null; try { var context = Excel2SheetTools.GetContext(reference); if (context.Sequence == _lastSequence) return; _lastSequence = context.Sequence; var template = Excel2SheetTools.GetSheet(templateReference); var target = Excel2SheetTools.GetSheet(context); if (copyTemplate) { source = template.UsedRange; destination = target.Cells[1, 1] as Excel.Range; source.Copy(destination); } Excel2SheetTools.WriteRows(target, cell, data, overwrite); DA.SetData(0, "写入完成"); DA.SetData(1, true); DA.SetData(2, context.Next(target)); } catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "模板写入失败: " + ex.Message); DA.SetData(0, "错误: " + ex.Message); DA.SetData(1, false); } finally { Excel2SheetTools.Release(destination); Excel2SheetTools.Release(source); } }
        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2Write");
        public override Guid ComponentGuid => new Guid("4C3CDCA7-BEBF-48E8-A83D-828C0056B47E");
    }
}
