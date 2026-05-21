namespace OperatorTrainingRecord
{
    partial class frmDownloadExpiryDate
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
            this.btnDownloadExpiryDate = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.dateEndDate = new System.Windows.Forms.DateTimePicker();
            this.dateStartDate = new System.Windows.Forms.DateTimePicker();
            this.SuspendLayout();
            // 
            // InfoLabel
            // 
            this.InfoLabel.AutoSize = true;
            this.InfoLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.InfoLabel.Location = new System.Drawing.Point(12, 111);
            this.InfoLabel.Name = "InfoLabel";
            this.InfoLabel.Size = new System.Drawing.Size(67, 15);
            this.InfoLabel.TabIndex = 9;
            this.InfoLabel.Text = "Total data :";
            // 
            // btnDownloadExpiryDate
            // 
            this.btnDownloadExpiryDate.BackColor = System.Drawing.Color.Pink;
            this.btnDownloadExpiryDate.Location = new System.Drawing.Point(339, 92);
            this.btnDownloadExpiryDate.Name = "btnDownloadExpiryDate";
            this.btnDownloadExpiryDate.Size = new System.Drawing.Size(106, 31);
            this.btnDownloadExpiryDate.TabIndex = 8;
            this.btnDownloadExpiryDate.Text = "Download";
            this.btnDownloadExpiryDate.UseVisualStyleBackColor = false;
            this.btnDownloadExpiryDate.Click += new System.EventHandler(this.btnDownloadExpiryDate_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(247, 13);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(26, 13);
            this.label2.TabIndex = 17;
            this.label2.Text = "End";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(24, 13);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(29, 13);
            this.label1.TabIndex = 18;
            this.label1.Text = "Start";
            // 
            // dateEndDate
            // 
            this.dateEndDate.CustomFormat = "dd MMMM   yyy";
            this.dateEndDate.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.dateEndDate.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.dateEndDate.Location = new System.Drawing.Point(250, 35);
            this.dateEndDate.Name = "dateEndDate";
            this.dateEndDate.Size = new System.Drawing.Size(195, 26);
            this.dateEndDate.TabIndex = 15;
            this.dateEndDate.Value = new System.DateTime(2018, 3, 15, 0, 0, 0, 0);
            // 
            // dateStartDate
            // 
            this.dateStartDate.CustomFormat = "dd MMMM   yyy";
            this.dateStartDate.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.dateStartDate.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.dateStartDate.Location = new System.Drawing.Point(24, 35);
            this.dateStartDate.Name = "dateStartDate";
            this.dateStartDate.Size = new System.Drawing.Size(195, 26);
            this.dateStartDate.TabIndex = 16;
            this.dateStartDate.Value = new System.DateTime(2018, 3, 15, 0, 0, 0, 0);
            // 
            // frmDownloadExpiryDate
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(471, 135);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.dateEndDate);
            this.Controls.Add(this.dateStartDate);
            this.Controls.Add(this.InfoLabel);
            this.Controls.Add(this.btnDownloadExpiryDate);
            this.Name = "frmDownloadExpiryDate";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Download Expiry Date";
           // this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.frmDownloadExpiryDate_FormClosing);
            //this.Load += new System.EventHandler(this.frmDownloadExpiryDate_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label InfoLabel;
        private System.Windows.Forms.Button btnDownloadExpiryDate;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.DateTimePicker dateEndDate;
        private System.Windows.Forms.DateTimePicker dateStartDate;
    }
}