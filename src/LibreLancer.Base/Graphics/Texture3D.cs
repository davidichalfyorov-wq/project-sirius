using LibreLancer.Graphics.Backends;

namespace LibreLancer.Graphics;

/// <summary>
/// 3D texture (phase 5 volumetrics: froxel grids, noise volumes, LUTs).
/// Vulkan-only - gate creation behind HasFeature(GraphicsFeature.Compute).
/// Pass storage: true when a compute shader writes it (RWTexture3D).
/// </summary>
public class Texture3D : Texture
{
    internal ITexture3D Backing3D;
    public int Width => Backing3D.Width;
    public int Height => Backing3D.Height;
    public int Depth => Backing3D.Depth;

    public Texture3D(RenderContext context, int width, int height, int depth,
        SurfaceFormat format = SurfaceFormat.HdrBlendable, bool storage = false)
    {
        Backing3D = context.Backend.CreateTexture3D(width, height, depth, format, storage);
        SetBacking(Backing3D);
    }

    public void SetData<T>(T[] data) where T : unmanaged => Backing3D.SetData(data);
}
