using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MahjongGame.Systems;
using DG.Tweening;

namespace MahjongGame.Core
{
    public abstract class MahjongHandViewBase : MonoBehaviour
    {
        [Header("Common Hand Settings")]
        public GameObject tilePrefab;
        public float tileGap = 0.84f;
        public float drawGap = 0.32f;

        [Header("Common Meld Settings")]
        public Transform meldSpawnPoint;
        public float meldGap = 0.2f;
        public float meldTileWidth = 0.8f;

        [Header("Common Refs")]
        public RiverController myRiver;

        protected List<TileVisual> _handTiles = new List<TileVisual>();
        protected float _currentMeldOffset = 0f;

        // 子类特有的手牌排列逻辑
        protected abstract void UpdateHandPositions();

        // 统一处理点击（子类按需重写）
        public virtual void OnTileClicked(TileVisual tile) { }

        protected virtual void ConfigureTileVisual(TileVisual tile) { }

        protected Sprite GetTileSprite(TileData data)
        {
            if (data == null) return null;
            var config = DeckManager.Instance?.tileConfig;
            return config?.GetSprite(data);
        }

        public virtual void ClearHand()
        {
            foreach (var tile in _handTiles)
            {
                if (tile != null)
                {
                    tile.transform.DOKill();
                    Destroy(tile.gameObject);
                }
            }
            _handTiles.Clear();
            _currentMeldOffset = 0f;

            if (meldSpawnPoint != null)
            {
                foreach (Transform child in meldSpawnPoint)
                {
                    child.DOKill();
                    Destroy(child.gameObject);
                }
            }

            // 同时清理牌河
            if (myRiver != null) myRiver.Clear();
        }

        /// <summary>Builds an authoritative river without replaying obsolete motion after recovery.</summary>
        protected void RebuildRiver(IEnumerable<TileData> tiles)
        {
            if (myRiver == null || tilePrefab == null) return;
            myRiver.Clear();
            foreach (var data in tiles ?? Enumerable.Empty<TileData>())
            {
                if (data == null) continue;
                GameObject go = Instantiate(tilePrefab);
                TileVisual visual = go.GetComponent<TileVisual>();
                if (visual == null)
                {
                    Destroy(go);
                    continue;
                }

                visual.Initialize(data, this, GetTileSprite(data));
                myRiver.AddTileToRiverImmediate(visual);
            }
        }

        /// <summary>
        /// 统一的生成副露视觉对象的方法
        /// </summary>
        protected void CreateMeldVisual(MeldType type, List<TileData> meldTiles)
        {
            if (meldSpawnPoint == null || tilePrefab == null) return;

            // 视觉占位数量 (加杠虽然4张，但视觉上只占3张宽度)
            int visualTileCount = (type == MeldType.Kan_Added) ? 3 : meldTiles.Count;

            // 计算该副露起点的 X 坐标 (最左侧边界)
            float totalMeldWidth = visualTileCount * meldTileWidth;
            float startX = _currentMeldOffset - totalMeldWidth;

            for (int i = 0; i < meldTiles.Count; i++)
            {
                TileData data = meldTiles[i];
                GameObject go = Instantiate(tilePrefab, meldSpawnPoint);
                TileVisual visual = go.GetComponent<TileVisual>();
                
                // MCR 暗杠的四张牌均须朝下，明杠/加杠仍保持牌面朝上。
                bool isConcealed = MeldVisualPolicy.IsTileFaceDown(type, i);

                if (!isConcealed)
                {
                    Sprite face = GetTileSprite(data);
                    visual.Initialize(data, this, face);
                }
                else
                {
                    // 扣着的牌不需要初始化花色
                    visual.Initialize(data, this, null);
                }

                ConfigureTileVisual(visual);

                // --- 视觉旋转逻辑 ---
                Quaternion rotation;

                if (!isConcealed)
                {
                    // 碰 (Pon) - 第一张横置
                    if (type == MeldType.Pon && i == 0)
                    {
                        rotation = Quaternion.Euler(90, -90, 0); 
                    }
                    // 加杠 (Kan_Added) - 第1张和第4张横置
                    else if (type == MeldType.Kan_Added && (i == 0 || i == 3))
                    {
                        rotation = Quaternion.Euler(90, -90, 0);
                    }
                    else
                    {
                        rotation = Quaternion.Euler(90, 0, 0);
                    }
                }
                else
                {
                    // 扣牌的旋转：背面朝上
                    rotation = Quaternion.Euler(-90, 0, 0);
                }

                go.transform.localRotation = rotation;

                // --- 位置计算 ---
                float xPos = startX + (i * meldTileWidth);
                float yPos = 0f;

                // 加杠特殊处理：第4张牌(i=3)叠在第1张(i=0)上面
                if (type == MeldType.Kan_Added && i == 3)
                {
                    xPos = startX; 
                    yPos = 0.5f; 
                }
                
                // 微调：横置的牌位置中心点
                if ((type == MeldType.Pon || type == MeldType.Kan_Added) && i == 0)
                {
                    xPos -= 0.15f; 
                }

                go.transform.localPosition = new Vector3(xPos, yPos, 0);
            }

            // 更新偏移量，下一个副露将在此基础上继续向左偏移
            _currentMeldOffset = startX - meldGap;
        }
    }
}
