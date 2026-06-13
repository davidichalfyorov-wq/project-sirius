// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using LibreLancer.Data;
using LibreLancer.Data.GameData.World;
using LibreLancer.Graphics;
using LibreLancer.Graphics.Primitives;
using LibreLancer.Graphics.Vertices;
using LibreLancer.Render.Materials;
using LibreLancer.Resources;
using LibreLancer.Utf.Cmp;
using LibreLancer.Utf.Mat;
using LibreLancer.Utf.Vms;

namespace LibreLancer.Render
{
    public class AsteroidFieldRenderer : IDisposable
    {
        private const int SIDES = 20;

        private AsteroidField field;
        private bool renderBand = false;
        private Matrix4x4 bandTransform;
        private OpenCylinder bandCylinder = null!;
        private Vector3 cameraPos;
        private float lightingRadius;
        private float renderDistSq;
        // Golden captures need deterministic decorative noise; normal play
        // gets fresh randomness (task #39).
        private Random rand = SiriusAutoplay.GoldenDir != null ? new Random(12345) : new Random();

        // SIRIUS_FIELD_GOLDEN=1: keep asteroid fields in golden captures
        // (the C2 mesh-vs-classic parity gate shoots inside a debris field;
        // cube placement is deterministic there thanks to the seeded rand).
        private static readonly bool FieldGoldenOverride =
            Environment.GetEnvironmentVariable("SIRIUS_FIELD_GOLDEN") == "1";

        // Diagnostic: restrict the mesh path to one named zone.
        private static readonly string? MsOnlyZone =
            Environment.GetEnvironmentVariable("SIRIUS_MS_ONLY_ZONE");
        private SystemRenderer sys;
        private AsteroidBandMaterial bandMaterial = null!;
        private AsteroidCubeMesh cubeMesh = null!;

