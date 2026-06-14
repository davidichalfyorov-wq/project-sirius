using System.Diagnostics.CodeAnalysis;
using LibreLancer.Graphics;

namespace LibreLancer.Shaders;

public static class AllShaders
{
    private static bool iscompiled = false;

    public static ShaderBundle AsteroidBand = null!;
    public static ShaderBundle Atmosphere = null!;
    public static ShaderBundle Basic_PositionColor = null!;
    public static ShaderBundle Basic_FVF = null!;
    public static ShaderBundle Basic_PositionTexture = null!;
    public static ShaderBundle Basic_Skinned = null!;
    public static ShaderBundle Billboard = null!;
    public static ShaderBundle BloomExtract = null!;
    public static ShaderBundle BloomDownsample = null!;
    public static ShaderBundle BloomUpsample = null!;
    public static ShaderBundle BrdfLut = null!;
    public static ShaderBundle DetailMap2Dm1Msk2PassMaterial = null!;
    public static ShaderBundle DetailMapMaterial = null!;
    public static ShaderBundle Fxaa = null!;
    public static ShaderBundle SmaaEdges = null!;
    public static ShaderBundle SmaaWeights = null!;
    public static ShaderBundle SmaaBlend = null!;
    public static ShaderBundle RTDebug = null!;
    // Mesh-pipeline bundles are SPIR-V-only: loaded behind the feature gate.
    public static ShaderBundle? MSDebug;
    public static ShaderBundle? CubeField;
    // Compute bundles are SPIR-V-only: loaded behind GraphicsFeature.Compute.
    public static ShaderBundle? ComputeSmoke;
    public static ShaderBundle? Texture3DVis;
    public static ShaderBundle? FroxelClear;
    public static ShaderBundle? FroxelDebugSlice;
    public static ShaderBundle? FroxelInject;
    public static ShaderBundle? FroxelLight;
    public static ShaderBundle? FroxelDisplacement;
    public static ShaderBundle? FroxelDisplacementHistory;
    public static ShaderBundle? FroxelWakeCurl;
    public static ShaderBundle? FroxelLightning;
    public static ShaderBundle? FroxelIntegrate;
    public static ShaderBundle? FroxelTemporal;
    public static ShaderBundle? FroxelComposite;
    // Volumetric nebulae (phase 5 track V): froxel chain + composite.
    public static ShaderBundle GodRaysMask = null!;
    public static ShaderBundle GodRaysBlur = null!;
    public static ShaderBundle IllumDetailMapMaterial = null!;
    public static ShaderBundle Masked2DetailMapMaterial = null!;
    public static ShaderBundle Navmap = null!;
    public static ShaderBundle NebulaExtPuff = null!;
    public static ShaderBundle NebulaInterior = null!;
    public static ShaderBundle NebulaMaterial = null!;
    public static ShaderBundle Nomad = null!;
    public static ShaderBundle Particle = null!;
    public static ShaderBundle ParticleBeam = null!;
    public static ShaderBundle PBR = null!;
    public static ShaderBundle PhysicsDebug = null!;
    public static ShaderBundle ShadowCaster = null!;
    public static ShaderBundle Sprite = null!;
    public static ShaderBundle StarsphereCubemap = null!;
    public static ShaderBundle SunRadial = null!;
    public static ShaderBundle SunSpine = null!;
    public static ShaderBundle Tonemap = null!;
    public static ShaderBundle ZoneVolume = null!;

    // ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract

    public static void CompileBillboard(RenderContext context) =>
        Billboard ??= Compile(context, "Billboard");

    public static void CompilePhysicsDebug(RenderContext context) =>
        PhysicsDebug ??= Compile(context, "PhysicsDebug");

