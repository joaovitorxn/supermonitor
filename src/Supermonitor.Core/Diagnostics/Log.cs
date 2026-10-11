namespace Supermonitor.Core.Diagnostics;

public static class Log
{
    private static readonly Lock Gate = new();
    private static string? _directory;

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        _directory = directory;
        var names = Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>();
        foreach (var name in LogRetention.FilesToDelete(names, DateOnly.FromDateTime(DateTime.Now), LogRetention.KeepDays).ToArray())
        {
            try
            {
                File.Delete(Path.Combine(directory, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Arquivo antigo bloqueado ou somente leitura: fica para a próxima limpeza.
            }
        }
    }

    public static void Info(string message) => Write("INF", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WRN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var directory = _directory;
        if (directory is null)
        {
            return;
        }

        var now = DateTime.Now;
        var line = exception is null
            ? $"{now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}"
            : $"{now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}{exception}{Environment.NewLine}";

        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path.Combine(directory, $"{LogRetention.FilePrefix}{now:yyyyMMdd}.log"), line);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
