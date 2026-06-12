namespace LibreLancer.Graphics.Backends;

public interface ITexture3D : ITexture
{
    int Width { get; }
    int Height { get; }
    int Depth { get; }
    void SetData<T>(T[] data) where T : unmanaged;
}
