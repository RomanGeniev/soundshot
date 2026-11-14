using System;
using System.Windows;
using System.Windows.Controls;
using GraphiteCompanion.Services;

namespace GraphiteCompanion.Controls;

public sealed class GameBetBar : StackPanel
{
    private readonly GameWallet wallet;
    private readonly Button amount,minus,plus;
    private readonly WrapPanel chips=new();
    private readonly TextBlock hint=GameUi.Text("",11,true);
    private long stake=10;
    private bool locked;
    public event Action? Changed;
    public long Stake=>stake;
    public bool CanBet=>!locked&&stake<=wallet.Balance&&wallet.Balance>0;
    public bool Locked {set{locked=value;Refresh();}}
    public GameBetBar(GameWallet value)
    {
        wallet=value;Margin=new Thickness(0,10,0,4);
        var top=new Grid();top.ColumnDefinitions.Add(new());top.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var label=GameUi.Text("СТАВКА",10,true);label.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(label);
        var stepper=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(stepper,1);top.Children.Add(stepper);
        minus=GameUi.Button("−",()=>Set(stake-Step()));minus.Width=32;minus.Padding=new Thickness(0);
        amount=GameUi.Button("10",Edit);amount.MinWidth=78;amount.Margin=new Thickness(4,0,4,0);
        plus=GameUi.Button("+",()=>Set(stake+Step()));plus.Width=32;plus.Padding=new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(minus,"Уменьшить ставку");System.Windows.Automation.AutomationProperties.SetName(plus,"Увеличить ставку");
        stepper.Children.Add(minus);stepper.Children.Add(amount);stepper.Children.Add(plus);Children.Add(top);
        chips.Margin=new Thickness(0,7,0,0);foreach(long n in new long[]{10,25,50,100}){long selected=n;var chip=GameUi.Button(n.ToString(),()=>Set(selected));chip.Tag=n;chip.MinWidth=43;chip.Margin=new Thickness(0,0,5,0);chips.Children.Add(chip);}
        var all=GameUi.Button("Все",()=>Set(Math.Clamp(wallet.Balance,1,1_000_000)));all.Tag=-1L;all.MinWidth=46;chips.Children.Add(all);Children.Add(chips);hint.MinHeight=15;hint.Margin=new Thickness(0,4,0,0);Children.Add(hint);
        Loaded+=(_,_)=>{wallet.Changed+=WalletChanged;Refresh();};Unloaded+=(_,_)=>wallet.Changed-=WalletChanged;Refresh();
    }
    private long Step()=>stake<10?1:stake<100?5:stake<1000?25:100;
    private void WalletChanged(){Refresh();Changed?.Invoke();}
    private void Set(long value){if(locked)return;stake=Math.Clamp(value,1,1_000_000);Refresh();Changed?.Invoke();}
    public void Refresh()
    {
        amount.Content=stake.ToString("N0");System.Windows.Automation.AutomationProperties.SetName(amount,$"Ставка {stake} монет. Ввести свою сумму");amount.IsEnabled=!locked;minus.IsEnabled=!locked&&stake>1;plus.IsEnabled=!locked&&stake<1_000_000;
        foreach(Button chip in chips.Children){long n=(long)chip.Tag;chip.IsEnabled=!locked&&(n==-1?wallet.Balance>0:n<=wallet.Balance);GameUi.Select(chip,n==stake||n==-1&&stake==wallet.Balance);}
        hint.Text=locked?"Ставка принята":wallet.Balance==0?"Новый старт доступен через баланс":stake>wallet.Balance?$"Не хватает {stake-wallet.Balance:N0} монет":"Нажмите сумму, чтобы ввести свою";
        hint.SetResourceReference(TextBlock.ForegroundProperty,!locked&&stake>wallet.Balance?"AccentBrush":"SubTextBrush");
    }
    private void Edit()
    {
        var body=new StackPanel();var dialog=GameUi.Dialog(this,"Размер ставки",body,350);
        body.Children.Add(GameUi.Text($"Доступно {wallet.Balance:N0} монет",13,true));var input=new TextBox{Text=stake.ToString(),FontSize=24,MaxLength=7,Margin=new Thickness(0,12,0,8),Padding=new Thickness(10)};System.Windows.Automation.AutomationProperties.SetName(input,"Сумма ставки");body.Children.Add(input);var error=GameUi.Text("От 1 до 1 000 000 монет",12,true);body.Children.Add(error);
        void Accept(){if(!long.TryParse(input.Text,out long n)||n<1||n>1_000_000||n>wallet.Balance){error.Text="Введите сумму от 1 до доступного баланса (не больше 1 000 000).";return;}Set(n);dialog.Close();}
        var actions=new WrapPanel{Margin=new Thickness(0,12,0,0)};actions.Children.Add(GameUi.Button("Применить",Accept,true));actions.Children.Add(GameUi.Button("Отмена",dialog.Close));body.Children.Add(actions);
        input.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Accept();e.Handled=true;}};dialog.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};dialog.ShowDialog();
    }
}

