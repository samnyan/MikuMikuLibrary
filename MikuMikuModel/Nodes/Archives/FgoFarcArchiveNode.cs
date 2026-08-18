using MikuMikuLibrary.Archives;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.MasterTables;
using MikuMikuModel.Modules;
using MikuMikuModel.Nodes.IO;
using MikuMikuModel.Resources;

namespace MikuMikuModel.Nodes.Archives;

/// <summary>Tree node for a writable FGO Arcade FARc archive.</summary>
public sealed class FgoFarcArchiveNode : BinaryFileNode<FgoFarcArchive>
{
    public override NodeFlags Flags =>
        NodeFlags.Add | NodeFlags.Remove | NodeFlags.Move | NodeFlags.Export |
        NodeFlags.Replace | NodeFlags.Rename;

    public override Bitmap Image => ResourceStore.LoadBitmap("Icons/Archive.png");

    protected override void Initialize()
    {
        AddImportHandler<Stream>(filePath => Data.Add(Path.GetFileName(filePath), filePath, ConflictPolicy.RaiseError));
        AddExportHandler<FgoFarcArchive>(filePath => Data.Save(filePath));
        AddReplaceHandler<FgoFarcArchive>(BinaryFile.Load<FgoFarcArchive>);
    }

    protected override void PopulateCore()
    {
        foreach (string fileName in Data)
        {
            var module = ModuleImportUtilities.GetModule(
                fileName, () => Data.Open(fileName, EntryStreamMode.OriginalStream));
            INode node;

            if (module != null && typeof(IBinaryFile).IsAssignableFrom(module.ModelType) &&
                NodeFactory.NodeTypes.ContainsKey(module.ModelType))
            {
                node = NodeFactory.Create(module.ModelType, fileName,
                    new Func<Stream>(() => Data.Open(fileName, EntryStreamMode.MemoryStream)));
            }
            else if (IsFgoMasterTable(fileName))
            {
                node = NodeFactory.Create(typeof(FgoMasterTable), fileName,
                    new Func<Stream>(() => Data.Open(fileName, EntryStreamMode.MemoryStream)));
            }
            else
            {
                // Keep the decoded entry lazy. This avoids retaining an archive
                // entry stream for every file while the tree is being scanned.
                node = new StreamNode(fileName,
                    () => Data.Open(fileName, EntryStreamMode.OriginalStream));
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
        foreach (INode node in Nodes)
        {
            switch (node)
            {
                case IDirtyNode dirtyNode when dirtyNode.IsDirty:
                    Data.Add(dirtyNode.Name, dirtyNode.GetStream(), false, ConflictPolicy.Replace);
                    break;
                case StreamNode streamNode:
                    Data.Add(streamNode.Name, streamNode.Data, true, ConflictPolicy.Replace);
                    break;
            }
        }

        foreach (string entryName in Data.FileNames
                     .Except(Nodes.Select(x => x.Name), StringComparer.InvariantCultureIgnoreCase)
                     .ToList())
            Data.Remove(entryName);
    }

    protected override void OnExport(string filePath)
    {
        foreach (INode node in Nodes)
        {
            if (node.Data is not Stream stream)
                continue;
            stream.Close();
            node.Replace(Data.Open(node.Name, EntryStreamMode.OriginalStream));
        }
        base.OnExport(filePath);
    }

    public FgoFarcArchiveNode(string name, FgoFarcArchive data) : base(name, data) { }
    public FgoFarcArchiveNode(string name, Func<Stream> streamGetter) : base(name, streamGetter) { }
}
