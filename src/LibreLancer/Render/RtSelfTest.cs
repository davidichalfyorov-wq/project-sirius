using System;
using System.Numerics;
using LibreLancer.Graphics;

namespace LibreLancer.Render;

/// <summary>
/// SIRIUS_RT_SELFTEST=1: builds a 12-triangle cube BLAS and a 1-instance
/// TLAS on the first frames, logs the lifecycle, then destroys the BLAS -
/// exercises buffers, device addresses, barriers and deferred destruction
/// under validation without touching the scene.
/// </summary>
public class RtSelfTest
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("SIRIUS_RT_SELFTEST") == "1";

    private int frame;
    private BlasHandle blas = BlasHandle.Invalid;
    private bool done;

    public void Tick(RenderContext rstate)
    {
        if (!Enabled || done)
        {
            return;
        }
        var rt = rstate.RayTracing;
        if (rt == null)
        {
            FLLog.Info("RtSelfTest", "Ray tracing unavailable; self-test skipped");
            done = true;
            return;
        }

        frame++;
        if (frame == 1)
        {
            Span<Vector3> corners = stackalloc Vector3[8]
            {
                new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
                new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)
            };
            Span<ushort> indices = stackalloc ushort[36]
            {
                0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1, 3, 2, 6, 3, 6, 7,
                0, 3, 7, 0, 7, 4, 1, 5, 6, 1, 6, 2
            };
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(corners);
            blas = rt.CreateBlas(bytes, 12, 8, indices, stackalloc BlasRange[1] { new(0, 12, 0) });
            FLLog.Info("RtSelfTest", $"Cube BLAS created: handle={blas.Id}, blasCount={rt.BlasCount}");
        }
        else if (frame is > 1 and <= 4 && blas.IsValid)
        {
            rt.BuildSceneTlas(stackalloc TlasInstance[1]
            {
                new(blas, Matrix4x4.CreateTranslation(0, 0, -10), 7)
            });
            FLLog.Info("RtSelfTest", $"TLAS built: instances={rt.LastInstanceCount}");
        }
        else if (frame == 5)
        {
            rt.DestroyBlas(blas);
            FLLog.Info("RtSelfTest", "BLAS destroy queued (deferred)");
            done = true;
        }
    }
}
