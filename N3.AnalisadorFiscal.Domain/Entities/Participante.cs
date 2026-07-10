namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class Participante
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int SpedArquivoId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string? Cnpj { get; set; }
    public string? Cpf { get; set; }
    public string? InscricaoEstadual { get; set; }
    public string? CodigoPais { get; set; }
    public string? CodigoMunicipio { get; set; }
    public string? Suframa { get; set; }
    public string? Endereco { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public bool? SimplesNacional { get; set; }
    public DateTime? SimplesNacionalConsultadoEm { get; set; }
    public string? SimplesNacionalMensagem { get; set; }
}
