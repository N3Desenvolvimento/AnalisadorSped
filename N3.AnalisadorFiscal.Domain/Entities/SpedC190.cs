namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class SpedC190
{
    public int Id { get; set; }
    public int SpedC100Id { get; set; }
    public string CstIcms { get; set; } = string.Empty;
    public string Cfop { get; set; } = string.Empty;
    public decimal AliquotaIcms { get; set; }
    public decimal ValorOperacao { get; set; }
    public decimal ValorBcIcms { get; set; }
    public decimal ValorIcms { get; set; }
    public decimal ValorBcIcmsSt { get; set; }
    public decimal ValorIcmsSt { get; set; }
    public decimal ValorReducaoBcIcms { get; set; }
    public decimal ValorIpi { get; set; }
    public string? CodigoObservacao { get; set; }
}
