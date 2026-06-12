using System;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.Graphics.Vertices;
using LibreLancer.Render.Materials;
using LibreLancer.Resources;
using LibreLancer.Shaders;
using LibreLancer.Data.GameData;
using LibreLancer.Utf.Mat;

namespace LibreLancer.Render;

/// <summary>
/// Cascaded shadow maps for the system's directional sun light (roadmap
/// 5.4). The engine's depth buffers aren't sampleable, so each cascade
/// renders linear light-space depth (RGB-packed) into a tile of a plain
/// colour atlas; receivers compare against it with PCF. Cascade
/// projections snap to texel-sized steps so edges stay still while the
/// camera pans.
/// </summary>
public sealed class ShadowMapRenderer : IDisposable
{
    public const int Cascades = 3;
    public const int TileSize = 1024;

    // Cascade reach in world units, tuned for the space scale: cockpit
    // ranges resolve crisp, station-sized casters keep coverage.
    private static readonly float[] cascadeFar = [600f, 2500f, 10000f];
    private const float LightRange = 30000f; // depth extent along the sun ray

    public RenderTarget2D Atlas { get; }
    public Texture2D AtlasTexture { get; }
    public Matrix4x4[] LightViewProjection { get; } = new Matrix4x4[Cascades];
    public Vector4 CascadeSplits => new(cascadeFar[0], cascadeFar[1], cascadeFar[2], 1f / LightRange);

    // Local spotlight shadows (roadmap 5.5): up to 4 tiles in a second
    // atlas; lights matched in-shader by position.
    public const int LocalLights = 4;
    public const int LocalTileSize = 512;
    public RenderTarget2D LocalAtlas { get; }
    public Texture2D LocalAtlasTexture { get; }
    public Matrix4x4[] LocalViewProjection { get; } = new Matrix4x4[LocalLights];
    public Vector4[] LocalPositions { get; } = new Vector4[LocalLights]; // xyz pos, w: 1/range
    public int LocalCount { get; private set; }

    private readonly RenderContext rstate;
    private readonly ShadowCasterMaterial casterMaterial;
    private readonly Material overrideMaterial;
    private readonly ShadowCamera shadowCamera = new();

    public ShadowMapRenderer(RenderContext rstate, ResourceManager resources)
    {
        this.rstate = rstate;
        AtlasTexture = new Texture2D(rstate, TileSize * Cascades, TileSize, false, SurfaceFormat.Bgra8);
        Atlas = new RenderTarget2D(rstate, AtlasTexture);
        LocalAtlasTexture = new Texture2D(rstate, LocalTileSize * 2, LocalTileSize * 2, false, SurfaceFormat.Bgra8);
        LocalAtlas = new RenderTarget2D(rstate, LocalAtlasTexture);
        casterMaterial = new ShadowCasterMaterial(resources);
        overrideMaterial = new Material(casterMaterial);
    }

