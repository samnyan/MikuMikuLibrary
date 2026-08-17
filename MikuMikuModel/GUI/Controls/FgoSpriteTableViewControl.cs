using MikuMikuLibrary.Aets.Resources;

namespace MikuMikuModel.GUI.Controls;

/// <summary>Read-only grid view for an FGO Sprite table.</summary>
public sealed class FgoSpriteTableViewControl : UserControl
{
    private readonly DataGridView mGrid = new();

    public FgoSpriteTableViewControl(FgoSpriteTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        InitializeGrid();

        mGrid.Columns.Add("Index", "Index");
        mGrid.Columns.Add("Name", "Sprite");
        mGrid.Columns.Add("Texture", "Texture");
        mGrid.Columns.Add("TextureIndex", "Texture index");
        mGrid.Columns.Add("Atlas", "Atlas");
        mGrid.Columns.Add("Rectangle", "Rectangle");
        mGrid.Columns.Add("ResourceIndex", "Resource index");

        foreach (var entry in table.Entries)
        {
            mGrid.Rows.Add(
                entry.Index,
                entry.Name,
                entry.TextureName,
                entry.TextureIndex,
                $"{entry.AtlasWidth}x{entry.AtlasHeight}",
                $"{entry.X0},{entry.Y0} - {entry.X1},{entry.Y1}",
                entry.ResourceIndex);
        }
    }

    private void InitializeGrid()
    {
        Dock = DockStyle.Fill;
        mGrid.Dock = DockStyle.Fill;
        mGrid.ReadOnly = true;
        mGrid.AllowUserToAddRows = false;
        mGrid.AllowUserToDeleteRows = false;
        mGrid.AllowUserToResizeRows = false;
        mGrid.AutoGenerateColumns = false;
        mGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        mGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
        mGrid.RowHeadersVisible = false;
        mGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        mGrid.MultiSelect = false;
        Controls.Add(mGrid);
    }
}
