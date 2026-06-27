/****************************************************
    文件：GameController.cs
    作者：k0itoyuu
    日期：2026/06/20
    功能：游戏控制器（Controller 层）—— 管理手牌上限、发牌、弃牌、选中逻辑。
          初始化功能统一在此实现，通过 UICardForm 委托创建/销毁 Card View。
*****************************************************/
using System.Collections.Generic;
using GameFramework;
using UnityEngine;

namespace Yuu
{
    /// <summary>
    /// 卡牌点击操作的结果。
    /// </summary>
    public enum CardClickResult
    {
        /// <summary>卡牌被成功选中（弹出）。</summary>
        Selected,

        /// <summary>卡牌被成功取消选中（复位）。</summary>
        Deselected,

        /// <summary>选中被拒绝（已达最大选中数量上限）。</summary>
        Rejected,
    }

    /// <summary>
    /// 游戏控制器。
    /// 连接 Model（<see cref="SelectionModel"/>）和 View（<see cref="Card"/>），
    /// 管理手牌上限、发牌、弃牌补牌、选中限制等全部游戏逻辑。
    /// </summary>
    public partial class GameController : IReference
    {
        // === 手牌管理 ===

        /// <summary>
        /// 手牌上限（默认 8 张）。
        /// </summary>
        public int HandSize { get; set; } = 8;

        /// <summary>
        /// 当前手牌数量。
        /// </summary>
        public int HandCount
        {
            get { return _handCards.Count; }
        }

        private UICardForm _cardForm;
        private List<CardData> _handCards = new List<CardData>();
        private List<CardData> _playCards = new List<CardData>();

        /// <summary>抽牌堆（洗好的牌从这里取）。</summary>
        private Stack<CardData> _drawPile = new Stack<CardData>();

        /// <summary>弃牌堆（已使用过的牌，抽牌堆空时洗回）。</summary>
        private List<CardData> _discardPile = new List<CardData>();

        /// <summary>
        /// 出牌动画进行中标记，用于屏蔽动画期间的点击和按钮操作。
        /// </summary>
        public bool IsPlayingHand { get; set; }

        // === 手牌查询与重排 ===

        /// <summary>
        /// 获取手牌列表副本，供 UI 层排序等只读操作使用。
        /// </summary>
        public List<CardData> GetHandCardsSnapshot()
        {
            return new List<CardData>(_handCards);
        }

        /// <summary>
        /// 按指定顺序替换手牌列表。
        /// 由 UICardForm 在排序动画中调用，传入列表必须包含完全相同的手牌元素。
        /// </summary>
        /// <param name="sortedOrder">已排序的卡牌数据列表。</param>
        public void ReorderHandCards(List<CardData> sortedOrder)
        {
            if (sortedOrder == null)
                return;
            if (sortedOrder.Count != _handCards.Count)
                return;
            _handCards = sortedOrder;
        }

        // === 选中管理 ===

        private SelectionModel _selectionModel;

        /// <summary>
        /// 获取内部的 <see cref="SelectionModel"/>（只读访问）。
        /// </summary>
        public SelectionModel Model
        {
            get { return _selectionModel; }
        }

        /// <summary>
        /// 当前是否还可以选中更多卡牌。
        /// </summary>
        public bool CanSelectMore
        {
            get { return !_selectionModel.IsFull; }
        }

        /// <summary>
        /// 当前已选中卡牌数量。
        /// </summary>
        public int SelectedCount
        {
            get { return _selectionModel.SelectedCount; }
        }

        // === 生命周期 ===

        public static GameController Create()
        {
            GameController gameController = ReferencePool.Acquire<GameController>();
            return gameController;
        }

        /// <summary>
        /// 初始化游戏控制器。
        /// 绑定 UICardForm 引用，准备手牌管理。
        /// </summary>
        /// <param name="cardForm">卡牌界面。</param>
        public void Initialize(UICardForm cardForm)
        {
            _selectionModel = new SelectionModel();
            _cardForm = cardForm;
            _handCards.Clear();
            _playCards.Clear();
            _drawPile.Clear();
            _discardPile.Clear();

            // 生成一副完整扑克牌（52 张）并洗牌
            BuildAndShuffleDeck();
        }

