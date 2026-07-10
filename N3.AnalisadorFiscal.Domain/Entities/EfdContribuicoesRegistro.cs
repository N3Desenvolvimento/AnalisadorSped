namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class EfdContribuicoesRegistro
{
    public required string Codigo { get; init; }
    public required string[] Campos { get; init; }
}
