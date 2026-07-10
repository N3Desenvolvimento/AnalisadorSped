using System.Data.Common;

namespace N3.AnalisadorFiscal.Data;

public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
}
