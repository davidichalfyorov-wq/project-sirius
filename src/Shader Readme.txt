Includes use the syntax '#pragma include (file.inc)'
Not having whitespace screws with the regex <3

== Compute shaders (phase 5) ==

Bundle description (.txt): a single line
    computeshader MyPass.comp.hlsl
No vertex/fragment/meshshader lines are allowed in the same bundle.
'feature'/'vulkanfeature' permutations work as usual; vulkanfeature
permutations compile as cs_6_5 with the vulkan1.2 target (ray query).

Compute bundles are SPIR-V only (like mesh): load them behind
context.HasFeature(GraphicsFeature.Compute) or AllShaders gating, never
on the GL path.

Authoring conventions:
- Descriptor spaces: TEXTURE_SPACE = space0, UNIFORM_SPACE = space1
  (compute owns the vertex-stage descriptor sets; sets 2/3 stay empty).
- Register rules. The SDL_GPU-style de-alias maps IMAGE registers
  (Texture*/RWTexture*/AS) tN/uN -> binding 2N and samplers sN -> 2N+1;
  STRUCTURED BUFFERS keep their raw register index as the binding (no
  remap). Bindings must be unique per shader - a collision throws at
  load ("descriptor collision at set..binding.."). Convention:
      sampled textures   t0..t3  -> bindings 0,2,4,6 (+samplers odd)
      storage images     u4..u7  -> bindings 8,10,12,14
      structured buffers t9,t11,t13,t15 -> bindings 9,11,13,15
  (odd SSBO registers never collide with the even image bindings).
- Storage images MUST carry an explicit format annotation:
      [[vk::image_format("rgba16f")]] RWTexture3D<float4> Out : register(u4, TEXTURE_SPACE);
  (format=Unknown needs a device feature we do not enable; VKSpirv logs
  a warning when the annotation is missing.)
- Uniforms: cbuffer at register(b3, UNIFORM_SPACE), filled with
  shader.SetUniformBlock(3, ref data) like fragment passes.

Dispatch pattern (engine side):
    rstate.SetStorageImage(4, volumeTexture);   // u4 -> slot 4
    rstate.Shader = AllShaders.MyPass.Get(0);
    rstate.DispatchCompute(gx, gy, gz);         // ends any active render pass
    rstate.BarrierComputeToGraphics();          // before sampling results
Barrier discipline: BarrierComputeToCompute between dependent dispatches,
BarrierGraphicsToCompute after rendering inputs a dispatch will read
(e.g. CopyDepth), BarrierComputeToGraphics before any draw samples the
results. Storage images live in GENERAL layout permanently - the global
memory barriers above are all that is needed.

3D textures: new Texture3D(rstate, w, h, d, format, storage: true) for
compute-written volumes; sample in any stage via rstate.Textures[N] like
a 2D texture (Texture3D<float4> + SampleLevel in HLSL).

Scene depth: rstate.CopyDepth(sceneTarget, depthTexture2D) copies the
depth attachment into a sampleable SurfaceFormat.Depth texture (the live
attachment itself is never sampleable by engine design).
