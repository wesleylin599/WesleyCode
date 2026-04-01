namespace TestConsole5.Hosting;

internal sealed class ConsoleLifecycle : IDisposable
{
    private readonly CancellationTokenSource _cts;

    public ConsoleLifecycle()
    {
        _cts = new CancellationTokenSource();
        Console.CancelKeyPress += Console_CancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public CancellationToken Token => _cts.Token;

    private void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _cts.Cancel();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        _cts.Cancel();
    }

    public void Dispose()
    {
        _cts.Dispose();
        Console.CancelKeyPress -= Console_CancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }
}
