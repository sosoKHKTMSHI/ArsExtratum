namespace ArsExtractum.Core.Documents;

public sealed record PdfPoint(double X, double Y);

public sealed record PdfBounds(double Left, double Bottom, double Right, double Top)
{
    public double Width => Right - Left;

    public double Height => Top - Bottom;

    public double CenterX => (Left + Right) / 2d;

    public double CenterY => (Bottom + Top) / 2d;

    public static PdfBounds Union(IEnumerable<PdfBounds> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        var values = bounds.ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("Ao menos um retângulo é necessário.", nameof(bounds));
        }

        return new(
            values.Min(static item => item.Left),
            values.Min(static item => item.Bottom),
            values.Max(static item => item.Right),
            values.Max(static item => item.Top));
    }
}
