using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace MahjongGame.Core
{
    public class OpponentViewController : MonoBehaviour
    {
        [Header("Settings")]
        public GameObject tilePrefab; // 统一使用一个麻将预制体，通过旋转显示背面

        [Header("Hand Settings")]
        public Transform handSpawnPoint;
        public float tileGap = 0.8f;
        public float drawGap = 1.0f;
        public Vector3 opponentHandRotation = new Vector3(0f, 0f, 0f); // 假设立起且背对牌桌中心(Y轴转180度)
        
        [Header("Meld Settings")]
        public Transform meldSpawnPoint;
        public float meldGap = 0.2f;
        public float meldTileWidth = 0.8f;

        [Header("Refs")]
        public RiverController myRiver;

        private List<GameObject> _handTiles = new List<GameObject>();
        private bool _justDrawn = false;
        private float _currentMeldOffset = 0f;

        public void InitHand(int count)
        {
            ClearHand();
            for (int i = 0; i < count; i++) AddTileBack();
            UpdateHandPositions();
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
                GameObject tileToRemove = _handTiles[_handTiles.Count - 1];
                _handTiles.RemoveAt(_handTiles.Count - 1);
                tileToRemove.transform.DOKill();
                Destroy(tileToRemove);
            }
            
            _justDrawn = false;
            UpdateHandPositions();

            // 生成一张明牌打入该玩家的牌河
            if (myRiver != null && tilePrefab != null)
            {
                GameObject go = Instantiate(tilePrefab);
                TileVisual visual = go.GetComponent<TileVisual>();
                var config = MahjongGame.Systems.DeckManager.Instance.tileConfig;
                Sprite face = config != null ? config.GetSprite(discardedData) : null;
                if (visual != null) visual.Initialize(discardedData, null, face);
                
                myRiver.AddTileToRiver(visual);
            }
        }

        private void AddTileBack()
        {
            if (tilePrefab == null) return;
            GameObject go = Instantiate(tilePrefab, handSpawnPoint);
            // 盖牌不需要初始化正面花色，直接设置旋转即可
            go.transform.localRotation = Quaternion.Euler(opponentHandRotation);
            _handTiles.Add(go);
        }

        private void UpdateHandPositions()
        {
            for (int i = 0; i < _handTiles.Count; i++)
            {
                float targetX = i * tileGap;
                if (_justDrawn && i == _handTiles.Count - 1) targetX += drawGap;
                _handTiles[i].transform.DOKill(); // 在播放新动画前杀掉旧动画，防止冲突
                _handTiles[i].transform.DOLocalMove(new Vector3(targetX, 0, 0), 0.3f).SetLink(_handTiles[i]);
                _handTiles[i].transform.DOLocalRotate(opponentHandRotation, 0.3f).SetLink(_handTiles[i]);
            }
        }

        public void ClearHand()
        {
            foreach (var t in _handTiles)
            {
                t.transform.DOKill();
                Destroy(t);
            }
            _handTiles.Clear();
            _justDrawn = false;
            if (meldSpawnPoint != null)
            {
                foreach (Transform child in meldSpawnPoint) Destroy(child.gameObject);
            }
            _currentMeldOffset = 0f;
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
                    GameObject t = _handTiles[_handTiles.Count - 1];
                    _handTiles.RemoveAt(_handTiles.Count - 1);
                    t.transform.DOKill();
                    Destroy(t);
                }
            }
            UpdateHandPositions();

            if (meldSpawnPoint == null || tilePrefab == null) return;

            // 2. 生成副露模型
            int visualCount = type == MeldType.Kan_Added ? 3 : meldTiles.Count;
            float startX = _currentMeldOffset - (visualCount * meldTileWidth);

            for (int i = 0; i < meldTiles.Count; i++)
            {
                TileData data = meldTiles[i];
                GameObject go = Instantiate(tilePrefab, meldSpawnPoint);
                TileVisual visual = go.GetComponent<TileVisual>();
                
                Quaternion rotation;

                // 判断是否是扣着的牌（暗杠的第1和第4张）
                bool isConcealed = (type == MeldType.Kan_Concealed && (i == 0 || i == 3));

                if (!isConcealed)
                {
                    var config = MahjongGame.Systems.DeckManager.Instance.tileConfig;
                    Sprite face = config != null ? config.GetSprite(data) : null;
                    if (visual != null) visual.Initialize(data, null, face);

                    if (type == MeldType.Pon && i == 0)
                    {
                        rotation = Quaternion.Euler(90, -90, 0); 
                    }
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
                    // 扣着的牌，不需要初始化花色
                    rotation = Quaternion.Euler(-90, 0, 0);
                }

                go.transform.localRotation = rotation;

                // --- 位置计算 ---
                float xPos = startX + (i * meldTileWidth);
                float yPos = 0f;

                if (type == MeldType.Kan_Added && i == 3)
                {
                    xPos = startX; // 加杠叠在第一张上
                    yPos = 0.5f;
                }
                
                if ((type == MeldType.Pon || type == MeldType.Kan_Added) && i == 0)
                {
                    xPos -= 0.15f; // 横置修正
                }

                go.transform.localPosition = new Vector3(xPos, yPos, 0);
            }
            _currentMeldOffset = startX - meldGap;
        }
    }
}