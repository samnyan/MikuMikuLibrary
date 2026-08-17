using MikuMikuModel.Nodes.IO;
using MikuMikuModel.Configurations;
using MikuMikuModel.Resources;

namespace MikuMikuModel.Nodes.FileSystem;

/// <summary>
/// A physical directory represented as a lazily populated node.
/// Only direct children are enumerated when the directory is expanded.
/// </summary>
public sealed class DirectoryNode : Node<DirectoryInfo>
{
    private readonly DirectoryInfo mDirectory;

    private static readonly EnumerationOptions sEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public override NodeFlags Flags => NodeFlags.Add;
    public override Bitmap Image => ResourceStore.LoadBitmap("Icons/Folder.png");

    [Category("General")]
    public string FullPath => mDirectory.FullName;

    protected override void Initialize()
    {
        AddCustomHandler("Refresh", () => NotifyModified(NodeModifyFlags.Collection));
    }

    protected override void PopulateCore()
    {
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = mDirectory.EnumerateFileSystemInfos("*", sEnumerationOptions).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not enumerate directory '{FullPath}': {exception.Message}");
            return;
        }

        foreach (var directory in entries.OfType<DirectoryInfo>().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                ConfigurationList.Instance.DetermineCurrentConfiguration(directory.FullName);
                Nodes.Add(new DirectoryNode(directory));
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Could not add directory '{directory.FullName}': {exception.Message}");
            }
        }

        foreach (var file in entries.OfType<FileInfo>().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Nodes.Add(NodeFactory.CreateFileNode(file.FullName));
            }
            catch (Exception exception)
            {
                // Keep a problematic file visible without opening it during the scan.
                Nodes.Add(new StreamNode(file.Name, () => File.OpenRead(file.FullName), file.FullName));
                System.Diagnostics.Debug.WriteLine($"Could not classify file '{file.FullName}': {exception.Message}");
            }
        }
    }

    protected override void SynchronizeCore()
    {
    }

    public DirectoryNode(DirectoryInfo data) : base(GetName(data), data)
    {
        mDirectory = data;
    }

    public DirectoryNode(string path) : this(new DirectoryInfo(Path.GetFullPath(path)))
    {
    }

    private static string GetName(DirectoryInfo directory)
    {
        string path = directory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(path) ? directory.FullName : Path.GetFileName(path);
    }
}
