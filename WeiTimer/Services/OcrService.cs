using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace WeiTimer.Services;

/// <summary>
/// OCR for the carat count on the career-complete results screen.
///
/// Unlike the timer (a presence check, handled by perceptual hashing), this
/// needs the actual numeric value. Uses Windows.Media.Ocr via WinRT interop
/// rather than Tesseract, so no OCR binaries/tessdata need to be bundled. The
/// result is always shown to the user to confirm/edit before it's committed --
/// OCR misreads on stylized digits are common enough that it shouldn't be
/// trusted blindly.
/// </summary>
public static partial class OcrService
{
    [GeneratedRegex(@"\d[\d,]*")]
    private static partial Regex DigitPattern();

    /// <summary>Returns the parsed integer carat count, or null if nothing
    /// digit-shaped was found in the region (or no OCR engine/language is
    /// available on this machine).</summary>
    public static async Task<int?> ExtractCaratCountAsync(Bitmap img)
    {
        using var processed = Preprocess(img);

        using var pngStream = new MemoryStream();
        processed.Save(pngStream, ImageFormat.Png);
        pngStream.Position = 0;

        using var randomAccessStream = pngStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        var engine = OcrEngine.TryCreateFromLanguage(new Language("en"))
                     ?? OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
            return null;

        var result = await engine.RecognizeAsync(softwareBitmap);
        var match = DigitPattern().Match(result.Text);
        if (!match.Success)
            return null;

        var digits = match.Value.Replace(",", "");
        return int.TryParse(digits, out var value) ? value : null;
    }

    /// <summary>Grayscale + fixed threshold + upscale -- makes stylized/anti-aliased
    /// game fonts far more reliable for OCR than the raw crop.</summary>
    private static Bitmap Preprocess(Bitmap source, int upscale = 3)
    {
        using var gray = ToGrayscale(source);
        using var blackAndWhite = ThresholdToBlackAndWhite(gray, threshold: 140);
        return upscale > 1 ? Upscale(blackAndWhite, upscale) : (Bitmap)blackAndWhite.Clone();
    }

    private static Bitmap ToGrayscale(Bitmap source)
    {
        var gray = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(gray);
        var grayMatrix = new ColorMatrix(new float[][]
        {
            new float[] { 0.299f, 0.299f, 0.299f, 0, 0 },
            new float[] { 0.587f, 0.587f, 0.587f, 0, 0 },
            new float[] { 0.114f, 0.114f, 0.114f, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 },
        });
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(grayMatrix);
        g.DrawImage(
            source,
            new Rectangle(0, 0, source.Width, source.Height),
            0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel,
            attributes);
        return gray;
    }

    private static Bitmap ThresholdToBlackAndWhite(Bitmap gray, byte threshold)
    {
        var result = new Bitmap(gray.Width, gray.Height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < gray.Height; y++)
        {
            for (var x = 0; x < gray.Width; x++)
            {
                var v = gray.GetPixel(x, y).R > threshold ? (byte)255 : (byte)0;
                result.SetPixel(x, y, Color.FromArgb(255, v, v, v));
            }
        }
        return result;
    }

    private static Bitmap Upscale(Bitmap source, int factor)
    {
        var scaled = new Bitmap(source.Width * factor, source.Height * factor, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, new Rectangle(0, 0, scaled.Width, scaled.Height));
        return scaled;
    }
}
