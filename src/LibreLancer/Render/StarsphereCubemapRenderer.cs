using System;
using System.IO;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.ImageLib;
using LibreLancer.Resources;
using LibreLancer.Shaders;

namespace LibreLancer.Render;

internal enum StarsphereLayer
{
    Basic = 0,
    Complex = 1,
    Nebula = 2
}

internal sealed class StarsphereCubemapRenderer : IDisposable
{
    private readonly TextureCube?[] layers = new TextureCube?[3];
    private readonly string?[] layerPaths = new string?[3];
    private TextureCube? transparentCube;
    private readonly RenderContext renderContext;
    private Shader? shader;

    private struct Parameters
    {
        public Matrix4x4 InverseViewProjection;
        public Vector4 ViewportTime;
        public Vector4 LayerMask;
        public Vector4 BasicRotation;
        public Vector4 ComplexRotation;
    }

    public StarsphereCubemapRenderer(RenderContext renderContext)
    {
        this.renderContext = renderContext;
    }

    public bool AnyLoaded => layers[0] != null || layers[1] != null || layers[2] != null;

    public bool AllLoaded => layers[0] != null && layers[1] != null && layers[2] != null;

    public bool HasLayer(StarsphereLayer layer) => layers[(int)layer] != null;

    public void Clear()
    {
        for (var i = 0; i < layers.Length; i++)
        {
            layers[i]?.Dispose();
            layers[i] = null;
            layerPaths[i] = null;
        }
    }

    public void LoadLayer(StarsphereLayer layer, string? path, ResourceManager resources)
    {
        if (string.IsNullOrWhiteSpace(path) || !resources.ResourceExists(path))
        {
            return;
        }

        try
        {
            using var stream = resources.OpenResource(path);
            if (DDS.FromStream(renderContext, stream) is not TextureCube cube)
            {
                FLLog.Warning("Starsphere", $"Cubemap '{path}' is not a DDS cubemap. Falling back to CMP.");
                return;
            }

            var index = (int)layer;
            layers[index]?.Dispose();
            layers[index] = cube;
            layerPaths[index] = path;
            FLLog.Info("Starsphere", $"Loaded cubemap layer {layer}: {path}");
        }
        catch (Exception ex)
        {
            FLLog.Warning("Starsphere", $"Could not load cubemap '{path}': {ex.Message}. Falling back to CMP.");
        }
    }

    public void DrawComposite(ICamera camera, double time, Rectangle viewport)
    {
        Draw(camera, time, viewport, new Vector3(
            layers[0] != null ? 1 : 0,
            layers[1] != null ? 1 : 0,
            layers[2] != null ? 1 : 0));
    }

    public void DrawLayer(ICamera camera, double time, Rectangle viewport, StarsphereLayer layer)
    {
        var mask = layer switch
        {
            StarsphereLayer.Basic => new Vector3(1, 0, 0),
            StarsphereLayer.Complex => new Vector3(0, 1, 0),
            StarsphereLayer.Nebula => new Vector3(0, 0, 1),
            _ => Vector3.Zero
        };
        Draw(camera, time, viewport, mask);
    }

    private void Draw(ICamera camera, double time, Rectangle viewport, Vector3 layerMask)
    {
        // Golden captures freeze the slow star-layer rotation: at 0.03 deg/s
        // any wall-clock difference between runs shifts every star and the
        // screenshots can never match.
        if (SiriusAutoplay.GoldenDir != null)
        {
            time = 0;
        }

        shader ??= AllShaders.StarsphereCubemap.Get(0);
        transparentCube ??= CreateTransparentCube(renderContext);

        // Starspheres sit at infinity: reconstruct view rays from a
        // translation-free view matrix so the camera's position never leaks
        // into the sampled direction. With the full ViewProjection the
        // far-near subtraction loses precision at Freelancer-scale
        // coordinates and the sky visibly slides with camera movement.
        var rotationOnlyView = camera.View;
        rotationOnlyView.Translation = Vector3.Zero;
        Matrix4x4.Invert(rotationOnlyView * camera.Projection, out var inverseViewProjection);

        var basicAngle = MathHelper.DegreesToRadians((float)(time * 0.01));
        var complexAngle = MathHelper.DegreesToRadians((float)(time * 0.03));
        var parameters = new Parameters
        {
            InverseViewProjection = inverseViewProjection,
            ViewportTime = new Vector4(viewport.Width, viewport.Height, (float)time, 0),
            LayerMask = new Vector4(layerMask, 0),
            BasicRotation = new Vector4(MathF.Sin(basicAngle), MathF.Cos(basicAngle), 0, 0),
            ComplexRotation = new Vector4(MathF.Sin(complexAngle), MathF.Cos(complexAngle), 0, 0)
        };

        var oldBlend = renderContext.BlendMode;
        var oldCull = renderContext.Cull;
        try
        {
            shader.SetUniformBlock(3, ref parameters);
            renderContext.Textures[0] = layers[0] ?? transparentCube;
            renderContext.Textures[1] = layers[1] ?? transparentCube;
            renderContext.Textures[2] = layers[2] ?? transparentCube;
            renderContext.Samplers[0] = SamplerState.LinearClamp;
            renderContext.Samplers[1] = SamplerState.LinearClamp;
            renderContext.Samplers[2] = SamplerState.LinearClamp;
            renderContext.BlendMode = BlendMode.Normal;
            renderContext.Cull = false;
            renderContext.Shader = shader;
            renderContext.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        }
        finally
        {
            renderContext.BlendMode = oldBlend;
            renderContext.Cull = oldCull;
        }
    }

    private static TextureCube CreateTransparentCube(RenderContext renderContext)
    {
        var cube = new TextureCube(renderContext, 1, false, SurfaceFormat.Bgra8);
        var transparent = new byte[] { 0, 0, 0, 0 };
        for (var i = 0; i < 6; i++)
        {
            cube.SetData((CubeMapFace)i, transparent);
        }
        return cube;
    }

    public void Dispose()
    {
        Clear();
        transparentCube?.Dispose();
        transparentCube = null;
    }
}
