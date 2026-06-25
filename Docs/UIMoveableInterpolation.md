# UIMoveable T/VT 帧间插值动画系统

## 概述

`UIMoveable` 是所有 UI 动画的底层引擎，采用 **T/VT 双变换 + 指数平滑** 实现帧间插值，完全独立于 DOTween。系统中所有卡牌移动（出牌飞入、弃牌叠放、手牌弧形对齐、排序动画、选中弹出/复位、拖拽回弹）均由同一套机制驱动。

**文件**: `Assets/GameMain/Scripts/UI/UIMoveable.cs`
**基类**: `ItemLogicEx` + `IPointerDownHandler / IDragHandler / IPointerUpHandler`

---

## 核心数据结构

### Transform2D

```csharp
public struct Transform2D
{
    public Vector2 position;   // anchoredPosition
    public float rotation;     // Z 轴旋转（度）
    public Vector2 scale;      // localScale（xy）
}
```

封装 UI 元素的三维变换状态。提供 `FromRect`（读）和 `ApplyTo`（写）两个静态辅助。

### T / VT / CT 三缓冲

```
T  = Target   (目标变换)   → 外部写入，表示"最终要到达的状态"
VT = Visible  (可见变换)   → 每帧向 T 指数平滑趋近，实际渲染值
CT = Collision (碰撞变换)  → 别名到 VT，碰撞检测用
```

| 字段 | 谁写入 | 谁读取 |
|------|--------|--------|
| `T` | 外部（`SetTargetPosition` / `AnimateSmoothTo` / 直接赋值 `T.rotation`） | `Update()` 插值计算 |
| `VT` | `Update()` 插值结果 | `VT.ApplyTo(RectTransform)` 每帧渲染 |
| `CT` | `Update()`（`CT = VT`） | 外部碰撞检测 |

---

## 核心算法：指数平滑

### 数学公式

```
t  = 1 - e^(-speed × Δt)
VT = Lerp(VT, T, t)
```

其中 `t` 是帧间逼近系数，由 `speed` 和 `Δtime` 决定。这不是等速移动，而是**每帧按固定比例缩小 VT 与 T 之间的距离**。

### 逐帧行为

```
帧 0: VT = 0,    T = 100,   距离 = 100
帧 1: VT = 100×t,            距离 = 100(1-t)
帧 2: VT = 100(1-(1-t)²),   距离 = 100(1-t)²
帧 N: VT → T                 距离 → 0 (指数衰减)
```

### 与线性 Lerp 对比

| 特性 | 指数平滑 `Lerp + 1-e^(-speed*dt)` | 线性 `Lerp + fixed ratio` |
|------|----------------------------------|--------------------------|
| 帧率无关 | ✅ dt 参与计算，不同帧率轨迹一致 | ❌ 帧率波动导致快慢不一 |
| 缓入缓出 | ✅ 初始快、末尾慢（自然衰减） | ❌ 等速 |
| 目标动态修改 | ✅ 平滑转向新目标，无跳变 | ❌ 中途改目标会跳变 |

### 速度参数对照

```
speed = 5   → t(60fps) ≈ 0.08  → 约 0.5s 接近目标
speed = 12  → t(60fps) ≈ 0.18  → 约 0.25s 接近目标（默认位置速度）
speed = 20  → t(60fps) ≈ 0.28  → 约 0.15s 接近目标（弹出速度）
```

---

## Update 循环

`Update()` 每帧按顺序执行三个阶段：

### 第一阶段：抖动处理

```csharp
// Perlin 噪声生成随机偏移 + 时间衰减
float decay = 1 - elapsed/duration;
x = (PerlinNoise(0, elapsed*vibrato) * 2 - 1) * strength * decay;
y = (PerlinNoise(100, elapsed*vibrato) * 2 - 1) * strength * decay;
T.position = basePosition + (x, y);
```

抖动通过修改 `T.position` 实现——VT 自动跟随 T，无需额外动画逻辑。衰减系数 `decay` 使抖动逐渐减弱直至归零。

触发场景：选中被拒时 Card 调用 `PlayShake()`。

### 第二阶段：倾斜复位

```csharp
// 拖拽结束后的倾斜插值复位
tt = 1 - exp(-_tiltSpeed * dt);
newZ = Lerp(currentZ, targetZ, tt);
```

倾斜系统独立于 T/VT，直接操作子物体的 `localRotation`。拖拽中实时跟手（跳过插值），松手后平滑复位。

### 第三阶段：T/VT 插值

```csharp
// 跳过条件：拖拽中 / 已稳定 / 无目标
if (_isDragging || IsSettled || _smoothRectTarget == null) return;

// 三通道独立速度
pt = 1 - exp(-posSpeed * dt);       // 位置
rt = 1 - exp(-rotationSpeed * dt);  // 旋转
st = 1 - exp(-scaleSpeed * dt);     // 缩放

VT.position = Lerp(VT.position, T.position, pt);
VT.rotation = Lerp(VT.rotation, T.rotation, rt);
VT.scale    = Lerp(VT.scale,    T.scale,    st);

VT.ApplyTo(_smoothRectTarget);
IsSettled = VT.Approximately(T);  // 距离 < 0.1px 且 旋转 < 0.05° 且 缩放 < 0.001
```

关键设计：
- **三通道独立**：位置、旋转、缩放各自以不同速度插值，互不干扰
- **跳过优化**：`IsSettled` 为 true 时跳过整个 T/VT 插值块，节省 CPU
- **`_overridePositionSpeed`**：支持临时覆盖位置速度，下次 `IsSettled` 时自动恢复默认值

