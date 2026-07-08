using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
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
    public class WriteExcel : GH_Component
    {
        public WriteExcel()
          : base("写入Excel", "写入Excel",
              "将 GH 数据写入 Excel，可从指定位置写入，也可以自动找到末尾追加",
              "Parrot", "Excel")
        {
        }

        public enum ButtonColor { Black, Grey }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;

        private bool _triggerRun = false;
        private bool _lastWriteInput = false;
        private bool _donePulse = false;
        private string _lastFilePath = "";
        private string _lastSheetName = "";

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("路径", "路径", "Excel 文件完整路径，不存在时会新建", GH_ParamAccess.item);
            p.AddTextParameter("表", "表", "目标 Sheet 名称，不存在时会新建", GH_ParamAccess.item);
            p.AddTextParameter("位置", "位置", "起始位置，例如 A1", GH_ParamAccess.item);
            p.AddTextParameter("数据", "数据", "要写入的数据，每行格式如 A|B|C", GH_ParamAccess.list);
            p.AddBooleanParameter("覆盖", "覆盖", "True 覆盖起始位置后的旧内容；False 从该列末尾追加", GH_ParamAccess.item, true);
            p.AddBooleanParameter("写入", "写入", "从 False 变为 True 时执行写入", GH_ParamAccess.item, false);
            p.AddBooleanParameter("显示", "显示", "写入完成后是否显示 Excel，默认 True", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("日志", "日志", "写入日志", GH_ParamAccess.item);
            p.AddBooleanParameter("完成", "完成", "写入完成后输出一次 True 脉冲，可连接下一级写入电池", GH_ParamAccess.item);
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_WriteExcel(this);
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendItem(menu, "运行", OnRunClicked);
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
                ShowExcel(_lastFilePath, _lastSheetName);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "显示 Excel 失败: " + ex.Message);
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string targetPath = "";
            string targetSheet = "";
            string startCell = "";
            List<string> data = new List<string>();
            bool overwrite = true;
            bool writeInput = false;
            bool showExcel = true;

            if (!DA.GetData(0, ref targetPath)) return;
            if (!DA.GetData(1, ref targetSheet)) return;
            if (!DA.GetData(2, ref startCell)) return;
            if (!DA.GetDataList(3, data)) return;
            DA.GetData(4, ref overwrite);
            DA.GetData(5, ref writeInput);
            DA.GetData(6, ref showExcel);

            bool trigger = _triggerRun || (writeInput && !_lastWriteInput);
            _lastWriteInput = writeInput;

            if (!trigger)
            {
                DA.SetData(0, "未触发");
                DA.SetData(1, _donePulse);
                return;
            }

            _triggerRun = false;

            List<string[]> parsed = ParseRows(data, out int colCount);
            int rowCount = parsed.Count;

            if (rowCount == 0 || colCount == 0)
            {
                DA.SetData(0, "没有可写入的数据");
                DA.SetData(1, false);
                return;
            }

            object[,] arr = BuildArray(parsed, rowCount, colCount);

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
                string directory = Path.GetDirectoryName(fullTargetPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (!TryGetOpenWorkbook(fullTargetPath, out app, out wb))
                {
                    app = new Excel.Application();
                    appCreated = true;
                    app.Visible = false;

                    if (File.Exists(fullTargetPath))
                    {
                        wb = app.Workbooks.Open(fullTargetPath);
                    }
                    else
                    {
                        wb = app.Workbooks.Add();
                        wb.SaveAs(fullTargetPath);
                    }
                }

                originalDisplayAlerts = app.DisplayAlerts;
                originalScreenUpdating = app.ScreenUpdating;
                restoreExcelSettings = true;

                app.DisplayAlerts = false;
                app.ScreenUpdating = false;

                ws = GetOrCreateSheet(wb, targetSheet);

                var start = ws.Range[startCell];
                int startRow = start.Row;
                int startCol = start.Column;
                ReleaseCom(start);

                int writeRow = startRow;

                if (!overwrite)
                    writeRow = Math.Max(GetLastNonEmptyRowInColumn(ws, startCol, startRow) + 1, startRow);

                if (overwrite)
                    ClearOutputRange(ws, writeRow, startCol, rowCount, colCount);

                WriteDataSafe(ws, writeRow, startCol, parsed, arr, false);

                wb.Save();

                _lastFilePath = fullTargetPath;
                _lastSheetName = ws.Name;

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
                    MessageBox.Show("写入已经完成", "写入Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                DA.SetData(0, $"完成：{rowCount} 行 x {colCount} 列");
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

                ReleaseCom(ws);
                ReleaseCom(wb);

                if (app != null && appCreated && !showExcel)
                {
                    app.Quit();
                    ReleaseCom(app);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private static List<string[]> ParseRows(List<string> data, out int colCount)
        {
            List<string[]> parsed = new List<string[]>();
            colCount = 0;

            foreach (string line in data)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split('|');
                parsed.Add(parts);
                if (parts.Length > colCount)
                    colCount = parts.Length;
            }

            return parsed;
        }

        private static object[,] BuildArray(List<string[]> parsed, int rowCount, int colCount)
        {
            object[,] arr = new object[rowCount, colCount];

            for (int i = 0; i < rowCount; i++)
                for (int j = 0; j < parsed[i].Length; j++)
                    arr[i, j] = parsed[i][j];

            return arr;
        }

        private Excel.Worksheet GetOrCreateSheet(Excel.Workbook wb, string name)
        {
            foreach (Excel.Worksheet sheet in wb.Sheets)
            {
                if (sheet.Name == name)
                    return sheet;

                ReleaseCom(sheet);
            }

            Excel.Worksheet ws = wb.Sheets.Add(After: wb.Sheets[wb.Sheets.Count]) as Excel.Worksheet;
            ws.Name = name;
            return ws;
        }

        private void ShowExcel(string filePath, string sheetName)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidOperationException("还没有写入过 Excel。");

            Excel.Application app = null;
            Excel.Workbook wb = null;
            Excel.Worksheet ws = null;
            bool appCreated = false;

            try
            {
                string fullPath = Path.GetFullPath(filePath);

                if (!TryGetOpenWorkbook(fullPath, out app, out wb))
                {
                    app = new Excel.Application();
                    appCreated = true;
                    wb = app.Workbooks.Open(fullPath);
                }

                app.Visible = true;
                app.WindowState = Excel.XlWindowState.xlMaximized;
                ActivateWorkbook(wb);

                if (!string.IsNullOrWhiteSpace(sheetName))
                {
                    ws = GetSheetSafe(wb, sheetName);
                    ws?.Activate();
                }
            }
            finally
            {
                ReleaseCom(ws);

                if (appCreated)
                {
                    ReleaseCom(wb);
                    ReleaseCom(app);
                }
            }
        }

        private Excel.Worksheet GetSheetSafe(Excel.Workbook wb, string name)
        {
            foreach (Excel.Worksheet sheet in wb.Sheets)
            {
                if (sheet.Name == name)
                    return sheet;

                ReleaseCom(sheet);
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

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_Gh2Excel"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("C66537F5-E7A6-4289-AAF8-D8685226755F"); }
        }
    }

    internal class CButton_WriteExcel : GH_ComponentAttributes
    {
        public CButton_WriteExcel(WriteExcel component) : base(component) { }

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
                GH_Palette palette = ((WriteExcel)Owner).CurrentButtonColor == WriteExcel.ButtonColor.Black
                    ? GH_Palette.Black
                    : GH_Palette.Grey;

                using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                {
                    capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                }
            }

            using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold))
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
                WriteExcel component = (WriteExcel)Owner;
                component.CurrentButtonColor = WriteExcel.ButtonColor.Grey;
                component.ExpireSolution(true);
                Thread.Sleep(50);
                component.CurrentButtonColor = WriteExcel.ButtonColor.Black;

                component.TriggerWrite();
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