        // Mesh-shader path (roadmap 7.5): static meshlet geometry in
        // storage buffers + a per-frame cube transform stream. Built only
        // when the device supports mesh shaders.
        private AsteroidMeshletSet? meshlets;
        private StorageBuffer? msVerts, msHeaders, msVertIds, msTris, msWorlds;
        private (int Start, int Count)[] meshletRanges = [];
        private long msFrame;
        private int msLogCountdown;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        private struct MeshBasicParameters
        {
            public Color4 Dc;
            public Color4 Ec;
            public Vector2 FadeRange;
            public float Oc;
            public float Tex2Type;
            public float DebugMode;
            public Vector3 _pad;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        private struct FieldParams
        {
            public uint MeshletBase;
            public uint MeshletCount;
            public uint CubeCount;
            public uint _pad;
        }

        private TextureShape billboardShape;

        public AsteroidFieldRenderer(AsteroidField field, SystemRenderer sys)
        {
            this.field = field;
            this.sys = sys;
            // Set up renderDistSq
            var rdist = Math.Max(Math.Max(field.Zone!.Size.X, field.Zone.Size.Y), field.Zone.Size.Z);

            if (field.BillboardCount != -1)
            {
                SetBillboardShape();
                billboardCube = new AsteroidBillboard[field.BillboardCount];
                for (var i = 0; i < field.BillboardCount; i++)
                    billboardCube[i].Spawn(this);
                calculatedBillboards = new AsteroidBillboard[field.BillboardCount];
            }

            rdist += field.FillDist;
            renderDistSq = rdist * rdist;
            cubes = new CalculatedCube[4000];

            if (field.Cube!.Count > 0)
            {
                cubeMesh = new AsteroidCubeMeshBuilder().CreateMesh(sys.RenderContext, field, sys.ResourceManager);
                if (sys.RenderContext.HasFeature(GraphicsFeature.MeshShaders) &&
                    cubeMesh.SourceVertices is { Length: > 0 } srcVerts &&
                    cubeMesh.SourceIndices is { Length: > 0 } srcIndices)
                {
                    BuildMeshletResources(srcVerts, srcIndices);
                }
            }

            // Set up band
            if (field.Band == null ||
                (field.Zone.Shape != ShapeKind.Sphere && field.Zone.Shape != ShapeKind.Ellipsoid))
            {
                return;
            }

            bandMaterial = new AsteroidBandMaterial(sys.ResourceManager)
            {
                Texture = field.Band.Shape!,
                ColorShift = field.Band.ColorShift,
                TextureAspect = field.Band.TextureAspect
            };

            renderBand = true;
            bandCylinder = sys.ResourceManager.GetOpenCylinder(SIDES);
        }

        private void SetBillboardShape()
        {
            var col = new TexturePanelCollection();

            foreach (var f in field.TexturePanels)
            {
                f.Load(sys.ResourceManager);
                col.AddFile(f);
            }

            billboardShape = col.GetShape(field.BillboardShape);
        }

        public void Dispose()
        {
            cubeMesh?.Dispose();
        }

        private float lastFog = float.MaxValue;
        private ICamera _camera = null!;

        public void Update(ICamera camera)
        {
            // Golden captures: field contents (billboards, dynamic rocks)
            // are consumed from Random streams along the camera's flight
            // path, so the pattern is never reproducible between runs. The
            // whole field is decorative - skip it for screenshot parity.
            if (SiriusAutoplay.GoldenDir != null && !FieldGoldenOverride)
            {
                return;
            }

            _camera = camera;
            cameraPos = camera.Position;

            if (Vector3.DistanceSquared(cameraPos, field.Zone!.Position) <= renderDistSq)
            {
                if (field.Cube!.Count > 0)
                {
                    asteroidsTask = Task.Run(() => CalculateAsteroidsTask(camera));
                }

                // Golden captures: dust billboards are consumed from a Random
                // stream as the camera moves, so their pattern depends on the
                // flight path before the director pose - never reproducible.
                // Solid asteroids (deterministic from world position) stay.
                if (field.BillboardCount != -1 && SiriusAutoplay.GoldenDir == null)
                {
                    billboardTask = Task.Run(() => CalculateBillboards(camera));
                }
            }
        }

        private AsteroidExclusionZone? GetExclusionZone(Vector3 pt) =>
            field.ExclusionZones.FirstOrDefault(f => f.Zone!.ContainsPoint(pt));

        private struct AsteroidBillboard : IComparable<AsteroidBillboard>
        {
            public Vector3 Position;
            public float Size;
            public int Texture;
            public float Distance;

            public void Spawn(AsteroidFieldRenderer r)
            {
                var p = new Vector3(
                    r.rand.NextFloat(-1, 1),
                    r.rand.NextFloat(-1, 1),
                    r.rand.NextFloat(-1, 1)
                );
                Position = (p * r.field.FillDist);
                Size = r.rand.NextFloat(r.field.BillboardSize.X, r.field.BillboardSize.Y) * 2;
                Texture = r.rand.Next(0, 3);
            }

            public int CompareTo(AsteroidBillboard other) => Distance.CompareTo(other.Distance);
        }

        /*
         * Asteroid billboards are generated in a cube of size fillDist * 2
         * This is up to billboard_count billboards
         * The billboards spawn from 110% of the distance to the center
         */
        private AsteroidBillboard[] billboardCube = null!;
        private AsteroidBillboard[] calculatedBillboards = null!;
        private AsteroidBillboard[] billboardBuffer = new AsteroidBillboard[9000];
        private int billboardCount = 0;
        private Task billboardTask = null!;
        private bool warnedTooManyBillboards = false;

        private void CalculateBillboards(ICamera camera)
        {
            billboardCount = 0;
            var position = camera.Position;
            var close = AsteroidFieldShared.GetCloseCube(position, (int) (field.FillDist * 2));
            var checkRad = field.FillDist + field.BillboardSize.Y;
            var checkCount = 0;

            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    for (var z = -1; z <= 1; z++)
                    {
                        var center = close + new Vector3(x, y, z) * (field.FillDist * 2);

                        // early bail for billboards too far
                        if (Vector3.Distance(position, center) - checkRad > field.FillDist)
                        {
                            continue;
                        }

                        // bail billboards outside of zone - avoids popping
                        if (field.Zone!.ScaledDistance(center) > 1.1f)
                        {
                            continue;
                        }

                        // rotate
                        var rotation =
                            AsteroidCubeRotation.Default.GetRotation((int) (AsteroidFieldShared.PositionHash(center) *
                                                                            63));

                        for (var i = 0; i < billboardCube.Length; i++)
                        {
                            var spritepos = center + Vector3.Transform(billboardCube[i].Position, rotation);

                            // cull individual billboards too far
                            if (Vector3.Distance(position, spritepos) > field.FillDist)
                            {
                                continue;
                            }

                            billboardBuffer[checkCount] = billboardCube[i];
                            billboardBuffer[checkCount].Position = spritepos;
                            billboardBuffer[checkCount++].Distance = Vector3.DistanceSquared(center, position);
                        }
                    }
                }
            }

