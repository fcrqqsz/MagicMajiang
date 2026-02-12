using System.Collections.Generic;
using UnityEngine;
using MahjongGame.Systems; // 需要引用 DeckManager
using DG.Tweening; // 引入 DoTween
using System.Linq; // 引用 Linq 用于快速查找

namespace MahjongGame.Core
{
    public class HandController : MonoBehaviour
    {
        private List<TileVisual> _handTiles = new List<TileVisual>();
        
        // 当前选中的牌 (指针)
        private TileVisual _selectedTile = null;

        // [Header("Settings")]
        // public float tileGap = 1.1f;
        public GameObject tilePrefab; // 需要在这里引用Prefab用于抽牌
        [Header("Refs")]
        public RiverController myRiver; // <--- 新增引用
        [Header("Visual Settings")]
        public float tileGap = 1.0f;
        public float drawGap = 1.0f; // [新增] 新摸的牌与手牌的额外距离
        public Vector3 handRotation = new Vector3(25f, 0f, 0f); // 让牌后仰 25 度

        // [重要] 标记最后摸到的那张牌
        private TileVisual _lastDrawnTile = null;

        public TileData LastDrawnData => _lastDrawnTile?.Data;

        public List<Meld> Melds { get; private set; } = new List<Meld>();

        [Header("Meld Settings")]
        public Transform meldSpawnPoint; // 刚才创建的锚点
        public float meldGap = 0.2f;     // 副露之间的间距
        public float meldTileWidth = 0.8f; // 单张副露牌的估算宽度
        // 记录当前已经生成了多宽的副露区域 (用于向左推或者向右排)
        private float _currentMeldOffset = 0f;

        // 获取 Sprite 的辅助方法
        private Sprite GetTileSprite(TileData data)
        {
            var config = MahjongGame.Systems.DeckManager.Instance.tileConfig;
            if (config == null) return null;
            return config.GetSprite(data);
        }

        public List<TileData> GetHandData()
        {
            List<TileData> list = new List<TileData>();
            foreach (var tile in _handTiles)
            {
                list.Add(tile.Data);
            }
            return list;
        }

        public void AddMeld(Meld meld)
        {
            Melds.Add(meld);
            // 生成副露的 3D 模型
            CreateMeldVisual(meld);
        }

        /// <summary>
        /// 生成副露的视觉对象
        /// </summary>
        private void CreateMeldVisual(Meld meld)
        {
            // 简单的排列逻辑：从 meldSpawnPoint 开始向右延伸
            // 更好的做法通常是：副露固定在右边，手牌向左挤，这里为了演示简化处理
            
            // 视觉占位数量 (加杠虽然4张，但视觉上只占3张宽度)
            int visualTileCount = (meld.Type == MeldType.Kan_Added) ? 3 : meld.Tiles.Count;

            for (int i = 0; i < meld.Tiles.Count; i++)
            {
                TileData data = meld.Tiles[i];
                
                // 实例化
                GameObject go = Instantiate(tilePrefab, meldSpawnPoint);
                TileVisual visual = go.GetComponent<TileVisual>();
                
                Sprite face = GetTileSprite(data);
                visual.Initialize(data, null, face); // 传入 s

                // --- 视觉旋转逻辑 ---
                Quaternion rotation;

                // 1. 碰 (Pon) - 第一张横置
                if (meld.Type == MeldType.Pon && i == 0)
                {
                    rotation = Quaternion.Euler(90, -90, 0); 
                }
                // 1.5 加杠 (Kan_Added) - 第1张和第4张(i=3)横置
                else if (meld.Type == MeldType.Kan_Added && (i == 0 || i == 3))
                {
                    rotation = Quaternion.Euler(90, -90, 0);
                }
                // 2. 暗杠 (Kan_Concealed) - 两头扣，中间亮
                // 扣牌的旋转：正面朝下。如果 (90, 0, 0) 是正面朝上，那么 (-90, 0, 0) 就是正面朝下
                else if ((meld.Type == MeldType.Kan_Concealed) && (i == 0 || i == 3))
                {
                    // 第1张和第4张
                    rotation = Quaternion.Euler(-90, 0, 0); // 背面朝上 (扣着)
                }
                // 3. 其他 (吃、明杠、碰的后两张) - 全部正面朝上
                else
                {
                    rotation = Quaternion.Euler(90, 0, 0);
                }

                go.transform.localRotation = rotation;

                // --- 位置计算 ---
                // 注意：横置的牌宽度不同，这里简化处理，假设宽度一致
                // 如果需要精确，横置牌的宽度应该是 tileHeight 而不是 tileWidth
                float xPos = _currentMeldOffset + (i * meldTileWidth);
                float yPos = 0f;

                // 加杠特殊处理：第4张牌(i=3)叠在第1张(i=0)上面
                if (meld.Type == MeldType.Kan_Added && i == 3)
                {
                    xPos = _currentMeldOffset + (0 * meldTileWidth); // 回到第1张的位置
                    yPos = 0.6f; // 向上堆叠 (根据牌厚度调整)
                }
                
                // 微调：如果是横置的牌，位置可能需要修正一下中心点
                if ((meld.Type == MeldType.Pon || meld.Type == MeldType.Kan_Added) && i == 0)
                {
                    // 简单的修正，让横着的牌不跟后面的重叠
                    // xPos -= 0.1f; 
                }

                go.transform.localPosition = new Vector3(xPos, yPos, 0);
            }

            // 更新偏移量 (3张牌 + 额外间距)
            _currentMeldOffset += (visualTileCount * meldTileWidth) + meldGap;
        }
        
