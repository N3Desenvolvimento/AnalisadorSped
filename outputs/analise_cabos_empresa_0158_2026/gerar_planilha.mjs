import fs from "node:fs/promises";
import { Workbook, SpreadsheetFile } from "@oai/artifact-tool";

const outputDir = new URL("./", import.meta.url).pathname.replace(/^\/(.:)/, "$1");
const raw = (await fs.readFile(`${outputDir}dados.json`, "utf8")).replace(/^\uFEFF/, "");
const payload = JSON.parse(raw);
const rows = payload.rows;
if (!rows.length) throw new Error("A consulta não retornou itens para o relatório.");

const money = (v) => Number(v ?? 0);
const text = (v) => v == null ? "" : String(v);
const literal = (v) => v == null || String(v) === "" ? "" : `\u200B${String(v)}`;
const rounded = (v) => Math.round((Number(v) + Number.EPSILON) * 100) / 100;
const date = (v) => {
  if (!v) return null;
  const match = String(v).match(/^\/Date\((\d+)(?:[+-]\d+)?\)\/$/);
  const parsed = new Date(match ? Number(match[1]) : v);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
};
const keyForNote = (r) => text(r.ChaveNfe) || `${text(r.DocumentoFornecedor)}|${text(r.NumeroNota)}|${text(r.Serie)}|${text(r.DataEntrada)}`;

const grouped = new Map();
for (const r of rows) {
  const key = `${text(r.Fornecedor)}|${text(r.DocumentoFornecedor)}`;
  if (!grouped.has(key)) grouped.set(key, { fornecedor: text(r.Fornecedor), documento: text(r.DocumentoFornecedor), notes: new Set(), items: 0, valor: 0, base: 0, icms: 0 });
  const g = grouped.get(key);
  g.notes.add(keyForNote(r));
  g.items += 1;
  g.valor += money(r.ValorItem);
  g.base += money(r.BaseIcms);
  g.icms += money(r.IcmsDestacado);
}
const summary = [...grouped.values()].sort((a, b) => b.icms - a.icms);
const uniqueNotes = new Set(rows.map(keyForNote)).size;
const totalValue = rounded(rows.reduce((s, r) => s + money(r.ValorItem), 0));
const totalIcms = rounded(rows.reduce((s, r) => s + money(r.IcmsDestacado), 0));

const wb = Workbook.create();
const resumo = wb.worksheets.add("Resumo por fornecedor");
const detalhes = wb.worksheets.add("Itens detalhados");
const navy = "#17365D", blue = "#D9EAF7", light = "#F5F8FC", border = "#CBD5E1", green = "#E2F0D9";

function title(sheet, endCol, titleText, subtitle) {
  sheet.showGridLines = false;
  sheet.getRange(`A1:${endCol}1`).merge();
  sheet.getRange("A1").values = [[titleText]];
  sheet.getRange(`A1:${endCol}1`).format = { fill: navy, font: { bold: true, color: "#FFFFFF", size: 16 }, rowHeight: 30, verticalAlignment: "center" };
  sheet.getRange(`A2:${endCol}2`).merge();
  sheet.getRange("A2").values = [[subtitle]];
  sheet.getRange(`A2:${endCol}2`).format = { fill: blue, font: { color: "#17365D", italic: true }, rowHeight: 24, verticalAlignment: "center" };
}

title(resumo, "G", "Compras de cabos com ICMS destacado — 2026", `${rows[0].Empresa} | Código fiscal Fortes 0158 | Fonte: base SQL do Analisador SPED`);
resumo.getRange("A4:G4").values = [["Fornecedores", summary.length, "Notas", uniqueNotes, "Valor dos itens", totalValue, "ICMS destacado"]];
resumo.getRange("A5:G5").values = [["Itens encontrados", rows.length, "Ano", 2026, "", "", totalIcms]];
resumo.getRange("A4:G5").format = { fill: light, borders: { preset: "all", style: "thin", color: border }, verticalAlignment: "center" };
resumo.getRange("A4:G4").format.font = { bold: true, color: navy };
resumo.getRange("F4:F5").setNumberFormat("R$ #,##0.00");
resumo.getRange("G4:G5").setNumberFormat("R$ #,##0.00");

