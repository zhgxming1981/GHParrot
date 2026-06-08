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

        private static void DrawArrow(Graphics graphics, Pen pen, float x1, float y1, float x2, float y2)
        {
            graphics.DrawLine(pen, x1, y1, x2, y2);
            graphics.DrawLine(pen, x2, y2, x2 - 2, y2 - 2);
            graphics.DrawLine(pen, x2, y2, x2 - 2, y2 + 2);
        }
    }
}