        public void Clear()
        {
            _selectionModel?.ClearAll();
            _selectionModel = null;
            _handCards.Clear();
            _playCards.Clear();
            _drawPile.Clear();
            _discardPile.Clear();
            _cardForm = null;
            IsPlayingHand = false;
        }

        // === 发牌 ===

        /// <summary>
        /// 发放初始手牌，直到手牌数量达到 <see cref="HandSize"/> 上限。
        /// </summary>
        public void DealInitialHand()
        {
            if (_cardForm == null)
            {
                Debug.LogWarning("GameController.DealInitialHand: UICardForm is null.");
                return;
            }

            for (int i = 0; i < HandSize; i++)
            {
                CreateAndAddCard();
            }
        }

        // === 弃牌 ===

        /// <summary>
        /// 弃置所有已选中的手牌，并补齐至 <see cref="HandSize"/> 上限。
        /// 若没有选中任何卡牌则不执行操作。
        /// </summary>
        public void DiscardSelected()
        {
            if (_cardForm == null)
                return;

            var selected = new List<CardData>(_selectionModel.SelectedCards);

            if (selected.Count == 0)
                return;

            // 清除 Model 层选中状态
            _selectionModel.ClearAll();

            // 移除选中的卡牌，加入弃牌堆
            foreach (var cardData in selected)
            {
                _handCards.Remove(cardData);
                _discardPile.Add(cardData);
                _cardForm.RemoveCardView(cardData);
            }

            // 补齐至手牌上限
            int toRefill = HandSize - _handCards.Count;
            for (int i = 0; i < toRefill; i++)
            {
                CreateAndAddCard();
            }
        }

        // === 牌堆管理 ===

        /// <summary>
        /// 构建一副完整 52 张扑克牌并洗牌。
        /// 4 花色 × 13 点数 = 52 张，使用 Fisher-Yates 洗牌算法。
        /// </summary>
        private void BuildAndShuffleDeck()
        {
            string[] suits = { "Hearts", "Spades", "Diamonds", "Clubs" };
            int id = 1;

            List<CardData> deck = new List<CardData>(52);
            for (int s = 0; s < suits.Length; s++)
            {
                for (int rank = 2; rank <= 14; rank++)  // 2-14 (11=J, 12=Q, 13=K, 14=A)
                {
                    deck.Add(new CardData(id++, suits[s], rank, rank));
                }
            }

            // Fisher-Yates 洗牌
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }

            // 压入抽牌堆（Stack: 最后洗好的牌在栈顶，先被取出）
            for (int i = deck.Count - 1; i >= 0; i--)
            {
                _drawPile.Push(deck[i]);
            }
        }

        /// <summary>
        /// 从抽牌堆取一张牌。若抽牌堆空则将弃牌堆洗回。
        /// </summary>
        private CardData DrawCard()
        {
            if (_drawPile.Count == 0 && _discardPile.Count > 0)
            {
                // 弃牌堆洗回：Fisher-Yates → 压入抽牌堆
                for (int i = _discardPile.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (_discardPile[i], _discardPile[j]) = (_discardPile[j], _discardPile[i]);
                }
                for (int i = _discardPile.Count - 1; i >= 0; i--)
                {
                    _drawPile.Push(_discardPile[i]);
                }
                _discardPile.Clear();
            }

            if (_drawPile.Count == 0)
            {
                Debug.LogWarning("DrawCard: 牌堆已空，无法抽牌。");
                return null;
            }

            return _drawPile.Pop();
        }

        // === 卡牌创建（内部） ===

        /// <summary>
        /// 从抽牌堆取一张牌并委托 UICardForm 创建 View。
        /// 若抽牌堆空则返回不作操作。
        /// </summary>
        private void CreateAndAddCard()
        {
            CardData cardData = DrawCard();
            if (cardData == null)
                return;

            _handCards.Add(cardData);
            _cardForm.CreateCardView(cardData, this);
        }

        // === 选中逻辑 ===

