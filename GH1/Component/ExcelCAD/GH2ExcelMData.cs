using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;
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

        // ===== 触发控制 =====
        private bool _triggerRun = false;
        private static bool _lastWrite = false;


        // ===== 缓存（用于右键）=====
        private string _lastFilePath = "";
        private string _lastSheetName = "";

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


        // ===== 右键菜单 =====
        public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
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
            ShowExcelAndActivateSheet(_lastFilePath, _lastSheetName);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = "";
            string sheetName = "";
            List<string> startCells = new List<string>();
            List<string> dataList = new List<string>();
            bool writeInput = false;

            if (!DA.GetData(0, ref filePath)) return;
            if (!DA.GetData(1, ref sheetName)) return;
            if (!DA.GetDataList(2, startCells)) return;
            if (!DA.GetDataList(3, dataList)) return;
            DA.GetData(4, ref writeInput);

            _lastFilePath = filePath;
            _lastSheetName = sheetName;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                DA.SetData(0, "错误：FilePath 不能为空");
                return;
            }

            if (string.IsNullOrWhiteSpace(sheetName))
            {
                DA.SetData(0, "错误：SheetName 不能为空");
                return;
            }

            if (startCells.Count != dataList.Count)
            {
                DA.SetData(0, "错误：StartCells 和 DataList 数量不一致");
                return;
            }

            bool trigger = writeInput || _triggerRun;

            if (!trigger)
            {
                DA.SetData(0, "未触发");
                return;
            }

            if (writeInput && _lastWrite)
            {
                DA.SetData(0, "等待 Write 复位");
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
                // ===== 获取 Excel =====
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

                // ===== 获取 Workbook =====
                foreach (Excel.Workbook w in app.Workbooks)
                {
                    if (string.Equals(w.FullName, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        wb = w;
                        break;
                    }
                }

                if (wb == null)
                {
                    if (File.Exists(filePath))
                        wb = app.Workbooks.Open(filePath);
                    else
                    {
                        wb = app.Workbooks.Add();
                        wb.SaveAs(filePath);
                    }
                }

                // ===== Sheet =====
                ws = GetSheetSafe(wb, sheetName);
                if (ws == null)
                {
                    ws = wb.Worksheets.Add();
                    ws.Name = sheetName;
                }

                // ===== 写入 =====
                for (int i = 0; i < startCells.Count; i++)
                {
                    ParseCell(startCells[i], out int row, out int col);

                    var values = dataList[i].Split('|');
                    int colCursor = col;

                    foreach (var val in values)
                    {
                        Excel.Range cell = ws.Cells[row, colCursor];

                        if (cell.MergeCells)
                        {
                            Excel.Range area = cell.MergeArea;
                            area.Cells[1, 1].Value2 = val;
                            colCursor += area.Columns.Count;
                            Marshal.ReleaseComObject(area);
                        }
                        else
                        {
                            cell.Value2 = val;
                            colCursor++;
                        }

                        Marshal.ReleaseComObject(cell);
                    }
                }

                wb.Save();

                // ⭐ 先显示，再结束（避免空白）
                ShowExcelAndActivateSheet(filePath, sheetName);

                DA.SetData(0, "写入完成 ✅");
            }
            catch (Exception ex)
            {
                DA.SetData(0, "错误: " + ex.Message);
            }
            finally
            {
                // ❗ 不释放 wb / ws（关键修复点）

                if (app != null && appCreated)
                {
                    app.Quit();
                    Marshal.ReleaseComObject(app);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // ===== 显示Excel并定位Sheet =====
        private void ShowExcelAndActivateSheet(string filePath, string sheetName)
        {
            try
            {
                var app = (Excel.Application)Marshal.GetActiveObject("Excel.Application");

                app.Visible = true;
                app.WindowState = Excel.XlWindowState.xlMaximized;

                Excel.Workbook wb = null;

                foreach (Excel.Workbook w in app.Workbooks)
                {
                    if (string.Equals(w.FullName, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        wb = w;
                        break;
                    }
                }

                if (wb == null && File.Exists(filePath))
                {
                    wb = app.Workbooks.Open(filePath);
                }

                if (wb != null)
                {
                    wb.Activate();

                    Excel.Worksheet ws = null;

                    foreach (Excel.Worksheet s in wb.Sheets)
                    {
                        if (s.Name == sheetName)
                        {
                            ws = s;
                            break;
                        }
                    }

                    ws?.Activate();
                }

                app.ScreenUpdating = true;
                app.ActiveWindow?.Activate();
            }
            catch
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "显示Excel失败");
            }
        }

        // ===== 工具函数 =====
        private Excel.Worksheet GetSheetSafe(Excel.Workbook wb, string name)
        {
            foreach (Excel.Worksheet s in wb.Sheets)
            {
                if (s.Name == name)
                    return s;
            }
            return null;
        }

        private void ParseCell(string cell, out int row, out int col)
        {
            cell = cell.ToUpper();

            int i = 0;
            while (i < cell.Length && char.IsLetter(cell[i])) i++;

            string colPart = cell.Substring(0, i);
            string rowPart = cell.Substring(i);

            col = 0;
            foreach (char c in colPart)
                col = col * 26 + (c - 'A' + 1);

            row = int.Parse(rowPart);
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