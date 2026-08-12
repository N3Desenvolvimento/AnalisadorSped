using System.Security.Cryptography;
using System.Text;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using N3.AnalisadorFiscal.Data.Dashboard;
using N3.AnalisadorFiscal.Data.Repositories;
using N3.AnalisadorFiscal.Domain.Entities;
using N3.AnalisadorFiscal.Sped.Services;

namespace N3.AnalisadorFiscal.Web.Services;

public interface IPreAnaliseFortesFiscalService
{
    Task<PreAnaliseSpedImportacaoResultado> ImportarAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
}

public sealed class PreAnaliseFortesFiscalService : IPreAnaliseFortesFiscalService
{
    private readonly IConfiguration _configuration;
    private readonly IEmpresaRepository _empresaRepository;
    private readonly IPreAnaliseSpedRepository _repository;
    private readonly IPreAnaliseItemRuleService _ruleService;

    public PreAnaliseFortesFiscalService(IConfiguration configuration, IEmpresaRepository empresaRepository,
        IPreAnaliseSpedRepository repository, IPreAnaliseItemRuleService ruleService)
    {
        _configuration = configuration; _empresaRepository = empresaRepository;
        _repository = repository; _ruleService = ruleService;
    }

    public async Task<PreAnaliseSpedImportacaoResultado> ImportarAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        var empresa = await _empresaRepository.GetByIdAsync(empresaId, cancellationToken) ?? throw new InvalidOperationException("Empresa não encontrada.");
        var codigoFortes = empresa.CodigoEmpresaFiscalFortes?.Trim();
        if (string.IsNullOrWhiteSpace(codigoFortes)) throw new InvalidOperationException("Informe o código da empresa no Fiscal Fortes.");
        var inicio = new DateTime(ano, mes, 1); var fim = inicio.AddMonths(1);

        var connectionString = _configuration.GetConnectionString("FortesFolha") ?? throw new InvalidOperationException("A conexão com o Fortes não foi configurada.");
        var builder = new FbConnectionStringBuilder(connectionString);
        var senha = _configuration["FortesFolha:Password"];
        if (string.IsNullOrWhiteSpace(senha)) senha = Environment.GetEnvironmentVariable("FORTES_FOLHA_PASSWORD");
        if (string.IsNullOrWhiteSpace(senha)) throw new InvalidOperationException("A senha do banco do Fortes não foi configurada.");
        builder.Password = senha;
        await using var connection = new FbConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var linhas = (await connection.QueryAsync<LinhaFiscalFortes>(new CommandDefinition("""
            SELECT n.SEQ AS NotaSequencial, n.ESPECIE AS Especie, n.SERIE AS Serie,
                   n.NUMERO AS Numero, n.DTEMISSAO AS DataEmissao, n.DTENTRADASAIDA AS DataEntrada,
                   n.PAR_CODIGO AS CodigoParticipante, par.NOME AS NomeParticipante,
                   par.CNPJCPF AS CnpjParticipante, n.TOTALVR AS ValorDocumento,
                   n.TOTALPRODUTOS AS ValorMercadorias, n.CHAVEELET AS ChaveNfe,
                   p.SEQ AS NumeroItem, p.PRO_CODIGO AS CodigoItem,
                   pro.DESCRICAO AS Descricao, pro.CDNCM AS Ncm, pro.CEST_CODIGO AS Cest,
                   p.CFO_CODIGO AS Cfop, p.CSTA AS CstA, p.CSTB AS CstB,
                   p.QUANTIDADE AS Quantidade, p.UNIDMEDIDA AS Unidade,
                   p.VRTOTAL AS ValorItem, p.VRDESCONTO AS ValorDesconto,
                   p.ICMSBASECALC AS BaseIcms, p.ICMSALIQ AS AliquotaIcms,
                   p.ICMSSUBSTBC AS BaseIcmsSt, p.ICMSSUBSTVR AS ValorIcmsSt
            FROM NFM n
            INNER JOIN PNM p ON p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ
            LEFT JOIN PRO pro ON pro.EMP_CODIGO=p.EMP_CODIGO AND pro.CODIGO=p.PRO_CODIGO
            LEFT JOIN PAR par ON par.CODIGO=n.PAR_CODIGO
            WHERE TRIM(n.EMP_CODIGO)=@CodigoFortes AND n.OPERACAO='E'
              AND COALESCE(n.CANCELADO,0)=0
              AND n.DTENTRADASAIDA>=@Inicio AND n.DTENTRADASAIDA<@Fim
            ORDER BY n.DTENTRADASAIDA,n.SEQ,p.SEQ
            """, new { CodigoFortes = codigoFortes, Inicio = inicio, Fim = fim }, commandTimeout: 120, cancellationToken: cancellationToken))).AsList();
        if (linhas.Count == 0) throw new InvalidOperationException("Nenhuma nota de entrada foi encontrada no Fortes para a competência selecionada.");

