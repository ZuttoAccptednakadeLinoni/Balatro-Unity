# 出牌模块 MVC 架构文档

## 概述

出牌模块实现 Balatro 风格的"选中 → 出牌 → 弃牌 → 补牌"完整流程，严格遵循 MVC 分层架构。Model 管理纯数据状态，View 负责视觉表现与动画，Controller 协调数据流转。

---

## Model 层

### CardData — 卡牌数据模型

**文件**: `Assets/GameMain/Scripts/Game/Model/CardData.cs`

纯 C# 数据类，无 UnityEngine 依赖。作为卡牌状态的单点真相源。

| 属性 | 类型 | 说明 |
|------|------|------|
| `Id` | int | 唯一标识 |
| `Suit` | string | 花色 (Hearts/Spades/Diamonds/Clubs) |
| `Rank` | int | 点数 (2-14, 11=J, 12=Q, 13=K, 14=A) |
| `BaseChips` | int | 基础筹码值 |
| `IsSelected` | bool | 选中状态标记 |

```
CardData 与区域无关——牌属于手牌/出牌/弃牌区由所在列表决定，
不对应 Balatro 中 card.area 运行时引用。
```

### SelectionModel — 选中状态管理

**文件**: `Assets/GameMain/Scripts/Game/Model/SelectionModel.cs`

维护当前选中卡牌集合，强制最多 5 张限制。纯逻辑，无 UI 依赖。

| 成员 | 类型 | 说明 |
|------|------|------|
| `MaxSelectionCount` | const int = 5 | 单次最多选中/出牌数 |
| `SelectedCount` | int | 当前已选中数 |
| `IsFull` | bool | SelectedCount >= 5 |
| `SelectedCards` | IReadOnlyList\<CardData\> | 已选中列表（只读） |
| `OnSelectionChanged` | event | 选中变更事件 |

| 方法 | 说明 |
|------|------|
| `TrySelect(CardData)` | 尝试选中；达上限返回 false |
| `Deselect(CardData)` | 取消选中 |
| `ClearAll()` | 清空全部选中 |
| `IsSelected(CardData)` | 查询选中状态 |

---

## View 层

### UIMoveable — 插值动画基类

**文件**: `Assets/GameMain/Scripts/UI/UIMoveable.cs`

T/VT 双变换系统，所有动画（移动/旋转/缩放）由帧间插值驱动，不依赖 DOTween。

```
T  = Target  (目标变换)     ← SetTargetPosition / AnimateSmoothTo 写入
VT = Visible (可见变换)     ← Update() 每帧向 T 指数平滑趋近
CT = Collision (别名到 VT) ← 碰撞检测用
```

| 方法 | 说明 | 出牌模块调用场景 |
|------|------|----------------|
| `AnimateSmoothTo(target, start)` | VT=start, T=target, 启动插值 | 出牌 / 弃牌动画 |
| `SetTargetPosition(pos, speed)` | 仅设 T, VT 保持, 指定速度 | AlignHandCards 弧形对齐 |
| `SnapSmoothTransform()` | T=VT=当前位置, 杀停动画 | 动画前复位 |
| `SetSmoothPositionImmediate(pos)` | 瞬间跳转 | 需要时用 |
| `IsSettled` | VT ≈ T 时为 true | 等待动画完成 |

### Card — 卡牌视图

**文件**: `Assets/GameMain/Scripts/UI/Card.cs`

继承 UIMoveable。View 层：负责牌面显示、选中弹出/复位动画、点击事件转发。

| 成员 | 说明 |
|------|------|
| `CardData` | 数据绑定（Model 引用） |
| `GameController` | Controller 引用（点击时委托处理） |
| `IsSelected` | 选中状态 → 驱动 ApplyLift 弹出/复位 |
| `deck` (Image) | 牌面贴图，通过 SuitBaseIndex + RankToOffset 从图集计算 Sprite |

| 方法 | 说明 |
|------|------|
| `OnPointerClick` | 转发给 GameController.OnCardClicked，动画中 (IsPlayingHand) 屏蔽 |
| `ApplyLift()` | 选中弹出 / 取消复位动画 |
| `UpdateRestPosition()` | 刷新静止位（LayoutGroup 模式） |
| `SetRestPosition(Vector2)` | 静默设置静止位（弧形布局模式） |
| `RefreshView()` | 刷新视图（ApplyLift + UpdateDisplay） |
| `OnOpen()` | 初始化静止位 |
| `RegisterToController / UnregisterFromController` | MVC 三角绑定/解绑 |

