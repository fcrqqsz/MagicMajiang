using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Systems;

namespace MahjongGame.UI
{
    public class ResultPanelController : MonoBehaviour, ILocalResultPresentation
    {
        public static ResultPanelController Instance;

        [SerializeField] private UIDocument _document;
        private VisualElement _documentRoot;
        private VisualElement _overlay;
        private Label _titleLabel;
        private Label _finalFanHero;
        private VisualElement _fanBreakdown;
        private Label _baseFanRow;
        private VisualElement _talentContributionList;
        private ScrollView _listContainer;
        private Button _btnRestart;
        private Button _battleMenuButton;
        private WinningHandStripView _winningHandView;
        private readonly TalentFanPresentationState _talentFanPresentation =
            new TalentFanPresentationState();
        public TalentFanBreakdownMessage TalentFanBreakdown => _talentFanPresentation.Current;

        // 多局对战状态
        private GameSession _session;
        private bool _isShowingFinalResult = false;
        private bool _returnToLobbyInFlight;

        void Awake()
        {
            Instance = this;
            var root = _document.rootVisualElement;
            _documentRoot = root;

            _overlay = root.Q<VisualElement>("Overlay");
            _titleLabel = root.Q<Label>("TitleLabel");
            _finalFanHero = root.Q<Label>("FinalFanHero");
            _fanBreakdown = root.Q<VisualElement>("FanBreakdown");
            _baseFanRow = root.Q<Label>("BaseFanRow");
            _talentContributionList = root.Q<VisualElement>("TalentContributionList");
            _listContainer = root.Q<ScrollView>("FanListContainer");
            _btnRestart = root.Q<Button>("BtnRestart");
            _battleMenuButton = root.Q<Button>("BattleMenuButton");
            if (_battleMenuButton != null) _battleMenuButton.clicked += OpenBattleMenu;
            _winningHandView = new WinningHandStripView(
                root.Q<VisualElement>("WinningHandSection"),
                root.Q<VisualElement>("WinningHandRow"));

            _btnRestart.clicked += OnRestartClicked;

            // 初始隐藏
            SetDocumentVisibility(false);
        }

        private void OnDestroy()
        {
            if (_battleMenuButton != null) _battleMenuButton.clicked -= OpenBattleMenu;
            StopAllCoroutines();
            CancelInvoke();
            if (_btnRestart != null) _btnRestart.clicked -= OnRestartClicked;
            _winningHandView?.Dispose();
            if (Instance == this) Instance = null;
        }

        private static void OpenBattleMenu() => BattleMenuController.Instance?.OpenMenu();

        /// <summary>
        /// 由 GameManager 在局结束时调用，传入当前对战状态
        /// </summary>
        public void SetSessionInfo(GameSession session)
        {
            _session = session;
            _isShowingFinalResult = false;
            UpdateButtonText();
        }

        /// <summary>Applies the authoritative terminal state without replacing the visible round result.</summary>
        public void ApplySessionEnd(GameSession session)
        {
            _session = session;
            UpdateButtonText();
            if (!_isShowingFinalResult && _overlay != null
                && _overlay.style.display != DisplayStyle.None)
                AppendScoreInfo();
        }

        /// <summary>Stops stale result animation before a recovered projection chooses the visible result.</summary>
        public void ResetForRecovery()
        {
            StopAllCoroutines();
            CancelInvoke();
            _isShowingFinalResult = false;
            _winningHandView?.Hide();
            HideTalentResult();
            if (_overlay == null) return;
            _overlay.RemoveFromClassList("overlay--visible");
            SetDocumentVisibility(false);
        }

        public void ApplyRecoveryResult(RoomGameSnapshot snapshot, GameSession session)
        {
            ResetForRecovery();
            new LocalResultPresentationBridge(this).ShowRecovery(snapshot);
            var result = snapshot?.result;
            if (result == null) return;

            SetSessionInfo(session);
            if (result.isSessionOver)
            {
                if (_session != null) ShowSessionResult(isRecovery: true);
                return;
            }
            if (result.isDrawGame)
            {
                RenderDraw(new List<string> { "流局" }, isRecovery: true);
                return;
            }
            if (result.winnerId < 0) return;

            var details = result.fanDetails?.ToList() ?? new List<string>();
            RenderRoundResult(
                result.winnerId,
                IsLocalSeat(result.winnerId),
                result.fanCount,
                details,
                result.isSelfDraw,
                result.winningHand,
                TalentFanBreakdown,
                isRecovery: true);
        }

        private void UpdateButtonText()
        {
            if (_btnRestart != null)
                _btnRestart.text = ResultSessionPresentationPolicy.GetContinueButtonText(_session);
        }

        public void ShowDraw(List<string> playerStatuses = null)
        {
            RenderDraw(playerStatuses, isRecovery: false);
        }

