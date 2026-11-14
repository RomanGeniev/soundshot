using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace GraphiteCompanion.Services;

public readonly record struct PlayingCard(int Rank,int Suit)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Face=>(Rank switch{1=>"A",11=>"J",12=>"Q",13=>"K",_=>Rank.ToString()})+"♠♥♦♣"[Suit];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Red=>Suit is 1 or 2;
}
public sealed class TwentyOneGame
{
    private readonly List<PlayingCard> deck=new();
    private readonly List<PlayingCard> player=new(),dealer=new();
    public IReadOnlyList<PlayingCard> Player=>player;
    public IReadOnlyList<PlayingCard> Dealer=>dealer;
    public bool Playing {get;private set;}
    public string Result {get;private set;}="Соберите 21, не превысив сумму";
    public int Outcome {get;private set;} // -1 loss, 0 push, 1 win
    public TwentyOneHand Snapshot()=>new(){Deck=deck.ToList(),Player=player.ToList(),Dealer=dealer.ToList(),Playing=Playing,Result=Result,Outcome=Outcome};
    public void Restore(TwentyOneHand hand){deck.Clear();deck.AddRange(hand.Deck);player.Clear();player.AddRange(hand.Player);dealer.Clear();dealer.AddRange(hand.Dealer);Playing=hand.Playing;Result=hand.Result;Outcome=hand.Outcome;}
    public static int Total(IEnumerable<PlayingCard> cards){var hand=cards.ToArray();int value=hand.Sum(c=>c.Rank==1?11:Math.Min(10,c.Rank)),aces=hand.Count(c=>c.Rank==1);while(value>21&&aces-->0)value-=10;return value;}
    public void Start(){deck.Clear();for(int s=0;s<4;s++)for(int r=1;r<=13;r++)deck.Add(new(r,s));for(int i=deck.Count-1;i>0;i--){int j=RandomNumberGenerator.GetInt32(i+1);(deck[i],deck[j])=(deck[j],deck[i]);}player.Clear();dealer.Clear();player.Add(Draw());dealer.Add(Draw());player.Add(Draw());dealer.Add(Draw());Playing=true;Result="Карту или хватит?";if(Total(player)==21||Total(dealer)==21)Finish();}
    private PlayingCard Draw(){var c=deck[^1];deck.RemoveAt(deck.Count-1);return c;}
    public void Hit(){if(!Playing)return;player.Add(Draw());if(Total(player)>=21){if(Total(player)>21)Finish();else Stand();}}
    public void Stand(){if(!Playing)return;while(Total(dealer)<17)dealer.Add(Draw());Finish();}
    private void Finish(){Playing=false;int p=Total(player),d=Total(dealer);Outcome=p>21?-1:d>21?1:p.CompareTo(d);Result=p>21?"Перебор. Выиграл дилер":d>21?"У дилера перебор. Вы выиграли!":p==d?"Ничья":p>d?"Вы выиграли!":"Выиграл дилер";}
}
public sealed class TwentyOneHand
{
    public List<PlayingCard> Deck {get;set;}=new();
    public List<PlayingCard> Player {get;set;}=new();
    public List<PlayingCard> Dealer {get;set;}=new();
    public bool Playing {get;set;}
    public string Result {get;set;}="";
    public int Outcome {get;set;}
}
public static class OverlayRandom
{
    public static int Number(int minimum,int maximum){if(minimum>maximum)throw new ArgumentException("Число «от» должно быть не больше числа «до».");return (int)Random.Shared.NextInt64(minimum,(long)maximum+1);}
    public static readonly int[] Wheel={0,32,15,19,4,21,2,25,17,34,6,27,13,36,11,30,8,23,10,5,24,16,33,1,20,14,31,9,22,18,29,7,28,12,35,3,26};
    public static bool IsRed(int n)=>new[]{1,3,5,7,9,12,14,16,18,19,21,23,25,27,30,32,34,36}.Contains(n);
}
public sealed class OverlayGamesState
{
    public GameWallet Wallet {get;}
    public TwentyOneGame TwentyOne=>Wallet.TwentyOne;
    public OverlayGamesState(GameWallet? wallet=null){Wallet=wallet??new GameWallet(null,_=>null);}
    public int Minimum {get;set;}=1;
    public int Maximum {get;set;}=100;
    public int? LastNumber {get;set;}
    public int? RouletteNumber {get;set;}
    public Queue<int> RouletteHistory {get;}=new();
}
