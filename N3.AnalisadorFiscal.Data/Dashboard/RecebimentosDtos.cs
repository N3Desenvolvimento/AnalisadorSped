namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class RecebimentoResumoMesDto
{
    public int Mes { get; set; }
    public decimal ValorRecebido { get; set; }
    public int QuantidadeRecebimentos { get; set; }
    public int QuantidadeClientes { get; set; }
}

public sealed class RecebimentoCompetenciaDto
{
    public decimal ValorRecebido { get; set; }
    public int QuantidadeRecebimentos { get; set; }
    public int QuantidadeClientes { get; set; }
    public decimal ValorFaturadoEmitido { get; set; }
    public decimal ValorRetencoesInformadas { get; set; }
    public int QuantidadeDocumentos { get; set; }
}

public sealed class RecebimentoDetalheDto
{
    public DateTime DataRecebimento { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string? NotasVinculadas { get; set; }
    public decimal ValorRecebido { get; set; }
    public string? Parcela { get; set; }
    public DateTime? DataEmissaoReferencia { get; set; }
    public bool RecebidoAntesEmissao { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public DateTime CompetenciaArquivo { get; set; }
}

public sealed class RecebimentoDocumentoDto
{
    public DateTime? DataEmissao { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string? NumeroNota { get; set; }
    public string? CodigoServico { get; set; }
    public decimal ValorFaturado { get; set; }
    public decimal ValorRetencoes { get; set; }
    public string? IndicadorRetencoesFederais { get; set; }
    public string? IndicadorIss { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
}

public sealed class RecebimentoImportacaoResumoDto
{
    public int Id { get; set; }
    public DateTime CompetenciaArquivo { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string NomeAba { get; set; } = string.Empty;
    public int LinhasLidas { get; set; }
    public int QuantidadeDocumentos { get; set; }
    public int QuantidadeRecebimentos { get; set; }
    public decimal TotalFaturadoArquivo { get; set; }
    public decimal TotalRecebidoArquivo { get; set; }
    public decimal TotalRetencoesArquivo { get; set; }
    public int QuantidadeAlertas { get; set; }
    public DateTime ImportadoEm { get; set; }
}

public sealed class RecebimentoFortesOrigemDto
{
    public string MovimentoChaveNatural { get; set; } = string.Empty;
    public DateTime DataRecebimento { get; set; }
    public decimal ValorRecebido { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string? NotasVinculadas { get; set; }
    public string? NumeroNota { get; set; }
    public DateTime? DataEmissao { get; set; }
    public decimal? ValorFaturado { get; set; }
    public decimal? DescontoInformado { get; set; }
    public decimal ValorRecebidoTotalConhecido { get; set; }
}
