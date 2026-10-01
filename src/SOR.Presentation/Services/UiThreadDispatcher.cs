using System.Windows;
using System.Windows.Threading;

namespace SOR.Presentation.Services;

/// <summary>
/// Most między asynchronicznym kontekstem aplikacji a wątkiem WPF.
/// Serwisy domenowe publikują zdarzenia z wątków roboczych (pętla monitoringu), a WPF
/// wymaga aktualizacji właściwości wyłącznie na wątku UI.
/// </summary>
public sealed class UiThreadDispatcher
{
    private readonly Dispatcher _dispatcher;

    public UiThreadDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>Czy wykonujemy się już na wątku UI.</summary>
    public bool IsOnUiThread => _dispatcher.CheckAccess();

    /// <summary>Wykonuje akcję na wątku UI (natychmiast, jeśli już na nim jesteśmy).</summary>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsOnUiThread)
        {
            action();
            return;
        }

        _dispatcher.Invoke(action, DispatcherPriority.Normal);
    }

    /// <summary>Kolejkuje akcję na wątku UI (nie blokuje wątku wywołującego).</summary>
    public void BeginInvoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsOnUiThread)
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    /// <summary>Kolejkuje akcję na wątku UI i zwraca zadanie ukończone po jej wykonaniu.</summary>
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }
}