        private void RenderDraw(List<string> playerStatuses, bool isRecovery)
        {
            if (!isRecovery) _talentFanPresentation.ApplyLive(null);
            _winningHandView?.Hide();
            HideTalentResult();
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

            ShowOverlay(isRecovery);
        }

        public void ShowLose(int aiId, int totalFan, List<string> fanDetails,
            WinningHandSnapshot winningHand = null,
            TalentFanBreakdownMessage talentFanBreakdown = null)
        {
            RenderRoundResult(
                aiId,
                isLocalWinner: false,
                totalFan,
                fanDetails,
                isTsumo: false,
                winningHand,
                talentFanBreakdown,
                isRecovery: false);
        }

        public void ShowWin(int totalFan, List<string> fanDetails, bool isTsumo,
            WinningHandSnapshot winningHand = null,
            TalentFanBreakdownMessage talentFanBreakdown = null)
        {
            RenderRoundResult(
                winnerId: -1,
                isLocalWinner: true,
                totalFan,
                fanDetails,
                isTsumo,
                winningHand,
                talentFanBreakdown,
                isRecovery: false);
        }

        public void ReceiveRecoveryTalentFanBreakdown(
            TalentFanBreakdownMessage talentFanBreakdown)
        {
            _talentFanPresentation.ApplyRecovery(talentFanBreakdown);
        }

        /// <summary>
        /// 在列表底部追加当前权威分数信息
        /// </summary>
        private void AppendScoreInfo()
        {
            if (_session == null || _listContainer == null) return;

            foreach (VisualElement item in _listContainer.contentContainer.Children()
                         .Where(child => child.ClassListContains("session-score-item"))
                         .ToArray())
                item.RemoveFromHierarchy();

            // 分隔线
            Label separator = new Label("────────────");
            separator.AddToClassList("fan-item");
            separator.AddToClassList("session-score-item");
            separator.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            _listContainer.Add(separator);

            // 当前分数
            for (int i = 0; i < 4; i++)
            {
                string playerName = GetPlayerDisplayName(i);
                string scoreText = $"{playerName}: {_session.Scores[i]:+#;-#;0} 分";
                Label scoreLabel = new Label(scoreText);
                scoreLabel.AddToClassList("fan-item");
                scoreLabel.AddToClassList("session-score-item");
                if (IsLocalSeat(i))
                    scoreLabel.style.color = new StyleColor(new Color(1f, 0.85f, 0.4f));
                _listContainer.Add(scoreLabel);
            }
        }

