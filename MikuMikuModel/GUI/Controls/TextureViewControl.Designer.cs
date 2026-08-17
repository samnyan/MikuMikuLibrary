namespace MikuMikuModel.GUI.Controls
{
    partial class TextureViewControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer mComponents = null;
        
        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.mStatusStrip = new System.Windows.Forms.StatusStrip();
            this.mFlipCheckBox = new System.Windows.Forms.CheckBox();
            this.mFormatLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.mSizeLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.mMipMapLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.mLevelLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.mStatusStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // mStatusStrip
            // 
            this.mStatusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mFormatLabel,
            this.mSizeLabel,
            this.mMipMapLabel,
            this.mLevelLabel});
            this.mStatusStrip.Location = new System.Drawing.Point(0, 340);
            this.mStatusStrip.Name = "mStatusStrip";
            this.mStatusStrip.Size = new System.Drawing.Size(538, 22);
            this.mStatusStrip.SizingGrip = false;
            this.mStatusStrip.TabIndex = 0;
            this.mStatusStrip.Text = "statusStrip1";
            //
            // mFlipCheckBox
            //
            this.mFlipCheckBox.AutoSize = true;
            this.mFlipCheckBox.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.mFlipCheckBox.Height = 22;
            this.mFlipCheckBox.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.mFlipCheckBox.Name = "mFlipCheckBox";
            this.mFlipCheckBox.Padding = new System.Windows.Forms.Padding(4, 0, 0, 0);
            this.mFlipCheckBox.TabIndex = 1;
            this.mFlipCheckBox.Text = "Flip";
            this.mFlipCheckBox.UseVisualStyleBackColor = true;
            this.mFlipCheckBox.CheckedChanged += new System.EventHandler(this.OnFlipCheckedChanged);
            // 
            // mFormatLabel
            // 
            this.mFormatLabel.Name = "mFormatLabel";
            this.mFormatLabel.Size = new System.Drawing.Size(130, 17);
            this.mFormatLabel.Spring = true;
            this.mFormatLabel.Text = "Format";
            // 
            // mSizeLabel
            // 
            this.mSizeLabel.Name = "mSizeLabel";
            this.mSizeLabel.Size = new System.Drawing.Size(130, 17);
            this.mSizeLabel.Spring = true;
            this.mSizeLabel.Text = "Size";
            // 
            // mMipMapLabel
            // 
            this.mMipMapLabel.Name = "mMipMapLabel";
            this.mMipMapLabel.Size = new System.Drawing.Size(130, 17);
            this.mMipMapLabel.Spring = true;
            this.mMipMapLabel.Text = "MipMap";
            // 
            // mLevelLabel
            // 
            this.mLevelLabel.Name = "mLevelLabel";
            this.mLevelLabel.Size = new System.Drawing.Size(130, 17);
            this.mLevelLabel.Spring = true;
            this.mLevelLabel.Text = "Level";
            // 
            // TextureViewControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.mFlipCheckBox);
            this.Controls.Add(this.mStatusStrip);
            this.DoubleBuffered = true;
            this.Name = "TextureViewControl";
            this.Size = new System.Drawing.Size(538, 362);
            this.mStatusStrip.ResumeLayout(false);
            this.mStatusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.StatusStrip mStatusStrip;
        private System.Windows.Forms.CheckBox mFlipCheckBox;
        private System.Windows.Forms.ToolStripStatusLabel mFormatLabel;
        private System.Windows.Forms.ToolStripStatusLabel mSizeLabel;
        private System.Windows.Forms.ToolStripStatusLabel mMipMapLabel;
        private System.Windows.Forms.ToolStripStatusLabel mLevelLabel;
    }
}
