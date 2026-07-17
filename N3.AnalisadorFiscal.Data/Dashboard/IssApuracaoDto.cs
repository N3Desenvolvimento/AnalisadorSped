namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class IssApuracaoDto
{
    public int EmpresaId { get; set; }
    public int Ano { get; set; }
    public int Mes { get; set; }
    public int QuantidadePrestadas { get; set; }
    public int QuantidadeTomadas { get; set; }
    public decimal ValorServicosPrestados { get; set; }
    public decimal ValorServicosTomados { get; set; }
    public decimal IssDevido { get; set; }
    public decimal IssRetidoPorTomadores { get; set; }
    public decimal IssRetidoComoTomador { get; set; }
    public decimal IssARecolher => Math.Max(0, IssDevido - IssRetidoPorTomadores + IssRetidoComoTomador);
    public IReadOnlyList<IssNotaDto> Notas { get; set; } = [];
}

public sealed class IssNotaDto
{
    public string ChaveAcesso { get; set; } = string.Empty;
    public string? NumeroNota { get; set; }
    public DateTime DataEmissao { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string CnpjPrestador { get; set; } = string.Empty;
    public string? NomePrestador { get; set; }
    public string CnpjTomador { get; set; } = string.Empty;
    public string? NomeTomador { get; set; }
    public string? CodigoServico { get; set; }
    public string? MunicipioIncidencia { get; set; }
    public decimal ValorServicos { get; set; }
    public decimal BaseCalculo { get; set; }
    public decimal Aliquota { get; set; }
    public decimal ValorIss { get; set; }
    public decimal ValorPisRetido { get; set; }
    public decimal ValorCofinsRetido { get; set; }
    public decimal ValorIrrfRetido { get; set; }
    public decimal ValorCsllRetido { get; set; }
    public decimal ValorInssRetido { get; set; }
    public decimal TotalRetido => (IssRetido ? ValorIss : 0) + ValorPisRetido + ValorCofinsRetido + ValorIrrfRetido + ValorCsllRetido + ValorInssRetido;
    public bool IssRetido { get; set; }
}
