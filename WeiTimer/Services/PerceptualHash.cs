using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;

namespace WeiTimer.Services;

/// <summary>
/// Autorun-timer presence detection via perceptual hashing (a DCT-based pHash,
/// hand-rolled rather than pulled from a NuGet package).
///
/// Deliberately NOT OCR. The timer container has static elements (border, icon,
/// "AUTO" label, panel background) that don't change frame to frame, even though
/// the digits inside it do. Hashing the whole region and comparing distances is
/// far cheaper than OCR and is exactly what a presence check needs, rather than
/// reading a value.
///
/// Bit-format does not need to match Python's `imagehash` library: calibration
/// and matching both happen entirely within this app, so only self-consistency
/// matters.
/// </summary>
public static class PerceptualHash
{
    private const int ImgSize = 32;   // resize target before DCT
    private const int HashSize = 8;   // top-left low-frequency block edge length

    // CosTable[k, n] = cos(pi/N * (n + 0.5) * k) — the DCT-II basis, precomputed once.
    private static readonly double[,] CosTable = BuildCosTable();

    private static double[,] BuildCosTable()
    {
        var table = new double[ImgSize, ImgSize];
        for (var k = 0; k < ImgSize; k++)
            for (var n = 0; n < ImgSize; n++)
                table[k, n] = Math.Cos(Math.PI / ImgSize * (n + 0.5) * k);
        return table;
    }

    /// <summary>Resize-to-grayscale-32x32 -> 2D DCT-II -> top-left 8x8 block ->
    /// threshold each coefficient against the block's median -> 64-bit hash.</summary>
    public static ulong ComputeHash(Bitmap image)
    {
        var pixels = ToGrayscale(image, ImgSize, ImgSize);
        var dct = Dct2D(pixels);

        var block = new double[HashSize * HashSize];
        var idx = 0;
        for (var y = 0; y < HashSize; y++)
            for (var x = 0; x < HashSize; x++)
                block[idx++] = dct[y, x];

        var sorted = (double[])block.Clone();
        Array.Sort(sorted);
        var mid = sorted.Length / 2;
        var median = (sorted[mid - 1] + sorted[mid]) / 2.0;

        ulong hash = 0;
        for (var i = 0; i < block.Length; i++)
            if (block[i] > median)
                hash |= 1UL << i;
        return hash;
    }

    public static int HammingDistance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    private static double[,] ToGrayscale(Bitmap source, int width, int height)
    {
        // Combined resize + grayscale in one DrawImage call: a Rec.601 luma
        // color matrix (matching PIL's "L" conversion weights) applied while
        // downsampling with high-quality bicubic interpolation.
        using var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

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
                new Rectangle(0, 0, width, height),
                0, 0, source.Width, source.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        var pixels = new double[height, width];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                pixels[y, x] = resized.GetPixel(x, y).R; // R==G==B after the grayscale matrix

        return pixels;
    }

    private static double[,] Dct2D(double[,] input)
    {
        var n = ImgSize;
        var temp = new double[n, n];
        for (var x = 0; x < n; x++)
            for (var k = 0; k < n; k++)
            {
                double sum = 0;
                for (var row = 0; row < n; row++)
                    sum += input[row, x] * CosTable[k, row];
                temp[k, x] = sum;
            }

        var output = new double[n, n];
        for (var y = 0; y < n; y++)
            for (var k = 0; k < n; k++)
            {
                double sum = 0;
                for (var col = 0; col < n; col++)
                    sum += temp[y, col] * CosTable[k, col];
                output[y, k] = sum;
            }

        return output;
    }
}

/// <summary>A calibrated hash + the distance threshold under which a frame counts
/// as "the timer box is showing". Mirrors detect.py's ReferenceHash.</summary>
public sealed class ReferenceHash
{
    // Fallback if calibration only had one sample to work with.
    public const int FallbackMaxDistance = 10;
    // Safety margin added on top of the largest distance observed between
    // same-timer-different-tick samples during calibration.
    private const int CalibrationMargin = 4;

    public ulong HashBits { get; }
    public int MaxDistance { get; }

    public ReferenceHash(ulong hashBits, int maxDistance = FallbackMaxDistance)
    {
        HashBits = hashBits;
        MaxDistance = maxDistance;
    }

    public string ToHexString() => HashBits.ToString("X16");

    public static ReferenceHash FromHexString(string hex, int maxDistance) =>
        new(Convert.ToUInt64(hex, 16), maxDistance);

    /// <summary>Build a reference from multiple frames captured ~1s apart while the
    /// timer box is visible (so the digits differ between samples but the
    /// surrounding chrome doesn't). The threshold is derived from how much the
    /// hash naturally moves just from the digits ticking, rather than a fixed
    /// guess -- this is what keeps a per-second-changing countdown from being
    /// misread as "the box disappeared".</summary>
    public static ReferenceHash Calibrate(IReadOnlyList<Bitmap> samples)
    {
        if (samples.Count == 0)
            throw new ArgumentException("Calibrate() needs at least one sample image", nameof(samples));

        var hashes = new ulong[samples.Count];
        for (var i = 0; i < samples.Count; i++)
            hashes[i] = PerceptualHash.ComputeHash(samples[i]);

        var reference = hashes[0];
        int maxDistance;
        if (hashes.Length == 1)
        {
            maxDistance = FallbackMaxDistance;
        }
        else
        {
            var maxObserved = 0;
            for (var i = 1; i < hashes.Length; i++)
                maxObserved = Math.Max(maxObserved, PerceptualHash.HammingDistance(reference, hashes[i]));
            maxDistance = maxObserved + CalibrationMargin;
        }

        return new ReferenceHash(reference, maxDistance);
    }

    /// <summary>True if `current` is close enough to this reference to count as
    /// "the timer box is showing".</summary>
    public bool IsTimerPresent(Bitmap current)
    {
        var currentHash = PerceptualHash.ComputeHash(current);
        return PerceptualHash.HammingDistance(currentHash, HashBits) <= MaxDistance;
    }
}
