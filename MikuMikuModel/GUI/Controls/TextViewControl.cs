using System.Text;
using MikuMikuLibrary.Text;
using MikuMikuModel.Nodes;
using MikuMikuModel.Nodes.IO;
using MikuMikuModel.Nodes.Text;

namespace MikuMikuModel.GUI.Controls;

public class TextViewControl : RichTextBox
{
    private readonly TextFileNode mNode;

    protected override void OnTextChanged(EventArgs e)
    {
        if (Text != mNode.Data.Text)
        {
            mNode.Data.Text = Text;
            mNode.NotifyModified(NodeModifyFlags.Property);
        }

        base.OnTextChanged(e);
    }

    public TextViewControl(TextFileNode node)
    {
        mNode = node;
        Text = node.Data.Text;
        BorderStyle = BorderStyle.FixedSingle;
    }
}

public sealed class StreamTextViewControl : RichTextBox
{
    public StreamTextViewControl(StreamNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var stream = node.Data;
        if (stream.CanSeek)
            stream.Seek(0, SeekOrigin.Begin);

        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
        Text = reader.ReadToEnd();
        ReadOnly = true;
        BorderStyle = BorderStyle.FixedSingle;
        DetectUrls = false;
        WordWrap = false;
    }
}
