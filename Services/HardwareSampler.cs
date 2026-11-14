using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace GraphiteCompanion.Services;

internal sealed class HardwareSampler : IDisposable
{
    private readonly Computer computer=new(){IsCpuEnabled=true,IsGpuEnabled=true,IsMemoryEnabled=true,IsStorageEnabled=true,IsNetworkEnabled=true,IsMotherboardEnabled=true,IsBatteryEnabled=true};
    private readonly FrameMetrics frames=new();
    private bool opened;
    private string fault="";
    private ulong idle,kernel,user;
    private readonly Dictionary<string,(double Min,double Max)> ranges=new();
    public HardwareSampler(){GetSystemTimes(out idle,out kernel,out user);try{computer.Open();opened=true;}catch(Exception ex){fault="Часть датчиков недоступна: "+ex.Message;}}
    public MetricSnapshot Read(MetricRequest request)
    {
        var values=new List<MetricValue>();
        void Add(string id,string group,string name,string unit,double? v)
        {
            if(v.HasValue&&!double.IsFinite(v.Value))v=null;
            if(v is { } n){if(!ranges.ContainsKey(id)&&ranges.Count>=4096)ranges.Remove(ranges.Keys.First());var r=ranges.TryGetValue(id,out var old)?(Math.Min(old.Min,n),Math.Max(old.Max,n)):(n,n);ranges[id]=r;}
            var range=ranges.TryGetValue(id,out var found)?found:((double Min,double Max)? )null;
            values.Add(new(id,group,name,unit,v,range?.Min,range?.Max));
        }
        double? load=null;
        if(GetSystemTimes(out var ni,out var nk,out var nu)){ulong total=nk-kernel+nu-user;if(total>0&&ni>=idle)load=Math.Clamp(100d*(total-(double)(ni-idle))/total,0,100);idle=ni;kernel=nk;user=nu;}
        Add("system/cpu","Система","CPU · общая загрузка","%",load);
        var mem=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        if(GlobalMemoryStatusEx(ref mem)){Add("system/memory/used","Система","Оперативная память · занято","ГБ",(mem.TotalPhysical-mem.AvailablePhysical)/1073741824d);Add("system/memory/load","Система","Оперативная память · загрузка","%",mem.Load);Add("system/memory/total","Система","Оперативная память · всего","ГБ",mem.TotalPhysical/1073741824d);}
        var errors=new List<string>();if(fault.Length>0)errors.Add(fault);
        void Visit(IHardware hardware,string group)
        {
            try{hardware.Update();foreach(var s in hardware.Sensors){string unit=Unit(s.SensorType);double? value=s.Value;if(s.SensorType==SensorType.Throughput)value/=1048576;Add("hw"+s.Identifier.ToString(),group,s.Name,unit,value);}foreach(var sub in hardware.SubHardware)Visit(sub,group+" · "+sub.Name);}
            catch(Exception ex){errors.Add(hardware.Name+": "+ex.Message);}
        }
        if(opened)foreach(var h in computer.Hardware)Visit(h,Group(h.HardwareType)+" · "+h.Name);
        var gpu=opened?computer.Hardware.FirstOrDefault(h=>h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel):null;
        var gpuLoad=gpu?.Sensors.FirstOrDefault(s=>s.SensorType==SensorType.Load&&(s.Name.Contains("Core",StringComparison.OrdinalIgnoreCase)||s.Name.Equals("D3D 3D",StringComparison.OrdinalIgnoreCase)));
        Add("system/gpu/load",gpu==null?"GPU":"GPU · "+gpu.Name,"GPU · загрузка основного ядра","%",gpuLoad?.Value);
        double? rtssRate=null,rtssTime=null;bool rtss=request.Frames&&RtssOutput.ReadFrames(request.ProcessId,request.ProcessStartedTicks,out rtssRate,out rtssTime);
        (double? Fps,double? Time,double? Low) fps;
        if(rtss){frames.Configure(false,0);fps=(rtssRate,rtssTime,null);}
        else{frames.Configure(request.Frames,request.ProcessId,request.ProcessStartedTicks);fps=frames.Read();}
        Add("frames/fps","Игра","FPS","кадр/с",fps.Fps);Add("frames/time","Игра","Время кадра","мс",fps.Time);Add("frames/low","Игра","1% low · последние 10 секунд","кадр/с",fps.Low);
        string note="Данные обновляются. "+(rtss?"FPS через RTSS · 1% low доступен при измерении через PresentMon":frames.Status);
        if(errors.Count>0)note+=" · "+string.Join("; ",errors.Take(3));
        return new(DateTime.UtcNow,values.Take(4096).ToList(),note);
    }
    public static string Unit(SensorType type)=>type switch{SensorType.Temperature=>"°C",SensorType.Load or SensorType.Level or SensorType.Control or SensorType.Humidity=>"%",SensorType.Clock=>"МГц",SensorType.Fan=>"об/мин",SensorType.Voltage=>"В",SensorType.Current=>"А",SensorType.Power=>"Вт",SensorType.Data=>"ГБ",SensorType.SmallData=>"МБ",SensorType.Throughput=>"МБ/с",SensorType.Flow=>"л/ч",SensorType.Frequency=>"Гц",_=>""};
    private static string Group(HardwareType type)=>type switch{HardwareType.Cpu=>"CPU",HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel=>"GPU",HardwareType.Memory=>"Память",HardwareType.Storage=>"Накопители",HardwareType.Network=>"Сеть",HardwareType.Motherboard or HardwareType.SuperIO=>"Плата",HardwareType.Battery=>"Батарея",_=>"Оборудование"};
    public void Dispose(){frames.Dispose();try{computer.Close();}catch{}}
    [DllImport("kernel32.dll")]private static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto,SetLastError=true)]private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    [StructLayout(LayoutKind.Sequential)]private struct MemoryStatus{public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;}
}
