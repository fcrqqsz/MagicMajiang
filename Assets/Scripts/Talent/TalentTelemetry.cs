using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MahjongGame.Core;
using UnityEngine;

namespace MahjongGame.Talents
{
    public interface ITalentTelemetrySink
    {
        void Record(TalentTelemetryRecord record);
    }

    [Serializable]
    public sealed class TalentTelemetryRecord
    {
        public string anonymousSessionId;
        public string preset;
        public string mode;
        public int completedRound;
        public string eventType;
        public int seatIndex = -1;
        public string talentId;
        public int publicValue;
        public int[] drawsPerSeat = Array.Empty<int>();
        public int baseFan;
        public int eligibilityFan;
        public int postLegalBonusFan;
        public int negativeFan;
        public int finalFan;
        public int winnerSeatIndex = -1;
        public bool controlApplied;
        public bool controlBlocked;
        public bool sideboardAccepted;
        public bool sideboardOriginal;
        public bool sideboardTimeout;

        public TalentTelemetryRecord Copy()
        {
            return new TalentTelemetryRecord
            {
                anonymousSessionId = anonymousSessionId,
                preset = preset,
                mode = mode,
                completedRound = completedRound,
                eventType = eventType,
                seatIndex = seatIndex,
                talentId = talentId,
                publicValue = publicValue,
                drawsPerSeat = (drawsPerSeat ?? Array.Empty<int>()).ToArray(),
                baseFan = baseFan,
                eligibilityFan = eligibilityFan,
                postLegalBonusFan = postLegalBonusFan,
                negativeFan = negativeFan,
                finalFan = finalFan,
                winnerSeatIndex = winnerSeatIndex,
                controlApplied = controlApplied,
                controlBlocked = controlBlocked,
                sideboardAccepted = sideboardAccepted,
                sideboardOriginal = sideboardOriginal,
                sideboardTimeout = sideboardTimeout
            };
        }
    }

    public static class TalentTelemetry
    {
        public static string FormatPreset(AlienationPreset preset) =>
            preset.ToString().ToLowerInvariant();

        public static string FormatMode(GameMode mode)
        {
            return mode switch
            {
                GameMode.EastOnly => "east_only",
                GameMode.HalfGame => "half_game",
                GameMode.FullGame => "full_game",
                _ => "single"
            };
        }

        public static string Serialize(TalentTelemetryRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            return JsonUtility.ToJson(record);
        }

        public static void RecordSafely(
            ITalentTelemetrySink sink,
            TalentTelemetryRecord record)
        {
            try
            {
                (sink ?? NullTalentTelemetrySink.Instance).Record(record);
            }
            catch (Exception error)
            {
                Debug.LogError($"[TalentTelemetry] Sink failure ignored: {error}");
            }
        }

        public static ITalentTelemetrySink CreateJsonLineSinkSafely(string path)
        {
            try
            {
                return new JsonLineTalentTelemetrySink(path);
            }
            catch (Exception error)
            {
                Debug.LogError($"[TalentTelemetry] JSONL sink unavailable; telemetry disabled: {error}");
                return NullTalentTelemetrySink.Instance;
            }
        }
    }

    public sealed class NullTalentTelemetrySink : ITalentTelemetrySink
    {
        public static NullTalentTelemetrySink Instance { get; } = new NullTalentTelemetrySink();

        private NullTalentTelemetrySink()
        {
        }

        public void Record(TalentTelemetryRecord record)
        {
        }
    }

    public sealed class MemoryTalentTelemetrySink : ITalentTelemetrySink
    {
        private readonly object _sync = new object();
        private readonly List<TalentTelemetryRecord> _records = new List<TalentTelemetryRecord>();

        public IReadOnlyList<TalentTelemetryRecord> Records
        {
            get
            {
                lock (_sync)
                    return _records.Select(record => record.Copy()).ToArray();
            }
        }

        public void Record(TalentTelemetryRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            lock (_sync) _records.Add(record.Copy());
        }
    }

    public sealed class JsonLineTalentTelemetrySink : ITalentTelemetrySink, IDisposable
    {
        private readonly object _sync = new object();
        private StreamWriter _writer;

        public JsonLineTalentTelemetrySink(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A telemetry path is required.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            _writer = new StreamWriter(
                new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }

        public void Record(TalentTelemetryRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            lock (_sync)
            {
                if (_writer == null) throw new ObjectDisposedException(nameof(JsonLineTalentTelemetrySink));
                _writer.WriteLine(TalentTelemetry.Serialize(record));
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
