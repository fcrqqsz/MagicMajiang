using System.Collections.Generic;
using UnityEngine;
using DG.Tweening; // 如果你没装 DoTween，我会提供标准插值替代方案

namespace MahjongGame.Core
{
    public class RiverController : MonoBehaviour
    {
        [Header("Layout Settings")]
        public float xGap = 0.9f; // 牌宽度间隔
        public float zGap = 1.2f; // 牌高度间隔 (行距)
        public int tilesPerRow = 6; // 每行几张牌

        // 存储已经打入牌河的牌
        private List<TileVisual> _discardedTiles = new List<TileVisual>();

        /// <summary>
        /// 接收一张打出的牌
        /// </summary>
        public void AddTileToRiver(TileVisual tile)
        {
            // 1. 数据记录
            _discardedTiles.Add(tile);
            
            // 2. 变更父节点 (这一步很重要，让牌跟随牌河物体移动)
            tile.transform.SetParent(this.transform);

            // 3. 计算目标位置 (局部坐标)
            int index = _discardedTiles.Count - 1;
            int row = index / tilesPerRow;
            int col = index % tilesPerRow;

            Vector3 targetPos = new Vector3(col * xGap, 0, -row * zGap);
            
            // 4. 移动动画 (如果你有 DoTween)
            tile.transform.DOLocalMove(targetPos, 0.5f);
            tile.transform.DOLocalRotate(new Vector3(90, 0, 0), 0.5f); // 牌河里的牌通常是倒下的
        }

        // 获取最后打出的一张牌 (用于吃碰杠判定)
        public TileVisual GetLastDiscard()
        {
            if (_discardedTiles.Count == 0) return null;
            return _discardedTiles[_discardedTiles.Count - 1];
        }

        /// <summary>
        /// 移除最后打入的一张牌 (被吃碰杠走了)
        /// </summary>
        public void RemoveLastDiscard()
        {
            if (_discardedTiles.Count == 0) return;

            TileVisual last = _discardedTiles[_discardedTiles.Count - 1];
            _discardedTiles.RemoveAt(_discardedTiles.Count - 1);
            
            Destroy(last.gameObject);
        }
    }
}