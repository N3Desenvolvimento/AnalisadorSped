# Contexto do projeto para agentes Codex

## Visao geral

- Solucao .NET 8 para importar, persistir e analisar arquivos SPED Fiscal (EFD ICMS/IPI) e EFD Contribuicoes.
- A interface e uma aplicacao Blazor Web App com componentes interativos no servidor e MudBlazor.
- A persistencia usa SQL Server, Dapper e repositorios explicitos.
- Arquivo de solucao: `N3.AnalisadorFiscal.sln`.
- Projeto inicial: `N3.AnalisadorFiscal.Web.csproj`.

## Projetos e dependencias

```text
N3.AnalisadorFiscal.Web
|-- N3.AnalisadorFiscal.Sped
|   |-- N3.AnalisadorFiscal.Data
|   |   `-- N3.AnalisadorFiscal.Domain
|   `-- N3.AnalisadorFiscal.Domain
|-- N3.AnalisadorFiscal.Data
|   `-- N3.AnalisadorFiscal.Domain
`-- N3.AnalisadorFiscal.Domain
```

- `N3.AnalisadorFiscal.Domain`: entidades de negocio e modelos persistidos; nao referencia outros projetos.
- `N3.AnalisadorFiscal.Data`: conexao SQL Server, repositorios Dapper e DTOs de consulta dos dashboards; referencia Domain.
- `N3.AnalisadorFiscal.Sped`: parsers e servicos de importacao de EFD ICMS/IPI e EFD Contribuicoes; referencia Domain e Data.
- `N3.AnalisadorFiscal.Web`: composicao da aplicacao, paginas Razor, endpoints de download e servicos de Excel/PDF/consulta externa; referencia os tres projetos.

## Pastas importantes

- `Components/Pages`: paginas e rotas Blazor, incluindo importacao, dashboards e detalhamentos.
- `Components/Layout`: layout principal e navegacao.
- `Services`: geracao de Excel/PDF, leitura de guia ICMS e consulta ao Simples Nacional; estes servicos pertencem atualmente ao projeto Web.
- `N3.AnalisadorFiscal.Sped/Parsing`: leitura e transformacao dos registros dos arquivos EFD.
- `N3.AnalisadorFiscal.Sped/Services`: orquestracao da importacao e gravacao por repositorios.
- `N3.AnalisadorFiscal.Data/Repositories`: contratos e implementacoes Dapper.
- `N3.AnalisadorFiscal.Data/Dashboard`: DTOs de leitura usados pelos dashboards e relatorios.
- `N3.AnalisadorFiscal.Domain/Entities`: entidades centrais.
- `Database`: scripts SQL para o esquema fiscal e de contribuicoes.
- `App_Data/Uploads`: arquivos recebidos em tempo de execucao; trate-os como dados locais, nao como codigo-fonte.
- `wwwroot`: CSS, imagens e outros recursos estaticos.

## Fluxos principais

### Importacao

1. Uma pagina em `Components/Pages/Importar*.razor` recebe o arquivo.
2. O arquivo e salvo em `App_Data/Uploads/<id>/`.
3. Um servico de `N3.AnalisadorFiscal.Sped/Services` chama o parser correspondente.
4. O servico resolve empresa, participantes, unidades e produtos e persiste os registros pelos repositorios de Data.
5. As paginas de dashboard consultam as tabelas persistidas pelos repositorios de dashboard.

### Consulta e exportacao

1. Paginas Razor injetam diretamente repositorios de dashboard.
2. DTOs agregados de `N3.AnalisadorFiscal.Data/Dashboard` retornam os dados de leitura.
3. Endpoints `/downloads/*` em `Program.cs` geram planilhas ou PDFs por servicos do projeto Web.

## Composicao e configuracao

- `Program.cs` e a raiz de composicao e registra `AddDataAccess()`, `AddSpedServices()`, servicos Web e o cliente HTTP da BrasilAPI.
- `N3.AnalisadorFiscal.Data/DependencyInjection.cs` registra a fabrica de conexao e os repositorios.
- `N3.AnalisadorFiscal.Sped/DependencyInjection.cs` registra parsers e importadores.
- A conexao e lida de `ConnectionStrings:DefaultConnection`.
- Nunca grave credenciais reais em arquivos versionados. Prefira Secret Manager, variaveis de ambiente ou configuracao externa e mantenha apenas exemplos seguros no repositorio.

## Regras para alteracoes

- Preserve a direcao atual das dependencias: Domain nao deve depender de Data, Sped ou Web; Data nao deve depender de Sped ou Web.
- Coloque regras de parsing/importacao em Sped, acesso SQL em Data, entidades compartilhadas em Domain e apresentacao/HTTP em Web.
- Ao alterar registros SPED, revise em conjunto parser, resultado parseado, entidade, repositorio e script SQL correspondente.
- Ao alterar consultas de dashboard, revise DTO, repositorio, pagina consumidora e exportacoes relacionadas.
- Use consultas parametrizadas no Dapper. Nao componha SQL com valores provenientes do arquivo ou da interface.
- Nao edite nem remova arquivos em `App_Data/Uploads` sem solicitacao explicita.
- Nao inclua `bin`, `obj`, arquivos `.user`, uploads, segredos ou artefatos gerados em commits.
- Ha dois arquivos SQL independentes em `Database`; mantenha mudancas de esquema repetiveis e compativeis com bases existentes.

## Validacao

- Compilar a solucao: `dotnet build N3.AnalisadorFiscal.sln`.
- Estado-base observado em 2026-07-10: compilacao bem-sucedida, sem avisos nem erros.
- Nao ha projeto de testes automatizados na solucao. Para mudancas de parser, persistencia ou calculo fiscal, priorize adicionar testes antes de refatoracoes extensas.

## Pontos de atencao arquitetural

- Sped depende diretamente de Data, portanto a orquestracao de importacao esta acoplada aos repositorios concretos desta solucao. Se surgir necessidade de outras persistencias ou testes isolados, considere mover contratos para uma camada de aplicacao/abstracoes.
- As paginas Web consultam repositorios Data diretamente. Isso e simples no tamanho atual, mas regras de negocio crescentes devem migrar para servicos de aplicacao em vez de permanecer nos componentes Razor.
- `Program.cs` concentra varios endpoints e regras de classificacao de CFOP. Ao ampliar exportacoes, considere grupos de endpoints e um servico dedicado para a regra fiscal compartilhada.
- DTOs de dashboard residem em Data e vazam para Web. Se os contratos de leitura se tornarem estaveis ou compartilhados, considere um projeto de Application/Contracts.
- Existem paginas de exemplo (`Counter.razor` e `Weather.razor`) fora do menu; confirme sua finalidade antes de mante-las ou remove-las.
- O projeto Web exclui explicitamente da compilacao os fontes das subpastas dos projetos referenciados, pois todos estao abaixo da raiz do projeto Web. Preserve essas exclusoes enquanto essa disposicao fisica continuar.
