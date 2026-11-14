using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GraphiteCompanion.Controls;

internal static class MonitoringHelp
{
    public static void Show(Window? owner)
    {
        var content=new StackPanel{Margin=new Thickness(22)};
        var window=Dialog(owner,"Мониторинг · справка",content);
        void Section(string title,string text){content.Children.Add(MonitorUi.Text(title,17));content.Children.Add(MonitorUi.Text(text,13));}
        Section("Панель на экране","Выберите показатели в «Настроить панель». Можно менять подписи, порядок, размер, цвет, графики и монитор. В режиме размещения перетащите панель, нажмите Enter для сохранения или Esc для отмены. Ctrl + Alt + O включает и скрывает метрики; сочетание можно изменить в настройках горячих клавиш.");
        Section("Полноэкранные игры · RTSS","Для эксклюзивного полноэкранного режима установите и запустите RivaTuner Statistics Server (RTSS), затем выберите «В игре через RTSS». В RTSS включите On-Screen Display и профиль игры. Состав и цвета строк задаются в SoundShot, размер и положение — в RTSS. Графики панели SoundShot здесь заменяются числовыми значениями. Некоторые игры ограничивают сторонние оверлеи. Полная панель управления SoundShot рассчитана на рабочий стол и оконный режим без рамки.");
        content.Children.Add(MonitorUi.Button("Страница загрузки RTSS ↗",()=>{try{Process.Start(new ProcessStartInfo("https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/"){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(window,ex.Message,"Не удалось открыть страницу");}}));
        Section("FPS и доступность датчиков","Включите «Измерять FPS игры» и выберите её процесс. При доступном RTSS SoundShot получает FPS и время кадра из него; иначе используется PresentMon. Если Windows запрещает сбор кадров, статус объяснит причину — автоматического повышения прав нет. 1% low доступен с PresentMon по последним 10 секундам. Прочерк означает недоступное или устаревшее значение. Температуры, обороты, мощность и другие датчики зависят от оборудования, драйверов и прав доступа.");
        Section("Нагрузка и история","По умолчанию обновление раз в секунду. История ограничена двумя минутами. Сборщик общий для приложения, панели и журналов и останавливается, когда они не нужны. FPS выключен по умолчанию. SoundShot не меняет напряжение, частоты и вентиляторы.");
        Section("Журнал для отладки","«Начать журнал…» создаёт отдельную папку в выбранном месте. session.json содержит версию, среду и настройки; metrics-*.jsonl — все доступные датчики, единицы, минимумы/максимумы, UTC и монотонное время, длительность опроса, статусы, изменения конфигурации и пропуски записи. Файлы делятся по 20 МБ, при общем лимите запись останавливается. Остановка журнала завершает запись. CSV сохраняет только текущий снимок значений. Перед отправкой журналов учитывайте, что в них есть названия оборудования и идентификатор выбранного процесса.");
        content.Children.Add(MonitorUi.Button("Лицензии компонентов…",()=>Licenses(window)));
        window.Show();
    }
    private static Window Dialog(Window? owner,string title,UIElement content)
    {
        var window=new Window{Owner=owner,Title=title,Width=650,Height=620,MinWidth=380,MinHeight=300,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        window.SetResourceReference(Window.BackgroundProperty,"BgBrush");window.SetResourceReference(Window.ForegroundProperty,"TextBrush");return window;
    }
    private static void Licenses(Window owner)
    {
        var assembly=Assembly.GetExecutingAssembly();var names=assembly.GetManifestResourceNames().Where(n=>n.Contains(".Licenses.")&&n.EndsWith(".txt")).OrderBy(n=>n).ToArray();
        var root=new DockPanel{Margin=new Thickness(16)};var choices=new ComboBox{ItemsSource=names,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(choices,Dock.Top);root.Children.Add(choices);
        var text=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas"),FontSize=12};root.Children.Add(text);
        choices.SelectionChanged+=(_,_)=>{if(choices.SelectedItem is string name){using var stream=assembly.GetManifestResourceStream(name);if(stream!=null){using var reader=new StreamReader(stream);text.Text=reader.ReadToEnd();text.ScrollToHome();}}};
        var window=Dialog(owner,"Лицензии компонентов",root);window.Content=root;choices.SelectedIndex=0;window.Show();
    }
}
