namespace Indexa.Api.Comum;

/// <summary>Armadilha 7: aniversários e avisos são decididos no calendário de Brasília, não em UTC.</summary>
public static class Relogio
{
    public static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateOnly HojeEmBrasilia(this TimeProvider relogio) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(relogio.GetUtcNow(), Brasilia).DateTime);
}