        /// <summary>
        /// 执行 "碰" 操作
        /// </summary>
        /// <param name="targetTile">别人打出的那张牌</param>
        public void ExecutePon(TileData targetTile)
        {
            // 1. 在手牌里找两张一样的牌
            var matchingTiles = _handTiles
                .Where(t => t.Data.TileSuit == targetTile.TileSuit && t.Data.Value == targetTile.Value)
                .Take(2)
                .ToList();

            if (matchingTiles.Count < 2)
            {
                Debug.LogError("严重错误：试图碰牌，但手牌里没有两张同牌！");
                return;
            }

            // 3. 构建副露数据 (Meld Data)
            List<TileData> meldDataList = new List<TileData> { targetTile, matchingTiles[0].Data, matchingTiles[1].Data };
            Meld newMeld = new Meld(MeldType.Pon, meldDataList, targetTile.OriginalOwnerID);
            AddMeld(newMeld);

            // 2. 从手牌列表移除，并销毁 3D 物体
            foreach (var visual in matchingTiles)
            {
                _handTiles.Remove(visual);
                Destroy(visual.gameObject); // 或者做个飞过去的动画再销毁
            }
            _lastDrawnTile = null;

            // 5. 整理剩余手牌
            SortHand();
            UpdateHandPositions();
        }

        /// <summary>
        /// 执行 "明杠" 操作
        /// </summary>
        public void ExecuteMingGan(TileData targetTile)
        {
            // 1. 在手牌里找 3 张一样的牌
            var matchingTiles = _handTiles
                .Where(t => t.Data.TileSuit == targetTile.TileSuit && t.Data.Value == targetTile.Value)
                .Take(3) // 找3张
                .ToList();

            if (matchingTiles.Count < 3)
            {
                Debug.LogError("错误：试图明杠，但手牌不足3张同牌！");
                return;
            }

            // 2. 移除手牌
            foreach (var visual in matchingTiles)
            {
                _handTiles.Remove(visual);
                Destroy(visual.gameObject);
            }

            // 3. 构建副露数据 (4张牌)
            List<TileData> meldData = new List<TileData> 
            { 
                targetTile, 
                matchingTiles[0].Data, 
                matchingTiles[1].Data, 
                matchingTiles[2].Data 
            };
            
            Meld newMeld = new Meld(MeldType.Kan_Exposed, meldData, targetTile.OriginalOwnerID);
            Melds.Add(newMeld);

            // 4. 生成视觉对象
            CreateMeldVisual(newMeld);

            // 5. 理牌
            SortHand();
            UpdateHandPositions();
        }

        /// <summary>
        /// 获取可以暗杠的牌 (返回牌的 Value 列表，区分花色)
        /// </summary>
        public List<TileData> GetAnGanOptions()
        {
            // 查找手牌中数量 == 4 的牌
            var groups = _handTiles
                .GroupBy(t => new { t.Data.TileSuit, t.Data.Value })
                .Where(g => g.Count() >= 4)
                .Select(g => g.First().Data) // 取其中一张做代表
                .ToList();
            
            return groups;
        }

