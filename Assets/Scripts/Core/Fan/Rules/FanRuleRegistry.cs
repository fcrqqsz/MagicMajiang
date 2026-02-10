using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MahjongGame.Core.Fan
{
    public class FanRuleRegistry
    {
        private static FanRuleRegistry _instance;
        public static FanRuleRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FanRuleRegistry();
                }
                return _instance;
            }
        }

        // 存储所有规则: Key = RuleID
        private Dictionary<string, FanRule> _ruleDatabase = new Dictionary<string, FanRule>();

        // 当前激活的规则列表 (用于计算)
        public List<FanRule> ActiveRules => _ruleDatabase.Values.ToList();

        private FanRuleRegistry()
        {
            LoadRulesByReflection();
        }

        private void LoadRulesByReflection()
        {
            _ruleDatabase.Clear();

            // 1. 获取当前程序集中的所有类型
            var assembly = Assembly.GetExecutingAssembly();
            
            // 2. 筛选：继承自 FanRule 且 带有 FanRuleAttribute 的非抽象类
            var ruleTypes = assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(FanRule)) && !t.IsAbstract && t.GetCustomAttribute<FanRuleAttribute>() != null);

            foreach (var type in ruleTypes)
            {
                // 读取 Attribute
                var attr = type.GetCustomAttribute<FanRuleAttribute>();

                // 实例化
                FanRule rule = (FanRule)Activator.CreateInstance(type);
                
                // 注入数据
                rule.Initialize(attr.Id, attr.DefaultFan);

                // 注册
                if (!_ruleDatabase.ContainsKey(attr.Id))
                {
                    _ruleDatabase.Add(attr.Id, rule);
                    // Debug.Log($"[Registry] 已加载番种: {rule.Name} ({rule.FanValue}番)");
                }
            }
            
            Debug.Log($"[Registry] 番种加载完毕，共 {_ruleDatabase.Count} 个规则。");
        }

        // --- 天赋接口 ---
        
        /// <summary>
        /// 修改指定番种的分值
        /// </summary>
        public void ModifyRuleFan(string ruleId, int delta)
        {
            if (_ruleDatabase.TryGetValue(ruleId, out FanRule rule))
            {
                rule.ApplyModifier(delta);
                Debug.Log($"[天赋生效] {rule.Name} 的番数变更了 {delta}，当前为 {rule.FanValue}。");
            }
        }
    }
}