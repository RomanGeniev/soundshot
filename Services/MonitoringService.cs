using System;
using System.Threading;
using System.Threading.Tasks;

namespace GraphiteCompanion.Services;

public sealed class MonitoringService : IAsyncDisposable
{
    private readonly SemaphoreSlim lifecycle=new(1,1);
    private CancellationTokenSource? cancellation;
    private Task? loop;
    private volatile MetricRequest request=new();
    public event Action<MetricSnapshot>? Updated;
    public bool Running=>cancellation is {IsCancellationRequested:false}&&loop is {IsCompleted:false};
    public void Configure(MetricRequest value)=>request=value with {IntervalMs=Math.Clamp(value.IntervalMs,250,5000)};
    public async Task SetActiveAsync(bool active)
    {
        await lifecycle.WaitAsync();
        try
        {
            if(loop is {IsCompleted:true}){cancellation?.Dispose();cancellation=null;loop=null;}
            if(active&&cancellation==null){cancellation=new();var token=cancellation.Token;loop=Task.Run(()=>Run(token));}
            else if(!active&&cancellation!=null){cancellation.Cancel();if(loop!=null)await Task.WhenAny(loop,Task.Delay(2500));if(loop==null||loop.IsCompleted){cancellation.Dispose();cancellation=null;loop=null;}}
            else if(active&&cancellation is {IsCancellationRequested:true})Updated?.Invoke(new(DateTime.UtcNow,new(),"Ожидаем завершения предыдущего опроса датчиков. Повторный сбор пока не запускается."));
        }
        finally{lifecycle.Release();}
    }
    private async Task Run(CancellationToken token)
    {
        try
        {
            using var sampler=new HardwareSampler();
            while(!token.IsCancellationRequested)
            {
                long started=System.Diagnostics.Stopwatch.GetTimestamp();var snapshot=sampler.Read(request);snapshot=snapshot with{CollectionDurationMs=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds};if(!token.IsCancellationRequested)Updated?.Invoke(snapshot);
                await Task.Delay(request.IntervalMs,token);
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!token.IsCancellationRequested)Updated?.Invoke(new(DateTime.UtcNow,new(),"Мониторинг недоступен: "+ex.Message));}
    }
    public async ValueTask DisposeAsync()=>await SetActiveAsync(false);
}
