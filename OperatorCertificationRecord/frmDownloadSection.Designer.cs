namespace OperatorTrainingRecord
{
    partial class frmDownloadSection
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.InfoLabel = new System.Windows.Forms.Label();
            this.btnDownloadSection = new System.Windows.Forms.Button();
            this.cbbDownLoadSection = new System.Windows.Forms.ComboBox();
            this.SuspendLayout();
            // 
            // InfoLabel
            // 
            this.InfoLabel.AutoSize = true;
            this.InfoLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.InfoLabel.Location = new System.Drawing.Point(233, 67);
            this.InfoLabel.Name = "InfoLabel";
            this.InfoLabel.Size = new System.Drawing.Size(67, 15);
            this.InfoLabel.TabIndex = 9;
            this.InfoLabel.Text = "Total data :";
            // 
            // btnDownloadSection
            // 
            this.btnDownloadSection.BackColor = System.Drawing.Color.Pink;
            this.btnDownloadSection.Location = new System.Drawing.Point(194, 24);
            this.btnDownloadSection.Name = "btnDownloadSection";
            this.btnDownloadSection.Size = new System.Drawing.Size(106, 31);
            this.btnDownloadSection.TabIndex = 8;
            this.btnDownloadSection.Text = "Download";
            this.btnDownloadSection.UseVisualStyleBackColor = false;
            this.btnDownloadSection.Click += new System.EventHandler(this.btnDownloadSection_Click);
            // 
            // cbbDownLoadSection
            // 
            this.cbbDownLoadSection.FormattingEnabled = true;
            this.cbbDownLoadSection.Location = new System.Drawing.Point(33, 30);
            this.cbbDownLoadSection.Name = "cbbDownLoadSection";
            this.cbbDownLoadSection.Size = new System.Drawing.Size(155, 21);
            this.cbbDownLoadSection.TabIndex = 7;
            this.cbbDownLoadSection.Text = "By each Section";
            // 
            // frmDownloadSection
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(333, 106);
            this.Controls.Add(this.InfoLabel);
            this.Controls.Add(this.btnDownloadSection);
            this.Controls.Add(this.cbbDownLoadSection);
            this.MaximumSize = new System.Drawing.Size(349, 145);
            this.MinimumSize = new System.Drawing.Size(349, 145);
            this.Name = "frmDownloadSection";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Download data by Section";
            this.Load += new System.EventHandler(this.frmDownloadSection_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label InfoLabel;
        private System.Windows.Forms.Button btnDownloadSection;
        private System.Windows.Forms.ComboBox cbbDownLoadSection;

    }
}