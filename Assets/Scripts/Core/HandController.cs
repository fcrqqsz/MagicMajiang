using System.Collections.Generic;
using UnityEngine;
using MahjongGame.Systems; 
using DG.Tweening; 
using System.Linq; 

namespace MahjongGame.Core
{
    public class HandController : MahjongHandViewBase
    {
        public event System.Action<TileData> OnTileDiscardedEvent;
        
        // 当前选中的牌 (指针)
        private TileVisual _selectedTile = null;

        [Header("Visual Settings")]
        public Vector3 handRotation = new Vector3(25f, 0f, 0f); // 让牌后仰 25 度

        // [重要] 标记最后摸到的那张牌
        private TileVisual _lastDrawnTile = null;

        public TileData LastDrawnData => _lastDrawnTile?.Data;

        public List<Meld> Melds { get; private set; } = new List<Meld>();

        // 当前风位 (由 LocalPlayerClient.OnRoundStart 设置)
        public WindDirection RoundWind { get; set; } = WindDirection.East;
        public WindDirection SeatWind { get; set; } = WindDirection.East;
        // 天赋加成 (由 LocalPlayerClient.OnTalentInfo 设置)
        public ScoringOptions ScoringOptions { get; set; }

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
            CreateMeldVisual(meld.Type, meld.Tiles);
        }
        
        /// <summary>
        /// 执行 "碰" 操作
        /// </summary>
        public void ExecutePon(TileData targetTile)
        {
            var matchingTiles = _handTiles
                .Where(t => t.Data.TileSuit == targetTile.TileSuit && t.Data.Value == targetTile.Value)
                .Take(2)
                .ToList();

            if (matchingTiles.Count < 2)
            {
                Debug.LogError("严重错误：试图碰牌，但手牌里没有两张同牌！");
                return;
            }

            List<TileData> meldDataList = new List<TileData> { targetTile, matchingTiles[0].Data, matchingTiles[1].Data };
            Meld newMeld = new Meld(MeldType.Pon, meldDataList, targetTile.OriginalOwnerID);
            AddMeld(newMeld);

            foreach (var visual in matchingTiles)
            {
                _handTiles.Remove(visual);
                visual.transform.DOKill();
                Destroy(visual.gameObject); 
            }
            _lastDrawnTile = null;

            SortHand();
            UpdateHandPositions();
        }

        /// <summary>
        /// 执行 "明杠" 操作
        /// </summary>
        public void ExecuteMingGan(TileData targetTile)
        {
            var matchingTiles = _handTiles
                .Where(t => t.Data.TileSuit == targetTile.TileSuit && t.Data.Value == targetTile.Value)
                .Take(3) 
                .ToList();

            if (matchingTiles.Count < 3)
            {
                Debug.LogError("错误：试图明杠，但手牌不足3张同牌！");
                return;
            }

            foreach (var visual in matchingTiles)
            {
                _handTiles.Remove(visual);
                visual.transform.DOKill();
                Destroy(visual.gameObject);
            }

            List<TileData> meldData = new List<TileData> 
            { 
                targetTile, 
                matchingTiles[0].Data, 
                matchingTiles[1].Data, 
                matchingTiles[2].Data 
            };
            
            Meld newMeld = new Meld(MeldType.Kan_Exposed, meldData, targetTile.OriginalOwnerID);
            Melds.Add(newMeld);

            CreateMeldVisual(newMeld.Type, newMeld.Tiles);

            SortHand();
            UpdateHandPositions();
        }

        /// <summary>
        /// 获取可以暗杠的牌 (返回牌的 Value 列表，区分花色)
        /// </summary>
        public List<TileData> GetAnGanOptions()
        {
            return GetSelfTurnKongOptions().AnGangTargets.ToList();
        }

        /// <summary>
        /// 执行暗杠
        /// </summary>
        public void ExecuteAnGan(TileData targetData)
        {
            var matchingTiles = _handTiles
                .Where(t => t.Data.TileSuit == targetData.TileSuit && t.Data.Value == targetData.Value)
                .ToList();

            if (matchingTiles.Count < 4) return;

            var tilesToRemove = matchingTiles.Take(4).ToList(); 

            List<TileData> meldData = new List<TileData>();
            foreach (var visual in tilesToRemove)
            {
                meldData.Add(visual.Data);
                
                _handTiles.Remove(visual);
                visual.transform.DOKill();
                Destroy(visual.gameObject);
            }

            Meld newMeld = new Meld(MeldType.Kan_Concealed, meldData, targetData.OriginalOwnerID, true);
            Melds.Add(newMeld);

            CreateMeldVisual(newMeld.Type, newMeld.Tiles);
            
            _lastDrawnTile = null; 
            
            SortHand();
            UpdateHandPositions();
        }

