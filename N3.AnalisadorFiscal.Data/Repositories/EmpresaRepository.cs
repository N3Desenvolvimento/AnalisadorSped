using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IEmpresaRepository
{
    Task<int> InsertAsync(Empresa empresa, CancellationToken cancellationToken = default);
    Task<Empresa?> GetByCnpjAsync(string cnpj, CancellationToken cancellationToken = default);
    Task<Empresa?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Empresa>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(Empresa empresa, CancellationToken cancellationToken = default);
}

public sealed class EmpresaRepository : RepositoryBase, IEmpresaRepository
{
    public EmpresaRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<int> InsertAsync(Empresa empresa, CancellationToken cancellationToken = default)
    {
        await EnsureCadastroColumnsAsync(cancellationToken);
        const string sql = """
            INSERT INTO EMPRESA (CNPJ, RAZAO_SOCIAL, NOME_FANTASIA, UF, IE, COD_MUNICIPIO,
                DATA_ABERTURA, CNAE_PRINCIPAL_CODIGO, CNAE_PRINCIPAL_DESCRICAO, CNAES_SECUNDARIOS,
                NATUREZA_JURIDICA, PORTE, SITUACAO_CADASTRAL, DATA_SITUACAO_CADASTRAL,
                MOTIVO_SITUACAO_CADASTRAL, SITUACAO_ESPECIAL, DATA_SITUACAO_ESPECIAL,
                TIPO_LOGRADOURO, LOGRADOURO, NUMERO, COMPLEMENTO, CEP, BAIRRO, MUNICIPIO,
                EMAIL, TELEFONE, ENTE_FEDERATIVO_RESPONSAVEL, SINCRONIZADO_EM,
                REGIME_TRIBUTARIO, LOGOMARCA, LOGOMARCA_CONTENT_TYPE, LOGOMARCA_NOME_ARQUIVO,
                CERTIFICADO_THUMBPRINT, CERTIFICADO_STORE_LOCATION, ATIVO)
            OUTPUT INSERTED.ID_EMPRESA
            VALUES (@Cnpj, @RazaoSocial, @NomeFantasia, @Uf, @InscricaoEstadual, @CodigoMunicipio,
                @DataAbertura, @CnaePrincipalCodigo, @CnaePrincipalDescricao, @CnaesSecundarios,
                @NaturezaJuridica, @Porte, @SituacaoCadastral, @DataSituacaoCadastral,
                @MotivoSituacaoCadastral, @SituacaoEspecial, @DataSituacaoEspecial,
                @TipoLogradouro, @Logradouro, @Numero, @Complemento, @Cep, @Bairro, @Municipio,
                @Email, @Telefone, @EnteFederativoResponsavel, @SincronizadoEm,
                @RegimeTributario, @Logomarca, @LogomarcaContentType, @LogomarcaNomeArquivo,
                @CertificadoThumbprint, @CertificadoStoreLocation, 1);
            """;

        return await InsertAsync(sql, empresa, cancellationToken);
    }

    public async Task<Empresa?> GetByCnpjAsync(string cnpj, CancellationToken cancellationToken = default)
    {
        await EnsureCadastroColumnsAsync(cancellationToken);
        const string sql = """
            SELECT TOP 1
                ID_EMPRESA AS Id,
                CNPJ AS Cnpj,
                RAZAO_SOCIAL AS RazaoSocial,
                NOME_FANTASIA AS NomeFantasia,
                IE AS InscricaoEstadual,
                UF AS Uf,
                COD_MUNICIPIO AS CodigoMunicipio
                ,REGIME_TRIBUTARIO AS RegimeTributario
                ,DATA_ABERTURA AS DataAbertura, CNAE_PRINCIPAL_CODIGO AS CnaePrincipalCodigo
                ,CNAE_PRINCIPAL_DESCRICAO AS CnaePrincipalDescricao, CNAES_SECUNDARIOS AS CnaesSecundarios
                ,NATUREZA_JURIDICA AS NaturezaJuridica, PORTE AS Porte, SITUACAO_CADASTRAL AS SituacaoCadastral
                ,DATA_SITUACAO_CADASTRAL AS DataSituacaoCadastral, MOTIVO_SITUACAO_CADASTRAL AS MotivoSituacaoCadastral
                ,SITUACAO_ESPECIAL AS SituacaoEspecial, DATA_SITUACAO_ESPECIAL AS DataSituacaoEspecial
                ,TIPO_LOGRADOURO AS TipoLogradouro, LOGRADOURO AS Logradouro, NUMERO AS Numero
                ,COMPLEMENTO AS Complemento, CEP AS Cep, BAIRRO AS Bairro, MUNICIPIO AS Municipio
                ,EMAIL AS Email, TELEFONE AS Telefone, ENTE_FEDERATIVO_RESPONSAVEL AS EnteFederativoResponsavel
                ,SINCRONIZADO_EM AS SincronizadoEm
                ,LOGOMARCA AS Logomarca
                ,LOGOMARCA_CONTENT_TYPE AS LogomarcaContentType
                ,LOGOMARCA_NOME_ARQUIVO AS LogomarcaNomeArquivo
                ,CERTIFICADO_THUMBPRINT AS CertificadoThumbprint
                ,CERTIFICADO_STORE_LOCATION AS CertificadoStoreLocation
            FROM EMPRESA
            WHERE CNPJ = @Cnpj;
            """;

        return await QuerySingleOrDefaultAsync<Empresa>(sql, new { Cnpj = cnpj }, cancellationToken);
    }

