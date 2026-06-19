using System.Drawing;

namespace NS_Parrot
{
    internal static class GeneratedIcon
    {
        public static Bitmap Get(string name)
        {
            return parrot.Properties.Resources.ResourceManager.GetObject(name) as Bitmap;
        }

        public static Bitmap GetScrewLineArray()
        {
            Bitmap bitmap = CreateCanvas();
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen guidePen = new Pen(Color.FromArgb(45, 92, 175), 3.0f))
            using (Pen darkPen = new Pen(Color.FromArgb(42, 42, 42), 2.0f))
            using (SolidBrush boltBrush = new SolidBrush(Color.White))
            using (SolidBrush accentBrush = new SolidBrush(Color.FromArgb(45, 92, 175)))
            {
                ConfigureGraphics(graphics);
                graphics.DrawLine(guidePen, 5, 12, 19, 12);
                DrawArrow(graphics, guidePen, 18, 12, 21, 12);
                graphics.DrawLine(darkPen, 4, 18, 20, 18);
                for (int i = 0; i < 4; i++)
                {
                    float x = 5 + i * 5;
                    DrawBolt(graphics, boltBrush, darkPen, accentBrush, x, 18);
                }
            }

            return bitmap;
        }

        public static Bitmap GetScrewLineArcArray()
        {
            Bitmap bitmap = CreateCanvas();
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen guidePen = new Pen(Color.FromArgb(214, 116, 31), 3.0f))
            using (Pen darkPen = new Pen(Color.FromArgb(42, 42, 42), 2.0f))
            using (SolidBrush boltBrush = new SolidBrush(Color.White))
            using (SolidBrush accentBrush = new SolidBrush(Color.FromArgb(214, 116, 31)))
            {
                ConfigureGraphics(graphics);
                RectangleF arcRect = new RectangleF(4, 4, 18, 18);
                graphics.DrawArc(guidePen, arcRect, 200, 115);
                DrawArrow(graphics, guidePen, 17, 5, 19, 7);
                graphics.FillEllipse(accentBrush, 11, 16, 3, 3);
                graphics.DrawEllipse(darkPen, 11, 16, 3, 3);

                PointF center = new PointF(13, 17);
                float radius = 11.0f;
                float[] angles = { 215, 245, 275, 305 };
                foreach (float angle in angles)
                {
                    double radians = angle * System.Math.PI / 180.0;
                    float x = center.X + (float)System.Math.Cos(radians) * radius;
                    float y = center.Y + (float)System.Math.Sin(radians) * radius;
                    DrawBolt(graphics, boltBrush, darkPen, accentBrush, x, y);
                }
            }

