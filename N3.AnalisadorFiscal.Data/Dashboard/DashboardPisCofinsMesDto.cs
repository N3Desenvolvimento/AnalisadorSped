namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardPisCofinsMesDto
{
    public int Mes { get; set; }
    public string NomeMes { get; set; } = string.Empty;
    public int SpedArquivoId { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public DateTime DataImportacao { get; set; }
    public decimal DebitoPis { get; set; }
    public decimal CreditoPis { get; set; }
    public decimal PisAPagar { get; set; }
    public decimal DebitoCofins { get; set; }
    public decimal CreditoCofins { get; set; }
    public decimal CofinsAPagar { get; set; }
    public decimal TotalAPagar => PisAPagar + CofinsAPagar;
}
