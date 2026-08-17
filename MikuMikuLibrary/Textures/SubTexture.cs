using MikuMikuLibrary.IO.Common;

namespace MikuMikuLibrary.Textures;

public class SubTexture
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public TextureFormat Format { get; private set; }
    public byte[] Data { get; private set; }

    internal void Read(EndianBinaryReader reader)
    {
        int signature = reader.ReadInt32();

        if (signature != 0x02505854)
            throw new InvalidDataException("Invalid signature (expected TXP with type 2)");

        Width = reader.ReadInt32();
        Height = reader.ReadInt32();
        int format = reader.ReadInt32();
        // FGO Arcade stores BC7 textures with its title-specific format IDs
        // 130 and 131. Normalize them to the library's canonical BC7 value so
        // the native decoder does not treat the data as an unknown format.
        Format = format is 130 or 131 ? TextureFormat.BC7 : (TextureFormat)format;
        reader.SeekCurrent(4); // ID

        int dataSize = reader.ReadInt32();
        Data = reader.ReadBytes(dataSize);
    }

    internal void Write(EndianBinaryWriter writer, int id)
    {
        writer.Write(0x02505854);
        writer.Write(Width);
        writer.Write(Height);
        writer.Write((int)Format);
        writer.Write(id);
        writer.Write(Data.Length);
        writer.Write(Data);
    }

    internal SubTexture(EndianBinaryReader reader)
    {
        Read(reader);
    }

    public SubTexture(int width, int height, TextureFormat format)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Format = format;
        Data = new byte[TextureFormatUtilities.CalculateDataSize(width, height, format)];
    }
}
