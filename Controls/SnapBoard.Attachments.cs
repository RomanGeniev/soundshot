using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using GraphiteCompanion.Services;
namespace GraphiteCompanion.Controls;

public sealed partial class SnapBoard
{
    // Keep registered controls until the editing transaction ends: Undo restores
    // block membership without losing its content or event handlers.
    public void AddCards(IEnumerable<CanvasCard> additions)
    {
        if(!Editing)return;
        var added=additions.ToArray();if(added.Length==0)return;
        BeginOperation();
        foreach(var card in added){if(tiles.ContainsKey(card.Id))continue;CanvasParts.Enrich(card);cards.Add(card);AddTile(card);state.Blocks.Add(new(){Id=card.Id,Zone=card.Zone,Visible=true});}
        Render();EndOperation();
        var last=added[^1];tiles[last.Id].BringIntoView();
        if(added.Length==1&&last.Options!=null)Enter(last.Id);
    }
    public void RemoveCard(string id)
    {
        if(!Editing||!cards.Any(c=>c.Id==id&&c.Optional))return;
        BeginOperation();state.Blocks.RemoveAll(x=>x.Id==id);options.Children.Clear();Render();EndOperation();
    }
    public void SyncOptionalCards(IEnumerable<CanvasCard> replacements)
    {
        if(Editing)throw new InvalidOperationException("Finish editing before synchronizing cards.");
        var next=replacements.ToDictionary(c=>c.Id);
        foreach(var card in cards.Where(c=>c.Optional).ToArray()){
            if(next.TryGetValue(card.Id,out var same)&&ReferenceEquals(card,same))continue;
            var tile=tiles[card.Id];if(tile.Parent is Panel parent)parent.Children.Remove(tile);tile.Detach();tiles.Remove(card.Id);cards.Remove(card);
        }
        foreach(var card in next.Values){if(tiles.ContainsKey(card.Id))continue;CanvasParts.Enrich(card);cards.Add(card);AddTile(card);}
    }
}
