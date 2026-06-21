using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zenit.Data;
using Zenit.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Zenit.Infrastructure.Persistence;

public class TokenRepository
{
    private readonly AppDbContext _db;

    public TokenRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Obtiene el último token vigente.
    /// Usamos AsNoTracking por rendimiento (no necesitamos tracking para leer).
    /// </summary>
    public async Task<TokenEntity?> GetLastValidAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Tokens
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync(t => t.ExpiresAtUtc > DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Guarda un token en la base de datos.
    /// </summary>
    public async Task SaveAsync(TokenEntity token, CancellationToken cancellationToken = default)
    {
        _db.Tokens.Add(token);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
