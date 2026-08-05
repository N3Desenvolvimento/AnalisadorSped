using System.Globalization;
using N3.AnalisadorFiscal.Data.Dashboard;
using N3.AnalisadorFiscal.Domain.Entities;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace N3.AnalisadorFiscal.Web.Services;

public interface IContrachequePdfService
{
    byte[] Gerar(Empresa empresa, FolhaEspelhoDto folha, int trabalhadorId = 0);
}

public sealed class ContrachequePdfService : IContrachequePdfService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly XPen Linha = new(XColors.Black, 0.7);
    private static readonly XBrush Fundo = new XSolidBrush(XColor.FromArgb(225, 225, 225));

    public byte[] Gerar(Empresa empresa, FolhaEspelhoDto folha, int trabalhadorId = 0)
    {
        using var documento = new PdfDocument();
        documento.Info.Title = $"Contracheques - {folha.RazaoSocial} - {folha.Mes:00}/{folha.Ano}";

        foreach (var trabalhador in folha.Trabalhadores.Where(x => trabalhadorId == 0 || x.Id == trabalhadorId))
        {
            var pagina = documento.AddPage();
            pagina.Size = PdfSharp.PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(pagina);
            DesenharVia(gfx, empresa, folha, trabalhador, 28, 28, pagina.Width.Point - 56, 375);
            DesenharVia(gfx, empresa, folha, trabalhador, 28, 439, pagina.Width.Point - 56, 375);
        }

        using var memoria = new MemoryStream();
        documento.Save(memoria, false);
        return memoria.ToArray();
    }

    private static void DesenharVia(XGraphics gfx, Empresa empresa, FolhaEspelhoDto folha,
        FolhaEspelhoTrabalhadorDto trabalhador, double x, double y, double largura, double altura)
    {
        var f8 = new XFont("Arial", 8);
        var f9 = new XFont("Arial", 9);
        var f9b = new XFont("Arial", 9, XFontStyleEx.Bold);
        var f12b = new XFont("Arial", 12, XFontStyleEx.Bold);
        var f14b = new XFont("Arial", 14, XFontStyleEx.Bold);

        gfx.DrawRectangle(Linha, x, y, largura, altura);
        var tituloLargura = largura * 0.44;
        gfx.DrawLine(Linha, x + tituloLargura, y, x + tituloLargura, y + 31);
        Centralizar(gfx, "Recibo de Pagamento", f14b, x, y + 2, tituloLargura, 16);
        Centralizar(gfx, "( Folha de Pagamento )", f9, x, y + 17, tituloLargura, 12);
        Texto(gfx, "Data e Assinatura", f8, x + tituloLargura + 5, y + 3);
        Texto(gfx, "____/____/________     __________________________________________", f9, x + tituloLargura + 5, y + 18);
        gfx.DrawLine(Linha, x, y + 31, x + largura, y + 31);

        var empresaY = y + 31;
        var colEmpresa = largura * 0.44;
        var colInscricao = largura * 0.22;
        Campo(gfx, "Empregador", folha.RazaoSocial, f8, f9, x, empresaY, colEmpresa, 28);
        Campo(gfx, "Inscrição", FormatarCnpj(empresa.Cnpj), f8, f9, x + colEmpresa, empresaY, colInscricao, 28);
        Campo(gfx, "Competência", $"{NomeMes(folha.Mes)} de {folha.Ano}", f8, f9,
            x + colEmpresa + colInscricao, empresaY, largura - colEmpresa - colInscricao, 28, false);

        var empregadoY = empresaY + 28;
        Campo(gfx, "Empregado", $"{trabalhador.CodigoFortes} {trabalhador.Nome}", f8, f9, x, empregadoY, largura * 0.72, 28);
        Campo(gfx, "Lotação", "GERAL", f8, f9, x + largura * 0.72, empregadoY, largura * 0.28, 28, false);

        var verbasY = empregadoY + 28;
        gfx.DrawLine(Linha, x, verbasY, x + largura, verbasY);
        Centralizar(gfx, "Discriminação das Verbas", f9b, x, verbasY + 2, largura, 13);
        var tabelaY = verbasY + 17;
        gfx.DrawLine(Linha, x, tabelaY, x + largura, tabelaY);

        var colunas = new[] { 0d, 0.06, 0.58, 0.70, 0.85, 1d }.Select(p => x + largura * p).ToArray();
        var rodapeY = y + altura - 52;
        for (var i = 1; i < colunas.Length - 1; i++)
            gfx.DrawLine(Linha, colunas[i], tabelaY, colunas[i], rodapeY);

        Cabecalho(gfx, "Cód.", f8, colunas[0], tabelaY + 2, colunas[1] - colunas[0]);
        Cabecalho(gfx, "Descrição", f8, colunas[1] + 5, tabelaY + 2, colunas[2] - colunas[1] - 5, false);
        Cabecalho(gfx, "Referência", f8, colunas[2], tabelaY + 2, colunas[3] - colunas[2]);
        Cabecalho(gfx, "Provento", f8, colunas[3], tabelaY + 2, colunas[4] - colunas[3]);
        Cabecalho(gfx, "Desconto", f8, colunas[4], tabelaY + 2, colunas[5] - colunas[4]);
        gfx.DrawLine(Linha, x, tabelaY + 16, x + largura, tabelaY + 16);

        var eventos = trabalhador.Eventos.Where(x => x.TipoEvento is 1 or -1).ToArray();
        var alturaLinha = Math.Min(15, (rodapeY - tabelaY - 20) / Math.Max(eventos.Length, 1));
        for (var i = 0; i < eventos.Length; i++)
        {
            var evento = eventos[i];
            var linhaY = tabelaY + 20 + i * alturaLinha;
            Texto(gfx, evento.CodigoFortes, f9, colunas[0] + 6, linhaY);
            TextoLimitado(gfx, evento.Nome, f9, colunas[1] + 5, linhaY, colunas[2] - colunas[1] - 10);
            Direita(gfx, Referencia(evento.Referencia), f9, colunas[2] + 4, linhaY, colunas[3] - colunas[2] - 8);
            Direita(gfx, evento.TipoEvento == 1 ? Numero(evento.Valor) : "", f9, colunas[3] + 4, linhaY, colunas[4] - colunas[3] - 8);
            Direita(gfx, evento.TipoEvento == -1 ? Numero(evento.Valor) : "", f9, colunas[4] + 4, linhaY, colunas[5] - colunas[4] - 8);
        }

        gfx.DrawLine(Linha, x, rodapeY, x + largura, rodapeY);
        var totaisX = colunas[3];
        gfx.DrawLine(Linha, totaisX, rodapeY, totaisX, y + altura);
        gfx.DrawLine(Linha, colunas[4], rodapeY, colunas[4], rodapeY + 29);
        Centralizar(gfx, "Total de Proventos", f8, totaisX, rodapeY + 2, colunas[4] - totaisX, 10);
        Centralizar(gfx, Numero(trabalhador.TotalProventos), f12b, totaisX, rodapeY + 13, colunas[4] - totaisX, 14);
        Centralizar(gfx, "Total de Descontos", f8, colunas[4], rodapeY + 2, x + largura - colunas[4], 10);
        Centralizar(gfx, Numero(trabalhador.TotalDescontos), f12b, colunas[4], rodapeY + 13, x + largura - colunas[4], 14);
        gfx.DrawLine(Linha, totaisX, rodapeY + 29, x + largura, rodapeY + 29);
        gfx.DrawRectangle(Fundo, totaisX, rodapeY + 30, x + largura - totaisX - 0.5, 21.5);
        Centralizar(gfx, "Líquido a Receber", f8, totaisX, rodapeY + 30, x + largura - totaisX, 9);
        Direita(gfx, Numero(trabalhador.Liquido), f12b, totaisX, rodapeY + 40, x + largura - totaisX - 7);
    }

    private static void Campo(XGraphics gfx, string rotulo, string valor, XFont fr, XFont fv,
        double x, double y, double largura, double altura, bool bordaDireita = true)
    {
        if (bordaDireita) gfx.DrawLine(Linha, x + largura, y, x + largura, y + altura);
        Texto(gfx, rotulo, fr, x + 5, y + 3);
        TextoLimitado(gfx, valor, fv, x + 5, y + 15, largura - 10);
    }

    private static void Cabecalho(XGraphics gfx, string texto, XFont fonte, double x, double y, double largura, bool centro = true) =>
        gfx.DrawString(texto, fonte, XBrushes.Black, new XRect(x, y, largura, 12), centro ? XStringFormats.Center : XStringFormats.CenterLeft);
    private static void Centralizar(XGraphics gfx, string texto, XFont fonte, double x, double y, double largura, double altura) =>
        gfx.DrawString(texto, fonte, XBrushes.Black, new XRect(x, y, largura, altura), XStringFormats.Center);
    private static void Direita(XGraphics gfx, string texto, XFont fonte, double x, double y, double largura) =>
        gfx.DrawString(texto, fonte, XBrushes.Black, new XRect(x, y, largura, 12), XStringFormats.CenterRight);
    private static void Texto(XGraphics gfx, string texto, XFont fonte, double x, double y) =>
        gfx.DrawString(texto, fonte, XBrushes.Black, x, y + fonte.Size);

    private static void TextoLimitado(XGraphics gfx, string texto, XFont fonte, double x, double y, double largura)
    {
        while (texto.Length > 1 && gfx.MeasureString(texto, fonte).Width > largura)
            texto = texto[..^1];
        Texto(gfx, texto, fonte, x, y);
    }

    private static string Numero(decimal valor) => valor.ToString("N2", PtBr);
    private static string Referencia(decimal valor) => valor == 0 ? "" : valor.ToString("N2", PtBr);
    private static string NomeMes(int mes) => PtBr.DateTimeFormat.GetMonthName(mes);
    private static string FormatarCnpj(string? cnpj)
    {
        var digitos = new string((cnpj ?? "").Where(char.IsDigit).ToArray());
        return digitos.Length == 14 ? Convert.ToUInt64(digitos).ToString(@"00\.000\.000\/0000\-00") : cnpj ?? "";
    }
}
