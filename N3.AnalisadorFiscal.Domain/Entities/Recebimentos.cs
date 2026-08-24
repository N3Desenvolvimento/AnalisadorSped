namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class RecebimentoImportacao
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public DateTime CompetenciaArquivo { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string HashArquivo { get; set; } = string.Empty;
    public string NomeAba { get; set; } = string.Empty;
    public int LinhasLidas { get; set; }
    public decimal TotalFaturadoArquivo { get; set; }
    public decimal TotalRecebidoArquivo { get; set; }
    public decimal TotalRetencoesArquivo { get; set; }
    public int QuantidadeAlertas { get; set; }
    public DateTime ImportadoEm { get; set; } = DateTime.UtcNow;
    public List<RecebimentoDocumento> Documentos { get; } = [];
    public List<RecebimentoMovimento> Movimentos { get; } = [];
    public List<RecebimentoLinhaOrigem> LinhasOrigem { get; } = [];
}

public sealed class RecebimentoDocumento
{
    public string ChaveNatural { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public DateTime? DataEmissao { get; set; }
    public decimal ValorFaturado { get; set; }
    public decimal? DescontoInformado { get; set; }
    public decimal ValorIss { get; set; }
    public decimal ValorPis { get; set; }
    public decimal ValorCofins { get; set; }
    public decimal ValorIr { get; set; }
    public decimal ValorCsll { get; set; }
    public decimal ValorInss { get; set; }
    public decimal ValorOutros { get; set; }
    public string? CodigoServico { get; set; }
    public string? NumeroNota { get; set; }
    public string? IndicadorRetencoesFederais { get; set; }
    public string? IndicadorIss { get; set; }
    public int LinhaOrigem { get; set; }
    public decimal TotalRetencoes => ValorIss + ValorPis + ValorCofins + ValorIr + ValorCsll + ValorInss + ValorOutros;
}

public sealed class RecebimentoMovimento
{
    public string ChaveNatural { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public DateTime DataRecebimento { get; set; }
    public decimal ValorRecebido { get; set; }
    public string? Parcela { get; set; }
    public string? NotasVinculadas { get; set; }
    public DateTime? DataEmissaoReferencia { get; set; }
    public bool RecebidoAntesEmissao { get; set; }
    public int LinhaOrigem { get; set; }
}

public sealed class RecebimentoLinhaOrigem
{
    public int NumeroLinha { get; set; }
    public string ConteudoJson { get; set; } = string.Empty;
    public string? Alerta { get; set; }
}
