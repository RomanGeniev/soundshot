using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraphiteCompanion.Services;

public sealed record MetricValue(string Id, string Group, string Name, string Unit, double? Value, double? Minimum = null, double? Maximum = null);
public sealed record MetricSnapshot(DateTime Timestamp, List<MetricValue> Values, string Status, double? CollectionDurationMs = null);
public sealed record MetricRequest(int IntervalMs = 1000, int ProcessId = 0, bool Frames = false, long ProcessStartedTicks = 0);

public sealed class MetricTile
{
    public MetricTile Copy()=>(MetricTile)MemberwiseClone();
    public string MetricId { get; set; } = "system/cpu";
    public string Label { get; set; } = "";
    public string View { get; set; } = "Text";
    public string Color { get; set; } = "";
    public double FontSize { get; set; }
    public double? GraphMin { get; set; }
    public double? GraphMax { get; set; }
}
public sealed class MonitorProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Основной";
    public string Monitor { get; set; } = "";
    public double X { get; set; } = .02;
    public double Y { get; set; } = .02;
    public string Presentation { get; set; } = "Compact";
    public string Direction { get; set; } = "Rows";
    public double Width { get; set; } = 320;
    public double FontSize { get; set; } = 14;
    public int Columns { get; set; } = 1;
    public double Spacing { get; set; } = 1;
    public double BackgroundOpacity { get; set; } = 0;
    public string Background { get; set; } = "#101722";
    public string Foreground { get; set; } = "#E8EDF5";
    public bool ShowUnits { get; set; } = true;
    public List<MetricTile> Tiles { get; set; } = new()
    {
        new() { MetricId="system/cpu",Label="CPU загрузка" },
        new() { MetricId="system/gpu/load",Label="GPU загрузка" },
        new() { MetricId="system/memory/used",Label="RAM" },
        new() { MetricId="frames/fps",Label="FPS" },
        new() { MetricId="frames/time",Label="Кадр" }
    };
    public MonitorProfile Copy(){var copy=(MonitorProfile)MemberwiseClone();copy.Tiles=(Tiles??new()).Where(t=>t!=null).Select(t=>t.Copy()).ToList();return copy;}
    public MonitorProfile Normalize()
    {
        var p=Copy();p.Id=string.IsNullOrWhiteSpace(p.Id)?Guid.NewGuid().ToString("N"):p.Id;
        p.Presentation=p.Presentation=="Card"?"Card":"Compact";
        p.Direction=p.Direction=="Inline"?"Inline":"Rows";
        p.Name=Clean(p.Name,48,"Основной");p.Monitor??="";
        p.X=Clamp(p.X,0,1,.02);p.Y=Clamp(p.Y,0,1,.02);p.Width=Clamp(p.Width,160,1600,320);
        p.FontSize=Clamp(p.FontSize,10,64,16);p.Columns=Math.Clamp(p.Columns,1,4);p.Spacing=Clamp(p.Spacing,0,30,5);
        p.BackgroundOpacity=Clamp(p.BackgroundOpacity,0,1,.75);
        p.Background=Color(p.Background,"#101722");p.Foreground=Color(p.Foreground,"#E8EDF5");
        p.Tiles=(p.Tiles??new()).Where(t=>t!=null&&!string.IsNullOrWhiteSpace(t.MetricId)).Take(64).ToList();
        foreach(var t in p.Tiles){t.Label=Clean(t.Label,80,"");t.View=t.View is "Graph" or "Bar"?t.View:"Text";t.Color=Color(t.Color,"");t.FontSize=t.FontSize==0?0:Clamp(t.FontSize,10,64,16);if(t.GraphMin is { } lo&&!double.IsFinite(lo))t.GraphMin=null;if(t.GraphMax is { } hi&&!double.IsFinite(hi))t.GraphMax=null;if(t.GraphMax<=t.GraphMin){t.GraphMin=t.GraphMax=null;}}
        return p;
    }
    internal static double Clamp(double v,double lo,double hi,double fallback)=>double.IsFinite(v)?Math.Clamp(v,lo,hi):fallback;
    internal static string Color(string? s,string fallback)=>s!=null&&System.Text.RegularExpressions.Regex.IsMatch(s,"^#[0-9A-Fa-f]{6}$")?s:fallback;
    internal static string Clean(string? s,int length,string fallback){var v=new string((s??"").Where(c=>!char.IsControl(c)).Take(length).ToArray()).Trim();return v.Length==0?fallback:v;}
}
public sealed class MonitoringSettings
{
    public bool ShowOverlay { get; set; }
    public string Renderer { get; set; } = "Window";
    public int IntervalMs { get; set; } = 1000;
    public int LogLimitMb {get;set;}=100;
    public bool FramesEnabled { get; set; }
    public string ActiveProfile { get; set; } = "";
    public List<MonitorProfile> Profiles { get; set; } = new() { new() };
    public MonitoringSettings Copy(){var copy=(MonitoringSettings)MemberwiseClone();copy.Profiles=(Profiles??new()).Where(p=>p!=null).Select(p=>p.Copy()).ToList();return copy;}
    public MonitoringSettings Normalize()
    {
        var s=Copy();s.IntervalMs=Math.Clamp(s.IntervalMs,250,5000);
        s.LogLimitMb=Math.Clamp(s.LogLimitMb,10,2048);
        s.Renderer=s.Renderer=="RTSS"?"RTSS":"Window";
        s.Profiles=(s.Profiles??new()).Where(p=>p!=null).Take(24).Select(p=>p.Normalize()).DistinctBy(p=>p.Id).ToList();
        if(s.Profiles.Count==0)s.Profiles.Add(new());
        if(!s.Profiles.Any(p=>p.Id==s.ActiveProfile))s.ActiveProfile=s.Profiles[0].Id;
        return s;
    }
    [JsonIgnore] public MonitorProfile Current=>Profiles.FirstOrDefault(p=>p.Id==ActiveProfile)??Profiles[0];
}
public sealed class MetricHistory
{
    private readonly Dictionary<string,Queue<(DateTime Time,double? Value)>> samples=new();
    private readonly Dictionary<string,DateTime> lastSeen=new();
    public void Add(MetricSnapshot snapshot)
    {
        var present=snapshot.Values.Select(v=>v.Id).ToHashSet();
        foreach(var v in snapshot.Values.Take(4096).DistinctBy(v=>v.Id)){if(!samples.TryGetValue(v.Id,out var q)){if(samples.Count>=4096){string oldest=lastSeen.MinBy(p=>p.Value).Key;samples.Remove(oldest);lastSeen.Remove(oldest);}samples[v.Id]=q=new();}q.Enqueue((snapshot.Timestamp,v.Value));lastSeen[v.Id]=snapshot.Timestamp;}
        foreach(var id in samples.Keys.ToArray()){var q=samples[id];if(!present.Contains(id))q.Enqueue((snapshot.Timestamp,null));while(q.Count>240||q.Count>0&&snapshot.Timestamp-q.Peek().Time>TimeSpan.FromMinutes(2))q.Dequeue();if(snapshot.Timestamp-lastSeen[id]>TimeSpan.FromMinutes(2)){samples.Remove(id);lastSeen.Remove(id);}}
    }
    public double?[] Get(string id)=>samples.TryGetValue(id,out var q)?q.Select(s=>s.Value).ToArray():Array.Empty<double?>();
    public void Clear(){samples.Clear();lastSeen.Clear();}
}
public static class MetricText
{
    public static string Label(MetricValue metric)
    {
        string device=metric.Group.StartsWith("GPU",StringComparison.OrdinalIgnoreCase)?"GPU":metric.Group.StartsWith("CPU",StringComparison.OrdinalIgnoreCase)?"CPU":"";
        string kind=metric.Unit switch{"°C"=>"температура","МГц" or "Гц"=>"частота","%"=>"загрузка","Вт"=>"мощность","об/мин"=>"вентилятор",_=>""};
        string name=metric.Name;
        if(device.Length==0)return name;
        if(name is "GPU Core" or "CPU Package" or "CPU Total")return device+" "+(kind.Length>0?kind:name);
        return (name.StartsWith(device,StringComparison.OrdinalIgnoreCase)?"":device+" · ")+name+(kind.Length>0&&!name.Contains(kind,StringComparison.OrdinalIgnoreCase)?" · "+kind:"");
    }
    public static string Format(double? value,string unit,bool showUnit=true)
    {
        if(value is not { } v||!double.IsFinite(v))return "—";
        string digits=unit is "МГц" or "об/мин"?"0":Math.Abs(v)>=100?"0":"0.0";
        return v.ToString(digits,CultureInfo.CurrentCulture)+(showUnit&&unit.Length>0?" "+unit:"");
    }
}
