//  Copyright (C) 2016, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Duplicati.Server.WebServer
{
    /// <summary>
    /// Helper class for creating Captcha images
    /// </summary>
    public static class CaptchaUtil
    {
        /// <summary>
        /// A lookup string with characters to use
        /// </summary>
        private static readonly string DEFAULT_CHARS = "ACDEFGHJKLMNPQRTUVWXY34679";

        /// <summary>
        /// A range of possible brush colors
        /// </summary>
        private static readonly Brush[] BRUSH_COLORS =
            typeof(Brushes)
                .GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                .Where(x => x.PropertyType == typeof(Brush))
                .Select(x => x.GetValue(null, null) as Brush)
                .Where(x => x != null)
                .ToArray();

        /// <summary>
        /// Approximate the size in pixels of text drawn at the given fontsize
        /// </summary>
        private static int ApproxTextWidth(string text, FontFamily fontfamily, int fontsize)
        {
            using (var font = new Font(fontfamily, fontsize, GraphicsUnit.Pixel))
            using (var graphics = Graphics.FromImage(new Bitmap(1, 1))) {
                return (int) graphics.MeasureString(text, font).Width;
            }
        }

        /// <summary>
        /// Creates a random answer.
        /// </summary>
        /// <returns>The random answer.</returns>
        /// <param name="allowedchars">The list of allowed chars, supply a character multiple times to change frequency.</param>
        /// <param name="minlength">The minimum answer length.</param>
        /// <param name="maxlength">The maximum answer length.</param>
        public static string CreateRandomAnswer(string allowedchars = null, int minlength = 10, int maxlength = 12)
        {
            allowedchars = allowedchars ?? DEFAULT_CHARS;
            var rnd = new Random();
            var len = rnd.Next(Math.Min(minlength, maxlength), Math.Max(minlength, maxlength) + 1);
            if (len <= 0)
                throw new ArgumentException($"The values ${minlength} and ${maxlength} gave a final length of {len} and it must be greater than 0");

            return new string(Enumerable.Range(0, len).Select(x => allowedchars[rnd.Next(0, allowedchars.Length)]).ToArray());
        }

        /// <summary>
        /// Creates a captcha image.
        /// </summary>
        /// <returns>The captcha image.</returns>
        /// <param name="answer">The captcha solution string.</param>
        /// <param name="size">The size of the image, omit to get a size based on the string.</param>
        /// <param name="fontsize">The size of the font used to create the captcha, in pixels.</param>
        public static Bitmap CreateCaptcha(string answer, Size size = default(Size), int fontsize = 40)
        {
            var fontfamily = FontFamily.GenericSansSerif;
            var text_width = ApproxTextWidth(answer, fontfamily, fontsize);
            if (size.Width == 0 || size.Height == 0)
                size = new Size((int) (text_width * 1.2), (int) (fontsize * 1.2));

            var bmp = new Bitmap(size.Width, size.Height);
            var rnd = new Random();
            var stray_x = fontsize / 2;
            var stray_y = size.Height / 4;
            var ans_stray_x = fontsize / 3;
            var ans_stray_y = size.Height / 6;
            using (var graphics = Graphics.FromImage(bmp))
            using (var font1 = new Font(fontfamily, fontsize, GraphicsUnit.Pixel))
            using (var font2 = new Font(fontfamily, fontsize, GraphicsUnit.Pixel))
            using (var font3 = new HatchBrush(HatchStyle.Shingle, Color.GhostWhite, Color.DarkBlue))
            {
                graphics.Clear(Color.White);
                graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

                // Apply a some background string to make it hard to do OCR
                foreach (var color in new[] { Color.Yellow, Color.LightGreen, Color.GreenYellow })
                    using (var brush = new SolidBrush(color))
                        graphics.DrawString(CreateRandomAnswer(minlength: answer.Length, maxlength: answer.Length), font2, brush, rnd.Next(-stray_x, stray_x), rnd.Next(-stray_y, stray_y));


                var spacing = (size.Width / fontsize) + rnd.Next(0, stray_x);

                // Create a vertical background lines
                for (var i = rnd.Next(0, stray_x); i < size.Width; i += spacing)
                    using (var pen = new Pen(BRUSH_COLORS[rnd.Next(0, BRUSH_COLORS.Length)]))
                        graphics.DrawLine(pen, i + rnd.Next(-stray_x, stray_x), rnd.Next(0, stray_y), i + rnd.Next(-stray_x, stray_x), size.Height - rnd.Next(0, stray_y));

                spacing = (size.Height / fontsize) + rnd.Next(0, stray_y);
                // Create a horizontal background lines
                for (var i = rnd.Next(0, stray_y); i < size.Height; i += spacing)
                    using (var pen = new Pen(BRUSH_COLORS[rnd.Next(0, BRUSH_COLORS.Length)]))
                        graphics.DrawLine(pen, rnd.Next(0, stray_x), i + rnd.Next(-stray_y, stray_y), size.Width - rnd.Next(0, stray_x), i + rnd.Next(-stray_y, stray_y));

                // Draw the actual answer
                graphics.DrawString(answer, font1, font3, ((size.Width - text_width) / 2) + rnd.Next(-ans_stray_x, ans_stray_x), ((size.Height - fontsize) / 2) + rnd.Next(-ans_stray_y, ans_stray_y));

                return bmp;
            }
        }
    }
}