    public async Task<Empresa?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureCadastroColumnsAsync(cancellationToken);
        const string sql = """
            SELECT ID_EMPRESA AS Id, CNPJ AS Cnpj, RAZAO_SOCIAL AS RazaoSocial,
                NOME_FANTASIA AS NomeFantasia, IE AS InscricaoEstadual,
                UF AS Uf, COD_MUNICIPIO AS CodigoMunicipio,
                REGIME_TRIBUTARIO AS RegimeTributario, LOGOMARCA AS Logomarca,
                DATA_ABERTURA AS DataAbertura, CNAE_PRINCIPAL_CODIGO AS CnaePrincipalCodigo,
                CNAE_PRINCIPAL_DESCRICAO AS CnaePrincipalDescricao, CNAES_SECUNDARIOS AS CnaesSecundarios,
                NATUREZA_JURIDICA AS NaturezaJuridica, PORTE AS Porte, SITUACAO_CADASTRAL AS SituacaoCadastral,
                DATA_SITUACAO_CADASTRAL AS DataSituacaoCadastral, MOTIVO_SITUACAO_CADASTRAL AS MotivoSituacaoCadastral,
                SITUACAO_ESPECIAL AS SituacaoEspecial, DATA_SITUACAO_ESPECIAL AS DataSituacaoEspecial,
                TIPO_LOGRADOURO AS TipoLogradouro, LOGRADOURO AS Logradouro, NUMERO AS Numero,
                COMPLEMENTO AS Complemento, CEP AS Cep, BAIRRO AS Bairro, MUNICIPIO AS Municipio,
                EMAIL AS Email, TELEFONE AS Telefone, ENTE_FEDERATIVO_RESPONSAVEL AS EnteFederativoResponsavel,
                SINCRONIZADO_EM AS SincronizadoEm,
                LOGOMARCA_CONTENT_TYPE AS LogomarcaContentType,
                LOGOMARCA_NOME_ARQUIVO AS LogomarcaNomeArquivo,
                CERTIFICADO_THUMBPRINT AS CertificadoThumbprint,
                CERTIFICADO_STORE_LOCATION AS CertificadoStoreLocation
            FROM EMPRESA WHERE ID_EMPRESA=@Id;
            """;
        return await QuerySingleOrDefaultAsync<Empresa>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<IReadOnlyList<Empresa>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCadastroColumnsAsync(cancellationToken);
        const string sql = """
            SELECT ID_EMPRESA AS Id, CNPJ AS Cnpj, RAZAO_SOCIAL AS RazaoSocial,
                NOME_FANTASIA AS NomeFantasia, IE AS InscricaoEstadual,
                UF AS Uf, COD_MUNICIPIO AS CodigoMunicipio,
                REGIME_TRIBUTARIO AS RegimeTributario, LOGOMARCA AS Logomarca,
                DATA_ABERTURA AS DataAbertura, CNAE_PRINCIPAL_CODIGO AS CnaePrincipalCodigo,
                CNAE_PRINCIPAL_DESCRICAO AS CnaePrincipalDescricao, CNAES_SECUNDARIOS AS CnaesSecundarios,
                NATUREZA_JURIDICA AS NaturezaJuridica, PORTE AS Porte, SITUACAO_CADASTRAL AS SituacaoCadastral,
                DATA_SITUACAO_CADASTRAL AS DataSituacaoCadastral, MOTIVO_SITUACAO_CADASTRAL AS MotivoSituacaoCadastral,
                SITUACAO_ESPECIAL AS SituacaoEspecial, DATA_SITUACAO_ESPECIAL AS DataSituacaoEspecial,
                TIPO_LOGRADOURO AS TipoLogradouro, LOGRADOURO AS Logradouro, NUMERO AS Numero,
                COMPLEMENTO AS Complemento, CEP AS Cep, BAIRRO AS Bairro, MUNICIPIO AS Municipio,
                EMAIL AS Email, TELEFONE AS Telefone, ENTE_FEDERATIVO_RESPONSAVEL AS EnteFederativoResponsavel,
                SINCRONIZADO_EM AS SincronizadoEm,
                LOGOMARCA_CONTENT_TYPE AS LogomarcaContentType,
                LOGOMARCA_NOME_ARQUIVO AS LogomarcaNomeArquivo,
                CERTIFICADO_THUMBPRINT AS CertificadoThumbprint,
                CERTIFICADO_STORE_LOCATION AS CertificadoStoreLocation
            FROM EMPRESA WHERE COALESCE(ATIVO, 1)=1 ORDER BY RAZAO_SOCIAL;
            """;
        await using var connection = ConnectionFactory.CreateConnection();
        return (await Dapper.SqlMapper.QueryAsync<Empresa>(connection,
            new Dapper.CommandDefinition(sql, cancellationToken: cancellationToken))).ToList();
    }

    public async Task UpdateAsync(Empresa empresa, CancellationToken cancellationToken = default)
    {
        await EnsureCadastroColumnsAsync(cancellationToken);
        const string sql = """
            UPDATE EMPRESA SET CNPJ=@Cnpj, RAZAO_SOCIAL=@RazaoSocial,
                NOME_FANTASIA=@NomeFantasia, IE=@InscricaoEstadual,
                UF=@Uf, COD_MUNICIPIO=@CodigoMunicipio,
                DATA_ABERTURA=@DataAbertura, CNAE_PRINCIPAL_CODIGO=@CnaePrincipalCodigo,
                CNAE_PRINCIPAL_DESCRICAO=@CnaePrincipalDescricao, CNAES_SECUNDARIOS=@CnaesSecundarios,
                NATUREZA_JURIDICA=@NaturezaJuridica, PORTE=@Porte, SITUACAO_CADASTRAL=@SituacaoCadastral,
                DATA_SITUACAO_CADASTRAL=@DataSituacaoCadastral, MOTIVO_SITUACAO_CADASTRAL=@MotivoSituacaoCadastral,
                SITUACAO_ESPECIAL=@SituacaoEspecial, DATA_SITUACAO_ESPECIAL=@DataSituacaoEspecial,
                TIPO_LOGRADOURO=@TipoLogradouro, LOGRADOURO=@Logradouro, NUMERO=@Numero,
                COMPLEMENTO=@Complemento, CEP=@Cep, BAIRRO=@Bairro, MUNICIPIO=@Municipio,
                EMAIL=@Email, TELEFONE=@Telefone, ENTE_FEDERATIVO_RESPONSAVEL=@EnteFederativoResponsavel,
                SINCRONIZADO_EM=@SincronizadoEm,
                REGIME_TRIBUTARIO=@RegimeTributario, LOGOMARCA=@Logomarca,
                LOGOMARCA_CONTENT_TYPE=@LogomarcaContentType,
                LOGOMARCA_NOME_ARQUIVO=@LogomarcaNomeArquivo,
                CERTIFICADO_THUMBPRINT=@CertificadoThumbprint,
                CERTIFICADO_STORE_LOCATION=@CertificadoStoreLocation
            WHERE ID_EMPRESA=@Id;
            """;
        await ExecuteAsync(sql, empresa, cancellationToken);
    }

    private async Task EnsureCadastroColumnsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            IF COL_LENGTH('dbo.EMPRESA', 'NOME_FANTASIA') IS NULL
                ALTER TABLE dbo.EMPRESA ADD NOME_FANTASIA VARCHAR(255) NULL;

            IF COL_LENGTH('dbo.EMPRESA', 'ATIVO') IS NULL
                ALTER TABLE dbo.EMPRESA ADD ATIVO BIT NOT NULL
                    CONSTRAINT DF_EMPRESA_ATIVO DEFAULT 1 WITH VALUES;

            IF COL_LENGTH('dbo.EMPRESA', 'CERTIFICADO_THUMBPRINT') IS NULL
                ALTER TABLE dbo.EMPRESA ADD CERTIFICADO_THUMBPRINT VARCHAR(64) NULL;

            IF COL_LENGTH('dbo.EMPRESA', 'CERTIFICADO_STORE_LOCATION') IS NULL
                ALTER TABLE dbo.EMPRESA ADD CERTIFICADO_STORE_LOCATION VARCHAR(20) NULL;

            IF COL_LENGTH('dbo.EMPRESA', 'REGIME_TRIBUTARIO') IS NULL
                ALTER TABLE dbo.EMPRESA ADD REGIME_TRIBUTARIO VARCHAR(20) NULL;

            IF COL_LENGTH('dbo.EMPRESA', 'LOGOMARCA') IS NULL
                ALTER TABLE dbo.EMPRESA ADD LOGOMARCA VARBINARY(MAX) NULL;

            IF COL_LENGTH('dbo.EMPRESA', 'LOGOMARCA_CONTENT_TYPE') IS NULL
                ALTER TABLE dbo.EMPRESA ADD LOGOMARCA_CONTENT_TYPE VARCHAR(100) NULL;

            IF COL_LENGTH('dbo.EMPRESA', 'LOGOMARCA_NOME_ARQUIVO') IS NULL
                ALTER TABLE dbo.EMPRESA ADD LOGOMARCA_NOME_ARQUIVO VARCHAR(255) NULL;

            IF COL_LENGTH('dbo.EMPRESA', 'DATA_ABERTURA') IS NULL ALTER TABLE dbo.EMPRESA ADD DATA_ABERTURA DATE NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'CNAE_PRINCIPAL_CODIGO') IS NULL ALTER TABLE dbo.EMPRESA ADD CNAE_PRINCIPAL_CODIGO VARCHAR(10) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'CNAE_PRINCIPAL_DESCRICAO') IS NULL ALTER TABLE dbo.EMPRESA ADD CNAE_PRINCIPAL_DESCRICAO VARCHAR(500) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'CNAES_SECUNDARIOS') IS NULL ALTER TABLE dbo.EMPRESA ADD CNAES_SECUNDARIOS VARCHAR(MAX) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'NATUREZA_JURIDICA') IS NULL ALTER TABLE dbo.EMPRESA ADD NATUREZA_JURIDICA VARCHAR(255) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'PORTE') IS NULL ALTER TABLE dbo.EMPRESA ADD PORTE VARCHAR(100) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'SITUACAO_CADASTRAL') IS NULL ALTER TABLE dbo.EMPRESA ADD SITUACAO_CADASTRAL VARCHAR(100) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'DATA_SITUACAO_CADASTRAL') IS NULL ALTER TABLE dbo.EMPRESA ADD DATA_SITUACAO_CADASTRAL DATE NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'MOTIVO_SITUACAO_CADASTRAL') IS NULL ALTER TABLE dbo.EMPRESA ADD MOTIVO_SITUACAO_CADASTRAL VARCHAR(255) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'SITUACAO_ESPECIAL') IS NULL ALTER TABLE dbo.EMPRESA ADD SITUACAO_ESPECIAL VARCHAR(255) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'DATA_SITUACAO_ESPECIAL') IS NULL ALTER TABLE dbo.EMPRESA ADD DATA_SITUACAO_ESPECIAL DATE NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'TIPO_LOGRADOURO') IS NULL ALTER TABLE dbo.EMPRESA ADD TIPO_LOGRADOURO VARCHAR(50) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'LOGRADOURO') IS NULL ALTER TABLE dbo.EMPRESA ADD LOGRADOURO VARCHAR(255) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'NUMERO') IS NULL ALTER TABLE dbo.EMPRESA ADD NUMERO VARCHAR(30) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'COMPLEMENTO') IS NULL ALTER TABLE dbo.EMPRESA ADD COMPLEMENTO VARCHAR(255) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'CEP') IS NULL ALTER TABLE dbo.EMPRESA ADD CEP VARCHAR(8) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'BAIRRO') IS NULL ALTER TABLE dbo.EMPRESA ADD BAIRRO VARCHAR(150) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'MUNICIPIO') IS NULL ALTER TABLE dbo.EMPRESA ADD MUNICIPIO VARCHAR(150) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'EMAIL') IS NULL ALTER TABLE dbo.EMPRESA ADD EMAIL VARCHAR(255) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'TELEFONE') IS NULL ALTER TABLE dbo.EMPRESA ADD TELEFONE VARCHAR(100) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'ENTE_FEDERATIVO_RESPONSAVEL') IS NULL ALTER TABLE dbo.EMPRESA ADD ENTE_FEDERATIVO_RESPONSAVEL VARCHAR(255) NULL;
            IF COL_LENGTH('dbo.EMPRESA', 'SINCRONIZADO_EM') IS NULL ALTER TABLE dbo.EMPRESA ADD SINCRONIZADO_EM DATETIME2 NULL;
            """;
        await ExecuteAsync(sql, null, cancellationToken);
    }
}
