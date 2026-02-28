using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class SingleInstance : IDisposable
{
    private readonly string _lockFilePath;
    private FileStream? _lockFileStream;
    private bool _isDisposed;

    public SingleInstance(string lockFileName = "bot.lock")
    {
        _lockFilePath = Path.Combine(Path.GetTempPath(), lockFileName);
    }

    public bool TryAcquireLock()
    {
        try
        {
            _lockFileStream = new FileStream(
                _lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                4096,
                FileOptions.DeleteOnClose
            );

            // Write current process ID to lock file
            var pidBytes = System.Text.Encoding.UTF8.GetBytes(Environment.ProcessId.ToString());
            _lockFileStream.Write(pidBytes, 0, pidBytes.Length);
            _lockFileStream.Flush();

            return true;
        }
        catch (IOException)
        {
            // File is already locked by another process
            return false;
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _lockFileStream?.Dispose();
            try
            {
                if (File.Exists(_lockFilePath))
                {
                    File.Delete(_lockFilePath);
                }
            }
            catch { }
            _isDisposed = true;
        }
        GC.SuppressFinalize(this);
    }
}