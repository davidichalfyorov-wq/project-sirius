namespace LibreLancer.Graphics;

public enum GraphicsFeature
{
    GLES,
    Anisotropy,
    S3TC,
    DebugInfo,
    LargeStorageBuffers,
    RayQuery,
    MeshShaders,
    VariableRateShading,
    // The cascade shadow atlas pass in space scenes. The GL backend hangs
    // inside the caster pass (un-diagnosed driver stall) - feature-gated to
    // Vulkan until that is hunted down; GL simply keeps the pre-shadow look.
    SceneShadows,
    // Compute shaders (roadmap phase 5 foundation: froxel volumetrics,
    // DDGI probe updates, LUT generation). Vulkan-only.
    Compute
}
