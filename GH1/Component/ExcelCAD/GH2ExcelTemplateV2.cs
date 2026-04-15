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
              "套用模板填写Excel表格增强版，可以从指定位置写入，也可以自动找到末尾追加",
              "Parrot", "ExcelCAD")
        {
        }

        // ===== 触发控制 =====
        private bool _triggerRun = false;
        private static bool _lastWrite = false;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("TemplatePath", "TP", "模板路径（可空）", GH_ParamAccess.item);
            p.AddTextParameter("TemplateSheet", "TS", "模板Sheet（可空）", GH_ParamAccess.item);
            p.AddTextParameter("TargetPath", "DP", "目标路径", GH_ParamAccess.item);
            p.AddTextParameter("TargetSheet", "DS", "目标Sheet", GH_ParamAccess.item);
            p.AddTextParameter("StartCell", "SC", "起始单元格", GH_ParamAccess.item);
            p.AddTextParameter("Data", "D", "数据 A|B|C", GH_ParamAccess.list);
            p.AddBooleanParameter("Overwrite", "O", "覆盖模式", GH_ParamAccess.item, true);
            p.AddBooleanParameter("Write", "W", "执行", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Log", "L", "日志", GH_ParamAccess.item);
        }

        // ===== 右键菜单 =====
        public  override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendItem(menu, "运行 Run", OnRunClicked);
            Menu_AppendItem(menu, "显示 Excel", OnShowExcelClicked);
        }

        private void OnRunClicked(object sender, EventArgs e)
        {
            _triggerRun = true;
            ExpireSolution(true);
        }

        private void OnShowExcelClicked(object sender, EventArgs e)
        {
            try
            {
                var app = (Excel.Application)Marshal.GetActiveObject("Excel.Application");
                app.Visible = true;
                app.WindowState = Excel.XlWindowState.xlMaximized;
                app.ActiveWorkbook?.Activate();
                app.ActiveWindow?.Activate();
            }
            catch
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "未找到Excel实例");
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string templatePath = "";
            string templateSheet = "";
            string targetPath = "";
            string targetSheet = "";
            string startCell = "";
            List<string> data = new List<string>();
            bool overwrite = true;
            bool writeInput = false;

            if (!DA.GetData(0, ref templatePath)) return;
            DA.GetData(1, ref templateSheet);
            if (!DA.GetData(2, ref targetPath)) return;
            if (!DA.GetData(3, ref targetSheet)) return;
            if (!DA.GetData(4, ref startCell)) return;
            if (!DA.GetDataList(5, data)) return;
            DA.GetData(6, ref overwrite);
            DA.GetData(7, ref writeInput);

            // ===== 统一触发 =====
            bool trigger = writeInput || _triggerRun;

            if (!trigger)
            {
                DA.SetData(0, "未触发");
                return;
            }

            if (writeInput && _lastWrite)
            {
                DA.SetData(0, "等待Write复位");
                return;
            }

            _lastWrite = writeInput;
            _triggerRun = false;

            Excel.Application app = null;
            Excel.Workbook wb = null;
            Excel.Worksheet ws = null;
            bool appCreated = false;

            try
            {
                // ===== 获取Excel实例 =====
                try
                {
                    app = (Excel.Application)Marshal.GetActiveObject("Excel.Application");
                }
                catch
                {
                    app = new Excel.Application();
                    appCreated = true;
                }

                app.Visible = false;
                app.DisplayAlerts = false;
                app.ScreenUpdating = false;

                // ===== 文件处理 =====
                if (!File.Exists(targetPath))
                {
                    if (string.IsNullOrWhiteSpace(templatePath))
                    {
                        DA.SetData(0, "目标文件不存在且无模板");
                        return;
                    }
                    File.Copy(templatePath, targetPath, true);
                }

                // ===== 获取Workbook =====
                foreach (Excel.Workbook w in app.Workbooks)
                {
                    if (string.Equals(w.FullName, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        wb = w;
                        break;
                    }
                }

                if (wb == null)
                    wb = app.Workbooks.Open(targetPath);

                // ===== 获取Sheet（安全方式）=====
                ws = GetSheetSafe(wb, targetSheet);

                // ===== Sheet不存在 → 创建或复制 =====
                if (ws == null)
                {
                    Excel.Worksheet tempWs = null;

                    bool sameFile = !string.IsNullOrWhiteSpace(templatePath) &&
                        string.Equals(Path.GetFullPath(templatePath),
                                      Path.GetFullPath(targetPath),
                                      StringComparison.OrdinalIgnoreCase);

                    // ===== 优先模板 =====
                    if (!string.IsNullOrWhiteSpace(templateSheet))
                    {
                        if (sameFile)
                        {
                            tempWs = GetSheetSafe(wb, templateSheet);
                            tempWs?.Copy(After: wb.Sheets[wb.Sheets.Count]);
                        }
                        else if (!string.IsNullOrWhiteSpace(templatePath))
                        {
                            var tempWb = app.Workbooks.Open(templatePath);
                            tempWs = tempWb.Sheets[templateSheet] as Excel.Worksheet;

                            tempWs.Copy(After: wb.Sheets[wb.Sheets.Count]);

                            tempWb.Close(false);
                            Marshal.ReleaseComObject(tempWb);
                        }

                        if (tempWs != null)
                            ws = wb.Sheets[wb.Sheets.Count];
                    }

                    // ===== 没模板 → 新建 =====
                    if (ws == null)
                    {
                        ws = wb.Sheets.Add(After: wb.Sheets[wb.Sheets.Count]);
                    }

                    // ===== 命名（防重名）=====
                    string name = targetSheet;
                    int i = 1;

                    while (SheetExists(wb, name))
                    {
                        name = targetSheet + "_" + i;
                        i++;
                    }

                    ws.Name = name;
                }

                // ===== 起始位置 =====
                var start = ws.Range[startCell];
                int startRow = start.Row;
                int startCol = start.Column;
                Marshal.ReleaseComObject(start);

                int writeRow = startRow;

                // ===== 追加模式 =====
                if (!overwrite)
                {
                    var col = ws.Columns[startCol];
                    var last = col.Cells[ws.Rows.Count].End(Excel.XlDirection.xlUp);

                    int lastRow = 0;

                    while (last != null)
                    {
                        if (last.Value2 != null)
                        {
                            string v = last.Value2.ToString().Trim();
                            if (!string.IsNullOrWhiteSpace(v))
                            {
                                lastRow = last.Row;
                                break;
                            }
                        }
                        last = last.Offset[-1, 0];
                    }

                    writeRow = Math.Max(lastRow + 1, startRow);

                    Marshal.ReleaseComObject(col);
                    Marshal.ReleaseComObject(last);
                }

                // ===== 数据处理 =====
                List<string[]> parsed = new List<string[]>();
                int colCount = 0;

                foreach (var line in data)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var p = line.Split('|');
                    parsed.Add(p);
                    if (p.Length > colCount) colCount = p.Length;
                }

                int rowCount = parsed.Count;
                object[,] arr = new object[rowCount, colCount];

                for (int i = 0; i < rowCount; i++)
                    for (int j = 0; j < parsed[i].Length; j++)
                        arr[i, j] = parsed[i][j];

                // ===== 写入 =====
                var range = ws.Range[
                    ws.Cells[writeRow, startCol],
                    ws.Cells[writeRow + rowCount - 1, startCol + colCount - 1]
                ];

                range.ClearContents();
                range.Value2 = arr;

                Marshal.ReleaseComObject(range);

                wb.Save();

                // ===== 显示Excel =====
                app.Visible = true;
                app.ScreenUpdating = true;
                app.ActiveWorkbook?.Activate();
                app.ActiveWindow?.Activate();

                DA.SetData(0, $"完成：{rowCount}行 × {colCount}列");
            }
            catch (Exception ex)
            {
                DA.SetData(0, "错误: " + ex.Message);
            }
            finally
            {
                if (wb != null) Marshal.ReleaseComObject(wb);
                if (ws != null) Marshal.ReleaseComObject(ws);

                if (app != null && appCreated)
                {
                    app.Quit();
                    Marshal.ReleaseComObject(app);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private Excel.Worksheet GetSheetSafe(Excel.Workbook wb, string name)
        {
            foreach (Excel.Worksheet s in wb.Sheets)
            {
                if (s.Name == name)
                    return s;
            }
            return null;
        }

        private bool SheetExists(Excel.Workbook wb, string name)
        {
            foreach (Excel.Worksheet s in wb.Sheets)
                if (s.Name == name) return true;
            return false;
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