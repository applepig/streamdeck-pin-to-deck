using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PinToDeck.Models;

namespace PinToDeck.Core
{
    public class IconProcessor
    {
        public Bitmap ToGrayscale(Bitmap original)
        {
            // Create a grayscale matrix
            ColorMatrix colorMatrix = new ColorMatrix(
                new float[][]
                {
                    new float[] {.3f, .3f, .3f, 0, 0},
                    new float[] {.59f, .59f, .59f, 0, 0},
                    new float[] {.11f, .11f, .11f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                });

            ImageAttributes attributes = new ImageAttributes();
            attributes.SetColorMatrix(colorMatrix);

            Bitmap grayscale = new Bitmap(original.Width, original.Height);
            using (Graphics g = Graphics.FromImage(grayscale))
            {
                g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                    0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }
            return grayscale;
        }

        public Bitmap AddBadge(Bitmap original, int count, BadgePosition position = BadgePosition.BottomRight)
        {
            // 只要有視窗就顯示 badge（包含 1）
            if (count <= 0) return new Bitmap(original);
            return AddBadge(original, count > 99 ? "99+" : count.ToString(), Color.Red, position);
        }

        public Bitmap AddBadge(Bitmap original, string text, Color badgeColor, BadgePosition position = BadgePosition.BottomRight)
        {
            Bitmap badged = new Bitmap(original);
            using (Graphics g = Graphics.FromImage(badged))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                int size = (int)(original.Width * 0.35);
                if (size < 20) size = 20;

                // Calculate proportional values based on 72px standard
                // If image is 256px, scale factor is ~3.55
                float scale = original.Width / 72f;

                int padding = (int)(4 * scale); // 4px at 72px base
                if (padding < 2) padding = 2;

                int gapThickness = (int)(2 * scale); // 2px at 72px base
                if (gapThickness < 2) gapThickness = 2; // Min 2px gap

                int textOffsetY = (int)(2 * scale);

                int x = 0;
                int y = 0;

                switch (position)
                {
                    case BadgePosition.TopRight:
                    default:
                        x = original.Width - size - padding;
                        y = padding;
                        break;
                    case BadgePosition.BottomRight:
                        x = original.Width - size - padding;
                        y = original.Height - size - padding;
                        break;
                    case BadgePosition.BottomLeft:
                        x = padding;
                        y = original.Height - size - padding;
                        break;
                    case BadgePosition.TopLeft:
                        x = padding;
                        y = padding;
                        break;
                }

                // Draw Cutout (Eraser) - size + gap * 2
                // We want a gap of 'gapThickness' around the circle
                int cutoutSize = size + (gapThickness * 2);
                int cutoutX = x - gapThickness;
                int cutoutY = y - gapThickness;

                var oldCompositingMode = g.CompositingMode;
                g.CompositingMode = CompositingMode.SourceCopy;
                using (Brush brush = new SolidBrush(Color.Transparent))
                {
                    g.FillEllipse(brush, cutoutX, cutoutY, cutoutSize, cutoutSize);
                }
                g.CompositingMode = oldCompositingMode;

                // Draw Circle
                using (Brush brush = new SolidBrush(badgeColor))
                {
                    g.FillEllipse(brush, x, y, size, size);
                }

                // Draw Text
                float fontSizeFactor = text.Length > 1 ? (text.Length > 2 ? 0.35f : 0.45f) : 0.6f;
                using (Font font = new Font("Arial", size * fontSizeFactor, FontStyle.Bold))
                using (Brush textBrush = new SolidBrush(Color.White))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;

                    g.DrawString(text, font, textBrush, new Rectangle(x, y + textOffsetY, size, size), sf);
                }
            }
            return badged;
        }

        public Bitmap AddForegroundBorder(Bitmap original)
        {
            Bitmap bordered = new Bitmap(original);
            using (Graphics g = Graphics.FromImage(bordered))
            {
                int borderSize = 4;
                using (Pen pen = new Pen(Color.LimeGreen, borderSize))
                {
                    pen.Alignment = PenAlignment.Inset;
                    g.DrawRectangle(pen, 0, 0, bordered.Width, bordered.Height);
                }
            }
            return bordered;
        }

        public Bitmap Process(Bitmap original, AppState state, int windowCount, BadgePosition position = BadgePosition.BottomRight, int targetSize = 72)
        {
            if (original == null) return null;

            // If BadgePosition is None, skip all badge processing, just resize
            if (position == BadgePosition.None)
            {
                if (original.Width == targetSize && original.Height == targetSize)
                {
                    return new Bitmap(original);
                }
                Bitmap resized = new Bitmap(targetSize, targetSize);
                using (Graphics g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(original, 0, 0, targetSize, targetSize);
                }
                return resized;
            }

            // Target size is usually 72 (standard), 96 (XL), etc.
            // We should aim for @2x for high DPI rendering (Stream Deck best practice)
            // So resizing to targetSize * 2 is ideal.
            // However, the caller might simply pass the pixel size they want (e.g. 144)
            // Let's assume targetSize is the DESIRED PIXEL WIDTH.

            Bitmap workingImage = original;

            // Only resize if original is significantly larger than target (to save perf)
            // Or if we want to enforce consistency
            if (original.Width != targetSize || original.Height != targetSize)
            {
                workingImage = new Bitmap(targetSize, targetSize);
                using (Graphics g = Graphics.FromImage(workingImage))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(original, 0, 0, targetSize, targetSize);
                }
            }

            Bitmap result;
            if (state == AppState.NotRunning)
            {
                // Not Running: Original color + Grey Badge "0"
                result = AddBadge(workingImage, "0", Color.Gray, position);
                // If we resized, workingImage is a new bitmap, so we don't dispose logical original, but the temp one
                if (workingImage != original) workingImage.Dispose();
            }
            else
            {
                // Running (Background or Foreground)
                // Always start with clone of working image to avoid modifying source if it's the original
                // Or to use as base for badge
                Bitmap tempBase = new Bitmap(workingImage); // Clone for modification

                if (state == AppState.Foreground)
                {
                    // Foreground: Always show GREEN Badge
                    string text = windowCount > 99 ? "99+" : windowCount.ToString();
                    result = AddBadge(tempBase, text, Color.FromArgb(0, 180, 0), position); // Darker Green
                    tempBase.Dispose();
                }
                else
                {
                    // Background
                    if (windowCount > 0)
                    {
                        result = AddBadge(tempBase, windowCount, position);
                        tempBase.Dispose();
                    }
                    else
                    {
                        // No badge needed, just return the resized/original image (cloned to return new instance)
                        result = tempBase;
                    }
                }

                if (workingImage != original) workingImage.Dispose();
            }

            return result;
        }
    }
}
