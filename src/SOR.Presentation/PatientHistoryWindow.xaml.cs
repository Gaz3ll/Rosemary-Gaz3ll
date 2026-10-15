using System.Windows;

namespace SOR.Presentation;

/// <summary>
/// Okno „Historia pobytu” — widok wyłącznie do odczytu. Cała logika wczytywania danych
/// należy do <see cref="ViewModels.PatientHistoryViewModel"/>, dlatego kod-behind pozostaje pusty.
/// </summary>
public partial class PatientHistoryWindow : Window
{
    public PatientHistoryWindow() => InitializeComponent();
}
