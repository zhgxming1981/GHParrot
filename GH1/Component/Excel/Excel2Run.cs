using Grasshopper.Kernel;
using System;

namespace NS_Parrot
{
    public class Excel2Run : GH_Component
    {
        private bool _lastRun;
        private long _sequence;

        public Excel2Run()
          : base("Excel2运行", "Run", "创建一次新的 Excel2 执行引用，驱动下游处理电池", "Parrot", "Excel2") { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("当前Sheet引用", "Sheet", "设置当前Sheet电池输出的工作表引用", GH_ParamAccess.item);
            p.AddBooleanParameter("运行", "Run", "False 变为 True 时创建新的执行引用", GH_ParamAccess.item, false);
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("执行引用", "执行", "连接到下游 Excel2 处理电池", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object reference = null;
            bool run = false;
            DA.GetData(0, ref reference);
            DA.GetData(1, ref run);
            bool trigger = run && !_lastRun;
            _lastRun = run;
            if (!trigger) return;
            try
            {
                var sheet = Excel2SheetTools.GetSheet(reference);
                var workbook = sheet.Parent as Microsoft.Office.Interop.Excel.Workbook;
                if (workbook == null) throw new ArgumentException("当前 Sheet 没有关联的 Excel 工作簿。");
                DA.SetData(0, new Excel2ExecutionContext(workbook, sheet, ++_sequence));
                Message = "执行 " + _sequence;
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "创建执行引用失败: " + ex.Message); }
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2Run");
        public override Guid ComponentGuid => new Guid("5B88225E-0AA9-42B3-B46E-A184E47F5922");
    }
}