            return bitmap;
        }

        public static Bitmap GetScrewHoleByVector()
        {
            Bitmap bitmap = CreateCanvas();
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen boardPen = new Pen(Color.FromArgb(62, 62, 62), 1.4f))
            using (Pen screwPen = new Pen(Color.FromArgb(38, 38, 38), 2.0f))
            using (Pen cutterPen = new Pen(Color.FromArgb(45, 130, 255), 2.0f))
            using (SolidBrush boardBrush = new SolidBrush(Color.FromArgb(232, 238, 244)))
            using (SolidBrush headBrush = new SolidBrush(Color.FromArgb(245, 145, 40)))
            using (SolidBrush holeBrush = new SolidBrush(Color.White))
            {
                ConfigureGraphics(graphics);
                graphics.FillRectangle(boardBrush, 5, 6, 14, 12);
                graphics.DrawRectangle(boardPen, 5, 6, 14, 12);
                graphics.DrawLine(screwPen, 12, 2, 12, 22);
                DrawArrow(graphics, screwPen, 12, 18, 12, 22);
                graphics.DrawEllipse(cutterPen, 8, 9, 8, 6);
                graphics.FillEllipse(holeBrush, 10, 10, 4, 4);
                graphics.FillPolygon(headBrush, new[] { new PointF(8, 3), new PointF(16, 3), new PointF(14, 6), new PointF(10, 6) });
            }

            return bitmap;
        }

        public static Bitmap GetScrewHoleByLineInfo()
        {
            Bitmap bitmap = GetScrewHoleByVector();
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen outlinePen = new Pen(Color.FromArgb(42, 42, 42), 1.0f))
            using (SolidBrush tapBrush = new SolidBrush(Color.FromArgb(45, 130, 255)))
            using (SolidBrush clearanceBrush = new SolidBrush(Color.FromArgb(30, 190, 95)))
            using (SolidBrush processBrush = new SolidBrush(Color.FromArgb(245, 145, 40)))
            {
                ConfigureGraphics(graphics);
                DrawInfoDot(graphics, tapBrush, outlinePen, 19, 6);
                DrawInfoDot(graphics, clearanceBrush, outlinePen, 19, 12);
                DrawInfoDot(graphics, processBrush, outlinePen, 19, 18);
            }

            return bitmap;
        }

        public static Bitmap GetSectionMake2DV2()
        {
            Bitmap bitmap = CreateCanvas();
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen outlinePen = new Pen(Color.FromArgb(42, 42, 42), 1.6f))
            using (Pen sectionPen = new Pen(Color.FromArgb(230, 92, 36), 2.4f))
            using (Pen drawingPen = new Pen(Color.FromArgb(32, 145, 205), 2.0f))
            using (SolidBrush planeBrush = new SolidBrush(Color.FromArgb(72, 230, 92, 36)))
            {
                ConfigureGraphics(graphics);

                PointF[] solid =
                {
                    new PointF(4, 7), new PointF(11, 3), new PointF(19, 6),
                    new PointF(19, 14), new PointF(12, 18), new PointF(4, 15)
                };
                graphics.DrawPolygon(outlinePen, solid);
                graphics.DrawLine(outlinePen, 4, 7, 12, 10);
                graphics.DrawLine(outlinePen, 12, 10, 19, 6);
                graphics.DrawLine(outlinePen, 12, 10, 12, 18);

                PointF[] cuttingPlane =
                {
                    new PointF(8, 3), new PointF(14, 5), new PointF(14, 19), new PointF(8, 17)
                };
                graphics.FillPolygon(planeBrush, cuttingPlane);
                graphics.DrawLine(sectionPen, 9, 3, 9, 19);
                DrawArrow(graphics, sectionPen, 9, 18, 5, 21);

                graphics.DrawLine(drawingPen, 14, 18, 21, 18);
                graphics.DrawLine(drawingPen, 14, 21, 21, 21);
                graphics.DrawLine(drawingPen, 18, 15, 21, 18);
            }

            return bitmap;
        }

        private static Bitmap CreateCanvas()
        {
            Bitmap bitmap = new Bitmap(24, 24);
            bitmap.SetResolution(96, 96);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.Transparent);
            return bitmap;
        }

        private static void ConfigureGraphics(Graphics graphics)
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        }

        private static void DrawBolt(Graphics graphics, Brush fill, Pen outline, Brush accent, float x, float y)
        {
            graphics.FillEllipse(fill, x - 2.2f, y - 2.2f, 4.4f, 4.4f);
            graphics.DrawEllipse(outline, x - 2.2f, y - 2.2f, 4.4f, 4.4f);
            graphics.FillEllipse(accent, x - 0.8f, y - 0.8f, 1.6f, 1.6f);
        }

        private static void DrawInfoDot(Graphics graphics, Brush fill, Pen outline, float x, float y)
        {
            graphics.FillEllipse(fill, x - 2.0f, y - 2.0f, 4.0f, 4.0f);
            graphics.DrawEllipse(outline, x - 2.0f, y - 2.0f, 4.0f, 4.0f);
        }

        private static void DrawArrow(Graphics graphics, Pen pen, float x1, float y1, float x2, float y2)
        {
            graphics.DrawLine(pen, x1, y1, x2, y2);
            graphics.DrawLine(pen, x2, y2, x2 - 2, y2 - 2);
            graphics.DrawLine(pen, x2, y2, x2 - 2, y2 + 2);
        }
    }
}
