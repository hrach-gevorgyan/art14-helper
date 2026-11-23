using System.Globalization;
using System.Xml.Linq;
namespace Art14Helper;

/// <summary>A color space by its primaries and white point, as CIE 1931 xy.</summary>
internal sealed record Chromaticities(
    (double X, double Y) Red,
    (double X, double Y) Green,
    (double X, double Y) Blue,
    (double X, double Y) White,
    string Source)
{
    internal static readonly (double, double) D65 = (0.3127, 0.3290);

    internal static readonly Chromaticities Srgb =
        new((0.640, 0.330), (0.300, 0.600), (0.150, 0.060), D65, "sRGB");

    internal static readonly Chromaticities DisplayP3 =
        new((0.680, 0.320), (0.265, 0.690), (0.150, 0.060), D65, "Display P3");

    /// <summary>Adobe RGB (1998) primaries (the color space; no Adobe data or file).</summary>
    internal static readonly Chromaticities AdobeRgb =
        new((0.640, 0.330), (0.210, 0.710), (0.150, 0.060), D65, "AdobeRGB-compatible");

    /// <summary>Plausible values: inside the xy chart and a non-degenerate triangle.</summary>
    internal bool IsValid()
    {
        var points = new[] { Red, Green, Blue, White };
        if (points.Any(p => p.X <= 0 || p.Y <= 0 || p.X >= 1 || p.Y >= 1 || p.X + p.Y >= 1))
            return false;
        var area = Math.Abs((Green.X - Red.X) * (Blue.Y - Red.Y) - (Blue.X - Red.X) * (Green.Y - Red.Y)) / 2;
        return area > 0.05;
    }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "{0}: R {1:0.0000},{2:0.0000} G {3:0.0000},{4:0.0000} B {5:0.0000},{6:0.0000} W {7:0.0000},{8:0.0000}",
        Source, Red.X, Red.Y, Green.X, Green.Y, Blue.X, Blue.Y, White.X, White.Y);

    /// <summary>
    /// The panel's measured primaries and white from a HONOR factory calibration
    /// file (DUTInfo_Feature: "x,y,luminance"). Only these numbers are read.
    /// </summary>
    internal static Chromaticities? FromFactoryCalibration(string xml)
    {
        var info = XDocument.Parse(xml).Root?.Element("DUTInfo_Feature");
        if (info is null)
            return null;
        (double, double)? Read(string name)
        {
            var parts = info.Element(name)?.Value.Split(',');
            if (parts is not { Length: >= 2 }
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return null;
            return (x, y);
        }
        if (Read("Origin_Red") is not { } r || Read("Origin_Green") is not { } g
            || Read("Origin_Blue") is not { } b || Read("Origin_White") is not { } w)
            return null;
        var result = new Chromaticities(r, g, b, w, "factory measurement");
        return result.IsValid() ? result : null;
    }
}

/// <summary>
/// The color math behind the color modes: converting content in a target space
/// (sRGB, Display P3) into the panel's own colors, as a 3D LUT for the graphics
/// driver and as ICC profiles for color-managed apps. Matches HONOR's factory
/// correction within a few percent on saturated colors and exactly on white.
/// </summary>
internal static class ColorMath
{
    internal const double Gamma = 2.2;

    /// <summary>RGB (linear) → XYZ matrix of a color space, white at Y = 1.</summary>
    internal static double[,] RgbToXyz(Chromaticities space)
    {
        var primaries = new double[3, 3];
        var columns = new[] { space.Red, space.Green, space.Blue };
        for (var c = 0; c < 3; c++)
        {
            var xyz = XyToXyz(columns[c]);
            for (var r = 0; r < 3; r++)
                primaries[r, c] = xyz[r];
        }
        var scale = Solve(primaries, XyToXyz(space.White));
        var result = new double[3, 3];
        for (var r = 0; r < 3; r++)
        for (var c = 0; c < 3; c++)
            result[r, c] = primaries[r, c] * scale[c];
        return result;
    }

    internal static double[] XyToXyz((double X, double Y) xy) => [xy.X / xy.Y, 1.0, (1 - xy.X - xy.Y) / xy.Y];

