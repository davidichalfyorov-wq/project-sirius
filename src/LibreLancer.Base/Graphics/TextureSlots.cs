namespace LibreLancer.Graphics;

public sealed class TextureSlots
{
    // Matches the backend slot count (slot 8 carries the shadow atlas).
    private Texture?[] slots = new Texture[16];
    private RenderContext rc;
    internal TextureSlots(RenderContext rc)
    {
        this.rc = rc;
    }

    public Texture? this[int index]
    {
        get => slots[index];
        set
        {
            slots[index] = value;
            rc.Backend.SetTextureSlot(index, value);
        }
    }
}
