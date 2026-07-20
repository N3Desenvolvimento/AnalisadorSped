namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class PreAnaliseDivergenciaNcmDto
{
    public string Ncm { get; set; } = string.Empty;
    public int QuantidadeItens { get; set; }
    public int QuantidadeNotas { get; set; }
    public decimal ValorItens { get; set; }
    public decimal BaseIcms { get; set; }
    public decimal IcmsInformado { get; set; }
    public decimal CreditoPermitido { get; set; }
    public decimal Divergencia { get; set; }
}
