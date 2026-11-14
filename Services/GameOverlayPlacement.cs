using System;

namespace GraphiteCompanion.Services;

public sealed class GameOverlayPlacement
{
    public string Monitor { get; set; } = "";
    public double X { get; set; } = .7;
    public double Y { get; set; } = .2;
    public GameOverlayPlacement Copy() => new()
    {
        Monitor = Monitor ?? "",
        X = double.IsFinite(X) ? Math.Clamp(X, 0, 1) : .7,
        Y = double.IsFinite(Y) ? Math.Clamp(Y, 0, 1) : .2
    };
}
