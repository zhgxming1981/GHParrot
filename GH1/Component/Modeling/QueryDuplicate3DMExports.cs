using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class QueryDuplicate3DMExports : GH_Component
    {
        private readonly List<DuplicateModelExportRecord> _cachedRecords = new List<DuplicateModelExportRecord>();
        private string _lastInputSignature = string.Empty;
        private string _status = "未查询。";

        public bool RunRequested { get; set; }
        public bool ExcelRequested { get; set; }
        public bool CanOpenExcel { get; private set; }

        public QueryDuplicate3DMExports()
          : base("筛选重复导出", "重复导出",
              "查询 .3dm 导出数据库中重复导出的模型，并可打开 Excel 查看结果",
              "Parrot", "Modeling")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("数据库地址", "数据库", "导出.3dm 使用的 SQLite 数据库文件地址。", GH_ParamAccess.item);
            pManager.AddTextParameter("源文件过滤", "源文件", "可选，只查询某个 Rhino 文件路径。为空时查询全部。", GH_ParamAccess.item);
            pManager.AddIntegerParameter("最少次数", "最少次数", "导出次数达到该数量才输出，最小为 2。", GH_ParamAccess.item, 2);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status", "查询状态。", GH_ParamAccess.item);
            pManager.AddTextParameter("源文件", "源文件", "重复导出的源 Rhino 文件路径。", GH_ParamAccess.list);
            pManager.AddTextParameter("模型编号", "模型编号", "重复导出的模型编号。", GH_ParamAccess.list);
            pManager.AddIntegerParameter("导出次数", "导出次数", "同源文件、同模型编号的导出次数。", GH_ParamAccess.list);
            pManager.AddTextParameter("首次导出时间", "首次时间", "首次导出时间。", GH_ParamAccess.list);
            pManager.AddTextParameter("最后导出时间", "最后时间", "最后导出时间。", GH_ParamAccess.list);
            pManager.AddTextParameter("最后导出路径", "最后路径", "最后一次导出的 .3dm 路径。", GH_ParamAccess.list);
            pManager.AddTextParameter("所有导出路径", "所有路径", "该模型所有导出路径，用分号合并。", GH_ParamAccess.list);
            pManager.AddTextParameter("记录明细", "明细", "适合接 Panel 的重复导出明细。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string databasePath = string.Empty;
            string sourceFilter = string.Empty;
            int minimumCount = 2;

            DA.GetData(0, ref databasePath);
            DA.GetData(1, ref sourceFilter);
            DA.GetData(2, ref minimumCount);

            databasePath = ModelExportRecordStore.NormalizePath(databasePath);
            sourceFilter = ModelExportRecordStore.NormalizePath(sourceFilter);
            minimumCount = Math.Max(2, minimumCount);

            string inputSignature = databasePath + "|" + sourceFilter + "|" + minimumCount;
            if (!string.Equals(_lastInputSignature, inputSignature, StringComparison.OrdinalIgnoreCase))
            {
                _lastInputSignature = inputSignature;
                _cachedRecords.Clear();
                CanOpenExcel = false;
                _status = "输入已变化，请点击 Run 查询。";
            }

            if (RunRequested)
            {
                RunRequested = false;
                Query(databasePath, sourceFilter, minimumCount);
            }

            if (ExcelRequested)
            {
                ExcelRequested = false;
                if (CanOpenExcel)
                    OpenExcel();
            }

            SetOutputs(DA);
        }

        public override void CreateAttributes()
        {
            Attributes = new QueryDuplicate3DMExportsAttributes(this);
        }

        private void Query(string databasePath, string sourceFilter, int minimumCount)
        {
            _cachedRecords.Clear();
            CanOpenExcel = false;

            try
            {
                if (string.IsNullOrWhiteSpace(databasePath))
                    throw new ArgumentException("数据库地址不能为空。");

                ModelExportRecordStore store = new ModelExportRecordStore(databasePath);
                _cachedRecords.AddRange(store.QueryDuplicates(sourceFilter, minimumCount));
                CanOpenExcel = true;
                _status = $"查询完成：重复导出记录 {_cachedRecords.Count} 条。";
            }
            catch (Exception ex)
            {
                _status = "查询失败：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
            }
        }

        private void SetOutputs(IGH_DataAccess DA)
        {
            List<string> sources = new List<string>();
            List<string> modelCodes = new List<string>();
            List<int> counts = new List<int>();
            List<string> firstTimes = new List<string>();
            List<string> lastTimes = new List<string>();
            List<string> lastPaths = new List<string>();
            List<string> allPaths = new List<string>();
            List<string> details = new List<string>();

            foreach (DuplicateModelExportRecord record in _cachedRecords)
            {
                sources.Add(record.SourceRhinoPath);
                modelCodes.Add(record.ModelCode);
                counts.Add(record.ExportCount);
                firstTimes.Add(record.FirstExportedAt);
                lastTimes.Add(record.LastExportedAt);
                lastPaths.Add(record.LastExportPath);
                allPaths.Add(record.AllExportPaths);
                details.Add(record.DetailText);
            }

            DA.SetData(0, _status);
            DA.SetDataList(1, sources);
            DA.SetDataList(2, modelCodes);
            DA.SetDataList(3, counts);
            DA.SetDataList(4, firstTimes);
            DA.SetDataList(5, lastTimes);
            DA.SetDataList(6, lastPaths);
            DA.SetDataList(7, allPaths);
            DA.SetDataList(8, details);
        }

        private void OpenExcel()
        {
            Excel.Application excel = null;
            Excel.Workbook workbook = null;
            Excel.Worksheet worksheet = null;

            try
            {
                excel = new Excel.Application();
                excel.Visible = true;
                workbook = excel.Workbooks.Add();
                worksheet = (Excel.Worksheet)workbook.Worksheets[1];
                worksheet.Name = "重复导出";

                string[] headers =
                {
                    "源文件",
                    "模型编号",
                    "导出次数",
                    "首次导出时间",
                    "最后导出时间",
                    "最后导出路径",
                    "所有导出路径"
                };

                for (int i = 0; i < headers.Length; i++)
                    worksheet.Cells[1, i + 1] = headers[i];

                if (_cachedRecords.Count == 0)
                {
                    worksheet.Cells[2, 1] = "没有重复导出记录";
                }
                else
                {
                    for (int i = 0; i < _cachedRecords.Count; i++)
                    {
                        DuplicateModelExportRecord record = _cachedRecords[i];
                        int row = i + 2;
                        worksheet.Cells[row, 1] = record.SourceRhinoPath;
                        worksheet.Cells[row, 2] = record.ModelCode;
                        worksheet.Cells[row, 3] = record.ExportCount;
                        worksheet.Cells[row, 4] = record.FirstExportedAt;
                        worksheet.Cells[row, 5] = record.LastExportedAt;
                        worksheet.Cells[row, 6] = record.LastExportPath;
                        worksheet.Cells[row, 7] = record.AllExportPaths;
                    }
                }

                Excel.Range headerRange = worksheet.Range["A1", "G1"];
                headerRange.Font.Bold = true;
                headerRange.Interior.Color = ColorTranslator.ToOle(Color.FromArgb(220, 230, 241));
                worksheet.Columns.AutoFit();

                string tempPath = Path.Combine(Path.GetTempPath(), "Parrot重复导出_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
                workbook.SaveAs(tempPath);
                excel.Visible = true;

                ReleaseComObject(headerRange);
            }
            catch (Exception ex)
            {
                _status = "打开 Excel 失败：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
            }
            finally
            {
                ReleaseComObject(worksheet);
                ReleaseComObject(workbook);
                ReleaseComObject(excel);
            }
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_GetFileNamesByExtension"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("A2322C92-B415-43BC-93C7-E6B89F0D041A"); }
        }
    }

    internal class QueryDuplicate3DMExportsAttributes : GH_ComponentAttributes
    {
        public QueryDuplicate3DMExportsAttributes(QueryDuplicate3DMExports owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 44.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            QueryDuplicate3DMExports component = (QueryDuplicate3DMExports)Owner;
            RectangleF runRect = GetRunButtonRect();
            RectangleF excelRect = GetExcelButtonRect();

            RenderButton(graphics, runRect, "Run", GH_Palette.Black, Brushes.White);
            RenderButton(graphics, excelRect, "Excel", component.CanOpenExcel ? GH_Palette.Black : GH_Palette.Grey, component.CanOpenExcel ? Brushes.White : Brushes.DimGray);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != MouseButtons.Left)
                return base.RespondToMouseDown(sender, e);

            QueryDuplicate3DMExports component = (QueryDuplicate3DMExports)Owner;
            if (GetRunButtonRect().Contains(e.CanvasLocation))
            {
                component.RunRequested = true;
                component.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            if (GetExcelButtonRect().Contains(e.CanvasLocation))
            {
                if (component.CanOpenExcel)
                {
                    component.ExcelRequested = true;
                    component.ExpireSolution(true);
                }
                return GH_ObjectResponse.Handled;
            }

            return base.RespondToMouseDown(sender, e);
        }

        private RectangleF GetRunButtonRect()
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 44, Bounds.Width, 21.0f);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }

        private RectangleF GetExcelButtonRect()
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 22, Bounds.Width, 21.0f);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }

        private void RenderButton(Graphics graphics, RectangleF buttonRect, string text, GH_Palette palette, Brush textBrush)
        {
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
            {
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            }

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(text, font, textBrush, buttonRect, format);
            }
        }
    }
}