        /// <summary>
        /// 执行暗杠
        /// </summary>
        public void ExecuteAnGan(TileData targetData)
        {
            // 1. 找到这4张牌
            var matchingTiles = _handTiles
                .Where(t => t.Data.TileSuit == targetData.TileSuit && t.Data.Value == targetData.Value)
                .ToList();

            if (matchingTiles.Count < 4) return;

            // 2. [关键] 只取前 4 张移除，多余的保留在手牌中
            var tilesToRemove = matchingTiles.Take(4).ToList(); 

            // 3. 构建副露数据
            List<TileData> meldData = new List<TileData>();
            foreach (var visual in tilesToRemove)
            {
                meldData.Add(visual.Data);
                
                // 从手牌列表移除并销毁物体
                _handTiles.Remove(visual);
                Destroy(visual.gameObject);
            }

            // 3. 添加副露 (暗杠)
            // 暗杠的 SourceID 是自己
            Meld newMeld = new Meld(MeldType.Kan_Concealed, meldData, targetData.OriginalOwnerID);
            Melds.Add(newMeld);

            // 4. 视觉生成 (暗杠通常中间两张是扣着的，这里暂用普通生成)
            CreateMeldVisual(newMeld);
            
            // 5. 状态清理
            _lastDrawnTile = null; // 杠完后需要重新摸牌，当前状态清空
            
            SortHand();
            UpdateHandPositions();
        }

        /// <summary>
        /// 获取可以加杠的牌 (返回牌的 Value 列表)
        /// </summary>
        public List<TileData> GetJiaGangOptions()
        {
            List<TileData> options = new List<TileData>();

            foreach (var meld in Melds)
            {
                if (meld.Type == MeldType.Pon)
                {
                    // 在手牌里找匹配的
                    var match = _handTiles.FirstOrDefault(t => t.Data.TileSuit == meld.FirstTile.TileSuit && t.Data.Value == meld.FirstTile.Value);
                    if (match != null)
                    {
                        options.Add(match.Data);
                    }
                }
            }
            return options;
        }

        /// <summary>
        /// 执行加杠
        /// </summary>
        public void ExecuteJiaGang(TileData targetData)
        {
            // 1. 从手牌移除这张牌
            var tileToRemove = _handTiles.FirstOrDefault(t => t.Data.TileSuit == targetData.TileSuit && t.Data.Value == targetData.Value);
            if (tileToRemove == null) return;

            _handTiles.Remove(tileToRemove);
            Destroy(tileToRemove.gameObject);

            // 2. 找到对应的副露并修改数据
            var targetMeld = Melds.FirstOrDefault(m => m.Type == MeldType.Pon && m.FirstTile.TileSuit == targetData.TileSuit && m.FirstTile.Value == targetData.Value);
            if (targetMeld != null)
            {
                // 修改类型为 加杠
                targetMeld.Type = MeldType.Kan_Added;
                targetMeld.Tiles.Add(targetData); // 数据加进去
                
                // 3. 视觉更新
                // 简单做法：销毁旧的副露，重新生成一个新的
                // 高级做法：找到那个副露的 GameObject，往上加一张牌
                // 这里采用简单做法：清空副露区，重新生成所有副露 (RefreshAllMelds)
                
                // 为了演示，我们假设你有一个清除 Melds 视觉的方法
                // 或者我们直接找到那个 Transform 删掉重画
                // 这里简化：直接销毁所有副露子物体，重新 CreateMeldVisual
                RefreshAllMeldsVisual();
            }

            // 4. 状态清理
            _lastDrawnTile = null; 
            SortHand();
            UpdateHandPositions();
        }

        // 辅助：刷新所有副露显示 (简单粗暴但有效)
        private void RefreshAllMeldsVisual()
        {
            // 清空 MeldSpawnPoint 下所有物体
            foreach (Transform child in meldSpawnPoint) Destroy(child.gameObject);
            
            _currentMeldOffset = 0f;
            foreach (var meld in Melds)
            {
                CreateMeldVisual(meld);
            }
        }