---

## 公开 API

### 动画启动

| 方法 | T 写入 | VT 写入 | 使用场景 |
|------|--------|---------|---------|
| `AnimateSmoothTo(target, start)` | position = target, scale = (1,1) | position = start, 立即 ApplyTo | 出牌飞入、弃牌移动、排序 |
| `SetTargetPosition(pos)` | position = pos | 保持不变 | 手牌弧形对齐（每帧更新 T） |
| `SetTargetPosition(pos, speed)` | position = pos, 设 _overridePositionSpeed | 保持不变 | 弧形对齐（指定速度） |
| `SetTargetScale(scale)` | scale = scale | 保持不变 | 拖拽放大/缩小 |

### 动画终止

| 方法 | 行为 |
|------|------|
| `SnapSmoothTransform()` | T = VT = 当前 RectTransform 状态，IsSettled = true（杀停所有动画） |
| `SnapToTarget()` | VT = T，立即 ApplyTo（跳到目标位置） |
| `SetSmoothPositionImmediate(pos)` | T = VT = pos，立即 ApplyTo，IsSettled = true（瞬间跳转） |

### 状态查询

| 属性 | 说明 |
|------|------|
| `IsSettled` | VT 与 T 在所有通道上都近似相等时为 true |
| `T` / `VT` / `CT` | 公开可读写（`T.rotation` 直接赋值用于扇形/弃牌旋转） |

---

## 出牌模块使用模式

### 模式 1：固定起点 → 固定终点（AnimateSmoothTo）

用于出牌飞入和弃牌动画。起点和终点都是一次性确定的。

```
出牌:  card 在手牌区位置 → 出牌区目标位置
      AnimateSmoothTo(playTarget, currentHandPos)
      每张牌错开 0.12s

弃牌:  card 在出牌区位置 → 弃牌区随机位置
      AnimateSmoothTo(randomDiscardPos, currentPlayPos)
      延迟递减（收牌效果）
```

### 模式 2：动态目标（SetTargetPosition）

用于手牌弧形对齐。每帧（或每次变化时）重新计算所有牌的 T 位置，VT 自动平滑趋近。

```
AlignHandCards():
  计算 arc_x, arc_y, fan_rotation
  → SetTargetPosition(x, y), speed=15
  → T.rotation = fan_angle
  Update() 自动插值 VT → T
```

此模式下外部持续写入 T，VT 永远在追赶当前 T，形成"目标追踪"效果。适合手牌区滑动、补牌后位置重算等 T 会变化但不应跳变的场景。

### 模式 3：杀停 + 重启（Snap + Animate）

用于排序动画。先杀停所有动画，再以旧位置为起点启动新动画。

```
ClearAllSelections():
  → SnapSmoothTransform()  // 杀停
  → 快照 VT.position        // 记录旧位置

AlignCards():
  → LayoutRebuild           // 计算新位置
  → AnimateSmoothTo(newPos, oldPos)  // 启动插值
```

### 模式 4：T.rotation 直接赋值

`AnimateSmoothTo` 不写旋转通道。外部直接设 `mv.T.rotation`：

```
出牌区: mv.AnimateSmoothTo(target, current);
        mv.T.rotation = 0f;         // 水平排列

弃牌堆: mv.AnimateSmoothTo(target, current);
        mv.T.rotation = randomRot;   // 随机倾斜

手牌区: AlignHandCards()
        mv.T.rotation = fanAngle;    // 扇形展开
```

`Update()` 会自动将 `VT.rotation` 向 `T.rotation` 插值，形成平滑旋转动画。

### 模式 5：选中弹出（SetTargetPosition + speed override）

```
ApplyLift():
  targetY = _restPosition.y ± _liftAmount
  SetTargetPosition(x, targetY), speed=liftSpeed/dropSpeed
```

弹出和复位使用不同速度（弹出快、复位慢），通过 `speed` 参数传递给 `SetTargetPosition`。

---

## 与 DOTween 的对比

| 维度 | DOTween | UIMoveable T/VT |
|------|---------|----------------|
| 依赖 | 第三方库 | 纯 Unity API |
| 动画定义 | 命令式 `transform.DOMove()` | 声明式 `T.position = target` |
| 中断处理 | `Kill()` 清理 tweener | `SnapSmoothTransform()` 一行 |
| 目标动态变更 | 需 `SetTarget()` 或重建 tweener | 直接改 T，自动转向 |
| 旋转/缩放 | 独立 tweener | 统一定义在 Transform2D 中 |
| 帧率无关性 | ✅ 底层实现 | ✅ 指数平滑公式保证 |
| 每帧目标计算 | 困难（需外部同步） | 天然支持（AlignHandCards 模式） |

---

## 关键设计决策

1. **VT 为首要渲染源**：`VT.ApplyTo(rt)` 每帧无条件执行。T 只作为目标参考，不存在"T 直接渲染"的路径。保证视觉始终平滑。

2. **IsSettled 优化**：稳定态跳过插值计算。大批手牌（8 张）在静止时 Update 几乎零开销。

3. **速度与 T 分离**：`SetTargetPosition(pos)` 不耦合速度参数，速度属于动画层配置。`_overridePositionSpeed` 机制允许临时覆盖。

4. **旋转通道延迟写入**：`AnimateSmoothTo` 不重置 `T.rotation`，允许外部在调用后单独设旋转。这一设计支持扇形手牌、弃牌随机旋转等场景。
