namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class SpedE110
{
    public int Id { get; set; }
    public int SpedArquivoId { get; set; }
    public decimal ValorTotalDebitos { get; set; }
    public decimal ValorAjustesDebitos { get; set; }
    public decimal ValorTotalAjustesDebitos { get; set; }
    public decimal ValorEstornosCreditos { get; set; }
    public decimal ValorTotalCreditos { get; set; }
    public decimal ValorAjustesCreditos { get; set; }
    public decimal ValorTotalAjustesCreditos { get; set; }
    public decimal ValorEstornosDebitos { get; set; }
    public decimal ValorSaldoCredorAnterior { get; set; }
    public decimal ValorSaldoDevedor { get; set; }
    public decimal ValorDeducoes { get; set; }
    public decimal ValorIcmsRecolher { get; set; }
    public decimal ValorSaldoCredorTransportar { get; set; }
    public decimal ValorExtraApuracao { get; set; }
}