    /// <summary>
    /// size^3 LUT (red outermost, values 0..1) that shows <paramref name="target"/>
    /// content correctly on <paramref name="panel"/>: gamma 2.2 in and out, the
    /// target's white (D65), full brightness kept (the brightest channel of white
    /// stays at 1), colors outside the panel's range clipped.
    /// </summary>
    internal static double[] BuildLut(Chromaticities panel, Chromaticities target, int size)
    {
        var toXyz = RgbToXyz(target);
        var toPanel = Invert(RgbToXyz(panel));
        var convert = Multiply(toPanel, toXyz);
        var white = Apply(convert, [1, 1, 1]);
        var norm = white.Max();
        var lut = new double[size * size * size * 3];
        var max = size - 1;
        var i = 0;
        for (var r = 0; r < size; r++)
        for (var g = 0; g < size; g++)
        for (var b = 0; b < size; b++)
        {
            var linear = new[] { Math.Pow(r / (double)max, Gamma), Math.Pow(g / (double)max, Gamma), Math.Pow(b / (double)max, Gamma) };
            var output = Apply(convert, linear);
            for (var c = 0; c < 3; c++)
                lut[i++] = Math.Pow(Math.Clamp(output[c] / norm, 0, 1), 1 / Gamma);
        }
        return lut;
    }

    /// <summary>
    /// Moves a correction table's white to the panel's own white: every channel is
    /// divided (in linear light) by what white came out as. Colors stay calibrated,
    /// only the warmer D65 white goes away.
    /// </summary>
    internal static double[] KeepNativeWhite(double[] lut)
    {
        var white = new double[3];
        for (var c = 0; c < 3; c++)
            white[c] = Math.Max(1e-6, Math.Pow(lut[lut.Length - 3 + c], Gamma));
        var result = new double[lut.Length];
        for (var i = 0; i < lut.Length; i++)
            result[i] = Math.Pow(Math.Clamp(Math.Pow(lut[i], Gamma) / white[i % 3], 0, 1), 1 / Gamma);
        return result;
    }

    // ---- ICC profile (version 2, matrix and gamma) ----

