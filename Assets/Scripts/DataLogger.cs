using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;

/// <summary>
/// Crash-safe CSV data logger for Unity simulations.
/// Automatically creates trial subdirectories and handles multiple log files.
/// </summary>
public class DataLogger : MonoBehaviour
{
    private static DataLogger _instance;
    public static DataLogger Instance => _instance;

    [Header("Configuration")]
    [Tooltip("Maximum time between disk writes when not using AutoFlush (seconds)")]
    public float FlushInterval = 0.5f;

    [Tooltip("Enable for maximum crash safety (slight performance cost)")]
    public bool AutoFlushEnabled = true;

    private string _trialDirectory;
    private DateTime _simulationStartTime;
    private readonly Dictionary<string, StreamWriter> _writers = new Dictionary<string, StreamWriter>();
    private readonly Dictionary<string, bool> _headersWritten = new Dictionary<string, bool>();
    private readonly object _lock = new object();
    private bool _isInitialized = false;
    private bool _isShuttingDown = false;
    private Thread _flushThread;

    void Awake()
    {
        // Singleton pattern
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Initialize()
    {
        try
        {
            // Use persistent data path for cross-platform safety
            string basePath = Path.Combine(Application.persistentDataPath, "Trials");
            Directory.CreateDirectory(basePath);

            // Find next available trial number
            int trialNumber = GetNextTrialNumber(basePath);
            _trialDirectory = Path.Combine(basePath, $"Trial {trialNumber:D3}");
            Directory.CreateDirectory(_trialDirectory);

            _simulationStartTime = DateTime.UtcNow;
            _isInitialized = true;

            Debug.Log($"[DataLogger] Initialized. Output directory: {_trialDirectory}");

            // Register shutdown handler
            Application.quitting += OnApplicationQuitting;

            // Start background flush thread if needed
            if (!AutoFlushEnabled)
            {
                _flushThread = new Thread(FlushWorker) { IsBackground = true };
                _flushThread.Start();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataLogger] Initialization failed: {e.Message}");
            enabled = false;
        }
    }

    private int GetNextTrialNumber(string basePath)
    {
        try
        {
            var dirs = Directory.GetDirectories(basePath, "Trial *");
            int maxNum = 0;
            foreach (var dir in dirs)
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith("Trial ") && int.TryParse(name.Substring(6), out int num))
                {
                    maxNum = Mathf.Max(maxNum, num);
                }
            }
            return maxNum + 1;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>
    /// Log data to a CSV file. Automatically adds timestamp columns.
    /// </summary>
    /// <param name="fileName">Target CSV file name (e.g., "buttons.csv")</param>
    /// <param name="values">Data values to log</param>
    public void Log(string fileName, params string[] values)
    {
        if (!_isInitialized || _isShuttingDown) return;

        try
        {
            lock (_lock)
            {
                // Get or create file writer
                if (!_writers.TryGetValue(fileName, out StreamWriter writer))
                {
                    string filePath = Path.Combine(_trialDirectory, SanitizeFileName(fileName));
                    string directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    // Open file with shared read access so external programs can read it
                    var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    writer = new StreamWriter(fileStream);
                    writer.AutoFlush = AutoFlushEnabled;
                    _writers[fileName] = writer;
                    _headersWritten[fileName] = false;
                }

                // Write header on first write
                if (!_headersWritten[fileName])
                {
                    WriteHeader(writer, values.Length);
                    _headersWritten[fileName] = true;
                }

                // Write data row
                WriteDataRow(writer, values);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataLogger] Failed to log to '{fileName}': {e.Message}");
        }
    }

    private void WriteHeader(StreamWriter writer, int dataColumnCount)
    {
        var headers = new List<string> { "SystemTime", "SimulationTime" };
        for (int i = 1; i <= Mathf.Max(1, dataColumnCount); i++)
        {
            headers.Add($"Column{i}");
        }
        writer.WriteLine(string.Join(",", headers));
    }

    private void WriteDataRow(StreamWriter writer, string[] values)
    {
        double simTime = (DateTime.UtcNow - _simulationStartTime).TotalSeconds;
        string systemTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        var line = new List<string>
        {
            EscapeCsvValue(systemTime),
            EscapeCsvValue(simTime.ToString("F3", CultureInfo.InvariantCulture))
        };

        line.AddRange(values.Select(v => EscapeCsvValue(v ?? "")));
        writer.WriteLine(string.Join(",", line));
    }

    private string EscapeCsvValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        // Escape if contains special characters
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "data.csv";

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }

        if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            fileName += ".csv";

        return fileName;
    }

    private void FlushWorker()
    {
        while (!_isShuttingDown)
        {
            Thread.Sleep(Mathf.RoundToInt(FlushInterval * 1000));

            lock (_lock)
            {
                foreach (var writer in _writers.Values)
                {
                    writer.Flush();
                }
            }
        }
    }

    private void OnApplicationQuitting()
    {
        Shutdown();
    }

    void OnDestroy()
    {
        Shutdown();
    }

    private void Shutdown()
    {
        _isShuttingDown = true;

        lock (_lock)
        {
            foreach (var writer in _writers.Values)
            {
                try
                {
                    writer.Flush();
                    writer.Close();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DataLogger] Error closing writer: {e.Message}");
                }
            }
            _writers.Clear();
            _headersWritten.Clear();
        }

        if (_flushThread?.IsAlive == true)
        {
            _flushThread.Join(1000); // Wait max 1 second
        }
    }
}
