namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardGuiaIcmsPdfDto
{
    public int EmpresaId { get; set; }
    public int Ano { get; set; }
    public int Mes { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public byte[] ArquivoPdf { get; set; } = [];
    public decimal ValorPrincipal { get; set; }
    public decimal IcmsARecolher { get; set; }
    public decimal Diferenca { get; set; }
    public string Situacao { get; set; } = string.Empty;
    public DateTime ConferidoEm { get; set; }
}
