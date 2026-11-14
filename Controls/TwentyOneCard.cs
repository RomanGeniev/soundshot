using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GraphiteCompanion.Services;

namespace GraphiteCompanion.Controls;

public sealed class TwentyOneCard : Border
{
    private readonly GameWallet wallet;
    private readonly GameBetBar bet;
    private readonly StackPanel idle=new(),hands=new();
    private readonly WrapPanel dealer=new(),player=new(),actions=new();
    private readonly TextBlock dealerTitle=GameUi.Text(""),playerTitle=GameUi.Text(""),result=GameUi.Text("",13);
    private readonly Button hit,stand,deal;
    private string shownHand="";
    public TwentyOneCard(GameWallet value)
    {
        wallet=value;Padding=new Thickness(14);CornerRadius=new CornerRadius(14);SetResourceReference(BackgroundProperty,"InsetBrush");
        var body=new StackPanel();body.Children.Add(GameUi.Header(this,"21",wallet,"Наберите больше дилера, но не больше 21. Туз — 1 или 11, картинки — 10. Дилер останавливается на 17, включая мягкие 17.\n\nПобеда: возврат ставки ×2. Ничья: возврат ставки. 21 с первых двух карт оплачивается так же. Незаконченная партия сохраняется. Баланс общий с рулеткой."));
        var backs=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,5,0,10)};backs.Children.Add(Card(new(1,0),false));backs.Children.Add(Card(new(13,1),false));idle.Children.Add(backs);
        var welcome=GameUi.Text("Соберите 21",18);welcome.FontWeight=FontWeights.SemiBold;welcome.TextAlignment=TextAlignment.Center;idle.Children.Add(welcome);var intro=GameUi.Text("Берите карты или остановитесь вовремя.\nВыберите ставку и начните партию.",12,true);intro.TextAlignment=TextAlignment.Center;intro.Margin=new Thickness(0,5,0,0);idle.Children.Add(intro);
        dealerTitle.Margin=new Thickness(0,0,0,7);playerTitle.Margin=new Thickness(0,0,0,7);var handGrid=new Grid();handGrid.ColumnDefinitions.Add(new());handGrid.ColumnDefinitions.Add(new(){Width=new GridLength(12)});handGrid.ColumnDefinitions.Add(new());var left=new StackPanel();left.Children.Add(dealerTitle);left.Children.Add(dealer);handGrid.Children.Add(left);var right=new StackPanel();right.Children.Add(playerTitle);right.Children.Add(player);Grid.SetColumn(right,2);handGrid.Children.Add(right);hands.Children.Add(handGrid);
        var table=new Grid{MinHeight=158};table.Children.Add(idle);table.Children.Add(hands);var tableBox=new Border{Child=table,Padding=new Thickness(12),CornerRadius=new CornerRadius(10)};tableBox.SetResourceReference(BackgroundProperty,"CardBrush");body.Children.Add(tableBox);
        result.Margin=new Thickness(0,10,0,0);result.MinHeight=32;result.FontWeight=FontWeights.SemiBold;body.Children.Add(result);
        bet=new(wallet);body.Children.Add(bet);bet.Changed+=Refresh;
        hit=GameUi.Button("Ещё карту",()=>wallet.Play(true),true);stand=GameUi.Button("Хватит",()=>wallet.Play(false));deal=GameUi.Button("Начать игру",()=>wallet.Deal(bet.Stake),true);actions.Children.Add(hit);actions.Children.Add(stand);actions.Children.Add(deal);body.Children.Add(actions);Child=body;
        Loaded+=(_,_)=>{wallet.Changed+=Refresh;Refresh();};Unloaded+=(_,_)=>wallet.Changed-=Refresh;Refresh();
    }
    private static Border Card(PlayingCard c,bool hidden)
    {
        var body=new Grid();if(hidden){var mark=new TextBlock{Text="◆",FontSize=27,Foreground=new SolidColorBrush(Color.FromRgb(158,195,223)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};body.Children.Add(mark);}
        else{var brush=c.Red?new SolidColorBrush(Color.FromRgb(185,47,68)):new SolidColorBrush(Color.FromRgb(27,37,51));body.Children.Add(new TextBlock{Text=c.Face[..^1],FontSize=15,FontWeight=FontWeights.SemiBold,Foreground=brush,Margin=new Thickness(6,3,0,0)});body.Children.Add(new TextBlock{Text=c.Face[^1..],FontSize=27,Foreground=brush,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,4)});}
        return new Border{Width=43,Height=64,Margin=new Thickness(0,0,5,5),CornerRadius=new CornerRadius(7),Background=hidden?new SolidColorBrush(Color.FromRgb(30,66,94)):Brushes.WhiteSmoke,BorderBrush=new SolidColorBrush(Color.FromArgb(65,124,159,184)),BorderThickness=new Thickness(1),Child=body};
    }
    private void Refresh()
    {
        var game=wallet.TwentyOne;bool empty=game.Player.Count==0;idle.Visibility=empty?Visibility.Visible:Visibility.Collapsed;hands.Visibility=empty?Visibility.Collapsed:Visibility.Visible;
        string key=string.Join(",",game.Player)+"/"+string.Join(",",game.Dealer)+game.Playing;
        if(shownHand!=key){shownHand=key;dealer.Children.Clear();player.Children.Clear();for(int i=0;i<game.Dealer.Count;i++)dealer.Children.Add(Card(game.Dealer[i],game.Playing&&i==1));foreach(var card in game.Player)player.Children.Add(Card(card,false));}
        dealerTitle.Text="Дилер  ·  "+(game.Playing?"?":TwentyOneGame.Total(game.Dealer).ToString());playerTitle.Text="Вы  ·  "+TwentyOneGame.Total(game.Player);playerTitle.FontWeight=FontWeights.SemiBold;
        var last=wallet.Snapshot.History.LastOrDefault(r=>r.Game=="21");
        result.Text=wallet.Error??(empty?"":game.Playing?$"Ваш ход · в игре {wallet.TwentyOneStake} монет":game.Result+(last==null?"":"  ·  "+Delta(last)));
        result.SetResourceReference(TextBlock.ForegroundProperty,game.Playing||empty?"SubTextBrush":"TextBrush");
        hit.Visibility=stand.Visibility=game.Playing?Visibility.Visible:Visibility.Collapsed;deal.Visibility=game.Playing?Visibility.Collapsed:Visibility.Visible;bet.Locked=game.Playing;deal.Content=(empty?"Начать игру":"Новая партия")+$" · {bet.Stake:N0}";deal.IsEnabled=bet.CanBet;
    }
    internal static string Delta(GameRound r){long net=r.Returned-r.Stake;return (net>0?"+":"")+net.ToString("N0")+" монет";}
}
