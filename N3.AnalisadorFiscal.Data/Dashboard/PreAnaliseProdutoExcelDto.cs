namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class PreAnaliseProdutoExcelDto
{
    public string Produto { get; set; } = string.Empty;
    public string Ncm { get; set; } = string.Empty;
    public decimal ValorTotal { get; set; }
    public decimal IcmsCreditado { get; set; }
    public string SituacaoAnalise { get; set; } = string.Empty;
    public string Fornecedor { get; set; } = string.Empty;
    public string NumeroNota { get; set; } = string.Empty;
    public string ChaveNfe { get; set; } = string.Empty;
}
