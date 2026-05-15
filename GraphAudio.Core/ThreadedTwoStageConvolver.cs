using System.Threading;

namespace GraphAudio.Core;

internal sealed class ThreadedTwoStageConvolver : TwoStageConvolver
{
    private readonly Thread _thread;
    private readonly ManualResetEvent _workReady = new(false);
    private readonly ManualResetEvent _workDone = new(true); //
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public ThreadedTwoStageConvolver()
        : base()
    {
        _cts = new CancellationTokenSource();
        _thread = new Thread(BGLoop)
        {
            IsBackground = true,
            Name = $"ConvTail_{GetHashCode() & 0xFFFF:X4}",
        };
        _thread.Start();
    }

    protected override void StartBackgroundProcessing()
    {
        _workDone.Reset();
        _workReady.Set();
    }

    protected override void WaitForBackgroundProcessing() => _workDone.WaitOne();

    private void BGLoop()
    {
        while (!_disposed && !_cts!.Token.IsCancellationRequested)
        {
            _workReady.WaitOne();

            if (_disposed || _cts!.Token.IsCancellationRequested)
                break;

            DoBackgroundProcessing();

            _workReady.Reset();
            _workDone.Set();
        }
    }

    public override void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cts!.Cancel();
        _workReady.Set();
        _thread.Join(500);

        _cts.Dispose();
        _workReady.Dispose();
        _workDone.Dispose();

        base.Dispose();
    }
}
