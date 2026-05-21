using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
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

        public enum ButtonColor { Black, Grey }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;

        // ===== 触发控制 =====
        private bool _triggerRun = false;
        private bool _lastWriteInput = false;
        private bool _donePulse = false;

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
            p.AddBooleanParameter("显示", "显示", "写入完成后是否显示Excel，默认true", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Log", "L", "日志", GH_ParamAccess.item);
            p.AddBooleanParameter("Done", "Done", "写入完成后输出一次true脉冲，可连接下一级写入电池", GH_ParamAccess.item);
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_GH2ExcelTemplateV2(this);
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
            TriggerWrite();
        }

        public void TriggerWrite()
        {
            _triggerRun = true;
            ExpireSolution(true);
        }

        private void PulseDone()
        {
            _donePulse = true;

            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 200;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                _donePulse = false;
                ExpireSolution(true);
            };
            timer.Start();
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
            bool showExcel = true;

            if (!DA.GetData(0, ref templatePath)) return;
            DA.GetData(1, ref templateSheet);
            if (!DA.GetData(2, ref targetPath)) return;
            if (!DA.GetData(3, ref targetSheet)) return;
            if (!DA.GetData(4, ref startCell)) return;
            if (!DA.GetDataList(5, data)) return;
            DA.GetData(6, ref overwrite);
            DA.GetData(7, ref writeInput);
            DA.GetData(8, ref showExcel);

            // ===== 统一触发 =====
            bool trigger = _triggerRun || (writeInput && !_lastWriteInput);
            _lastWriteInput = writeInput;

            if (!trigger)
            {
                DA.SetData(0, "未触发");
                DA.SetData(1, _donePulse);
                return;
            }

            // 直接重置
            _triggerRun = false;

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

            if (rowCount == 0 || colCount == 0)
            {
                DA.SetData(0, "没有可写入的数据");
                DA.SetData(1, false);
                return;
            }

            object[,] arr = new object[rowCount, colCount];

            for (int i = 0; i < rowCount; i++)
                for (int j = 0; j < parsed[i].Length; j++)
                    arr[i, j] = parsed[i][j];

            Excel.Application app = null;
            Excel.Workbook wb = null;
            Excel.Worksheet ws = null;
            bool appCreated = false;
            bool restoreExcelSettings = false;
            bool originalDisplayAlerts = true;
            bool originalScreenUpdating = true;

            try
            {
                string fullTargetPath = Path.GetFullPath(targetPath);

                // ===== 文件处理 =====
                if (!File.Exists(fullTargetPath))
                {
                    if (string.IsNullOrWhiteSpace(templatePath))
                    {
                        DA.SetData(0, "目标文件不存在且无模板");
                        DA.SetData(1, false);
                        return;
                    }
                    File.Copy(templatePath, fullTargetPath, true);
                }

                // ===== 获取Workbook =====
                if (!TryGetOpenWorkbook(fullTargetPath, out app, out wb))
                {
                    app = new Excel.Application();
                    appCreated = true;
                    app.Visible = false;
                    wb = app.Workbooks.Open(fullTargetPath);
                }

                originalDisplayAlerts = app.DisplayAlerts;
                originalScreenUpdating = app.ScreenUpdating;
                restoreExcelSettings = true;

                app.DisplayAlerts = false;
                app.ScreenUpdating = false;

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
                    var last = col.Cells[ws.Rows.Count].End[Excel.XlDirection.xlUp];

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

                // ===== 写入 =====
                if (overwrite)
                    ClearOutputRange(ws, writeRow, startCol, rowCount, colCount);

                WriteDataSafe(ws, writeRow, startCol, parsed, arr, false);

                wb.Save();

                if (showExcel)
                {
                    app.Visible = true;
                    app.ScreenUpdating = true;
                    ActivateWorkbook(wb);
                    ws.Activate();
                }
                else
                {
                    if (appCreated)
                        app.Visible = false;

                    app.ScreenUpdating = false;
                    System.Windows.Forms.MessageBox.Show("写入已经完成", "GH2Excel", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                }

                DA.SetData(0, $"完成：{rowCount}行 × {colCount}列");
                DA.SetData(1, true);
                PulseDone();
            }
            catch (Exception ex)
            {
                DA.SetData(0, "错误: " + ex.Message);
                DA.SetData(1, false);
            }
            finally
            {
                if (app != null && restoreExcelSettings)
                {
                    try
                    {
                        app.DisplayAlerts = originalDisplayAlerts;
                        app.ScreenUpdating = originalScreenUpdating;
                    }
                    catch
                    {
                    }
                }

                if (wb != null && appCreated && !showExcel)
                {
                    try
                    {
                        wb.Close(false);
                    }
                    catch
                    {
                    }
                }

                if (wb != null) Marshal.ReleaseComObject(wb);
                if (ws != null) Marshal.ReleaseComObject(ws);

                if (app != null && appCreated && !showExcel)
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

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out System.Runtime.InteropServices.ComTypes.IRunningObjectTable prot);

        private bool TryGetOpenWorkbook(string fullTargetPath, out Excel.Application app, out Excel.Workbook wb)
        {
            app = null;
            wb = null;

            System.Runtime.InteropServices.ComTypes.IRunningObjectTable rot = null;
            System.Runtime.InteropServices.ComTypes.IEnumMoniker enumMoniker = null;

            try
            {
                if (GetRunningObjectTable(0, out rot) != 0 || rot == null)
                    return false;

                rot.EnumRunning(out enumMoniker);
                if (enumMoniker == null)
                    return false;

                var monikers = new System.Runtime.InteropServices.ComTypes.IMoniker[1];
                IntPtr fetched = IntPtr.Zero;

                while (enumMoniker.Next(1, monikers, fetched) == 0)
                {
                    object obj = null;

                    try
                    {
                        rot.GetObject(monikers[0], out obj);

                        if (obj is Excel.Workbook openWorkbook && IsSameWorkbook(openWorkbook, fullTargetPath))
                        {
                            wb = openWorkbook;
                            app = openWorkbook.Application as Excel.Application;
                            return app != null;
                        }

                        if (obj is Excel.Application openApp && TryFindWorkbookInApp(openApp, fullTargetPath, out Excel.Workbook appWorkbook))
                        {
                            app = openApp;
                            wb = appWorkbook;
                            return true;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        if (obj != null && obj != wb && obj != app)
                            ReleaseCom(obj);

                        if (monikers[0] != null)
                            ReleaseCom(monikers[0]);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseCom(enumMoniker);
                ReleaseCom(rot);
            }

            return false;
        }

        private bool TryFindWorkbookInApp(Excel.Application app, string fullTargetPath, out Excel.Workbook wb)
        {
            wb = null;

            try
            {
                foreach (Excel.Workbook openWorkbook in app.Workbooks)
                {
                    if (IsSameWorkbook(openWorkbook, fullTargetPath))
                    {
                        wb = openWorkbook;
                        return true;
                    }

                    ReleaseCom(openWorkbook);
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsSameWorkbook(Excel.Workbook wb, string fullTargetPath)
        {
            try
            {
                return string.Equals(Path.GetFullPath(wb.FullName), fullTargetPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void ActivateWorkbook(Excel.Workbook wb)
        {
            try
            {
                wb.Activate();
                Excel.Window window = wb.Windows.Count > 0 ? wb.Windows[1] : null;
                if (window != null)
                {
                    window.Visible = true;
                    window.Activate();
                    ReleaseCom(window);
                }
            }
            catch
            {
            }
        }

        private void ClearOutputRange(Excel.Worksheet ws, int startRow, int startCol, int rowCount, int colCount)
        {
            int oldLastRow = GetLastNonEmptyRowInColumn(ws, startCol, startRow);
            int clearLastRow = Math.Max(startRow + rowCount - 1, oldLastRow);
            int clearLastCol = startCol + colCount - 1;

            Excel.Range startCell = null;
            Excel.Range endCell = null;
            Excel.Range range = null;

            try
            {
                startCell = ws.Cells[startRow, startCol] as Excel.Range;
                endCell = ws.Cells[clearLastRow, clearLastCol] as Excel.Range;
                range = ws.Range[startCell, endCell];
                range.ClearContents();
            }
            catch (COMException)
            {
                ClearRangeCellByCell(ws, startRow, startCol, clearLastRow, clearLastCol);
            }
            finally
            {
                ReleaseCom(range);
                ReleaseCom(endCell);
                ReleaseCom(startCell);
            }
        }

        private int GetLastNonEmptyRowInColumn(Excel.Worksheet ws, int col, int minRow)
        {
            Excel.Range column = null;
            Excel.Range last = null;

            try
            {
                column = ws.Columns[col] as Excel.Range;
                last = column.Cells[ws.Rows.Count].End[Excel.XlDirection.xlUp] as Excel.Range;

                if (last != null && last.Row >= minRow && last.Value2 != null)
                {
                    string value = last.Value2.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        return last.Row;
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseCom(last);
                ReleaseCom(column);
            }

            return minRow - 1;
        }

        private void ClearRangeCellByCell(Excel.Worksheet ws, int startRow, int startCol, int endRow, int endCol)
        {
            for (int r = startRow; r <= endRow; r++)
            {
                for (int c = startCol; c <= endCol; c++)
                {
                    Excel.Range cell = null;

                    try
                    {
                        cell = ws.Cells[r, c] as Excel.Range;

                        if (cell != null && Convert.ToBoolean(cell.MergeCells))
                        {
                            Excel.Range mergeArea = null;
                            Excel.Range target = null;

                            try
                            {
                                mergeArea = cell.MergeArea;
                                target = mergeArea.Cells[1, 1] as Excel.Range;
                                target.Value2 = null;
                            }
                            finally
                            {
                                ReleaseCom(target);
                                ReleaseCom(mergeArea);
                            }
                        }
                        else if (cell != null)
                        {
                            cell.Value2 = null;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        ReleaseCom(cell);
                    }
                }
            }
        }

        private bool SheetExists(Excel.Workbook wb, string name)
        {
            foreach (Excel.Worksheet s in wb.Sheets)
                if (s.Name == name) return true;
            return false;
        }

        private void WriteDataSafe(Excel.Worksheet ws, int startRow, int startCol, List<string[]> parsed, object[,] arr, bool overwrite)
        {
            Excel.Range startCell = null;
            Excel.Range endCell = null;
            Excel.Range range = null;

            try
            {
                startCell = ws.Cells[startRow, startCol] as Excel.Range;
                endCell = ws.Cells[startRow + parsed.Count - 1, startCol + arr.GetLength(1) - 1] as Excel.Range;
                range = ws.Range[startCell, endCell];

                try
                {
                    if (overwrite)
                        range.ClearContents();

                    range.Value2 = arr;
                    return;
                }
                catch (COMException)
                {
                    // Excel 2016 is stricter around partially selected merged cells.
                    WriteDataCellByCell(ws, startRow, startCol, parsed, overwrite);
                }
            }
            finally
            {
                ReleaseCom(range);
                ReleaseCom(endCell);
                ReleaseCom(startCell);
            }
        }

        private void WriteDataCellByCell(Excel.Worksheet ws, int startRow, int startCol, List<string[]> parsed, bool overwrite)
        {
            for (int i = 0; i < parsed.Count; i++)
            {
                int colCursor = startCol;

                for (int j = 0; j < parsed[i].Length; j++)
                {
                    Excel.Range cell = null;
                    Excel.Range mergeArea = null;
                    Excel.Range target = null;

                    try
                    {
                        cell = ws.Cells[startRow + i, colCursor] as Excel.Range;

                        if (cell != null && Convert.ToBoolean(cell.MergeCells))
                        {
                            mergeArea = cell.MergeArea;
                            target = mergeArea.Cells[1, 1] as Excel.Range;

                            if (overwrite)
                                target.Value2 = null;

                            target.Value2 = parsed[i][j];
                            colCursor += mergeArea.Columns.Count;
                        }
                        else
                        {
                            if (overwrite)
                                cell.Value2 = null;

                            cell.Value2 = parsed[i][j];
                            colCursor++;
                        }
                    }
                    finally
                    {
                        ReleaseCom(target);
                        ReleaseCom(mergeArea);
                        ReleaseCom(cell);
                    }
                }
            }
        }

        private void ReleaseCom(object obj)
        {
            if (obj == null)
                return;

            try
            {
                if (Marshal.IsComObject(obj))
                    Marshal.ReleaseComObject(obj);
            }
            catch
            {
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
                return GeneratedIcon.Get("gen_GH2ExcelTemplateV2");
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

    internal class CButton_GH2ExcelTemplateV2 : GH_ComponentAttributes
    {
        public CButton_GH2ExcelTemplateV2(GH2ExcelTemplateV2 component) : base(component) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 20.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);

            if (channel == GH_CanvasChannel.Objects)
            {
                GH_Palette palette = ((GH2ExcelTemplateV2)Owner).CurrentButtonColor == GH2ExcelTemplateV2.ButtonColor.Black
                    ? GH_Palette.Black
                    : GH_Palette.Grey;

                using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                {
                    capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                }
            }

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat stringFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("写入", font, Brushes.White, buttonRect, stringFormat);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                GH2ExcelTemplateV2 info = (GH2ExcelTemplateV2)Owner;
                info.CurrentButtonColor = GH2ExcelTemplateV2.ButtonColor.Grey;
                info.ExpireSolution(true);
                Thread.Sleep(50);
                info.CurrentButtonColor = GH2ExcelTemplateV2.ButtonColor.Black;

                info.TriggerWrite();
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
