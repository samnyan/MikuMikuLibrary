using MikuMikuLibrary.MasterTables;
using MikuMikuLibrary.Selection;
using MikuMikuModel.GUI.Controls;
using MikuMikuModel.Nodes.IO;
using MikuMikuModel.Resources;

namespace MikuMikuModel.Nodes.MasterTables;

public sealed class FgoMasterTableNode : BinaryFileNode<FgoMasterTable>
{
    public override NodeFlags Flags => NodeFlags.None;
    public override Bitmap Image => ResourceStore.LoadBitmap("Icons/File.png");
    public override Control Control => new FgoSelectionTableViewControl(Data);

    protected override void Initialize()
    {
        AddCustomHandler("Export", ExportText);
        AddCustomHandler("Export (JSON)", ExportJson);
    }

    protected override void PopulateCore() { }
    protected override void SynchronizeCore() { }

    private void ExportText()
    {
        using var dialog = new SaveFileDialog
        {
            AutoUpgradeEnabled = true,
            CheckPathExists = true,
            FileName = Name,
            Filter = "FGO table files (*.bin;*.prop)|*.bin;*.prop|All files (*.*)|*.*",
            OverwritePrompt = true,
            Title = "Select a table file to export to.",
            ValidateNames = true,
            AddExtension = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            File.WriteAllText(dialog.FileName, Data.Text, new UTF8Encoding(false));
    }

    private void ExportJson()
    {
        using var dialog = new SaveFileDialog
        {
            AutoUpgradeEnabled = true,
            CheckPathExists = true,
            FileName = Path.ChangeExtension(Name, ".json"),
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            OverwritePrompt = true,
            Title = "Select a JSON file to export to.",
            ValidateNames = true,
            AddExtension = true,
            DefaultExt = "json"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            File.WriteAllText(dialog.FileName, FgoSelectionJsonExporter.Serialize(Data));
    }

    public FgoMasterTableNode(string name, FgoMasterTable data) : base(name, data) { }
    public FgoMasterTableNode(string name, Func<Stream> streamGetter) : base(name, streamGetter) { }
}
