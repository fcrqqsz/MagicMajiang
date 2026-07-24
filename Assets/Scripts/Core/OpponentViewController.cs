using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DG.Tweening;

namespace MahjongGame.Core
{
    public class OpponentViewController : MahjongHandViewBase
    {
        [Header("Opponent Specific Settings")]
        public Transform handSpawnPoint;
        public Vector3 opponentHandRotation = new Vector3(0f, 0f, 0f); 

        private bool _justDrawn = false;

        public void InitHand(int count)
        {
            InitHand(count, true);
        }

        private void InitHand(int count, bool animate)
        {
            ClearHand();
            for (int i = 0; i < count; i++) AddTileBack();
            if (animate) UpdateHandPositions();
            else UpdateHandPositionsImmediately();
        }

        public void DrawCard()
        {
            AddTileBack();
            _justDrawn = true;
            UpdateHandPositions();
        }

        public void DiscardTile(TileData discardedData)
        {
            if (_handTiles.Count > 0)
            {
                TileVisual tileToRemove = _handTiles[_handTiles.Count - 1];
                _handTiles.RemoveAt(_handTiles.Count - 1);
                tileToRemove.transform.DOKill();
                Destroy(tileToRemove.gameObject);
            }
            
            _justDrawn = false;
            UpdateHandPositions();

            // 生成一张明牌打入该玩家的牌河
            if (myRiver != null && tilePrefab != null)
            {
                GameObject go = Instantiate(tilePrefab);
                TileVisual visual = go.GetComponent<TileVisual>();
                Sprite face = GetTileSprite(discardedData);
                if (visual != null) visual.Initialize(discardedData, this, face);
                
                myRiver.AddTileToRiver(visual);
            }
        }

        private void AddTileBack()
        {
            if (tilePrefab == null) return;
            GameObject go = Instantiate(tilePrefab, handSpawnPoint);
            TileVisual visual = go.GetComponent<TileVisual>();
            
            // 为了防止在 TileVisual.Initialize 中 Data 报空指针，我们随便给个 Dummy Data
            TileData dummyData = new TileData(Suit.Wind, 0, -1);
            visual.Initialize(dummyData, this, null);
            
            go.transform.localRotation = Quaternion.Euler(opponentHandRotation);
            _handTiles.Add(visual);
        }

        protected override void UpdateHandPositions()
        {
            for (int i = 0; i < _handTiles.Count; i++)
            {
                float targetX = i * tileGap;
                if (_justDrawn && i == _handTiles.Count - 1) targetX += drawGap;
                _handTiles[i].transform.DOKill(); // 在播放新动画前杀掉旧动画，防止冲突
                _handTiles[i].transform.DOLocalMove(new Vector3(targetX, 0, 0), 0.3f).SetLink(_handTiles[i].gameObject);
                _handTiles[i].transform.DOLocalRotate(opponentHandRotation, 0.3f).SetLink(_handTiles[i].gameObject);
            }
        }

        public override void ClearHand()
        {
            base.ClearHand();
            _justDrawn = false;
        }

        /// <summary>Rebuilds public opponent information only; every concealed tile remains a back.</summary>
        public void RebuildFromSnapshot(int concealedTileCount, IEnumerable<Meld> melds, IEnumerable<TileData> river)
        {
            InitHand(Mathf.Max(0, concealedTileCount), false);
            foreach (var meld in melds ?? Enumerable.Empty<Meld>())
            {
                if (meld?.Tiles == null || meld.Tiles.Count == 0) continue;
                CreateMeldVisual(meld.Type, meld.Tiles);
            }

            RebuildRiver(river);
        }

        private void UpdateHandPositionsImmediately()
        {
            for (int i = 0; i < _handTiles.Count; i++)
            {
                var tile = _handTiles[i];
                tile.transform.DOKill();
                tile.transform.localPosition = new Vector3(i * tileGap, 0, 0);
                tile.transform.localRotation = Quaternion.Euler(opponentHandRotation);
            }
        }

        // --- 副露表现 ---
        public void ExecuteMeld(MeldType type, List<TileData> meldTiles)
        {
            // 1. 从手牌扣除对应的牌数
            int tilesToRemove = type == MeldType.Kan_Concealed ? 4 : (type == MeldType.Kan_Added ? 1 : (type == MeldType.Kan_Exposed ? 3 : 2));
            for(int i = 0; i < tilesToRemove; i++)
            {
                if (_handTiles.Count > 0)
                {
                    TileVisual t = _handTiles[_handTiles.Count - 1];
                    _handTiles.RemoveAt(_handTiles.Count - 1);
                    t.transform.DOKill();
                    Destroy(t.gameObject);
                }
            }
            UpdateHandPositions();

            // 2. 复用基类逻辑生成副露模型
            CreateMeldVisual(type, meldTiles);
        }
    }
}