        /// <summary>
        /// 获取可以加杠的牌 (返回牌的 Value 列表)
        /// </summary>
        public List<TileData> GetJiaGangOptions()
        {
            return GetSelfTurnKongOptions().JiaGangTargets.ToList();
        }

        public SelfTurnKongOptions GetSelfTurnKongOptions()
        {
            return SelfTurnKongResolver.Resolve(_handTiles.Select(tile => tile.Data), Melds);
        }

        /// <summary>
        /// 执行加杠
        /// </summary>
        public void ExecuteJiaGang(TileData targetData)
        {
            var tileToRemove = _handTiles.FirstOrDefault(t => t.Data.TileSuit == targetData.TileSuit && t.Data.Value == targetData.Value);
            if (tileToRemove == null) return;

            _handTiles.Remove(tileToRemove);
            tileToRemove.transform.DOKill();
            Destroy(tileToRemove.gameObject);

            var targetMeld = Melds.FirstOrDefault(m => m.Type == MeldType.Pon && m.FirstTile.TileSuit == targetData.TileSuit && m.FirstTile.Value == targetData.Value);
            if (targetMeld != null)
            {
                targetMeld.Type = MeldType.Kan_Added;
                targetMeld.Tiles.Add(targetData); 
                
                RefreshAllMeldsVisual();
            }

            _lastDrawnTile = null; 
            SortHand();
            UpdateHandPositions();
        }

