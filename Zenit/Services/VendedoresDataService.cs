using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zenit.Core.Data;
using Zenit.Helpers;
using Zenit.Models.Vendedores;
using Microsoft.EntityFrameworkCore;

namespace Zenit.Services;

/// <summary>
/// Acceso a tabla Vendedores usando la conexion ya configurada en AppDbContext.
/// </summary>
public sealed class VendedoresDataService
{
    private readonly AppDbContext _dbContext;
    private bool? _hasTelefonoColumn;

    public VendedoresDataService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IReadOnlyList<Vendedor>> GetVendedoresAsync(
        string? nombre,
        string? ruta,
        string? grupo,
        string? subgrupo,
        CancellationToken cancellationToken = default)
    {
        return UseConnectionAsync(async connection =>
        {
            var hasTelefonoColumn = await EnsureTelefonoColumnFlagAsync(connection, cancellationToken);
            using var command = connection.CreateCommand();
            var sql = new StringBuilder(
                @"SELECT COD_VEND, COD_GRUPO, GRUPO, COD_RUTA, NOMVEN, SUBGRUPO, SUB_GRUPO2");

            if (hasTelefonoColumn)
                sql.Append(", TELEFONO");
            else
                sql.Append(", '' AS TELEFONO");

            sql.Append(
                @"
                  FROM Vendedores
                  WHERE 1 = 1");

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                sql.Append(" AND UPPER(NOMVEN) LIKE UPPER(@nombre)");
                AddParameter(command, "@nombre", $"%{nombre.Trim()}%");
            }

            if (!string.IsNullOrWhiteSpace(ruta))
            {
                sql.Append(" AND UPPER(COD_RUTA) LIKE UPPER(@ruta)");
                AddParameter(command, "@ruta", $"%{ruta.Trim()}%");
            }

            if (!string.IsNullOrWhiteSpace(grupo))
            {
                sql.Append(" AND GRUPO = @grupo");
                AddParameter(command, "@grupo", grupo.Trim());
            }

            if (!string.IsNullOrWhiteSpace(subgrupo))
            {
                sql.Append(" AND SUBGRUPO = @subgrupo");
                AddParameter(command, "@subgrupo", subgrupo.Trim());
            }

            sql.Append(" ORDER BY GRUPO, SUBGRUPO, NOMVEN, COD_VEND");
            command.CommandText = sql.ToString();

            var result = new List<Vendedor>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new Vendedor
                {
                    COD_VEND = ReadString(reader, "COD_VEND"),
                    COD_GRUPO = ReadString(reader, "COD_GRUPO"),
                    GRUPO = ReadString(reader, "GRUPO"),
                    COD_RUTA = ReadString(reader, "COD_RUTA"),
                    NOMVEN = ReadString(reader, "NOMVEN"),
                    SUBGRUPO = ReadString(reader, "SUBGRUPO"),
                    SUB_GRUPO2 = ReadString(reader, "SUB_GRUPO2"),
                    TELEFONO = VendedorTelefonoHelper.FormatForDisplay(ReadString(reader, "TELEFONO"))
                });
            }

            return (IReadOnlyList<Vendedor>)result;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetGruposAsync(CancellationToken cancellationToken = default)
    {
        return GetDistinctValuesAsync("GRUPO", null, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetSubgruposAsync(string? grupo, CancellationToken cancellationToken = default)
    {
        return GetDistinctValuesAsync("SUBGRUPO", grupo, cancellationToken);
    }

    public Task<bool> HasTelefonoColumnAsync(CancellationToken cancellationToken = default)
    {
        return UseConnectionAsync(async connection => await EnsureTelefonoColumnFlagAsync(connection, cancellationToken), cancellationToken);
    }

    public Task CreateAsync(Vendedor vendedor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vendedor);
        ValidateRequired(vendedor);

        return UseConnectionAsync(async connection =>
        {
            var hasTelefonoColumn = await EnsureTelefonoColumnFlagAsync(connection, cancellationToken);
            if (!hasTelefonoColumn && !string.IsNullOrWhiteSpace(vendedor.TELEFONO))
                throw BuildMissingTelefonoColumnException();

            using var command = connection.CreateCommand();
            if (hasTelefonoColumn)
            {
                command.CommandText =
                    @"INSERT INTO Vendedores (COD_VEND, COD_GRUPO, GRUPO, COD_RUTA, NOMVEN, SUBGRUPO, SUB_GRUPO2, TELEFONO)
                      VALUES (@codVend, @codGrupo, @grupo, @codRuta, @nomven, @subgrupo, @subGrupo2, @telefono)";
            }
            else
            {
                command.CommandText =
                    @"INSERT INTO Vendedores (COD_VEND, COD_GRUPO, GRUPO, COD_RUTA, NOMVEN, SUBGRUPO, SUB_GRUPO2)
                      VALUES (@codVend, @codGrupo, @grupo, @codRuta, @nomven, @subgrupo, @subGrupo2)";
            }

            FillWriteParameters(command, vendedor, hasTelefonoColumn);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public Task UpdateAsync(string originalCodVend, Vendedor vendedor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vendedor);
        if (string.IsNullOrWhiteSpace(originalCodVend))
            throw new ArgumentException("originalCodVend es requerido.", nameof(originalCodVend));

        ValidateRequired(vendedor);

        return UseConnectionAsync(async connection =>
        {
            var hasTelefonoColumn = await EnsureTelefonoColumnFlagAsync(connection, cancellationToken);
            if (!hasTelefonoColumn && !string.IsNullOrWhiteSpace(vendedor.TELEFONO))
                throw BuildMissingTelefonoColumnException();

            using var command = connection.CreateCommand();
            var sql = new StringBuilder(
                @"UPDATE Vendedores
                  SET COD_VEND = @codVend,
                      COD_GRUPO = @codGrupo,
                      GRUPO = @grupo,
                      COD_RUTA = @codRuta,
                      NOMVEN = @nomven,
                      SUBGRUPO = @subgrupo,
                      SUB_GRUPO2 = @subGrupo2");

            if (hasTelefonoColumn)
                sql.Append(", TELEFONO = @telefono");

            sql.Append(" WHERE COD_VEND = @originalCodVend");
            command.CommandText = sql.ToString();

            FillWriteParameters(command, vendedor, hasTelefonoColumn);
            AddParameter(command, "@originalCodVend", originalCodVend.Trim());

            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
                throw new InvalidOperationException($"No existe vendedor con COD_VEND '{originalCodVend}'.");
        }, cancellationToken);
    }

    public Task DeleteAsync(string codVend, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codVend))
            return Task.CompletedTask;

        return UseConnectionAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Vendedores WHERE COD_VEND = @codVend";
            AddParameter(command, "@codVend", codVend.Trim());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    private Task<IReadOnlyList<string>> GetDistinctValuesAsync(
        string columnName,
        string? grupo,
        CancellationToken cancellationToken)
    {
        return UseConnectionAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            var sql = new StringBuilder(
                $@"SELECT DISTINCT {columnName}
                   FROM Vendedores
                   WHERE {columnName} IS NOT NULL AND TRIM({columnName}) <> ''");

            if (!string.IsNullOrWhiteSpace(grupo) && !string.Equals(grupo, "Todos", StringComparison.OrdinalIgnoreCase))
            {
                sql.Append(" AND GRUPO = @grupo");
                AddParameter(command, "@grupo", grupo.Trim());
            }

            sql.Append($" ORDER BY {columnName}");
            command.CommandText = sql.ToString();

            var list = new List<string>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    var value = reader.GetValue(0)?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        list.Add(value);
                }
            }

            return (IReadOnlyList<string>)list;
        }, cancellationToken);
    }

    private static void FillWriteParameters(DbCommand command, Vendedor vendedor, bool includeTelefono)
    {
        AddParameter(command, "@codVend", vendedor.COD_VEND.Trim());
        AddParameter(command, "@codGrupo", vendedor.COD_GRUPO.Trim());
        AddParameter(command, "@grupo", NullIfEmpty(vendedor.GRUPO));
        AddParameter(command, "@codRuta", NullIfEmpty(vendedor.COD_RUTA));
        AddParameter(command, "@nomven", vendedor.NOMVEN.Trim());
        AddParameter(command, "@subgrupo", NullIfEmpty(vendedor.SUBGRUPO));
        AddParameter(command, "@subGrupo2", NullIfEmpty(vendedor.SUB_GRUPO2));

        if (includeTelefono)
        {
            AddParameter(command, "@telefono", NullIfEmpty(VendedorTelefonoHelper.NormalizeForStorage(vendedor.TELEFONO)));
        }
    }

    private static void ValidateRequired(Vendedor vendedor)
    {
        if (string.IsNullOrWhiteSpace(vendedor.COD_VEND))
            throw new InvalidOperationException("COD_VEND es obligatorio.");
        if (string.IsNullOrWhiteSpace(vendedor.COD_GRUPO))
            throw new InvalidOperationException("COD_GRUPO es obligatorio.");
        if (string.IsNullOrWhiteSpace(vendedor.NOMVEN))
            throw new InvalidOperationException("NOMVEN es obligatorio.");
    }

    private static object NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string ReadString(DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
            return string.Empty;

        return reader.GetValue(ordinal)?.ToString()?.Trim() ?? string.Empty;
    }

    private async Task<bool> EnsureTelefonoColumnFlagAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (_hasTelefonoColumn.HasValue)
            return _hasTelefonoColumn.Value;

        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        // PostgreSQL (Npgsql): validamos columna via information_schema.
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT 1
                  FROM information_schema.columns
                  WHERE lower(table_name) = lower(@tableName)
                    AND lower(column_name) = lower(@columnName)
                    AND table_schema NOT IN ('pg_catalog', 'information_schema')
                  LIMIT 1";
            AddParameter(command, "@tableName", "Vendedores");
            AddParameter(command, "@columnName", "TELEFONO");

            var exists = await command.ExecuteScalarAsync(cancellationToken);
            _hasTelefonoColumn = exists != null && exists != DBNull.Value;
            return _hasTelefonoColumn.Value;
        }