        /// <summary>
        /// 为指定 Card 注册到此控制器。
        /// </summary>
        public void RegisterCard(Card card)
        {
            if (card == null)
                return;

            card.GameController = this;
        }

        /// <summary>
        /// 解除指定 Card 的注册。
        /// </summary>
        public void UnregisterCard(Card card)
        {
            if (card == null)
                return;

            card.GameController = null;
        }

        /// <summary>
        /// View 层 Card 被点击时调用此方法。
        /// 根据当前状态决定选中或取消选中。
        /// 仅操作 Model 层，不直接操作 View —— 由 Card 根据返回值自行刷新表现。
        /// </summary>
        /// <param name="card">被点击的卡牌 View。</param>
        /// <returns>
        /// <see cref="CardClickResult.Selected"/> = 成功选中；
        /// <see cref="CardClickResult.Deselected"/> = 成功取消选中；
        /// <see cref="CardClickResult.Rejected"/> = 已达上限被拒绝。
        /// </returns>
        public CardClickResult OnCardClicked(Card card)
        {
            if (card == null)
                return CardClickResult.Rejected;

            CardData cardData = card.CardData;
            if (cardData == null)
                return CardClickResult.Rejected;

            // 已选中 → 取消选中
            if (_selectionModel.IsSelected(cardData))
            {
                _selectionModel.Deselect(cardData);
                _cardForm?.OnSelectionChanged();  // 对应 Balatro remove_from_highlighted → parse_highlighted
                return CardClickResult.Deselected;
            }

            // 未选中 → 尝试选中
            bool success = _selectionModel.TrySelect(cardData);
            if (success)
            {
                _cardForm?.OnSelectionChanged();  // 对应 Balatro add_to_highlighted → parse_highlighted
            }
            return success ? CardClickResult.Selected : CardClickResult.Rejected;
        }

        /// <summary>
        /// 清除所有选中状态（Model 层）。
        /// </summary>
        public void ClearSelection()
        {
            _selectionModel.ClearAll();
            _cardForm?.OnSelectionChanged();  // 对应 Balatro unhighlight_all → parse_highlighted
        }

        /// <summary>
        /// 获取当前已选中卡牌的数据列表。
        /// 供出牌、计分等后续游戏逻辑使用。
        /// </summary>
        public IReadOnlyList<CardData> GetSelectedCards()
        {
            return _selectionModel.SelectedCards;
        }

        // === 出牌 ===

        /// <summary>
        /// 出牌：将已选中卡牌从手牌移至出牌区。
        /// 单次最多 5 张由 <see cref="SelectionModel.MaxSelectionCount"/> 保证。
        /// </summary>
        /// <returns>待出牌列表（按选中顺序）；无选中时返回 null。</returns>
        public List<CardData> PlaySelected()
        {
            var selected = new List<CardData>(_selectionModel.SelectedCards);

            if (selected.Count == 0)
                return null;

            // 清空 Model 层选中状态
            _selectionModel.ClearAll();

            // 从手牌数据移除
            foreach (var cardData in selected)
            {
                _handCards.Remove(cardData);
            }

            // 加入出牌区数据
            _playCards.AddRange(selected);

            return selected;
        }

        /// <summary>
        /// 出牌后补牌至手牌上限。
        /// 补牌至手牌上限 <see cref="HandSize"/>，复用现有 <see cref="CreateAndAddCard"/>。
        /// </summary>
        public void RefillHandAfterPlay()
        {
            if (_cardForm == null)
                return;

            int toRefill = HandSize - _handCards.Count;
            for (int i = 0; i < toRefill; i++)
            {
                CreateAndAddCard();
            }
        }

        /// <summary>
        /// 清空出牌区数据（计分后调用）。
        /// </summary>
        public void ClearPlayCards()
        {
            // 出牌区卡牌进入弃牌堆
            _discardPile.AddRange(_playCards);
            _playCards.Clear();
        }

        /// <summary>
        /// 获取出牌区卡牌数据副本（只读访问）。
        /// </summary>
        public List<CardData> GetPlayCardsSnapshot()
        {
            return new List<CardData>(_playCards);
        }
    }
}
