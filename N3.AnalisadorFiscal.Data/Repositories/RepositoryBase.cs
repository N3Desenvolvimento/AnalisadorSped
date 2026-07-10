using Dapper;

namespace N3.AnalisadorFiscal.Data.Repositories;

public abstract class RepositoryBase
{
    protected const int CommandTimeoutSeconds = 30;
    private readonly IDbConnectionFactory _connectionFactory;

    protected RepositoryBase(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    protected async Task<int> InsertAsync(string sql, object param, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, param, commandTimeout: CommandTimeoutSeconds, cancellationToken: cancellationToken));
    }

    protected async Task<int> ExecuteAsync(string sql, object? param, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.ExecuteAsync(
            new CommandDefinition(sql, param, commandTimeout: CommandTimeoutSeconds, cancellationToken: cancellationToken));
    }

    protected async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object param, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(sql, param, commandTimeout: CommandTimeoutSeconds, cancellationToken: cancellationToken));
    }

    protected async Task<bool> ExistsAsync(string sql, object param, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, param, commandTimeout: CommandTimeoutSeconds, cancellationToken: cancellationToken));
    }
}
