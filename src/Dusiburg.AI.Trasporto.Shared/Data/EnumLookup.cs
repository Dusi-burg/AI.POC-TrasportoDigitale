namespace Dusiburg.AI.Trasporto.Shared.Data;

/// <summary>
/// Riga di una tabella di lookup generata da un enum: <see cref="Id"/> è il valore numerico esplicito nel codice,
/// <see cref="Name"/> il nome del membro. Le righe vengono da <c>Enum.GetValues</c>, mai scritte a mano.
/// </summary>
public sealed class EnumLookup<TEnum> where TEnum : struct, Enum
{
    public TEnum Id { get; set; }

    public required string Name { get; set; }
}
