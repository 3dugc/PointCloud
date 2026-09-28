using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Bujiaban.PointCloud.Immersal
{
    // Local, bounded, best-effort diagnostics. IO errors must never fail localization.
    internal sealed class PointCloudDiagnosticJournal : IDisposable
    {
        internal const int DefaultMaximumFileBytes = 4 * 1024 * 1024;
        internal const int DefaultRetainedFiles = 8;
        private readonly object _sync = new object();
        private readonly string _directory, _runId, _headerJson;
        private readonly Action<string> _warning;
        private readonly int _maximumFileBytes, _retainedFiles;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private StreamWriter _writer;
        private int _part, _bytes;
        private long _sequence;
        private bool _disabled;
        internal string CurrentPath { get; private set; }

        internal PointCloudDiagnosticJournal(string directory, string runId, string headerJson,
            Action<string> warning, int maximumFileBytes = DefaultMaximumFileBytes,
            int retainedFiles = DefaultRetainedFiles)
        {
            _directory = directory;
            _runId = runId;
            _headerJson = headerJson;
            _warning = warning;
            _maximumFileBytes = Math.Max(1024, maximumFileBytes);
            _retainedFiles = Math.Max(1, retainedFiles);
            try { OpenPart(); }
            catch (Exception exception) { Disable(exception); }
        }

        internal void Write(string eventName, string payloadJson)
        {
            lock (_sync)
            {
                if (_disabled) return;
                try
                {
                    int payloadBytes = Encoding.UTF8.GetByteCount(payloadJson) + 256;
                    if (_bytes + payloadBytes > _maximumFileBytes)
                    {
                        _writer.Dispose();
                        OpenPart();
                    }
                    WriteLine(Envelope(eventName, payloadJson));
                }
                catch (Exception exception) { Disable(exception); }
            }
        }

        private void OpenPart()
        {
            Directory.CreateDirectory(_directory);
            CurrentPath = Path.Combine(_directory, $"pointcloud-{_runId}-{_part++:D3}.jsonl");
            _writer = new StreamWriter(new FileStream(CurrentPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
            _bytes = 0;
            WriteLine(Envelope("run_header", _headerJson));
            // Only our dedicated journal files are eligible for bounded retention.
            FileInfo[] files = new DirectoryInfo(_directory).GetFiles("pointcloud-*.jsonl")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal).ToArray();
            foreach (FileInfo file in files.Skip(_retainedFiles))
            {
                if (file.FullName == CurrentPath) continue;
                try { file.Delete(); }
                catch (IOException) { /* An active reader/writer may still own an old part. */ }
                catch (UnauthorizedAccessException) { }
            }
        }

        private string Envelope(string eventName, string payloadJson) =>
            "{\"schema\":1,\"runId\":" + Quote(_runId) +
            ",\"sequence\":" + (++_sequence).ToString(CultureInfo.InvariantCulture) +
            ",\"utc\":" + Quote(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)) +
            ",\"elapsedSeconds\":" + _clock.Elapsed.TotalSeconds.ToString("R", CultureInfo.InvariantCulture) +
            ",\"event\":" + Quote(eventName) + ",\"data\":" + payloadJson + "}";

        private void WriteLine(string line)
        {
            _writer.WriteLine(line);
            _bytes += Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
        }

        private void Disable(Exception exception)
        {
            _disabled = true;
            try { _writer?.Dispose(); } catch { }
            _writer = null;
            try { _warning?.Invoke($"Diagnostic file logging disabled: {exception.GetType().Name}: {exception.Message}"); }
            catch { /* Diagnostics must remain observational. */ }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disabled) return;
                try { _writer?.Dispose(); }
                catch (Exception exception) { Disable(exception); }
                _disabled = true;
            }
        }

        internal static string Quote(string value)
        {
            if (value == null) return "null";
            var text = new StringBuilder(value.Length + 2).Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (character < 32) text.Append("\\u" + ((int)character).ToString("x4"));
                        else text.Append(character);
                        break;
                }
            }
            return text.Append('"').ToString();
        }
    }
}
