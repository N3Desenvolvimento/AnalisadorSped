IF OBJECT_ID('dbo.SPED_E111', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SPED_E111 (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SPED_E111 PRIMARY KEY,
        SpedE110Id INT NOT NULL,
        CodigoAjusteApuracao VARCHAR(20) NOT NULL,
        DescricaoComplementar VARCHAR(255) NOT NULL,
        ValorAjuste DECIMAL(18, 2) NOT NULL
    );
END;

IF OBJECT_ID('dbo.SPED_E110', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SPED_E110 (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SPED_E110 PRIMARY KEY,
        SpedArquivoId INT NOT NULL,
        ValorTotalDebitos DECIMAL(18, 2) NOT NULL,
        ValorAjustesDebitos DECIMAL(18, 2) NOT NULL,
        ValorTotalAjustesDebitos DECIMAL(18, 2) NOT NULL,
        ValorEstornosCreditos DECIMAL(18, 2) NOT NULL,
        ValorTotalCreditos DECIMAL(18, 2) NOT NULL,
        ValorAjustesCreditos DECIMAL(18, 2) NOT NULL,
        ValorTotalAjustesCreditos DECIMAL(18, 2) NOT NULL,
        ValorEstornosDebitos DECIMAL(18, 2) NOT NULL,
        ValorSaldoCredorAnterior DECIMAL(18, 2) NOT NULL,
        ValorSaldoDevedor DECIMAL(18, 2) NOT NULL,
        ValorDeducoes DECIMAL(18, 2) NOT NULL,
        ValorIcmsRecolher DECIMAL(18, 2) NOT NULL,
        ValorSaldoCredorTransportar DECIMAL(18, 2) NOT NULL,
        ValorExtraApuracao DECIMAL(18, 2) NOT NULL
    );
END;

IF OBJECT_ID('dbo.SPED_C190', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SPED_C190 (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SPED_C190 PRIMARY KEY,
        SpedC100Id INT NOT NULL,
        CstIcms VARCHAR(3) NOT NULL,
        Cfop VARCHAR(4) NOT NULL,
        AliquotaIcms DECIMAL(18, 2) NOT NULL,
        ValorOperacao DECIMAL(18, 2) NOT NULL,
        ValorBcIcms DECIMAL(18, 2) NOT NULL,
        ValorIcms DECIMAL(18, 2) NOT NULL,
        ValorBcIcmsSt DECIMAL(18, 2) NOT NULL,
        ValorIcmsSt DECIMAL(18, 2) NOT NULL,
        ValorReducaoBcIcms DECIMAL(18, 2) NOT NULL,
        ValorIpi DECIMAL(18, 2) NOT NULL,
        CodigoObservacao VARCHAR(20) NULL
    );
END;

IF OBJECT_ID('dbo.SPED_C170', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SPED_C170 (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SPED_C170 PRIMARY KEY,
        SpedC100Id INT NOT NULL,
        ProdutoId INT NULL,
        NumeroItem VARCHAR(10) NOT NULL,
        CodigoItem VARCHAR(80) NOT NULL,
        DescricaoComplementar VARCHAR(255) NULL,
        Quantidade DECIMAL(18, 6) NOT NULL,
        Unidade VARCHAR(20) NOT NULL,
        ValorItem DECIMAL(18, 2) NOT NULL,
        ValorDesconto DECIMAL(18, 2) NOT NULL,
        CstIcms VARCHAR(3) NOT NULL,
        Cfop VARCHAR(4) NOT NULL,
        NaturezaBcIcms VARCHAR(5) NOT NULL,
        ValorBcIcms DECIMAL(18, 2) NOT NULL,
        AliquotaIcms DECIMAL(18, 2) NOT NULL,
        ValorIcms DECIMAL(18, 2) NOT NULL,
        ValorBcIcmsSt DECIMAL(18, 2) NOT NULL,
        AliquotaIcmsSt DECIMAL(18, 2) NOT NULL,
        ValorIcmsSt DECIMAL(18, 2) NOT NULL,
        CstIpi VARCHAR(3) NULL,
        ValorBcIpi DECIMAL(18, 2) NOT NULL,
        AliquotaIpi DECIMAL(18, 2) NOT NULL,
        ValorIpi DECIMAL(18, 2) NOT NULL
    );
END;

IF OBJECT_ID('dbo.SPED_C100', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SPED_C100 (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SPED_C100 PRIMARY KEY,
        SpedArquivoId INT NOT NULL,
        ParticipanteId INT NULL,
        IndicadorOperacao VARCHAR(1) NOT NULL,
        IndicadorEmitente VARCHAR(1) NOT NULL,
        CodigoParticipante VARCHAR(80) NOT NULL,
        CodigoModelo VARCHAR(2) NOT NULL,
        CodigoSituacao VARCHAR(2) NOT NULL,
        Serie VARCHAR(20) NOT NULL,
        NumeroDocumento VARCHAR(20) NOT NULL,
        ChaveNfe VARCHAR(44) NULL,
        DataDocumento DATE NULL,
        DataEntradaSaida DATE NULL,
        ValorDocumento DECIMAL(18, 2) NOT NULL,
        ValorDesconto DECIMAL(18, 2) NOT NULL,
        ValorMercadoria DECIMAL(18, 2) NOT NULL,
        ValorFrete DECIMAL(18, 2) NOT NULL,
        ValorSeguro DECIMAL(18, 2) NOT NULL,
        ValorOutrasDespesas DECIMAL(18, 2) NOT NULL,
        ValorIcms DECIMAL(18, 2) NOT NULL,
        ValorIcmsSt DECIMAL(18, 2) NOT NULL,
        ValorIpi DECIMAL(18, 2) NOT NULL,
        ValorPis DECIMAL(18, 2) NOT NULL,
        ValorCofins DECIMAL(18, 2) NOT NULL
    );
END;

IF OBJECT_ID('dbo.PRODUTO', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PRODUTO (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PRODUTO PRIMARY KEY,
        SpedArquivoId INT NOT NULL,
        UnidadeMedidaId INT NULL,
        Codigo VARCHAR(80) NOT NULL,
        Descricao VARCHAR(255) NOT NULL,
        CodigoBarra VARCHAR(80) NULL,
        CodigoAnterior VARCHAR(80) NULL,
        Unidade VARCHAR(20) NULL,
        TipoItem VARCHAR(2) NULL,
        CodigoNcm VARCHAR(8) NULL,
        ExIpi VARCHAR(3) NULL,
        CodigoGenero VARCHAR(2) NULL,
        CodigoLst VARCHAR(5) NULL,
        AliquotaIcms DECIMAL(18, 2) NULL
    );
END;

IF OBJECT_ID('dbo.UNIDADE_MEDIDA', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UNIDADE_MEDIDA (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UNIDADE_MEDIDA PRIMARY KEY,
        SpedArquivoId INT NOT NULL,
        Codigo VARCHAR(20) NOT NULL,
        Descricao VARCHAR(255) NOT NULL
    );
END;

IF OBJECT_ID('dbo.PARTICIPANTE', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PARTICIPANTE (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PARTICIPANTE PRIMARY KEY,
        SpedArquivoId INT NOT NULL,
        Codigo VARCHAR(80) NOT NULL,
        Nome VARCHAR(255) NOT NULL,
        Cnpj VARCHAR(14) NULL,
        Cpf VARCHAR(11) NULL,
        InscricaoEstadual VARCHAR(20) NULL,
        CodigoPais VARCHAR(5) NULL,
        CodigoMunicipio VARCHAR(7) NULL,
        Suframa VARCHAR(20) NULL,
        Endereco VARCHAR(255) NULL,
        Numero VARCHAR(20) NULL,
        Complemento VARCHAR(100) NULL,
        Bairro VARCHAR(100) NULL
    );
END;

IF OBJECT_ID('dbo.SPED_ARQUIVO', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SPED_ARQUIVO (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SPED_ARQUIVO PRIMARY KEY,
        EmpresaId INT NOT NULL,
        NomeArquivo VARCHAR(255) NOT NULL,
        HashArquivo VARCHAR(64) NULL,
        VersaoLeiaute VARCHAR(20) NULL,
        FinalidadeArquivo VARCHAR(2) NULL,
        PeriodoInicial DATE NOT NULL,
        PeriodoFinal DATE NOT NULL,
        ImportadoEm DATETIME2 NOT NULL
    );
END;

IF OBJECT_ID('dbo.EMPRESA', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EMPRESA (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EMPRESA PRIMARY KEY,
        Cnpj VARCHAR(14) NOT NULL,
        RazaoSocial VARCHAR(255) NOT NULL,
        NomeFantasia VARCHAR(255) NULL,
        InscricaoEstadual VARCHAR(20) NULL,
        Uf VARCHAR(2) NULL,
        CodigoMunicipio VARCHAR(7) NULL,
        CriadoEm DATETIME2 NOT NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_EMPRESA_Cnpj' AND object_id = OBJECT_ID('dbo.EMPRESA'))
    CREATE UNIQUE INDEX UX_EMPRESA_Cnpj ON dbo.EMPRESA (Cnpj);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SPED_ARQUIVO_HashArquivo' AND object_id = OBJECT_ID('dbo.SPED_ARQUIVO'))
    CREATE UNIQUE INDEX UX_SPED_ARQUIVO_HashArquivo ON dbo.SPED_ARQUIVO (HashArquivo) WHERE HashArquivo IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SPED_ARQUIVO_EmpresaPeriodo' AND object_id = OBJECT_ID('dbo.SPED_ARQUIVO'))
    CREATE UNIQUE INDEX UX_SPED_ARQUIVO_EmpresaPeriodo ON dbo.SPED_ARQUIVO (EmpresaId, PeriodoInicial, PeriodoFinal);
