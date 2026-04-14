using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using Excel = Microsoft.Office.Interop.Excel;
using System.IO;
namespace NS_Parrot
{
    public class GH2ExcelMData : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GH2ExcelMData class.
        /// </summary>
        public GH2ExcelMData()
          : base("GH2ExcelMData", "GH2ExcelMData",
              "可以一次指定多个数据起点，每个数据分别写在相应起点处",
              "Parrot", "ExcelCAD")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("FilePath", "FP", "Excel路径", GH_ParamAccess.item);
            pManager.AddTextParameter("SheetName", "SN", "Sheet名称", GH_ParamAccess.item);
            pManager.AddTextParameter("StartCells", "SC", "多个起始单元格", GH_ParamAccess.list);
            pManager.AddTextParameter("DataList", "DL", "数据列表（A|B|C）", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Write", "W", "执行", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Log", "L", "日志", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = "";
            string sheetName = "";
            List<string> startCells = new List<string>();
            List<string> dataList = new List<string>();
            bool write = false;

            if (!DA.GetData(0, ref filePath)) return;
            if (!DA.GetData(1, ref sheetName)) return;
            if (!DA.GetDataList(2, startCells)) return;
            if (!DA.GetDataList(3, dataList)) return;
            if (!DA.GetData(4, ref write)) return;

            if (!write)
            {
                DA.SetData(0, "Write = false");
                return;
            }

            if (startCells.Count != dataList.Count)
            {
                DA.SetData(0, "StartCells 和 DataList 数量不一致");
                return;
            }

            Excel.Application app = new Excel.Application();
            app.DisplayAlerts = false;

            try
            {
                // ===== 打开或创建文件 =====
                Excel.Workbook wb;

                if (File.Exists(filePath))
                {
                    wb = app.Workbooks.Open(filePath);
                }
                else
                {
                    wb = app.Workbooks.Add();
                    wb.SaveAs(filePath);
                }

                // ===== 获取或创建 Sheet =====
                Excel.Worksheet ws = null;

                foreach (Excel.Worksheet s in wb.Worksheets)
                {
                    if (s.Name == sheetName)
                    {
                        ws = s;
                        break;
                    }
                }

                if (ws == null)
                {
                    ws = wb.Worksheets.Add();
                    ws.Name = sheetName;
                }

                // ===== 多块写入 =====
                for (int i = 0; i < startCells.Count; i++)
                {
                    string startCell = startCells[i];
                    string line = dataList[i];

                    ParseCell(startCell, out int row, out int col);

                    var values = line.Split('|');

                    int colCursor = col;

                    foreach (var val in values)
                    {
                        Excel.Range cell = (Excel.Range)ws.Cells[row, colCursor];

                        if (cell.MergeCells)
                        {
                            Excel.Range area = cell.MergeArea;
                            area.Cells[1, 1].Value = val;

                            colCursor += area.Columns.Count;
                        }
                        else
                        {
                            cell.Value = val;
                            colCursor += 1;
                        }
                    }
                }

                wb.Save();
                wb.Close();
                app.Quit();

                DA.SetData(0, "写入完成 ✅（多点写入）");
            }
            catch (Exception ex)
            {
                app.Quit();
                DA.SetData(0, ex.Message);
            }
        }

        // ===== 单元格解析 =====
        private void ParseCell(string cell, out int row, out int col)
        {
            int i = 0;
            col = 0;

            while (i < cell.Length && Char.IsLetter(cell[i]))
            {
                col = col * 26 + (Char.ToUpper(cell[i]) - 'A' + 1);
                i++;
            }

            row = int.Parse(cell.Substring(i));
        }
        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("47C790EC-5D6B-44D8-B2CE-9B1FF04A7040"); }
        }
    }
}