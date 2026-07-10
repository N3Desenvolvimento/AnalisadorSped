namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class SpedC100
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int SpedArquivoId { get; set; }
    public int? ParticipanteId { get; set; }
    public string IndicadorOperacao { get; set; } = string.Empty;
    public string IndicadorEmitente { get; set; } = string.Empty;
    public string CodigoParticipante { get; set; } = string.Empty;
    public string CodigoModelo { get; set; } = string.Empty;
    public string CodigoSituacao { get; set; } = string.Empty;
    public string Serie { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string? ChaveNfe { get; set; }
    public DateTime? DataDocumento { get; set; }
    public DateTime? DataEntradaSaida { get; set; }
    public decimal ValorDocumento { get; set; }
    public decimal ValorDesconto { get; set; }
    public decimal ValorMercadoria { get; set; }
    public decimal ValorFrete { get; set; }
    public decimal ValorSeguro { get; set; }
    public decimal ValorOutrasDespesas { get; set; }
    public decimal ValorIcms { get; set; }
    public decimal ValorIcmsSt { get; set; }
    public decimal ValorIpi { get; set; }
    public decimal ValorPis { get; set; }
    public decimal ValorCofins { get; set; }
}