        /// <summary>
        /// 显示最终总结算面板
        /// </summary>
        private void ShowSessionResult(bool isRecovery = false)
        {
            _talentFanPresentation.ApplyLive(null);
            _winningHandView?.Hide();
            HideTalentResult();
            _titleLabel.text = "对战结束";
            _titleLabel.style.color = new StyleColor(new Color(1f, 0.85f, 0.4f));

            _listContainer.Clear();

            string endReason = ResultSessionPresentationPolicy.GetEndReasonText(
                _session, GetPlayerDisplayName);
            if (!string.IsNullOrEmpty(endReason))
            {
                Label reason = new Label(endReason);
                reason.AddToClassList("fan-item");
                _listContainer.Add(reason);
            }

            int rank = 1;
            foreach (int id in ResultSessionPresentationPolicy.GetRankedSeatIndices(_session))
            {
                string playerName = GetPlayerDisplayName(id);
                string depletedMarker = ResultSessionPresentationPolicy.IsDepletedSeat(_session, id)
                    ? "  [击飞]"
                    : string.Empty;
                Label item = new Label(
                    $"第{rank}名  {playerName}    {_session.Scores[id]:+#;-#;0} 分{depletedMarker}");
                item.AddToClassList("fan-item");
                if (IsLocalSeat(id))
                    item.style.color = new StyleColor(new Color(1f, 0.85f, 0.4f));
                _listContainer.Add(item);
                rank++;
            }

            _btnRestart.text = "返回主菜单";
            _isShowingFinalResult = true;

            ShowOverlay(isRecovery);
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

        private sealed class WinningHandStripView
        {
            private readonly VisualElement _section;
            private readonly VisualElement _row;
            private readonly List<VisualElement> _tileElements = new List<VisualElement>();
            private readonly EventCallback<GeometryChangedEvent> _geometryChangedCallback;
            private int _groupCount;
            private int _visibleTileCount;

            public WinningHandStripView(VisualElement section, VisualElement row)
            {
                _section = section;
                _row = row;
                _geometryChangedCallback = _ => ApplySizing();
                _row?.RegisterCallback(_geometryChangedCallback);
                Hide();
            }

            public void Dispose()
            {
                _row?.UnregisterCallback(_geometryChangedCallback);
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

        private void RenderRoundResult(
            int winnerId,
            bool isLocalWinner,
            int acceptedFinalFan,
            IEnumerable<string> fanDetails,
            bool isTsumo,
            WinningHandSnapshot winningHand,
            TalentFanBreakdownMessage talentFanBreakdown,
            bool isRecovery)
        {
            if (!isRecovery) _talentFanPresentation.ApplyLive(talentFanBreakdown);
            TalentResultView resultView = TalentResultPresentationPolicy.BuildAcceptedWin(
                acceptedFinalFan,
                talentFanBreakdown,
                MahjongGame.Talents.TalentRegistry.Instance);
            RenderTalentResult(resultView);

            _winningHandView?.Show(winningHand);
            _titleLabel.text = isLocalWinner
                ? (isTsumo ? "自  摸" : "荣  胡")
                : $"{GetPlayerDisplayName(winnerId)} 胡牌";
            _titleLabel.style.color = new StyleColor(isLocalWinner
                ? new Color(1f, 0.26f, 0.26f)
                : new Color(0.5f, 0.5f, 0.8f));

            _listContainer.Clear();
            foreach (string detail in fanDetails ?? Enumerable.Empty<string>())
            {
                Label label = new Label(detail ?? string.Empty);
                label.AddToClassList("fan-item");
                _listContainer.Add(label);
            }
            AppendScoreInfo();
            ShowOverlay(isRecovery);
        }

        private void RenderTalentResult(TalentResultView view)
        {
            HideTalentResult();
            if (view?.IsVisible != true) return;

            _finalFanHero.text = view.FinalFanText;
            _finalFanHero.style.display = DisplayStyle.Flex;
            if (view.Rows.Count == 0)
            {
                if (view.HasMismatchDiagnostic)
                    Debug.LogWarning("[ResultPanel] TalentFanBreakdownMismatch");
                return;
            }

            _fanBreakdown.style.display = DisplayStyle.Flex;
            _baseFanRow.text = view.Rows[0].Text;

            foreach (TalentResultRow row in view.Rows.Skip(1))
            {
                var label = new Label(row.Text);
                label.AddToClassList("breakdown-row");
                if (row.IsNegative) label.AddToClassList("breakdown-row--negative");
                _talentContributionList.Add(label);
                if (row.ShouldLogWarning)
                    Debug.LogWarning("[ResultPanel] UnknownTalentFanContribution");
            }

            if (view.HasMismatchDiagnostic)
                Debug.LogWarning("[ResultPanel] TalentFanBreakdownMismatch");
        }

        private void HideTalentResult()
        {
            _talentContributionList?.Clear();
            if (_finalFanHero != null) _finalFanHero.style.display = DisplayStyle.None;
            if (_fanBreakdown != null) _fanBreakdown.style.display = DisplayStyle.None;
        }

        private void ShowOverlay(bool isRecovery)
        {
            CancelInvoke(nameof(FadeIn));
            ResultOverlayPresentation presentation =
                ResultOverlayPresentationPolicy.Build(isRecovery);
            _overlay.RemoveFromClassList("overlay--visible");
            SetDocumentVisibility(true);
            if (presentation.ShowImmediately)
            {
                _overlay.style.opacity = 1f;
                _overlay.AddToClassList("overlay--visible");
                return;
            }

            _overlay.style.opacity = StyleKeyword.Null;
            if (presentation.Animate) Invoke(nameof(FadeIn), 0.05f);
        }

        private void FadeIn()
        {
            _overlay.AddToClassList("overlay--visible");
        }

        private void SetDocumentVisibility(bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_overlay != null) _overlay.style.display = display;
            if (_documentRoot != null) _documentRoot.style.display = display;
        }

        private void OnRestartClicked()
        {
            if (_returnToLobbyInFlight || BattleMenuInputGate.Instance.IsBlocked(Time.frameCount)) return;
            // 关键：先隐藏面板，避免在可见状态下修改内容触发字体图集异常
            _overlay.RemoveFromClassList("overlay--visible");
            SetDocumentVisibility(false);
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
            if (_returnToLobbyInFlight) return;
            _returnToLobbyInFlight = true;
            try
            {
                NetworkManager networkManager = NetworkManager.Instance;
                if (networkManager == null)
                {
                    SceneManager.LoadScene(SceneNames.Persistent, LoadSceneMode.Single);
                    return;
                }

                await networkManager.LeaveBattleToLobbyAsync();
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[ResultPanel] Return to lobby failed: " + exception.Message);
                if (this == null) return;
                // Room authority has already been cleared. Keep a retry entry without
                // depending on the menu's active-room admission check.
                _isShowingFinalResult = true;
                if (_btnRestart != null) _btnRestart.text = "重试返回大厅";
                SetDocumentVisibility(true);
                _overlay.style.opacity = 1f;
                _overlay.AddToClassList("overlay--visible");
            }
            finally { _returnToLobbyInFlight = false; }
        }
    }
}
