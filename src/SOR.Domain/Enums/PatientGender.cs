namespace SOR.Domain.Enums;

/// <summary>Płeć biologiczna pacjenta — wymagana w nagłym zgłoszeniu do SOR.</summary>
public enum PatientGender
{
    /// <summary>Płeć żeńska.</summary>
    Female = 0,

    /// <summary>Płeć męska.</summary>
    Male = 1,

    /// <summary>Inna / nieokreślona.</summary>
    Other = 2
}