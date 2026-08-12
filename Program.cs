using MudBlazor.Services;
using N3.AnalisadorFiscal.Data;
using System.IO.Compression;
using System.Text;
using N3.AnalisadorFiscal.Data.Dashboard;
using N3.AnalisadorFiscal.Data.Repositories;
using N3.AnalisadorFiscal.Sped;
using N3.AnalisadorFiscal.Web.Components;
using N3.AnalisadorFiscal.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddDataAccess();
builder.Services.AddSpedServices();
builder.Services.AddScoped<INotasEntradaExcelService, NotasEntradaExcelService>();
builder.Services.AddScoped<ICfopExcelService, CfopExcelService>();
builder.Services.AddScoped<IIcmsApuracaoPdfService, IcmsApuracaoPdfService>();
builder.Services.AddScoped<IGuiaIcmsPdfLeituraService, GuiaIcmsPdfLeituraService>();
builder.Services.AddScoped<IFolhaFortesImportService, FolhaFortesImportService>();
builder.Services.AddScoped<IContrachequePdfService, ContrachequePdfService>();
builder.Services.AddScoped<IPreAnaliseFortesFiscalService, PreAnaliseFortesFiscalService>();
builder.Services.AddScoped<IIcmsFortesApuracaoService, IcmsFortesApuracaoService>();
builder.Services.AddScoped<INfseFortesInssService, NfseFortesInssService>();
builder.Services.Configure<NfseNacionalOptions>(builder.Configuration.GetSection(NfseNacionalOptions.SectionName));
builder.Services.AddScoped<INfseNacionalService, NfseNacionalService>();
builder.Services.AddSingleton<ICertificateStoreService, CertificateStoreService>();
builder.Services.AddHttpClient<ISimplesNacionalConsultaService, SimplesNacionalConsultaService>(client =>
{
    client.BaseAddress = new Uri("https://brasilapi.com.br/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<ICnpjPublicoConsultaService, CnpjPublicoConsultaService>(client =>
{
    client.BaseAddress = new Uri("https://brasilapi.com.br/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient("DanfseNacional", client =>
{
    client.BaseAddress = new Uri("https://adn.nfse.gov.br/");
    client.Timeout = TimeSpan.FromSeconds(45);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/downloads/entradas-fornecedores/{empresaId:int}/{ano:int}/{mes:int}", async (
    int empresaId,
    int ano,
    int mes,
    IDashboardFiscalRepository dashboardFiscalRepository,
    INotasEntradaExcelService excelService,
    CancellationToken cancellationToken) =>
{
    var notas = await dashboardFiscalRepository.GetNotasEntradaFornecedoresAsync(empresaId, ano, mes, cancellationToken);
    var arquivo = excelService.Gerar(notas);
    var nomeArquivo = $"notas_entrada_fornecedores_{ano}_{mes:00}.xlsx";

    return Results.File(
        arquivo,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        nomeArquivo);
});

app.MapGet("/downloads/pre-analise-icms/{preAnaliseSpedId:int}", async (
    int preAnaliseSpedId,
    IPreAnaliseSpedRepository repository,
    INotasEntradaExcelService excelService,
    CancellationToken cancellationToken) =>
{
    var notas = await repository.GetNotasAsync(preAnaliseSpedId, cancellationToken);
    var arquivo = excelService.Gerar(notas);
    return Results.File(arquivo,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"pre_analise_icms_{preAnaliseSpedId}.xlsx");
});

app.MapGet("/downloads/pre-analise-produtos/{preAnaliseSpedId:int}", async (
    int preAnaliseSpedId,
    IPreAnaliseSpedRepository repository,
    INotasEntradaExcelService excelService,
    CancellationToken cancellationToken) =>
{
    var produtos = await repository.GetProdutosExcelAsync(preAnaliseSpedId, cancellationToken);
    var arquivo = excelService.GerarAnaliseProdutos(produtos);
    return Results.File(arquivo,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"analise_de_produtos_{preAnaliseSpedId}.xlsx");
});

app.MapGet("/downloads/apuracao-icms/{empresaId:int}/{ano:int}/{mes:int}", async (
    int empresaId,
    int ano,
    int mes,
    IDashboardFiscalRepository dashboardFiscalRepository,
    IIcmsApuracaoPdfService pdfService,
    CancellationToken cancellationToken) =>
{
    var empresas = await dashboardFiscalRepository.GetEmpresasAsync(cancellationToken);
    var empresa = empresas.FirstOrDefault(item => item.Id == empresaId);
    var competencia = new DateTime(ano, mes, 1);
    var dashboard = await dashboardFiscalRepository.GetDashboardAsync(empresaId, competencia, cancellationToken);
    var ultimosSeisMeses = new List<ApuracaoMensalPdfDto>();

    for (var i = 5; i >= 0; i--)
    {
        var competenciaAnalise = competencia.AddMonths(-i);
        var dashboardAnalise = await dashboardFiscalRepository.GetDashboardAsync(empresaId, competenciaAnalise, cancellationToken);
        ultimosSeisMeses.Add(new ApuracaoMensalPdfDto
        {
            Competencia = competenciaAnalise,
            Dashboard = dashboardAnalise
        });
    }

    var arquivo = pdfService.GerarDemonstrativo(empresa, competencia, dashboard, ultimosSeisMeses);
    var nomeEmpresa = NomeSeguroParaArquivo(empresa?.RazaoSocial, $"empresa_{empresaId}");
    var nomeArquivo = $"apuracao_icms_{nomeEmpresa}_{ano}_{mes:00}.pdf";

    return Results.File(arquivo, "application/pdf", nomeArquivo);
});

app.MapGet("/downloads/apuracao-icms-3-meses/{empresaId:int}/{ano:int}/{mes:int}", async (
    int empresaId,
    int ano,
    int mes,
    IDashboardFiscalRepository dashboardFiscalRepository,
    IIcmsApuracaoPdfService pdfService,
    CancellationToken cancellationToken) =>
{
    var empresas = await dashboardFiscalRepository.GetEmpresasAsync(cancellationToken);
    var empresa = empresas.FirstOrDefault(item => item.Id == empresaId);
    var competenciaBase = new DateTime(ano, mes, 1);
    var apuracoes = new List<ApuracaoMensalPdfDto>();

    for (var i = 2; i >= 0; i--)
    {
        var competencia = competenciaBase.AddMonths(-i);
        var dashboard = await dashboardFiscalRepository.GetDashboardAsync(empresaId, competencia, cancellationToken);
        apuracoes.Add(new ApuracaoMensalPdfDto
        {
            Competencia = competencia,
            Dashboard = dashboard
        });
    }

    var arquivo = pdfService.GerarAnaliseTresMeses(empresa, apuracoes);
    var nomeArquivo = $"analise_icms_3_meses_{ano}_{mes:00}.pdf";

    return Results.File(arquivo, "application/pdf", nomeArquivo);
});

app.MapGet("/downloads/guia-icms/{empresaId:int}/{ano:int}/{mes:int}", async (
    int empresaId,
    int ano,
    int mes,
    IDashboardFiscalRepository dashboardFiscalRepository,
    CancellationToken cancellationToken) =>
{
    var guia = await dashboardFiscalRepository.GetGuiaIcmsPdfAsync(empresaId, ano, mes, cancellationToken);
    if (guia is null || guia.ArquivoPdf.Length == 0)
    {
        return Results.NotFound("Guia nao encontrada para esta competencia.");
    }

    var nomeArquivo = string.IsNullOrWhiteSpace(guia.NomeArquivo)
        ? $"guia_icms_{ano}_{mes:00}.pdf"
        : guia.NomeArquivo;

    return Results.File(guia.ArquivoPdf, "application/pdf", nomeArquivo);
});

app.MapGet("/downloads/cfop-resumo-operacoes/{empresaId:int}/{ano:int}/{mes:int}", async (
    int empresaId,
    int ano,
    int mes,
    IDashboardFiscalRepository dashboardFiscalRepository,
    ICfopExcelService excelService,
    CancellationToken cancellationToken) =>
{
    var dashboard = await dashboardFiscalRepository.GetDashboardAsync(empresaId, new DateTime(ano, mes, 1), cancellationToken);
    var linhas = new[] { "0", "1", "TE", "TS" }
        .Select(tipo =>
        {
            var itens = dashboard.ResumoPorCfopCst
                .Where(item => PertenceAoTipoOperacaoCfop(item, tipo))
                .ToArray();

            return (IReadOnlyList<object?>)
            [
                NomeTipoOperacaoCfop(tipo),
                itens.Sum(item => item.ValorOperacao),
                itens.Sum(item => item.BaseIcms),
                itens.Sum(item => item.ValorIcms),
                itens.Select(item => item.Cfop).Distinct().Count()
            ];
        })
        .ToArray();

    var arquivo = excelService.Gerar(
        "Resumo CFOP",
        ["Operacao", "Valor operacao", "Base ICMS", "Valor ICMS", "CFOPs"],
        linhas);
    var nomeArquivo = $"resumo_cfop_{ano}_{mes:00}.xlsx";

    return Results.File(
        arquivo,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        nomeArquivo);
});

app.MapGet("/downloads/cfop-resumo/{empresaId:int}/{ano:int}/{mes:int}/{tipoOperacao}", async (
    int empresaId,
    int ano,
    int mes,
    string tipoOperacao,
    IDashboardFiscalRepository dashboardFiscalRepository,
    ICfopExcelService excelService,
    CancellationToken cancellationToken) =>
{
    var tipoSelecionado = TipoOperacaoCfopValido(tipoOperacao);
    var dashboard = await dashboardFiscalRepository.GetDashboardAsync(empresaId, new DateTime(ano, mes, 1), cancellationToken);
    var linhas = dashboard.ResumoPorCfopCst
        .Where(item => PertenceAoTipoOperacaoCfop(item, tipoSelecionado))
        .GroupBy(item => item.Cfop)
        .Select(grupo => (IReadOnlyList<object?>)
        [
            grupo.Key,
            grupo.Sum(item => item.ValorOperacao),
            grupo.Sum(item => item.BaseIcms),
            grupo.Sum(item => item.ValorIcms)
        ])
        .OrderBy(item => item[0]?.ToString())
        .ToArray();

    var arquivo = excelService.Gerar(
        NomeTipoOperacaoCfop(tipoSelecionado),
        ["CFOP", "Valor operacao", "Base ICMS", "Valor ICMS"],
        linhas);
    var nomeArquivo = $"cfop_{NomeArquivoTipoOperacaoCfop(tipoSelecionado)}_{ano}_{mes:00}.xlsx";

    return Results.File(
        arquivo,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        nomeArquivo);
});

app.MapGet("/downloads/cfop-detalhe/{empresaId:int}/{ano:int}/{mes:int}/{tipoOperacao}/{cfop}", async (
    int empresaId,
    int ano,
    int mes,
    string tipoOperacao,
    string cfop,
    IDashboardFiscalRepository dashboardFiscalRepository,
    ICfopExcelService excelService,
    CancellationToken cancellationToken) =>
{
    var tipoSelecionado = TipoOperacaoCfopValido(tipoOperacao);
    var dashboard = await dashboardFiscalRepository.GetDashboardAsync(empresaId, new DateTime(ano, mes, 1), cancellationToken);
    var linhas = dashboard.ResumoPorCfopCst
        .Where(item => PertenceAoTipoOperacaoCfop(item, tipoSelecionado) &&
            string.Equals(item.Cfop, cfop, StringComparison.OrdinalIgnoreCase))
        .OrderBy(item => item.CstIcms)
        .Select(item => (IReadOnlyList<object?>)
        [
            item.CstIcms,
            item.ValorOperacao,
            item.BaseIcms,
            item.ValorIcms
        ])
        .ToArray();

    var arquivo = excelService.Gerar(
        $"CFOP {cfop}",
        ["CST ICMS", "Valor operacao", "Base ICMS", "Valor ICMS"],
        linhas);
    var nomeArquivo = $"cfop_{cfop}_detalhe_{ano}_{mes:00}.xlsx";

    return Results.File(
        arquivo,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        nomeArquivo);
});

app.MapGet("/downloads/nfse-excel/{empresaId:int}/{ano:int}/{mes:int}", async (
    int empresaId,
    int ano,
    int mes,
    INfseRepository nfseRepository,
    IEmpresaRepository empresaRepository,
    INotasEntradaExcelService excelService,
    CancellationToken cancellationToken) =>
{
    if (mes is < 1 or > 12 || ano is < 2000 or > 2100)
        return Results.BadRequest("Competência inválida.");

    var empresa = await empresaRepository.GetByIdAsync(empresaId, cancellationToken);
    if (empresa is null) return Results.NotFound("Empresa não encontrada.");

    var apuracao = await nfseRepository.GetApuracaoAsync(empresaId, empresa.Cnpj, ano, mes, cancellationToken);
    if (apuracao.Notas.Count == 0)
        return Results.NotFound("Nenhuma NFS-e encontrada para a competência.");

    var arquivo = excelService.GerarNfse(apuracao.Notas);
    return Results.File(
        arquivo,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"nfse_{ano}_{mes:00}.xlsx");
});

app.MapGet("/downloads/nfse-xml/{tipo}/{empresaId:int}/{ano:int}/{mes:int}", async (
    string tipo,
    int empresaId,
    int ano,
    int mes,
    INfseRepository nfseRepository,
    IEmpresaRepository empresaRepository,
    CancellationToken cancellationToken) =>
{
    if (mes is < 1 or > 12 || ano is < 2000 or > 2100)
        return Results.BadRequest("Competência inválida.");
    tipo = tipo.ToLowerInvariant();
    if (tipo is not ("prestadas" or "tomadas"))
        return Results.BadRequest("Tipo de NFS-e inválido.");

    var empresa = await empresaRepository.GetByIdAsync(empresaId, cancellationToken);
    if (empresa is null) return Results.NotFound("Empresa não encontrada.");
    var documentos = await nfseRepository.GetXmlsAsync(empresaId, empresa.Cnpj, ano, mes, tipo, cancellationToken);
    if (documentos.Count == 0)
        return Results.NotFound($"Nenhum XML de NFS-e {tipo} encontrado para a competência.");

    using var memoria = new MemoryStream();
    using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var documento in documentos)
        {
            var chave = new string(documento.ChaveAcesso.Where(char.IsLetterOrDigit).ToArray());
            var sufixo = documento.Cancelada ? "_cancelada" : string.Empty;
            var entrada = zip.CreateEntry($"{chave}{sufixo}.xml", CompressionLevel.Optimal);
            await using var destino = entrada.Open();
            await using var escritor = new StreamWriter(destino, new UTF8Encoding(false));
            await escritor.WriteAsync(documento.Xml.AsMemory(), cancellationToken);
        }
    }

    return Results.File(memoria.ToArray(), "application/zip", $"nfse_{tipo}_{ano}_{mes:00}.zip");
});

app.MapGet("/downloads/nfse-pdf/{chave}", async (
    string chave,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    chave = new string(chave.Where(char.IsLetterOrDigit).ToArray());
    if (chave.Length is < 40 or > 60)
        return Results.BadRequest("Chave de acesso da NFS-e inválida.");

    try
    {
        var client = httpClientFactory.CreateClient("DanfseNacional");
        using var response = await client.GetAsync($"danfse/{Uri.EscapeDataString(chave)}", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var pdf = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (pdf.Length > 4 && pdf[0] == (byte)'%' && pdf[1] == (byte)'P' && pdf[2] == (byte)'D' && pdf[3] == (byte)'F')
                return Results.File(pdf, "application/pdf", $"nfse_{chave}.pdf");
        }
    }
    catch (HttpRequestException) { }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }

    return Results.Redirect($"https://www.nfse.gov.br/ConsultaPublica/?chave={Uri.EscapeDataString(chave)}&tpc=1");
});

app.MapGet("/downloads/contracheques/{empresaId:int}/{ano:int}/{mes:int}/{trabalhadorId:int}", async (
    int empresaId,
    int ano,
    int mes,
    int trabalhadorId,
    IEmpresaRepository empresaRepository,
    IFolhaPagamentoRepository folhaRepository,
    IContrachequePdfService pdfService,
    CancellationToken cancellationToken) =>
{
    var empresa = await empresaRepository.GetByIdAsync(empresaId, cancellationToken);
    if (empresa is null)
        return Results.NotFound("Empresa não encontrada.");

    var folha = await folhaRepository.GetEspelhoAsync(empresaId, ano, mes, cancellationToken);
    if (folha is null)
        return Results.NotFound("Folha não encontrada para a competência selecionada.");

    if (trabalhadorId != 0 && folha.Trabalhadores.All(x => x.Id != trabalhadorId))
        return Results.NotFound("Colaborador não encontrado nesta folha.");

    var pdf = pdfService.Gerar(empresa, folha, trabalhadorId);
    var sufixo = trabalhadorId == 0 ? "todos" : trabalhadorId.ToString();
    return Results.File(pdf, "application/pdf", $"contracheques_{ano}_{mes:00}_{sufixo}.pdf");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool PertenceAoTipoOperacaoCfop(DashboardFiscalCfopCstDto item, string tipoOperacao)
{
    var transferencia = CfopTransferencia(item.Cfop);

    return tipoOperacao switch
    {
        "0" => item.IndicadorOperacao == "0" && !transferencia,
        "1" => item.IndicadorOperacao == "1" && !transferencia,
        "TE" => item.IndicadorOperacao == "0" && transferencia,
        "TS" => item.IndicadorOperacao == "1" && transferencia,
        _ => false
    };
}

static string TipoOperacaoCfopValido(string tipoOperacao)
{
    return tipoOperacao is "1" or "TE" or "TS" ? tipoOperacao : "0";
}

static string NomeTipoOperacaoCfop(string tipoOperacao)
{
    return tipoOperacao switch
    {
        "1" => "Saidas",
        "TE" => "Transferencia entrada",
        "TS" => "Transferencia saida",
        _ => "Entradas"
    };
}

static string NomeArquivoTipoOperacaoCfop(string tipoOperacao)
{
    return tipoOperacao switch
    {
        "1" => "saidas",
        "TE" => "transferencia_entrada",
        "TS" => "transferencia_saida",
        _ => "entradas"
    };
}

static bool CfopTransferencia(string cfop)
{
    cfop = cfop.Trim();

    return cfop is "1151" or "1152" or "1408"
        or "2151" or "2152" or "2408" or "2409"
        or "5151" or "5152" or "5408" or "5409"
        or "6151" or "6152" or "6408" or "6409";
}

static string NomeSeguroParaArquivo(string? valor, string valorPadrao)
{
    if (string.IsNullOrWhiteSpace(valor))
    {
        return valorPadrao;
    }

    var nome = new string(valor.Trim()
        .Select(caractere => char.IsLetterOrDigit(caractere) ? caractere : '_')
        .ToArray());

    while (nome.Contains("__", StringComparison.Ordinal))
    {
        nome = nome.Replace("__", "_", StringComparison.Ordinal);
    }

    return nome.Trim('_');
}
