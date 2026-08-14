using MikuMikuLibrary.Archives;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.MasterTables;
using MikuMikuModel.Modules;
using MikuMikuModel.Nodes.IO;
using MikuMikuModel.Resources;

namespace MikuMikuModel.Nodes.Archives;

/// <summary>
/// Tree node for read-only FGO Arcade FARc archives.
/// </summary>
public sealed class FgoFarcArchiveNode : BinaryFileNode<FgoFarcArchive>
{
    // NodeTreeView uses Add as the marker for lazily populated container nodes.
    // The node has no add handler, so this does not make the read-only archive writable.
    public override NodeFlags Flags => NodeFlags.Add;

    public override Bitmap Image =>
        ResourceStore.LoadBitmap("Icons/Archive.png");

    protected override void Initialize()
    {
        // FGO FARc files are currently read-only. Child entries can still be
        // inspected and exported through their StreamNode instances.
    }

    protected override void PopulateCore()
    {
        foreach (string fileName in Data)
        {
            var module = ModuleImportUtilities.GetModule(
                fileName,
                () => Data.Open(fileName, EntryStreamMode.OriginalStream));

            INode node;

            if (module != null && typeof(IBinaryFile).IsAssignableFrom(module.ModelType) &&
                NodeFactory.NodeTypes.ContainsKey(module.ModelType))
            {
                node = NodeFactory.Create(
                    module.ModelType,
                    fileName,
                    new Func<Stream>(() => Data.Open(fileName, EntryStreamMode.MemoryStream)));
            }
            else if (IsFgoMasterTable(fileName))
            {
                node = NodeFactory.Create(
                    typeof(FgoMasterTable),
                    fileName,
                    new Func<Stream>(() => Data.Open(fileName, EntryStreamMode.MemoryStream)));
            }
            else
            {
                node = new StreamNode(fileName, Data.Open(fileName, EntryStreamMode.OriginalStream));
            }

            Nodes.Add(node);
        }
    }

    private bool IsFgoMasterTable(string fileName)
    {
        if (Path.GetFileName(fileName).StartsWith("arms_mst_", StringComparison.OrdinalIgnoreCase))
            return true;

        using var stream = Data.Open(fileName, EntryStreamMode.OriginalStream);
        return FgoMasterTable.IsTextTable(stream);
    }

    protected override void SynchronizeCore()
    {
        // FGO FARc archives are read-only; there is nothing to write back.
    }

    public FgoFarcArchiveNode(string name, FgoFarcArchive data) : base(name, data)
    {
    }

    public FgoFarcArchiveNode(string name, Func<Stream> streamGetter) : base(name, streamGetter)
    {
    }
}