            // Highly unlikely this check will succeed. If it does there's something wrong with the cube code
            if (checkCount > field.BillboardCount)
            {
                if (!warnedTooManyBillboards)
                {
                    warnedTooManyBillboards = true;
                    FLLog.Warning("Asteroids", "Too many billboards in sort task for field " + field.Zone!.Nickname);
                }

                Array.Sort(billboardBuffer, 0, checkCount); // Get closest
                checkCount = field.BillboardCount;
            }

            // Cull ones that aren't on screen
            for (var i = 0; i < checkCount; i++)
            {
                var billboard = billboardBuffer[i];
                var sphere = new BoundingSphere(billboard.Position, billboard.Size * 1.5f);

                if (!camera.FrustumCheck(sphere))
                {
                    continue;
                }

                calculatedBillboards[billboardCount++] = billboard;
            }
        }

        private struct CalculatedCube
        {
            public Vector3 pos;
            public Matrix4x4 tr;

            public CalculatedCube(Vector3 p, Transform3D r)
            {
                pos = p;
                tr = r.Matrix();
            }
        }

        private Task asteroidsTask = null!;
        private int cubeCount = -1;
        private CalculatedCube[] cubes;

        private void CalculateAsteroidsTask(ICamera cam)
        {
            cubeCount = 0;
            var position = cam.Position;
            var close = AsteroidFieldShared.GetCloseCube(cameraPos, field.CubeSize);
            var amountCubes = (int) Math.Floor((field.FillDist / field.CubeSize)) + 1;

            for (var x = -amountCubes; x <= amountCubes; x++)
            {
                for (var y = -amountCubes; y <= amountCubes; y++)
                {
                    for (var z = -amountCubes; z <= amountCubes; z++)
                    {
                        var center = close + new Vector3(x, y, z) * field.CubeSize;
                        var closestDistance = (Vector3.Distance(center, position) - cubeMesh.Radius);

                        if (closestDistance >= field.FillDist || closestDistance >= lastFog)
                        {
                            continue;
                        }

                        if (!field.Zone!.ContainsPoint(center))
                        {
                            continue;
                        }

                        var cubeSphere = new BoundingSphere(center, cubeMesh.Radius);

                        if (!cam.FrustumCheck(cubeSphere))
                        {
                            continue;
                        }

                        if (!AsteroidFieldShared.CubeExists(center, field.EmptyCubeFrequency, out var tval))
                        {
                            continue;
                        }

                        if (GetExclusionZone(center) != null)
                        {
                            continue;
                        }

                        cubes[cubeCount++] =
                            new CalculatedCube(center, new Transform3D(center, field.CubeRotation!.GetRotation(tval)));
                    }
                }
            }
        }

        private Texture2D? billboardTex;