### UICardForm — 卡牌主界面

**文件**: `Assets/GameMain/Scripts/UI/UICardForm.cs`

管理三个区域（手牌区 / 出牌区 / 弃牌区），协调所有卡牌 View 的创建、销毁、布局、动画。

#### 三层区域管理

```
┌─ UICardForm ────────────────────────────────────────┐
│                                                       │
│  ┌─ levelSelectButtonRoot (手牌区) ────────────────┐ │
│  │  字典: _cardItemMap                             │ │
│  │  布局: AlignHandCards() 弧形+扇形               │ │
│  │  滑动: UpdateHandAreaSlide()                    │ │
│  └────────────────────────────────────────────────┘ │
│                                                       │
│  ┌─ _playAreaRoot (出牌区) ────────────────────────┐ │
│  │  字典: _playAreaCardMap                         │ │
│  │  布局: CalculatePlayAreaPositions() temp_limit=5│ │
│  │  动画: AnimateSmoothTo (错开 0.12s)             │ │
│  └────────────────────────────────────────────────┘ │
│                                                       │
│  ┌─ _discardAreaRoot (弃牌区) ────────────────────┐ │
│  │  字典: _discardAreaCardMap                      │ │
│  │  位置: random jitter + rotation                 │ │
│  │  动画: AnimateSmoothTo (延迟递减)               │ │
│  └────────────────────────────────────────────────┘ │
│                                                       │
└───────────────────────────────────────────────────────┘
```

#### 核心方法

| 方法 | 层级 | 说明 |
|------|------|------|
| `CreateCardView` | V | 创建卡牌 View，MVC 绑定，触发布局刷新 |
| `RemoveCardView` | V | 销毁卡牌 View（三区字典联合查找） |
| `RefreshAllCardRestPositions` | V | 手牌静止位刷新（手动模式→AlignHandCards） |
| `AlignHandCards()` | V | 手动弧形+扇形布局计算。X=temp_limit 间距，Y=弧形偏移，R=扇形旋转 |
| `UpdateHandAreaSlide()` | V | 手牌区整体 Y 轴滑动（每帧指数平滑） |
| `SetHandAreaSlide(bool)` | V | 设滑动目标（上移露牌 / 复位） |
| `OnPlayHandButtonClick` | V→C | 入口守卫 → GameController.PlaySelected → 启动协程 |
| `PlayCardsSequence` | V | 核心动画协程（9 步完整流程） |
| `CalculatePlayAreaPositions` | V | 出牌区位置计算（temp_limit=5，居中，水平） |
| `DiscardPlayCardsToDiscard` | V | 弃牌动画协程（random jitter + rotation） |
| `WaitForPlayCardsSettled` | V | 等待出牌区动画完成 |
| `WaitForHandCardsSettled` | V | 等待手牌区动画完成 |
| `ClearPlayAreaViews` | V | 清除出牌区视图（计分后） |

---

## Controller 层

### GameController — 游戏控制器

**文件**: `Assets/GameMain/Scripts/Game/Controller/GameController.cs`

连接 Model ↔ View。管理手牌/出牌数据、发牌/弃牌/出牌逻辑、选中限制。

#### 数据管理

| 字段 | 类型 | 说明 |
|------|------|------|
| `HandSize` | int = 8 | 手牌上限 |
| `_handCards` | List\<CardData\> | 手牌数据 |
| `_playCards` | List\<CardData\> | 出牌区数据 |
| `_selectionModel` | SelectionModel | 选中状态 |
| `IsPlayingHand` | bool | 出牌动画锁 |

#### 出牌相关方法

| 方法 | 说明 |
|------|------|
| `PlaySelected()` | 快照选中列表 → ClearAll → 从 _handCards 移至 _playCards → 返回列表 |
| `RefillHandAfterPlay()` | 补牌至 HandSize |
| `ClearPlayCards()` | 清空出牌区数据 |
| `GetPlayCardsSnapshot()` | 返回出牌区副本 |
| `DealInitialHand()` | 发初始手牌 |
| `DiscardSelected()` | 弃牌+补牌 |
| `OnCardClicked(Card)` | 点击委托 → SelectionModel.TrySelect/Deselect → 返回 CardClickResult |
| `CreateAndAddCard()` | 生成随机 CardData + 委托 UICardForm.CreateCardView |
| `GetHandCardsSnapshot()` | 返回手牌列表副本（UI 排序/布局用） |

---

## MVC 数据流

### 出牌完整流程

