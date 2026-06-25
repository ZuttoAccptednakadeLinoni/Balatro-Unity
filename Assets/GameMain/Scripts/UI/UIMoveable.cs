/****************************************************
    文件：UIMoveable.cs
    作者：k0itoyuu
    日期：#CreateTime#
    功能：可拖拽 UI 窗体 —— T / VT / CT 帧间插值驱动所有动画。
          按住拖动时视觉层跟随+倾斜，松开后 VT 向 T 缓动复位。
          位置层与倾斜层分离，速度平滑化消除抖动。
*****************************************************/
using UnityEngine;
using UnityEngine.EventSystems;

namespace Yuu
{
    /// <summary>
    /// 二维变换状态：位置（anchoredPosition）、旋转（Z 轴）、缩放。
    /// T（Target）= 目标 / VT（Visible）= 可见 / CT（Collision）= 碰撞。
    /// </summary>
    [System.Serializable]
    public struct Transform2D
    {
        public Vector2 position;
        public float rotation;
        public Vector2 scale;

        public static Transform2D FromRect(RectTransform rt)
        {
            return new Transform2D
            {
                position = rt.anchoredPosition,
                rotation = rt.localRotation.eulerAngles.z,
                scale = rt.localScale
            };
        }

        public void ApplyTo(RectTransform rt)
        {
            rt.anchoredPosition = position;
            rt.localRotation = Quaternion.Euler(0f, 0f, rotation);
            rt.localScale = new Vector3(scale.x, scale.y, 1f);
        }

        public bool Approximately(Transform2D other, float posEps = 0.1f, float rotEps = 0.05f, float sclEps = 0.001f)
        {
            return Vector2.Distance(position, other.position) < posEps
                && Mathf.Abs(rotation - other.rotation) < rotEps
                && Vector2.Distance(scale, other.scale) < sclEps;
        }
    }

