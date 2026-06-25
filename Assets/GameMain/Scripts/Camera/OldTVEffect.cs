using UnityEngine;

namespace Yuu
{
    /// <summary>
    /// 摄像机旧电视 / CRT 后处理效果。
    /// 通过 OnRenderImage 对摄像机画面应用扫描线、暗角、色差、噪点、闪烁等复古效果。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    [ImageEffectAllowedInSceneView]
    public class OldTVEffect : MonoBehaviour
    {
        [Header("全局")]
        [SerializeField, Range(0f, 1f)]
        private float m_EffectStrength = 1f;

        [Header("扫描线")]
        [SerializeField, Range(0f, 1f)]
        private float m_ScanlineIntensity = 0.6f;
        [SerializeField, Range(10f, 500f)]
        private float m_ScanlineCount = 200f;
        [SerializeField, Range(0f, 0.5f)]
        private float m_ScanlineSpeed = 0.08f;

        [Header("暗角")]
        [SerializeField, Range(0f, 1f)]
        private float m_VignetteIntensity = 0.7f;

        [Header("色差")]
        [SerializeField, Range(0f, 1f)]
        private float m_AberrationIntensity = 0.25f;

        [Header("噪点")]
        [SerializeField, Range(0f, 1f)]
        private float m_NoiseIntensity = 0.15f;

        [Header("屏幕曲率")]
        [SerializeField, Range(0f, 1f)]
        private float m_DistortionIntensity = 0.08f;

        [Header("颜色")]
        [SerializeField, Range(0f, 2f)]
        private float m_Saturation = 0.5f;
        [SerializeField, Range(0f, 1f)]
        private float m_WarmthShift = 0.25f;

        [Header("闪烁")]
        [SerializeField, Range(0f, 1f)]
        private float m_FlickerIntensity = 0.06f;

        private Material m_Material;
        private bool m_LoggedOnce;
        private static readonly int k_ShaderPropEffectStrength = Shader.PropertyToID("_EffectStrength");
        private static readonly int k_ShaderPropScanlineIntensity = Shader.PropertyToID("_ScanlineIntensity");
        private static readonly int k_ShaderPropScanlineCount = Shader.PropertyToID("_ScanlineCount");
        private static readonly int k_ShaderPropScanlineSpeed = Shader.PropertyToID("_ScanlineSpeed");
        private static readonly int k_ShaderPropVignetteIntensity = Shader.PropertyToID("_VignetteIntensity");
        private static readonly int k_ShaderPropAberrationIntensity = Shader.PropertyToID("_AberrationIntensity");
        private static readonly int k_ShaderPropNoiseIntensity = Shader.PropertyToID("_NoiseIntensity");
        private static readonly int k_ShaderPropDistortionIntensity = Shader.PropertyToID("_DistortionIntensity");
        private static readonly int k_ShaderPropSaturation = Shader.PropertyToID("_Saturation");
        private static readonly int k_ShaderPropFlickerIntensity = Shader.PropertyToID("_FlickerIntensity");
        private static readonly int k_ShaderPropWarmthShift = Shader.PropertyToID("_WarmthShift");

        private Material Material
        {
            get
            {
                if (m_Material == null)
                {
                    Shader shader = Shader.Find("Camera/OldTV");
                    if (shader != null)
                    {
                        m_Material = new Material(shader);
                        m_Material.hideFlags = HideFlags.DontSave;
                    }
                    else
                    {
                        Debug.LogWarning("[OldTVEffect] 找不到 Camera/OldTV 着色器，请确认 Shader 文件存在。");
                    }
                }
                return m_Material;
            }
        }

        private void Start()
        {
            if (!m_LoggedOnce)
            {
                Debug.Log($"[OldTVEffect] Start called. Shader found: {Shader.Find("Camera/OldTV") != null}, Material: {m_Material != null}");
                m_LoggedOnce = true;
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!m_LoggedOnce)
            {
                Debug.Log($"[OldTVEffect] OnRenderImage called! source={source?.width}x{source?.height}, Material={m_Material != null}");
                m_LoggedOnce = true;
            }

            if (Material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            ApplyProperties();
            Graphics.Blit(source, destination, Material, 0);
        }

        private void ApplyProperties()
        {
            if (m_Material == null) return;

            m_Material.SetFloat(k_ShaderPropEffectStrength, m_EffectStrength);
            m_Material.SetFloat(k_ShaderPropScanlineIntensity, m_ScanlineIntensity);
            m_Material.SetFloat(k_ShaderPropScanlineCount, m_ScanlineCount);
            m_Material.SetFloat(k_ShaderPropScanlineSpeed, m_ScanlineSpeed);
            m_Material.SetFloat(k_ShaderPropVignetteIntensity, m_VignetteIntensity);
            m_Material.SetFloat(k_ShaderPropAberrationIntensity, m_AberrationIntensity);
            m_Material.SetFloat(k_ShaderPropNoiseIntensity, m_NoiseIntensity);
            m_Material.SetFloat(k_ShaderPropDistortionIntensity, m_DistortionIntensity);
            m_Material.SetFloat(k_ShaderPropSaturation, m_Saturation);
            m_Material.SetFloat(k_ShaderPropFlickerIntensity, m_FlickerIntensity);
            m_Material.SetFloat(k_ShaderPropWarmthShift, m_WarmthShift);
        }

        private void OnDisable()
        {
            if (m_Material != null)
            {
                if (Application.isPlaying)
                    Destroy(m_Material);
                else
                    DestroyImmediate(m_Material);
                m_Material = null;
            }
        }
    }
}