        // Fallback para otros proveedores ANSI.
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                @"SELECT 1
                  FROM information_schema.columns
                  WHERE lower(table_name) = lower(@tableName)
                    AND lower(column_name) = lower(@columnName)";
            AddParameter(command, "@tableName", "Vendedores");
            AddParameter(command, "@columnName", "TELEFONO");

            try
            {
                var exists = await command.ExecuteScalarAsync(cancellationToken);
                _hasTelefonoColumn = exists != null && exists != DBNull.Value;
                return _hasTelefonoColumn.Value;
            }
            catch
            {
                _hasTelefonoColumn = false;
                return false;
            }
        }

    }

    private static InvalidOperationException BuildMissingTelefonoColumnException()
    {
        return new InvalidOperationException(
            "La tabla Vendedores no tiene la columna TELEFONO. Ejecuta: ALTER TABLE Vendedores ADD COLUMN TELEFONO TEXT;");
    }

    private async Task<T> UseConnectionAsync<T>(
        Func<DbConnection, Task<T>> callback,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;

        if (mustClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            return await callback(connection);
        }
        finally
        {
            if (mustClose)
                await connection.CloseAsync();
        }
    }

    private async Task UseConnectionAsync(
        Func<DbConnection, Task> callback,
        CancellationToken cancellationToken)
    {
        await UseConnectionAsync(async connection =>
        {
            await callback(connection);
            return true;
        }, cancellationToken);
    }
}
