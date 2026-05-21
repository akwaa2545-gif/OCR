namespace OperatorTrainingRecord
{
    partial class frmDisqualification
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
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.btnConfirmDis = new System.Windows.Forms.Button();
            this.btnCancelDis = new System.Windows.Forms.Button();
            this.rdDis1 = new System.Windows.Forms.RadioButton();
            this.rdDis2 = new System.Windows.Forms.RadioButton();
            this.rdDis3 = new System.Windows.Forms.RadioButton();
            this.rdDis4 = new System.Windows.Forms.RadioButton();
            this.panel1 = new System.Windows.Forms.Panel();
            this.rdDis5 = new System.Windows.Forms.RadioButton();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // pictureBox1
            // 
            this.pictureBox1.Image = global::OperatorTrainingRecord.Properties.Resources.Dis;
            this.pictureBox1.Location = new System.Drawing.Point(163, 22);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(234, 53);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // btnConfirmDis
            // 
            this.btnConfirmDis.BackColor = System.Drawing.Color.MediumSeaGreen;
            this.btnConfirmDis.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.btnConfirmDis.Location = new System.Drawing.Point(548, 244);
            this.btnConfirmDis.Name = "btnConfirmDis";
            this.btnConfirmDis.Size = new System.Drawing.Size(81, 42);
            this.btnConfirmDis.TabIndex = 17;
            this.btnConfirmDis.Text = "Confirm";
            this.btnConfirmDis.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnConfirmDis.UseVisualStyleBackColor = false;
            this.btnConfirmDis.Click += new System.EventHandler(this.btnConfirmDis_Click);
            // 
            // btnCancelDis
            // 
            this.btnCancelDis.BackColor = System.Drawing.Color.LightCoral;
            this.btnCancelDis.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.btnCancelDis.Location = new System.Drawing.Point(461, 244);
            this.btnCancelDis.Name = "btnCancelDis";
            this.btnCancelDis.Size = new System.Drawing.Size(81, 42);
            this.btnCancelDis.TabIndex = 18;
            this.btnCancelDis.Text = "Cancel";
            this.btnCancelDis.UseVisualStyleBackColor = false;
            this.btnCancelDis.Click += new System.EventHandler(this.btnCancelDis_Click);
            // 
            // rdDis1
            // 
            this.rdDis1.AutoSize = true;
            this.rdDis1.Location = new System.Drawing.Point(30, 9);
            this.rdDis1.Name = "rdDis1";
            this.rdDis1.Size = new System.Drawing.Size(443, 17);
            this.rdDis1.TabIndex = 19;
            this.rdDis1.TabStop = true;
            this.rdDis1.Text = "No compliance with work specification and make high significant mistake on qualit" +
    "y issue";
            this.rdDis1.UseVisualStyleBackColor = true;
            // 
            // rdDis2
            // 
            this.rdDis2.AutoSize = true;
            this.rdDis2.Location = new System.Drawing.Point(30, 32);
            this.rdDis2.Name = "rdDis2";
            this.rdDis2.Size = new System.Drawing.Size(199, 17);
            this.rdDis2.TabIndex = 19;
            this.rdDis2.TabStop = true;
            this.rdDis2.Text = "Non-working 3 months consecutively";
            this.rdDis2.UseVisualStyleBackColor = true;
            // 
            // rdDis3
            // 
            this.rdDis3.AutoSize = true;
            this.rdDis3.Location = new System.Drawing.Point(30, 55);
            this.rdDis3.Name = "rdDis3";
            this.rdDis3.Size = new System.Drawing.Size(191, 17);
            this.rdDis3.TabIndex = 19;
            this.rdDis3.TabStop = true;
            this.rdDis3.Text = "Failed at certify or re-certify process";
            this.rdDis3.UseVisualStyleBackColor = true;
            // 
            // rdDis4
            // 
            this.rdDis4.AutoSize = true;
            this.rdDis4.Location = new System.Drawing.Point(30, 78);
            this.rdDis4.Name = "rdDis4";
            this.rdDis4.Size = new System.Drawing.Size(249, 17);
            this.rdDis4.TabIndex = 19;
            this.rdDis4.TabStop = true;
            this.rdDis4.Text = "Others such as any 4M changed or revised etc.";
            this.rdDis4.UseVisualStyleBackColor = true;
            // 
            // panel1
            // 
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(this.rdDis2);
            this.panel1.Controls.Add(this.rdDis5);
            this.panel1.Controls.Add(this.rdDis4);
            this.panel1.Controls.Add(this.rdDis1);
            this.panel1.Controls.Add(this.rdDis3);
            this.panel1.Location = new System.Drawing.Point(33, 92);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(596, 132);
            this.panel1.TabIndex = 20;
            // 
            // rdDis5
            // 
            this.rdDis5.AutoSize = true;
            this.rdDis5.Location = new System.Drawing.Point(30, 101);
            this.rdDis5.Name = "rdDis5";
            this.rdDis5.Size = new System.Drawing.Size(68, 17);
            this.rdDis5.TabIndex = 19;
            this.rdDis5.TabStop = true;
            this.rdDis5.Text = "Pregnant";
            this.rdDis5.UseVisualStyleBackColor = true;
            // 
            // frmDisqualification
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(683, 298);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.btnConfirmDis);
            this.Controls.Add(this.btnCancelDis);
            this.Controls.Add(this.pictureBox1);
            this.Name = "frmDisqualification";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Disqualification";
            this.Load += new System.EventHandler(this.frmDisqualification_Load);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Button btnConfirmDis;
        private System.Windows.Forms.Button btnCancelDis;
        private System.Windows.Forms.RadioButton rdDis1;
        private System.Windows.Forms.RadioButton rdDis2;
        private System.Windows.Forms.RadioButton rdDis3;
        private System.Windows.Forms.RadioButton rdDis4;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.RadioButton rdDis5;

    }
}