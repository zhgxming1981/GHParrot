using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class MergeExcelRangeSheets : GH_Component
    {
        public enum ButtonColor { Black, Grey }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;

        public string SourcePath { get; private set; } = string.Empty;
        public List<string> IgnoreSheets { get; private set; } = new List<string>();
        public string RangeText { get; private set; } = string.Empty;
        public string TargetPath { get; private set; } = string.Empty;
        public string TargetSheet { get; private set; } = string.Empty;
        public string TargetStartCell { get; private set; } = "A1";
        public List<string> EmptyTexts { get; private set; } = new List<string>();
        public bool MergeInput { get; private set; } = false;
        public bool DonePulse { get; set; } = false;
        public string ResultMessage { get; set; } = string.Empty;
        private bool _lastMergeInput = false;

        public MergeExcelRangeSheets()
          : base("MergeExcelRangeSheets", "合并Excel区域",
              "将同一Excel文件中多个工作表的指定区域依次合并到新的工作表",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("读取Excel文件", "InFile", "要读取的Excel文件路径", GH_ParamAccess.item);
            pManager.AddTextParameter("排除工作表", "Ignore", "要排除的工作表名称，可以为空或输入多个", GH_ParamAccess.list);
            pManager.AddTextParameter("合并区域", "Range", "每个工作表要复制的同一区域，例如 A1:D10、A1~D10、1~10", GH_ParamAccess.item);
            pManager.AddTextParameter("写入Excel文件", "OutFile", "要写入的Excel文件路径，不存在时会新建", GH_ParamAccess.item);
            pManager.AddTextParameter("写入工作表", "OutSheet", "要写入的工作表名称，不存在时会新建", GH_ParamAccess.item);
            pManager.AddTextParameter("开始写入位置", "Start", "开始写入的单元格，例如 A2，用于给表头留出位置", GH_ParamAccess.item, "A1");
            pManager.AddTextParameter("视同空值文本", "EmptyText", "第一列命中这些文本时视同为空，例如 -", GH_ParamAccess.list);
            pManager.AddBooleanParameter("合并", "Merge", "为true时触发一次合并，与按钮功能相同", GH_ParamAccess.item, false);
            pManager[1].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("Done", "Done", "合并完成后输出一次true脉冲", GH_ParamAccess.item);
            pManager.AddTextParameter("消息", "M", "执行结果消息", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string sourcePath = SourcePath;
            List<string> ignoreSheets = new List<string>();
            string rangeText = RangeText;
            string targetPath = TargetPath;
            string targetSheet = TargetSheet;
            string targetStartCell = TargetStartCell;
            List<string> emptyTexts = new List<string>();
            bool mergeInput = MergeInput;

            DA.GetData(0, ref sourcePath);
            DA.GetDataList(1, ignoreSheets);
            DA.GetData(2, ref rangeText);
            DA.GetData(3, ref targetPath);
            DA.GetData(4, ref targetSheet);
            DA.GetData(5, ref targetStartCell);
            DA.GetDataList(6, emptyTexts);
            DA.GetData(7, ref mergeInput);

            SourcePath = sourcePath;
            IgnoreSheets = ignoreSheets;
            RangeText = rangeText;
            TargetPath = targetPath;
            TargetSheet = targetSheet;
            TargetStartCell = targetStartCell;
            EmptyTexts = emptyTexts;
            MergeInput = mergeInput;

            if (mergeInput && !_lastMergeInput)
                ExecuteMerge(false);

            _lastMergeInput = mergeInput;

            DA.SetData(0, DonePulse);
            DA.SetData(1, ResultMessage);
        }

        public void ExecuteMerge(bool expireNow = true)
        {
            try
            {
                int count = ExcelPulseTools.MergeSameRangeFromSheets(
                    SourcePath,
                    IgnoreSheets,
                    RangeText,
                    TargetPath,
                    TargetSheet,
                    TargetStartCell,
                    EmptyTexts);

                ExcelPulseTools.ShowExcel(TargetPath, TargetSheet);
                ResultMessage = "合并完成，共写入 " + count + " 行。";
                PulseDone(expireNow);
            }
            catch (Exception ex)
            {
                DonePulse = false;
                ResultMessage = "合并失败: " + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ResultMessage);
                if (expireNow)
                    ExpireSolution(true);
            }
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_MergeExcelRangeSheets(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "显示写入Excel", ShowExcelMenuClicked);
        }

        private void ShowExcelMenuClicked(object sender, EventArgs e)
        {
            try
            {
                ExcelPulseTools.ShowExcel(TargetPath, TargetSheet);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "显示Excel失败: " + ex.Message);
            }
        }

        public void PulseDone(bool expireNow = true)
        {
            DonePulse = true;
            if (expireNow)
                ExpireSolution(true);

            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 200;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                DonePulse = false;
                ExpireSolution(true);
            };
            timer.Start();
        }

        protected override Bitmap Icon => GeneratedIcon.Get("gen_MergeExcelRangeSheets");

        public override Guid ComponentGuid => new Guid("B8B2CC0A-45F6-4C83-9F46-C6B7E379D3B1");
    }

    internal class CButton_MergeExcelRangeSheets : GH_ComponentAttributes
    {
        public CButton_MergeExcelRangeSheets(MergeExcelRangeSheets component) : base(component) { }

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
                GH_Palette palette = ((MergeExcelRangeSheets)Owner).CurrentButtonColor == MergeExcelRangeSheets.ButtonColor.Black
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
                graphics.DrawString("合并", font, Brushes.White, buttonRect, stringFormat);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                MergeExcelRangeSheets info = (MergeExcelRangeSheets)Owner;
                info.CurrentButtonColor = MergeExcelRangeSheets.ButtonColor.Grey;
                info.ExpireSolution(true);
                Thread.Sleep(50);
                info.CurrentButtonColor = MergeExcelRangeSheets.ButtonColor.Black;

                info.ExecuteMerge();

                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
