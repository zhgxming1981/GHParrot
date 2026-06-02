using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class ClearExcelRange : GH_Component
    {
        public enum ButtonColor { Black, Grey }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;

        public string FilePath { get; private set; } = string.Empty;
        public string SheetName { get; private set; } = string.Empty;
        public string RangeText { get; private set; } = string.Empty;
        public bool ShowExcel { get; private set; } = true;
        public bool DonePulse { get; set; } = false;
        public string ResultMessage { get; set; } = string.Empty;
        private bool _lastRunInput = false;

        public ClearExcelRange()
          : base("ClearExcelRange", "清空Excel区域",
              "点击按钮清空Excel指定单元格范围的值，并输出一次Done信号",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Excel文件路径", "Path", "Excel文件路径", GH_ParamAccess.item);
            pManager.AddTextParameter("SheetName", "Sheet", "工作表名称", GH_ParamAccess.item);
            pManager.AddTextParameter("单元格范围", "Range", "需要清空的单元格范围，例如 A1:D20", GH_ParamAccess.item);
            pManager.AddBooleanParameter("显示", "显示", "清空完成后是否显示Excel，默认true", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("运行", "Run", "由False变为True时执行清空，可连接Button", GH_ParamAccess.item, false);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("Done", "Done", "清空完成后输出一次true脉冲", GH_ParamAccess.item);
            pManager.AddTextParameter("消息", "M", "执行结果消息", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = FilePath;
            string sheetName = SheetName;
            string rangeText = RangeText;
            bool showExcel = ShowExcel;
            bool runInput = false;

            DA.GetData(0, ref filePath);
            DA.GetData(1, ref sheetName);
            DA.GetData(2, ref rangeText);
            DA.GetData(3, ref showExcel);
            DA.GetData(4, ref runInput);

            FilePath = filePath;
            SheetName = sheetName;
            RangeText = rangeText;
            ShowExcel = showExcel;

            bool runTriggered = runInput && !_lastRunInput;
            _lastRunInput = runInput;
            if (runTriggered)
                ExecuteClear();

            DA.SetData(0, DonePulse);
            DA.SetData(1, ResultMessage);
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_ClearExcelRange(this);
        }

        protected override Bitmap Icon => GeneratedIcon.Get("gen_ClearExcelRange");

        public override Guid ComponentGuid => new Guid("D9F3B0D4-D3D0-4C9A-AE80-5BB7A725B5F1");

        public void PulseDone()
        {
            DonePulse = true;
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

        public void ExecuteClear()
        {
            try
            {
                ExcelPulseTools.ClearRange(FilePath, SheetName, RangeText);
                if (ShowExcel)
                {
                    ExcelPulseTools.ShowExcel(FilePath, SheetName);
                }
                else
                {
                    MessageBox.Show("写入已经完成", "ClearExcelRange", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                ResultMessage = "清空完成";
                PulseDone();
            }
            catch (Exception ex)
            {
                DonePulse = false;
                ResultMessage = "清空失败: " + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ResultMessage);
                ExpireSolution(true);
            }
        }
    }

    internal class CButton_ClearExcelRange : GH_ComponentAttributes
    {
        public CButton_ClearExcelRange(ClearExcelRange component) : base(component) { }

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
                GH_Palette palette = ((ClearExcelRange)Owner).CurrentButtonColor == ClearExcelRange.ButtonColor.Black
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
                graphics.DrawString("清空", font, Brushes.White, buttonRect, stringFormat);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                ClearExcelRange info = (ClearExcelRange)Owner;
                info.CurrentButtonColor = ClearExcelRange.ButtonColor.Grey;
                info.ExpireSolution(true);
                Thread.Sleep(50);
                info.CurrentButtonColor = ClearExcelRange.ButtonColor.Black;

                info.ExecuteClear();

                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
