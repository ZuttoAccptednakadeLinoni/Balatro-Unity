/****************************************************
    文件：PokerHandEvaluator.cs
    作者：k0itoyuu
    日期：2026/06/24
    功能：牌型判断器 —— 根据出牌列表按优先级判定牌型，
          返回 EnumPokerHand + 对应 Add/Mul 分值。
          算法仿照 Balatro evaluate_poker_hand。
*****************************************************/
using System.Collections.Generic;
using System.Linq;
using Yuu.Data;

namespace Yuu
{
    /// <summary>
    /// 牌型判定结果。
    /// </summary>
    public class PokerHandResult
    {
        /// <summary>判定牌型。</summary>
        public EnumPokerHand HandType { get; set; }

        /// <summary>加码值。</summary>
        public int Add { get; set; }

        /// <summary>乘倍值。</summary>
        public int Mul { get; set; }

        /// <summary>
        /// 组成牌型的计分卡牌列表（对应 Balatro scoring_hand）。
        /// 仅在 HandType != None 时有效；为 null 表示无计分牌。
        /// </summary>
        public List<CardData> ScoringHand { get; set; }

        public override string ToString()
        {
            return string.Format("{0}  Add={1}  Mul={2}  Scoring={3}",
                HandType, Add, Mul,
                ScoringHand != null ? ScoringHand.Count.ToString() : "0");
        }
    }

    /// <summary>
    /// 牌型判断器。
    /// 输入出牌列表，按 Balatro 优先级从高到低判定最佳牌型。
    /// </summary>
    public static class PokerHandEvaluator
    {
        // ============================================================
        //  辅助函数 —— 对应 Balatro misc_functions: get_X_same / get_flush / get_straight
        // ============================================================

        /// <summary>
        /// 获取点数出现次数 >= x 的分组。
        /// 返回 List<(rank, count)>，按 count 降序。
        /// </summary>
        private static List<(int rank, int count)> GetRankGroups(List<CardData> cards)
        {
            Dictionary<int, int> rankCount = new Dictionary<int, int>();
            foreach (var card in cards)
            {
                if (rankCount.ContainsKey(card.Rank))
                    rankCount[card.Rank]++;
                else
                    rankCount[card.Rank] = 1;
            }

            // 按 count 降序、rank 降序排序
            var groups = rankCount
                .Select(kv => (rank: kv.Key, count: kv.Value))
                .OrderByDescending(g => g.count)
                .ThenByDescending(g => g.rank)
                .ToList();

            return groups;
        }

        /// <summary>
        /// 检查是否存在 count >= x 的牌组。
        /// 返回 (存在?, 该点数, 具体数量)。
        /// </summary>
        private static bool HasXSame(int x, List<CardData> cards, out int rank, out int count)
        {
            var groups = GetRankGroups(cards);
            foreach (var g in groups)
            {
                if (g.count >= x)
                {
                    rank = g.rank;
                    count = g.count;
                    return true;
                }
            }
            rank = 0;
            count = 0;
            return false;
        }

        /// <summary>
        /// 获取出现 count == x 的分组列表（用于对子、三条等精确匹配）。
        /// </summary>
        private static List<int> GetExactXSame(int x, List<CardData> cards)
        {
            var groups = GetRankGroups(cards);
            List<int> result = new List<int>();
            foreach (var g in groups)
            {
                if (g.count == x)
                    result.Add(g.rank);
            }
            return result;
        }

