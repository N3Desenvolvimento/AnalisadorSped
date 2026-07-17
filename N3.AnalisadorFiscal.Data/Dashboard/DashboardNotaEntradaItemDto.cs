namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardNotaEntradaItemDto
{
    public int SpedC170Id { get; set; }
    public int NumeroItem { get; set; }
    public string CodigoItem { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string Ncm { get; set; } = string.Empty;
    public string Cest { get; set; } = string.Empty;
    public string Cfop { get; set; } = string.Empty;
    public string CstIcms { get; set; } = string.Empty;
    public decimal ValorItem { get; set; }
    public decimal ValorBaseIcms { get; set; }
    public decimal AliquotaIcms { get; set; }
    public decimal ValorIcmsCredito { get; set; }
    public string Classificacao { get; set; } = string.Empty;
    public string NivelConfianca { get; set; } = string.Empty;
    public string Justificativa { get; set; } = string.Empty;
    public decimal? CreditoPermitido { get; set; }
    public decimal? DiferencaCredito { get; set; }
    public string RegraCodigo { get; set; } = string.Empty;
    public string RegraNome { get; set; } = string.Empty;
}
