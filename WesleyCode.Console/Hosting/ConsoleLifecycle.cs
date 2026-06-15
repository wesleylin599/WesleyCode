namespace WesleyCode.Console.Hosting;

internal sealed class ConsoleLifecycle : IDisposable
{
    private readonly CancellationTokenSource _cts;

    public ConsoleLifecycle()
    {
        _cts = new CancellationTokenSource();
        System.Console.CancelKeyPress += Console_CancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public CancellationToken Token => _cts.Token;

    private void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        TryCancel();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        TryCancel();
    }

    public void Dispose()
    {
        System.Console.CancelKeyPress -= Console_CancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _cts.Dispose();
    }

    private void TryCancel()
    {
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException) { }
    }
}
