using MikuMikuLibrary.Selection;
using MikuMikuModel.GUI.Controls;
using MikuMikuModel.Nodes.IO;
using MikuMikuModel.Resources;

namespace MikuMikuModel.Nodes.Selection;

public sealed class FgoSelCommonTableNode : BinaryFileNode<FgoSelCommonTable>
{
    public override NodeFlags Flags => NodeFlags.None;
    public override Bitmap Image => ResourceStore.LoadBitmap("Icons/File.png");
    public override Control Control => new FgoSelectionTableViewControl(Data);

    protected override void Initialize()
    {
        AddCustomHandler("Export", ExportBinary);
        AddCustomHandler("Export (JSON)", ExportJson);
    }
    protected override void PopulateCore() { }
    protected override void SynchronizeCore() { }

    private void ExportBinary()
    {
        using var dialog = new SaveFileDialog
        {
            AutoUpgradeEnabled = true,
            CheckPathExists = true,
            FileName = Name,
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
            OverwritePrompt = true,
            Title = "Select a binary file to export to.",
            ValidateNames = true,
            AddExtension = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            File.WriteAllBytes(dialog.FileName, Data.OriginalBytes);
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

    public FgoSelCommonTableNode(string name, FgoSelCommonTable data) : base(name, data) { }
    public FgoSelCommonTableNode(string name, Func<Stream> streamGetter) : base(name, streamGetter) { }
}