        /// <summary>
        /// [重构] 获取所有能吃这张牌的组合 (去重版)
        /// 返回：List<int[]>，每个数组包含两个整数，代表用来吃的那两张牌的 Value
        /// </summary>
        public List<int[]> GetChiCombinations(TileData target)
        {
            List<int[]> combos = new List<int[]>();
            
            // 字牌不能吃
            if (target.TileSuit == Suit.Wind || target.TileSuit == Suit.Dragon) return combos;

            int val = target.Value;
            
            // 1. 获取手里该花色所有【不重复】的数值
            // 这一步解决了 "重复选项" 问题
            var distinctValues = _handTiles
                .Where(t => t.Data.TileSuit == target.TileSuit)
                .Select(t => t.Data.Value)
                .Distinct() // <--- 关键：去重
                .ToHashSet();

            // 2. 检查三种吃法 (左/中/右)
            // 左吃 (val-2, val-1)
            if (distinctValues.Contains(val - 2) && distinctValues.Contains(val - 1))
                combos.Add(new int[] { val - 2, val - 1 });

            // 中吃 (val-1, val+1)
            if (distinctValues.Contains(val - 1) && distinctValues.Contains(val + 1))
                combos.Add(new int[] { val - 1, val + 1 });

            // 右吃 (val+1, val+2)
            if (distinctValues.Contains(val + 1) && distinctValues.Contains(val + 2))
                combos.Add(new int[] { val + 1, val + 2 });

            return combos;
        }

        /// <summary>
        /// [重构] 执行 "吃" 操作 (按数值删除)
        /// </summary>
        public void ExecuteChi(TileData targetTile, int[] eatingValues)
        {
            // 1. 准备要存入副露的数据
            List<TileData> meldData = new List<TileData>();
            meldData.Add(targetTile); // 第一张是吃进来的牌

            // 2. 从手牌中查找并移除对应数值的牌
            foreach (int val in eatingValues)
            {
                // 查找手里第一张符合该数值的牌
                // 注意：由于我们之前做了去重判定，这里肯定能找到至少一张
                var visual = _handTiles.FirstOrDefault(t => 
                    t.Data.TileSuit == targetTile.TileSuit && 
                    t.Data.Value == val);

                if (visual != null)
                {
                    meldData.Add(visual.Data); // 记录数据
                    _handTiles.Remove(visual); // 移除列表
                    Destroy(visual.gameObject); // 销毁物体
                }
            }
            _lastDrawnTile = null;

            // 3. 生成副露对象
            // 为了显示美观，我们把 meldData 排序： 比如吃了3，用24，显示应该是 234
            meldData.Sort((a, b) => a.Value.CompareTo(b.Value));

            Meld newMeld = new Meld(MeldType.Chi, meldData, targetTile.OriginalOwnerID);
            Melds.Add(newMeld);

            // 4. 生成视觉 & 理牌
            CreateMeldVisual(newMeld);
            SortHand();
            UpdateHandPositions();
        }

        // --- 1. 抽牌逻辑 (Draw) ---
        public void DrawCard()
        {
            // A. 从数据层拿数据
            TileData newData = DeckManager.Instance.DrawTile();
            if (newData == null) 
            {
                Debug.Log("无牌可摸！");
                return;
            }

            // >>> 埋点 1: 通知天赋系统 "我摸牌了" <<<
            // 天赋可能会修改 newData 的属性 (引用传递)
            TalentManager.Instance.TriggerOnDraw(newData);

            // B. 生成实体
            AddTileDirectly(newData);
        }

        /// <summary>
        /// 直接向手牌中添加一张指定的牌 (用于测试或特殊逻辑)
        /// </summary>
        public void AddTileDirectly(TileData data)
        {
            if (data == null) return;

            GameObject go = Instantiate(tilePrefab);
            TileVisual visual = go.GetComponent<TileVisual>();
            Sprite face = GetTileSprite(data);
            visual.Initialize(data, this, face);
            AddTileToHand(visual);
        }

        // 内部辅助：加入列表并刷新
        private void AddTileToHand(TileVisual visual)
        {
            visual.transform.SetParent(this.transform);
            _handTiles.Add(visual);
            _lastDrawnTile = visual; // 记录它是刚摸的
            
            // 抽牌后通常自动理牌 (为了简化演示，这里直接重排)
            UpdateHandPositions();
        }

