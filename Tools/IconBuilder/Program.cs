using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var outputPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "AppIcon.ico");

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
using var output = File.Create(outputPath);
using var writer = new BinaryWriter(output);

writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)sizes.Length);

var pngImages = sizes
    .Select(size => (Size: size, Bytes: RenderPng(size)))
    .ToList();

var imageOffset = 6 + (16 * pngImages.Count);

foreach (var image in pngImages)
{
    writer.Write((byte)(image.Size == 256 ? 0 : image.Size));
    writer.Write((byte)(image.Size == 256 ? 0 : image.Size));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(image.Bytes.Length);
    writer.Write(imageOffset);

    imageOffset += image.Bytes.Length;
}

foreach (var image in pngImages)
{
    writer.Write(image.Bytes);
}

static byte[] RenderPng(int size)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(bitmap);

    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    graphics.Clear(Color.Transparent);
    graphics.ScaleTransform(size / 256f, size / 256f);

    using var backgroundBrush = new LinearGradientBrush(
        new Rectangle(18, 18, 220, 220),
        Color.FromArgb(25, 132, 255),
        Color.FromArgb(9, 54, 120),
        LinearGradientMode.ForwardDiagonal);

    using var shadowBrush = new SolidBrush(Color.FromArgb(45, 0, 0, 0));
    using var boxBrush = new LinearGradientBrush(
        new Rectangle(58, 82, 140, 102),
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(203, 219, 240),
        LinearGradientMode.Vertical);
    using var lidBrush = new LinearGradientBrush(
        new Rectangle(48, 68, 160, 42),
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(173, 199, 229),
        LinearGradientMode.Vertical);
    using var strokePen = new Pen(Color.FromArgb(7, 38, 84), 10)
    {
        LineJoin = LineJoin.Round,
        StartCap = LineCap.Round,
        EndCap = LineCap.Round
    };
    using var arrowPen = new Pen(Color.FromArgb(24, 150, 92), 18)
    {
        LineJoin = LineJoin.Round,
        StartCap = LineCap.Round,
        EndCap = LineCap.Round
    };
    using var highlightPen = new Pen(Color.FromArgb(170, 255, 255, 255), 8)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round
    };

    FillRoundedRectangle(graphics, shadowBrush, new RectangleF(24, 30, 216, 216), 54);
    FillRoundedRectangle(graphics, backgroundBrush, new RectangleF(18, 18, 220, 220), 54);

    graphics.DrawArc(highlightPen, 54, 44, 92, 72, 205, 68);

    FillRoundedRectangle(graphics, boxBrush, new RectangleF(58, 88, 140, 102), 16);
    DrawRoundedRectangle(graphics, strokePen, new RectangleF(58, 88, 140, 102), 16);
    FillRoundedRectangle(graphics, lidBrush, new RectangleF(48, 70, 160, 38), 13);
    DrawRoundedRectangle(graphics, strokePen, new RectangleF(48, 70, 160, 38), 13);

    graphics.DrawLine(strokePen, 84, 126, 172, 126);
    graphics.DrawLine(strokePen, 84, 154, 150, 154);

    graphics.DrawArc(arrowPen, 87, 71, 88, 88, 210, 238);
    graphics.DrawLine(arrowPen, 154, 74, 181, 80);
    graphics.DrawLine(arrowPen, 154, 74, 165, 101);

    using var memoryStream = new MemoryStream();
    bitmap.Save(memoryStream, ImageFormat.Png);
    return memoryStream.ToArray();
}

static void FillRoundedRectangle(Graphics graphics, Brush brush, RectangleF bounds, float radius)
{
    using var path = CreateRoundedRectanglePath(bounds, radius);
    graphics.FillPath(brush, path);
}

static void DrawRoundedRectangle(Graphics graphics, Pen pen, RectangleF bounds, float radius)
{
    using var path = CreateRoundedRectanglePath(bounds, radius);
    graphics.DrawPath(pen, path);
}

static GraphicsPath CreateRoundedRectanglePath(RectangleF bounds, float radius)
{
    var diameter = radius * 2;
    var path = new GraphicsPath();

    path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
    path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
    path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
    path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
    path.CloseFigure();

    return path;
}
