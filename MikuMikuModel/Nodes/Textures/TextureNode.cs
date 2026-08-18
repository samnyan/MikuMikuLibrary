using System.Drawing.Imaging;
using MikuMikuLibrary.Textures;
using MikuMikuLibrary.Textures.Processing;
using MikuMikuModel.GUI.Controls;
using MikuMikuModel.GUI.Forms;
using MikuMikuModel.Modules;
using MikuMikuModel.Nodes.TypeConverters;
using MikuMikuModel.Resources;

namespace MikuMikuModel.Nodes.Textures;

public class TextureNode : Node<Texture>
{
    private bool mEncodingInProgress;
    public override NodeFlags Flags => NodeFlags.Rename | NodeFlags.Export | NodeFlags.Replace;
    public override Bitmap Image => ResourceStore.LoadBitmap("Icons/Texture.png");

    public override Control Control
    {
        get
        {
            TextureViewControl.Instance.SetTexture(Data);
            return TextureViewControl.Instance;
        }
    }

    [Category("General")]
    [TypeConverter(typeof(IdTypeConverter))]
    public uint Id
    {
        get => GetProperty<uint>();
        set => SetProperty(value);
    }

    [Category("General")] public int Width => GetProperty<int>();
    [Category("General")] public int Height => GetProperty<int>();
    [Category("General")] public TextureFormat Format => GetProperty<TextureFormat>();

    private ImageFormat GetImageFormat(string filePath)
    {
        string extension = Path.GetExtension(filePath).Trim('.').ToLowerInvariant();
        switch (extension)
        {
            case "png":
                return ImageFormat.Png;

            case "jpg":
            case "jpeg":
                return ImageFormat.Jpeg;

            case "gif":
                return ImageFormat.Gif;

            case "bmp":
                return ImageFormat.Bmp;

            default:
                throw new ArgumentException("Image format could not be detected", nameof(filePath));
        }
    }

    private void EncodeTexture(TextureFormat format, bool ycbcr, bool flipped)
    {
        string filePath = ModuleImportUtilities.SelectModuleImport<Bitmap>();

        if (string.IsNullOrEmpty(filePath))
            return;

        TextureFormat targetFormat = format != TextureFormat.Unknown ? format : Format;
        bool generateMipMaps = Data.MipMapCount > 1;
        StartEncoding("Encoding texture...", () =>
        {
            using var bitmap = new Bitmap(filePath);
            if (flipped)
                bitmap.RotateFlip(RotateFlipType.Rotate180FlipX);

            return ycbcr
                ? TextureEncoder.EncodeYCbCrFromBitmap(bitmap)
                : TextureEncoder.EncodeFromBitmap(bitmap, targetFormat, generateMipMaps);
        });
    }

    protected override void Initialize()
    {
        AddExportHandler<Texture>(filePath => TextureDecoder.DecodeToFile(Data, filePath));
        AddReplaceHandler<Texture>(filePath =>
        {
            bool ycbcr = Data.IsYCbCr;
            TextureFormat format = Data.Format;
            bool generateMipMaps = Data.MipMapCount > 1;
            StartEncoding("Encoding texture...", () => ycbcr
                ? TextureEncoder.EncodeYCbCrFromFile(filePath)
                : TextureEncoder.EncodeFromFile(filePath, format, generateMipMaps));
            return null;
        });

        AddCustomHandlerSeparator();
        AddCustomHandler("Export flipped", () =>
        {
            string filePath = ModuleExportUtilities.SelectModuleExport<Bitmap>("Select a file to export to.", Name);

            if (string.IsNullOrEmpty(filePath))
                return;

            var imageFormat = GetImageFormat(filePath);

            using (var bitmap = TextureDecoder.DecodeToBitmap(Data))
            {
                bitmap.RotateFlip(RotateFlipType.Rotate180FlipX);
                bitmap.Save(filePath, imageFormat);
            }
        });

        AddCustomHandler("Replace flipped", () => EncodeTexture(TextureFormat.Unknown, Data.IsYCbCr, true));

        AddCustomHandlerSeparator();

        AddCustomHandler("Replace as...");
        {
            AppendCustomHandler("Uncompressed", () => EncodeTexture(TextureFormat.RGBA8, false, false));
            AppendCustomHandler("DXT1/DXT5", () => EncodeTexture(TextureFormat.DXT5, false, false));
            AppendCustomHandler("ATI1", () => EncodeTexture(TextureFormat.ATI1, false, false));
            AppendCustomHandler("ATI2", () => EncodeTexture(TextureFormat.ATI2, false, false));
            AppendCustomHandler("YCbCr", () => EncodeTexture(TextureFormat.Unknown, true, false));
            AppendCustomHandler("BC7 (MM+ Only)", () => EncodeTexture(TextureFormat.BC7, false, false));
        }

        AddCustomHandler("Replace as... (flipped)");
        {
            AppendCustomHandler("Uncompressed", () => EncodeTexture(TextureFormat.RGBA8, false, true));
            AppendCustomHandler("DXT1/DXT5", () => EncodeTexture(TextureFormat.DXT5, false, true));
            AppendCustomHandler("ATI1", () => EncodeTexture(TextureFormat.ATI1, false, true));
            AppendCustomHandler("ATI2", () => EncodeTexture(TextureFormat.ATI2, false, true));
            AppendCustomHandler("YCbCr", () => EncodeTexture(TextureFormat.Unknown, true, true));
            AppendCustomHandler("BC7 (MM+ Only)", () => EncodeTexture(TextureFormat.BC7, false, true));
        }
    }

    protected override void PopulateCore()
    {
    }

    protected override void SynchronizeCore()
    {
    }

    protected override void OnReplace(Texture previousData)
    {
        Data.Name = previousData.Name;
        Data.Id = previousData.Id;

        // FGO stores BC7 with title-specific wire identifiers (130/131).
        // TextureEncoder exposes the canonical BC7 enum, so carry the source
        // identifier across image replacement before TextureSet serialization.
        if (Data.Format == previousData.Format &&
            Data.ArraySize == previousData.ArraySize &&
            Data.MipMapCount == previousData.MipMapCount)
        {
            for (int arrayIndex = 0; arrayIndex < Data.ArraySize; arrayIndex++)
            for (int mipMapIndex = 0; mipMapIndex < Data.MipMapCount; mipMapIndex++)
                Data[arrayIndex, mipMapIndex].PreserveSerializedFormat(
                    previousData[arrayIndex, mipMapIndex]);
        }

        base.OnReplace(previousData);
    }

    private void StartEncoding<T>(string message, Func<T> operation)
    {
        if (mEncodingInProgress)
        {
            MessageBox.Show("A texture is already being encoded.", Program.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        mEncodingInProgress = true;
        var form = new TextureEncodingProgressForm(message);
        form.Show();
        _ = CompleteEncodingAsync(form, operation);
    }

    private async Task CompleteEncodingAsync<T>(TextureEncodingProgressForm form, Func<T> operation)
    {
        try
        {
            T result = await Task.Run(operation);
            if (result is Texture texture)
                Replace(texture);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Texture encoding failed: {exception.Message}", Program.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            mEncodingInProgress = false;
            if (!form.IsDisposed)
            {
                form.Close();
                form.Dispose();
            }
        }
    }

    public TextureNode(string name, Texture data) : base(name, data)
    {
    }
}
