namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardTopProdutoSaidaDto
{
    public string CodigoProduto { get; set; } = string.Empty;
    public string DescricaoProduto { get; set; } = string.Empty;
    public decimal ValorSaida { get; set; }
    public decimal PercentualSaida { get; set; }
}