    /// <summary>
    /// 可拖拽 UI 窗体组件。
    /// 所有动画由 T/VT 帧间插值驱动，不依赖 DOTween。
    /// </summary>
    public class UIMoveable : ItemLogicEx, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField]
        [Tooltip("拖拽目标 RectTransform（只做位移，不旋转）")]
        protected RectTransform _dragTarget = null;

        [SerializeField]
        [Tooltip("倾斜父级 Transform（旋转作用对象），留空则自动取 _dragTarget 的直接子物体")]
        private Transform _tiltParent = null;

        [SerializeField]
        [Tooltip("拖拽灵敏度")]
        private float _dragSensitivity = 1f;

        [Header("倾斜")]
        [SerializeField]
        [Tooltip("跟随移动产生的旋转幅度（每像素移动产生的旋转角度）")]
        private float _rotationAmount = 2f;

        [SerializeField]
        [Tooltip("最大倾斜角度（度）")]
        private float _maxTiltAngle = 45f;

        [SerializeField]
        [Tooltip("旋转平滑速度")]
        private float _rotationSmoothSpeed = 15f;

        [Header("插值速度")]
        [SerializeField]
        [Tooltip("位置插值速度（值越大越快趋近目标）")]
        private float _positionSpeed = 12f;

        [SerializeField]
        [Tooltip("旋转插值速度")]
        private float _rotationSpeed = 15f;

        [SerializeField]
        [Tooltip("缩放插值速度")]
        private float _scaleSpeed = 10f;

        [SerializeField]
        [Tooltip("倾斜复位插值速度")]
        private float _tiltSpeed = 15f;

        [Header("拖拽缩放")]
        [SerializeField]
        [Tooltip("拖拽时缩放（1 = 不变）")]
        private float _scaleOnDrag = 1.05f;

        protected Vector2 _originalPosition;
        private Vector3 _smoothedDelta;
        protected bool _isDragging;

        // 倾斜目标列表
        private System.Collections.Generic.List<Transform> _tiltTargets;
        private System.Collections.Generic.List<Vector3> _originalTiltRotations;
        private System.Collections.Generic.List<float> _tiltTargetRotations;

        // === T / VT / CT 帧间插值系统 ===

        /// <summary>T : 目标变换。</summary>
        [HideInInspector] public Transform2D T;
        /// <summary>VT : 可见变换（每帧向 T 缓动）。</summary>
        [HideInInspector] public Transform2D VT;
        /// <summary>CT : 碰撞变换（别名到 VT）。</summary>
        [HideInInspector] public Transform2D CT;
        /// <summary>插值是否已完成（VT ≈ T 且倾斜已复位）。</summary>
        public bool IsSettled { get; private set; }

        private RectTransform _smoothRectTarget;
        private float _overridePositionSpeed = -1f;

        // === 抖动（shake）状态 ===
        private bool _shakeActive;
        private float _shakeStartTime;
        private float _shakeDuration;
        private float _shakeStrength;
        private int _shakeVibrato;
        private float _shakeRandomness;
        private Vector2 _shakeBasePosition;

        private void InitSmoothTransform()
        {
            if (_smoothRectTarget == null)
                _smoothRectTarget = _dragTarget != null ? _dragTarget : GetComponent<RectTransform>();
            if (_smoothRectTarget != null && VT.position == Vector2.zero && T.position == Vector2.zero)
            {
                T = Transform2D.FromRect(_smoothRectTarget);
                VT = T;
                CT = T;
                IsSettled = true;
            }
        }

        // ============================================================
        //  Update —— 每帧驱动 VT → T 插值 + 倾斜插值 + 抖动
        // ============================================================

        protected virtual void Update()
        {
            float dt = Time.deltaTime;

            // ---- 抖动（shake）处理 ----
            if (_shakeActive)
            {
                float elapsed = Time.time - _shakeStartTime;
                if (elapsed >= _shakeDuration)
                {
                    _shakeActive = false;
                    // 抖动结束，恢复位置
                    T.position = _shakeBasePosition;
                }
                else
                {
                    float decay = 1f - (elapsed / _shakeDuration);
                    float x = (Mathf.PerlinNoise(0f, elapsed * _shakeVibrato) * 2f - 1f) * _shakeStrength * decay;
                    float y = (Mathf.PerlinNoise(100f, elapsed * _shakeVibrato) * 2f - 1f) * _shakeStrength * decay;
                    T.position = _shakeBasePosition + new Vector2(x, y);
                }
            }

            // ---- 倾斜插值（倾斜目标 → 原始旋转） ----
            if (_tiltTargetRotations != null && !_isDragging)
            {
                float tt = 1f - Mathf.Exp(-_tiltSpeed * dt);
                bool tiltSettled = true;
                for (int i = 0; i < _tiltTargets.Count; i++)
                {
                    if (_tiltTargets[i] == null)
                        continue;
                    Vector3 cur = _tiltTargets[i].localRotation.eulerAngles;
                    float targetZ = _tiltTargetRotations[i];
                    if (Mathf.Abs(cur.z - targetZ) > 0.05f)
                    {
                        tiltSettled = false;
                        float newZ = Mathf.Lerp(cur.z, targetZ, tt);
                        _tiltTargets[i].localRotation = Quaternion.Euler(cur.x, cur.y, newZ);
                    }
                }
                if (!tiltSettled)
                    IsSettled = false;
            }

            // ---- T/VT 插值（位置、旋转、缩放） ----
            if (_isDragging || IsSettled || _smoothRectTarget == null)
                return;

            float posSpeed = _overridePositionSpeed > 0f ? _overridePositionSpeed : _positionSpeed;
            float pt = 1f - Mathf.Exp(-posSpeed * dt);
            float rt = 1f - Mathf.Exp(-_rotationSpeed * dt);
            float st = 1f - Mathf.Exp(-_scaleSpeed * dt);

            VT.position = Vector2.Lerp(VT.position, T.position, pt);
            VT.rotation = Mathf.Lerp(VT.rotation, T.rotation, rt);
            VT.scale = Vector2.Lerp(VT.scale, T.scale, st);

            CT = VT;
            VT.ApplyTo(_smoothRectTarget);

            IsSettled = VT.Approximately(T);
        }

        // ============================================================
        //  公开方法
        // ============================================================

        /// <summary>快照当前 RectTransform 到 T/VT，停止插值。</summary>
        public void SnapSmoothTransform()
        {
            InitSmoothTransform();
            T = Transform2D.FromRect(_smoothRectTarget);
            VT = T;
            CT = T;
            IsSettled = true;
        }

        /// <summary>强制 VT = T（跳过插值动画，直接到目标）。</summary>
        public void SnapToTarget()
        {
            InitSmoothTransform();
            VT = T;
            CT = T;
            if (_smoothRectTarget != null)
                VT.ApplyTo(_smoothRectTarget);
            IsSettled = true;
        }

        /// <summary>瞬间跳转到目标位置（无动画）。</summary>
        public void SetSmoothPositionImmediate(Vector2 pos)
        {
            InitSmoothTransform();
            T.position = pos;
            VT.position = pos;
            CT.position = pos;
            if (_smoothRectTarget != null)
                _smoothRectTarget.anchoredPosition = pos;
            IsSettled = true;
        }

        /// <summary>启动从 startPos 到 targetPos 的插值动画。</summary>
        public void AnimateSmoothTo(Vector2 targetPos, Vector2 startPos)
        {
            InitSmoothTransform();
            T.position = targetPos;
            T.scale = Vector2.one;
            VT.position = startPos;
            if (_smoothRectTarget != null)
                _smoothRectTarget.anchoredPosition = startPos;
            IsSettled = false;
        }

        /// <summary>设置 T 目标位置（VT 不变，Update 自动趋近）。</summary>
        public void SetTargetPosition(Vector2 targetPos)
        {
            InitSmoothTransform();
            T.position = targetPos;
            IsSettled = false;
        }

        /// <summary>
        /// 设置 T 目标位置并指定本次插值速度（用于弹出/复位等不同速度需求）。
        /// 速度会在下次 IsSettled 时自动重置为默认值。
        /// </summary>
        public void SetTargetPosition(Vector2 targetPos, float speed)
        {
            InitSmoothTransform();
            _overridePositionSpeed = speed;
            T.position = targetPos;
            IsSettled = false;
        }

        /// <summary>设置 T 目标缩放。</summary>
        public void SetTargetScale(Vector2 targetScale)
        {
            InitSmoothTransform();
            T.scale = targetScale;
            IsSettled = false;
        }

        /// <summary>播放抖动动画（程序化 Perlin 噪声，无 DOTween 依赖）。</summary>
        public void PlayShake(float strength, float duration, int vibrato, float randomness)
        {
            InitSmoothTransform();
            _shakeActive = true;
            _shakeStartTime = Time.time;
            _shakeDuration = duration;
            _shakeStrength = strength;
            _shakeVibrato = vibrato;
            _shakeRandomness = randomness;
            _shakeBasePosition = T.position;
            IsSettled = false;
        }

        // ============================================================
        //  拖拽事件
        // ============================================================

        public void OnPointerDown(PointerEventData eventData)
        {
            EnsureInitialized();

            _isDragging = true;
            _smoothedDelta = Vector3.zero;
            SnapSmoothTransform();          // 停止所有插值，快照当前位置
            _shakeActive = false;           // 中断抖动

            _originalPosition = _dragTarget.anchoredPosition;

            // 拖拽时放大 → 设 T.scale
            SetTargetScale(new Vector2(_scaleOnDrag, _scaleOnDrag));
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDragging || _dragTarget == null)
                return;

            Vector2 velocity = eventData.delta * _dragSensitivity;
            _dragTarget.anchoredPosition += velocity;

            // 平滑速度消除抖动
            _smoothedDelta = Vector3.Lerp(_smoothedDelta, velocity, _rotationSmoothSpeed * Time.deltaTime);
            ApplyTilt(_smoothedDelta);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isDragging = false;

            if (_dragTarget == null)
                return;

            // 缩放复位 → 设 T.scale = (1,1)，Update 自动插值
            SetTargetScale(Vector2.one);

            // 位置复位 → 设 T.position = 原始位置，VT 从当前开始插值
            Vector2 currentPos = _dragTarget.anchoredPosition;
            AnimateSmoothTo(_originalPosition, currentPos);

            // 倾斜复位 → 设倾斜目标的 targetZ = 原始 Z
            if (_tiltTargetRotations != null)
            {
                for (int i = 0; i < _tiltTargets.Count; i++)
                {
                    if (_tiltTargets[i] != null && i < _originalTiltRotations.Count)
                        _tiltTargetRotations[i] = _originalTiltRotations[i].z;
                }
            }
        }

        // ============================================================
        //  倾斜
        // ============================================================

        private void ApplyTilt(Vector3 smoothedVelocity)
        {
            if (_tiltTargets == null || _tiltTargets.Count == 0)
                return;
            if (_tiltTargetRotations == null)
                return;

            float tiltZ = Mathf.Clamp(
                -smoothedVelocity.x * _rotationAmount,
                -_maxTiltAngle,
                _maxTiltAngle
            );

            // 设置每帧的倾斜目标 Z（Update 中的倾斜插值会平滑趋近）
            for (int i = 0; i < _tiltTargets.Count; i++)
            {
                if (_tiltTargets[i] == null || i >= _originalTiltRotations.Count)
                    continue;
                _tiltTargetRotations[i] = _originalTiltRotations[i].z + tiltZ;
            }

            // 拖拽中直接设置旋转（无插值延迟，跟手）
            for (int i = 0; i < _tiltTargets.Count; i++)
            {
                if (_tiltTargets[i] == null || i >= _originalTiltRotations.Count)
                    continue;
                Vector3 orig = _originalTiltRotations[i];
                _tiltTargets[i].localRotation = Quaternion.Euler(orig.x, orig.y, orig.z + tiltZ);
            }
        }

        // ============================================================
        //  初始化
        // ============================================================

        protected void EnsureInitialized()
        {
            if (_dragTarget == null)
                _dragTarget = GetComponent<RectTransform>();

            if (_tiltTargets == null || _tiltTargets.Count == 0)
            {
                _tiltTargets = new System.Collections.Generic.List<Transform>();

                if (_tiltParent != null)
                {
                    _tiltTargets.Add(_tiltParent);
                }
                else if (_dragTarget != null && _dragTarget.childCount > 0)
                {
                    for (int i = 0; i < _dragTarget.childCount; i++)
                    {
                        Transform child = _dragTarget.GetChild(i);
                        if (child != null)
                            _tiltTargets.Add(child);
                    }
                }

                _originalTiltRotations = new System.Collections.Generic.List<Vector3>(_tiltTargets.Count);
                _tiltTargetRotations = new System.Collections.Generic.List<float>(_tiltTargets.Count);
                for (int i = 0; i < _tiltTargets.Count; i++)
                {
                    Vector3 euler = _tiltTargets[i].localRotation.eulerAngles;
                    _originalTiltRotations.Add(euler);
                    _tiltTargetRotations.Add(euler.z);
                }
            }

            InitSmoothTransform();
        }
    }
}