```
┌────── 用户操作 ──────────────────────────────────────────────────┐
│                                                                    │
│  1. 点击卡牌                                                       │
│     Card.OnPointerClick → GameController.OnCardClicked            │
│       → SelectionModel.TrySelect (Model 层)                       │
│       → CardClickResult → Card.RefreshView (View 刷新)            │
│                                                                    │
│  2. 按下 Play Hand                                                 │
│     UICardForm.OnPlayHandButtonClick (View 守卫)                  │
│       → GameController.PlaySelected (Controller 层)               │
│           → _selectionModel.ClearAll()     [Model 选中清空]       │
│           → _handCards.Remove()            [Model 手牌移除]       │
│           → _playCards.AddRange()          [Model 出牌记录]       │
│       → PlayCardsSequence 协程启动          [View 动画]           │
│                                                                    │
│  3. PlayCardsSequence 动画管线                                      │
│     ┌─ 排序 (x 坐标) → 取消选中 → 禁 LayoutGroup                  │
│     ├─ 错开循环: SetParent → AnimateSmoothTo → 旋转归零           │
│     ├─ WaitForPlayCardsSettled                                     │
│     ├─ WaitForSeconds(1s)                                          │
│     ├─ DiscardPlayCardsToDiscard (弃牌动画)                        │
│     ├─ AlignHandCards (手牌弧形回流)                               │
│     ├─ RefillHandAfterPlay (Controller → CreateCardView)          │
│     └─ IsPlayingHand = false (解锁交互)                            │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

### 三层数据同步

```
        Model                          Controller                     View
        ─────                          ──────────                     ────
   CardData.IsSelected  ←────────  GameController  ──────────→  Card.IsSelected
                                    .OnCardClicked()              .ApplyLift()
                                    .PlaySelected()               .RefreshView()
                          
   _handCards[]          ←────────  GameController  ──────────→  UICardForm
   _playCards[]                      .PlaySelected()              ._cardItemMap
   SelectionModel                    .RefillHandAfterPlay()       ._playAreaCardMap
                                     
   CardData (不可变)     ─────────────────────────────────────→  Card.deck.sprite
                                                                (SuitBaseIndex + RankToOffset)
```

---

## Balatro 映射表

| Balatro (Lua) | Unity C# | 层级 |
|---------------|----------|------|
| `G.hand:remove_card(card)` | `_handCards.Remove(cardData)` | C |
| `G.play:emplace(card)` | `_playCards.Add(cardData)` → `SetParent(_playAreaRoot)` → `AnimateSmoothTo` | C→V |
| `draw_card(from, to, percent, dir)` | `PlayCardsSequence` 步骤 5 错开循环 | V |
| `CardArea:align_cards()` (hand) | `AlignHandCards()` — temp_limit + 弧形 + 扇形 | V |
| `CardArea:align_cards()` (play) | `CalculatePlayAreaPositions()` — temp_limit=5 + 水平居中 | V |
| `draw_from_play_to_discard` | `DiscardPlayCardsToDiscard()` — random jitter + rotation | V |
| `play_cards_from_highlighted` | `OnPlayHandButtonClick()` → `PlayCardsSequence()` | V |
| `G.E_MANAGER:add_event(delay=0.1)` | `yield return new WaitForSeconds(_playStaggerDelay)` | V |
| `card.T.x / T.y / T.r` | `UIMoveable.T.position / T.rotation` | V |
| `Moveable:move_xy(dt)` VT→T 弹簧 | `UIMoveable.Update()` 指数平滑 | V |
| `CardArea:move(dt)` desired_y 滑动 | `UpdateHandAreaSlide()` | V |
| `G.hand.highlighted` | `SelectionModel.SelectedCards` | M |
| `G.hand.config.temp_limit` | `GameController.HandSize` | C |
| `G.STATES.SELECTING_HAND` | `SetHandAreaSlide(true)` | V |

---

## 关键设计决策

1. **View 不直接改 Model**：Card 点击委托 GameController，由 Controller 操作 SelectionModel，Card 仅根据返回值刷新自身
2. **三区字典分离**：`_cardItemMap` / `_playAreaCardMap` / `_discardAreaCardMap` 独立管理，防止布局计算相互污染
3. **T/VT 双变换**：所有动画只设 T，VT 由 Update 自动趋近，解耦"目标计算"与"动画执行"
4. **temp_limit 布局**：手牌区和出牌区均使用上限值（HandSize / MaxSelectionCount）计算间距，出牌后剩余牌保持原位不挤拢
5. **IsPlayingHand 锁**：出牌动画期间屏蔽所有点击和按钮操作
