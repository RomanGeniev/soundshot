using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GraphiteCompanion.Services;

namespace GraphiteCompanion.Controls;

public sealed class RouletteCard : Border,IDisposable
{
    private readonly OverlayGamesState state;
    private readonly RouletteWheel wheel=new(){Width=150,Height=150};
    private readonly TextBlock number=GameUi.Text("—",36),description=GameUi.Text("Результат вращения",12,true),outcomeText=GameUi.Text("",13),selection=GameUi.Text("",11,true);
    private readonly WrapPanel history=new();
    private readonly Button spin,numbersToggle;
    private readonly GameBetBar bet;
    private readonly UniformGrid numberGrid=new(){Columns=10,Visibility=Visibility.Collapsed,Margin=new Thickness(0,5,0,0)};
    private readonly Dictionary<string,Button> kinds=new();
    private readonly Dictionary<int,Button> numbers=new();
    private string kind="red";
    private int chosenNumber;
    private bool spinning,finishing;
    public RouletteCard(OverlayGamesState value)
    {
        state=value;Padding=new Thickness(14);CornerRadius=new CornerRadius(14);SetResourceReference(BackgroundProperty,"InsetBrush");var root=new StackPanel();root.Children.Add(GameUi.Header(this,"Рулетка",state.Wallet,"Выберите поле, сумму и нажмите «Крутить». Все 37 секторов равновероятны.\n\nЦвет, чётность и диапазон: возврат ставки ×2. Одно число: ×36. Коэффициент включает исходную ставку. Зеро проигрывает на всех полях, кроме ставки на число 0.\n\nПринятое вращение сохраняет свой результат даже при закрытии оверлея. Баланс общий с «21»."));
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.Children.Add(wheel);var info=new StackPanel{Margin=new Thickness(16,0,0,0),VerticalAlignment=VerticalAlignment.Center};info.Children.Add(number);description.Margin=new Thickness(0,3,0,7);info.Children.Add(description);outcomeText.FontWeight=FontWeights.SemiBold;outcomeText.MinHeight=36;info.Children.Add(outcomeText);Grid.SetColumn(info,1);row.Children.Add(info);root.Children.Add(row);
        var choices=new UniformGrid{Columns=3,Margin=new Thickness(0,10,0,0)};
        foreach(var (id,label) in new[]{("red","Красное"),("even","Чётное"),("low","1–18"),("black","Чёрное"),("odd","Нечётное"),("high","19–36")}){string key=id;var b=GameUi.Button(label,()=>{kind=key;RefreshSelection();});b.Margin=new Thickness(0,0,5,5);b.Padding=new Thickness(3,6,3,6);b.FontSize=12;kinds[id]=b;choices.Children.Add(b);}root.Children.Add(choices);
        numbersToggle=GameUi.Button("Одно число  ▾",()=>{numberGrid.Visibility=numberGrid.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;RefreshSelection();});numbersToggle.HorizontalAlignment=HorizontalAlignment.Left;numbersToggle.Padding=new Thickness(8,4,8,4);root.Children.Add(numbersToggle);
        for(int n=0;n<=36;n++){int selected=n;var b=GameUi.Button(n.ToString(),()=>{kind="number";chosenNumber=selected;RefreshSelection();});b.Padding=new Thickness(0);b.MinHeight=27;b.FontSize=11;b.Margin=new Thickness(0,0,3,3);numbers[n]=b;numberGrid.Children.Add(b);}root.Children.Add(numberGrid);
        selection.Margin=new Thickness(0,6,0,0);root.Children.Add(selection);bet=new(state.Wallet);bet.Changed+=RefreshActions;root.Children.Add(bet);spin=GameUi.Button("Крутить",Spin,true);spin.HorizontalAlignment=HorizontalAlignment.Stretch;spin.Margin=new Thickness(0);root.Children.Add(spin);
        history.Margin=new Thickness(0,10,0,0);root.Children.Add(history);Child=root;
        Loaded+=(_,_)=>{state.Wallet.Changed+=WalletChanged;if(state.Wallet.PendingRoulette!=null&&!spinning)Finish();Refresh();};Unloaded+=(_,_)=>{state.Wallet.Changed-=WalletChanged;Finish();};Refresh();
    }
    private string Choice=>kind switch{"red"=>"Красное","black"=>"Чёрное","even"=>"Чётное","odd"=>"Нечётное","low"=>"1–18","high"=>"19–36",_=>"Число "+chosenNumber};
    private void RefreshSelection(){foreach(var p in kinds)GameUi.Select(p.Value,kind==p.Key);foreach(var p in numbers)GameUi.Select(p.Value,kind=="number"&&p.Key==chosenNumber);numbersToggle.Content=(kind=="number"?$"Число {chosenNumber}":"Одно число")+(numberGrid.Visibility==Visibility.Visible?"  ▴":"  ▾");selection.Text=Choice+" · возврат "+(kind=="number"?"×36":"×2");}
    private void RefreshActions(){if(spin==null)return;bool pending=state.Wallet.PendingRoulette!=null;bet.Locked=spinning||pending;spin.IsEnabled=!spinning&&(pending||bet.CanBet);spin.Content=spinning?"Вращается…":pending?"Завершить вращение":$"Крутить · {bet.Stake:N0}";foreach(var b in kinds.Values.Concat(numbers.Values))b.IsEnabled=!spinning&&!pending;numbersToggle.IsEnabled=!spinning&&!pending;}
    private void WalletChanged(){if(!spinning&&!finishing)Refresh();else RefreshActions();}
    private void Spin()
    {
        if(spinning)return;if(state.Wallet.PendingRoulette!=null){Finish();return;}spinning=true;
        if(!state.Wallet.Spin(bet.Stake,kind,chosenNumber)){spinning=false;Refresh();return;}
        number.Text="…";description.Text="Колесо вращается";outcomeText.Text="";RefreshActions();wheel.SpinTo(state.Wallet.PendingRoulette!.Outcome,Finish);
    }
    private void Finish(){if(finishing)return;finishing=true;wheel.Stop();spinning=false;state.Wallet.FinishSpin();finishing=false;Refresh();}
    private void Refresh()
    {
        int? last=state.Wallet.LastRoulette;state.RouletteNumber=last;number.Text=last?.ToString()??"—";description.Text=last is not int n?"Результат вращения":n==0?"Зеро":(OverlayRandom.IsRed(n)?"Красное":"Чёрное")+" · "+(n%2==0?"чётное":"нечётное");
        var rounds=state.Wallet.Snapshot.History.Where(r=>r.Game=="Рулетка").ToArray();var round=rounds.LastOrDefault();outcomeText.Text=state.Wallet.Error??(round==null?"":(round.Returned>0?"Выигрыш ":"Проигрыш ")+TwentyOneCard.Delta(round));if(last is int result)wheel.ShowNumber(result);
        history.Children.Clear();if(rounds.Length>0){var label=GameUi.Text("Последние",10,true);label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new Thickness(0,0,8,0);history.Children.Add(label);foreach(var r in rounds.TakeLast(6).Reverse())if(int.TryParse(r.Result.Split(' ')[0],out int v)){var t=new TextBlock{Text=v.ToString(),FontSize=11,Foreground=Brushes.White,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center};history.Children.Add(new Border{Width=25,Height=23,CornerRadius=new CornerRadius(5),Margin=new Thickness(0,0,4,0),Background=new SolidColorBrush(v==0?Color.FromRgb(34,113,79):OverlayRandom.IsRed(v)?Color.FromRgb(174,52,69):Color.FromRgb(46,55,69)),Child=t});}}
        RefreshSelection();RefreshActions();
    }
    public void Dispose(){state.Wallet.Changed-=WalletChanged;Finish();}
}

