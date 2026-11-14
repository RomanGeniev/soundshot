using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using GraphiteCompanion.Services;
using Microsoft.Win32;
namespace GraphiteCompanion.Controls;

public sealed class OverlayAttachmentCard : StackPanel
{
    private readonly Func<OverlayAttachment?> read;
    private readonly Func<bool> editing;
    private readonly TextBlock text=new(){TextWrapping=TextWrapping.Wrap,FontSize=15};
    private readonly Image image=new(){MaxHeight=280,Stretch=Stretch.Uniform};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBox? input;
    private string? loadedPath;
    private bool syncing;
    public FrameworkElement Options { get; }
    public OverlayAttachmentCard(Func<OverlayAttachment?> read,Func<bool> editing,Action remove)
    {
        this.read=read;this.editing=editing;var item=read()!;
        var title=new TextBlock{Text=item.Name,FontSize=11,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)};title.SetResourceReference(TextBlock.ForegroundProperty,"SubTextBrush");Children.Add(title);
        text.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");status.SetResourceReference(TextBlock.ForegroundProperty,"SubTextBrush");
        var options=new StackPanel{Margin=new Thickness(0,8,0,0)};options.Children.Add(SnapBoard.Text(item.Name));
        if(item.Kind=="note"){
            Children.Add(text);input=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=80,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};AutomationProperties.SetName(input,"Текст "+item.Name);
            input.TextChanged+=(_,_)=>{if(syncing||!editing()||read() is not {} current)return;current.Text=input.Text;Refresh();};options.Children.Add(input);
        }else{
            Children.Add(image);Children.Add(status);
            options.Children.Add(SnapBoard.Button("Заменить картинку…",()=>{
                if(!editing())return;var picker=new OpenFileDialog{Title="Заменить картинку",Filter=PictureLoader.Filter,CheckFileExists=true};
                if(picker.ShowDialog(Window.GetWindow(options)??Window.GetWindow(this))!=true)return;
                try{PictureLoader.Load(picker.FileName);if(read() is {} current){current.ImagePath=picker.FileName;loadedPath=null;Refresh();}}
                catch(Exception){status.Text="Не удалось прочитать картинку";status.Visibility=Visibility.Visible;}
            }));
        }
        options.Children.Add(SnapBoard.Button(item.Kind=="note"?"Удалить заметку":"Удалить картинку",remove));Options=options;Refresh();
    }
    public void Refresh()
    {
        var item=read();if(item==null)return;
        text.Text=string.IsNullOrWhiteSpace(item.Text)?"Напишите свою заметку…":item.Text;
        if(input!=null&&input.Text!=item.Text){syncing=true;input.Text=item.Text;syncing=false;}
        if(item.Kind!="image"||loadedPath==item.ImagePath)return;
        loadedPath=item.ImagePath;image.Source=null;
        try{image.Source=PictureLoader.Load(item.ImagePath);}
        catch(Exception){status.Text="Картинка недоступна. Выберите другую в содержимом блока.";}
        status.Visibility=image.Source==null?Visibility.Visible:Visibility.Collapsed;
    }
}
