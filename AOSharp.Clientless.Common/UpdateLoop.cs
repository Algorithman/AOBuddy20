using System.Diagnostics;

namespace AOSharp.Clientless.Common;

public class UpdateLoop
{
    public const int UpdateRate = 64;
    private readonly Action<double> _callback;
    private CancellationTokenSource _cancellationToken;

    private Stopwatch _stopWatch;

    public UpdateLoop(Action<double> callback)
    {
        _callback = callback;
    }

    private void Run()
    {
        var desiredDeltaTime = 1000 / UpdateRate;

        while (!_cancellationToken.IsCancellationRequested)
        {
            var deltaTime = _stopWatch.ElapsedMilliseconds;
            _stopWatch.Restart();
            Tick(deltaTime / 1000d);
            Thread.Sleep((int)Math.Max(desiredDeltaTime - _stopWatch.ElapsedMilliseconds, 0));
        }
    }

    private void Tick(double deltaTime)
    {
        _callback.Invoke(deltaTime);
    }

    public void Start()
    {
        _stopWatch = Stopwatch.StartNew();
        _cancellationToken = new CancellationTokenSource();
        Task.Factory.StartNew(Run, _cancellationToken.Token);
    }

    public void Stop()
    {
        _cancellationToken.Cancel();
    }
}