using MikuMikuModel.Resources;

namespace MikuMikuModel.Nodes.IO;

public class StreamNode : Node<Stream>
{
    private Func<Stream> mStreamGetter;
    private Stream mStream;

    public override NodeFlags Flags =>
        NodeFlags.Export | NodeFlags.Move | NodeFlags.Remove | NodeFlags.Rename | NodeFlags.Replace;

    public override Bitmap Image =>
        ResourceStore.LoadBitmap("Icons/File.png");

    [Category("General")]
    public string FilePath { get; }

    protected override Stream InternalData
    {
        get
        {
            if (mStream != null)
                return mStream;

            if (mStreamGetter != null)
                return mStream = mStreamGetter();

            return base.InternalData;
        }
    }

    protected override void Initialize()
    {
        AddRawExportHandler(filePath =>
        {
            using var stream = File.Create(filePath);
            if (Data.CanSeek)
                Data.Seek(0, SeekOrigin.Begin);

            Data.CopyTo(stream);
        });
        AddRawReplaceHandler(File.OpenRead);
    }

    protected override void PopulateCore()
    {
    }

    protected override void SynchronizeCore()
    {
    }

    protected override void OnReplace(Stream previousData)
    {
        mStream?.Dispose();
        mStream = null;
        mStreamGetter = null;
        base.OnReplace(previousData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            mStream?.Dispose();

        base.Dispose(disposing);
    }

    public StreamNode(string name, Stream data) : base(name, data)
    {
        mStream = data;
    }

    public StreamNode(string name, Func<Stream> streamGetter) : this(name, streamGetter, null)
    {
    }

    public StreamNode(string name, Func<Stream> streamGetter, string filePath) : base(name, Stream.Null)
    {
        mStreamGetter = streamGetter ?? throw new ArgumentNullException(nameof(streamGetter));
        FilePath = filePath;
    }
}
