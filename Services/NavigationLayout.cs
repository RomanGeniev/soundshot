using System.Collections.Generic;
using System.Linq;
namespace GraphiteCompanion.Services;

public sealed class NavigationItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
}
public static class NavigationLayout
{
    public static readonly (string Id,string Name)[] Catalog = {("Eq","Звук"),("Mic","Микрофон"),("Monitoring","Мониторинг"),("Record","Запись экрана"),("Gallery","Галерея"),("AutoClicker","Автокликер"),("Effects","Эффекты"),("Settings","Настройки")};
    public static List<NavigationItem> Normalize(IEnumerable<NavigationItem>? source)
    {
        var result=(source??Enumerable.Empty<NavigationItem>()).Where(x=>x!=null&&Catalog.Any(c=>c.Id==x.Id)).DistinctBy(x=>x.Id)
            .Select(x=>new NavigationItem{Id=x.Id,Name=Clean(x.Name,Catalog.First(c=>c.Id==x.Id).Name),Visible=x.Visible}).ToList();
        foreach(var (id,name) in Catalog)if(!result.Any(x=>x.Id==id)) {
            var item=new NavigationItem{Id=id,Name=name};
            int beforeRecord=id=="Monitoring"?result.FindIndex(x=>x.Id=="Record"):-1;
            if(beforeRecord>=0)result.Insert(beforeRecord,item);else result.Add(item);
        }
        return result;
    }
    private static string Clean(string? text,string fallback) { var value=new string((text??"").Where(c=>!char.IsControl(c)).ToArray()).Trim();return value.Length==0?fallback:value.Length>32?value[..32]:value; }
}
