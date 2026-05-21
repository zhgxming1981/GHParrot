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
          : base("MergeExcelRangeSheets", "\u5408\u5e76Excel\u533a\u57df",
              "\u5c06\u540c\u4e00Excel\u6587\u4ef6\u4e2d\u591a\u4e2a\u5de5\u4f5c\u8868\u7684\u6307\u5b9a\u533a\u57df\u4f9d\u6b21\u5408\u5e76\u5230\u65b0\u7684\u5de5\u4f5c\u8868",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("\u8bfb\u53d6Excel\u6587\u4ef6", "InFile", "\u8981\u8bfb\u53d6\u7684Excel\u6587\u4ef6\u8def\u5f84", GH_ParamAccess.item);
            pManager.AddTextParameter("\u6392\u9664\u5de5\u4f5c\u8868", "Ignore", "\u8981\u6392\u9664\u7684\u5de5\u4f5c\u8868\u540d\u79f0\uff0c\u53ef\u4ee5\u4e3a\u7a7a\u6216\u8f93\u5165\u591a\u4e2a", GH_ParamAccess.list);
            pManager.AddTextParameter("\u5408\u5e76\u533a\u57df", "Range", "\u6bcf\u4e2a\u5de5\u4f5c\u8868\u8981\u590d\u5236\u7684\u540c\u4e00\u533a\u57df\uff0c\u4f8b\u5982 A1:D10\u3001A1~D10\u30011~10", GH_ParamAccess.item);
            pManager.AddTextParameter("\u5199\u5165Excel\u6587\u4ef6", "OutFile", "\u8981\u5199\u5165\u7684Excel\u6587\u4ef6\u8def\u5f84\uff0c\u4e0d\u5b58\u5728\u65f6\u4f1a\u65b0\u5efa", GH_ParamAccess.item);
            pManager.AddTextParameter("\u5199\u5165\u5de5\u4f5c\u8868", "OutSheet", "\u8981\u5199\u5165\u7684\u5de5\u4f5c\u8868\u540d\u79f0\uff0c\u4e0d\u5b58\u5728\u65f6\u4f1a\u65b0\u5efa", GH_ParamAccess.item);
            pManager.AddTextParameter("\u5f00\u59cb\u5199\u5165\u4f4d\u7f6e", "Start", "\u5f00\u59cb\u5199\u5165\u7684\u5355\u5143\u683c\uff0c\u4f8b\u5982 A2\uff0c\u7528\u4e8e\u7ed9\u8868\u5934\u7559\u51fa\u4f4d\u7f6e", GH_ParamAccess.item, "A1");
            pManager.AddTextParameter("\u89c6\u540c\u7a7a\u503c\u6587\u672c", "EmptyText", "\u7b2c\u4e00\u5217\u547d\u4e2d\u8fd9\u4e9b\u6587\u672c\u65f6\u89c6\u540c\u4e3a\u7a7a\uff0c\u4f8b\u5982 -", GH_ParamAccess.list);
            pManager.AddBooleanParameter("\u5408\u5e76", "Merge", "\u4e3atrue\u65f6\u89e6\u53d1\u4e00\u6b21\u5408\u5e76\uff0c\u4e0e\u6309\u94ae\u529f\u80fd\u76f8\u540c", GH_ParamAccess.item, false);
            pManager[1].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("Done", "Done", "\u5408\u5e76\u5b8c\u6210\u540e\u8f93\u51fa\u4e00\u6b21true\u8109\u51b2", GH_ParamAccess.item);
            pManager.AddTextParameter("\u6d88\u606f", "M", "\u6267\u884c\u7ed3\u679c\u6d88\u606f", GH_ParamAccess.item);
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
                ResultMessage = "\u5408\u5e76\u5b8c\u6210\uff0c\u5171\u5199\u5165 " + count + " \u884c\u3002";
                PulseDone(expireNow);
            }
            catch (Exception ex)
            {
                DonePulse = false;
                ResultMessage = "\u5408\u5e76\u5931\u8d25: " + ex.Message;
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
            Menu_AppendItem(menu, "\u663e\u793a\u5199\u5165Excel", ShowExcelMenuClicked);
        }

        private void ShowExcelMenuClicked(object sender, EventArgs e)
        {
            try
            {
                ExcelPulseTools.ShowExcel(TargetPath, TargetSheet);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "\u663e\u793aExcel\u5931\u8d25: " + ex.Message);
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

        protected override Bitmap Icon => null;

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
                graphics.DrawString("\u5408\u5e76", font, Brushes.White, buttonRect, stringFormat);
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
