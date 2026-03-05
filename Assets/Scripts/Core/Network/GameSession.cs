using UnityEngine;

namespace MahjongGame.Core.Network
{
    public class GameSession
    {
        public GameMode Mode;
        public WindDirection PrevalentWind;  // 当前圈风
        public int DealerIndex;              // 东家玩家索引 (0-3)，决定先摸牌和门风分配
        public int RoundInWind;              // 当前圈内局数 (0-3)
        public int TotalRoundsPlayed;        // 已完成局数
        public int[] Scores;                 // 四家累计分数
        public const int BaseScore = 8;      // 底分

        // 最近一局结果 (供 UI 读取)
        public int LastWinnerId = -1;
        public int LastFanCount;
        public bool LastIsSelfDraw;
        public int LastLoserId = -1;         // 放炮者 (-1 表示自摸或流局)

        public GameSession(GameMode mode)
        {
            Mode = mode;
            PrevalentWind = WindDirection.East;
            DealerIndex = 0;
            RoundInWind = 0;
            TotalRoundsPlayed = 0;
            Scores = new int[] { 0, 0, 0, 0 };
        }

        public void ResetRoundState()
        {
            LastWinnerId = -1;
            LastFanCount = 0;
            LastIsSelfDraw = false;
            LastLoserId = -1;
        }

        public WindDirection GetSeatWind(int playerIndex)
        {
            int offset = (playerIndex - DealerIndex + 4) % 4;
            return (WindDirection)(offset + 1); // 0→East(1), 1→South(2), 2→West(3), 3→North(4)
        }

        public int GetTotalRounds()
        {
            switch (Mode)
            {
                case GameMode.Single: return 1;
                case GameMode.EastOnly: return 4;
                case GameMode.HalfGame: return 8;
                case GameMode.FullGame: return 16;
                default: return 1;
            }
        }

        public bool IsSessionOver()
        {
            return TotalRoundsPlayed >= GetTotalRounds();
        }

        /// <summary>
        /// 固定轮转：东家顺移，每4局圈风推进
        /// </summary>
        public void AdvanceRound()
        {
            TotalRoundsPlayed++;
            DealerIndex = (DealerIndex + 1) % 4;
            RoundInWind = (RoundInWind + 1) % 4;

            if (RoundInWind == 0 && TotalRoundsPlayed > 0)
            {
                // 每4局圈风推进
                int nextWind = (int)PrevalentWind + 1;
                if (nextWind > 4) nextWind = 1;
                PrevalentWind = (WindDirection)nextWind;
            }
        }

        /// <summary>
        /// 计算并应用分数
        /// 自摸: 三家各扣(8+fan), 赢家+3*(8+fan)
        /// 点炮: 放炮者扣(8+fan), 其余两家各扣8, 赢家+(fan+24)
        /// </summary>
        public void ApplyScore(int winnerId, int fanCount, bool isSelfDraw, int loserId = -1)
        {
            LastWinnerId = winnerId;
            LastFanCount = fanCount;
            LastIsSelfDraw = isSelfDraw;
            LastLoserId = loserId;

            // 国标麻将计分 (包含底分制):
            // 自摸: 三家各付 (底分+番数), 赢家得 3×(底分+番数)
            // 点炮: 放炮者付 (底分+番数), 另两家各付底分, 赢家得 (番数+3×底分)
            // 此为"三家付底分"变体规则，保证零和
            if (isSelfDraw)
            {
                int payment = BaseScore + fanCount;
                for (int i = 0; i < 4; i++)
                {
                    if (i == winnerId)
                        Scores[i] += 3 * payment;
                    else
                        Scores[i] -= payment;
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    if (i == winnerId)
                        Scores[i] += fanCount + 3 * BaseScore;
                    else if (i == loserId)
                        Scores[i] -= (BaseScore + fanCount);
                    else
                        Scores[i] -= BaseScore;
                }
            }
        }

        private static readonly string[] WindNames = { "", "东", "南", "西", "北" };
        private static readonly string[] RoundNames = { "一", "二", "三", "四" };

        public string GetRoundLabel()
        {
            string windName = WindNames[(int)PrevalentWind];
            string roundName = RoundNames[RoundInWind];
            return $"{windName}{roundName}局";
        }

        public string GetCurrentRoundInfo()
        {
            return $"{GetRoundLabel()} (第{TotalRoundsPlayed + 1}/{GetTotalRounds()}局)";
        }
    }
}
