# 卡牌选中功能（MVC 架构）

## 概述

基于 MVC 架构的卡牌选中功能，支持最多 **5 张**卡牌同时处于选中（弹出）状态。Model 存数据，View 管表现，Controller 管逻辑，三层分离。

## 文件结构

```
Assets/GameMain/Scripts/Game/
├── README.md
├── Model/
│   ├── CardData.cs                 # 卡牌游戏数据
│   └── SelectionModel.cs           # 选中状态管理
└── Controller/
    └── CardSelectionController.cs  # 选中逻辑控制器

Assets/GameMain/Scripts/UI/
└── Card.cs                         # 卡牌 View（已重构）
```

## 架构设计

```
┌──────────────────────────────────────────────────────┐
│                     Controller                        │
│   CardSelectionController : MonoBehaviour             │
│   ┌────────────────────────────────────────────┐     │
│   │ OnCardClicked(Card) → CardClickResult       │     │
│   │ ClearSelection()                           │     │
│   │ GetSelectedCards() → IReadOnlyList<CardData>│     │
│   └──────────────┬─────────────────────────────┘     │
│                  │ 操作/读取                           │
├──────────────────┼──────────────────────────────────────┤
│                  ▼                  Model               │
│   ┌──────────────────────────────┐                     │
│   │ SelectionModel               │                     │
│   │  MaxSelectionCount = 5       │                     │
│   │  TrySelect() / Deselect()    │                     │
│   │  ClearAll() / IsSelected()   │                     │
│   │  OnSelectionChanged 事件      │                     │
│   └──────────┬───────────────────┘                     │
│              │ 包含                                     │
│   ┌──────────▼───────────────────┐                     │
│   │ CardData                     │                     │
│   │  Id / Suit / Rank / BaseChips│                     │
│   │  IsSelected                  │                     │
│   └──────────────────────────────┘                     │
├──────────────────────────────────────────────────────────┤
│                        View                              │
│   Card : UIMoveable, IPointerClickHandler               │
│   ┌────────────────────────────────────────────┐       │
│   │ CardData { get; set; }                     │       │
│   │ SelectionController { get; set; }           │       │
│   │ RefreshView() — 弹出/复位动画                │       │
│   │ PlayRejectAnimation() — 拒绝抖动            │       │
│   │ OnPointerClick() → 委托 Controller          │       │
│   └────────────────────────────────────────────┘       │
└──────────────────────────────────────────────────────┘
```

## 数据流

```
用户点击 Card
    │
    ▼
Card.OnPointerClick()
    │
    ▼
CardSelectionController.OnCardClicked(card)
    │
    ├─ 已选中?
    │   └─ SelectionModel.Deselect(cardData)
    │       └─ cardData.IsSelected = false
    │       └─ 触发 OnSelectionChanged
    │       └─ 返回 CardClickResult.Deselected
    │           └─ Card.RefreshView() → ApplyLift() → 复位动画
    │
    ├─ 未选中?
    │   └─ SelectionModel.TrySelect(cardData)
    │       ├─ 成功 (当前 < 5 张)
    │       │   └─ cardData.IsSelected = true
    │       │   └─ 触发 OnSelectionChanged
    │       │   └─ 返回 CardClickResult.Selected
    │       │       └─ Card.RefreshView() → ApplyLift() → 弹出动画
    │       │
    │       └─ 失败 (已达 5 张上限)
    │           └─ 返回 CardClickResult.Rejected
    │               └─ Card.PlayRejectAnimation() → 抖动反馈
    │
    └─ 无 CardData 绑定?
        └─ 返回 CardClickResult.Rejected
```

## 类详述

### CardData（Model 层）

纯 C# 数据类，无 UnityEngine 依赖。

| 属性 | 类型 | 说明 |
|------|------|------|
| `Id` | `int` | 卡牌唯一标识 |
| `Suit` | `string` | 花色：Hearts / Spades / Diamonds / Clubs |
| `Rank` | `int` | 点数：2-14（11=J, 12=Q, 13=K, 14=A） |
| `BaseChips` | `int` | 基础筹码值 |
| `IsSelected` | `bool` | 是否处于选中状态 |

```csharp
// 创建示例
var card = new CardData(id: 1, suit: "Hearts", rank: 14, baseChips: 11);
// → [1] Hearts A (Chips: 11)
```

### SelectionModel（Model 层）

纯 C# 逻辑类，无 UnityEngine 依赖。

