using Excel = Microsoft.Office.Interop.Excel;
using Grasshopper.Kernel;
using System;
using System.Runtime.InteropServices;

namespace NS_Parrot
{
    public class SetCurrentExcelSheet : GH_Component
    {
        public SetCurrentExcelSheet()
          : base("设置当前Excel工作表", "当前Sheet",
              "将指定 Excel 工作簿中的 Sheet 设为当前工作表，并输出该工作表引用",
              "Parrot", "Excel2")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Excel工作簿引用", "Workbook", "打开 Excel 电池输出的工作簿引用", GH_ParamAccess.item);
            pManager.AddTextParameter("工作表名称", "Sheet", "要设为当前工作表的 Sheet 名称", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("当前Sheet引用", "Sheet", "已设置的 Excel 工作表引用", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object excelReference = null;
            string sheetName = string.Empty;
            DA.GetData(0, ref excelReference);
            DA.GetData(1, ref sheetName);

            try
            {
                Excel.Workbook workbook = Excel2SheetTools.Unwrap(excelReference) as Excel.Workbook;
                if (workbook == null)
                    throw new ArgumentException("Excel 工作簿引用无效，请连接‘打开Excel’电池的输出。");
                if (string.IsNullOrWhiteSpace(sheetName))
                    throw new ArgumentException("工作表名称为空。");
                Excel.Worksheet worksheet = GetWorksheet(workbook, sheetName);
                worksheet.Activate();
                Message = worksheet.Name;
                DA.SetData(0, worksheet);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "设置当前工作表失败: " + ex.Message);
            }
        }

        private static Excel.Worksheet GetWorksheet(Excel.Workbook workbook, string sheetName)
        {
            foreach (Excel.Worksheet worksheet in workbook.Worksheets)
            {
                if (string.Equals(worksheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return worksheet;

                try { Marshal.ReleaseComObject(worksheet); } catch { }
            }

            throw new ArgumentException("找不到工作表: " + sheetName);
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Excel2SetSheet");

        public override Guid ComponentGuid => new Guid("B1B79239-0EC3-4AD2-8400-505B7A1265EE");
    }
}
