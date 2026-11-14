using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace GraphiteCompanion.Services;

public sealed class GameRound
{
    public DateTime Utc {get;set;}=DateTime.UtcNow;
    public string Game {get;set;}="";
    public string Result {get;set;}="";
    public long Stake {get;set;}
    public long Returned {get;set;}
}
public sealed class RouletteRound
{
    public long Stake {get;set;}
    public string Kind {get;set;}="red";
    public int Number {get;set;}
    public int Outcome {get;set;}
}
public sealed class GameWalletState
{
    public long Balance {get;set;}=1000;
    public long TwentyOneStake {get;set;}
    public TwentyOneHand? Hand {get;set;}
    public RouletteRound? Roulette {get;set;}
    public int? LastRoulette {get;set;}
    public List<GameRound> History {get;set;}=new();
    public GameWalletState Copy()=>JsonSerializer.Deserialize<GameWalletState>(JsonSerializer.Serialize(this))!;
}

// A single local wallet. Every action commits a complete snapshot before it is
// displayed; a failed disk write leaves both the hand and balance unchanged.
public sealed class GameWallet
{
    private GameWalletState state;
    private readonly Func<GameWalletState,string?> save;
    public TwentyOneGame TwentyOne {get;}=new();
    public GameWalletState Snapshot=>state.Copy();
    public long Balance=>state.Balance;
    public long TwentyOneStake=>state.TwentyOneStake;
    public RouletteRound? PendingRoulette=>state.Roulette;
    public int? LastRoulette=>state.LastRoulette;
    public string? Error {get;private set;}
    public event Action? Changed;
    public GameWallet(GameWalletState? initial,Func<GameWalletState,string?> save)
    {
        this.save=save;state=(initial??new()).Copy();state.Balance=Math.Clamp(state.Balance,0,9_000_000_000L);state.History=(state.History??new()).Where(r=>r!=null).TakeLast(20).ToList();
        bool validHand=state.Hand!=null&&Valid(state.Hand)&&(state.Hand.Playing?state.TwentyOneStake is >=1 and <=1_000_000:state.TwentyOneStake==0);
        if(validHand)TwentyOne.Restore(state.Hand!);
        else if(state.Hand!=null||state.TwentyOneStake!=0){Error="Сохранённая партия повреждена. Ставка возвращена при следующем сохранении.";state.Balance+=Math.Clamp(state.TwentyOneStake,0,1_000_000);state.TwentyOneStake=0;state.Hand=null;}
        if(state.Roulette is { } r&&(r.Stake<1||r.Stake>1_000_000||r.Outcome<0||r.Outcome>36||!Kinds.Contains(r.Kind)||r.Number<0||r.Number>36)){state.Balance+=Math.Clamp(r.Stake,0,1_000_000);state.Roulette=null;Error="Сохранённое вращение повреждено. Ставка возвращена при следующем сохранении.";}
    }
    private static bool Valid(TwentyOneHand h){if(h.Deck==null||h.Player==null||h.Dealer==null)return false;var all=h.Deck.Concat(h.Player).Concat(h.Dealer).ToArray();return all.Length==52&&all.Distinct().Count()==52&&all.All(c=>c.Rank is >=1 and <=13&&c.Suit is >=0 and <=3)&&h.Player.Count>=2&&h.Dealer.Count>=2&&(!h.Playing||h.Deck.Count>0&&TwentyOneGame.Total(h.Player)<21&&TwentyOneGame.Total(h.Dealer)<21);}
    private bool Commit(GameWalletState next)
    {
        var error=save(next.Copy());if(error!=null){Error="Не удалось сохранить игру: "+error;Changed?.Invoke();return false;}
        state=next;Error=null;if(state.Hand!=null)TwentyOne.Restore(state.Hand);Changed?.Invoke();return true;
    }
    private bool CanBet(long stake){if(stake is <1 or >1_000_000||stake>state.Balance){Error="Недостаточно монет для выбранной ставки.";Changed?.Invoke();return false;}return true;}
    private static void Record(GameWalletState next,string game,string result,long stake,long returned){next.Balance+=returned;next.History.Add(new(){Game=game,Result=result,Stake=stake,Returned=returned});next.History=next.History.TakeLast(20).ToList();}
    public bool Deal(long stake)
    {
        if(state.TwentyOneStake>0||!CanBet(stake))return false;var next=state.Copy();next.Balance-=stake;next.TwentyOneStake=stake;var game=new TwentyOneGame();game.Start();next.Hand=game.Snapshot();SettleHand(next,game);return Commit(next);
    }
    public bool Play(bool hit)
    {
        if(state.TwentyOneStake<=0||state.Hand?.Playing!=true)return false;var next=state.Copy();var game=new TwentyOneGame();game.Restore(next.Hand!);if(hit)game.Hit();else game.Stand();next.Hand=game.Snapshot();SettleHand(next,game);return Commit(next);
    }
    private static void SettleHand(GameWalletState next,TwentyOneGame game)
    {
        if(game.Playing)return;long stake=next.TwentyOneStake;Record(next,"21",game.Result,stake,game.Outcome>0?stake*2:game.Outcome==0?stake:0);next.TwentyOneStake=0;
    }
    public static readonly string[] Kinds={"red","black","even","odd","low","high","number"};
    public static long RouletteReturn(RouletteRound r)
    {
        bool win=r.Kind switch{"number"=>r.Outcome==r.Number,"red"=>r.Outcome>0&&OverlayRandom.IsRed(r.Outcome),"black"=>r.Outcome>0&&!OverlayRandom.IsRed(r.Outcome),"even"=>r.Outcome>0&&r.Outcome%2==0,"odd"=>r.Outcome%2==1,"low"=>r.Outcome is >=1 and <=18,"high"=>r.Outcome is >=19 and <=36,_=>false};return win?r.Stake*(r.Kind=="number"?36:2):0;
    }
    public bool Spin(long stake,string kind,int number)
    {
        if(state.Roulette!=null||!Kinds.Contains(kind)||number is <0 or >36||!CanBet(stake))return false;var next=state.Copy();next.Balance-=stake;next.Roulette=new(){Stake=stake,Kind=kind,Number=number,Outcome=RandomNumberGenerator.GetInt32(37)};return Commit(next);
    }
    public bool FinishSpin()
    {
        if(state.Roulette==null)return true;var next=state.Copy();var r=next.Roulette!;long returned=RouletteReturn(r);next.LastRoulette=r.Outcome;Record(next,"Рулетка",$"{r.Outcome} · "+(returned>0?"выигрыш":"проигрыш"),r.Stake,returned);next.Roulette=null;return Commit(next);
    }
    public bool Restart()
    {
        if(state.Balance!=0||state.TwentyOneStake!=0||state.Roulette!=null)return false;var next=state.Copy();next.Balance=1000;next.History.Add(new(){Game="Кошелёк",Result="Новый старт · 1 000 монет"});next.History=next.History.TakeLast(20).ToList();return Commit(next);
    }
}
