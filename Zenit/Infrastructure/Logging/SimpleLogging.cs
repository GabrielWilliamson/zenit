using System.Diagnostics;
using System.Globalization;

namespace Zenit.Infrastructure.Logging;

public interface ILogger<TCategoryName>
{
    void LogInformation(string message, params object?[] args);
    void LogWarning(string message, params object?[] args);
    void LogWarning(Exception exception, string message, params object?[] args);
    void LogError(Exception exception, string message, params object?[] args);
}

public sealed class ConsoleLogger<TCategoryName> : ILogger<TCategoryName>
{
    private readonly string _categoryName = typeof(TCategoryName).Name;

    public void LogInformation(string message, params object?[] args)
    {
        Write("info", message, null, args);
    }

    public void LogWarning(string message, params object?[] args)
    {
        Write("warn", message, null, args);
    }

    public void LogWarning(Exception exception, string message, params object?[] args)
    {
        Write("warn", message, exception, args);
    }

    public void LogError(Exception exception, string message, params object?[] args)
    {
        Write("error", message, exception, args);
    }

    private void Write(string level, string message, Exception? exception, object?[] args)
    {
        var formatted = FormatMessage(message, args);
        var line = $"[{DateTime.Now.ToString("u", CultureInfo.InvariantCulture)}] {_categoryName} {level}: {formatted}";
        if (exception != null)
            line += $"{Environment.NewLine}{exception}";

        Debug.WriteLine(line);
        Console.WriteLine(line);
    }

    private static string FormatMessage(string message, object?[] args)
    {
        if (args.Length == 0)
            return message;

        var normalized = message;
        for (var i = 0; i < args.Length; i++)
        {
            normalized = ReplaceFirstPlaceholder(normalized, args[i]);
        }

        return normalized;
    }

    private static string ReplaceFirstPlaceholder(string template, object? value)
    {
        var start = template.IndexOf('{');
        var end = template.IndexOf('}', start + 1);
        if (start < 0 || end <= start)
            return template;

        return string.Concat(
            template.AsSpan(0, start),
            Convert.ToString(value, CultureInfo.CurrentCulture),
            template.AsSpan(end + 1));
    }
}
