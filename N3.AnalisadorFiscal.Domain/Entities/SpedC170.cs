namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class SpedC170
{
    public int Id { get; set; }
    public int SpedC100Id { get; set; }
    public int? ProdutoId { get; set; }
    public string NumeroItem { get; set; } = string.Empty;
    public string CodigoItem { get; set; } = string.Empty;
    public string? DescricaoComplementar { get; set; }
    public decimal Quantidade { get; set; }
    public string Unidade { get; set; } = string.Empty;
    public decimal ValorItem { get; set; }
    public decimal ValorDesconto { get; set; }
    public string CstIcms { get; set; } = string.Empty;
    public string Cfop { get; set; } = string.Empty;
    public string NaturezaBcIcms { get; set; } = string.Empty;
    public decimal ValorBcIcms { get; set; }
    public decimal AliquotaIcms { get; set; }
    public decimal ValorIcms { get; set; }
    public decimal ValorBcIcmsSt { get; set; }
    public decimal AliquotaIcmsSt { get; set; }
    public decimal ValorIcmsSt { get; set; }
    public string? CstIpi { get; set; }
    public decimal ValorBcIpi { get; set; }
    public decimal AliquotaIpi { get; set; }
    public decimal ValorIpi { get; set; }
}
