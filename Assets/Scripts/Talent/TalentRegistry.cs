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
        private Dictionary<string, (TalentTier tier, int cost, TalentPhase[] phases, string displayName, string description)> _metadata
            = new Dictionary<string, (TalentTier, int, TalentPhase[], string, string)>();

        private TalentRegistry()
        {
            LoadByReflection();
        }

        private void LoadByReflection()
        {
            _talentTypes.Clear();
            _metadata.Clear();

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
                    _metadata[attr.Id] = (attr.Tier, attr.AlienationCost, attr.Phases, attr.DisplayName, attr.Description);
                }
            }

            Debug.Log($"[TalentRegistry] 天赋加载完毕，共 {_talentTypes.Count} 个天赋。");
        }

        public TalentRule CreateInstance(string id, int ownerPlayerId)
        {
            if (!_talentTypes.TryGetValue(id, out var type)) return null;
            var meta = _metadata[id];

            var rule = (TalentRule)Activator.CreateInstance(type);
            rule.Initialize(id, meta.tier, meta.cost, meta.phases);
            rule.OwnerPlayerId = ownerPlayerId;
            return rule;
        }

        public int GetCost(string id) => _metadata.TryGetValue(id, out var m) ? m.cost : 0;
        public TalentTier GetTier(string id) => _metadata.TryGetValue(id, out var m) ? m.tier : TalentTier.Small;
        public string GetDisplayName(string id) => _metadata.TryGetValue(id, out var m) ? m.displayName : id;
        public string GetDescription(string id) => _metadata.TryGetValue(id, out var m) ? m.description : "";
        public List<string> GetAllIds() => _talentTypes.Keys.ToList();
        public bool HasTalent(string id) => _talentTypes.ContainsKey(id);
    }
}
