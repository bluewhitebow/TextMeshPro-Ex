using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


namespace TMPro
{
    /// <summary>
    /// Groups TMP UGUI drawing as: all atlas outline passes, then all atlas fill passes.
    /// Atlas 0 is moved off the parent CanvasRenderer so sibling order can own the whole stack.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMeshProUGUI))]
    [AddComponentMenu("UI/TextMeshPro - Grouped Outline (UI)", 12)]
    public class TMP_GroupedOutlineRenderer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Outer-pass outline material template. Atlas texture and SDF metrics are copied per material index.")]
        Material m_OutlineMaterial;

        [SerializeField]
        [Tooltip("When disabled, native TMP CanvasRenderer / SubMeshUI drawing is restored.")]
        bool m_EnableOutlinePass = true;

        TextMeshProUGUI m_Text;
        readonly List<TMP_TextPassUI> m_OutlinePasses = new List<TMP_TextPassUI>();
        readonly List<TMP_TextPassUI> m_FillPasses = new List<TMP_TextPassUI>();
        readonly Dictionary<int, Material> m_OutlineMaterialCache = new Dictionary<int, Material>();
        readonly List<int> m_ScratchMaterialIds = new List<int>();

        bool m_Registered;
        bool m_PassesHaveGeometry;


        public Material outlineMaterial
        {
            get { return m_OutlineMaterial; }
            set
            {
                if (m_OutlineMaterial == value)
                    return;

                m_OutlineMaterial = value;
                ReleaseOutlineMaterials();
                ApplySuppressState(true);
            }
        }

        public bool enableOutlinePass
        {
            get { return m_EnableOutlinePass; }
            set
            {
                if (m_EnableOutlinePass == value)
                    return;

                m_EnableOutlinePass = value;
                ApplySuppressState(true);
            }
        }

        bool IsFeatureActive
        {
            get { return isActiveAndEnabled && m_EnableOutlinePass && m_OutlineMaterial != null && m_Text != null; }
        }


        void Awake()
        {
            m_Text = GetComponent<TextMeshProUGUI>();
        }


        void OnEnable()
        {
            if (m_Text == null)
                m_Text = GetComponent<TextMeshProUGUI>();

            Register();
            ApplySuppressState(true);
        }


        void OnDisable()
        {
            Unregister();
            ClearAllPassMeshes();
            SetPassesEnabled(false);

            if (m_Text != null)
            {
                m_Text.suppressNativeCanvasMesh = false;
                m_Text.SetVerticesDirty();
            }
        }


        void OnDestroy()
        {
            Unregister();
            DestroyPasses();
            ReleaseOutlineMaterials();

            if (m_Text != null)
                m_Text.suppressNativeCanvasMesh = false;
        }


#if UNITY_EDITOR
        void OnValidate()
        {
            if (m_Text == null)
                m_Text = GetComponent<TextMeshProUGUI>();

            if (!isActiveAndEnabled || m_Text == null)
                return;

            ReleaseOutlineMaterials();
            ApplySuppressState(true);
        }
#endif


        void Register()
        {
            if (m_Registered || m_Text == null)
                return;

            m_Text.OnPreRenderText += OnPreRenderText;
            m_Text.OnMeshUploaded += OnMeshUploaded;
            m_Text.onCullStateChanged.AddListener(OnParentCullChanged);
            Canvas.willRenderCanvases += SyncRendererState;
            m_Registered = true;
        }


        void Unregister()
        {
            if (!m_Registered)
                return;

            if (m_Text != null)
            {
                m_Text.OnPreRenderText -= OnPreRenderText;
                m_Text.OnMeshUploaded -= OnMeshUploaded;
                m_Text.onCullStateChanged.RemoveListener(OnParentCullChanged);
            }

            Canvas.willRenderCanvases -= SyncRendererState;
            m_Registered = false;
        }


