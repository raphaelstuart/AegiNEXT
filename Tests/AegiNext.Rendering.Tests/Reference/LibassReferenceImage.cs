using System.Runtime.InteropServices;

namespace AegiNext.Rendering.Tests.Reference;

[StructLayout(LayoutKind.Sequential)]
internal struct LibassReferenceImage
{
    internal int Width;
    internal int Height;
    internal int Stride;
    internal nint Bitmap;
    internal uint Color;
    internal int X;
    internal int Y;
    internal nint Next;
    internal int Type;
}