        /// <summary>
        /// 判断是否同花（所有牌花色相同）。
        /// 需要至少 5 张牌。
        /// </summary>
        private static bool IsFlush(List<CardData> cards)
        {
            if (cards == null || cards.Count < 5)
                return false;

            string suit = cards[0].Suit;
            for (int i = 1; i < cards.Count; i++)
            {
                if (cards[i].Suit != suit)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 判断是否顺子（点数连续）。
        /// 需要至少 5 张牌，A 可作 1 或 14。
        /// 返回 (是顺子?, 最小点数, 最大点数)。
        /// </summary>
        private static bool IsStraight(List<CardData> cards, out int lowRank, out int highRank)
        {
            lowRank = 0;
            highRank = 0;

            if (cards == null || cards.Count < 5)
                return false;

            // 去重排序
            var ranks = cards.Select(c => c.Rank).Distinct().OrderBy(r => r).ToList();

            // 标准顺子检查
            if (IsConsecutive(ranks, out lowRank, out highRank))
                return true;

            // A-2-3-4-5 特殊处理: 将 A(14) 视为 1
            if (ranks.Contains(14))
            {
                var lowRanks = ranks.Select(r => r == 14 ? 1 : r).Distinct().OrderBy(r => r).ToList();
                if (IsConsecutive(lowRanks, out lowRank, out highRank))
                {
                    // lowRank 为 1，highRank 为 5
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查排序后的点数列表是否连续。
        /// </summary>
        private static bool IsConsecutive(List<int> sortedRanks, out int low, out int high)
        {
            low = 0;
            high = 0;

            if (sortedRanks.Count < 5)
                return false;

            // 尝试找最长连续段
            int bestStart = 0;
            int bestLen = 1;
            int curStart = 0;
            int curLen = 1;

            for (int i = 1; i < sortedRanks.Count; i++)
            {
                if (sortedRanks[i] == sortedRanks[i - 1] + 1)
                {
                    curLen++;
                }
                else
                {
                    if (curLen > bestLen)
                    {
                        bestLen = curLen;
                        bestStart = curStart;
                    }
                    curStart = i;
                    curLen = 1;
                }
            }
            if (curLen > bestLen)
            {
                bestLen = curLen;
                bestStart = curStart;
            }

            if (bestLen >= 5)
            {
                low = sortedRanks[bestStart];
                high = sortedRanks[bestStart + bestLen - 1];
                return true;
            }

            return false;
        }

        // ============================================================
        //  计分牌提取 —— 对应 Balatro evaluate_poker_hand 返回的 scoring_hand
        //  从选中卡牌中精确提取组成牌型的那些卡牌
        // ============================================================

        /// <summary>
        /// 获取点数出现 >= x 次的所有卡牌（选 count 最高的点数，取前 x 张）。
        /// </summary>
        private static List<CardData> GetXSameCards(int x, List<CardData> cards)
        {
            var groups = cards.GroupBy(c => c.Rank)
                .Where(g => g.Count() >= x)
                .OrderByDescending(g => g.Key)
                .ToList();

            if (groups.Count == 0)
                return new List<CardData>();

            // 取点数最高的那组，取前 x 张
            return groups[0].Take(x).ToList();
        }

        /// <summary>
        /// 获取同花卡牌（返回花色出现 >= 5 次的所有卡牌）。
        /// </summary>
        private static List<CardData> GetFlushCards(List<CardData> cards)
        {
            var suitGroups = cards.GroupBy(c => c.Suit)
                .Where(g => g.Count() >= 5)
                .OrderByDescending(g => g.Count())
                .ToList();

            if (suitGroups.Count == 0)
                return new List<CardData>();

            // 返回同花的所有卡牌（按点数降序）
            return suitGroups[0].OrderByDescending(c => c.Rank).ToList();
        }

        /// <summary>
        /// 获取顺子计分牌（5 张连续点数卡牌）。
        /// 返回 5 张组成顺子的卡牌；若存在多个顺子选最高的一组。
        /// </summary>
        private static List<CardData> GetStraightCards(List<CardData> cards)
        {
            if (cards == null || cards.Count < 5)
                return new List<CardData>();

            // 去重 + 每点数取一张最高优先级代表（花色不影响顺子判定）
            var rankMap = new Dictionary<int, CardData>();
            foreach (var c in cards)
            {
                if (!rankMap.ContainsKey(c.Rank) || c.Rank > (rankMap[c.Rank]?.Rank ?? 0))
                    rankMap[c.Rank] = c;
            }

            var ranks = rankMap.Keys.OrderBy(r => r).ToList();

            // 标准顺子
            var result = FindStraightInRanks(ranks, rankMap);
            if (result != null)
                return result;

            // A-2-3-4-5 特殊处理
            if (ranks.Contains(14))
            {
                var lowRanks = ranks.Select(r => r == 14 ? 1 : r).Distinct().OrderBy(r => r).ToList();
                var lowMap = new Dictionary<int, CardData>();
                foreach (var kv in rankMap)
                {
                    int key = kv.Key == 14 ? 1 : kv.Key;
                    if (!lowMap.ContainsKey(key))
                        lowMap[key] = kv.Value;
                }
                return FindStraightInRanks(lowRanks, lowMap) ?? new List<CardData>();
            }

            return new List<CardData>();
        }

        /// <summary>
        /// 在排序点数列表中查找连续 5 张的顺子段并返回对应卡牌。
        /// </summary>
        private static List<CardData> FindStraightInRanks(List<int> sortedRanks, Dictionary<int, CardData> rankMap)
        {
            if (sortedRanks.Count < 5)
                return null;

            // 从高到低找连续 5 张
            for (int i = sortedRanks.Count - 1; i >= 4; i--)
            {
                bool consecutive = true;
                for (int j = 0; j < 4; j++)
                {
                    if (sortedRanks[i - j] != sortedRanks[i - j - 1] + 1)
                    {
                        consecutive = false;
                        break;
                    }
                }
                if (consecutive)
                {
                    var result = new List<CardData>();
                    for (int j = 0; j < 5; j++)
                    {
                        if (rankMap.TryGetValue(sortedRanks[i - j], out var card))
                            result.Add(card);
                    }
                    return result;
                }
            }
            return null;
        }

        /// <summary>获取最高点数的 count 张卡牌。</summary>
        private static List<CardData> GetHighCards(List<CardData> cards, int count)
        {
            return cards.OrderByDescending(c => c.Rank).Take(count).ToList();
        }

        /// <summary>获取最高点数的 1 张卡牌。</summary>
        private static List<CardData> GetHighCard(List<CardData> cards)
        {
            return GetHighCards(cards, 1);
        }

        // ============================================================
        //  主判定 —— 对应 Balatro evaluate_poker_hand
        //  按优先级从高到低依次判定，返回第一个匹配的牌型
        // ============================================================

        /// <summary>
        /// 判定牌型。返回最佳匹配的 PokerHandResult（含 Add/Mul/ScoringHand）。
        /// 若配置表未加载则 Add/Mul 为 0。
        /// </summary>
        /// <param name="cards">出牌列表（1-5 张）。</param>
        /// <returns>判定结果。</returns>
        public static PokerHandResult Evaluate(List<CardData> cards)
        {
            if (cards == null || cards.Count == 0)
                return new PokerHandResult { HandType = EnumPokerHand.None, Add = 0, Mul = 0, ScoringHand = null };

            int cardCount = cards.Count;
            var groups = GetRankGroups(cards);
            bool flush = IsFlush(cards);
            bool straight = IsStraight(cards, out int straightLow, out int straightHigh);

            EnumPokerHand handType;
            List<CardData> scoringHand = null;

            // ---- 按优先级判定 ----

            // Flush Five: 5 张同点 + 同花
            if (cardCount >= 5 && HasXSame(5, cards, out _, out _) && flush)
            {
                handType = EnumPokerHand.FlushFiveOfAKind;
                scoringHand = GetXSameCards(5, cards);
            }
            // Flush House: 三条 + 对子 + 同花（同花色）
            else if (cardCount >= 5 && HasXSame(3, cards, out int fh3Rank, out _) && HasXSame(2, cards, out int fh2Rank, out _) && flush)
            {
                var threes = GetExactXSame(3, cards);
                var twos = GetExactXSame(2, cards);
                if (threes.Count > 0 && twos.Count > 0 && threes[0] != twos[0])
                {
                    handType = EnumPokerHand.FlushFullHouse;
                    // 三条 + 对子（同花已由外层 flush 保证）
                    scoringHand = new List<CardData>();
                    scoringHand.AddRange(GetXSameCards(3, cards));
                    // 对子取不同于三条点数的
                    var pairCards = cards.Where(c => c.Rank != threes[0])
                        .GroupBy(c => c.Rank)
                        .Where(g => g.Count() >= 2)
                        .OrderByDescending(g => g.Key)
                        .FirstOrDefault();
                    if (pairCards != null)
                        scoringHand.AddRange(pairCards.Take(2));
                }
                else
                {
                    handType = EnumPokerHand.Flush;
                    scoringHand = GetFlushCards(cards);
                }
            }
            // Five of a Kind: 5 张同点
            else if (cardCount >= 5 && HasXSame(5, cards, out _, out _))
            {
                handType = EnumPokerHand.FiveOfAKind;
                scoringHand = GetXSameCards(5, cards);
            }
            // Straight Flush
            else if (straight && flush && cardCount >= 5)
            {
                handType = EnumPokerHand.StraightFlush;
                // 同花顺：取同花 + 顺子交集
                var flushCards = GetFlushCards(cards);
                scoringHand = GetStraightCards(flushCards);
                if (scoringHand == null || scoringHand.Count == 0)
                    scoringHand = GetStraightCards(cards); // fallback
            }
            // Four of a Kind
            else if (HasXSame(4, cards, out int fourRank, out _))
            {
                handType = EnumPokerHand.FourOfAKind;
                scoringHand = new List<CardData>();
                scoringHand.AddRange(GetXSameCards(4, cards));
                // 踢脚：最高点数的非四条牌
                var kicker = cards.Where(c => c.Rank != fourRank).OrderByDescending(c => c.Rank).FirstOrDefault();
                if (kicker != null)
                    scoringHand.Add(kicker);
            }
            // Full House: 三条 + 对子
            else if (HasXSame(3, cards, out int fh3r, out _) && HasXSame(2, cards, out int fh2r, out _))
            {
                var threes2 = GetExactXSame(3, cards);
                var twos2 = GetExactXSame(2, cards);
                if (threes2.Count > 0 && twos2.Count > 0 && threes2[0] != twos2[0])
                {
                    handType = EnumPokerHand.FullHouse;
                    scoringHand = new List<CardData>();
                    scoringHand.AddRange(GetXSameCards(3, cards));
                    var fhPairCards = cards.Where(c => c.Rank != threes2[0])
                        .GroupBy(c => c.Rank)
                        .Where(g => g.Count() >= 2)
                        .OrderByDescending(g => g.Key)
                        .FirstOrDefault();
                    if (fhPairCards != null)
                        scoringHand.AddRange(fhPairCards.Take(2));
                }
                else
                {
                    handType = EnumPokerHand.ThreeOfAKind;
                    scoringHand = GetXSameCards(3, cards);
                }
            }
            // Flush
            else if (flush && cardCount >= 5)
            {
                handType = EnumPokerHand.Flush;
                scoringHand = GetFlushCards(cards);
            }
            // Straight
            else if (straight && cardCount >= 5)
            {
                handType = EnumPokerHand.Straight;
                scoringHand = GetStraightCards(cards);
            }
            // Three of a Kind
            else if (HasXSame(3, cards, out _, out _))
            {
                handType = EnumPokerHand.ThreeOfAKind;
                scoringHand = GetXSameCards(3, cards);
            }
            // Two Pair
            else if (GetExactXSame(2, cards).Count >= 2)
            {
                handType = EnumPokerHand.TwoPair;
                var pairRanks = GetExactXSame(2, cards).OrderByDescending(r => r).Take(2).ToList();
                scoringHand = new List<CardData>();
                foreach (var pr in pairRanks)
                {
                    scoringHand.AddRange(cards.Where(c => c.Rank == pr).Take(2));
                }
            }
            // One Pair
            else if (HasXSame(2, cards, out _, out _))
            {
                handType = EnumPokerHand.OnePair;
                scoringHand = GetXSameCards(2, cards);
            }
            // High Card
            else
            {
                handType = EnumPokerHand.HighCard;
                scoringHand = GetHighCard(cards);
            }

            return BuildResult(handType, scoringHand);
        }

        /// <summary>
        /// 根据牌型枚举查询配置表构建结果。
        /// </summary>
        /// <param name="handType">牌型。</param>
        /// <param name="scoringHand">计分卡牌列表。</param>
        private static PokerHandResult BuildResult(EnumPokerHand handType, List<CardData> scoringHand)
        {
            int add = 0;
            int mul = 0;

            var data = GameEntry.Data.GetData<DataPokerHands>();
            if (data != null)
            {
                var pokerHandData = data.GetPokerHandData((int)handType);
                if (pokerHandData != null)
                {
                    add = pokerHandData.Add;
                    mul = pokerHandData.Mul;
                }
            }

            return new PokerHandResult
            {
                HandType = handType,
                Add = add,
                Mul = mul,
                ScoringHand = scoringHand,
            };
        }
    }
}
