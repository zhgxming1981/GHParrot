using Excel = Microsoft.Office.Interop.Excel;
using Grasshopper.Kernel;
using System;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class Excel2ShowHide : GH_Component
    {
        public bool ShowExcel { get; private set; } = true;
        private long _lastSequence = -1;

        public Excel2ShowHide()
          : base("显示隐藏Excel V2", "显示隐藏V2", "通过右键“显示Excel”勾选状态显示或隐藏当前 Sheet 所属的 Excel，并传递 Sheet 引用", "Parrot", "Excel2")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("执行引用", "执行", "上一个 Excel2 电池输出的执行引用", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("执行引用", "执行", "传递显示状态后的执行引用", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object reference = null;
            DA.GetData(0, ref reference);

            try
            {
                Excel2ExecutionContext context = Excel2SheetTools.GetContext(reference);
                if (context.Sequence == _lastSequence)
                    return;
                _lastSequence = context.Sequence;
                Excel.Worksheet sheet = Excel2SheetTools.GetSheet(context);
                Excel.Application application = sheet.Application;
                application.Visible = ShowExcel;
                if (ShowExcel)
                {
                    sheet.Activate();
                    application.ScreenUpdating = true;
                }

                Message = ShowExcel ? "显示" : "隐藏";
                DA.SetData(0, context.Next(sheet));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "切换 Excel 显示状态失败: " + ex.Message);
            }
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "显示Excel", ToggleShowExcel, true, ShowExcel);
        }

        private void ToggleShowExcel(object sender, EventArgs e)
        {
            ShowExcel = !ShowExcel;
            ExpireSolution(true);
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2ShowHide");

        public override Guid ComponentGuid => new Guid("E21C940A-A9B0-4808-BA09-1C2019E0DE73");
    }
}