    public static void Compile(RenderContext context)
    {
        if (iscompiled)
        {
            return;
        }

        iscompiled = true;

        FLLog.Debug("Shaders", "Compiling Game shaders");

        var before = Shader.TotalShaders;

        AsteroidBand ??= Compile(context, "AsteroidBand");
        Atmosphere ??= Compile(context, "Atmosphere");
        Basic_PositionColor ??= Compile(context, "Basic_PositionColor");
        Basic_FVF ??= Compile(context, "Basic_FVF");
        Basic_PositionTexture ??= Compile(context, "Basic_PositionTexture");
        Basic_Skinned ??= Compile(context, "Basic_Skinned");
        Billboard ??= Compile(context, "Billboard");
        BloomExtract ??= Compile(context, "BloomExtract");
        BloomDownsample ??= Compile(context, "BloomDownsample");
        BloomUpsample ??= Compile(context, "BloomUpsample");
        BrdfLut ??= Compile(context, "BrdfLut");
        DetailMap2Dm1Msk2PassMaterial ??= Compile(context, "DetailMap2Dm1Msk2PassMaterial");
        DetailMapMaterial ??= Compile(context, "DetailMapMaterial");
        Fxaa ??= Compile(context, "Fxaa");
        SmaaEdges ??= Compile(context, "SmaaEdges");
        SmaaWeights ??= Compile(context, "SmaaWeights");
        SmaaBlend ??= Compile(context, "SmaaBlend");
        RTDebug ??= Compile(context, "RTDebug");
        if (context.HasFeature(GraphicsFeature.MeshShaders))
        {
            MSDebug ??= Compile(context, "MSDebug");
            CubeField ??= Compile(context, "CubeField");
        }
        if (context.HasFeature(GraphicsFeature.Compute))
        {
            ComputeSmoke ??= Compile(context, "ComputeSmoke");
            Texture3DVis ??= Compile(context, "Texture3DVis");
            FroxelClear ??= Compile(context, "FroxelClear");
            FroxelDebugSlice ??= Compile(context, "FroxelDebugSlice");
            FroxelInject ??= Compile(context, "FroxelInject");
            FroxelLight ??= Compile(context, "FroxelLight");
            FroxelDisplacement ??= Compile(context, "FroxelDisplacement");
            FroxelDisplacementHistory ??= Compile(context, "FroxelDisplacementHistory");
            FroxelWakeCurl ??= Compile(context, "FroxelWakeCurl");
            FroxelLightning ??= Compile(context, "FroxelLightning");
            FroxelIntegrate ??= Compile(context, "FroxelIntegrate");
            FroxelTemporal ??= Compile(context, "FroxelTemporal");
            FroxelComposite ??= Compile(context, "FroxelComposite");
        }
        GodRaysMask ??= Compile(context, "GodRaysMask");
        GodRaysBlur ??= Compile(context, "GodRaysBlur");
        IllumDetailMapMaterial ??= Compile(context, "IllumDetailMapMaterial");
        Masked2DetailMapMaterial ??= Compile(context, "Masked2DetailMapMaterial");
        Navmap ??= Compile(context, "Navmap");
        NebulaExtPuff ??= Compile(context, "NebulaExtPuff");
        NebulaInterior ??= Compile(context, "NebulaInterior");
        NebulaMaterial ??= Compile(context, "NebulaMaterial");
        Nomad ??= Compile(context, "Nomad");
        Particle ??= Compile(context, "Particle");
        ParticleBeam ??= Compile(context, "ParticleBeam");
        PBR ??= Compile(context, "PBR");
        PhysicsDebug ??= Compile(context, "PhysicsDebug");
        ShadowCaster ??= Compile(context, "ShadowCaster");
        Sprite ??= Compile(context, "Sprite");
        StarsphereCubemap ??= Compile(context, "StarsphereCubemap");
        SunRadial ??= Compile(context, "SunRadial");
        SunSpine ??= Compile(context, "SunSpine");
        Tonemap ??= Compile(context, "Tonemap");
        ZoneVolume ??= Compile(context, "ZoneVolume");
        // ReSharper restore NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract

        var compileCount = Shader.TotalShaders - before;

        FLLog.Debug("Shaders", $"Compiled {compileCount} shaders.");

        FLLog.Debug("Shaders", "Compile complete");
    }

    private static ShaderBundle Compile(RenderContext context, string name)
    {
        FLLog.Debug("Shaders", $"Compiling {name}");
        return ShaderBundle.FromResource<FreelancerGame>(context, $"{name}.bin");
    }
}
