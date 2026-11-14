using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GraphiteCompanion.Services;
using Microsoft.Win32;
namespace GraphiteCompanion.Controls;

public sealed partial class OverlayWindow
{
    private readonly Dictionary<string,CanvasCard> attachmentCards=new();
    private CanvasCard AttachmentCard(OverlayAttachment item)
    {
        string id=item.Id;
        if(attachmentCards.TryGetValue(id,out var cached)){((OverlayAttachmentCard)cached.Element).Refresh();return cached;}
        var content=new OverlayAttachmentCard(()=>layout.Attachments.FirstOrDefault(x=>x.Id==id),()=>IsLayoutEditing,()=>overlayBoard.RemoveCard(id));
        return attachmentCards[id]=new(id,item.Name,content,item.Kind=="note"?1:2){Optional=true,Options=content.Options,InsideLabel=item.Kind=="note"?"Текст заметки":"Картинка"};
    }
    private void SyncAttachments()
    {
        var ids=layout.Attachments.Select(x=>x.Id).ToHashSet();foreach(var id in attachmentCards.Keys.Where(id=>!ids.Contains(id)).ToArray())attachmentCards.Remove(id);
        overlayBoard.SyncOptionalCards(layout.Attachments.Select(AttachmentCard));
    }
    private void AddNote()
    {
        if(!IsLayoutEditing)return;
        var item=new OverlayAttachment{Kind="note",Name=NextAttachmentName("note")};layout.Attachments.Add(item);overlayBoard.AddCards(new[]{AttachmentCard(item)});
    }
    private string NextAttachmentName(string kind)
    {
        string prefix=kind=="note"?"Заметка ":"Картинка ";int number=1;
        var names=layout.Attachments.Select(x=>x.Name).ToHashSet();while(names.Contains(prefix+number))number++;return prefix+number;
    }
    private void AddPictures()
    {
        if(!IsLayoutEditing)return;
        var picker=new OpenFileDialog{Title="Добавить картинки в оверлей",Filter=PictureLoader.Filter,Multiselect=true,CheckFileExists=true};if(picker.ShowDialog(overlayBoard.EditorWindow??this)!=true)return;
        var additions=new List<CanvasCard>();int failed=0;
        foreach(var path in picker.FileNames){try{PictureLoader.Load(path);var item=new OverlayAttachment{Kind="image",Name=NextAttachmentName("image"),ImagePath=path};layout.Attachments.Add(item);additions.Add(AttachmentCard(item));}catch(Exception){failed++;}}
        overlayBoard.AddCards(additions);
        if(failed>0)MessageBox.Show(overlayBoard.EditorWindow??this,$"Не удалось прочитать файлов: {failed}. Остальные картинки добавлены.","Картинки",MessageBoxButton.OK,MessageBoxImage.Information);
    }
    private void PruneAttachments()
    {
        var ids=overlayBoard.Value.Blocks.Select(x=>x.Id).ToHashSet();layout.Attachments.RemoveAll(x=>!ids.Contains(x.Id));
        SyncAttachments();overlayBoard.Apply(Composition());
    }
}
