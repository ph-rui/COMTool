using SkiaSharp;

namespace Iciclecreek.Terminal.Skia;

internal readonly record struct PictureDraw(SharedSkImage Image, SKRect Source, SKRect Destination, SKRect? Clip);