        void ApplySuppressState(bool rebuild)
        {
            if (m_Text == null)
                return;

            bool wantSuppress = IsFeatureActive;
            m_Text.suppressNativeCanvasMesh = wantSuppress;

            if (!wantSuppress)
            {
                ClearAllPassMeshes();
                SetPassesEnabled(false);
            }

            if (rebuild)
            {
                m_Text.havePropertiesChanged = true;
                m_Text.SetVerticesDirty();
            }
        }


        void OnPreRenderText(TMP_TextInfo info)
        {
            if (!IsFeatureActive || info == null)
                return;

            // Create pass Graphics before CanvasRenderer.SetMesh so they exist in this PreRender.
            EnsurePassCount(Mathf.Max(info.materialCount, 0));
            SetPassesEnabled(true);
            ReorderPasses(info.materialCount);
        }


        void OnMeshUploaded(TMP_Text tmp)
        {
            if (tmp != m_Text)
                return;

            if (!IsFeatureActive)
            {
                ClearAllPassMeshes();
                SetPassesEnabled(false);
                return;
            }

            if (!m_Text.IsActive())
            {
                ClearAllPassMeshes();
                SetPassesEnabled(false);
                return;
            }

            TMP_TextInfo info = m_Text.textInfo;
            int materialCount = info == null ? 0 : info.materialCount;
            bool hasGeometry = false;

            if (info != null && info.meshInfo != null)
            {
                for (int i = 0; i < materialCount && i < info.meshInfo.Length; i++)
                {
                    if (info.meshInfo[i].vertexCount > 0)
                    {
                        hasGeometry = true;
                        break;
                    }
                }
            }

            if (!hasGeometry)
            {
                ClearAllPassMeshes();
                return;
            }

            EnsurePassCount(materialCount);
            SetPassesEnabled(true);

            Color rendererColor = m_Text.canvasRenderer.GetColor();
            bool cullTransparent = m_Text.canvasRenderer.cullTransparentMesh;
            bool cull = m_Text.canvasRenderer.cull;
            bool parentMaskable = m_Text.maskable;

            m_ScratchMaterialIds.Clear();

            for (int i = 0; i < materialCount; i++)
            {
                TMP_MeshInfo meshInfo = info.meshInfo[i];
                Mesh mesh = meshInfo.mesh;
                Material fillMaterial = meshInfo.material;
                bool draw = meshInfo.vertexCount > 0 && mesh != null && fillMaterial != null;

                TMP_TextPassUI outlinePass = m_OutlinePasses[i];
                TMP_TextPassUI fillPass = m_FillPasses[i];

                outlinePass.maskable = parentMaskable;
                fillPass.maskable = parentMaskable;

                if (!draw)
                {
                    outlinePass.ClearDraw();
                    fillPass.ClearDraw();
                    continue;
                }

                fillPass.ApplyDraw(mesh, fillMaterial, rendererColor, cullTransparent, cull);

                if (CanUseOutlinePass(fillMaterial))
                {
                    Material outlineMat = GetOutlineMaterial(fillMaterial);
                    m_ScratchMaterialIds.Add(fillMaterial.GetInstanceID());
                    outlinePass.ApplyDraw(mesh, outlineMat, rendererColor, cullTransparent, cull);
                }
                else
                {
                    outlinePass.ClearDraw();
                }
            }

            for (int i = materialCount; i < m_OutlinePasses.Count; i++)
            {
                m_OutlinePasses[i].ClearDraw();
                m_FillPasses[i].ClearDraw();
            }

            PruneOutlineMaterials();
            ReorderPasses(materialCount);
            m_PassesHaveGeometry = true;
        }


