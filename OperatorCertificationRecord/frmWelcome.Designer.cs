namespace OperatorTrainingRecord
{
    partial class frmWelcome
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
            this.components = new System.ComponentModel.Container();
            this.label1 = new System.Windows.Forms.Label();
            this.txtSearchUser = new System.Windows.Forms.TextBox();
            this.grbDownloadDB = new System.Windows.Forms.GroupBox();
            this.label2 = new System.Windows.Forms.Label();
            this.lblUser = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.lblDept = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.lblDate = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.lblTime = new System.Windows.Forms.Label();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.InfoLabel = new System.Windows.Forms.Label();
            this.picAddEmp = new System.Windows.Forms.PictureBox();
            this.pictureBox2 = new System.Windows.Forms.PictureBox();
            this.picDownloadSection = new System.Windows.Forms.PictureBox();
            this.picDownLoadDisqualification = new System.Windows.Forms.PictureBox();
            this.picDownloadResignDate = new System.Windows.Forms.PictureBox();
            this.picDownloadExpiryDate = new System.Windows.Forms.PictureBox();
            this.PicNoSkill = new System.Windows.Forms.PictureBox();
            this.picObsoleted = new System.Windows.Forms.PictureBox();
            this.picDownLoadAllDept = new System.Windows.Forms.PictureBox();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.pictureBox3 = new System.Windows.Forms.PictureBox();
            this.grbDownloadDB.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.picAddEmp)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDownloadSection)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDownLoadDisqualification)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDownloadResignDate)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDownloadExpiryDate)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.PicNoSkill)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picObsoleted)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDownLoadAllDept)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox3)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.label1.Location = new System.Drawing.Point(410, 246);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(428, 37);
            this.label1.TabIndex = 0;
            this.label1.Text = "Operator\'s training Database";
            // 
            // txtSearchUser
            // 
            this.txtSearchUser.BackColor = System.Drawing.Color.White;
            this.txtSearchUser.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.txtSearchUser.Location = new System.Drawing.Point(543, 313);
            this.txtSearchUser.MaxLength = 7;
            this.txtSearchUser.Multiline = true;
            this.txtSearchUser.Name = "txtSearchUser";
            this.txtSearchUser.Size = new System.Drawing.Size(226, 28);
            this.txtSearchUser.TabIndex = 0;
            this.txtSearchUser.Text = "Enter the employee number";
            this.txtSearchUser.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.txtSearchUser_KeyPress);
            this.txtSearchUser.KeyUp += new System.Windows.Forms.KeyEventHandler(this.txtSearchUser_KeyUp);
            // 
            // grbDownloadDB
            // 
            this.grbDownloadDB.BackColor = System.Drawing.Color.PaleTurquoise;
            this.grbDownloadDB.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.grbDownloadDB.Controls.Add(this.picDownloadSection);
            this.grbDownloadDB.Controls.Add(this.picDownLoadDisqualification);
            this.grbDownloadDB.Controls.Add(this.picDownloadResignDate);
            this.grbDownloadDB.Controls.Add(this.picAddEmp);
            this.grbDownloadDB.Controls.Add(this.picDownloadExpiryDate);
            this.grbDownloadDB.Controls.Add(this.PicNoSkill);
            this.grbDownloadDB.Controls.Add(this.picObsoleted);
            this.grbDownloadDB.Controls.Add(this.picDownLoadAllDept);
            this.grbDownloadDB.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.grbDownloadDB.ForeColor = System.Drawing.Color.Maroon;
            this.grbDownloadDB.Location = new System.Drawing.Point(387, 355);
            this.grbDownloadDB.Name = "grbDownloadDB";
            this.grbDownloadDB.Size = new System.Drawing.Size(534, 271);
            this.grbDownloadDB.TabIndex = 2;
            this.grbDownloadDB.TabStop = false;
            this.grbDownloadDB.Text = "                                  Download database";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.label2.Location = new System.Drawing.Point(11, 675);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(62, 15);
            this.label2.TabIndex = 4;
            this.label2.Text = "Log in by :";
            // 
            // lblUser
            // 
            this.lblUser.AutoSize = true;
            this.lblUser.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.lblUser.Location = new System.Drawing.Point(71, 675);
            this.lblUser.Name = "lblUser";
            this.lblUser.Size = new System.Drawing.Size(33, 15);
            this.lblUser.TabIndex = 4;
            this.lblUser.Text = "User";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.label4.Location = new System.Drawing.Point(196, 675);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(81, 15);
            this.label4.TabIndex = 4;
            this.label4.Text = " Department :";
            // 
            // lblDept
            // 
            this.lblDept.AutoSize = true;
            this.lblDept.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.lblDept.Location = new System.Drawing.Point(275, 675);
            this.lblDept.Name = "lblDept";
            this.lblDept.Size = new System.Drawing.Size(33, 15);
            this.lblDept.TabIndex = 4;
            this.lblDept.Text = "Dept";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.label6.Location = new System.Drawing.Point(398, 675);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(39, 15);
            this.label6.TabIndex = 4;
            this.label6.Text = "Date :";
            // 
            // lblDate
            // 
            this.lblDate.AutoSize = true;
            this.lblDate.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.lblDate.Location = new System.Drawing.Point(435, 675);
            this.lblDate.Name = "lblDate";
            this.lblDate.Size = new System.Drawing.Size(33, 15);
            this.lblDate.TabIndex = 4;
            this.lblDate.Text = "Date";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.label8.Location = new System.Drawing.Point(583, 676);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(41, 15);
            this.label8.TabIndex = 4;
            this.label8.Text = "Time :";
            // 
            // lblTime
            // 
            this.lblTime.AutoSize = true;
            this.lblTime.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.lblTime.Location = new System.Drawing.Point(622, 676);
            this.lblTime.Name = "lblTime";
            this.lblTime.Size = new System.Drawing.Size(35, 15);
            this.lblTime.TabIndex = 4;
            this.lblTime.Text = "Time";
            // 
            // timer1
            // 
            this.timer1.Enabled = true;
            this.timer1.Interval = 1000;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // InfoLabel
            // 
            this.InfoLabel.AutoSize = true;
            this.InfoLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(222)));
            this.InfoLabel.Location = new System.Drawing.Point(746, 675);
            this.InfoLabel.Name = "InfoLabel";
            this.InfoLabel.Size = new System.Drawing.Size(67, 15);
            this.InfoLabel.TabIndex = 5;
            this.InfoLabel.Text = "Total data :";
            // 
            // picAddEmp
            // 
            this.picAddEmp.Image = global::OperatorTrainingRecord.Properties.Resources.add_emp;
            this.picAddEmp.Location = new System.Drawing.Point(69, 214);
            this.picAddEmp.Name = "picAddEmp";
            this.picAddEmp.Size = new System.Drawing.Size(178, 43);
            this.picAddEmp.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picAddEmp.TabIndex = 7;
            this.picAddEmp.TabStop = false;
            this.picAddEmp.Click += new System.EventHandler(this.picAddEmp_Click);
            // 
            // pictureBox2
            // 
            this.pictureBox2.ErrorImage = global::OperatorTrainingRecord.Properties.Resources.Picture11;
            this.pictureBox2.Image = global::OperatorTrainingRecord.Properties.Resources.Picture12;
            this.pictureBox2.InitialImage = global::OperatorTrainingRecord.Properties.Resources.Picture1;
            this.pictureBox2.Location = new System.Drawing.Point(134, 245);
            this.pictureBox2.Name = "pictureBox2";
            this.pictureBox2.Size = new System.Drawing.Size(275, 329);
            this.pictureBox2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox2.TabIndex = 3;
            this.pictureBox2.TabStop = false;
            // 
            // picDownloadSection
            // 
            this.picDownloadSection.Image = global::OperatorTrainingRecord.Properties.Resources.Section;
            this.picDownloadSection.Location = new System.Drawing.Point(274, 41);
            this.picDownloadSection.Name = "picDownloadSection";
            this.picDownloadSection.Size = new System.Drawing.Size(178, 43);
            this.picDownloadSection.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picDownloadSection.TabIndex = 7;
            this.picDownloadSection.TabStop = false;
            this.picDownloadSection.Click += new System.EventHandler(this.picDownloadSection_Click);
            // 
            // picDownLoadDisqualification
            // 
            this.picDownLoadDisqualification.Image = global::OperatorTrainingRecord.Properties.Resources.Disqualified;
            this.picDownLoadDisqualification.Location = new System.Drawing.Point(273, 98);
            this.picDownLoadDisqualification.Name = "picDownLoadDisqualification";
            this.picDownLoadDisqualification.Size = new System.Drawing.Size(178, 43);
            this.picDownLoadDisqualification.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picDownLoadDisqualification.TabIndex = 7;
            this.picDownLoadDisqualification.TabStop = false;
            this.picDownLoadDisqualification.Click += new System.EventHandler(this.picDownLoadDisqualification_Click);
            // 
            // picDownloadResignDate
            // 
            this.picDownloadResignDate.Image = global::OperatorTrainingRecord.Properties.Resources.Resign1;
            this.picDownloadResignDate.Location = new System.Drawing.Point(69, 157);
            this.picDownloadResignDate.Name = "picDownloadResignDate";
            this.picDownloadResignDate.Size = new System.Drawing.Size(178, 43);
            this.picDownloadResignDate.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picDownloadResignDate.TabIndex = 7;
            this.picDownloadResignDate.TabStop = false;
            this.picDownloadResignDate.Click += new System.EventHandler(this.picDownloadResignDate_Click);
            // 
            // picDownloadExpiryDate
            // 
            this.picDownloadExpiryDate.Image = global::OperatorTrainingRecord.Properties.Resources.Expire;
            this.picDownloadExpiryDate.Location = new System.Drawing.Point(69, 98);
            this.picDownloadExpiryDate.Name = "picDownloadExpiryDate";
            this.picDownloadExpiryDate.Size = new System.Drawing.Size(178, 43);
            this.picDownloadExpiryDate.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picDownloadExpiryDate.TabIndex = 7;
            this.picDownloadExpiryDate.TabStop = false;
            this.picDownloadExpiryDate.Click += new System.EventHandler(this.picDownloadExpiryDate_Click);
            // 
            // PicNoSkill
            // 
            this.PicNoSkill.Image = global::OperatorTrainingRecord.Properties.Resources.no_skill1;
            this.PicNoSkill.Location = new System.Drawing.Point(271, 214);
            this.PicNoSkill.Name = "PicNoSkill";
            this.PicNoSkill.Size = new System.Drawing.Size(178, 43);
            this.PicNoSkill.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.PicNoSkill.TabIndex = 7;
            this.PicNoSkill.TabStop = false;
            this.PicNoSkill.Click += new System.EventHandler(this.PicNoSkill_Click);
            // 
            // picObsoleted
            // 
            this.picObsoleted.Image = global::OperatorTrainingRecord.Properties.Resources.Obsoleted;
            this.picObsoleted.Location = new System.Drawing.Point(273, 157);
            this.picObsoleted.Name = "picObsoleted";
            this.picObsoleted.Size = new System.Drawing.Size(178, 43);
            this.picObsoleted.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picObsoleted.TabIndex = 7;
            this.picObsoleted.TabStop = false;
            this.picObsoleted.Click += new System.EventHandler(this.picObsoleted_Click);
            // 
            // picDownLoadAllDept
            // 
            this.picDownLoadAllDept.Image = global::OperatorTrainingRecord.Properties.Resources.skill;
            this.picDownLoadAllDept.Location = new System.Drawing.Point(69, 41);
            this.picDownLoadAllDept.Name = "picDownLoadAllDept";
            this.picDownLoadAllDept.Size = new System.Drawing.Size(178, 43);
            this.picDownLoadAllDept.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picDownLoadAllDept.TabIndex = 7;
            this.picDownLoadAllDept.TabStop = false;
            this.picDownLoadAllDept.Click += new System.EventHandler(this.picDownLoadAllDept_Click);
            // 
            // pictureBox1
            // 
            this.pictureBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pictureBox1.ErrorImage = global::OperatorTrainingRecord.Properties.Resources._00195189;
            this.pictureBox1.Image = global::OperatorTrainingRecord.Properties.Resources._00195189;
            this.pictureBox1.InitialImage = global::OperatorTrainingRecord.Properties.Resources._00195189;
            this.pictureBox1.Location = new System.Drawing.Point(140, 1);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(598, 237);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 3;
            this.pictureBox1.TabStop = false;
            this.pictureBox1.WaitOnLoad = true;
            // 
            // pictureBox3
            // 
            this.pictureBox3.Image = global::OperatorTrainingRecord.Properties.Resources.Search;
            this.pictureBox3.Location = new System.Drawing.Point(456, 302);
            this.pictureBox3.Name = "pictureBox3";
            this.pictureBox3.Size = new System.Drawing.Size(365, 50);
            this.pictureBox3.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox3.TabIndex = 3;
            this.pictureBox3.TabStop = false;
            // 
            // frmWelcome
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.PaleTurquoise;
            this.ClientSize = new System.Drawing.Size(884, 701);
            this.Controls.Add(this.InfoLabel);
            this.Controls.Add(this.lblTime);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.lblDate);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.lblDept);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.pictureBox2);
            this.Controls.Add(this.lblUser);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.grbDownloadDB);
            this.Controls.Add(this.txtSearchUser);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.pictureBox3);
            this.MaximizeBox = false;
            this.MaximumSize = new System.Drawing.Size(900, 740);
            this.MinimumSize = new System.Drawing.Size(900, 740);
            this.Name = "frmWelcome";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Welcome to Operator\'s training Database";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.frmWelcome_FormClosing);
            this.Load += new System.EventHandler(this.frmWelcome_Load);
            this.grbDownloadDB.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.picAddEmp)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDownloadSection)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDownLoadDisqualification)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDownloadResignDate)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDownloadExpiryDate)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.PicNoSkill)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picObsoleted)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picDownLoadAllDept)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox3)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtSearchUser;
        private System.Windows.Forms.GroupBox grbDownloadDB;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.PictureBox pictureBox2;
        private System.Windows.Forms.PictureBox pictureBox3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label lblUser;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label lblDept;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label lblDate;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label lblTime;
        public System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.Label InfoLabel;
        private System.Windows.Forms.PictureBox picDownLoadAllDept;
        private System.Windows.Forms.PictureBox picDownLoadDisqualification;
        private System.Windows.Forms.PictureBox picDownloadSection;
        private System.Windows.Forms.PictureBox picDownloadResignDate;
        private System.Windows.Forms.PictureBox picDownloadExpiryDate;
        private System.Windows.Forms.PictureBox picObsoleted;
        private System.Windows.Forms.PictureBox PicNoSkill;
        private System.Windows.Forms.PictureBox picAddEmp;
    }
}