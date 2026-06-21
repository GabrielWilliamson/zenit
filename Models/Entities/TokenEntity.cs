using System;

namespace Zenit.Models.Entities;

public class TokenEntity
{
    public int Id
    {
        get; set;
    }

    public string AccessToken { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc
    {
        get; set;
    }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsExpired()
    {
        return DateTime.UtcNow >= ExpiresAtUtc.AddMinutes(-5);
    }
}
