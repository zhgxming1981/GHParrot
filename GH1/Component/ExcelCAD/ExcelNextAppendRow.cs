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
    public class ExcelNextAppendRow : GH_Component
    {
        public enum ButtonColor { Black, Grey }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;

        public string FilePath { get; private set; } = string.Empty;
        public string SheetName { get; private set; } = string.Empty;
        public List<string> Columns { get; } = new List<string>();
        public int StartRow { get; private set; } = 1;
        public int Row { get; private set; } = 1;
        public bool DonePulse { get; set; } = false;
        public string ResultMessage { get; set; } = string.Empty;

        public ExcelNextAppendRow()
          : base("ExcelNextAppendRow", "Excel追加行",
              "点击按钮获取指定列最后一行的下一行行号，并输出一次Done信号",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Excel文件路径", "Path", "Excel文件路径", GH_ParamAccess.item);
            pManager.AddTextParameter("SheetName", "Sheet", "工作表名称", GH_ParamAccess.item);
            pManager.AddTextParameter("列号", "Col", "列号，支持 A~F,H,I,M~X 或 1~5,7,8~12", GH_ParamAccess.list);
            pManager.AddIntegerParameter("起始行", "StartRow", "从第几行开始查找第一个空行，默认1", GH_ParamAccess.item, 1);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("行号", "Row", "指定列最后一行的下一行行号", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Done", "Done", "查询完成后输出一次true脉冲", GH_ParamAccess.item);
            pManager.AddTextParameter("消息", "M", "执行结果消息", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = FilePath;
            string sheetName = SheetName;
            List<string> columns = new List<string>();
            int startRow = StartRow;

            DA.GetData(0, ref filePath);
            DA.GetData(1, ref sheetName);
            DA.GetDataList(2, columns);
            DA.GetData(3, ref startRow);

            FilePath = filePath;
            SheetName = sheetName;
            Columns.Clear();
            if (columns.Count == 0)
            {
                columns.Add("1");
            }
            Columns.AddRange(columns);
            StartRow = Math.Max(1, startRow);

            DA.SetData(0, Row);
            DA.SetData(1, DonePulse);
            DA.SetData(2, ResultMessage);
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_ExcelNextAppendRow(this);
        }

        protected override Bitmap Icon => GeneratedIcon.Get("gen_ExcelNextAppendRow");

        public override Guid ComponentGuid => new Guid("8C3B654A-9CB2-4F04-BA98-D64E225A5F09");

        public void SetRow(int row)
        {
            Row = row;
        }

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
    }

    internal class CButton_ExcelNextAppendRow : GH_ComponentAttributes
    {
        public CButton_ExcelNextAppendRow(ExcelNextAppendRow component) : base(component) { }

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
                GH_Palette palette = ((ExcelNextAppendRow)Owner).CurrentButtonColor == ExcelNextAppendRow.ButtonColor.Black
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
                graphics.DrawString("查行", font, Brushes.White, buttonRect, stringFormat);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                ExcelNextAppendRow info = (ExcelNextAppendRow)Owner;
                info.CurrentButtonColor = ExcelNextAppendRow.ButtonColor.Grey;
                info.ExpireSolution(true);
                Thread.Sleep(50);
                info.CurrentButtonColor = ExcelNextAppendRow.ButtonColor.Black;

                try
                {
                    int row = ExcelPulseTools.GetNextAppendRow(info.FilePath, info.SheetName, info.Columns, info.StartRow);
                    info.SetRow(row);
                    info.ResultMessage = "查询完成";
                    info.PulseDone();
                }
                catch (Exception ex)
                {
                    info.DonePulse = false;
                    info.ResultMessage = "查询失败: " + ex.Message;
                    info.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, info.ResultMessage);
                    info.ExpireSolution(true);
                }

                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