        private static readonly Vector2[][] billboardCoords =
        [
            [new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1, 0)],
            [new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(0, 1)],
            [new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(1, 1)],
            [new Vector2(0.5f, 0.5f), new Vector2(1, 0), new Vector2(1, 1)]
        ];

        private void BuildMeshletResources(VertexPositionNormalDiffuseTexture[] srcVerts, ushort[] srcIndices)
        {
            var rstate = sys.RenderContext;
            meshlets = AsteroidMeshletSet.Build(srcIndices, cubeMesh.Drawcalls);
            // Drawcall ranges: meshlets were emitted in drawcall order.
            meshletRanges = new (int, int)[cubeMesh.Drawcalls.Length];
            for (var dc = 0; dc < cubeMesh.Drawcalls.Length; dc++)
            {
                var start = -1;
                var count = 0;
                for (var m = 0; m < meshlets.Meshlets.Length; m++)
                {
                    if (meshlets.Meshlets[m].DrawcallIndex != dc)
                    {
                        continue;
                    }
                    if (start < 0)
                    {
                        start = m;
                    }
                    count++;
                }
                meshletRanges[dc] = (Math.Max(start, 0), count);
            }

            // SLOT-expanded packed vertices: one 48-byte record per meshlet
            // slot, indirection pre-applied (see CubeField.mesh.hlsl note).
            var slotCount = meshlets.MeshletVertices.Length;
            msVerts = new StorageBuffer(rstate, slotCount * 48, 48);
            var pv = msVerts.BeginStreaming();
            unsafe
            {
                var dst = (float*)pv;
                for (var i = 0; i < slotCount; i++)
                {
                    var v = srcVerts[meshlets.MeshletVertices[i]];
                    var c = (Color4)new VertexDiffuse() { Pixel = v.Diffuse };
                    dst[i * 12 + 0] = v.Position.X;
                    dst[i * 12 + 1] = v.Position.Y;
                    dst[i * 12 + 2] = v.Position.Z;
                    dst[i * 12 + 3] = v.Normal.X;
                    dst[i * 12 + 4] = v.Normal.Y;
                    dst[i * 12 + 5] = v.Normal.Z;
                    dst[i * 12 + 6] = v.TextureCoordinate.X;
                    dst[i * 12 + 7] = v.TextureCoordinate.Y;
                    dst[i * 12 + 8] = c.R;
                    dst[i * 12 + 9] = c.G;
                    dst[i * 12 + 10] = c.B;
                    dst[i * 12 + 11] = c.A;
                }
            }
            msVerts.EndStreaming(slotCount);

            // Meshlet headers (uint4).
            msHeaders = new StorageBuffer(rstate, meshlets.Meshlets.Length * 16, 16);
            var ph = msHeaders.BeginStreaming();
            unsafe
            {
                var dst = (uint*)ph;
                for (var i = 0; i < meshlets.Meshlets.Length; i++)
                {
                    var m = meshlets.Meshlets[i];
                    dst[i * 4 + 0] = m.VertexOffset;
                    dst[i * 4 + 1] = m.VertexCount;
                    dst[i * 4 + 2] = m.TriangleOffset;
                    dst[i * 4 + 3] = m.TriangleCount;
                }
            }
            msHeaders.EndStreaming(meshlets.Meshlets.Length);

            // Global vertex ids, padded to uint4 elements.
            var idCount = (meshlets.MeshletVertices.Length + 3) / 4 * 4;
            msVertIds = new StorageBuffer(rstate, idCount * 4, 16);
            var pi = msVertIds.BeginStreaming();
            unsafe
            {
                var dst = (uint*)pi;
                for (var i = 0; i < meshlets.MeshletVertices.Length; i++)
                {
                    dst[i] = meshlets.MeshletVertices[i];
                }
            }
            msVertIds.EndStreaming(idCount / 4);

            // Packed triangles (one uint each), padded to uint4 elements.
            var triCount = meshlets.MeshletTriangles.Length / 3;
            var triSlots = (triCount + 3) / 4 * 4;
            msTris = new StorageBuffer(rstate, triSlots * 4, 16);
            var pt = msTris.BeginStreaming();
            unsafe
            {
                var dst = (uint*)pt;
                for (var i = 0; i < triCount; i++)
                {
                    dst[i] = (uint)(meshlets.MeshletTriangles[i * 3] |
                        (meshlets.MeshletTriangles[i * 3 + 1] << 8) |
                        (meshlets.MeshletTriangles[i * 3 + 2] << 16));
                }
            }
            msTris.EndStreaming(triSlots / 4);

            // Per-frame cube world matrices.
            msWorlds = new StorageBuffer(rstate, cubes.Length * 64, 64);
            FLLog.Info("Asteroids",
                $"Mesh path ready [{field.Zone!.Nickname}]: {meshlets.Meshlets.Length} meshlets, {srcVerts.Length} verts, {triCount} tris");
            FLLog.Info("Asteroids",
                $"  probe ref [{field.Zone!.Nickname}]: vert0={srcVerts[0].Position} mv0={meshlets.MeshletVertices[0]} mv1={meshlets.MeshletVertices[1]} mv2={meshlets.MeshletVertices[2]} v[mv2].x={srcVerts[meshlets.MeshletVertices[2]].Position.X:F1} h0=({meshlets.Meshlets[0].VertexOffset},{meshlets.Meshlets[0].VertexCount},{meshlets.Meshlets[0].TriangleOffset},{meshlets.Meshlets[0].TriangleCount})");
        }

        private int pendingMeshCubes;
        private Lighting pendingMeshLight;

        /// <summary>Issues the deferred mesh-shader cube dispatch. Called
        /// by SystemRenderer after the opaque command pass (immediate draws
        /// in the middle of the frame walk inherit unsettled state).</summary>
        public void DrawMeshPath(ResourceManager res)
        {
            if (pendingMeshCubes <= 0)
            {
                return;
            }
            DrawCubesMesh(res, pendingMeshLight, pendingMeshCubes);
            pendingMeshCubes = 0;
        }

        private unsafe void DrawCubesMesh(ResourceManager res, Lighting lt, int opaqueCount)
        {
            var rstate = sys.RenderContext;
            // The camera uniform tracks the LAST SetCamera; by post-opaque
            // time that can be a special pass camera (starsphere). RenderDoc
            // verdict: world positions were perfect, clip W was ~18 - sky
            // scale. Re-pin the scene camera for this dispatch.
            rstate.SetCamera(_camera);
            rstate.BeginPassTimer("field.mesh");
            var dcQuantized = (VertexDiffuse)field.DiffuseColor;
            dcQuantized.A = 255;
            var texSelectors = Vector4.Zero;

            // Slots t10..t14: clear of the engine's vertex-stage storage
            // users (bones/particles/beams own t9 - clobbering it kills
            // every later draw that pulls from it).
            msWorlds!.BindTo(10);
            msVerts!.BindTo(11);
            msHeaders!.BindTo(12);
            msVertIds!.BindTo(13);
            msTris!.BindTo(14);

            rstate.DepthEnabled = true;
            rstate.Cull = true;
            rstate.CullFace = CullFaces.Back;

            for (var dc = 0; dc < cubeMesh.Drawcalls.Length; dc++)
            {
                var range = meshletRanges[dc];
                if (range.Count == 0)
                {
                    continue;
                }
                var mat = res.FindMaterial(cubeMesh.Drawcalls[dc].MaterialCrc);
                var basicMat = mat?.Render as BasicMaterial;
                basicMat?.BindDtForMesh(rstate);
                // Mirror the classic path's per-material alpha decision:
                // DXT1/alpha-test materials discard and alpha-blend.
                var alphaTest = basicMat?.UsesAlphaTestForMesh() ?? false;
                var caps = (alphaTest ? 1u : 0u) |
                    (RenderMaterial.DebugViewMode > 0 ? 2u : 0u);
                var shader = Shaders.AllShaders.CubeField!.Get(caps);
                rstate.Shader = shader;
                rstate.BlendMode = alphaTest ? BlendMode.Normal : BlendMode.Opaque;
                // Same constants the classic submit path feeds the shader:
                // Dc comes from the field colour (SetDc userData), the rest
                // from the cube's MAT material.
                RenderMaterial.SetLights(shader, rstate, ref lt, rstate.FrameNumber + (++msFrame << 32));
                shader.SetUniformBlock(5, ref texSelectors);
                var material = new MeshBasicParameters
                {
                    Dc = ColorSpace.SrgbToLinear((Color4)dcQuantized),
                    Ec = ColorSpace.SrgbToLinear(basicMat?.Ec ?? Color4.White),
                    FadeRange = Vector2.Zero,
                    Oc = (basicMat?.Oc ?? 1f) * (basicMat?.OpacityMultiplier ?? 1f),
                    Tex2Type = 0,
                    DebugMode = RenderMaterial.DebugViewMode
                };
                shader.SetUniformBlock(3, ref material);
                var fieldParams = new FieldParams
                {
                    MeshletBase = (uint)range.Start,
                    MeshletCount = (uint)range.Count,
                    CubeCount = (uint)opaqueCount
                };
                shader.SetUniformBlock(0, ref fieldParams);
                rstate.DrawMeshTasks((uint)range.Count, (uint)opaqueCount, 1);
            }
            rstate.EndPassTimer();
        }

        public void Draw(ResourceManager res, SystemLighting lighting, CommandBuffer buffer, NebulaRenderer nr)
        {
            // Golden captures: see Update - the whole field is skipped.
            if (SiriusAutoplay.GoldenDir != null && !FieldGoldenOverride)
            {
                return;
            }

            // Asteroids!
            if (Vector3.DistanceSquared(cameraPos, field.Zone!.Position) <= renderDistSq)
            {
                var fadeNear = field.FillDist - 100f;
                var fadeFar = field.FillDist;

                if (field.Cube!.Count > 0)
                {
                    if (cubeCount == -1)
                    {
                        return;
                    }

                    asteroidsTask.Wait();
                    var lt = RenderHelpers.ApplyLights(lighting, 0, cameraPos, field.FillDist, nr);
                    lt.Ambient += field.AmbientIncrease.Rgb;
                    lt.Ambient *= field.AmbientColor.Rgb;

                    if (lt.FogMode == FogModes.Linear)
                    {
                        lastFog = lt.FogRange.Y;
                    }
                    else
                    {
                        lastFog = float.MaxValue;
                    }

                    var fadeCount = 0;
                    var regCount = 0;

                    // Mesh-shader path: opaque (fully inside) cubes go down
                    // in ONE dispatch per material; the faded shell stays on
                    // the classic sorted-transparency submits. Uses the SAME
                    // field-adjusted light (lt) as the classic submits.
                    var useMeshPath = meshlets != null &&
                        sys.Settings.SelectedMeshAsteroids &&
                        Shaders.AllShaders.CubeField != null &&
                        (MsOnlyZone == null || field.Zone!.Nickname == MsOnlyZone);
                    var meshOpaque = 0;
                    if (!useMeshPath && msLogCountdown-- <= 0)
                    {
                        msLogCountdown = 300;
                        var classicOpaque = 0;
                        for (var j = 0; j < cubeCount; j++)
                        {
                            if ((Vector3.Distance(cubes[j].pos, cameraPos) + cubeMesh.Radius) < fadeNear)
                            {
                                classicOpaque++;
                            }
                        }
                        FLLog.Info("Asteroids", $"classic path: {classicOpaque} opaque cubes of {cubeCount}");
                    }
                    if (useMeshPath)
                    {
                        var pw = msWorlds!.BeginStreaming();
                        unsafe
                        {
                            var dst = (Matrix4x4*)pw;
                            for (var j = 0; j < cubeCount; j++)
                            {
                                if ((Vector3.Distance(cubes[j].pos, cameraPos) + cubeMesh.Radius) < fadeNear)
                                {
                                    dst[meshOpaque++] = cubes[j].tr;
                                }
                            }
                        }
                        msWorlds.EndStreaming(meshOpaque);
                        // Dispatch happens in DrawMeshPath - the system
                        // renderer calls it AFTER the opaque command pass,
                        // when the frame's viewport/state are settled
                        // (immediate draws from this spot land in whatever
                        // pass state preceded the field walk).
                        pendingMeshCubes = meshOpaque;
                        pendingMeshLight = lt;
                        if (msLogCountdown-- <= 0)
                        {
                            msLogCountdown = 300;
                            FLLog.Info("Asteroids", $"mesh path: {meshOpaque} opaque cubes of {cubeCount}");
                        }
                    }

                    for (var j = 0; j < cubeCount; j++)
                    {
                        var center = cubes[j].pos;
                        var z = RenderHelpers.GetZ(cameraPos, center);

                        for (var i = 0; i < cubeMesh.Drawcalls.Length; i++)
                        {
                            var dc = cubeMesh.Drawcalls[i];
                            var mat = res.FindMaterial(dc.MaterialCrc);

                            if ((Vector3.Distance(center, cameraPos) + cubeMesh.Radius) < fadeNear)
                            {
                                if (useMeshPath)
                                {
                                    continue; // already in the mesh dispatch
                                }
                                buffer.AddCommand(
                                    mat!.Render,
                                    null,
                                    buffer.WorldBuffer.SubmitMatrix(ref cubes[j].tr),
                                    lt,
                                    cubeMesh.VertexBuffer,
                                    1.0f,
                                    PrimitiveTypes.TriangleList,
                                    dc.BaseVertex,
                                    dc.StartIndex,
                                    dc.Count / 3,
                                    SortLayers.OBJECT,
                                    0, null, 0, BasicMaterial.SetDc(field.DiffuseColor)
                                );
                                regCount++;
                            }
                            else
                            {
                                buffer.AddCommandFade(
                                    mat!.Render,
                                    buffer.WorldBuffer.SubmitMatrix(ref cubes[j].tr),
                                    lt,
                                    cubeMesh.VertexBuffer,
                                    PrimitiveTypes.TriangleList,
                                    dc.BaseVertex,
                                    dc.StartIndex,
                                    dc.Count / 3,
                                    SortLayers.OBJECT,
                                    new Vector2(fadeNear, fadeFar),
                                    z, 0, BasicMaterial.SetDc(field.DiffuseColor)
                                );
                                fadeCount++;
                            }
                        }
                    }
                }

                // Matches the Update-side golden guard: stale billboards from
                // before the director pose must not be drawn either.
                if (field.BillboardCount != -1 && SiriusAutoplay.GoldenDir == null)
                {
                    var cameraLights = RenderHelpers.ApplyLights(lighting, 0, cameraPos, 1, nr);

                    if (billboardTex == null || billboardTex.IsDisposed)
                    {
                        billboardTex = (Texture2D?) res.FindTexture(billboardShape.Texture);
                    }

                    billboardTask.Wait();

                    for (var i = 0; i < billboardCount; i++)
                    {
                        var alpha = BillboardAlpha(Vector3.Distance(calculatedBillboards[i].Position, cameraPos));

                        if (alpha <= 0)
                        {
                            continue;
                        }

                        var coords = billboardCoords[calculatedBillboards[i].Texture];
                        sys.Billboards.DrawTri(
                            billboardTex!,
                            calculatedBillboards[i].Position,
                            calculatedBillboards[i].Size,
                            new Color4(field.BillboardTint * cameraLights.Ambient, alpha),
                            coords[0], coords[2], coords[1],
                            0,
                            SortLayers.OBJECT
                        );
                    }
                }

            }

            // Band is last
            if (renderBand)
            {
                CalculateBandTransform();

                if (!_camera.FrustumCheck(new BoundingSphere(field.Zone.Position, lightingRadius)))
                {
                    return;
                }

                var bandHandle = buffer.WorldBuffer.SubmitMatrix(ref bandTransform);

                for (var i = 0; i < SIDES; i++)
                {
                    var p = bandCylinder.GetSidePosition(i);
                    var zcoord = RenderHelpers.GetZ(bandTransform, cameraPos, p);
                    p = Vector3.Transform(p, bandTransform);
                    var lt = RenderHelpers.ApplyLights(lighting, 0, p, lightingRadius, nr);

                    if (lt.FogMode != FogModes.Linear || Vector3.DistanceSquared(cameraPos, p) <=
                        (lightingRadius + lt.FogRange.Y) * (lightingRadius + lt.FogRange.Y))
                    {
                        buffer.AddCommand(bandMaterial, null, bandHandle, lt, bandCylinder.VertexBuffer, 1.0f,
                            PrimitiveTypes.TriangleList, 0, i * 6, 2, SortLayers.OBJECT, zcoord);
                    }
                }
            }
        }

        private void CalculateBandTransform()
        {
            Vector3 sz = Vector3.Zero;

            if (field.Zone!.Shape == ShapeKind.Sphere)
            {
                sz = new Vector3(field.Zone.Size.X);
            }
            else
            {
                sz = field.Zone.Size;
            }

            sz.X -= field.Band!.OffsetDistance;
            sz.Z -= field.Band.OffsetDistance;
            lightingRadius = Math.Max(sz.X, sz.Z);
            bandTransform = (
                Matrix4x4.CreateScale(sz.X, field.Band.Height / 2f, sz.Z) *
                field.Zone.RotationMatrix *
                Matrix4x4.CreateTranslation(field.Zone.Position)
            );
        }

        private float BillboardAlpha(float dist)
        {
            if (dist >= field.BillboardDistance)
            {
                // Fade out from billboard_distance to filldist
                return (field.FillDist - dist) / (field.FillDist - field.BillboardDistance);
            }

            // visible from start_dist - start_dist * fade percentage
            var fadeNear = field.BillboardDistance - (field.BillboardDistance * field.BillboardFadePercentage);

            if (dist >= fadeNear)
            {
                var max = field.BillboardDistance * field.BillboardFadePercentage;
                return (dist - fadeNear) / max;
            }

            // Too close to the camera: invisible
            return 0;
        }
    }
}