| 成员 | 类型 | 说明 |
|------|------|------|
| `MaxSelectionCount` | `const int` | 最大选中数量 = **5** |
| `SelectedCount` | `int` | 当前已选中数量 |
| `IsFull` | `bool` | 是否已达上限 |
| `SelectedCards` | `IReadOnlyList<CardData>` | 已选中卡牌列表（只读） |
| `TrySelect(CardData)` | `bool` | 尝试选中，满 5 张返回 false |
| `Deselect(CardData)` | `bool` | 取消选中 |
| `ClearAll()` | `void` | 清除所有选中 |
| `IsSelected(CardData)` | `bool` | 检查是否已选中 |
| `OnSelectionChanged` | `event` | 选中状态变更事件 |

### CardSelectionController（Controller 层）

MonoBehaviour，挂载到手牌区域父 GameObject。

| 成员 | 类型 | 说明 |
|------|------|------|
| `Model` | `SelectionModel` | 内部模型（只读） |
| `CanSelectMore` | `bool` | 是否还可选中 |
| `SelectedCount` | `int` | 当前选中数量 |
| `RegisterCard(Card)` | `void` | 注册 Card View |
| `UnregisterCard(Card)` | `void` | 注销 Card View |
| `OnCardClicked(Card)` | `CardClickResult` | 处理点击（核心方法） |
| `ClearSelection()` | `void` | 清除所有选中 |
| `GetSelectedCards()` | `IReadOnlyList<CardData>` | 获取已选中数据 |

### CardClickResult（枚举）

| 值 | 说明 |
|------|------|
| `Selected` | 卡牌被成功选中 |
| `Deselected` | 卡牌被成功取消选中 |
| `Rejected` | 选中被拒绝（已达上限） |

### Card（View 层，已重构）

继承 `UIMoveable`，实现 `IPointerClickHandler`。

**新增字段：**

| 字段 | 类型 | Inspector | 说明 |
|------|------|-----------|------|
| `CardData` | `CardData` | ❌ | 关联的数据模型 |
| `SelectionController` | `CardSelectionController` | ❌ | 控制器引用 |
| `_rejectShakeStrength` | `float` | ✅ | 拒绝抖动力度（默认 8px） |
| `_rejectShakeDuration` | `float` | ✅ | 拒绝抖动时长（默认 0.3s） |
| `_rejectShakeVibrato` | `int` | ✅ | 拒绝抖动频率（默认 30） |

**新增方法：**

| 方法 | 说明 |
|------|------|
| `RefreshView()` | 根据当前选中状态驱动弹出/复位动画 |
| `PlayRejectAnimation(s, d, v)` | 播放拒绝抖动反馈 |

## 集成方式

### 1. 挂载 Controller

将 `CardSelectionController` 组件挂载到手牌区域父 GameObject（如 `UICardForm` 根节点）：

```
UICardForm (Canvas)
├── CardSelectionController ← 挂载此处
├── Scroll
│   └── Layout
│       ├── Card (1) ← 自动查找父级 Controller
│       ├── Card (2)
│       ├── ...
│       └── Card (8)
```

### 2. 传递 CardData

创建卡牌时通过 `userData` 传入：

```csharp
var cardData = new CardData(1, "Spades", 10, 10);
GameEntry.UI.OpenUIForm(EnumUIForm.UICardForm, cardData);
// 或动态创建 Item:
form.ShowItem<Card>(itemId, onShow: item =>
{
    var card = item.Logic as Card;
    card.CardData = cardData;
}, userData: cardData);
```

### 3. 获取选中结果

出牌时获取已选中的卡牌数据：

```csharp
var controller = GetComponent<CardSelectionController>();
var selectedCards = controller.GetSelectedCards(); // IReadOnlyList<CardData>

// 出牌、计分逻辑...
controller.ClearSelection(); // 出牌后清除选中
```

## 向下兼容

Card 保留 `IsSelected` 属性的完整功能：
- **有 CardData** → 读写 `CardData.IsSelected`
- **无 CardData** → 回退到内部 `_fallbackSelected` 标记
- **有 Controller** → MVC 流程（委托 Controller）
- **无 Controller** → 自管理模式（直接切换 `IsSelected`）

## 行为规格

| 场景 | 期望行为 |
|------|----------|
| 点击未选中卡牌（选中数 < 5） | 弹出（上移 `_liftAmount` 像素） |
| 点击未选中卡牌（选中数 = 5） | 拒绝：不弹出，播放抖动动画 |
| 点击已选中卡牌 | 复位（回到原位置） |
| 拖拽后释放（未拖出距离） | 不触发点击 |
| 点击无 CardData 的 Card | 拒绝（Controller 返回 Rejected） |
| 清除所有选中 | 所有卡牌复位 |

## 项目兼容性

- **Unity 版本**: 2022.3.62f1c1
- **C# 版本**: 9.0
- **命名空间**: `Yuu`
- **依赖**: DOTween (DG.Tweening)、Game Framework
