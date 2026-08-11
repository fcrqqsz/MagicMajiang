using System;
using System.Collections.Generic;

namespace MahjongGame.Talents
{
    public sealed class TalentRuntimeState
    {
        private readonly Dictionary<string, int> _matchCounters = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _roundCounters = new Dictionary<string, int>();
        private readonly Dictionary<string, long> _matchTokens = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _roundTokens = new Dictionary<string, long>();
        private readonly HashSet<string> _matchFlags = new HashSet<string>();
        private readonly HashSet<string> _roundFlags = new HashSet<string>();

        public bool IsActive { get; internal set; }
        public bool IsRevealed { get; internal set; }

        public int GetCounter(string key, TalentStateScope scope)
        {
            Dictionary<string, int> counters = GetCounters(scope);
            return counters.TryGetValue(key, out int value) ? value : 0;
        }

        public void SetCounter(string key, int value, TalentStateScope scope)
        {
            GetCounters(scope)[key] = value;
        }

        public int IncrementCounter(string key, TalentStateScope scope, int amount = 1)
        {
            int value = GetCounter(key, scope) + amount;
            SetCounter(key, value, scope);
            return value;
        }

        public long GetToken(string key, TalentStateScope scope)
        {
            Dictionary<string, long> tokens = GetTokens(scope);
            return tokens.TryGetValue(key, out long value) ? value : 0;
        }

        public void SetToken(string key, long value, TalentStateScope scope)
        {
            GetTokens(scope)[key] = value;
        }

        public bool GetFlag(string key, TalentStateScope scope)
        {
            return GetFlags(scope).Contains(key);
        }

        public void SetFlag(string key, bool value, TalentStateScope scope)
        {
            HashSet<string> flags = GetFlags(scope);
            if (value)
                flags.Add(key);
            else
                flags.Remove(key);
        }

        internal void ResetRoundState()
        {
            _roundCounters.Clear();
            _roundTokens.Clear();
            _roundFlags.Clear();
        }

        internal TalentRuntimeState CreateDetachedCopy()
        {
            var copy = new TalentRuntimeState
            {
                IsActive = IsActive,
                IsRevealed = IsRevealed
            };
            CopyCounters(_matchCounters, copy._matchCounters);
            CopyCounters(_roundCounters, copy._roundCounters);
            CopyTokens(_matchTokens, copy._matchTokens);
            CopyTokens(_roundTokens, copy._roundTokens);
            copy._matchFlags.UnionWith(_matchFlags);
            copy._roundFlags.UnionWith(_roundFlags);
            return copy;
        }

        private static void CopyCounters(
            Dictionary<string, int> source,
            Dictionary<string, int> destination)
        {
            foreach (KeyValuePair<string, int> pair in source)
                destination[pair.Key] = pair.Value;
        }

        private static void CopyTokens(
            Dictionary<string, long> source,
            Dictionary<string, long> destination)
        {
            foreach (KeyValuePair<string, long> pair in source)
                destination[pair.Key] = pair.Value;
        }

        private Dictionary<string, int> GetCounters(TalentStateScope scope)
        {
            return scope switch
            {
                TalentStateScope.Match => _matchCounters,
                TalentStateScope.Round => _roundCounters,
                _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
            };
        }

        private Dictionary<string, long> GetTokens(TalentStateScope scope)
        {
            return scope switch
            {
                TalentStateScope.Match => _matchTokens,
                TalentStateScope.Round => _roundTokens,
                _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
            };
        }

        private HashSet<string> GetFlags(TalentStateScope scope)
        {
            return scope switch
            {
                TalentStateScope.Match => _matchFlags,
                TalentStateScope.Round => _roundFlags,
                _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
            };
        }
    }
}
