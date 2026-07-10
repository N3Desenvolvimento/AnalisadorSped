namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardProdutoOperacaoDto
{
    public string IndicadorOperacao { get; set; } = string.Empty;
    public string Cfop { get; set; } = string.Empty;
    public string CodigoProduto { get; set; } = string.Empty;
    public string DescricaoProduto { get; set; } = string.Empty;
    public decimal Quantidade { get; set; }
    public string Unidade { get; set; } = string.Empty;
    public decimal ValorOperacao { get; set; }
}
