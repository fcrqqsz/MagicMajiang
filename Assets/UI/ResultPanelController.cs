using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Systems;

namespace MahjongGame.UI
{
    public class ResultPanelController : MonoBehaviour
    {
        public static ResultPanelController Instance;

        [SerializeField] private UIDocument _document;
        private VisualElement _overlay;
        private Label _titleLabel;
        private ScrollView _listContainer;
        private Label _totalLabel;
        private Button _btnRestart;
        private WinningHandStripView _winningHandView;

        // 多局对战状态
        private GameSession _session;
        private bool _isShowingFinalResult = false;

        void Awake()
        {
            Instance = this;
            var root = _document.rootVisualElement;

            _overlay = root.Q<VisualElement>("Overlay");
            _titleLabel = root.Q<Label>("TitleLabel");
            _listContainer = root.Q<ScrollView>("FanListContainer");
            _totalLabel = root.Q<Label>("TotalScoreLabel");
            _btnRestart = root.Q<Button>("BtnRestart");
            _winningHandView = new WinningHandStripView(
                root.Q<VisualElement>("WinningHandSection"),
                root.Q<VisualElement>("WinningHandRow"));

            _btnRestart.clicked += OnRestartClicked;

            // 初始隐藏
            _overlay.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 由 GameManager 在局结束时调用，传入当前对战状态
        /// </summary>
        public void SetSessionInfo(GameSession session)
        {
            _session = session;
            _isShowingFinalResult = false;
            UpdateButtonText();
        }

        /// <summary>Stops stale result animation before a recovered projection chooses the visible result.</summary>
        public void ResetForRecovery()
        {
            StopAllCoroutines();
            CancelInvoke();
            _isShowingFinalResult = false;
            _winningHandView?.Hide();
            if (_overlay == null) return;
            _overlay.RemoveFromClassList("overlay--visible");
            _overlay.style.display = DisplayStyle.None;
        }

        public void ApplyRecoveryResult(RoomGameSnapshot snapshot)
        {
            ResetForRecovery();
            var result = snapshot?.result;
            if (result == null) return;

            SetSessionInfo(GameManager.Instance?.Session);
            if (result.isSessionOver)
            {
                if (_session != null) ShowSessionResult();
                return;
            }
            if (result.isDrawGame)
            {
                ShowDraw(new List<string> { "流局" });
                return;
            }
            if (result.winnerId < 0) return;

            var details = result.fanDetails?.ToList() ?? new List<string>();
            if (IsLocalSeat(result.winnerId)) ShowWin(result.fanCount, details, result.isSelfDraw, result.winningHand);
            else ShowLose(result.winnerId, result.fanCount, details, result.winningHand);
        }

        private void UpdateButtonText()
        {
            if (_session == null)
            {
                _btnRestart.text = "返回主菜单";
            }
            else if (_session.Mode == GameMode.Single || _session.IsSessionOver())
            {
                _btnRestart.text = "查看总结算";
            }
            else
            {
                _btnRestart.text = "下一局";
            }
        }

        public void ShowDraw(List<string> playerStatuses = null)
        {
            _winningHandView?.Hide();
            _titleLabel.text = "流  局";
            _titleLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));

            _listContainer.Clear();

            if (playerStatuses != null && playerStatuses.Count > 0)
            {
                foreach (var status in playerStatuses)
                {
                    Label item = new Label(status);
                    item.AddToClassList("fan-item");
                    _listContainer.Add(item);
                }
            }
            else
            {
                Label info = new Label("牌山已空，无人胡牌");
                info.AddToClassList("fan-item");
                _listContainer.Add(info);
            }

            AppendScoreInfo();

            _totalLabel.text = "";

            _overlay.style.display = DisplayStyle.Flex;
            Invoke(nameof(FadeIn), 0.05f);
        }

