using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;


namespace TMPro
{
    /// <summary>
    /// Lightweight Canvas Graphic used by <see cref="TMP_GroupedOutlineRenderer"/> for one atlas / one pass.
    /// Geometry is pushed from the parent TMP; this object must not dirty the parent text.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public class TMP_TextPassUI : MaskableGraphic
    {
        public override Texture mainTexture
        {
            get
            {
                if (m_sharedMaterial != null)
                    return m_sharedMaterial.GetTexture(ShaderUtilities.ID_MainTex);

                return null;
            }
        }

        public override Material material
        {
            get { return m_sharedMaterial; }
            set { sharedMaterial = value; }
        }

        public Material sharedMaterial
        {
            get { return m_sharedMaterial; }
            set
            {
                if (m_sharedMaterial == value)
                    return;

                m_sharedMaterial = value;
                SetMaterialDirty();
            }
        }
        [SerializeField]
        Material m_sharedMaterial;

        public override Material materialForRendering
        {
            get { return TMP_MaterialManager.GetMaterialForRendering(this, m_sharedMaterial); }
        }

        public TextMeshProUGUI textComponent
        {
            get { return m_TextComponent; }
        }
        TextMeshProUGUI m_TextComponent;


        public static TMP_TextPassUI Create(TextMeshProUGUI textComponent, string objectName)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform));
            go.hideFlags = HideFlags.DontSave;
            go.layer = textComponent.gameObject.layer;
            go.transform.SetParent(textComponent.transform, false);

            RectTransform rectTransform = go.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.pivot = textComponent.rectTransform.pivot;

            LayoutElement layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            TMP_TextPassUI pass = go.AddComponent<TMP_TextPassUI>();
            pass.m_TextComponent = textComponent;
            pass.raycastTarget = false;
            return pass;
        }


        protected override void OnEnable()
        {
            base.OnEnable();

            hideFlags = HideFlags.DontSave;
            raycastTarget = false;

            m_ShouldRecalculateStencil = true;
            RecalculateClipping();
            RecalculateMasking();
        }


        protected override void OnDisable()
        {
            base.OnDisable();
        }


        protected override void OnTransformParentChanged()
        {
            if (!IsActive())
                return;

            m_ShouldRecalculateStencil = true;
            RecalculateClipping();
            RecalculateMasking();
        }


        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            Material mat = baseMaterial;

            if (m_ShouldRecalculateStencil)
            {
                var rootCanvas = MaskUtilities.FindRootSortOverrideCanvas(transform);
                m_StencilValue = maskable ? MaskUtilities.GetStencilDepth(transform, rootCanvas) : 0;
                m_ShouldRecalculateStencil = false;
            }

            if (m_StencilValue > 0)
            {
                Material maskMat = StencilMaterial.Add(mat, (1 << m_StencilValue) - 1, StencilOp.Keep, CompareFunction.Equal, ColorWriteMask.All, (1 << m_StencilValue) - 1, 0);
                StencilMaterial.Remove(m_MaskMaterial);
                m_MaskMaterial = maskMat;
                mat = m_MaskMaterial;
            }

            return mat;
        }


        public override void SetAllDirty() { }

        public override void SetVerticesDirty() { }

        public override void SetLayoutDirty() { }

        public override void SetMaterialDirty()
        {
            UpdateMaterial();
        }


        public override void Cull(Rect clipRect, bool validRect)
        {
            // Parent TextMeshProUGUI drives culling for all pass renderers.
        }


        protected override void UpdateGeometry() { }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }


        public override void Rebuild(CanvasUpdate update)
        {
            if (update == CanvasUpdate.PreRender)
                UpdateMaterial();
        }


        protected override void UpdateMaterial()
        {
            if (m_sharedMaterial == null)
                return;

            if (m_TextComponent != null && m_sharedMaterial.HasProperty(ShaderUtilities.ShaderTag_CullMode) && m_TextComponent.fontSharedMaterial != null && m_TextComponent.fontSharedMaterial.HasProperty(ShaderUtilities.ShaderTag_CullMode))
                m_sharedMaterial.SetFloat(ShaderUtilities.ShaderTag_CullMode, m_TextComponent.fontSharedMaterial.GetFloat(ShaderUtilities.ShaderTag_CullMode));

            canvasRenderer.materialCount = 1;
            canvasRenderer.SetMaterial(materialForRendering, 0);
        }


        internal void SyncPivot()
        {
            if (m_TextComponent == null)
                return;

            if (rectTransform.pivot != m_TextComponent.rectTransform.pivot)
                rectTransform.pivot = m_TextComponent.rectTransform.pivot;
        }


        internal void ApplyDraw(Mesh mesh, Material mat, Color rendererColor, bool cullTransparentMesh, bool cull)
        {
            if (mat != m_sharedMaterial)
            {
                m_sharedMaterial = mat;
                SetMaterialDirty();
            }

            SyncPivot();

            if (gameObject.layer != m_TextComponent.gameObject.layer)
                gameObject.layer = m_TextComponent.gameObject.layer;

            canvasRenderer.SetMesh(mesh);
            canvasRenderer.SetColor(rendererColor);
            canvasRenderer.cullTransparentMesh = cullTransparentMesh;
            canvasRenderer.cull = cull;
        }


        internal void ClearDraw()
        {
            if (canvasRenderer != null)
                canvasRenderer.SetMesh(null);
        }
    }
}
