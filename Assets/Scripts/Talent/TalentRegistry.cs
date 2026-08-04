using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using MahjongGame.Core;

namespace MahjongGame.Talents
{
    public class TalentRegistry
    {
        private static TalentRegistry _instance;
        public static TalentRegistry Instance => _instance ??= new TalentRegistry();

        private Dictionary<string, Type> _talentTypes = new Dictionary<string, Type>();
        private sealed class RegistryEntry
        {
            public TalentTier Tier { get; }
            public int Cost { get; }
            public TalentPhase[] Phases { get; }
            public string DisplayName { get; }
            public string Description { get; }
            public TalentMetadata Metadata { get; }

            public RegistryEntry(TalentRuleAttribute attribute)
            {
                Tier = attribute.Tier;
                Cost = attribute.AlienationCost;
                Phases = attribute.Phases;
                DisplayName = attribute.DisplayName;
                Description = attribute.Description;
                Metadata = new TalentMetadata(
                    attribute.StateScope,
                    attribute.ActivationWindow,
                    attribute.RevealPolicy,
                    attribute.SideboardPolicy);
            }
        }

        private Dictionary<string, RegistryEntry> _entries = new Dictionary<string, RegistryEntry>();

        private TalentRegistry()
        {
            LoadByReflection();
        }

        private void LoadByReflection()
        {
            _talentTypes.Clear();
            _entries.Clear();

            var assembly = Assembly.GetExecutingAssembly();
            var types = assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(TalentRule))
                         && !t.IsAbstract
                         && t.GetCustomAttribute<TalentRuleAttribute>() != null);

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<TalentRuleAttribute>();
                if (!_talentTypes.ContainsKey(attr.Id))
                {
                    _talentTypes[attr.Id] = type;
                    _entries[attr.Id] = new RegistryEntry(attr);
                }
            }

            Debug.Log($"[TalentRegistry] 天赋加载完毕，共 {_talentTypes.Count} 个天赋。");
        }

        public TalentRule CreateInstance(string id, int ownerPlayerId)
        {
            if (!_talentTypes.TryGetValue(id, out var type)) return null;
            var entry = _entries[id];

            var rule = (TalentRule)Activator.CreateInstance(type);
            rule.Initialize(id, entry.Tier, entry.Cost, entry.Phases);
            rule.OwnerPlayerId = ownerPlayerId;
            return rule;
        }

        public TalentMetadata GetMetadata(string talentId)
        {
            if (!_entries.TryGetValue(talentId, out RegistryEntry entry))
                throw new KeyNotFoundException($"Unknown talent id: {talentId}");
            return entry.Metadata;
        }

        public int GetCost(string id) => _entries.TryGetValue(id, out var entry) ? entry.Cost : 0;
        public TalentTier GetTier(string id) => _entries.TryGetValue(id, out var entry) ? entry.Tier : TalentTier.Small;
        public string GetDisplayName(string id) => _entries.TryGetValue(id, out var entry) ? entry.DisplayName : id;
        public string GetDescription(string id) => _entries.TryGetValue(id, out var entry) ? entry.Description : "";
        public List<string> GetAllIds() => _talentTypes.Keys.ToList();
        public bool HasTalent(string id) => _talentTypes.ContainsKey(id);
    }
}
