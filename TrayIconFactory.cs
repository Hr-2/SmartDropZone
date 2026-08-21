using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SmartDropZone
{
    /// <summary>
    /// Draws the small tray glyph (a rounded slate tile with a drop arrow)
    /// at runtime, so no icon asset file is required.
    /// </summary>
    internal static class TrayIconFactory
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon Create()
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Rounded slate tile
                using var bgBrush = new SolidBrush(Color.FromArgb(255, 43, 48, 58));
                using var path = RoundedRect(new RectangleF(1, 1, 30, 30), 9);
                g.FillPath(bgBrush, path);

                // Accent "drop into shelf" arrow
                using var accent = new SolidBrush(Color.FromArgb(255, 76, 194, 255));
                g.FillPolygon(accent, new[]
                {
                    new PointF(16f, 6.5f),
                    new PointF(9.5f, 17f),
                    new PointF(22.5f, 17f)
                });
                g.FillRectangle(accent, 7.5f, 20f, 17f, 3.5f);
            }

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var temp = Icon.FromHandle(hIcon);
                return (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}