const sumHeaders = ["Fornecedor", "CNPJ/CPF", "Qtd. notas", "Qtd. itens", "Valor dos itens", "Base de ICMS", "ICMS destacado"];
const sumData = summary.map(g => [g.fornecedor, literal(g.documento), g.notes.size, g.items, rounded(g.valor), rounded(g.base), rounded(g.icms)]);
resumo.getRange(`A7:G${7 + sumData.length}`).values = [sumHeaders, ...sumData];
const sumTable = resumo.tables.add(`A7:G${7 + sumData.length}`, true, "ResumoFornecedores");
sumTable.style = "TableStyleMedium2";
resumo.getRange(`E8:G${7 + sumData.length}`).setNumberFormat("R$ #,##0.00");
resumo.getRange(`B8:B${7 + sumData.length}`).setNumberFormat("@");
const totalRow = 8 + sumData.length;
resumo.getRange(`A${totalRow}:G${totalRow}`).values = [["TOTAL", "", "", "", null, null, null]];
resumo.getRange(`E${totalRow}:G${totalRow}`).formulas = [[`=ROUND(SUM(E8:E${totalRow-1}),2)`, `=ROUND(SUM(F8:F${totalRow-1}),2)`, `=ROUND(SUM(G8:G${totalRow-1}),2)`]];
resumo.getRange(`A${totalRow}:G${totalRow}`).format = { fill: green, font: { bold: true, color: navy }, borders: { preset: "all", style: "thin", color: border } };
resumo.getRange(`E${totalRow}:G${totalRow}`).setNumberFormat("R$ #,##0.00");
resumo.freezePanes.freezeRows(7);
resumo.getRange("A:A").format.columnWidth = 42;
resumo.getRange("B:B").format.columnWidth = 20;
resumo.getRange("C:D").format.columnWidth = 13;
resumo.getRange("E:G").format.columnWidth = 18;

title(detalhes, "T", "Itens de cabos com ICMS destacado", `${rows[0].Empresa} | Entradas válidas de 2026 | Descrição contém “CABO” e ICMS do item > 0`);
const detailHeaders = ["Fornecedor", "CNPJ/CPF", "Data entrada", "Emissão", "Série", "Nota", "Chave NF-e", "Item", "Código produto", "Produto", "NCM", "CFOP", "CST ICMS", "Quantidade", "Unidade", "Valor item", "Base ICMS", "Alíquota ICMS", "ICMS destacado", "Situação doc."];
const detailData = rows.map(r => [text(r.Fornecedor), literal(r.DocumentoFornecedor), date(r.DataEntrada), date(r.DataEmissao), text(r.Serie), text(r.NumeroNota), literal(r.ChaveNfe), text(r.NumeroItem), literal(r.CodigoItem), text(r.Produto), literal(r.Ncm), literal(r.Cfop), literal(r.CstIcms), money(r.Quantidade), text(r.Unidade), money(r.ValorItem), money(r.BaseIcms), money(r.AliquotaIcms) / 100, money(r.IcmsDestacado), literal(r.SituacaoDocumento)]);
detalhes.getRange(`A4:T${4 + detailData.length}`).values = [detailHeaders, ...detailData];
const detailTable = detalhes.tables.add(`A4:T${4 + detailData.length}`, true, "ItensCabos");
detailTable.style = "TableStyleMedium2";
detalhes.getRange(`C5:D${4 + detailData.length}`).setNumberFormat("dd/mm/yyyy");
detalhes.getRange(`B5:B${4 + detailData.length}`).setNumberFormat("@");
detalhes.getRange(`E5:M${4 + detailData.length}`).setNumberFormat("@");
detalhes.getRange(`T5:T${4 + detailData.length}`).setNumberFormat("@");
detalhes.getRange(`N5:N${4 + detailData.length}`).setNumberFormat("#,##0.000");
detalhes.getRange(`P5:Q${4 + detailData.length}`).setNumberFormat("R$ #,##0.00");
detalhes.getRange(`R5:R${4 + detailData.length}`).setNumberFormat("0.00%");
detalhes.getRange(`S5:S${4 + detailData.length}`).setNumberFormat("R$ #,##0.00");
detalhes.freezePanes.freezeRows(4);
detalhes.freezePanes.freezeColumns(2);
for (const [col, width] of Object.entries({A:38,B:18,C:13,D:13,E:9,F:12,G:46,H:8,I:16,J:48,K:12,L:9,M:11,N:13,O:10,P:15,Q:15,R:15,S:17,T:13})) detalhes.getRange(`${col}:${col}`).format.columnWidth = width;
detalhes.getRange(`J5:J${4 + detailData.length}`).format.wrapText = true;

const xlsx = await SpreadsheetFile.exportXlsx(wb);
await xlsx.save(`${outputDir}compras_cabos_empresa_0158_2026.xlsx`);
for (const sheetName of ["Resumo por fornecedor", "Itens detalhados"]) {
  const preview = await wb.render({ sheetName, autoCrop: "all", scale: 1, format: "png" });
  await fs.writeFile(`${outputDir}${sheetName.startsWith("Resumo") ? "preview_resumo" : "preview_detalhes"}.png`, new Uint8Array(await preview.arrayBuffer()));
}
console.log((await wb.inspect({ kind: "region", sheetId: "Resumo por fornecedor", range: `A1:G${Math.min(totalRow, 30)}`, maxChars: 5000 })).ndjson);
console.log((await wb.inspect({ kind: "region", sheetId: "Itens detalhados", range: "A1:T12", maxChars: 5000 })).ndjson);
console.log((await wb.inspect({ kind: "match", searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A", options: { useRegex: true, maxResults: 100 }, maxChars: 3000 })).ndjson);
