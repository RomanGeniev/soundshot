using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace GraphiteCompanion.Controls;

public static class PictureLoader
{
    public const string Filter="Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.webp";
    public static BitmapImage Load(string path,int maxSide=1024)
    {
        using var stream=File.OpenRead(path);
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
        var frame=decoder.Frames[0];bool wide=frame.PixelWidth>=frame.PixelHeight;
        int longest=Math.Max(frame.PixelWidth,frame.PixelHeight);stream.Position=0;
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
        if(longest>maxSide){if(wide)image.DecodePixelWidth=maxSide;else image.DecodePixelHeight=maxSide;}
        image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
}

// A brush layer has no intrinsic desired size, so a large image cannot resize a window.
public sealed class BackgroundPicture : Border
{
    private string? loadedPath;
    public BackgroundPicture(){IsHitTestVisible=false;}
    public void Apply(string? path,double opacity)
    {
        Opacity=double.IsFinite(opacity)?Math.Clamp(opacity,0,1):.35;
        if(loadedPath==path)return;loadedPath=path;Background=null;
        if(string.IsNullOrEmpty(path))return;
        try{Background=new ImageBrush(PictureLoader.Load(path,2560)){Stretch=Stretch.UniformToFill};}
        catch(Exception){/* Missing pictures fall back to the chosen background colour. */}
    }
}

public sealed class BackgroundImageSettings : StackPanel
{
    private readonly Func<string?> readPath;
    private readonly Func<double> readOpacity;
    private readonly Action<string,double> apply;
    private readonly TextBlock name=new(){TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,2,0,8)};
    private readonly Slider transparency=new(){Minimum=0,Maximum=100,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,8,0,0)};
    private readonly TextBlock value=new();
    private readonly Button remove;
    private bool syncing;
    public BackgroundImageSettings(Func<string?> readPath,Func<double> readOpacity,Action<string,double> apply)
    {
        this.readPath=readPath;this.readOpacity=readOpacity;this.apply=apply;Margin=new Thickness(0,14,0,8);
        Children.Add(new TextBlock{Text="Картинка на фоне",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        var buttons=new WrapPanel();buttons.Children.Add(SnapBoard.Button("Выбрать картинку…",Choose));remove=SnapBoard.Button("Убрать",()=>{apply("",readOpacity());Refresh();});buttons.Children.Add(remove);Children.Add(buttons);
        name.SetResourceReference(TextBlock.ForegroundProperty,"SubTextBrush");Children.Add(name);
        var row=new DockPanel();DockPanel.SetDock(value,Dock.Right);row.Children.Add(value);row.Children.Add(new TextBlock{Text="Прозрачность картинки"});Children.Add(row);
        transparency.SetResourceReference(StyleProperty,"VolumeSlider");AutomationProperties.SetName(transparency,"Прозрачность фоновой картинки");Children.Add(transparency);
        transparency.ValueChanged+=(_,_)=>{if(syncing)return;value.Text=$"{transparency.Value:0}%";apply(readPath()??"",1-transparency.Value/100);};Refresh();
    }
    public void Refresh()
    {
        syncing=true;var path=readPath()??"";name.Text=path.Length==0?"Картинка не выбрана":File.Exists(path)?Path.GetFileName(path):"Файл недоступен — выберите другую картинку";
        remove.IsEnabled=transparency.IsEnabled=path.Length>0;transparency.Value=(1-Math.Clamp(readOpacity(),0,1))*100;value.Text=$"{transparency.Value:0}%";syncing=false;
    }
    private void Choose()
    {
        var picker=new OpenFileDialog{Title="Картинка на фоне",Filter=PictureLoader.Filter,CheckFileExists=true};
        if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        try{PictureLoader.Load(picker.FileName,2560);apply(picker.FileName,readOpacity());Refresh();}
        catch(Exception){name.Text="Не удалось прочитать картинку. Выберите другой файл.";}
    }
}
