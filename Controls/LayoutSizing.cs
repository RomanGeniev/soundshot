using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json.Serialization;

namespace GraphiteCompanion.Controls;

public sealed class LayoutSize
{
    public double Scale { get; set; } = 1;
    public double Width { get; set; }
    public double Height { get; set; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
    public double ReferenceWidth { get; set; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
    public double ReferenceHeight { get; set; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
    public bool FreeAspect { get; set; }
    [JsonIgnore] public bool Proportional => ReferenceWidth > 0 && ReferenceHeight > 0;
    public LayoutSize Copy() { var copy=new LayoutSize {Scale=Scale,Width=Width,Height=Height,ReferenceWidth=ReferenceWidth,ReferenceHeight=ReferenceHeight,FreeAspect=FreeAspect};copy.Normalize();return copy; }
    public void Normalize() {
        // A legacy card can already be fitted below the interactive minimum.
        // Retain that effective scale when its column constraint is removed.
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, Proportional?.1:.75, 1.5) : 1;
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 0, 1600) : 0;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 0, 1000) : 0;
        ReferenceWidth = double.IsFinite(ReferenceWidth) ? Math.Clamp(ReferenceWidth, 0, 10000) : 0;
        ReferenceHeight = double.IsFinite(ReferenceHeight) ? Math.Clamp(ReferenceHeight, 0, 10000) : 0;
        if(Proportional){Width=ReferenceWidth*Scale;Height=ReferenceHeight*Scale;}
        else {ReferenceWidth=ReferenceHeight=0;FreeAspect=false;}
    }
}

internal static class BlockResize
{
    // Deltas and bounds are all in authored canvas coordinates, independent of fit/DPI.
    internal static LayoutSize Apply(LayoutSize baseline,Size bounds,Vector delta,string edge)
    {
        var next=baseline.Copy();next.FreeAspect=true;
        bool left=edge.Contains('w'),right=edge.Contains('e'),top=edge.Contains('n'),bottom=edge.Contains('s');
        double width=left||right?Math.Clamp(bounds.Width+(left?-delta.X:delta.X),Math.Min(96,bounds.Width),Math.Max(1600,bounds.Width)):bounds.Width;
        double height=top||bottom?Math.Clamp(bounds.Height+(top?-delta.Y:delta.Y),Math.Min(48,bounds.Height),Math.Max(1000,bounds.Height)):bounds.Height;
        next.ReferenceWidth=width/next.Scale;next.ReferenceHeight=height/next.Scale;next.Normalize();return next;
    }
}

