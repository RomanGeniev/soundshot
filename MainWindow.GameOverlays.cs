using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GraphiteCompanion.Controls;
using GraphiteCompanion.Services;

namespace GraphiteCompanion;

public partial class MainWindow
{
    private Dictionary<string, GameOverlayWindow>? gameWindows;
    private Window? GameDialog => gameWindows?.Values.SelectMany(w => w.OwnedWindows.Cast<Window>()).FirstOrDefault(w => w.IsVisible);
    private bool ActivateGameDialog()
    {
        var dialog = GameDialog;
        if (dialog == null) return false;
        dialog.Activate();
        return true;
    }
    public void OpenGameOverlay(string game)
    {
        if (shutdownStarted || game is not ("twentyOne" or "roulette" or "random")) return;
        gameWindows ??= new();
        if (gameWindows.TryGetValue(game, out var existing)) { existing.BringForward(); return; }
        var placement = settings.GameOverlays != null && settings.GameOverlays.TryGetValue(game, out var saved) ? saved : null;
        var window = new GameOverlayWindow(game, OverlayGames, placement, next =>
        {
            var previous = settings.GameOverlays;
            settings.GameOverlays = new(previous ?? new()) { [game] = next.Copy() };
            if (SettingsStore.TrySave(settings, out var error)) return null;
            settings.GameOverlays = previous;
            return error ?? "Не удалось сохранить положение";
        });
        gameWindows[game] = window;
        window.Closed += (_, _) => gameWindows.Remove(game);
        window.Show();
    }
}