        private void RefreshAllMeldsVisual()
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
            foreach (var meld in Melds)
            {
                CreateMeldVisual(meld.Type, meld.Tiles);
            }
        }

        public void DrawCardData(TileData data)
        {
            AddTileDirectly(data);
        }

        public void ExecuteChi(TileData targetTile, int[] eatingValues)
        {
            if (eatingValues == null || eatingValues.Length != 2) return;

            List<TileData> meldData = new List<TileData>();
            meldData.Add(targetTile); 

            foreach (int val in eatingValues)
            {
                var visual = _handTiles.FirstOrDefault(t => 
                    t.Data.TileSuit == targetTile.TileSuit && 
                    t.Data.Value == val);

                if (visual != null)
                {
                    meldData.Add(visual.Data); 
                    _handTiles.Remove(visual); 
                    visual.transform.DOKill();
                    Destroy(visual.gameObject); 
                }
            }
            _lastDrawnTile = null;

            meldData.Sort((a, b) => a.Value.CompareTo(b.Value));

            Meld newMeld = new Meld(MeldType.Chi, meldData, targetTile.OriginalOwnerID);
            Melds.Add(newMeld);

            CreateMeldVisual(newMeld.Type, newMeld.Tiles);
            SortHand();
            UpdateHandPositions();
        }

        public void DrawCard()
        {
            TileData newData = DeckManager.Instance.DrawTile();
            if (newData == null) 
            {
                Debug.Log("无牌可摸！");
                return;
            }

            AddTileDirectly(newData);
        }

        public void AddTileDirectly(TileData data)
        {
            AddTileDirectly(data, true);
        }

        private void AddTileDirectly(TileData data, bool animate)
        {
            if (data == null) return;

            GameObject go = Instantiate(tilePrefab);
            TileVisual visual = go.GetComponent<TileVisual>();
            Sprite face = GetTileSprite(data);
            visual.Initialize(data, this, face);
            AddTileToHand(visual, animate);
        }

        private void AddTileToHand(TileVisual visual, bool animate = true)
        {
            visual.transform.SetParent(this.transform);
            _handTiles.Add(visual);
            _lastDrawnTile = visual; 
            
            if (animate) UpdateHandPositions();
        }

        public override void OnTileClicked(TileVisual clickedTile)
        {
            if (!_isInteractable) return; 
            
            if (_selectedTile == clickedTile)
            {
                DiscardTile(clickedTile);
            }
            else
            {
                SelectTile(clickedTile);
            }
        }

        private void SelectTile(TileVisual tile)
        {
            if (_selectedTile != null)
            {
                _selectedTile.transform.localPosition = new Vector3(_selectedTile.transform.localPosition.x, 0, 0);
            }

            _selectedTile = tile;

            Vector3 pos = tile.transform.localPosition;
            tile.transform.localPosition = new Vector3(pos.x, 0.5f, pos.z);
            
            if (UI.WaitHintController.Instance != null)
            {
                int tileIndex = MahjongLogic.GetTileIndex(tile.Data);
                
                if (_cachedWaitHints.TryGetValue(tileIndex, out var waitDetails))
                {
                    UI.WaitHintController.Instance.ShowHint(waitDetails);
                }
                else
                {
                    UI.WaitHintController.Instance.HideHint();
                }
            }

            Debug.Log($"选中了: {tile.Data}");
        }

        private void DiscardTile(TileVisual tile)
        {
            if (UI.WaitHintController.Instance != null)
            {
                UI.WaitHintController.Instance.HideHint();
            }

            _handTiles.Remove(tile);
            _selectedTile = null;
            _lastDrawnTile = null;

            if (myRiver != null) {
                myRiver.AddTileToRiver(tile);
            } else {
                tile.transform.DOKill();
                Destroy(tile.gameObject); 
            }

            SortHand();
            UpdateHandPositions();

            OnTileDiscardedEvent?.Invoke(tile.Data);
        }

        /// <summary>
        /// 强制移除一张牌（超时自动出牌用），不触发 OnTileDiscardedEvent
        /// </summary>
        public void ForceRemoveTile(TileData tileData)
        {
            var tile = _handTiles.FirstOrDefault(
                t => t.Data.TileSuit == tileData.TileSuit && t.Data.Value == tileData.Value);
            if (tile == null) return;

            _handTiles.Remove(tile);
            _selectedTile = null;
            _lastDrawnTile = null;

            if (myRiver != null)
                myRiver.AddTileToRiver(tile);
            else
            {
                tile.transform.DOKill();
                Destroy(tile.gameObject);
            }

            SortHand();
            UpdateHandPositions();
        }

        public void SortHand()
        {
            _handTiles.Sort((a, b) =>
            {
                if (a.Data.TileSuit != b.Data.TileSuit) return a.Data.TileSuit.CompareTo(b.Data.TileSuit);
                return a.Data.Value.CompareTo(b.Data.Value);
            });

            for(int i=0; i<_handTiles.Count; i++) _handTiles[i].transform.SetSiblingIndex(i);
            _lastDrawnTile = null; // 理牌后清除摸牌标记，避免最后一张多出间隔
            UpdateHandPositions();
        }

        private void SortHandImmediately()
        {
            _handTiles.Sort((a, b) =>
            {
                if (a.Data.TileSuit != b.Data.TileSuit) return a.Data.TileSuit.CompareTo(b.Data.TileSuit);
                return a.Data.Value.CompareTo(b.Data.Value);
            });

            for (int i = 0; i < _handTiles.Count; i++)
                _handTiles[i].transform.SetSiblingIndex(i);
            _lastDrawnTile = null;
        }

        protected override void UpdateHandPositions()
        {
            for (int i = 0; i < _handTiles.Count; i++)
            {
                float targetX = i * tileGap;

                if (i == _handTiles.Count - 1 && _handTiles[i] == _lastDrawnTile)
                {
                    targetX += drawGap;
                }

                Vector3 targetPos = new Vector3(targetX, 0, 0);
                Quaternion targetRot = Quaternion.Euler(handRotation);

                _handTiles[i].transform.DOKill(); 
                _handTiles[i].transform.DOLocalMove(targetPos, 0.3f);
                _handTiles[i].transform.DOLocalRotate(handRotation, 0.3f);
            }
        }

        public override void ClearHand()
        {
            base.ClearHand();
            _selectedTile = null;
            Melds.Clear();
        }

        /// <summary>Atomically replaces the local table view with the server's privacy-filtered snapshot.</summary>
        public void RebuildFromSnapshot(IEnumerable<TileData> concealedHand, IEnumerable<Meld> melds, IEnumerable<TileData> river)
        {
            ClearHand();
            foreach (var tile in concealedHand ?? Enumerable.Empty<TileData>())
                AddTileDirectly(tile, false);
            foreach (var meld in melds ?? Enumerable.Empty<Meld>())
            {
                if (meld?.Tiles == null || meld.Tiles.Count == 0) continue;
                Melds.Add(meld);
                CreateMeldVisual(meld.Type, meld.Tiles);
            }

            SortHandImmediately();
            _selectedTile = null;
            UpdateHandPositionsImmediately();
            RebuildRiver(river);
        }

        private void UpdateHandPositionsImmediately()
        {
            for (int i = 0; i < _handTiles.Count; i++)
            {
                var tile = _handTiles[i];
                tile.transform.DOKill();
                tile.transform.localPosition = new Vector3(i * tileGap, 0, 0);
                tile.transform.localRotation = Quaternion.Euler(handRotation);
            }
        }

        private bool _isInteractable = false;
        private Dictionary<int, List<MahjongLogic.WaitDetail>> _cachedWaitHints = new Dictionary<int, List<MahjongLogic.WaitDetail>>();

        public void SetInteractable(bool canInteract)
        {
            _isInteractable = canInteract;
            if (canInteract)
            {
                _cachedWaitHints = MahjongLogic.GetWaitHints(GetHandData(), Melds, RoundWind, SeatWind, ScoringOptions);
            }
            else
            {
                _cachedWaitHints.Clear();
                if (UI.WaitHintController.Instance != null)
                {
                    UI.WaitHintController.Instance.HideHint();
                }
            }
        }
    }
}
