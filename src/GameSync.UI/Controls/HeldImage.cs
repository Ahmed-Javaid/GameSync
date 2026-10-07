using Avalonia;
using SkiaSharp;

namespace GameSync.UI.Controls;

/// <summary>A prepared picture, held by a control and by the drawings of it the renderer still keeps; let go when none holds it.</summary>
internal sealed class HeldImage(SKImage image, PixelSize size)
{
    private int _holders = 1;

    public SKImage Image { get; } = image;

    public PixelSize Size { get; } = size;

    public HeldImage Take()
    {
        Interlocked.Increment(ref _holders);
        return this;
    }

    public void Release()
    {
        if (Interlocked.Decrement(ref _holders) == 0)
        {
            Image.Dispose();
        }
    }
}