    /// <summary>
    /// A display profile for <paramref name="space"/> with gamma 2.2: what color-
    /// managed apps (Photoshop, browsers, Photos) need to know about the screen.
    /// Generated by the app; no third-party data.
    /// </summary>
    internal static byte[] BuildIccProfile(Chromaticities space, string description)
    {
        // Colorants are stored adapted to the D50 connection space (Bradford).
        var matrix = Multiply(BradfordAdaptation(XyToXyz(space.White), [0.9642, 1.0, 0.8249]), RgbToXyz(space));
        var white = XyToXyz(space.White);

        var tags = new List<(string Signature, byte[] Data)>
        {
            ("desc", DescriptionTag(description)),
            ("cprt", TextTag("Generated by " + AppInfo.Name + ". No copyright.")),
            ("wtpt", XyzTag(white)),
            ("rXYZ", XyzTag([matrix[0, 0], matrix[1, 0], matrix[2, 0]])),
            ("gXYZ", XyzTag([matrix[0, 1], matrix[1, 1], matrix[2, 1]])),
            ("bXYZ", XyzTag([matrix[0, 2], matrix[1, 2], matrix[2, 2]]))
        };
        var curve = CurveTag(Gamma);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var tagCount = tags.Count + 3;
        var offset = 128 + 4 + tagCount * 12;
        var table = new List<(string, int, int)>();
        var blobs = new List<byte[]>();
        foreach (var (signature, data) in tags)
        {
            table.Add((signature, offset, data.Length));
            blobs.Add(data);
            offset += Align4(data.Length);
        }
        foreach (var signature in new[] { "rTRC", "gTRC", "bTRC" })
            table.Add((signature, offset, curve.Length)); // the three curves share one block
        blobs.Add(curve);
        offset += Align4(curve.Length);

        // Header.
        WriteU32(writer, (uint)offset);
        WriteU32(writer, 0);
        WriteU32(writer, 0x02100000);
        WriteAscii(writer, "mntr");
        WriteAscii(writer, "RGB ");
        WriteAscii(writer, "XYZ ");
        var now = DateTime.UtcNow;
        foreach (var part in new[] { now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second })
            WriteU16(writer, (ushort)part);
        WriteAscii(writer, "acsp");
        WriteAscii(writer, "MSFT");
        writer.Write(new byte[20]); // flags (4), manufacturer (4), model (4), attributes (8)
        WriteU32(writer, 0);        // perceptual intent
        WriteS15Fixed16(writer, 0.9642);
        WriteS15Fixed16(writer, 1.0);
        WriteS15Fixed16(writer, 0.8249);
        writer.Write(new byte[48]); // creator, profile ID, reserved

        WriteU32(writer, (uint)tagCount);
        foreach (var (signature, tagOffset, size) in table)
        {
            WriteAscii(writer, signature);
            WriteU32(writer, (uint)tagOffset);
            WriteU32(writer, (uint)size);
        }
        foreach (var blob in blobs)
        {
            writer.Write(blob);
            writer.Write(new byte[Align4(blob.Length) - blob.Length]);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static byte[] DescriptionTag(string text)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteAscii(writer, "desc");
        WriteU32(writer, 0);
        var ascii = System.Text.Encoding.ASCII.GetBytes(text);
        WriteU32(writer, (uint)ascii.Length + 1);
        writer.Write(ascii);
        writer.Write((byte)0);
        WriteU32(writer, 0); // Unicode language
        WriteU32(writer, 0); // Unicode count
        WriteU16(writer, 0); // ScriptCode code
        writer.Write((byte)0);
        writer.Write(new byte[67]);
        return stream.ToArray();
    }

    private static byte[] TextTag(string text)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteAscii(writer, "text");
        WriteU32(writer, 0);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(text));
        writer.Write((byte)0);
        return stream.ToArray();
    }

    private static byte[] XyzTag(double[] xyz)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteAscii(writer, "XYZ ");
        WriteU32(writer, 0);
        foreach (var value in xyz)
            WriteS15Fixed16(writer, value);
        return stream.ToArray();
    }

    private static byte[] CurveTag(double gamma)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteAscii(writer, "curv");
        WriteU32(writer, 0);
        WriteU32(writer, 1);
        WriteU16(writer, (ushort)Math.Round(gamma * 256)); // u8Fixed8
        return stream.ToArray();
    }

    private static void WriteU32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteU16(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteS15Fixed16(BinaryWriter writer, double value)
        => WriteU32(writer, unchecked((uint)(int)Math.Round(value * 65536)));

    private static void WriteAscii(BinaryWriter writer, string fourCc)
        => writer.Write(System.Text.Encoding.ASCII.GetBytes(fourCc));

    // ---- 3x3 matrices ----

    private static double[,] BradfordAdaptation(double[] sourceWhite, double[] destinationWhite)
    {
        double[,] bradford = { { 0.8951, 0.2664, -0.1614 }, { -0.7502, 1.7135, 0.0367 }, { 0.0389, -0.0685, 1.0296 } };
        var source = Apply(bradford, sourceWhite);
        var destination = Apply(bradford, destinationWhite);
        var scale = new double[3, 3];
        for (var i = 0; i < 3; i++)
            scale[i, i] = destination[i] / source[i];
        return Multiply(Invert(bradford), Multiply(scale, bradford));
    }

    internal static double[] Apply(double[,] m, double[] v) =>
    [
        m[0, 0] * v[0] + m[0, 1] * v[1] + m[0, 2] * v[2],
        m[1, 0] * v[0] + m[1, 1] * v[1] + m[1, 2] * v[2],
        m[2, 0] * v[0] + m[2, 1] * v[1] + m[2, 2] * v[2]
    ];

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var result = new double[3, 3];
        for (var r = 0; r < 3; r++)
        for (var c = 0; c < 3; c++)
            result[r, c] = a[r, 0] * b[0, c] + a[r, 1] * b[1, c] + a[r, 2] * b[2, c];
        return result;
    }

    internal static double[,] Invert(double[,] m)
    {
        var det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
                - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
                + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        if (Math.Abs(det) < 1e-12)
            throw new InvalidOperationException("The color primaries don't form a valid color space.");
        var inv = new double[3, 3];
        inv[0, 0] = (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) / det;
        inv[0, 1] = (m[0, 2] * m[2, 1] - m[0, 1] * m[2, 2]) / det;
        inv[0, 2] = (m[0, 1] * m[1, 2] - m[0, 2] * m[1, 1]) / det;
        inv[1, 0] = (m[1, 2] * m[2, 0] - m[1, 0] * m[2, 2]) / det;
        inv[1, 1] = (m[0, 0] * m[2, 2] - m[0, 2] * m[2, 0]) / det;
        inv[1, 2] = (m[0, 2] * m[1, 0] - m[0, 0] * m[1, 2]) / det;
        inv[2, 0] = (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]) / det;
        inv[2, 1] = (m[0, 1] * m[2, 0] - m[0, 0] * m[2, 1]) / det;
        inv[2, 2] = (m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0]) / det;
        return inv;
    }

    private static double[] Solve(double[,] m, double[] v) => Apply(Invert(m), v);
}
