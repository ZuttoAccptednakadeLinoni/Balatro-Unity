/****************************************************
    文件：Card.cs
    作者：k0itoyuu
    日期：#CreateTime#
    功能：卡牌 View —— 继承 UIMoveable，作为 MVC 架构中的 View 层。
          点击时委托 Controller 处理选中逻辑，
          根据 Model (CardData) 的状态驱动弹出/复位动画。
          所有动画由 UIMoveable 的 T/VT 帧间插值驱动。
*****************************************************/
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using Yuu.Data;

namespace Yuu
{
    /// <summary>
    /// 卡牌 View 组件。
    /// MVC 架构中的 View 层：负责视觉表现（弹出/复位动画、拒绝抖动），
    /// 不直接管理选中逻辑，而是委托给 <see cref="CardSelectionController"/>。
    /// 向下兼容：若无 Controller 注入则回退到自管理模式。
    /// </summary>
    public class Card : UIMoveable, IPointerClickHandler
    {
        [Header("点击弹出")]
        [SerializeField]
        [Tooltip("向上弹出的距离（像素）")]
        private float _liftAmount = 40f;

        [SerializeField]
        [Tooltip("弹出插值速度（值越大越快）")]
        private float _liftSpeed = 20f;

        [SerializeField]
        [Tooltip("复位插值速度")]
        private float _dropSpeed = 15f;

        [Header("拒绝反馈")]
        [SerializeField]
        [Tooltip("选中被拒时的抖动力度（像素）")]
        private float _rejectShakeStrength = 8f;

        [SerializeField]
        [Tooltip("选中被拒时的抖动持续时间（秒）")]
        private float _rejectShakeDuration = 0.3f;

        [SerializeField]
        [Tooltip("选中被拒时的抖动 vibrato（频率）")]
        private int _rejectShakeVibrato = 30;

        public Image deck;
        

        // === MVC 绑定 ===

        /// <summary>
        /// 关联的卡牌数据模型（Model 层）。
        /// 由外部（如 HandManager）在创建卡牌时赋值。
        /// </summary>
        public CardData CardData { get; set; }

        /// <summary>
        /// 选中控制器引用（Controller 层）。
        /// 由 <see cref="GameController.RegisterCard"/> 注入。
        /// </summary>
        public GameController GameController { get; set; }

        /// <summary>
        /// 当无 CardData 绑定时的回退选中状态标记。
        /// </summary>
        private bool _fallbackSelected;

        private bool _restPositionSaved;
        private Vector2 _restPosition;

        /// <summary>
        /// 是否被选中（弹出状态）。
        /// 优先读取 CardData.IsSelected；若无 CardData 绑定则使用内部回退标记。
        /// 设置时：若有 Controller 绑定则委托处理；否则直接操作回退状态。
        /// </summary>
        public bool IsSelected
        {
            get
            {
                if (CardData != null)
                    return CardData.IsSelected;
                return _fallbackSelected;
            }
            set
            {
                if (CardData != null)
                {
                    // 有数据模型绑定：直接修改模型并刷新视图
                    if (CardData.IsSelected == value)
                        return;
                    CardData.IsSelected = value;
                }
                else
                {
                    // 无数据模型：回退到自管理模式
                    if (_fallbackSelected == value)
                        return;
                    _fallbackSelected = value;
                }

                ApplyLift();
            }
        }

#if UNITY_2017_3_OR_NEWER
        public void OnOpen()
#else
        protected internal override void OnOpen(object userData)
#endif
        {
            EnsureInitialized();
            _restPosition = _dragTarget != null ? _dragTarget.anchoredPosition : Vector2.zero;
            _restPositionSaved = true;
        }

        /// <summary>
        /// 静默设置静止位（不杀停动画），供弧形布局系统使用。
        /// _restPosition 作为 ApplyLift 弹出/复位的基准位置。
        /// </summary>
        /// <param name="pos">新的静止位（不含弹出偏移）。</param>
        public void SetRestPosition(Vector2 pos)
        {
            _restPosition = pos;
            _restPositionSaved = true;
        }

        /// <summary>
        /// 刷新静止位为当前实际位置。
        /// 当 HorizontalLayoutGroup 重新排列后调用，确保弹出动画从正确位置开始。
        /// 若当前处于选中状态则立即重新应用弹出。
        /// </summary>
        public void UpdateRestPosition()
        {
            // 拖拽中跳过：_dragTarget 正被手动控制，不可作为静止位来源
            if (_isDragging)
                return;

            if (_dragTarget == null)
                return;

            // 同步 T/VT 到当前 Layout 位置，防止布局变更后动画从旧位置起跳
            SnapSmoothTransform();

            _restPosition = _dragTarget.anchoredPosition;
            _restPositionSaved = true;

            // 若当前选中中，从新位置重新弹出
            if (IsSelected)
            {
                ApplyLift();
            }
        }

        /// <summary>
        /// 向 Controller 注册并绑定 CardData。
        /// 由 GameController 在创建 Card 后调用，完成 MVC 三角绑定。
        /// </summary>
        /// <param name="controller">选中控制器。</param>
        /// <param name="data">卡牌数据模型。</param>
        public void RegisterToController(GameController controller, CardData data)
        {
            CardData = data;
            GameController = controller;
            controller?.RegisterCard(this);
            UpdateDisplay();
        }

