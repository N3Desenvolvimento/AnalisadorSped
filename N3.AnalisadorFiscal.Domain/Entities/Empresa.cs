namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class Empresa
{
    public int Id { get; set; }
    public string Cnpj { get; set; } = string.Empty;
    public string RazaoSocial { get; set; } = string.Empty;
    public string? NomeFantasia { get; set; }
    public string? InscricaoEstadual { get; set; }
    public string? Uf { get; set; }
    public string? CodigoMunicipio { get; set; }
    public DateTime? DataAbertura { get; set; }
    public string? CnaePrincipalCodigo { get; set; }
    public string? CnaePrincipalDescricao { get; set; }
    public string? CnaesSecundarios { get; set; }
    public string? NaturezaJuridica { get; set; }
    public string? Porte { get; set; }
    public string? SituacaoCadastral { get; set; }
    public DateTime? DataSituacaoCadastral { get; set; }
    public string? MotivoSituacaoCadastral { get; set; }
    public string? SituacaoEspecial { get; set; }
    public DateTime? DataSituacaoEspecial { get; set; }
    public string? TipoLogradouro { get; set; }
    public string? Logradouro { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Cep { get; set; }
    public string? Bairro { get; set; }
    public string? Municipio { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? EnteFederativoResponsavel { get; set; }
    public DateTime? SincronizadoEm { get; set; }
    public string? RegimeTributario { get; set; }
    public byte[]? Logomarca { get; set; }
    public string? LogomarcaContentType { get; set; }
    public string? LogomarcaNomeArquivo { get; set; }
    public string? CertificadoThumbprint { get; set; }
    public string? CertificadoStoreLocation { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