        void SyncRendererState()
        {
            if (!IsFeatureActive || m_Text == null || !m_Text.IsActive())
            {
                if (m_PassesHaveGeometry)
                {
                    ClearAllPassMeshes();
                    SetPassesEnabled(false);
                }

                return;
            }

            if (!m_PassesHaveGeometry)
                return;

            Color rendererColor = m_Text.canvasRenderer.GetColor();
            bool cullTransparent = m_Text.canvasRenderer.cullTransparentMesh;
            bool cull = m_Text.canvasRenderer.cull;

            for (int i = 0; i < m_FillPasses.Count; i++)
            {
                SyncPassRendererState(m_OutlinePasses[i], rendererColor, cullTransparent, cull);
                SyncPassRendererState(m_FillPasses[i], rendererColor, cullTransparent, cull);
            }
        }


        static void SyncPassRendererState(TMP_TextPassUI pass, Color rendererColor, bool cullTransparent, bool cull)
        {
            if (pass == null)
                return;

            pass.canvasRenderer.SetColor(rendererColor);
            pass.canvasRenderer.cullTransparentMesh = cullTransparent;
            pass.canvasRenderer.cull = cull;
        }


        void OnParentCullChanged(bool culled)
        {
            for (int i = 0; i < m_FillPasses.Count; i++)
            {
                if (m_OutlinePasses[i] != null)
                    m_OutlinePasses[i].canvasRenderer.cull = culled;
                if (m_FillPasses[i] != null)
                    m_FillPasses[i].canvasRenderer.cull = culled;
            }
        }


        void EnsurePassCount(int materialCount)
        {
            while (m_OutlinePasses.Count < materialCount)
            {
                int index = m_OutlinePasses.Count;
                m_OutlinePasses.Add(CreatePass("TMP Pass Outline [" + index + "]"));
                m_FillPasses.Add(CreatePass("TMP Pass Fill [" + index + "]"));
            }

            for (int i = 0; i < materialCount; i++)
            {
                if (m_OutlinePasses[i] == null)
                    m_OutlinePasses[i] = CreatePass("TMP Pass Outline [" + i + "]");
                if (m_FillPasses[i] == null)
                    m_FillPasses[i] = CreatePass("TMP Pass Fill [" + i + "]");
            }
        }


        TMP_TextPassUI CreatePass(string objectName)
        {
            return TMP_TextPassUI.Create(m_Text, objectName);
        }


        void ReorderPasses(int materialCount)
        {
            int sibling = 0;

            for (int i = 0; i < materialCount; i++)
                SetSibling(m_OutlinePasses[i], ref sibling);

            for (int i = 0; i < materialCount; i++)
                SetSibling(m_FillPasses[i], ref sibling);
        }


        static void SetSibling(TMP_TextPassUI pass, ref int sibling)
        {
            if (pass == null)
                return;

            if (pass.transform.GetSiblingIndex() != sibling)
                pass.transform.SetSiblingIndex(sibling);

            sibling++;
        }


        void SetPassesEnabled(bool enabled)
        {
            for (int i = 0; i < m_OutlinePasses.Count; i++)
            {
                SetPassEnabled(m_OutlinePasses[i], enabled);
                SetPassEnabled(m_FillPasses[i], enabled);
            }
        }


        static void SetPassEnabled(TMP_TextPassUI pass, bool enabled)
        {
            if (pass != null && pass.enabled != enabled)
                pass.enabled = enabled;
        }


        void ClearAllPassMeshes()
        {
            for (int i = 0; i < m_OutlinePasses.Count; i++)
            {
                if (m_OutlinePasses[i] != null)
                    m_OutlinePasses[i].ClearDraw();
                if (m_FillPasses[i] != null)
                    m_FillPasses[i].ClearDraw();
            }

            m_PassesHaveGeometry = false;
        }


        void DestroyPasses()
        {
            DestroyPassList(m_OutlinePasses);
            DestroyPassList(m_FillPasses);
            m_PassesHaveGeometry = false;
        }


        static void DestroyPassList(List<TMP_TextPassUI> passes)
        {
            for (int i = 0; i < passes.Count; i++)
            {
                TMP_TextPassUI pass = passes[i];
                if (pass == null)
                    continue;

                DestroyImmediate(pass.gameObject);
            }

            passes.Clear();
        }