        /// <summary>
        /// 从 Controller 解除注册并清理绑定。
        /// 由 UICardForm 在移除 Card 时调用。
        /// </summary>
        public void UnregisterFromController()
        {
            GameController?.UnregisterCard(this);
            CardData = null;
            UpdateDisplay();
        }



        /// <summary>
        /// 点击事件。
        /// MVC 流程：委托 Controller 处理 → 根据返回值刷新 View。
        /// 无 Controller 时回退到自管理模式。
        /// 拖拽后不触发点击。
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging)
                return;

            // 出牌动画进行中，屏蔽点击
            if (GameController != null && GameController.IsPlayingHand)
                return;

            if (GameController != null)
            {
                // MVC 流程：委托 Controller 处理选中逻辑
                CardClickResult result = GameController.OnCardClicked(this);

                switch (result)
                {
                    case CardClickResult.Selected:
                        RefreshView();
                        LogCardValue("选中");
                        break;

                    case CardClickResult.Deselected:
                        RefreshView();
                        break;

                    case CardClickResult.Rejected:
                        PlayRejectAnimation(_rejectShakeStrength, _rejectShakeDuration, _rejectShakeVibrato);
                        break;
                }
            }
            else
            {
                // 回退：自管理模式
                IsSelected = !IsSelected;
            }
        }

        /// <summary>
        /// 刷新视图表现。
        /// 根据当前选中状态驱动弹出或复位动画。
        /// 通常由 Controller 或外部逻辑在 Model 变更后调用。
        /// </summary>
        public void RefreshView()
        {
            ApplyLift();
            UpdateDisplay();
        }

        /// <summary>
        /// 播放选中被拒的抖动反馈动画（程序化 Perlin 噪声，无 DOTween 依赖）。
        /// 当已达最大选中数量时触发，提示玩家无法继续选中。
        /// </summary>
        public void PlayRejectAnimation(float strength = 8f, float duration = 0.3f, int vibrato = 30)
        {
            EnsureInitialized();
            if (_dragTarget == null)
                return;
            PlayShake(strength, duration, vibrato, 90f);
        }

        /// <summary>
        /// 应用弹出或复位动画。
        /// 根据 <see cref="IsSelected"/> 通过 T/VT 插值驱动位置，弹出速度 > 复位速度。
        /// </summary>
        private void ApplyLift()
        {
            EnsureInitialized();

            if (_dragTarget == null)
                return;

            // 懒初始化
            if (!_restPositionSaved)
            {
                _restPosition = _dragTarget.anchoredPosition;
                _restPositionSaved = true;
            }

            bool isLifted = IsSelected;

            float targetY = isLifted
                ? _restPosition.y + _liftAmount
                : _restPosition.y;

            float speed = isLifted ? _liftSpeed : _dropSpeed;
            SetTargetPosition(new Vector2(_restPosition.x, targetY), speed);
        }

        // === 显示逻辑 ===

        /// <summary>
        /// 图集中每 13 张为一个花色组：红桃 → 草花 → 方块 → 黑桃。
        /// 每组内点数排序：2, 3, 4, 5, 6, 7, 8, 9, 10, J, Q, K, A。
        /// </summary>
        internal static readonly System.Collections.Generic.Dictionary<string, int> SuitBaseIndex =
            new System.Collections.Generic.Dictionary<string, int>
            {
                { "Hearts",   0 },
                { "Clubs",   13 },
                { "Diamonds", 26 },
                { "Spades",  39 },
            };

        /// <summary>
        /// 输出选中卡牌的面值日志。
        /// </summary>
        private void LogCardValue(string action)
        {
            if (CardData == null)
                return;

            string rankStr = CardData.Rank switch
            {
                11 => "J",
                12 => "Q",
                13 => "K",
                14 => "A",
                _  => CardData.Rank.ToString(),
            };

            Debug.Log(string.Format("[{0}] {1}{2}  Id={3}  筹码: {4}",
                action, rankStr, CardData.Suit, CardData.Id, CardData.BaseChips));
        }

        /// <summary>
        /// 点数到组内偏移的转换。
        /// 图集顺序 2→A 升序：2→0, 3→1, ..., 10→8, J→9, Q→10, K→11, A→12。
        /// </summary>
        private static int RankToOffset(int rank)
        {
            return rank switch
            {
                14 => 12, // A
                13 => 11, // K
                12 => 10, // Q
                11 => 9,  // J
                _  => rank - 2
            };
        }

        /// <summary>
        /// 更新卡牌牌面显示。
        /// 根据 CardData 的花色与点数计算 8BitDeck 图集中的 Sprite 序号并赋值给 deck Image。
        /// </summary>
        private void UpdateDisplay()
        {
            if (deck == null)
                return;

            if (CardData == null)
            {
                deck.sprite = null;
                return;
            }

            if (!SuitBaseIndex.TryGetValue(CardData.Suit, out int suitBase))
            {
                Log.Warning("Unknown suit '{0}' for card id {1}.", CardData.Suit, CardData.Id);
                deck.sprite = null;
                return;
            }

            int rankOffset = RankToOffset(CardData.Rank);
            int spriteIndex = suitBase + rankOffset;

            deck.sprite = GameEntry.Data.GetData<DataTextures>().GetSprite(1001, spriteIndex);
        }

// public void OnPointerClick(PointerEventData eventData)
// {
//     throw new System.NotImplementedException();
// }
    }
}
