import fs from "node:fs/promises";
import { Workbook, SpreadsheetFile } from "@oai/artifact-tool";

const dir = new URL("./", import.meta.url).pathname.replace(/^\/(.:)/, "$1");
const payload = JSON.parse((await fs.readFile(`${dir}dados.json`, "utf8")).replace(/^\uFEFF/, ""));
const rows = payload.rows;
if (!rows.length) throw new Error("Nenhuma ocorrência para gerar a análise.");
const num = v => Number(v ?? 0);
const txt = v => v == null ? "" : String(v);
const id = v => v == null || String(v) === "" ? "" : `\u200B${String(v)}`;
const dt = v => { const m=String(v??"").match(/^\/Date\((\d+)/); const d=new Date(m?Number(m[1]):v); return Number.isNaN(d.getTime())?null:d; };
const round = v => Math.round((Number(v)+Number.EPSILON)*100)/100;
const noteKey = r => txt(r.ChaveNfe)||`${txt(r.DocumentoFornecedor)}|${txt(r.NumeroNota)}|${txt(r.DataEntrada)}`;

const groups = new Map();
for (const r of rows) {
  const k=`${r.Fornecedor}|${r.DocumentoFornecedor}`;
  if(!groups.has(k)) groups.set(k,{fornecedor:r.Fornecedor,doc:r.DocumentoFornecedor,notas:new Set(),itens:0,valor:0,div:0});
  const g=groups.get(k); g.notas.add(noteKey(r)); g.itens++; g.valor+=num(r.ValorItem); if(r.SituacaoNcm!=="NCM correto") g.div++;
}
const summary=[...groups.values()].sort((a,b)=>b.valor-a.valor);
const wb=Workbook.create(); const s=wb.worksheets.add("Resumo por fornecedor"); const d=wb.worksheets.add("Itens analisados");
const navy="#17365D",blue="#D9EAF7",red="#FCE8E6",border="#CBD5E1";
function heading(sh,end,title,sub){sh.showGridLines=false;sh.getRange(`A1:${end}1`).merge();sh.getRange("A1").values=[[title]];sh.getRange(`A1:${end}1`).format={fill:navy,font:{bold:true,color:"#FFFFFF",size:16},rowHeight:30};sh.getRange(`A2:${end}2`).merge();sh.getRange("A2").values=[[sub]];sh.getRange(`A2:${end}2`).format={fill:blue,font:{italic:true,color:navy},rowHeight:24};}
heading(s,"F","Extensões com ICMS zerado — análise do NCM",`${rows[0].Empresa} | Código Fortes 0158 | NCM esperado 85444200 | Fonte: base SQL do Analisador SPED`);
s.getRange("A4:F5").values=[["Fornecedores",summary.length,"Notas",new Set(rows.map(noteKey)).size,"Itens",rows.length],["Valor dos itens",round(rows.reduce((a,r)=>a+num(r.ValorItem),0)),"NCM correto",rows.filter(r=>r.SituacaoNcm==="NCM correto").length,"NCM divergente/ausente",rows.filter(r=>r.SituacaoNcm!=="NCM correto").length]];
s.getRange("A4:F5").format={fill:"#F5F8FC",borders:{preset:"all",style:"thin",color:border}};s.getRange("A4:F5").format.font={color:navy};s.getRange("B5").setNumberFormat("R$ #,##0.00");
const sh=["Fornecedor","CNPJ/CPF","Qtd. notas","Qtd. itens","Valor dos itens","Itens com NCM divergente/ausente"];
const sd=summary.map(g=>[g.fornecedor,id(g.doc),g.notas.size,g.itens,round(g.valor),g.div]);s.getRange(`A7:F${7+sd.length}`).values=[sh,...sd];s.tables.add(`A7:F${7+sd.length}`,true,"ResumoExtensoes").style="TableStyleMedium2";s.getRange(`B8:B${7+sd.length}`).setNumberFormat("@");s.getRange(`E8:E${7+sd.length}`).setNumberFormat("R$ #,##0.00");s.freezePanes.freezeRows(7);for(const [c,w] of Object.entries({A:42,B:20,C:13,D:13,E:28,F:28}))s.getRange(`${c}:${c}`).format.columnWidth=w;

heading(d,"V","Itens iniciados por “EXTENSÃO” com ICMS zerado",`NCM esperado 85444200 | As ocorrências abaixo não apresentaram correspondência exata no NCM cadastrado`);
const dh=["Fornecedor","CNPJ/CPF","Entrada","Emissão","Série","Nota","Chave NF-e","Item","Código produto","Produto","NCM cadastrado","NCM esperado","Situação NCM","CFOP","CST ICMS","Quantidade","Unidade","Valor item","Base ICMS","Alíquota ICMS","ICMS destacado","Situação doc."];
const dd=rows.map(r=>[r.Fornecedor,id(r.DocumentoFornecedor),dt(r.DataEntrada),dt(r.DataEmissao),id(r.Serie),id(r.NumeroNota),id(r.ChaveNfe),id(r.NumeroItem),id(r.CodigoItem),r.Produto,id(r.NcmCadastrado),id(r.NcmEsperado),r.SituacaoNcm,id(r.Cfop),id(r.CstIcms),num(r.Quantidade),r.Unidade,num(r.ValorItem),num(r.BaseIcms),num(r.AliquotaIcms)/100,num(r.IcmsDestacado),id(r.SituacaoDocumento)]);
d.getRange(`A4:V${4+dd.length}`).values=[dh,...dd];d.tables.add(`A4:V${4+dd.length}`,true,"ItensExtensoes").style="TableStyleMedium2";d.getRange(`C5:D${4+dd.length}`).setNumberFormat("dd/mm/yyyy");d.getRange(`B5:B${4+dd.length}`).setNumberFormat("@");d.getRange(`E5:O${4+dd.length}`).setNumberFormat("@");d.getRange(`R5:S${4+dd.length}`).setNumberFormat("R$ #,##0.00");d.getRange(`T5:T${4+dd.length}`).setNumberFormat("0.00%");d.getRange(`U5:U${4+dd.length}`).setNumberFormat("R$ #,##0.00");d.getRange(`M5:M${4+dd.length}`).format={fill:red,font:{bold:true,color:"#B91C1C"}};d.freezePanes.freezeRows(4);d.freezePanes.freezeColumns(2);
for(const [c,w] of Object.entries({A:38,B:18,C:13,D:13,E:8,F:12,G:46,H:8,I:16,J:46,K:16,L:16,M:22,N:9,O:11,P:13,Q:10,R:15,S:15,T:15,U:17,V:13}))d.getRange(`${c}:${c}`).format.columnWidth=w;
const out=await SpreadsheetFile.exportXlsx(wb);await out.save(`${dir}analise_extensoes_ncm_85444200_empresa_0158_2026.xlsx`);
for(const [name,file] of [["Resumo por fornecedor","preview_resumo.png"],["Itens analisados","preview_itens.png"]]){const p=await wb.render({sheetName:name,autoCrop:"all",scale:1,format:"png"});await fs.writeFile(`${dir}${file}`,new Uint8Array(await p.arrayBuffer()));}
console.log((await wb.inspect({kind:"region",sheetId:"Resumo por fornecedor",range:"A1:F12",maxChars:2500})).ndjson);
console.log((await wb.inspect({kind:"region",sheetId:"Itens analisados",range:"A1:V14",maxChars:3500})).ndjson);
console.log((await wb.inspect({kind:"match",searchTerm:"#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",options:{useRegex:true,maxResults:100},maxChars:1500})).ndjson);