        static bool CanUseOutlinePass(Material source)
        {
            ShaderUtilities.GetShaderPropertyIDs();

            return source != null
                && source.HasProperty(ShaderUtilities.ID_MainTex)
                && source.HasProperty(ShaderUtilities.ID_GradientScale)
                && source.GetTexture(ShaderUtilities.ID_MainTex) != null;
        }


        Material GetOutlineMaterial(Material source)
        {
            ShaderUtilities.GetShaderPropertyIDs();

            int id = source.GetInstanceID();
            Material instance;
            if (!m_OutlineMaterialCache.TryGetValue(id, out instance) || instance == null)
            {
                instance = new Material(m_OutlineMaterial);
                instance.hideFlags = HideFlags.HideAndDontSave;
#if UNITY_EDITOR
                instance.name = m_OutlineMaterial.name + " + " + source.name;
#endif
                m_OutlineMaterialCache[id] = instance;
            }

            CopyAtlasProperties(source, instance);
            return instance;
        }


        static void CopyAtlasProperties(Material source, Material dest)
        {
            CopyTexture(source, dest, ShaderUtilities.ID_MainTex);
            CopyFloat(source, dest, ShaderUtilities.ID_TextureWidth);
            CopyFloat(source, dest, ShaderUtilities.ID_TextureHeight);
            CopyFloat(source, dest, ShaderUtilities.ID_GradientScale);
            CopyFloat(source, dest, ShaderUtilities.ID_WeightNormal);
            CopyFloat(source, dest, ShaderUtilities.ID_WeightBold);
            CopyFloat(source, dest, ShaderUtilities.ID_ScaleX);
            CopyFloat(source, dest, ShaderUtilities.ID_ScaleY);
            CopyFloat(source, dest, ShaderUtilities.ID_PerspectiveFilter);
            CopyFloat(source, dest, ShaderUtilities.ID_Sharpness);
            CopyFloat(source, dest, ShaderUtilities.ID_ScaleRatio_A);
            CopyFloat(source, dest, ShaderUtilities.ID_ScaleRatio_B);
            CopyFloat(source, dest, ShaderUtilities.ID_ScaleRatio_C);

            if (source.HasProperty(ShaderUtilities.ShaderTag_CullMode) && dest.HasProperty(ShaderUtilities.ShaderTag_CullMode))
                dest.SetFloat(ShaderUtilities.ShaderTag_CullMode, source.GetFloat(ShaderUtilities.ShaderTag_CullMode));
        }


        static void CopyTexture(Material source, Material dest, int id)
        {
            if (source.HasProperty(id) && dest.HasProperty(id))
                dest.SetTexture(id, source.GetTexture(id));
        }


        static void CopyFloat(Material source, Material dest, int id)
        {
            if (source.HasProperty(id) && dest.HasProperty(id))
                dest.SetFloat(id, source.GetFloat(id));
        }


        void PruneOutlineMaterials()
        {
            if (m_OutlineMaterialCache.Count == 0)
                return;

            List<int> remove = null;
            foreach (var kvp in m_OutlineMaterialCache)
            {
                if (m_ScratchMaterialIds.Contains(kvp.Key))
                    continue;

                if (remove == null)
                    remove = new List<int>();
                remove.Add(kvp.Key);
            }

            if (remove == null)
                return;

            for (int i = 0; i < remove.Count; i++)
            {
                Material mat;
                if (m_OutlineMaterialCache.TryGetValue(remove[i], out mat))
                {
                    if (mat != null)
                        DestroyImmediate(mat);
                }

                m_OutlineMaterialCache.Remove(remove[i]);
            }
        }


        void ReleaseOutlineMaterials()
        {
            foreach (var kvp in m_OutlineMaterialCache)
            {
                if (kvp.Value == null)
                    continue;

                DestroyImmediate(kvp.Value);
            }

            m_OutlineMaterialCache.Clear();
        }
    }
}
