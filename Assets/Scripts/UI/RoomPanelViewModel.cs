using System;
using System.Linq;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.UI
{
    public enum RoomSeatVisualState
    {
        Empty,
        HumanOnline,
        HumanOffline,
        TemporaryAiControl,
        PermanentAi
    }

    public sealed class RoomSeatViewModel
    {
        public int SeatIndex { get; internal set; }
        public RoomSeatVisualState State { get; internal set; }
        public string DisplayName { get; internal set; }
        public string StatusText { get; internal set; }
        public string DifficultyText { get; internal set; }
        public string TemplateText { get; internal set; }
        public bool IsLocal { get; internal set; }
        public bool IsHost { get; internal set; }
        public bool IsReady { get; internal set; }
        public bool CanAddAi { get; internal set; }
        public bool CanEditAi { get; internal set; }
        public bool IsPermanentAi => State == RoomSeatVisualState.PermanentAi;
        public bool IsEmpty => State == RoomSeatVisualState.Empty;
    }

    /// <summary>Pure presentation projection for the lobby room panel.</summary>
    public sealed class RoomPanelViewModel
    {
        public string RoomId { get; private set; }
        public RoomState RoomState { get; private set; }
        public RoomSeatViewModel[] Seats { get; private set; } = Array.Empty<RoomSeatViewModel>();
        public int HumanCount { get; private set; }
        public int AiCount { get; private set; }
        public int EmptyCount { get; private set; }
        public bool IsLocalHost { get; private set; }
        public bool OwnReady { get; private set; }
        public bool CanToggleReady { get; private set; }
        public bool ReadyTarget => !OwnReady;
        public string ReadyButtonText => OwnReady ? "取消准备" : "确认准备";
        public string ReadyBlockedReason { get; private set; }
        public string NoticeText { get; private set; }

        public static RoomPanelViewModel Build(
            string roomId,
            RoomState roomState,
            RoomSeatMessage[] sourceSeats,
            int localSeatIndex,
            string noticeText)
        {
            RoomSeatMessage[] seats = new RoomSeatMessage[4];
            foreach (RoomSeatMessage seat in sourceSeats ?? Array.Empty<RoomSeatMessage>())
            {
                if (seat != null && seat.seatIndex >= 0 && seat.seatIndex < seats.Length)
                    seats[seat.seatIndex] = seat;
            }

            bool localHost = localSeatIndex >= 0
                             && localSeatIndex < seats.Length
                             && seats[localSeatIndex]?.isHost == true;
            bool waiting = roomState == RoomState.WaitingForPlayers
                           || roomState == RoomState.WaitingForMatchReady;

            var view = new RoomPanelViewModel
            {
                RoomId = roomId ?? string.Empty,
                RoomState = roomState,
                IsLocalHost = localHost,
                NoticeText = string.IsNullOrWhiteSpace(noticeText)
                    ? BuildDefaultNotice(roomState, seats, localSeatIndex)
                    : noticeText.Trim(),
                Seats = Enumerable.Range(0, 4)
                    .Select(index => BuildSeat(seats[index], index, localSeatIndex, localHost, waiting))
                    .ToArray()
            };

            view.HumanCount = view.Seats.Count(seat =>
                seat.State == RoomSeatVisualState.HumanOnline
                || seat.State == RoomSeatVisualState.HumanOffline
                || seat.State == RoomSeatVisualState.TemporaryAiControl);
            view.AiCount = view.Seats.Count(seat => seat.State == RoomSeatVisualState.PermanentAi);
            view.EmptyCount = view.Seats.Count(seat => seat.State == RoomSeatVisualState.Empty);
            view.OwnReady = localSeatIndex >= 0 && localSeatIndex < seats.Length
                            && seats[localSeatIndex]?.isReady == true;
            view.CanToggleReady = waiting && (view.OwnReady || view.EmptyCount == 0);
            view.ReadyBlockedReason = view.CanToggleReady
                ? null
                : view.EmptyCount > 0
                    ? "四个席位全部占用后才能准备。"
                    : "当前房间阶段不能修改准备状态。";
            return view;
        }

        private static RoomSeatViewModel BuildSeat(
            RoomSeatMessage seat,
            int seatIndex,
            int localSeatIndex,
            bool localHost,
            bool waiting)
        {
            var view = new RoomSeatViewModel
            {
                SeatIndex = seatIndex,
                IsLocal = seatIndex == localSeatIndex,
                IsHost = seat?.isHost == true,
                IsReady = seat?.isReady == true,
                DifficultyText = string.Empty,
                TemplateText = string.Empty
            };

            if (seat?.isOccupied != true)
            {
                view.State = RoomSeatVisualState.Empty;
                view.DisplayName = "空席";
                view.StatusText = "等待真人加入或由房主添加 AI";
                view.CanAddAi = localHost && waiting;
                return view;
            }

            view.DisplayName = string.IsNullOrWhiteSpace(seat.displayName)
                ? $"席位 {seatIndex + 1}"
                : seat.displayName.Trim();

            if (seat.isAi || seat.seatKind == (int)RoomSeatKind.PermanentAi)
            {
                view.State = RoomSeatVisualState.PermanentAi;
                view.DifficultyText = GetDifficultyText(seat.aiConfig?.difficulty ?? (int)AiDifficulty.Beginner);
                view.TemplateText = GetTemplateText(seat.aiConfig?.template ?? (int)AiLoadoutTemplate.Custom);
                view.StatusText = $"永久 AI | {view.DifficultyText} | {view.TemplateText}";
                view.CanEditAi = localHost && waiting;
                return view;
            }

            if (seat.isTemporarilyAiControlled || string.Equals(seat.controlState, "AiControlled", StringComparison.Ordinal))
            {
                view.State = RoomSeatVisualState.TemporaryAiControl;
                view.StatusText = "真人离线保留 | AI 临时托管";
            }
            else if (!seat.isOnline)
            {
                view.State = RoomSeatVisualState.HumanOffline;
                view.StatusText = "真人离线保留";
            }
            else
            {
                view.State = RoomSeatVisualState.HumanOnline;
                view.StatusText = view.IsReady ? "真人在线 | 已准备" : "真人在线 | 未准备";
            }

            return view;
        }

        public static string GetDifficultyText(int value) => (AiDifficulty)value switch
        {
            AiDifficulty.Standard => "标准",
            _ => "新手"
        };

        public static string GetTemplateText(int value) => (AiLoadoutTemplate)value switch
        {
            AiLoadoutTemplate.Aggressive => "进攻构筑",
            AiLoadoutTemplate.Stable => "稳健构筑",
            AiLoadoutTemplate.TalentSynergy => "天赋联动构筑",
            _ => "自定义构筑"
        };

        public static string GetRoomStateText(RoomState state) => state switch
        {
            RoomState.WaitingForPlayers => "等待补齐席位",
            RoomState.WaitingForMatchReady => "等待准备",
            RoomState.LoadingGameScene => "正在进入对局",
            RoomState.InRound => "对局进行中",
            RoomState.WaitingForNextRound => "等待下一局",
            RoomState.WaitingForSideboard => "中场备牌",
            _ => "房间已关闭"
        };

        private static string BuildDefaultNotice(RoomState state, RoomSeatMessage[] seats, int localSeatIndex)
        {
            int empty = seats.Count(seat => seat?.isOccupied != true);
            if (empty > 0) return $"仍有 {empty} 个空席；房主可手动添加 AI。";
            bool ownReady = localSeatIndex >= 0 && localSeatIndex < seats.Length
                            && seats[localSeatIndex]?.isReady == true;
            if (state == RoomState.WaitingForMatchReady || state == RoomState.WaitingForPlayers)
                return ownReady ? "已准备，等待其他真人玩家。" : "席位已满，可以确认准备。";
            return GetRoomStateText(state);
        }
    }
}
