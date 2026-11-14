using System;
using System.Collections.Generic;
using System.Linq;

namespace GraphiteCompanion.Services;

public sealed class OverlayBlock
{
    public string Id { get; set; } = "";
    public int Zone { get; set; } // 0 full width, 1 left, 2 right
    public bool Visible { get; set; } = true;
}

public sealed class OverlayLayout
{
    public List<OverlayAttachment> Attachments { get; set; } = new();
    public string BackgroundImagePath { get; set; } = "";
    public double BackgroundImageOpacity { get; set; } = .35;
    public CanvasLayout? Composition { get; set; }
    public double Opacity { get; set; } = 1;
    public string BackgroundColor { get; set; } = "";
    public Dictionary<string,CanvasPosition> MusicButtons {get;set;}=new();
    public double Scale { get; set; } = 1;
    public double Width { get; set; } = 960;
    public bool Footer { get; set; } = true;
    public bool RememberPosition { get; set; } = true;
    public double? X { get; set; }
    public double? Y { get; set; }
    public string Note { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public List<OverlayBlock> Blocks { get; set; } = Defaults();
    public static readonly (string Id, string Name, int Zone, bool Visible)[] Catalog =
    {
        ("music", "Музыкальный плеер", 0, true), ("sound", "Режимы и эквалайзер", 1, true),
        ("volume", "Громкость и микрофон", 1, true), ("voice", "Голосовые эффекты", 1, true),
        ("output", "Устройство вывода", 1, true), ("recording", "Запись и снимки", 2, true),
        ("timer", "Таймер", 2, true), ("apps", "Громкость программ", 2, true),
        ("note", "Моя заметка", 1, false), ("image", "Моя картинка", 2, false)
    };
    private static List<OverlayBlock> Defaults() => Catalog.Select(x => new OverlayBlock { Id = x.Id, Zone = x.Zone, Visible = x.Visible }).ToList();
    public OverlayLayout Copy() => new()
    {
        Composition = Composition?.Copy(), Opacity = Opacity, Scale = Scale, Width = Width, Footer = Footer, RememberPosition = RememberPosition,
        X = X, Y = Y, Note = Note, ImagePath = ImagePath, BackgroundColor=BackgroundColor,
        BackgroundImagePath=BackgroundImagePath, BackgroundImageOpacity=BackgroundImageOpacity,
        Attachments=(Attachments??new()).Where(x=>x!=null).Select(x=>x.Copy()).ToList(),
        MusicButtons=(MusicButtons??new()).Where(p=>p.Value!=null).ToDictionary(p=>p.Key,p=>p.Value.Copy()),
        Blocks = (Blocks ?? new()).Where(x => x != null).Select(x => new OverlayBlock { Id = x.Id, Zone = x.Zone, Visible = x.Visible }).ToList()
    };
    public OverlayLayout Normalize()
    {
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .35, 1) : 1;
        BackgroundColor=MonitorProfile.Color(BackgroundColor,"");
        BackgroundImagePath ??= "";
        BackgroundImageOpacity=MonitorProfile.Clamp(BackgroundImageOpacity,0,1,.35);
        Attachments=(Attachments??new()).Where(x=>x!=null && x.Id?.StartsWith("attachment:",StringComparison.Ordinal)==true && x.Kind is "note" or "image").DistinctBy(x=>x.Id).ToList();
        foreach(var item in Attachments){item.Text??="";item.ImagePath??="";item.Name=string.IsNullOrWhiteSpace(item.Name)?(item.Kind=="note"?"Заметка":"Картинка"):item.Name;}
        MusicButtons=(MusicButtons??new()).Where(p=>p.Key is "previous" or "play" or "next"&&p.Value!=null).ToDictionary(p=>p.Key,p=>new CanvasPosition{X=MonitorProfile.Clamp(p.Value.X,0,1,1),Y=MonitorProfile.Clamp(p.Value.Y,0,1,.5)});
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, .8, 1.25) : 1;
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 820, 1200) : 960;
        X = X is double x && double.IsFinite(x) ? Math.Clamp(x, 0, 1) : null;
        Y = Y is double y && double.IsFinite(y) ? Math.Clamp(y, 0, 1) : null;
        Note ??= ""; ImagePath ??= ""; if (Note.Length > 500) Note = Note[..500];
        var allowed=Catalog.Select(c=>c.Id).Concat(Attachments.Select(x=>x.Id)).ToHashSet();
        Blocks = (Blocks ?? new()).Where(x => x != null && allowed.Contains(x.Id)).DistinctBy(x => x.Id).ToList();
        foreach (var entry in Catalog) if (!Blocks.Any(x => x.Id == entry.Id)) Blocks.Add(new() { Id = entry.Id, Zone = entry.Zone, Visible = entry.Visible });
        foreach(var item in Attachments)if(!Blocks.Any(x=>x.Id==item.Id))Blocks.Add(new(){Id=item.Id,Zone=item.Kind=="note"?1:2});
        if(Composition!=null){Composition.Blocks.RemoveAll(x=>x.Id.StartsWith("attachment:",StringComparison.Ordinal)&&!allowed.Contains(x.Id));foreach(var item in Attachments)if(!Composition.Blocks.Any(x=>x.Id==item.Id))Composition.Blocks.Add(new(){Id=item.Id,Zone=item.Kind=="note"?1:2});}
        foreach (var block in Blocks) block.Zone = Math.Clamp(block.Zone, 0, 2);
        return this;
    }
}

public sealed class OverlayAttachment
{
    public string Id { get; set; } = "attachment:"+Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "note";
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public OverlayAttachment Copy() => new(){Id=Id,Kind=Kind,Name=Name,Text=Text,ImagePath=ImagePath};
}
