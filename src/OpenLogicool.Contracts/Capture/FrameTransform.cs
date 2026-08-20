namespace OpenLogicool.Contracts.Capture;

public sealed record FramePoint(double X, double Y);

public sealed record FrameSize(double Width, double Height)
{
    public void RequirePositive(string name)
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "width と height は正でなければなりません。");
        }
    }
}

public sealed record FrameRect(double X, double Y, double Width, double Height)
{
    public void RequirePositive(string name) =>
        new FrameSize(Width, Height).RequirePositive(name);

    public bool Contains(FramePoint point) =>
        point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;
}

public sealed record NormalizedPoint(double X, double Y)
{
    public void RequireUnitRange(string name)
    {
        if (X is < 0 or > 1 || Y is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name, "normalized 座標は 0..1 でなければなりません。");
        }
    }
}

/// <summary>一つの frame revision に固定された source→content→normalized→client→input の座標変換。</summary>
public sealed record FrameCoordinateTransform(long Revision, FrameRect ContentBounds)
{
    public NormalizedPoint SourceToNormalized(FramePoint source)
    {
        ContentBounds.RequirePositive(nameof(ContentBounds));
        if (!ContentBounds.Contains(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), "source 座標が content 範囲外です。");
        }

        return new NormalizedPoint(
            (source.X - ContentBounds.X) / ContentBounds.Width,
            (source.Y - ContentBounds.Y) / ContentBounds.Height);
    }

    public FramePoint NormalizedToClient(NormalizedPoint normalized, FrameSize clientSize)
    {
        normalized.RequireUnitRange(nameof(normalized));
        clientSize.RequirePositive(nameof(clientSize));
        return new FramePoint(normalized.X * clientSize.Width, normalized.Y * clientSize.Height);
    }

    public FramePoint ClientToInput(FramePoint client, FramePoint inputOrigin) =>
        new(client.X + inputOrigin.X, client.Y + inputOrigin.Y);
}

public sealed record FrameTransformSignature(
    int Width,
    int Height,
    string PixelFormat,
    double DpiX,
    double DpiY,
    FrameRect ContentBounds);
