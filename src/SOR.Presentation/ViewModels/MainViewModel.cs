using SOR.Presentation.Mvvm;

namespace SOR.Presentation.ViewModels;

/// <summary>
/// Główny ViewModel okna aplikacji. Pełni rolę punktu kompozycji dla widoku:
/// udostępnia kontekst sesji (logowanie, strefa, rotacja) oraz pulpit pacjentów,
/// nie zawierając samodzielnie logiki biznesowej.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    public MainViewModel(SessionViewModel session, PatientBoardViewModel board)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Board = board ?? throw new ArgumentNullException(nameof(board));
    }

    /// <summary>Kontekst sesji: logowanie, strefa robocza, wnioski o rotację (BR-02/BR-05).</summary>
    public SessionViewModel Session { get; }

    /// <summary>Pulpit strefy: rejestracja, triage, karta pacjenta, obciążenie (BR-03/BR-06).</summary>
    public PatientBoardViewModel Board { get; }
}