        // --- 2. 交互逻辑 (Click Handler) ---
        public void OnTileClicked(TileVisual clickedTile)
        {
            if (!_isInteractable) return; // 如果没轮到我，点了没反应
            
            // 情况 A: 点击了已经选中的牌 -> 确认出牌
            if (_selectedTile == clickedTile)
            {
                DiscardTile(clickedTile);
            }
            // 情况 B: 点击了其他牌 -> 切换选中
            else
            {
                SelectTile(clickedTile);
            }
        }

        // 选中状态处理
        private void SelectTile(TileVisual tile)
        {
            // 1. 如果之前有选中的牌，先把它放回去
            if (_selectedTile != null)
            {
                _selectedTile.transform.localPosition = new Vector3(_selectedTile.transform.localPosition.x, 0, 0);
            }

            // 2. 更新选中引用
            _selectedTile = tile;

            // 3. 让新选中的牌浮起来 (视觉反馈)
            // Y轴向上移动 0.5 单位
            Vector3 pos = tile.transform.localPosition;
            tile.transform.localPosition = new Vector3(pos.x, 0.5f, pos.z);
            
            Debug.Log($"选中了: {tile.Data}");
        }

        // --- 3. 出牌逻辑 (Discard) ---
        private void DiscardTile(TileVisual tile)
        {
            // >>> 埋点 2: 通知天赋系统 "我打牌了" <<<
            TalentManager.Instance.TriggerOnDiscard(tile.Data);

            // 1. 从逻辑列表移除
            _handTiles.Remove(tile);
            
            // 2. 清除选中状态
            _selectedTile = null;

            _lastDrawnTile = null;

            // 2. 视觉处理：交给牌河控制器
            if (myRiver != null) {
                myRiver.AddTileToRiver(tile);
            } else {
                Destroy(tile.gameObject); // 保底逻辑
            }

            // 4. 重新整理手牌 (填补空缺)
            SortHand();
            UpdateHandPositions();

            // 通知回合管理器
            if (MahjongGame.Systems.TurnManager.Instance != null)
            {
                MahjongGame.Systems.TurnManager.Instance.OnPlayerDiscarded();
            }
        }

        // --- 辅助：保持之前的排序和刷新逻辑 ---
        public void SortHand()
        {
            _handTiles.Sort((a, b) =>
            {
                if (a.Data.TileSuit != b.Data.TileSuit) return a.Data.TileSuit.CompareTo(b.Data.TileSuit);
                return a.Data.Value.CompareTo(b.Data.Value);
            });
            
            // 同步 Hierarchy 顺序
            for(int i=0; i<_handTiles.Count; i++) _handTiles[i].transform.SetSiblingIndex(i);
        }

        private void UpdateHandPositions()
        {
            for (int i = 0; i < _handTiles.Count; i++)
            {

                // 计算当前牌的目标 X
                float targetX = i * tileGap;

                // [核心逻辑] 如果是最后一张牌，并且手牌数量是 14 (或其他满手牌状态如 2, 5, 8, 11, 14)
                // 且这张牌就是我们刚摸上来的那张，则增加额外间距
                if (i == _handTiles.Count - 1 && _handTiles[i] == _lastDrawnTile)
                {
                    targetX += drawGap;
                }

                // 目标位置
                Vector3 targetPos = new Vector3(targetX, 0, 0);
                
                // 目标旋转 (基础后仰 + 以后可能的弯曲)
                Quaternion targetRot = Quaternion.Euler(handRotation);

                // 使用 DoTween 平滑移动和旋转
                _handTiles[i].transform.DOLocalMove(targetPos, 0.3f);
                _handTiles[i].transform.DOLocalRotate(handRotation, 0.3f);
            }
        }

        // 清空 (用于重置)
        public void ClearHand()
        {
            foreach(var tile in _handTiles) Destroy(tile.gameObject);
            _handTiles.Clear();
            _selectedTile = null;
        }

        private bool _isInteractable = false; // 交互锁

        public void SetInteractable(bool canInteract)
        {
            _isInteractable = canInteract;
        }
    }
}