        var dados = new PreAnaliseSpedDados
        {
            NomeArquivo = $"FORTES-FISCAL-{codigoFortes}-{ano}{mes:00}",
            HashArquivo = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"FORTES|{codigoFortes}|{ano}|{mes}|{DateTime.UtcNow.Ticks}"))).ToLowerInvariant(),
            Empresa = empresa,
            Arquivo = new SpedArquivo { EmpresaId=empresa.Id, NomeArquivo=$"Consulta Fortes Fiscal {mes:00}/{ano}", PeriodoInicial=inicio, PeriodoFinal=fim.AddDays(-1) }
        };

        foreach (var grupo in linhas.GroupBy(x => x.NotaSequencial))
        {
            var cabecalho = grupo.First();
            var nota = new PreAnaliseSpedNotaDados
            {
                Nota = new SpedC100 { EmpresaId=empresa.Id, IndicadorOperacao="0", CodigoParticipante=cabecalho.CodigoParticipante,
                    CodigoModelo=ModeloDocumento(cabecalho.Especie), CodigoSituacao="00", Serie=cabecalho.Serie,
                    NumeroDocumento=cabecalho.Numero, ChaveNfe=cabecalho.ChaveNfe, DataDocumento=cabecalho.DataEmissao,
                    DataEntradaSaida=cabecalho.DataEntrada, ValorDocumento=cabecalho.ValorDocumento, ValorMercadoria=cabecalho.ValorMercadorias },
                NomeParticipante = cabecalho.NomeParticipante ?? "(Participante não informado)",
                CnpjParticipante = SomenteDigitos(cabecalho.CnpjParticipante)
            };
            foreach (var linha in grupo)
            {
                var valorIcms = Math.Round(linha.BaseIcms * linha.AliquotaIcms / 100m, 2);
                nota.ValorBaseIcms += linha.BaseIcms; nota.ValorIcmsCredito += valorIcms;
                nota.Itens.Add(new PreAnaliseSpedItemDados
                {
                    Item = new SpedC170 { NumeroItem=linha.NumeroItem.ToString(), CodigoItem=linha.CodigoItem,
                        DescricaoComplementar=linha.Descricao, Quantidade=linha.Quantidade, Unidade=linha.Unidade ?? "",
                        ValorItem=linha.ValorItem, ValorDesconto=linha.ValorDesconto, CstIcms=$"{linha.CstA}{linha.CstB}".Trim(),
                        Cfop=linha.Cfop, ValorBcIcms=linha.BaseIcms, AliquotaIcms=linha.AliquotaIcms, ValorIcms=valorIcms,
                        ValorBcIcmsSt=linha.BaseIcmsSt, ValorIcmsSt=linha.ValorIcmsSt },
                    Descricao=linha.Descricao ?? linha.CodigoItem, Ncm=linha.Ncm, Cest=linha.Cest
                });
            }
            dados.Notas.Add(nota);
        }

        var regras = await _repository.GetRegrasAtivasAsync(empresa.Uf, dados.Arquivo.PeriodoFinal, cancellationToken);
        _ruleService.Aplicar(dados, regras);
        var id = await _repository.SalvarAsync(dados, cancellationToken);
        return new PreAnaliseSpedImportacaoResultado { PreAnaliseSpedId=id, NomeArquivo=dados.NomeArquivo,
            RazaoSocial=empresa.RazaoSocial, Cnpj=empresa.Cnpj, PeriodoInicial=inicio, PeriodoFinal=fim.AddDays(-1),
            TotalNotas=dados.Notas.Count, TotalItens=dados.Notas.Sum(x => x.Itens.Count) };
    }

    private static string ModeloDocumento(string? especie) => string.Equals(especie?.Trim(), "NFE", StringComparison.OrdinalIgnoreCase) ? "55" : especie?.Trim() ?? "";
    private static string SomenteDigitos(string? valor) => string.IsNullOrWhiteSpace(valor) ? "" : new string(valor.Where(char.IsDigit).ToArray());
    private sealed class LinhaFiscalFortes
    {
        public int NotaSequencial { get; set; } public string Especie { get; set; }=""; public string Serie { get; set; }=""; public string Numero { get; set; }="";
        public DateTime DataEmissao { get; set; } public DateTime DataEntrada { get; set; } public string CodigoParticipante { get; set; }=""; public string? NomeParticipante { get; set; } public string? CnpjParticipante { get; set; }
        public decimal ValorDocumento { get; set; } public decimal ValorMercadorias { get; set; } public string? ChaveNfe { get; set; } public int NumeroItem { get; set; } public string CodigoItem { get; set; }=""; public string? Descricao { get; set; }
        public string? Ncm { get; set; } public string? Cest { get; set; } public string Cfop { get; set; }=""; public string CstA { get; set; }=""; public string CstB { get; set; }="";
        public decimal Quantidade { get; set; } public string? Unidade { get; set; } public decimal ValorItem { get; set; } public decimal ValorDesconto { get; set; } public decimal BaseIcms { get; set; } public decimal AliquotaIcms { get; set; } public decimal BaseIcmsSt { get; set; } public decimal ValorIcmsSt { get; set; }
    }
}
