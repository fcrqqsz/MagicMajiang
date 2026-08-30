using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DG.Tweening;
using MahjongGame.Core.Network;

namespace MahjongGame.Core
{
    public class OpponentViewController : MahjongHandViewBase
    {
        [Header("Opponent Specific Settings")]
        public Transform handSpawnPoint;
        public Vector3 opponentHandRotation = new Vector3(0f, 0f, 0f); 

        private bool _justDrawn = false;
        private int _concealedTileCount;
        private List<PrivateKnownTileFace> _knownTiles = new List<PrivateKnownTileFace>();
        private readonly OpponentMeldState _meldState = new OpponentMeldState();

        public void InitHand(int count)
        {
            InitHand(count, true);
        }

        private void InitHand(int count, bool animate)
        {
            ClearHand();
            _concealedTileCount = Mathf.Max(0, count);
            RebuildConcealedTiles(animate, null);
        }

        public void DrawCard()
        {
            _concealedTileCount++;
            _justDrawn = true;
            RebuildConcealedTiles(true, null);
        }

        public void DiscardTile(TileData discardedData)
        {
            _concealedTileCount = Mathf.Max(0, _concealedTileCount - 1);
            _justDrawn = false;
            RebuildConcealedTiles(true, null);

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

        private TileVisual AddKnownTile(PrivateKnownTileFace knownTile)
        {
            if (tilePrefab == null || knownTile == null) return null;
            GameObject go = Instantiate(tilePrefab, handSpawnPoint);
            TileVisual visual = go.GetComponent<TileVisual>();
            if (visual == null)
            {
                Destroy(go);
                return null;
            }

            var data = new TileData(knownTile.Suit, knownTile.Value, -1)
            {
                IsModified = knownTile.IsModified
            };
            visual.Initialize(data, this, GetTileSprite(data));
            go.transform.localRotation = Quaternion.Euler(GetKnownFaceRotation());
            _handTiles.Add(visual);
            return visual;
        }

        protected override void UpdateHandPositions()
        {
            bool hasDrawGap = _justDrawn && _handTiles.Count > 1;
            OpponentKnownTileDisplay display = OpponentKnownTileDisplayPolicy.Build(
                _concealedTileCount,
                _knownTiles);
            for (int i = 0; i < _handTiles.Count; i++)
            {
                float targetX = GameTableLayoutPolicy.GetCenteredHandX(
                    _handTiles.Count,
                    i,
                    tileGap,
                    hasDrawGap,
                    drawGap);
                _handTiles[i].transform.DOKill(); // 在播放新动画前杀掉旧动画，防止冲突
                _handTiles[i].transform.DOLocalMove(new Vector3(targetX, 0, 0), 0.3f).SetLink(_handTiles[i].gameObject);
                _handTiles[i].transform.DOLocalRotate(
                        GetTileRotation(display.GetVisualKindAt(i)),
                        0.3f)
                    .SetLink(_handTiles[i].gameObject);
            }
        }

        public override void ClearHand()
        {
            base.ClearHand();
            _meldState.Clear();
            _justDrawn = false;
            _concealedTileCount = 0;
            _knownTiles.Clear();
        }

        /// <summary>Rebuilds public opponent state without any private known-hand faces.</summary>
        public void RebuildFromSnapshot(int concealedTileCount, IEnumerable<Meld> melds, IEnumerable<TileData> river)
        {
            RebuildFromSnapshot(
                concealedTileCount,
                Enumerable.Empty<PrivateKnownTileFace>(),
                melds,
                river);
        }

        public void RebuildFromSnapshot(
            int concealedTileCount,
            IEnumerable<PrivateKnownTileFace> knownTiles,
            IEnumerable<Meld> melds,
            IEnumerable<TileData> river)
        {
            ClearHand();
            _concealedTileCount = Mathf.Max(0, concealedTileCount);
            _knownTiles = OpponentKnownTileDisplayPolicy.Build(_concealedTileCount, knownTiles)
                .KnownTiles.ToList();
            RebuildConcealedTiles(false, null);
            _meldState.Replace(melds);
            RebuildMeldVisuals();

            RebuildRiver(river);
        }

        public void ApplyKnownTiles(IEnumerable<PrivateKnownTileFace> knownTiles)
        {
            List<PrivateKnownTileFace> next = OpponentKnownTileDisplayPolicy
                .Build(_concealedTileCount, knownTiles)
                .KnownTiles.ToList();
            List<PrivateKnownTileFace> added = SubtractFaces(next, _knownTiles);
            List<PrivateKnownTileFace> removed = SubtractFaces(_knownTiles, next);
            AnimateRemovedKnownFaces(removed);
            _knownTiles = next;
            RebuildConcealedTiles(true, added);
        }

        private void RebuildConcealedTiles(bool animate, IReadOnlyList<PrivateKnownTileFace> highlightFaces)
        {
            foreach (TileVisual tile in _handTiles)
            {
                if (tile == null) continue;
                tile.transform.DOKill();
                Destroy(tile.gameObject);
            }
            _handTiles.Clear();

            OpponentKnownTileDisplay display = OpponentKnownTileDisplayPolicy.Build(
                _concealedTileCount,
                _knownTiles);
            for (int index = 0; index < display.UnknownTileCount; index++) AddTileBack();

            var remainingHighlights = (highlightFaces ?? System.Array.Empty<PrivateKnownTileFace>()).ToList();
            foreach (PrivateKnownTileFace knownTile in display.KnownTiles)
            {
                TileVisual visual = AddKnownTile(knownTile);
                int highlightIndex = remainingHighlights.FindIndex(tile => SameFace(tile, knownTile));
                if (!animate || visual == null || highlightIndex < 0) continue;
                remainingHighlights.RemoveAt(highlightIndex);
                visual.SetObservationHighlight(true);
                DOVirtual.DelayedCall(0.65f, () =>
                    {
                        if (visual != null) visual.SetObservationHighlight(false);
                        _justDrawn = false;
                        UpdateHandPositions();
                    })
                    .SetLink(visual.gameObject);
            }

            if (animate) UpdateHandPositions();
            else UpdateHandPositionsImmediately();
        }

        private void AnimateRemovedKnownFaces(IEnumerable<PrivateKnownTileFace> removedFaces)
        {
            foreach (PrivateKnownTileFace face in removedFaces ?? Enumerable.Empty<PrivateKnownTileFace>())
            {
                if (tilePrefab == null || face == null) continue;
                GameObject go = Instantiate(tilePrefab, handSpawnPoint);
                TileVisual visual = go.GetComponent<TileVisual>();
                if (visual == null)
                {
                    Destroy(go);
                    continue;
                }
                var data = new TileData(face.Suit, face.Value, -1) { IsModified = face.IsModified };
                visual.Initialize(data, this, GetTileSprite(data));
                Vector3 knownRotation = GetKnownFaceRotation();
                go.transform.localRotation = Quaternion.Euler(knownRotation);
                go.transform.localPosition = Vector3.zero;
                if (visual.faceRenderer != null)
                {
                    visual.faceRenderer.DOFade(0f, 0.22f)
                        .SetLink(go);
                }
                go.transform.DOLocalRotate(knownRotation + new Vector3(0f, 90f, 0f), 0.22f)
                    .OnComplete(() => Destroy(go))
                    .SetLink(go);
            }
        }

        private static List<PrivateKnownTileFace> SubtractFaces(
            IEnumerable<PrivateKnownTileFace> source,
            IEnumerable<PrivateKnownTileFace> subtract)
        {
            var remaining = (subtract ?? Enumerable.Empty<PrivateKnownTileFace>()).ToList();
            var result = new List<PrivateKnownTileFace>();
            foreach (PrivateKnownTileFace face in source ?? Enumerable.Empty<PrivateKnownTileFace>())
            {
                int index = remaining.FindIndex(candidate => SameFace(candidate, face));
                if (index >= 0) remaining.RemoveAt(index);
                else result.Add(face);
            }
            return result;
        }

        private static bool SameFace(PrivateKnownTileFace first, PrivateKnownTileFace second) =>
            first != null
            && second != null
            && first.Suit == second.Suit
            && first.Value == second.Value
            && first.IsModified == second.IsModified;

        private void UpdateHandPositionsImmediately()
        {
            OpponentKnownTileDisplay display = OpponentKnownTileDisplayPolicy.Build(
                _concealedTileCount,
                _knownTiles);
            for (int i = 0; i < _handTiles.Count; i++)
            {
                var tile = _handTiles[i];
                tile.transform.DOKill();
                float x = GameTableLayoutPolicy.GetCenteredHandX(
                    _handTiles.Count,
                    i,
                    tileGap,
                    false,
                    drawGap);
                tile.transform.localPosition = new Vector3(x, 0, 0);
                tile.transform.localRotation = Quaternion.Euler(
                    GetTileRotation(display.GetVisualKindAt(i)));
            }
        }

        private Vector3 GetKnownFaceRotation() =>
            GetTileRotation(OpponentConcealedTileVisualKind.KnownFace);

        private Vector3 GetTileRotation(OpponentConcealedTileVisualKind kind)
        {
            Vector3 rotation = opponentHandRotation;
            rotation.y = OpponentKnownTileVisualPolicy.GetLocalYaw(rotation.y, kind);
            return rotation;
        }

        // --- 副露表现 ---
        public void ExecuteMeld(MeldType type, List<TileData> meldTiles)
        {
            // 1. 从手牌扣除对应的牌数
            int tilesToRemove = type == MeldType.Kan_Concealed ? 4 : (type == MeldType.Kan_Added ? 1 : (type == MeldType.Kan_Exposed ? 3 : 2));
            _concealedTileCount = Mathf.Max(0, _concealedTileCount - tilesToRemove);
            RebuildConcealedTiles(true, null);

            // 2. 加杠升级已有碰；其余动作新增副露，然后从本地状态重建视觉。
            if (!_meldState.TryApply(type, meldTiles))
            {
                Debug.LogWarning($"[OpponentView] 无法应用副露 {type}：未找到匹配的已有碰或牌数据为空");
                return;
            }

            RebuildMeldVisuals();
        }

        private void RebuildMeldVisuals()
        {
            if (meldSpawnPoint != null)
            {
                foreach (Transform child in meldSpawnPoint)
                {
                    child.DOKill();
                    Destroy(child.gameObject);
                }
            }

            _currentMeldOffset = 0f;
            foreach (var meld in _meldState.Melds)
            {
                CreateMeldVisual(meld.Type, meld.Tiles);
            }
        }
    }
}
