using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GraphiteCompanion.Controls;

public sealed partial class OverlayWindow
{
    private readonly Popup gamesPopup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 7 };
    private Button CreateGamesButton()
    {
        var button = GameUi.Button("Игры", () => gamesPopup.IsOpen = !gamesPopup.IsOpen);
        System.Windows.Automation.AutomationProperties.SetName(button, "Открыть отдельное окно игры");
        var menu = new StackPanel();
        foreach (var (id, label) in new[] { ("twentyOne", "21 · карты"), ("roulette", "Рулетка"), ("random", "Случайное число") })
        {
            string game = id;
            var launch = GameUi.Button(label, () => { gamesPopup.IsOpen = false; main.OpenGameOverlay(game); });
            launch.Margin = new Thickness(0, 2, 0, 2);
            menu.Children.Add(launch);
        }
        var frame = new Border { Child = menu, Padding = new Thickness(8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), MinWidth = 190 };
        frame.SetResourceReference(Border.BackgroundProperty, "CardBrush"); frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        gamesPopup.PlacementTarget = button; gamesPopup.Child = frame;
        return button;
    }
}