        public void ShowLose(int aiId, int totalFan, List<string> fanDetails,
            WinningHandSnapshot winningHand = null)
        {
            _winningHandView?.Show(winningHand);
            _titleLabel.text = $"{GetPlayerDisplayName(aiId)} 胡牌";
            _titleLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.8f));

            _listContainer.Clear();

            foreach (var detail in fanDetails)
            {
                Label item = new Label(detail);
                item.AddToClassList("fan-item");
                _listContainer.Add(item);
            }

            _totalLabel.text = $"被扣除：{totalFan} 番";

            AppendScoreInfo();

            _overlay.style.display = DisplayStyle.Flex;
            Invoke(nameof(FadeIn), 0.05f);
        }

        public void ShowWin(int totalFan, List<string> fanDetails, bool isTsumo,
            WinningHandSnapshot winningHand = null)
        {
            _winningHandView?.Show(winningHand);
            _titleLabel.text = isTsumo ? "自  摸" : "荣  胡";
            _titleLabel.style.color = new StyleColor(new Color(1f, 0.26f, 0.26f));

            _listContainer.Clear();
            _totalLabel.text = "合计：0 番";

            _overlay.style.display = DisplayStyle.Flex;

            StartCoroutine(RollScoreRoutine(fanDetails));

            Invoke(nameof(FadeIn), 0.05f);
        }

        /// <summary>
        /// 在列表底部追加当前分数信息 (多局模式)
        /// </summary>
        private void AppendScoreInfo()
        {
            if (_session == null || _session.Mode == GameMode.Single) return;

            // 分隔线
            Label separator = new Label("────────────");
            separator.AddToClassList("fan-item");
            separator.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            _listContainer.Add(separator);

            // 当前分数
            string[] windNames = { "东", "南", "西", "北" };
            for (int i = 0; i < 4; i++)
            {
                string playerName = GetPlayerDisplayName(i);
                string scoreText = $"{playerName}: {_session.Scores[i]:+#;-#;0} 分";
                Label scoreLabel = new Label(scoreText);
                scoreLabel.AddToClassList("fan-item");
                if (IsLocalSeat(i))
                    scoreLabel.style.color = new StyleColor(new Color(1f, 0.85f, 0.4f));
                _listContainer.Add(scoreLabel);
            }
        }

        /// <summary>
        /// 显示最终总结算面板
        /// </summary>
        private void ShowSessionResult()
        {
            _winningHandView?.Hide();
            _titleLabel.text = "对战结束";
            _titleLabel.style.color = new StyleColor(new Color(1f, 0.85f, 0.4f));

            _listContainer.Clear();

            // 排名
            var rankings = new List<(int id, int score)>();
            for (int i = 0; i < 4; i++)
                rankings.Add((i, _session.Scores[i]));
            rankings.Sort((a, b) => b.score.CompareTo(a.score));

            int rank = 1;
            foreach (var (id, score) in rankings)
            {
                string playerName = GetPlayerDisplayName(id);
                Label item = new Label($"第{rank}名  {playerName}    {score:+#;-#;0} 分");
                item.AddToClassList("fan-item");
                if (IsLocalSeat(id))
                    item.style.color = new StyleColor(new Color(1f, 0.85f, 0.4f));
                _listContainer.Add(item);
                rank++;
            }

            _totalLabel.text = "";
            _btnRestart.text = "返回主菜单";
            _isShowingFinalResult = true;

            _overlay.style.display = DisplayStyle.Flex;
            Invoke(nameof(FadeIn), 0.05f);
        }

        private static bool IsLocalSeat(int seatIndex)
        {
            var room = NetworkManager.Instance?.RoomService;
            return room != null && room.HasResultSeatSnapshot ? seatIndex == room.ResultSeatIndex : seatIndex == 0;
        }

        private static string GetPlayerDisplayName(int seatIndex)
        {
            if (IsLocalSeat(seatIndex)) return "你";

            var room = NetworkManager.Instance?.RoomService;
            var seats = room?.ResultSeats;
            if (seats != null && seatIndex >= 0 && seatIndex < seats.Length)
            {
                var seat = seats[seatIndex];
                if (seat != null && seat.isOccupied && !seat.isAi && !string.IsNullOrWhiteSpace(seat.displayName))
                    return seat.displayName;
            }

            return $"AI {seatIndex + 1}";
        }

        private struct FanItemData
        {
            public string FullText;
            public int Score;
        }

        private sealed class WinningHandStripView
        {
            private readonly VisualElement _section;
            private readonly VisualElement _row;
            private readonly List<VisualElement> _tileElements = new List<VisualElement>();
            private int _groupCount;
            private int _visibleTileCount;

            public WinningHandStripView(VisualElement section, VisualElement row)
            {
                _section = section;
                _row = row;
                _row?.RegisterCallback<GeometryChangedEvent>(_ => ApplySizing());
                Hide();
            }

            public void Show(WinningHandSnapshot hand)
            {
                if (_section == null || _row == null || hand?.winningTile == null || !hand.winningTile.isValid)
                {
                    Hide();
                    return;
                }

                _row.Clear();
                _tileElements.Clear();
                _groupCount = 0;
                _visibleTileCount = ResultHandLayoutPolicy.CountVisibleTiles(hand);

                var concealedTiles = (hand.concealedTiles ?? System.Array.Empty<SimpleTileData>())
                    .Where(tile => tile != null && tile.isValid)
                    .OrderBy(tile => tile.suit)
                    .ThenBy(tile => tile.value)
                    .ToList();
                AddGroup(concealedTiles, null, false);
                AddGroup(new List<SimpleTileData> { hand.winningTile }, null, true);

                foreach (var meld in hand.melds ?? System.Array.Empty<SnapshotMeld>())
                {
                    if (meld == null) continue;
                    var tiles = (meld.tiles ?? System.Array.Empty<SimpleTileData>())
                        .Where(tile => tile != null && tile.isValid)
                        .ToList();
                    AddGroup(tiles, (MeldType)meld.meldType, false);
                }

                if (_tileElements.Count == 0)
                {
                    Hide();
                    return;
                }

                _section.style.display = DisplayStyle.Flex;
                _row.schedule.Execute(ApplySizing);
            }

            public void Hide()
            {
                if (_row != null) _row.Clear();
                _tileElements.Clear();
                _groupCount = 0;
                _visibleTileCount = 0;
                if (_section != null) _section.style.display = DisplayStyle.None;
            }

            private void AddGroup(List<SimpleTileData> tiles, MeldType? meldType, bool isWinningTile)
            {
                if (tiles == null || tiles.Count == 0) return;

                var group = new VisualElement { pickingMode = PickingMode.Ignore };
                group.AddToClassList("winning-hand-group");
                if (_groupCount > 0) group.AddToClassList("winning-hand-group--separated");

                for (int index = 0; index < tiles.Count; index++)
                {
                    bool faceDown = meldType.HasValue
                        && ResultHandLayoutPolicy.ShouldUseTileBack(meldType.Value, index, tiles.Count);
                    group.Add(CreateTile(tiles[index], faceDown, isWinningTile));
                }

                _row.Add(group);
                _groupCount++;
            }

            private VisualElement CreateTile(SimpleTileData tile, bool faceDown, bool isWinningTile)
            {
                var image = new VisualElement { pickingMode = PickingMode.Ignore };
                image.AddToClassList("winning-hand-tile");
                if (isWinningTile) image.AddToClassList("winning-hand-tile--winning");

                string imagePath = faceDown
                    ? TileImageHelper.GetTileBackImagePath()
                    : TileImageHelper.GetTileImagePath((Suit)tile.suit, tile.value);
                Sprite tileSprite = Resources.Load<Sprite>(imagePath);
                if (tileSprite != null)
                {
                    image.style.backgroundImage = new StyleBackground(tileSprite);
                }
                else
                {
                    Debug.LogWarning($"[ResultPanel] Failed to load tile sprite at {imagePath}");
                }

                _tileElements.Add(image);
                return image;
            }

            private void ApplySizing()
            {
                if (_section == null || _row == null || _section.style.display == DisplayStyle.None
                    || _tileElements.Count == 0) return;

                float availableWidth = _row.resolvedStyle.width;
                if (float.IsNaN(availableWidth) || availableWidth <= 0f) return;

                float tileWidth = ResultHandLayoutPolicy.CalculateTileWidth(
                    availableWidth, _visibleTileCount, Mathf.Max(0, _groupCount - 1));
                float tileHeight = tileWidth * ResultHandLayoutPolicy.TileAspectRatio;
                foreach (var tile in _tileElements)
                {
                    tile.style.width = tileWidth;
                    tile.style.height = tileHeight;
                }
                _row.style.height = tileHeight;
            }
        }

        private IEnumerator RollScoreRoutine(List<string> fanDetails)
        {
            List<FanItemData> parsedDetails = new List<FanItemData>();

            foreach (var detail in fanDetails)
            {
                int score = 0;
                int startIdx = detail.LastIndexOf('(');
                int endIdx = detail.LastIndexOf(')');
                if (startIdx >= 0 && endIdx > startIdx)
                {
                    string numStr = detail.Substring(startIdx + 1, endIdx - startIdx - 1);
                    int.TryParse(numStr, out score);
                }

                parsedDetails.Add(new FanItemData { FullText = detail, Score = score });
            }

            parsedDetails.Sort((a, b) => b.Score.CompareTo(a.Score));

            int currentTotal = 0;

            foreach (var item in parsedDetails)
            {
                Label label = new Label(item.FullText);
                label.AddToClassList("fan-item");
                _listContainer.Add(label);

                int targetTotal = currentTotal + item.Score;
                float rollDuration = 0.2f;
                float elapsed = 0f;

                while (elapsed < rollDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / rollDuration;
                    int tempTotal = Mathf.RoundToInt(Mathf.Lerp(currentTotal, targetTotal, t));
                    _totalLabel.text = $"合计：{tempTotal} 番";
                    yield return null;
                }

                currentTotal = targetTotal;
                _totalLabel.text = $"合计：{currentTotal} 番";

                yield return new WaitForSeconds(0.5f);
            }

            // 番种滚动完成后追加分数信息
            AppendScoreInfo();
        }

        private void FadeIn()
        {
            _overlay.AddToClassList("overlay--visible");
        }

        private void OnRestartClicked()
        {
            // 关键：先隐藏面板，避免在可见状态下修改内容触发字体图集异常
            _overlay.RemoveFromClassList("overlay--visible");
            _overlay.style.display = DisplayStyle.None;
            _winningHandView?.Hide();
            StopAllCoroutines();
            CancelInvoke();

            if (_isShowingFinalResult)
            {
                // 已在总结算界面 → 返回主菜单
                ReturnToLobby();
            }
            else if (_session != null && !_session.IsSessionOver() && _session.Mode != GameMode.Single)
            {
                // 多局模式，还有下一局
                GameManager.Instance.StartNextRound();
            }
            else
            {
                // 单局模式 或 多局对战已结束 → 显示总结算
                ShowSessionResult();
            }
        }

        private async void ReturnToLobby()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.RoomService?.LeaveRoom();
                await NetworkManager.Instance.LoadSceneAndUnloadCurrentAsync(SceneNames.MainLobby, SceneNames.Game);
            }
            else
            {
                // Fallback: 直接从 Game 场景启动时没有 NetworkManager
                SceneManager.LoadScene(SceneNames.Game);
            }
        }
    }
}