    /// <summary>Renders all cascades; call before the main scene pass.</summary>
    public void Draw(System.Collections.Generic.List<ObjectRenderer> objects,
        System.Collections.Generic.List<DynamicLight> lights,
        Vector3 lightDirection, ICamera viewCamera, ResourceManager resources,
        bool skipSunCascades = false)
    {
        rstate.BeginPassTimer("shadow");
        var restoreTarget = rstate.RenderTarget;
        if (!skipSunCascades)
        {
            rstate.RenderTarget = Atlas;
            rstate.PushViewport(new Rectangle(0, 0, TileSize * Cascades, TileSize));
            rstate.ClearAll();
            rstate.PopViewport();
        }

        var lighting = Lighting.Empty;
        for (var cascade = 0; cascade < Cascades; cascade++)
        {
            var far = cascadeFar[cascade];
            // Fit an ortho box around the cascade's view-distance sphere,
            // centred ahead of the camera, snapped to shadow texels.
            var radius = far;
            var center = viewCamera.Position + CameraForward(viewCamera) * (far * 0.5f);
            var texel = (radius * 2f) / TileSize;
            var up = MathF.Abs(lightDirection.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
            var view = Matrix4x4.CreateLookAt(center - lightDirection * (LightRange * 0.5f), center, up);
            // Texel snap in light space (stabilization, roadmap 5.4).
            var origin = Vector3.Transform(Vector3.Zero, view);
            var snap = new Vector3(
                MathF.Floor(origin.X / texel) * texel - origin.X,
                MathF.Floor(origin.Y / texel) * texel - origin.Y, 0);
            view *= Matrix4x4.CreateTranslation(snap);
            var projection = Matrix4x4.CreateOrthographic(radius * 2f, radius * 2f, 0f, LightRange);
            LightViewProjection[cascade] = view * projection;

            shadowCamera.Set(view, projection, center);
            if (skipSunCascades)
            {
                continue; // matrices/splits computed above stay valid
            }
            rstate.SetCamera(shadowCamera);
            rstate.PushViewport(new Rectangle(cascade * TileSize, 0, TileSize, TileSize));
            casterMaterial.InverseFarPlane = 1f / LightRange;
            // Casters: rigid models only - suns, nebulae, particles and
            // beams never enter the atlas (roadmap 5.4).
            foreach (var obj in objects)
            {
                if (obj is ModelRenderer { Model: { } model } mr)
                {
                    model.DrawImmediate(rstate, resources, mr.World,
                        ref lighting, 0, overrideMaterial);
                }
            }
            rstate.PopViewport();
        }

        DrawLocalShadows(objects, lights, resources);

        rstate.RenderTarget = restoreTarget;
        rstate.EndPassTimer();
    }

    /// <summary>
    /// The brightest in-range spotlights get a perspective tile each
    /// (roadmap 5.5); selection is stable for static base lights.
    /// </summary>
    private void DrawLocalShadows(System.Collections.Generic.List<ObjectRenderer> objects,
        System.Collections.Generic.List<DynamicLight> lights, ResourceManager resources)
    {
        Span<int> picked = stackalloc int[LocalLights];
        Span<float> scores = stackalloc float[LocalLights];
        LocalCount = 0;
        for (var i = 0; i < lights.Count; i++)
        {
            if (!lights[i].Active || lights[i].Light.Kind != LightKind.Spotlight)
            {
                continue;
            }
            ref readonly var light = ref lights[i].Light;
            var score = (light.Color.R + light.Color.G + light.Color.B) * light.Range;
            var slot = LocalCount < LocalLights ? LocalCount : -1;
            if (slot < 0)
            {
                var minScore = float.MaxValue;
                for (var j = 0; j < LocalLights; j++)
                {
                    if (scores[j] < minScore)
                    {
                        minScore = scores[j];
                        slot = j;
                    }
                }
                if (score <= minScore)
                {
                    continue;
                }
            }
            else
            {
                LocalCount++;
            }
            picked[slot] = i;
            scores[slot] = score;
        }
        if (LocalCount == 0)
        {
            return;
        }

        rstate.RenderTarget = LocalAtlas;
        rstate.PushViewport(new Rectangle(0, 0, LocalTileSize * 2, LocalTileSize * 2));
        rstate.ClearAll();
        rstate.PopViewport();
        var lighting = Lighting.Empty;
        for (var slot = 0; slot < LocalCount; slot++)
        {
            ref readonly var light = ref lights[picked[slot]].Light;
            var range = MathF.Max(light.Range, 50f);
            var up = MathF.Abs(light.Direction.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
            var view = Matrix4x4.CreateLookAt(light.Position, light.Position + light.Direction, up);
            var fov = MathHelper.DegreesToRadians(MathF.Max(light.Phi, 10f));
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.Min(fov, MathF.PI * 0.9f), 1f, range * 0.01f, range);
            LocalViewProjection[slot] = view * projection;
            LocalPositions[slot] = new Vector4(light.Position, 1f / range);

            shadowCamera.Set(view, projection, light.Position);
            rstate.SetCamera(shadowCamera);
            rstate.PushViewport(new Rectangle(
                (slot & 1) * LocalTileSize, (slot >> 1) * LocalTileSize, LocalTileSize, LocalTileSize));
            casterMaterial.InverseFarPlane = 1f / range;
            foreach (var obj in objects)
            {
                if (obj is ModelRenderer { Model: { } model } mr &&
                    Vector3.DistanceSquared(mr.World.Translation, light.Position) < range * range * 4f)
                {
                    model.DrawImmediate(rstate, resources, mr.World, ref lighting, 0, overrideMaterial);
                }
            }
            rstate.PopViewport();
        }
    }

    private static Vector3 CameraForward(ICamera camera)
    {
        // Third row of the view matrix is -forward.
        var view = camera.View;
        return Vector3.Normalize(new Vector3(-view.M13, -view.M23, -view.M33));
    }

    public void Dispose()
    {
        Atlas.Dispose();
        AtlasTexture.Dispose();
        LocalAtlas.Dispose();
        LocalAtlasTexture.Dispose();
    }

    /// <summary>Minimal ICamera over the cascade's light-space matrices.</summary>
    private sealed class ShadowCamera : ICamera
    {
        private Matrix4x4 view;
        private Matrix4x4 projection;
        private Matrix4x4 viewProjection;
        private BoundingFrustum frustum = new(Matrix4x4.Identity);
        private Vector3 position;

        public void Set(Matrix4x4 view, Matrix4x4 projection, Vector3 position)
        {
            this.view = view;
            this.projection = projection;
            this.position = position;
            viewProjection = view * projection;
            frustum = new BoundingFrustum(viewProjection);
        }

        public Matrix4x4 ViewProjection => viewProjection;
        public Matrix4x4 Projection => projection;
        public Matrix4x4 View => view;
        public Vector3 Position => position;
        public bool FrustumCheck(BoundingSphere sphere) => frustum.Intersects(sphere);
        public bool FrustumCheck(BoundingBox box) => frustum.Intersects(box);
    }
}

/// <summary>Writes RGB-packed linear light depth (ShadowCaster shader).</summary>
internal sealed class ShadowCasterMaterial : RenderMaterial
{
    public float InverseFarPlane = 1f / 30000f;

    public ShadowCasterMaterial(ResourceManager library) : base(library)
    {
    }

    public override void Use(RenderContext rstate, IVertexType vertextype, ref Lighting lights, int userData)
    {
        var shader = AllShaders.ShadowCaster.Get(0);
        SetWorld(shader);
        var parameters = new Vector4(InverseFarPlane, 0, 0, 0);
        shader.SetUniformBlock(3, ref parameters);
        rstate.BlendMode = BlendMode.Opaque;
        rstate.Shader = shader;
    }

    public override bool IsTransparent => false;
}
