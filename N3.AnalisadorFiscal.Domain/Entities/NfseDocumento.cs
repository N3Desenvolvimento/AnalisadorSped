namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class NfseDocumento
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long Nsu { get; set; }
    public string ChaveAcesso { get; set; } = string.Empty;
    public string? NumeroNota { get; set; }
    public DateTime DataEmissao { get; set; }
    public DateTime Competencia { get; set; }
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
    public bool IssRetido { get; set; }
    public bool Cancelada { get; set; }
    public string Xml { get; set; } = string.Empty;
}
