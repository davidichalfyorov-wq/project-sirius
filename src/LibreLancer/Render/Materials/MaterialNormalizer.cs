namespace LibreLancer.Render.Materials;

/// <summary>
/// Physically-plausible defaults for MAT inputs (graphics roadmap 5.2).
/// Freelancer-era materials rarely author metallic/roughness factors;
/// the raw fallbacks (both = 1) read as polished chrome under a real
/// BRDF. Explicit factors and texture-driven channels stay untouched.
/// </summary>
public static class MaterialNormalizer
{
    /// <summary>Default metallic: 0 unless an Mt map or factor says otherwise.</summary>
    public static float Metallic(float? factor, bool hasMetallicMap) =>
        factor ?? (hasMetallicMap ? 1.0f : 0.0f);

    /// <summary>Default roughness: classic painted hulls sit around 0.65.</summary>
    public static float Roughness(float? factor, bool hasRoughnessMap) =>
        factor ?? (hasRoughnessMap ? 1.0f : 0.65f);
}
