namespace OperatorTrainingRecord
{
    partial class frmDownloadResignDate
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
            this.btnDownloadResign = new System.Windows.Forms.Button();
            this.dateStartDate = new System.Windows.Forms.DateTimePicker();
            this.dateEndDate = new System.Windows.Forms.DateTimePicker();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // InfoLabel
            // 
            this.InfoLabel.AutoSize = true;
            this.InfoLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.InfoLabel.Location = new System.Drawing.Point(12, 111);
            this.InfoLabel.Name = "InfoLabel";
            this.InfoLabel.Size = new System.Drawing.Size(67, 15);
            this.InfoLabel.TabIndex = 12;
            this.InfoLabel.Text = "Total data :";
            // 
            // btnDownloadResign
            // 
            this.btnDownloadResign.BackColor = System.Drawing.Color.Pink;
            this.btnDownloadResign.Location = new System.Drawing.Point(339, 92);
            this.btnDownloadResign.Name = "btnDownloadResign";
            this.btnDownloadResign.Size = new System.Drawing.Size(106, 31);
            this.btnDownloadResign.TabIndex = 11;
            this.btnDownloadResign.Text = "Download";
            this.btnDownloadResign.UseVisualStyleBackColor = false;
            this.btnDownloadResign.Click += new System.EventHandler(this.btnDownloadResign_Click);
            // 
            // dateStartDate
            // 
            this.dateStartDate.CustomFormat = "dd MMMM   yyy";
            this.dateStartDate.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.dateStartDate.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.dateStartDate.Location = new System.Drawing.Point(24, 35);
            this.dateStartDate.Name = "dateStartDate";
            this.dateStartDate.Size = new System.Drawing.Size(195, 26);
            this.dateStartDate.TabIndex = 13;
            this.dateStartDate.Value = new System.DateTime(2018, 3, 15, 0, 0, 0, 0);
            // 
            // dateEndDate
            // 
            this.dateEndDate.CustomFormat = "dd MMMM   yyy";
            this.dateEndDate.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.dateEndDate.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.dateEndDate.Location = new System.Drawing.Point(250, 35);
            this.dateEndDate.Name = "dateEndDate";
            this.dateEndDate.Size = new System.Drawing.Size(195, 26);
            this.dateEndDate.TabIndex = 13;
            this.dateEndDate.Value = new System.DateTime(2018, 3, 15, 0, 0, 0, 0);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(24, 13);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(29, 13);
            this.label1.TabIndex = 14;
            this.label1.Text = "Start";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(247, 13);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(26, 13);
            this.label2.TabIndex = 14;
            this.label2.Text = "End";
            // 
            // frmDownloadResignDate
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(471, 135);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.dateEndDate);
            this.Controls.Add(this.dateStartDate);
            this.Controls.Add(this.InfoLabel);
            this.Controls.Add(this.btnDownloadResign);
            this.Name = "frmDownloadResignDate";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Download Resign Date";
            //this.Load += new System.EventHandler(this.frmDownloadResignDate_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label InfoLabel;
        private System.Windows.Forms.Button btnDownloadResign;
        private System.Windows.Forms.DateTimePicker dateStartDate;
        private System.Windows.Forms.DateTimePicker dateEndDate;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
    }
}