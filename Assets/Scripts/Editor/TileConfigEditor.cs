using UnityEngine;
using UnityEditor;
using MahjongGame.Core;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MahjongGame.Editor
{
    [CustomEditor(typeof(TileResourceConfig))]
    public class TileConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GUILayout.Space(20);
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("自动扫描并填充图片 (Auto Fill)", GUILayout.Height(40)))
            {
                AutoFill();
            }
            GUI.backgroundColor = Color.white;
            
            GUILayout.Space(10);
            EditorGUILayout.HelpBox("支持命名格式:\n" +
                                    "1. 数字前缀: 1m, 5p, 7z (推荐)\n" +
                                    "2. 字母前缀: m1, p5, z7\n" +
                                    "字牌定义: 1z-4z(东南西北), 5z(白), 6z(发), 7z(中)", MessageType.Info);
        }

        private void AutoFill()
        {
            TileResourceConfig config = (TileResourceConfig)target;
            
            // 1. 初始化数组 (固定长度 34)
            config.allTileSprites = new Sprite[34];

            // 2. 查找所有 Sprite
            string[] guids = AssetDatabase.FindAssets("t:Sprite");
            int count = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;

                string name = sprite.name.ToLower();
                var match = Regex.Match(name, @"^(\d)([mpsz])$|^([mpsz])(\d)$");

                if (match.Success)
                {
                    string charPart;
                    int numPart;

                    if (match.Groups[1].Success) 
                    {
                        numPart = int.Parse(match.Groups[1].Value);
                        charPart = match.Groups[2].Value;
                    }
                    else
                    {
                        charPart = match.Groups[3].Success ? match.Groups[3].Value : match.Groups[2].Value;
                        numPart = int.Parse(match.Groups[4].Success ? match.Groups[4].Value : match.Groups[1].Value);
                    }

                    int targetIndex = -1;

                    // --- 计算目标索引 (对齐 MahjongLogic.GetTileIndex) ---
                    // 万 (m): 0-8
                    if (charPart == "m" && IsInRange(numPart, 1, 9)) targetIndex = numPart - 1;
                    // 筒 (p): 9-17
                    else if (charPart == "p" && IsInRange(numPart, 1, 9)) targetIndex = 8 + numPart;
                    // 索 (s): 18-26
                    else if (charPart == "s" && IsInRange(numPart, 1, 9)) targetIndex = 17 + numPart;
                    // 字牌 (z)
                    else if (charPart == "z")
                    {
                        // 东南西北 (1z-4z): 27-30
                        if (IsInRange(numPart, 1, 4)) targetIndex = 26 + numPart;
                        // 中发白 (7z, 6z, 5z): 31, 32, 33
                        // 映射逻辑: z7(中)->31, z6(发)->32, z5(白)->33
                        else if (IsInRange(numPart, 5, 7)) targetIndex = 31 + (7 - numPart);
                    }

                    if (targetIndex != -1 && targetIndex < 34)
                    {
                        config.allTileSprites[targetIndex] = sprite;
                        count++;
                    }
                }
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log($"<color=green>自动填充完成！成功匹配并填入了 {count} 张图片到统一索引数组。</color>");
            
            if (config.allTileSprites[33] == null)
            {
                Debug.LogWarning("提示: 白板 (5z) 没有找到对应的图片。索引 33 为空。");
            }
        }

        private bool IsInRange(int val, int min, int max) => val >= min && val <= max;
    }
}