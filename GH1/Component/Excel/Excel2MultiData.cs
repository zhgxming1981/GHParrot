using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

namespace NS_Parrot
{
    public class Excel2MultiData : GH_Component
    {
        private long _lastSequence = -1;
        private bool _wrote, _resetPass;
        public Excel2MultiData() : base("多位置写入Excel V2", "多写入V2", "向当前 Sheet 的多个起始位置写入同一组数据", "Parrot", "Excel2") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("执行引用", "执行", "上游 Excel2 电池输出的执行引用", GH_ParamAccess.item);
            p.AddTextParameter("起始单元格", "Cell", "多个起始单元格", GH_ParamAccess.list);
            p.AddTextParameter("数据", "Data", "每行格式 A|B|C", GH_ParamAccess.list);
            // 执行引用在脉冲复位时可能为空，仍需运行以输出False。
            p[0].Optional = p[1].Optional = p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("日志", "Log", "执行结果", GH_ParamAccess.item);
            p.AddGenericParameter("执行引用", "执行", "写入后的执行引用", GH_ParamAccess.item);
            p.AddBooleanParameter("写完", "写完", "所有位置写入并保存成功后输出一次True，随后恢复False。", GH_ParamAccess.item);
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            _wrote = false;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.SetData(2, false);
            if (_resetPass) return;
            object reference = null;
            var cells = new List<string>();
            var data = new List<string>();
            if (!DA.GetData(0, ref reference) || reference == null) return;
            DA.GetDataList(1, cells);
            DA.GetDataList(2, data);
            try
            {
                var context = Excel2SheetTools.GetContext(reference);
                if (context.Sequence == _lastSequence) return;
                _lastSequence = context.Sequence;
                if (cells.Count == 0) throw new ArgumentException("没有输入起始单元格。");
                var sheet = Excel2SheetTools.GetSheet(context);
                foreach (string cell in cells) Excel2SheetTools.WriteRows(sheet, cell, data, true);
                _wrote = true;
                DA.SetData(0, "写入完成: " + cells.Count + " 个位置。");
                DA.SetData(1, context.Next());
                DA.SetData(2, true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "多位置写入失败: " + ex.Message);
                DA.SetData(0, "错误: " + ex.Message);
            }
        }

        protected override void AfterSolveInstance()
        {
            base.AfterSolveInstance();
            if (_resetPass) { _resetPass = false; return; }
            if (!_wrote) return;
            var document = OnPingDocument();
            document?.ScheduleSolution(1, doc =>
            {
                if (OnPingDocument() != doc) return;
                _resetPass = true;
                ExpireSolution(false);
            });
        }
        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2MultiData");
        public override Guid ComponentGuid => new Guid("227A63B0-85C7-4142-B01C-AE40BB1C2D17");
    }
}
