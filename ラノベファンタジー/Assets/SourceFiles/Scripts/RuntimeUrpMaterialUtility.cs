using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ランタイム生成プリミティブ向け URP マテリアル適用。
/// CreatePrimitive の既定 Standard シェーダーは URP ビルドで欠落しピンクになるため、
/// Resources 内テンプレートまたは URP Lit へ差し替えます。
/// </summary>
public static class RuntimeUrpMaterialUtility
{
    private const string OpaqueTemplateResourcePath = "RuntimeMaterials/UrpLitOpaque";
    private const string TransparentTemplateResourcePath = "RuntimeMaterials/UrpLitTransparent";

    private static Material opaqueTemplate;
    private static Material transparentTemplate;
    private static Shader cachedLitShader;

    /// <summary>不透明 URP Lit マテリアルを Renderer に適用します。</summary>
    public static void ApplyOpaqueColor(Renderer renderer, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        Material material = CreateOpaqueMaterial(color);
        if (material != null)
        {
            renderer.material = material;
        }
    }

    /// <summary>半透明 URP Lit マテリアルを Renderer に適用します。</summary>
    public static void ApplyTransparentColor(Renderer renderer, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        Material material = CreateTransparentMaterial(color);
        if (material != null)
        {
            renderer.material = material;
        }
    }

    /// <summary>不透明色付きマテリアルインスタンスを生成します。</summary>
    public static Material CreateOpaqueMaterial(Color color)
    {
        Material material = InstantiateFromTemplate(ref opaqueTemplate, OpaqueTemplateResourcePath, transparent: false);
        if (material == null)
        {
            return null;
        }

        SetBaseColor(material, color);
        return material;
    }

    /// <summary>半透明色付きマテリアルインスタンスを生成します。</summary>
    public static Material CreateTransparentMaterial(Color color)
    {
        Material material = InstantiateFromTemplate(ref transparentTemplate, TransparentTemplateResourcePath, transparent: true);
        if (material == null)
        {
            return null;
        }

        SetBaseColor(material, color);
        return material;
    }

    private static Material InstantiateFromTemplate(ref Material cachedTemplate, string resourcePath, bool transparent)
    {
        if (cachedTemplate == null)
        {
            Material loaded = Resources.Load<Material>(resourcePath);
            if (loaded != null)
            {
                cachedTemplate = loaded;
            }
            else
            {
                Shader shader = ResolveLitShader();
                if (shader == null)
                {
                    Debug.LogWarning(
                        "[RuntimeUrpMaterialUtility] URP Lit シェーダーを解決できません。ピンク表示の可能性があります。");
                    return null;
                }

                cachedTemplate = new Material(shader);
                if (transparent)
                {
                    ConfigureUrpTransparent(cachedTemplate);
                }
            }
        }

        return new Material(cachedTemplate);
    }

    private static Shader ResolveLitShader()
    {
        if (cachedLitShader != null)
        {
            return cachedLitShader;
        }

        string[] shaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit"
        };

        for (int i = 0; i < shaderNames.Length; i++)
        {
            Shader shader = Shader.Find(shaderNames[i]);
            if (shader != null)
            {
                cachedLitShader = shader;
                return cachedLitShader;
            }
        }

        return null;
    }

    private static void ConfigureUrpTransparent(Material material)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.renderQueue = (int)RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetOverrideTag("RenderType", "Transparent");
    }

    private static void SetBaseColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }
}
