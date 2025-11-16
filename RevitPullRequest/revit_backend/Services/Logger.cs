using System;
using System.IO;
using System.Threading;

namespace PullRequestForRevit.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

public class Logger
{
    private static Logger? _instance;
    private static readonly object _lock = new object();
    private string _logDirectory;
    private string? _currentLogFile;
    private DateTime _currentLogDate;
    private readonly object _writeLock = new object();
    private LogLevel _minimumLevel = LogLevel.Debug;
    private string? _sessionId;

    private Logger()
    {
        // Initialize with default shared logs directory
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "DUMP", "logs");
        _logDirectory = basePath;
        
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        _currentLogDate = DateTime.Today;
        _currentLogFile = GetLogFilePath(_currentLogDate);
    }

    /// <summary>
    /// Initialize logger with a session ID to use session-specific log folders
    /// </summary>
    public void InitializeSession(string sessionId)
    {
        lock (_writeLock)
        {
            if (_sessionId == sessionId)
            {
                // Already initialized with this session
                return;
            }

            _sessionId = sessionId;
            
            // Update log directory to session-specific folder
            var basePath = Path.Combine(Directory.GetCurrentDirectory(), "DUMP", $"session_{sessionId}");
            _logDirectory = Path.Combine(basePath, "logs");
            
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            // Reset current log file to use new directory (must be done before logging)
            _currentLogDate = DateTime.Today;
            _currentLogFile = GetLogFilePath(_currentLogDate);
            
            // Ensure the new log file exists
            EnsureLogFile();
            
            // Now log to the new session-specific log file
            LogInfo($"Logger initialized for session: {sessionId}");
            LogInfo($"Session log directory: {_logDirectory}");
        }
    }

    public static Logger Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new Logger();
                    }
                }
            }
            return _instance;
        }
    }

    public void SetMinimumLevel(LogLevel level)
    {
        _minimumLevel = level;
    }

    private string GetLogFilePath(DateTime date)
    {
        return Path.Combine(_logDirectory, $"log_{date:yyyyMMdd}.txt");
    }

    private void EnsureLogFile()
    {
        var today = DateTime.Today;
        if (today != _currentLogDate)
        {
            _currentLogDate = today;
            _currentLogFile = GetLogFilePath(_currentLogDate);
        }

        if (!File.Exists(_currentLogFile) && !string.IsNullOrEmpty(_currentLogFile))
        {
            File.Create(_currentLogFile).Close();
        }
    }

    private void WriteLog(LogLevel level, string message, Exception? exception = null)
    {
        if (level < _minimumLevel)
            return;

        lock (_writeLock)
        {
            try
            {
                EnsureLogFile();

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var threadId = Thread.CurrentThread.ManagedThreadId;
                var levelStr = level.ToString().ToUpper().PadRight(7);

                var logEntry = $"[{timestamp}] [{levelStr}] [Thread:{threadId}] {message}";

                if (exception != null)
                {
                    logEntry += $"\nException: {exception.GetType().Name}: {exception.Message}";
                    logEntry += $"\nStack Trace:\n{exception.StackTrace}";

                    if (exception.InnerException != null)
                    {
                        logEntry += $"\nInner Exception: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}";
                        logEntry += $"\nInner Stack Trace:\n{exception.InnerException.StackTrace}";
                    }
                }

                logEntry += "\n";

                File.AppendAllText(_currentLogFile!, logEntry);
                
                // Flush immediately for crash recovery
                using (var stream = new FileStream(_currentLogFile!, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    stream.Flush(true);
                }
            }
            catch
            {
                // Silently fail if logging fails to prevent infinite loops
            }
        }
    }

    public void LogDebug(string message)
    {
        WriteLog(LogLevel.Debug, message);
    }

    public void LogInfo(string message)
    {
        WriteLog(LogLevel.Info, message);
    }

    public void LogWarning(string message)
    {
        WriteLog(LogLevel.Warning, message);
    }

    public void LogError(string message, Exception? exception = null)
    {
        WriteLog(LogLevel.Error, message, exception);
    }

    public void LogFatal(string message, Exception? exception = null)
    {
        WriteLog(LogLevel.Fatal, message, exception);
    }

    public void LogException(Exception exception, string? context = null)
    {
        var message = context ?? "Exception occurred";
        WriteLog(LogLevel.Error, message, exception);
    }
}

