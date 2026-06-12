using System;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.Shaders;

namespace LibreLancer.Render;

/// <summary>
/// SIRIUS_COMPUTE_SMOKE=1: dispatches a compute shader writing an XYZ
/// gradient into a 32^3 storage texture every frame, then visualises an
/// animated W slice in the corner of the screen. Exercises the whole
/// phase-5 compute foundation (bundle, pipeline, UAV descriptors,
/// dispatch, barrier, 3D sampling) under validation.
/// </summary>
public class ComputeSmokeTest
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("SIRIUS_COMPUTE_SMOKE") == "1";

    private Texture3D? volume;
    private bool unsupportedLogged;
    private double time;

    private struct SmokeParams
    {
        public Vector4 GridSize;
    }

    private struct VisParams
    {
        public Vector4 SliceParams;
    }

    public void Tick(RenderContext rstate, double elapsed)
    {
        if (!Enabled)
        {
            return;
        }
        if (!rstate.HasFeature(GraphicsFeature.Compute) ||
            AllShaders.ComputeSmoke == null || AllShaders.Texture3DVis == null)
        {
            if (!unsupportedLogged)
            {
                FLLog.Info("ComputeSmoke", "Compute unavailable; smoke test skipped");
                unsupportedLogged = true;
            }
            return;
        }

        time += elapsed;
        if (volume == null)
        {
            volume = new Texture3D(rstate, 32, 32, 32, SurfaceFormat.HdrBlendable, storage: true);
            FLLog.Info("ComputeSmoke", "32^3 storage volume created; dispatching every frame");
        }

        rstate.BeginPassTimer("compute.smoke");
        var smoke = AllShaders.ComputeSmoke.Get(0);
        var smokeParams = new SmokeParams { GridSize = new Vector4(32, 32, 32, (float)time) };
        smoke.SetUniformBlock(3, ref smokeParams);
        rstate.SetStorageImage(4, volume);
        rstate.Shader = smoke;
        rstate.DispatchCompute(8, 8, 8);
        rstate.BarrierComputeToGraphics();
        rstate.EndPassTimer();

        // Visualise an animated slice bottom-left so the gate screenshot
        // shows fresh GPU-written data, not a stale upload.
        var vis = AllShaders.Texture3DVis.Get(0);
        var visParams = new VisParams
        {
            SliceParams = new Vector4((float)(0.5 + 0.5 * Math.Sin(time * 0.5)), 1.0f, 0, 0)
        };
        vis.SetUniformBlock(3, ref visParams);
        // The swapchain pass renders with a flipped (negative-height)
        // viewport which inverts triangle winding: back-face culling would
        // discard the fullscreen triangle entirely (same reason
        // TintViewport disables culling).
        rstate.Cull = false;
        rstate.DepthEnabled = false;
        rstate.BlendMode = BlendMode.Opaque;
        rstate.Textures[0] = volume;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.PushViewport(16, 16, 256, 256);
        rstate.Shader = vis;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.PopViewport();
        if (!vizLogged)
        {
            FLLog.Info("ComputeSmoke", "slice visualiser draw submitted");
            vizLogged = true;
        }
    }

    private bool vizLogged;
}
