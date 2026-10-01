using System.Windows.Input;

namespace SOR.Presentation.Mvvm;

/// <summary>
/// Wzorzec Polecenia (Command Pattern) dla operacji synchronicznych.
/// Hermetyzuje akcję oraz warunek aktywności (CanExecute), dzięki czemu przyciski w XAML
/// nie zawierają logiki biznesowej.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>Czy wykonano już przynajmniej raz (przydatne do diagnostyki i testów).</summary>
    public int ExecutionCount { get; private set; }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        ExecutionCount++;
        _execute(parameter);
    }

    /// <summary>Wymusza ponowne przeliczenie aktywności polecenia (np. po zmianie stanu formularza).</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Polecenie asynchroniczne obsługujące operacje domenowe (logowanie, rotacja, zapis karty).
/// Blokuje ponowne wywołanie w trakcie trwania operacji, co zapobiega podwójnemu wysyłaniu
/// formularzy oraz wyścigom w dostępie do współdzielonego kontekstu EF Core.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>Czy operacja jest aktualnie wykonywana — źródło właściwości <c>IsBusy</c> w ViewModelu.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            _isRunning = value;
            RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync(parameter).ConfigureAwait(true);

    /// <summary>Wersja asynchroniczna testowalna i awaitowalna (bez <c>async void</c>).</summary>
    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        IsRunning = true;

        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}