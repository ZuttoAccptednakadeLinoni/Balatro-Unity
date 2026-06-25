/****************************************************
    文件：UICardForm.cs
    作者：k0itoyuu
    日期：#CreateTime#
    功能：卡牌界面 —— 手牌发牌区，管理 Card View 的创建与销毁。
          游戏逻辑（发牌/弃牌/手牌上限）全部委托给 GameController。
*****************************************************/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace Yuu
{
    public class UICardForm : UGuiFormEx
    {
        public Transform levelSelectButtonRoot;
        public Button button;

        [SerializeField]
        private Button _suitSortButton;

        [SerializeField]
        private Button _pointsSortButton;

        [Header("出牌区")]
        [SerializeField]
        private Button _playHandButton;

        [SerializeField]
        private RectTransform _playAreaRoot;

        [SerializeField]
        [Tooltip("出牌区 Y 偏移（像素）")]
        private float _playAreaYOffset = 120f;

        [SerializeField]
        [Tooltip("每张牌动画间隔（秒）")]
        private float _playStaggerDelay = 0.12f;

        [Header("手牌弧形布局 —— 对应 Balatro CardArea:align_cards")]
        [SerializeField]
        [Tooltip("手牌区总宽度（像素），卡牌在此范围内均匀分布")]
        private float _handAreaWidth = 800f;

        [SerializeField]
        [Tooltip("弧形高度系数：两端比中间低多少像素")]
        private float _handArcHeight = 25f;

        [SerializeField]
        [Tooltip("扇形旋转角度：左右两端最大旋转度数")]
        private float _handFanAngle = 3f;

        [SerializeField]
        [Tooltip("选中卡牌额外上移量（像素）")]
        private float _handHighlightY = 40f;

        [SerializeField]
        [Tooltip("手牌对齐插值速度")]
        private float _handAlignSpeed = 15f;

        [SerializeField]
        [Tooltip("手牌区整体 Y 轴滑动量：SELECTING_HAND 状态下上移像素")]
        private float _handAreaSlideY = 60f;

        [SerializeField]
        [Tooltip("手牌区整体滑动平滑速度")]
        private float _handAreaSlideSpeed = 8f;

        /// <summary>手牌区目标 Y 偏移（平滑过渡目标值）。</summary>
        private float _handAreaTargetY;

        /// <summary>禁用 LayoutGroup 使用手动弧形布局。</summary>
        private bool _useManualLayout = true;

        [Header("弃牌区")]
        [SerializeField]
        private RectTransform _discardAreaRoot;

        [SerializeField]
        [Tooltip("出牌后延迟多久开始弃牌（秒）")]
        private float _discardDelay = 1f;

        [SerializeField]
        [Tooltip("弃牌动画每张牌的间隔（秒）")]
        private float _discardStaggerDelay = 0.08f;

        [SerializeField]
        [Tooltip("弃牌堆随机偏移范围 X（像素）")]
        private float _discardJitterX = 15f;

        [SerializeField]
        [Tooltip("弃牌堆随机偏移范围 Y（像素）")]
        private float _discardJitterY = 8f;

        [SerializeField]
        [Tooltip("弃牌堆随机旋转范围（度）")]
        private float _discardJitterRotation = 5f;

        /// <summary>排序动画进行中标记，防止位置刷新覆盖 mid-animation 位置。</summary>
        private bool _isSorting = false;

        private GameController _gameController;
        private Dictionary<CardData, Item> _cardItemMap = new Dictionary<CardData, Item>();

        /// <summary>出牌区 Item 映射，与手牌 _cardItemMap 分离防止 LayoutGroup 刷新污染。</summary>
        private Dictionary<CardData, Item> _playAreaCardMap = new Dictionary<CardData, Item>();

        /// <summary>弃牌区 Item 映射，卡牌叠放形成弃牌堆。</summary>
        private Dictionary<CardData, Item> _discardAreaCardMap = new Dictionary<CardData, Item>();

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            button.onClick.AddListener(OnOutButtonClick);

            if (_suitSortButton != null)
                _suitSortButton.onClick.AddListener(OnSuitSortClick);
            if (_pointsSortButton != null)
                _pointsSortButton.onClick.AddListener(OnPointsSortClick);

            if (_playHandButton != null)
                _playHandButton.onClick.AddListener(OnPlayHandButtonClick);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            // 手动布局模式：初始禁用手牌区 LayoutGroup
            if (_useManualLayout)
            {
                HorizontalLayoutGroup layout = levelSelectButtonRoot.GetComponent<HorizontalLayoutGroup>();
                if (layout != null)
                    layout.enabled = false;
            }
        }

        /// <summary>
        /// 每帧驱动手牌区 Y 轴滑动。
        /// 对应 Balatro CardArea:move(dt) 中的 desired_y 指数平滑。
        /// </summary>
        protected virtual void Update()
        {
            if (!_useManualLayout)
                return;

            UpdateHandAreaSlide();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);
        }

        /// <summary>
        /// 创建一个卡牌 View 并完成 MVC 绑定。
        /// 由 <see cref="GameController"/> 在发牌/补牌时调用。
        /// </summary>
        /// <param name="cardData">卡牌数据模型。</param>
        /// <param name="controller">游戏控制器。</param>
        public void CreateCardView(CardData cardData, GameController controller)
        {
            ShowItem<Card>(EnumItem.Card, (item) =>
            {
                Card card = item.Logic as Card;
                if (card == null)
                    return;

                // 设置父节点为 Layout（受 HorizontalLayoutGroup 控制水平排列）
                item.transform.SetParent(levelSelectButtonRoot, false);
                item.transform.localScale = Vector3.one;

                // MVC 三角绑定：Card ↔ GameController ↔ CardData
                card.RegisterToController(controller, cardData);

                // 记录映射（用于弃牌时查找 Item）
                _cardItemMap[cardData] = item;

                // 手动布局模式：跳过 LayoutGroup 刷新，设初始位置为 (0,0)，稍后由 AlignHandCards 统一计算
                // LayoutGroup 模式：强制刷新布局确保位置正确
                if (!_useManualLayout)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)levelSelectButtonRoot);
                }

                // 初始化卡牌视图（此时 layout 已计算完毕，_restPosition 正确）
                card.OnOpen();

                // 手动布局模式：每添加一张牌重算弧形分布（累计效果 = 发牌动画）
                if (_useManualLayout && !_isSorting)
                {
                    AlignHandCards();
                }

                Debug.Log(string.Format("Created card: {0}", cardData));
            });
        }

        /// <summary>
        /// 移除一张卡牌 View（弃牌时调用）。
        /// 由 <see cref="GameController.DiscardSelected"/> 调用。
        /// </summary>
        /// <param name="cardData">要移除的卡牌数据。</param>
        public void RemoveCardView(CardData cardData)
        {
            // 同时检查手牌区、出牌区、弃牌区
            if (_cardItemMap.TryGetValue(cardData, out Item item))
            {
                _cardItemMap.Remove(cardData);
            }
            else if (_playAreaCardMap.TryGetValue(cardData, out item))
            {
                _playAreaCardMap.Remove(cardData);
            }
            else if (_discardAreaCardMap.TryGetValue(cardData, out item))
            {
                _discardAreaCardMap.Remove(cardData);
            }
            else
            {
                return;
            }

            Card card = item.Logic as Card;
            card?.UnregisterFromController();

            HideItem(item);
        }

        /// <summary>
        /// 刷新所有剩余手牌的静止位。
        /// 弃牌补牌后调用，确保 HorizontalLayoutGroup 重新排列后的位置被正确记录。
        /// </summary>
        public void RefreshAllCardRestPositions()
        {
            // 排序动画进行中跳过，防止 _restPosition 被中间帧位置污染
            if (_isSorting)
                return;

            if (_useManualLayout)
            {
                // 手动布局模式：用弧形扇形系统重算所有手牌 T 目标 + 静止位
                AlignHandCards();
                return;
            }

            // LayoutGroup 模式：强制重算布局 → 更新静止位
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)levelSelectButtonRoot);

            foreach (var kvp in _cardItemMap)
            {
                // 跳过出牌区卡牌，防止出牌动画中被 Layout 位置覆盖
                if (_playAreaCardMap.ContainsKey(kvp.Key))
                    continue;

                Card card = kvp.Value.Logic as Card;
                card?.UpdateRestPosition();
            }
        }

        // ============================================================
        //  弧形手牌布局系统 —— 对应 Balatro CardArea:align_cards()
        //  手动计算每张牌的 X/Y/Rotation，替代 HorizontalLayoutGroup
        //  T/VT 系统自动插值 → 平滑动画
        // ============================================================

        /// <summary>
        /// 手动计算所有手牌的 T 目标位置（弧形+扇形）。
        /// 使用 temp_limit（HandSize）保持间距，出牌后剩余牌不立即挤在一起。
        /// 对应 Balatro align_cards() 中的 X/Y/R 计算。
        /// </summary>
        private void AlignHandCards()
        {
            if (_gameController == null)
                return;

            var handCards = _gameController.GetHandCardsSnapshot();
            if (handCards == null || handCards.Count == 0)
                return;

            // ---- 参数准备 ----
            int handCount = handCards.Count;
            int maxCards = _gameController.HandSize; // temp_limit = 手牌上限（8）
            int displaySlots = Mathf.Max(handCount, maxCards);

            // 获取卡牌宽度
            float cardWidth = 100f;
            foreach (var kvp in _cardItemMap)
            {
                RectTransform rt = kvp.Value.transform as RectTransform;
                if (rt != null && rt.rect.width > 0)
                {
                    cardWidth = rt.rect.width;
                    break;
                }
            }

            // ---- 计算分布参数 ----
            // 对应 Balatro: max_cards = max(#cards, temp_limit)
            // cardSpacing 基于 displaySlots（= maxCards）计算，使剩余牌保持原间距
            float availableWidth = _handAreaWidth - cardWidth; // 首尾卡牌中心之间可用宽度
            float cardSpacing = availableWidth / Mathf.Max(displaySlots - 1, 1);
            // handCount 张牌在 displaySlots 个槽位中居中
            int startSlot = (displaySlots - handCount) / 2;
            float totalSlotWidth = (displaySlots - 1) * cardSpacing; // displaySlots 的完整宽度
            float startX = -totalSlotWidth / 2f;

            for (int i = 0; i < handCount; i++)
            {
                CardData cd = handCards[i];
                if (!_cardItemMap.TryGetValue(cd, out Item item) || item == null)
                    continue;

                UIMoveable mv = item.Logic as UIMoveable;
                Card card = item.Logic as Card;
                if (mv == null)
                    continue;

                // ---- X 轴（使用 displaySlots 宽度 + startSlot 居中） ----
                // 对应 Balatro: T.x = (T.w-card_w)*((k-1)/max(max_cards-1,1) - 0.5*(#cards-max_cards)/max(...))
                float x = startX + (startSlot + i) * cardSpacing;

                // ---- Y 轴（弧形） ----
                // 对应 Balatro: arc = |0.5*(-#cards/2 + k-0.5)/#cards| - 0.2
                float arcNorm = Mathf.Abs(0.5f * (-handCount / 2f + i - 0.5f) / Mathf.Max(handCount, 1)) - 0.2f;
                float arcOffset = _handArcHeight * arcNorm; // 负值 = 两端低于中间
                // 高亮卡牌上移
                float highlightOffset = (card != null && card.IsSelected) ? -_handHighlightY : 0f;
                float totalYOffset = arcOffset + highlightOffset + _handAreaTargetY;

                // ---- 旋转（扇形展开） ----
                // 对应 Balatro: T.r = 0.2*(-#cards/2 -0.5 + k)/#cards
                float rotNorm = (-handCount / 2f - 0.5f + i) / Mathf.Max(handCount, 1);
                float rotation = _handFanAngle * rotNorm;

                Vector2 targetPos = new Vector2(x, totalYOffset);

                // ---- 设置 T 目标（VT 保持不变 → Update 自动插值） ----
                mv.SetTargetPosition(targetPos, _handAlignSpeed);
                mv.T.rotation = rotation;
                mv.T.scale = Vector2.one;

                // ---- 更新静止位（供 ApplyLift 弹出/复位时使用） ----
                // 不含高亮的基准位置
                card?.SetRestPosition(new Vector2(x, arcOffset + _handAreaTargetY));
            }

            // 临时禁用 LayoutGroup（手动布局模式）
            if (_useManualLayout)
            {
                HorizontalLayoutGroup layout = levelSelectButtonRoot.GetComponent<HorizontalLayoutGroup>();
                if (layout != null)
                    layout.enabled = false;
            }
        }

        /// <summary>
        /// 手牌区整体 Y 轴滑动更新。
        /// 每帧平滑趋近 _handAreaTargetY，触发 AlignHandCards 重算位置。
        /// </summary>
        private void UpdateHandAreaSlide()
        {
            if (!_useManualLayout || levelSelectButtonRoot == null)
                return;

            RectTransform handRt = levelSelectButtonRoot as RectTransform;
            if (handRt == null)
                return;

            float currentY = handRt.anchoredPosition.y;
            float newY = Mathf.Lerp(currentY, _handAreaTargetY, 1f - Mathf.Exp(-_handAreaSlideSpeed * Time.deltaTime));

            if (Mathf.Abs(newY - currentY) > 0.05f)
            {
                handRt.anchoredPosition = new Vector2(handRt.anchoredPosition.x, newY);
                // 滑动中重算卡牌位置
                AlignHandCards();
            }
        }

        /// <summary>
        /// 设置手牌区滑动状态。
        /// 对应 Balatro: desired_y 在 SELECTING_HAND 时上移，出牌后下滑。
        /// </summary>
        /// <param name="slideUp">true = 上移露出手牌；false = 复位。</param>
        public void SetHandAreaSlide(bool slideUp)
        {
            _handAreaTargetY = slideUp ? _handAreaSlideY : 0f;
        }

        /// <summary>
        /// 等待所有手牌 T/VT 插值完成（不含出牌区、弃牌区）。
        /// </summary>
        private IEnumerator WaitForHandCardsSettled()
        {
            float timeout = 2f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                yield return null;
                elapsed += Time.deltaTime;

                bool allSettled = true;
                foreach (var kvp in _cardItemMap)
                {
                    if (_playAreaCardMap.ContainsKey(kvp.Key))
                        continue;
                    UIMoveable mv = kvp.Value.Logic as UIMoveable;
                    if (mv != null && !mv.IsSettled)
                    {
                        allSettled = false;
                        break;
                    }
                }
                if (allSettled)
                    break;
            }

            // 确保所有手牌对齐到 T
            foreach (var kvp in _cardItemMap)
            {
                if (_playAreaCardMap.ContainsKey(kvp.Key))
                    continue;
                UIMoveable mv = kvp.Value.Logic as UIMoveable;
                mv?.SnapSmoothTransform();
            }
        }
        private void OnOutButtonClick()
        {
            if (_gameController != null && _gameController.IsPlayingHand)
                return;

            if (_gameController == null)
            {
                Debug.LogWarning("UICardForm.OnOutButtonClick: GameController is null.");
                return;
            }

            _gameController.DiscardSelected();

            // 延迟刷新：ShowItem 回调是异步的，等一帧让 Layout 更新后再刷新所有静止位
            StartCoroutine(DelayedRefreshRestPositions());
        }

        /// <summary>
        /// 延迟一帧后刷新所有卡牌的静止位，确保 Layout 已重新计算。
        /// </summary>
        private IEnumerator DelayedRefreshRestPositions()
        {
            yield return null;  // 等待一帧，让 UI 布局更新
            RefreshAllCardRestPositions();
        }

        /// <summary>
        /// 设置 GameController 引用（由 ProcedureGame 调用）。
        /// </summary>
        public void SetGameController(GameController controller)
        {
            _gameController = controller;
        }

        // ============================================================
        //  排序系统 —— 参照 Balatro CardArea:sort() + align_cards() 模式
        //  sort()  → 重排数组 + 视图层级
        //  align() → 计算目标位置 → 设置 card.T → 启动插值
        //  Moveable.Update() → 每帧 VT → T 平滑过渡
        // ============================================================

        /// <summary>
        /// 花色排序权重获取，复用 Card.SuitBaseIndex。
        /// </summary>
        private static int GetSuitOrder(string suit)
        {
            if (Card.SuitBaseIndex.TryGetValue(suit, out int order))
                return order;
            return int.MaxValue;
        }

        private static int CompareBySuitThenRank(CardData a, CardData b)
        {
            int suitA = GetSuitOrder(a.Suit);
            int suitB = GetSuitOrder(b.Suit);
            if (suitA != suitB)
                return suitA.CompareTo(suitB);
            return a.Rank.CompareTo(b.Rank);
        }

        private static int CompareByRankThenSuit(CardData a, CardData b)
        {
            if (a.Rank != b.Rank)
                return a.Rank.CompareTo(b.Rank);
            return GetSuitOrder(a.Suit).CompareTo(GetSuitOrder(b.Suit));
        }

        // ---- 按钮回调（sort trigger） ----

        private void OnSuitSortClick()
        {
            if (_isSorting)
                return;
            if (_gameController != null && _gameController.IsPlayingHand)
                return;
            if (_gameController == null)
                return;
            if (_cardItemMap.Count <= 1)
                return;

            _isSorting = true;
            var oldPositions = ClearAllSelections();   // 1. 清除选中 + 快照旧位置
            SortCards("suit");                          // 2. 重排数组 + 层级
            AlignCards(oldPositions);                   // 3. 计算 T 目标位置 + 启动插值
            StartCoroutine(WaitForSettleAndRestore());  // 4. 等待 IsSettled → 恢复
        }

        private void OnPointsSortClick()
        {
            if (_isSorting)
                return;
            if (_gameController != null && _gameController.IsPlayingHand)
                return;
            if (_gameController == null)
                return;
            if (_cardItemMap.Count <= 1)
                return;

            _isSorting = true;
            var oldPositions = ClearAllSelections();
            SortCards("rank");
            AlignCards(oldPositions);
            StartCoroutine(WaitForSettleAndRestore());
        }

        // ---- CardArea:sort() 等效 —— 重排数组 + 视图层级 ----

        /// <summary>
        /// 排序卡牌：重排 Model 层列表 + View 层 sibling 层级。
        /// 对应 Balatro CardArea:sort(method)。
        /// </summary>
        private void SortCards(string method)
        {
            List<CardData> sorted = _gameController.GetHandCardsSnapshot();
            if (sorted.Count <= 1)
                return;

            if (method == "suit")
                sorted.Sort(CompareBySuitThenRank);
            else if (method == "rank")
                sorted.Sort(CompareByRankThenSuit);

            // 同步 Model 层顺序
            _gameController.ReorderHandCards(sorted);

            // 同步 View 层 sibling 层级
            foreach (CardData cardData in sorted)
            {
                if (_cardItemMap.TryGetValue(cardData, out Item item) && item != null)
                    item.transform.SetAsLastSibling();
            }
        }

        // ---- CardArea:align_cards() 等效 —— 计算目标位置 → 设置 card.T ----

        /// <summary>
        /// 对齐卡牌：重建 Layout 获取新目标位置，为每张卡设置 T/VT 启动插值。
        /// 对应 Balatro CardArea:align_cards() —— 计算 card.T.x / card.T.y / card.T.r。
        /// </summary>
        /// <param name="oldPositions">排序前各卡牌的旧 VT 位置（在 SortCards 前快照）。</param>
        private void AlignCards(Dictionary<CardData, Vector2> oldPositions)
        {
            HorizontalLayoutGroup layout = levelSelectButtonRoot.GetComponent<HorizontalLayoutGroup>();

            // 启用 LayoutGroup，重建布局计算新目标位置
            if (layout != null)
                layout.enabled = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)levelSelectButtonRoot);

            // 遍历每张卡：读取旧 VT 位置和新 Layout 目标位置，启动 T/VT 插值
            foreach (var kvp in _cardItemMap)
            {
                UIMoveable mv = kvp.Value.Logic as UIMoveable;
                if (mv == null)
                    continue;

                RectTransform rt = kvp.Value.transform as RectTransform;
                if (rt == null)
                    continue;

                if (!oldPositions.TryGetValue(kvp.Key, out Vector2 oldPos))
                    oldPos = rt.anchoredPosition;
                Vector2 newPos = rt.anchoredPosition; // Layout 刚计算的目标位置

                // T = 新目标位置，VT = 旧可见位置（下一帧 Update 自动 VT → T）
                mv.AnimateSmoothTo(newPos, oldPos);
            }

            // 禁用 LayoutGroup，将位置控制权移交给 UIMoveable.Update 插值
            if (layout != null)
                layout.enabled = false;
        }

        // ---- 插值等待 —— 所有卡 IsSettled 后恢复 LayoutGroup ----

        /// <summary>
        /// 等待所有卡牌的 T/VT 插值完成（IsSettled），然后恢复 LayoutGroup 和 _restPosition。
        /// </summary>
        private IEnumerator WaitForSettleAndRestore()
        {
            HorizontalLayoutGroup layout = levelSelectButtonRoot.GetComponent<HorizontalLayoutGroup>();

            // 等待所有卡 IsSettled（带超时保护）
            float timeout = 2f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                yield return null;
                elapsed += Time.deltaTime;

                bool allSettled = true;
                foreach (var kvp in _cardItemMap)
                {
                    UIMoveable mv = kvp.Value.Logic as UIMoveable;
                    if (mv != null && !mv.IsSettled)
                    {
                        allSettled = false;
                        break;
                    }
                }
                if (allSettled)
                    break;
            }

            // 确保所有卡对齐到 T
            foreach (var kvp in _cardItemMap)
            {
                UIMoveable mv = kvp.Value.Logic as UIMoveable;
                if (mv != null)
                    mv.SnapSmoothTransform();
            }

            // 恢复 LayoutGroup 控制权（手动模式保持禁用）
            if (!_useManualLayout && layout != null)
                layout.enabled = true;

            // 更新 _restPosition 到新位置
            if (_useManualLayout)
            {
                // 手动模式：弧形扇形系统重算所有手牌 T 目标 + 静止位
                // VT 已 Snap 到当前位置 → 平滑过渡到弧形成果
                AlignHandCards();
            }
            else
            {
                foreach (var kvp in _cardItemMap)
                {
                    Card card = kvp.Value.Logic as Card;
                    if (card != null)
                        card.UpdateRestPosition();
                }
            }

            _isSorting = false;
        }

        // ---- 辅助：清除所有选中 ----

        /// <summary>
        /// 排序前清除所有卡牌选中状态 + 杀停插值动画 + 快照旧位置。
        /// </summary>
        /// <returns>排序前各卡牌的旧 VT 位置快照。</returns>
        private Dictionary<CardData, Vector2> ClearAllSelections()
        {
            if (_gameController != null)
                _gameController.ClearSelection();

            foreach (var kvp in _cardItemMap)
            {
                Card card = kvp.Value.Logic as Card;
                if (card != null)
                    card.RefreshView();
            }

            // 杀停插值动画 + 快照排序前旧位置
            Dictionary<CardData, Vector2> oldPositions = new Dictionary<CardData, Vector2>();
            foreach (var kvp in _cardItemMap)
            {
                UIMoveable mv = kvp.Value.Logic as UIMoveable;
                if (mv == null)
                    continue;
                mv.SnapSmoothTransform();                  // 中止插值动画
                oldPositions[kvp.Key] = mv.VT.position;   // 快照排序前的 VT 位置
            }

            return oldPositions;
        }

        // ============================================================
        //  出牌系统 —— 对应 Balatro play_cards_from_highlighted + draw_card
        //  OnPlayHandButtonClick → PlaySelected → PlayCardsSequence
        //  SetParent + AnimateSmoothTo = draw_card → emplace → align_cards
        // ============================================================

        /// <summary>
        /// 牌型判定 + 更新 GameInfo UI。
        /// 对应 Balatro evaluate_poker_hand → update_hand_ui。
        /// </summary>
        private void EvaluateAndDisplayHandResult(List<CardData> playedCards)
        {
            // 牌型判定
            PokerHandResult result = PokerHandEvaluator.Evaluate(playedCards);

            // 更新 UIGameInfoForm
            var uiForm = GameEntry.UI.GetUIForm(EnumUIForm.UIGameInfoForm);
            if (uiForm is UIGameInfoForm infoForm)
            {
                infoForm.UpdateHandResult(result);
            }

            // 日志输出：点数列表 + 牌型 + 加码 + 倍率
            LogHandResult(playedCards, result);
        }

        /// <summary>
        /// 输出出牌日志：点数、牌型、加码、倍率。
        /// </summary>
        private static void LogHandResult(List<CardData> playedCards, PokerHandResult result)
        {
            if (result == null)
                return;

            // 构建点数列表字符串
            var rankStrs = new List<string>(playedCards.Count);
            foreach (var card in playedCards)
            {
                string rankStr = card.Rank switch
                {
                    11 => "J",
                    12 => "Q",
                    13 => "K",
                    14 => "A",
                    _  => card.Rank.ToString(),
                };
                rankStrs.Add(string.Format("{0}{1}", rankStr, card.Suit[0]));  // e.g. "A♥"
            }

            string cardsInfo = string.Join(" ", rankStrs);
            string handName  = UIGameInfoForm.GetHandTypeDisplayNameStatic(result.HandType);

            Debug.Log(string.Format(
                "[出牌] 点数: {0} | 牌型: {1}({2}) | 筹码: {3} | 倍率: ×{4} | 总分: {5}",
                cardsInfo,
                handName,
                result.HandType,
                result.Add,
                result.Mul,
                result.Add * result.Mul));
        }

        /// <summary>
        /// Play Hand 按钮回调 —— 入口守卫 + 启动出牌协程。
        /// </summary>
        private void OnPlayHandButtonClick()
        {
            // 守卫：动画进行中 / 排序进行中 / 控制器缺失
            if (_gameController != null && _gameController.IsPlayingHand)
                return;
            if (_isSorting)
                return;
            if (_gameController == null)
            {
                Debug.LogWarning("UICardForm.OnPlayHandButtonClick: GameController is null.");
                return;
            }
            if (_playAreaRoot == null)
            {
                Debug.LogWarning("UICardForm.OnPlayHandButtonClick: _playAreaRoot is not assigned.");
                return;
            }

            // 清除出牌区旧牌（上轮残留）
            ClearPlayAreaViews();

            // 控制器处理选中 → 获取待出牌列表
            List<CardData> cardsToPlay = _gameController.PlaySelected();
            if (cardsToPlay == null || cardsToPlay.Count == 0)
                return;

            _gameController.IsPlayingHand = true;
            StartCoroutine(PlayCardsSequence(cardsToPlay));
        }

        /// <summary>
        /// 出牌核心动画协程。
        /// 对应 Balatro: play_cards_from_highlighted → draw_card → emplace → align_cards。
        /// </summary>
        private IEnumerator PlayCardsSequence(List<CardData> cardsToPlay)
        {
            // ---- 步骤 1: 按当前视图 x 坐标排序（左→右） ----
            Dictionary<CardData, float> xPositions = new Dictionary<CardData, float>();
            foreach (var cd in cardsToPlay)
            {
                if (_cardItemMap.TryGetValue(cd, out Item item) && item != null)
                {
                    RectTransform rt = item.transform as RectTransform;
                    xPositions[cd] = rt != null ? rt.anchoredPosition.x : float.MaxValue;
                }
                else
                {
                    xPositions[cd] = float.MaxValue;
                }
            }
            cardsToPlay.Sort((a, b) => xPositions[a].CompareTo(xPositions[b]));

            // ---- 步骤 2: 快照卡牌状态 ----
            // 待出牌：取消选中（回落）+ 杀停动画
            foreach (var cd in cardsToPlay)
            {
                if (_cardItemMap.TryGetValue(cd, out Item item) && item != null)
                {
                    Card card = item.Logic as Card;
                    UIMoveable mv = item.Logic as UIMoveable;
                    if (card != null)
                        card.IsSelected = false;   // 复位弹出状态
                    mv?.SnapSmoothTransform();      // 杀停当前动画
                }
            }

            // 剩余手牌位置快照（仅非手动模式需要，供步骤 7 LayoutGroup 回流）
            Dictionary<CardData, Vector2> remainingOldPositions = null;
            if (!_useManualLayout)
            {
                remainingOldPositions = new Dictionary<CardData, Vector2>();
                foreach (var kvp in _cardItemMap)
                {
                    if (cardsToPlay.Contains(kvp.Key))
                        continue;
                    UIMoveable mv = kvp.Value.Logic as UIMoveable;
                    mv?.SnapSmoothTransform();
                    RectTransform rt = kvp.Value.transform as RectTransform;
                    remainingOldPositions[kvp.Key] = rt != null ? rt.anchoredPosition : Vector2.zero;
                }
            }

            // ---- 步骤 3: 计算出牌区目标位置 ----
            Dictionary<CardData, Vector2> playTargets = CalculatePlayAreaPositions(cardsToPlay);

            // ---- 步骤 4: 禁用手牌区 LayoutGroup，冻结剩余手牌 ----
            HorizontalLayoutGroup layout = levelSelectButtonRoot.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
                layout.enabled = false;

            // ---- 步骤 5: 错开循环（逐张 reparent + 启动插值动画） ----
            for (int i = 0; i < cardsToPlay.Count; i++)
            {
                CardData cd = cardsToPlay[i];
                if (!_cardItemMap.TryGetValue(cd, out Item item) || item == null)
                    continue;

                UIMoveable mv = item.Logic as UIMoveable;
                Card card = item.Logic as Card;
                if (mv == null)
                    continue;

                // 5a: 解除 Controller 引用（屏蔽后续点击），但保留 CardData 维持牌面贴图
                if (card != null)
                    card.GameController = null;

                // 5b: 切换父节点到出牌区（worldPositionStays=true 保持视觉位置不变）
                item.transform.SetParent(_playAreaRoot, true);

                // 5c: 字典迁移：手牌 → 出牌区
                _cardItemMap.Remove(cd);
                _playAreaCardMap[cd] = item;

                // 5d: 读取当前 anchoredPosition（Unity 已自动重算为出牌区空间坐标）
                RectTransform rt = item.transform as RectTransform;
                Vector2 currentPos = rt != null ? rt.anchoredPosition : Vector2.zero;
                Vector2 targetPos = playTargets.TryGetValue(cd, out Vector2 t) ? t : Vector2.zero;

                // 5e: 启动插值动画：VT=当前位置, T=目标位置 → Update 自动 VT→T 缓动
                // 出牌区水平排列：旋转归零（对应 Balatro play area T.r = 0）
                mv.AnimateSmoothTo(targetPos, currentPos);
                mv.T.rotation = 0f;
                mv.T.scale = Vector2.one;

                // 5f: 错开延迟（最后一张不等待）
                if (i < cardsToPlay.Count - 1)
                    yield return new WaitForSeconds(_playStaggerDelay);
            }

            // ---- 步骤 6: 等待所有出牌动画完成 ----
            yield return StartCoroutine(WaitForPlayCardsSettled(cardsToPlay));

            // ---- 步骤 6.2: 牌型判定 + 更新 UI ----
            // 对应 Balatro: evaluate_poker_hand → G.FUNCS.get_poker_hand_info
            EvaluateAndDisplayHandResult(cardsToPlay);

            // ---- 步骤 6.5: 延迟后弃置出牌区卡牌至弃牌堆 ----
            // 对应 Balatro: evaluate_play 完成后 delay=0.1 → draw_from_play_to_discard
            yield return new WaitForSeconds(_discardDelay);
            yield return StartCoroutine(DiscardPlayCardsToDiscard(cardsToPlay));

            // ---- 步骤 7: 剩余手牌回流动画 ----
            // 对应 Balatro: align_cards() 每帧计算 T → Moveable 自动插值 VT→T
            // 手动布局模式用弧形扇形计算；LayoutGroup 模式维持原有回流逻辑
            if (_useManualLayout)
            {
                // 弧形布局：AlignHandCards 设 T 目标 → UIMoveable.Update 自动 VT→T 插值
                AlignHandCards();
            }
            else
            {
                if (layout != null)
                    layout.enabled = true;
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)levelSelectButtonRoot);

                foreach (var kvp in _cardItemMap)
                {
                    if (_playAreaCardMap.ContainsKey(kvp.Key))
                        continue;

                    UIMoveable mv = kvp.Value.Logic as UIMoveable;
                    if (mv == null)
                        continue;

                    RectTransform rt = kvp.Value.transform as RectTransform;
                    Vector2 newPos = rt != null ? rt.anchoredPosition : Vector2.zero;
                    Vector2 oldPos = (remainingOldPositions != null && remainingOldPositions.TryGetValue(kvp.Key, out Vector2 op)) ? op : newPos;

                    mv.AnimateSmoothTo(newPos, oldPos);
                }
            }

            // 等待手牌回流动画完成
            yield return StartCoroutine(WaitForHandCardsSettled());

            // ---- 步骤 8: 补牌至手牌上限 ----
            _gameController.RefillHandAfterPlay();

            // 等待异步 ShowItem 回调
            yield return null;
            RefreshAllCardRestPositions();

            // ---- 步骤 9: 恢复交互 ----
            _gameController.IsPlayingHand = false;
        }

        /// <summary>
        /// 计算出牌区每张卡牌的目标 anchoredPosition。
        /// 对应 Balatro play area align_cards：水平居中、无旋转、temp_limit 均匀分布。
        /// </summary>
        private Dictionary<CardData, Vector2> CalculatePlayAreaPositions(List<CardData> cards)
        {
            Dictionary<CardData, Vector2> targets = new Dictionary<CardData, Vector2>();
            if (cards == null || cards.Count == 0 || _playAreaRoot == null)
                return targets;

            // 从第一张牌获取卡牌宽度
            float cardWidth = 100f;
            if (_cardItemMap.TryGetValue(cards[0], out Item firstItem) && firstItem != null)
            {
                RectTransform rt = firstItem.transform as RectTransform;
                if (rt != null && rt.rect.width > 0)
                    cardWidth = rt.rect.width;
            }

            int count = cards.Count;
            // play area temp_limit = 5（最大出牌数），对应 Balatro self.config.temp_limit
            int maxCards = Mathf.Max(count, 5);
            float availableWidth = _playAreaRoot.rect.width - cardWidth;
            float cardSpacing = availableWidth / Mathf.Max(maxCards - 1, 1);
            int startSlot = (maxCards - count) / 2;
            float totalSlotWidth = (maxCards - 1) * cardSpacing;
            float startX = -totalSlotWidth / 2f;

            for (int i = 0; i < count; i++)
            {
                float x = startX + (startSlot + i) * cardSpacing;
                // Y 居中，无弧形（对应 Balatro: T.y = self.T.y + T.h/2 - card.T.h/2）
                targets[cards[i]] = new Vector2(x, _playAreaYOffset);
            }

            return targets;
        }

        /// <summary>
        /// 等待出牌区所有卡牌 T/VT 插值完成。
        /// </summary>
        private IEnumerator WaitForPlayCardsSettled(List<CardData> playedCards)
        {
            float timeout = 2f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                yield return null;
                elapsed += Time.deltaTime;

                bool allSettled = true;
                foreach (var cd in playedCards)
                {
                    if (!_playAreaCardMap.TryGetValue(cd, out Item item) || item == null)
                        continue;
                    UIMoveable mv = item.Logic as UIMoveable;
                    if (mv != null && !mv.IsSettled)
                    {
                        allSettled = false;
                        break;
                    }
                }
                if (allSettled)
                    break;
            }

            // 超时保护：强制对齐到目标位置
            foreach (var cd in playedCards)
            {
                if (!_playAreaCardMap.TryGetValue(cd, out Item item) || item == null)
                    continue;
                UIMoveable mv = item.Logic as UIMoveable;
                mv?.SnapSmoothTransform();
            }
        }

        /// <summary>
        /// 清除出牌区所有卡牌视图（计分后调用）。
        /// </summary>
        public void ClearPlayAreaViews()
        {
            if (_gameController == null)
                return;

            var playCards = _gameController.GetPlayCardsSnapshot();
            foreach (var cd in playCards)
            {
                if (_playAreaCardMap.TryGetValue(cd, out Item item))
                {
                    _playAreaCardMap.Remove(cd);

                    Card card = item.Logic as Card;
                    card?.UnregisterFromController();

                    HideItem(item);
                }
            }
            _gameController.ClearPlayCards();
        }

        // ============================================================
        //  弃牌系统 —— 对应 Balatro draw_from_play_to_discard + draw_card(dir='down')
        //  出牌后延迟 _discardDelay 秒 → 逐张从出牌区移动至弃牌区
        //  弃牌区卡牌随机偏移 + 旋转，形成叠放效果
        // ============================================================

        /// <summary>
        /// 弃置出牌区所有卡牌至弃牌堆。
        /// 对应 Balatro draw_from_play_to_discard：
        ///   遍历 G.play.cards → draw_card(G.play, G.discard, percent, 'down')
        ///   'down' 使 percent 反转，音效音调从高到低。
        /// </summary>
        /// <param name="playedCards">要弃置的卡牌列表（出牌顺序）。</param>
        private IEnumerator DiscardPlayCardsToDiscard(List<CardData> playedCards)
        {
            if (_discardAreaRoot == null || playedCards == null || playedCards.Count == 0)
                yield break;

            // 统计还存在的出牌区卡牌（对应 Balatro 跳过 shattered/destroyed 的牌）
            List<CardData> validCards = new List<CardData>();
            foreach (var cd in playedCards)
            {
                if (_playAreaCardMap.ContainsKey(cd))
                    validCards.Add(cd);
            }

            int playCount = playedCards.Count;  // 原始总数（含已销毁，用于 percent 计算）
            int it = 0;  // 有效计数（参考 Balatro 中 it 只对未销毁牌递增）

            for (int i = 0; i < validCards.Count; i++)
            {
                CardData cd = validCards[i];
                if (!_playAreaCardMap.TryGetValue(cd, out Item item) || item == null)
                    continue;

                UIMoveable mv = item.Logic as UIMoveable;
                if (mv == null)
                    continue;

                it++;

                // 对应 Balatro draw_card(G.play, G.discard, it*100/play_count, 'down')
                // dir='down' 时 percent = 1-percent，使音效音调从高到低
                // float percent = (float)it * 100f / playCount;

                // 杀停当前动画，快照出牌区位置
                mv.SnapSmoothTransform();

                // 切换父节点到弃牌区
                item.transform.SetParent(_discardAreaRoot, true);

                // 字典迁移：出牌区 → 弃牌区
                _playAreaCardMap.Remove(cd);
                _discardAreaCardMap[cd] = item;

                // 读取当前位置（Unity 自动重算为弃牌区空间坐标）
                RectTransform rt = item.transform as RectTransform;
                Vector2 currentPos = rt != null ? rt.anchoredPosition : Vector2.zero;

                // 计算弃牌堆目标位置（随机偏移 + 随机旋转，模拟叠放效果）
                Vector2 discardTarget = CalculateDiscardAreaPosition();
                float randomRotation = Random.Range(-_discardJitterRotation, _discardJitterRotation);

                // 启动插值动画：位置 + 旋转
                // AnimateSmoothTo 内部已设 IsSettled=false；T.rotation 由 Update 自动趋近
                mv.AnimateSmoothTo(discardTarget, currentPos);
                mv.T.rotation = randomRotation;

                // 对应 Balatro：dir='down' 时越后面的牌延迟越短（收牌效果）
                // 用递减延迟模拟：延迟 = _discardStaggerDelay * (1 - it/playCount)
                float delay = _discardStaggerDelay * (1f - (float)it / playCount + 0.3f);
                if (i < validCards.Count - 1)
                    yield return new WaitForSeconds(Mathf.Max(0.02f, delay));
            }

            // 等待所有弃牌动画完成
            float timeout = 2f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                yield return null;
                elapsed += Time.deltaTime;

                bool allSettled = true;
                foreach (var cd in validCards)
                {
                    if (!_discardAreaCardMap.TryGetValue(cd, out Item item) || item == null)
                        continue;
                    UIMoveable mv = item.Logic as UIMoveable;
                    if (mv != null && !mv.IsSettled)
                    {
                        allSettled = false;
                        break;
                    }
                }
                if (allSettled)
                    break;
            }

            // 超时保护：强制对齐
            foreach (var cd in validCards)
            {
                if (!_discardAreaCardMap.TryGetValue(cd, out Item item) || item == null)
                    continue;
                UIMoveable mv = item.Logic as UIMoveable;
                mv?.SnapSmoothTransform();
            }
        }

        /// <summary>
        /// 计算弃牌堆中一张牌的目标位置。
        /// 随机偏移模拟叠放效果，对应 Balatro discard_pos + random jitter。
        /// </summary>
        private Vector2 CalculateDiscardAreaPosition()
        {
            float x = Random.Range(-_discardJitterX, _discardJitterX);
            float y = Random.Range(-_discardJitterY, _discardJitterY);
            return new Vector2(x, y);
        }
    }
}
