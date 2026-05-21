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
    public class ExcelInsertPicture : GH_Component
    {
        public enum ButtonColor { Black, Grey }
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;

        public string FilePath { get; private set; } = string.Empty;
        public string SheetName { get; private set; } = string.Empty;
        public List<string> PictureCells { get; } = new List<string>();
        public string PictureFolder { get; private set; } = string.Empty;
        public double HeightMinus { get; private set; } = 0.0;
        public List<string> InsertCells { get; } = new List<string>();
        public bool ShowExcel { get; private set; } = true;
        public bool DonePulse { get; set; } = false;
        public string ResultMessage { get; set; } = string.Empty;
        public int InsertedCount { get; private set; } = 0;

        public ExcelInsertPicture()
          : base("ExcelInsertPicture", "ExcelPic",
              "Read picture names from Pic cells and insert pictures into matching InsertCell cells. Pic and InsertCell must be on the same row.",
              "Parrot", "ExcelCAD")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Excel Path", "Path", "Excel file path", GH_ParamAccess.item);
            pManager.AddTextParameter("SheetName", "Sheet", "Worksheet name", GH_ParamAccess.item);
            pManager.AddTextParameter("Picture Cell", "Pic", "Cells containing picture names, such as D21, D22. Each must be on the same row as its InsertCell.", GH_ParamAccess.list);
            pManager.AddTextParameter("Picture Folder", "Folder", "Picture folder", GH_ParamAccess.item);
            pManager.AddNumberParameter("Height Minus", "H-", "Picture height = row height - this value", GH_ParamAccess.item, 0.0);
            pManager.AddTextParameter("Insert Cell", "Cell", "Cells to insert pictures into, such as F21, F22. One picture per cell.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Show", "Show", "Show Excel after inserting pictures. Default true", GH_ParamAccess.item, true);
            pManager[4].Optional = true;
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Count", "N", "Inserted picture count", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Done", "Done", "One true pulse after inserting pictures", GH_ParamAccess.item);
            pManager.AddTextParameter("Message", "M", "Result message", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = FilePath;
            string sheetName = SheetName;
            List<string> pictureCells = new List<string>();
            string pictureFolder = PictureFolder;
            double heightMinus = HeightMinus;
            List<string> insertCells = new List<string>();
            bool showExcel = ShowExcel;

            DA.GetData(0, ref filePath);
            DA.GetData(1, ref sheetName);
            DA.GetDataList(2, pictureCells);
            DA.GetData(3, ref pictureFolder);
            DA.GetData(4, ref heightMinus);
            DA.GetDataList(5, insertCells);
            DA.GetData(6, ref showExcel);

            FilePath = filePath;
            SheetName = sheetName;
            PictureCells.Clear();
            PictureCells.AddRange(pictureCells);
            PictureFolder = pictureFolder;
            HeightMinus = heightMinus;
            InsertCells.Clear();
            InsertCells.AddRange(insertCells);
            ShowExcel = showExcel;

            DA.SetData(0, InsertedCount);
            DA.SetData(1, DonePulse);
            DA.SetData(2, ResultMessage);
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_ExcelInsertPicture(this);
        }

        protected override Bitmap Icon => GeneratedIcon.Get("gen_ai_ExcelInsertPicture");

        public override Guid ComponentGuid => new Guid("57E40262-2D8E-442D-B84B-7E4D4C62B841");

        public void SetInsertedCount(int count)
        {
            InsertedCount = count;
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

    internal class CButton_ExcelInsertPicture : GH_ComponentAttributes
    {
        public CButton_ExcelInsertPicture(ExcelInsertPicture component) : base(component) { }

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
                GH_Palette palette = ((ExcelInsertPicture)Owner).CurrentButtonColor == ExcelInsertPicture.ButtonColor.Black
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
                graphics.DrawString("Pic", font, Brushes.White, buttonRect, stringFormat);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                ExcelInsertPicture info = (ExcelInsertPicture)Owner;
                info.CurrentButtonColor = ExcelInsertPicture.ButtonColor.Grey;
                info.ExpireSolution(true);
                Thread.Sleep(50);
                info.CurrentButtonColor = ExcelInsertPicture.ButtonColor.Black;

                try
                {
                    List<string> messages;
                    int inserted = ExcelPulseTools.InsertPictures(
                        info.FilePath,
                        info.SheetName,
                        info.PictureCells,
                        info.PictureFolder,
                        info.HeightMinus,
                        info.InsertCells,
                        out messages);

                    info.SetInsertedCount(inserted);
                    info.ResultMessage = string.Join(Environment.NewLine, messages);

                    if (info.ShowExcel)
                    {
                        ExcelPulseTools.ShowExcel(info.FilePath, info.SheetName);
                    }
                    else
                    {
                        MessageBox.Show("Insert pictures completed", "ExcelPic", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                    info.PulseDone();
                }
                catch (Exception ex)
                {
                    info.DonePulse = false;
                    info.ResultMessage = "Insert pictures failed: " + ex.Message;
                    info.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, info.ResultMessage);
                    info.ExpireSolution(true);
                }

                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
