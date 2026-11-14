using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GraphiteCompanion.Controls;

// Measures at the captured logical width, then scales the complete result once.
// Window constraints change only the uniform factor, never the layout inside a card.
internal sealed class UniformBlockHost : Decorator
{
    private LayoutSize size = new();
    private Size logical;
    private double factor = 1;
    private double contentFactor=1;
    public void Configure(LayoutSize value) { size=value.Copy();InvalidateMeasure(); }
    protected override Size MeasureOverride(Size available)
    {
        if(Child==null)return new Size();
        Child.Measure(new Size(size.ReferenceWidth,double.PositiveInfinity));
        if(size.FreeAspect){
            // Reflow at the requested width. A short frame fits its contents uniformly;
            // a tall frame adds room without distorting text, controls or pictures.
            logical=new Size(Math.Max(size.ReferenceWidth,Child.DesiredSize.Width),Math.Max(size.ReferenceHeight,Child.DesiredSize.Height));
            contentFactor=Math.Min(1,Math.Min(size.ReferenceWidth/logical.Width,size.ReferenceHeight/logical.Height));
            factor=Math.Min(size.Scale,double.IsFinite(available.Width)?Math.Max(1,available.Width)/size.ReferenceWidth:size.Scale);
            ClipToBounds=true;return new Size(size.ReferenceWidth*factor,size.ReferenceHeight*factor);
        }
        contentFactor=1;ClipToBounds=false;
        logical=new Size(size.ReferenceWidth,Math.Max(size.ReferenceHeight,Child.DesiredSize.Height));
        factor=Math.Min(size.Scale,double.IsFinite(available.Width)?Math.Max(1,available.Width)/logical.Width:size.Scale);
        return new Size(logical.Width*factor,logical.Height*factor);
    }
    protected override Size ArrangeOverride(Size final)
    {
        if(Child!=null){Child.RenderTransform=new ScaleTransform(factor*contentFactor,factor*contentFactor);Child.Arrange(new Rect(logical));}
        return final;
    }
}
