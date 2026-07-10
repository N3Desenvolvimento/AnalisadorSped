namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class SpedE111
{
    public int Id { get; set; }
    public int SpedE110Id { get; set; }
    public string CodigoAjusteApuracao { get; set; } = string.Empty;
    public string DescricaoComplementar { get; set; } = string.Empty;
    public decimal ValorAjuste { get; set; }
}
