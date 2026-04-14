using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class GH2ExcelTemplateV2 : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GH2ExcelTemplateV2 class.
        /// </summary>
        public GH2ExcelTemplateV2()
          : base("GH2ExcelTemplateV2", "GH2Excel",
              "套用模板填写Excel表格V2",
              "Parrot", "ExcelCAD")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("TemplatePath", "T", "模板路径", GH_ParamAccess.item);
            pManager.AddTextParameter("TemplateSheet", "TS", "模板Sheet（保留接口）", GH_ParamAccess.item);
            pManager.AddTextParameter("TargetPath", "P", "输出路径", GH_ParamAccess.item);
            pManager.AddTextParameter("TargetSheet", "S", "写入Sheet", GH_ParamAccess.item);
            pManager.AddTextParameter("StartCell", "C", "起始单元格（列有效）", GH_ParamAccess.item);
            pManager.AddTextParameter("Data", "D", "数据 A|B|C", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Overwrite", "O", "真为覆盖，假为追加", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Write", "W", "执行写入", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Result", "R", "结果", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string templatePath = "";
            string templateSheet = "";
            string targetPath = "";
            string targetSheet = "";
            string startCell = "";
            List<string> data = new List<string>();
            bool overwrite = true;
            bool write = false;

            if (!DA.GetData(0, ref templatePath)) return;
            if (!DA.GetData(1, ref templateSheet)) return;
            if (!DA.GetData(2, ref targetPath)) return;
            if (!DA.GetData(3, ref targetSheet)) return;
            if (!DA.GetData(4, ref startCell)) return;
            if (!DA.GetDataList(5, data)) return;
            DA.GetData(6, ref overwrite);
            DA.GetData(7, ref write);

            if (!write)
            {
                DA.SetData(0, "Write=false 未执行");
                return;
            }

            if (!File.Exists(templatePath))
            {
                DA.SetData(0, "模板不存在");
                return;
            }

            Excel.Application app = null;
            Excel.Workbook wb = null;
            Excel.Worksheet ws = null;

            try
            {
                // 1️⃣ 复制模板
                if (!File.Exists(targetPath))
                    File.Copy(templatePath, targetPath, true);

                // 2️⃣ 打开Excel
                app = new Excel.Application();
                app.Visible = false;
                app.DisplayAlerts = false;

                wb = app.Workbooks.Open(targetPath);
                ws = wb.Sheets[targetSheet] as Excel.Worksheet;

                if (ws == null)
                {
                    DA.SetData(0, "Sheet不存在");
                    return;
                }

                // 3️⃣ 获取列号（只用列）
                Excel.Range start = ws.Range[startCell];
                int startCol = start.Column;
                int startRow = start.Row; // 覆盖模式才用
                Marshal.ReleaseComObject(start);

                int writeRow = startRow;

                // 4️⃣ 追加模式（只看一列）
                if (!overwrite)
                {
                    int lastRow = 0;

                    Excel.Range colRange = ws.Columns[startCol];
                    int maxRow = ws.Rows.Count;

                    // 从底往上找（更快）
                    for (int r = maxRow; r >= 1; r--)
                    {
                        Excel.Range cell = ws.Cells[r, startCol] as Excel.Range;

                        if (cell != null && cell.Value2 != null)
                        {
                            string txt = cell.Value2.ToString().Trim();
                            if (!string.IsNullOrWhiteSpace(txt))
                            {
                                lastRow = r;
                                Marshal.ReleaseComObject(cell);
                                break;
                            }
                        }

                        if (cell != null) Marshal.ReleaseComObject(cell);
                    }

                    writeRow = lastRow + 1;
                }

                // 5️⃣ 解析数据 → 二维数组
                int rowCount = data.Count;
                int colCount = 0;

                List<string[]> parsed = new List<string[]>();

                foreach (var line in data)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split('|');
                    parsed.Add(parts);

                    if (parts.Length > colCount)
                        colCount = parts.Length;
                }

                rowCount = parsed.Count;

                object[,] arr = new object[rowCount, colCount];

                for (int i = 0; i < rowCount; i++)
                {
                    for (int j = 0; j < parsed[i].Length; j++)
                    {
                        string val = parsed[i][j];
                        if (!string.IsNullOrWhiteSpace(val))
                            arr[i, j] = val;
                    }
                }

                // 6️⃣ 一次性写入（核心提速点）
                Excel.Range writeRange = ws.Range[
                    ws.Cells[writeRow, startCol],
                    ws.Cells[writeRow + rowCount - 1, startCol + colCount - 1]
                ];

                writeRange.Value2 = arr;

                Marshal.ReleaseComObject(writeRange);

                // 7️⃣ 保存
                wb.Save();

                DA.SetData(0, $"完成：写入 {rowCount} 行，从第 {writeRow} 行");
            }
            catch (Exception ex)
            {
                DA.SetData(0, "错误: " + ex.Message);
            }
            finally
            {
                if (wb != null)
                {
                    wb.Close(true);
                    Marshal.ReleaseComObject(wb);
                }

                if (ws != null) Marshal.ReleaseComObject(ws);

                if (app != null)
                {
                    app.Quit();
                    Marshal.ReleaseComObject(app);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
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
            get { return new Guid("743BA8D3-7C85-4882-A268-C4B2F6D7F5CD"); }
        }
    }
}