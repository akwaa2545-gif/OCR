using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
//using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.IO;
using System.Text.RegularExpressions;


//using System.Timers;


namespace OperatorTrainingRecord
{
    public partial class frmUpdateSkill : Form
    {
        public frmUpdateSkill()
        {
            InitializeComponent();
            lblProcess.SelectedIndexChanged += new EventHandler(lblProcess_SelectedIndexChanged);
        }

        //********* Connection SQL ********
        StringBuilder sb = new StringBuilder();
        SqlConnection Conn = new SqlConnection();
       // string strConnString = "Server=localhost;UID=sa;PASSWORD=password;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
        string strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
        
        SqlCommand Cmd = new SqlCommand();
        //********** End Connection SQL *****

        OpenFileDialog Filedlg = new OpenFileDialog();
        private string ProcessSelect;
        private string EmpCodeSelect;
        private string PdfFile;
     //   string strFolderPath = "\\\\thb242it\\DATA\\";
        string strFolderPath = "\\\\svr120a\\Cert$\\" + frmWelcome.UserID + "" + "\\" + "";

        private void frmUpdateSkill_Load(object sender, EventArgs e)
        {
            ProcessSelect = frmOperatorTraining.SelectqualificationID;
            EmpCodeSelect = frmOperatorTraining.EmpCodeID;
            DataSet dsOpT = new DataSet();
            DataSet dsProcess = new DataSet();
            string strSqlProcess = "Select distinct ProcessName from tblQualified where EmpCode = '" + EmpCodeSelect + "' and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
            SqlDataAdapter daProcess = new SqlDataAdapter(strSqlProcess, strConnString);
            daProcess.Fill(dsProcess, "Process");
            lblProcess.DisplayMember = "ProcessName";
            lblProcess.ValueMember = "ProcessName";
            lblProcess.DataSource = dsProcess.Tables["Process"];
            lblProcess.Text = ProcessSelect;

            string strSql = "Select EmpCode,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as Theory,convert(varchar,OJTTraining,106) as OJT,CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark,Download from tblQualified where EmpCode = '" + EmpCodeSelect + "'and ProcessName = '" + ProcessSelect + "' and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
            DataTable dt = new DataTable();
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
           // Cmd = new SqlCommand(strSql, Conn);
 //           Conn.Open();
            da.Fill(dt);
            if(dt.Rows.Count > 0)
            {
                // lblProcess.Text = dt.Rows[0]["ProcessName"].ToString();
                cbbOperatorTraining.Text = dt.Rows[0]["OperatorTraining"].ToString();
                dTimeTheoryUpdate.Text = dt.Rows[0]["Theory"].ToString();
                dTimeOJTUpdate.Text = dt.Rows[0]["OJT"].ToString();
                txtFullScoreUpdate.Text = dt.Rows[0]["FullScore"].ToString();
                txtActualScoreUpdate.Text = dt.Rows[0]["ActualScore"].ToString();
                txtKnowledgeScoreUpdate.Text = dt.Rows[0]["KnowledgeScore"].ToString();
                txtSkillScoreUpdate.Text = dt.Rows[0]["SkillScore"].ToString();
                dTimeCertifiedUpdate.Text = dt.Rows[0]["CertifiedDate"].ToString();
                dTimeExpiryUpdate.Text = dt.Rows[0]["ExpiryDate"].ToString();
                txtVerefierName.Text = Login.UserName;

                
                //************ ดึงข้อมูล OperatorTraining ******

                strSql = "SELECT OperatorTrainingName,OperatorTrainingID FROM tblOperatorTraining";
                SqlDataAdapter daOpT = new SqlDataAdapter(strSql, strConnString);
                daOpT.Fill(dsOpT, "OperatorTraining");

                cbbOperatorTraining.DisplayMember = "OperatorTrainingName";
                cbbOperatorTraining.ValueMember = "OperatorTrainingID";
                cbbOperatorTraining.DataSource = dsOpT.Tables["OperatorTraining"];
              //  cbbOperatorTraining.SelectedIndex = -1;              

            }
          //  txtVerefierName.Text = dt.Rows[0][VerefierName].ToString();
        }

        private void lblProcess_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lblProcess.SelectedValue != null)
            {
                ProcessSelect = lblProcess.SelectedValue.ToString();
                // Reload the data for the new process
                DataSet dsOpT = new DataSet();
                string strSql = "Select EmpCode,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as Theory,convert(varchar,OJTTraining,106) as OJT,CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark,Download from tblQualified where EmpCode = '" + EmpCodeSelect + "'and ProcessName = '" + ProcessSelect + "' and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
                DataTable dt = new DataTable();
                SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
                da.Fill(dt);
                if(dt.Rows.Count > 0)
                {
                    cbbOperatorTraining.Text = dt.Rows[0]["OperatorTraining"].ToString();
                    dTimeTheoryUpdate.Text = dt.Rows[0]["Theory"].ToString();
                    dTimeOJTUpdate.Text = dt.Rows[0]["OJT"].ToString();
                    txtFullScoreUpdate.Text = dt.Rows[0]["FullScore"].ToString();
                    txtActualScoreUpdate.Text = dt.Rows[0]["ActualScore"].ToString();
                    txtKnowledgeScoreUpdate.Text = dt.Rows[0]["KnowledgeScore"].ToString();
                    txtSkillScoreUpdate.Text = dt.Rows[0]["SkillScore"].ToString();
                    dTimeCertifiedUpdate.Text = dt.Rows[0]["CertifiedDate"].ToString();
                    dTimeExpiryUpdate.Text = dt.Rows[0]["ExpiryDate"].ToString();
                    txtVerefierName.Text = Login.UserName;

                    //************ ดึงข้อมูล OperatorTraining ******

                    strSql = "SELECT OperatorTrainingName,OperatorTrainingID FROM tblOperatorTraining";
                    SqlDataAdapter daOpT = new SqlDataAdapter(strSql, strConnString);
                    daOpT.Fill(dsOpT, "OperatorTraining");

                    cbbOperatorTraining.DisplayMember = "OperatorTrainingName";
                    cbbOperatorTraining.ValueMember = "OperatorTrainingID";
                    cbbOperatorTraining.DataSource = dsOpT.Tables["OperatorTraining"];
                }
            }
        }

        private void txtFullScoreUpdate_KeyPress(object sender, KeyPressEventArgs e)
        {
            int cInt = Convert.ToInt32(e.KeyChar);
            if ((int)e.KeyChar >= 48 && (int)e.KeyChar <= 57 || cInt == 8)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void txtActualScoreUpdate_KeyPress(object sender, KeyPressEventArgs e)
        {
            int cInt = Convert.ToInt32(e.KeyChar);
            if ((int)e.KeyChar >= 48 && (int)e.KeyChar <= 57 || cInt == 8)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void txtKnowledgeScoreUpdate_KeyPress(object sender, KeyPressEventArgs e)
        {
            int cInt = Convert.ToInt32(e.KeyChar);
            if ((int)e.KeyChar >= 48 && (int)e.KeyChar <= 57 || cInt == 8)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void txtSkillScoreUpdate_KeyPress(object sender, KeyPressEventArgs e)
        {
            int cInt = Convert.ToInt32(e.KeyChar);
            if ((int)e.KeyChar >= 48 && (int)e.KeyChar <= 57 || cInt == 8)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void txtFullScoreUpdate_TextChanged(object sender, EventArgs e)
        {
            int FullScore = 0;
            int ActualScore = 0;

            // if ((txtActualScore.Text != "") && (txtFullScore.Text != ""))
            if (txtFullScoreUpdate.Text == "")
            {
                txtFullScoreUpdate.Text = "0";
            }
            if (txtActualScoreUpdate.Text == "")
            {
                txtActualScoreUpdate.Text = "0";
            }
            //if ((txtActualScore.Text != "") && ((Convert.ToInt16(txtFullScore.Text) > 0)))
            if (Convert.ToInt16(txtFullScoreUpdate.Text) > 0)
            {
                FullScore = int.Parse(txtFullScoreUpdate.Text);
                ActualScore = int.Parse(txtActualScoreUpdate.Text);
                lblTestResultUpdate.Text = ((ActualScore * 100) / FullScore).ToString();

                if (int.Parse(lblTestResultUpdate.Text) >= 80)
                {
                    lblJudgmentTheory.Text = "Pass";
                    lblTestResultUpdate.ForeColor = Color.Black;
                    lblJudgmentTheory.ForeColor = Color.Black;
                }
                else
                {

                    lblJudgmentTheory.Text = "Fail";
                    lblTestResultUpdate.ForeColor = Color.Red;
                    lblJudgmentTheory.ForeColor = Color.Red;
                }

            }
            else
            {
                return;
            }
        }

        private void txtActualScoreUpdate_TextChanged(object sender, EventArgs e)
        {
            int FullScore = 0;
            int ActualScore = 0;

            if (txtFullScoreUpdate.Text == "")
            {
                txtFullScoreUpdate.Text = "0";
            }
            if (txtActualScoreUpdate.Text == "")
            {
                txtActualScoreUpdate.Text = "0";
            }
            //if ((txtActualScore.Text != "") && ((Convert.ToInt16(txtFullScore.Text) > 0)))
            if (Convert.ToInt16(txtFullScoreUpdate.Text) > 0)
            {
                FullScore = int.Parse(txtFullScoreUpdate.Text);
                ActualScore = int.Parse(txtActualScoreUpdate.Text);
                lblTestResultUpdate.Text = ((ActualScore * 100) / FullScore).ToString();

                if (int.Parse(lblTestResultUpdate.Text) >= 80)
                {
                    lblJudgmentTheory.Text = "Pass";
                    lblTestResultUpdate.ForeColor = Color.Black;
                    lblJudgmentTheory.ForeColor = Color.Black;
                }
                else
                {

                    lblJudgmentTheory.Text = "Fail";
                    lblTestResultUpdate.ForeColor = Color.Red;
                    lblJudgmentTheory.ForeColor = Color.Red;

                }
            }
            else
            {
                return;
            }
        }

        private void txtKnowledgeScoreUpdate_TextChanged(object sender, EventArgs e)
        {
            int KnowledgeScore;
            int SkillScore;

            if (txtKnowledgeScoreUpdate.Text == "")
            {
                txtKnowledgeScoreUpdate.Text = "0";
            }
            KnowledgeScore = int.Parse(txtKnowledgeScoreUpdate.Text);


            if (txtSkillScoreUpdate.Text == "")
            {
                txtSkillScoreUpdate.Text = "0";
            }
            SkillScore = int.Parse(txtSkillScoreUpdate.Text);


            //if (KnowledgeScore >= 26)
            //{
            //    lblJudgmentUpdate.Text = "Pass";
            //    lblJudgmentUpdate.ForeColor = Color.Black;
            //}
            if (KnowledgeScore > 100)
            {
                lblKnowleageLevelUpdate.Text = "O";
                lblKnowleageLevelUpdate.ForeColor = Color.Black;
            }
            else if (KnowledgeScore >= 76)
            {
                lblKnowleageLevelUpdate.Text = "U";
                lblKnowleageLevelUpdate.ForeColor = Color.Black;
            }
            else if (KnowledgeScore >= 51)
            {
                lblKnowleageLevelUpdate.Text = "L";
                lblKnowleageLevelUpdate.ForeColor = Color.Black;
            }
            else if (KnowledgeScore >= 26)
            {
                lblKnowleageLevelUpdate.Text = "I";
                lblKnowleageLevelUpdate.ForeColor = Color.Black;
            }
            else if (KnowledgeScore <= 25)
            {
                lblKnowleageLevelUpdate.Text = "X";
                lblJudgmentPractice.Text = "Fail";
                lblKnowleageLevelUpdate.ForeColor = Color.Red;
                lblJudgmentPractice.ForeColor = Color.Red;
            }

            if (lblKnowleageLevelUpdate.Text != "X" && lblSkillLevelUpdate.Text != "X")
            {
                lblJudgmentPractice.Text = "Pass";
                lblJudgmentPractice.ForeColor = Color.Black;
            }
            else if (lblKnowleageLevelUpdate.Text == "X" || lblSkillLevelUpdate.Text != "X")
            {
                lblJudgmentPractice.Text = "Fail";
                lblJudgmentPractice.ForeColor = Color.Red;
            }
        }

        private void txtSkillScoreUpdate_TextChanged(object sender, EventArgs e)
        {

            int KnowledgeScore;
            int SkillScore;

            if (txtKnowledgeScoreUpdate.Text == "")
            {
                txtKnowledgeScoreUpdate.Text = "0";
            }
            KnowledgeScore = int.Parse(txtKnowledgeScoreUpdate.Text);


            if (txtSkillScoreUpdate.Text == "")
            {
                txtSkillScoreUpdate.Text = "0";
            }
            SkillScore = int.Parse(txtSkillScoreUpdate.Text);

            if (KnowledgeScore >= 26 && SkillScore >= 26)
            {
                lblJudgmentPractice.Text = "Pass";
                lblJudgmentPractice.ForeColor = Color.Black;

            }

            if (SkillScore > 100)
            {
                lblSkillLevelUpdate.Text = "O";
                lblSkillLevelUpdate.ForeColor = Color.Black;
            }
            else if (SkillScore >= 76)
            {
                lblSkillLevelUpdate.Text = "U";
                lblSkillLevelUpdate.ForeColor = Color.Black;
            }
            else if (SkillScore >= 51)
            {
                lblSkillLevelUpdate.Text = "L";
                lblSkillLevelUpdate.ForeColor = Color.Black;
            }
            else if (SkillScore >= 26)
            {
                lblSkillLevelUpdate.Text = "I";
                lblSkillLevelUpdate.ForeColor = Color.Black;
            }
            else
            {
                lblSkillLevelUpdate.Text = "X";
                lblJudgmentPractice.Text = "Fail";
                lblSkillLevelUpdate.ForeColor = Color.Red;
                lblJudgmentPractice.ForeColor = Color.Red;
            }
        }

        private void btnUploadFile_Click(object sender, EventArgs e)
        {

            Filedlg.Filter = "Pdf Only|*.pdf";
            Filedlg.Multiselect = false;
            PdfFile = Filedlg.FileName;

            if (Filedlg.ShowDialog() == DialogResult.OK)
            {
                this.btnUploadFile.Text = Filedlg.FileName;

                if (Regex.Match(btnUploadFile.Text.ToString(), "'").Success)
                {
                    MessageBox.Show("File invalid Text");

                }
              
            }
        }

        private void btnConfirmUpdateSkill_Click(object sender, EventArgs e)
        {

            string filePath = Filedlg.FileName;
            string fileName = System.IO.Path.GetFileName(filePath);


            if (lblTestResultUpdate.Text.Trim() == "")
            {
                MessageBox.Show("Please input the TestResult !!!", "TestResult None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (lblKnowleageLevelUpdate.Text.Trim() == "")
            {
                MessageBox.Show("Please input the KnowleageLevel !!!", "KnowleageLevel None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (lblSkillLevelUpdate.Text.Trim() == "")
            {
                MessageBox.Show("Please input the SkillLevel !!!", "SkillLevel None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (fileName == "")
            {
                MessageBox.Show("Please select upload PDFFile !!!", "File None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (Regex.Match(btnUploadFile.Text.ToString(), "'").Success) 
            {
                MessageBox.Show("Please Input the PDFFile !!!", "File incorrect", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            Conn.ConnectionString = strConnString;
            Conn.Open();
            string strSql = "Select EmpCode,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as Theory,convert(varchar,OJTTraining,106) as OJT,CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark,Download from tblQualified where EmpCode = '" + EmpCodeSelect + "'and ProcessName = '" + ProcessSelect + "' and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
            DataTable dt = new DataTable();
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(dt);


            if (dt.Rows.Count > 0 && MessageBox.Show("Update Skill ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                //*** Save File \\svr120a\Cert$

                if (fileName != "")
                {
                    //*** create folder \\svr120a\Cert$
                    System.IO.Directory.CreateDirectory(@"\\svr120a\Cert$\" + EmpCodeSelect + "");

                    File.Copy(filePath, strFolderPath + fileName, true);

                    //   Cmd.Parameters.Add("@sDownload", SqlDbType.NVarChar).Value = strFolderPath + fileName;                   

                    //                    Conn.Open();
                    sb = new StringBuilder();
                    sb.Append("INSERT INTO tblQualified_Obsoleted SELECT * FROM tblQualified where EmpCode='" + EmpCodeSelect + "' and ProcessName='" + ProcessSelect + "' and (DisQualifiedBy= ''or DisQualifiedBy is Null)");
                    string sqlCopy = sb.ToString();
                    Cmd.CommandType = CommandType.Text;
                    Cmd.CommandText = sqlCopy;
                    Cmd.Connection = Conn;
                    Cmd.ExecuteNonQuery();
//  //                  Conn.Close();

                    sb = new StringBuilder();
                    sb.Append("UPDATE tblQualified set OperatorTraining ='").Append(cbbOperatorTraining.Text).Append("', TheoryTraining='").Append(dTimeTheoryUpdate.Text).Append("', OJTTraining='").Append(dTimeOJTUpdate.Text).Append("', FullScore='").Append(txtFullScoreUpdate.Text).Append("', ActualScore='").Append(txtActualScoreUpdate.Text).Append("', TestResult='").Append(lblTestResultUpdate.Text).Append("', JudgmentTheory='").Append(lblJudgmentTheory.Text).Append("', KnowledgeScore='").Append(txtKnowledgeScoreUpdate.Text).Append("', KnowledgeLevel='").Append(lblKnowleageLevelUpdate.Text).Append("', SkillScore='").Append(txtSkillScoreUpdate.Text).Append("', SkillLevel='").Append(lblSkillLevelUpdate.Text).Append("', JudgmentPractice='").Append(lblJudgmentPractice.Text).Append("', CertifiedDate='").Append(dTimeCertifiedUpdate.Text).Append("', ExpiryDate='").Append(dTimeExpiryUpdate.Text).Append("', Verifier='").Append(Login.UserName).Append("', VerifierDate= GETDATE(), Remark='").Append(txtRemarkUpdate.Text).Append("', Download='").Append(strFolderPath + fileName).Append("'");

                    sb.Append("WHERE ProcessName= '").Append(ProcessSelect).Append("'and EmpCode='").Append(EmpCodeSelect).Append("'and (DisQualifiedBy= ''or DisQualifiedBy is Null)").Append("");
                    string sqlSave = sb.ToString();
                    Cmd.CommandType = CommandType.Text;
                    Cmd.CommandText = sqlSave;
                    Cmd.Connection = Conn;

                    Cmd.Parameters.Clear();

                    Cmd.ExecuteNonQuery();
                    Conn.Close();




                    //sb = new StringBuilder();
                    //sb.Remove(0, sb.Length);
                    //sb.Append("INSERT INTO tblQualified(EmpCode,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark,Download) VALUES (@sEmpCode,@sProcessName,@sOperatorTraining,@sTheoryTraining,@sOJTTraining,@sCertifiedDate,@sFullScore,@sActualScore,@sTestResult,@sJudgmentTheory,@sKnowledgeScore,@sKnowledgeLevel,@sSkillScore,@sSkillLevel,@sJudgmentPractice,@sExpiryDate,@sVerifier,@sVerifierDate,@sRemark,@sDownload)");

                    //string sqlInsert = sb.ToString();

                    //Cmd.CommandType = CommandType.Text;
                    //Cmd.CommandText = sqlInsert;
                    //Cmd.Connection = Conn;

                    //Cmd.Parameters.Clear();

                    //Cmd.Parameters.Add("@sEmpCode", SqlDbType.Int).Value = EmpCodeSelect;
                    //Cmd.Parameters.Add("@sProcessName", SqlDbType.NVarChar).Value = ProcessSelect;
                    //Cmd.Parameters.Add("@sOperatorTraining", SqlDbType.NVarChar).Value = cbbOperatorTraining.Text.Trim();
                    //Cmd.Parameters.Add("@sTheoryTraining", SqlDbType.Date).Value = lblTheoryUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sOJTTraining", SqlDbType.Date).Value = lblOJTUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sCertifiedDate", SqlDbType.Date).Value = dTimeCertifiedUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sFullScore", SqlDbType.NVarChar).Value = txtFullScoreUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sActualScore", SqlDbType.NVarChar).Value = txtActualScoreUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sTestResult", SqlDbType.NVarChar).Value = lblTestResultUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sJudgmentTheory", SqlDbType.NVarChar).Value = lblJudgmentTheory.Text.Trim();
                    //Cmd.Parameters.Add("@sKnowledgeScore", SqlDbType.NVarChar).Value = txtKnowledgeScoreUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sKnowledgeLevel", SqlDbType.NVarChar).Value = lblKnowleageLevelUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sSkillScore", SqlDbType.NVarChar).Value = txtSkillScoreUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sSkillLevel", SqlDbType.NVarChar).Value = lblSkillLevelUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sJudgmentPractice", SqlDbType.NVarChar).Value = lblJudgmentPractice.Text.Trim();
                    //Cmd.Parameters.Add("@sExpiryDate", SqlDbType.Date).Value = dTimeExpiryUpdate.Text.Trim();
                    //Cmd.Parameters.Add("@sVerifier", SqlDbType.NVarChar).Value = txtVerefierName.Text.Trim();
                    //Cmd.Parameters.Add("@sVerifierDate", SqlDbType.NVarChar).Value = DateTime.Now;
                    //Cmd.Parameters.Add("@sRemark", SqlDbType.NVarChar).Value = txtRemarkUpdate.Text.Trim();

                    //Cmd.Parameters.Add("@sDownload", SqlDbType.NVarChar).Value = strFolderPath + fileName;

                    //Cmd.ExecuteNonQuery();
                    ////  //                  Conn.Close();


                    //sb = new StringBuilder();

                    //strSql = "SELECT MAX(VerifierDate) FROM tblQualified WHERE ProcessName='ProcessSelect'";
                    //sb.Append("UPDATE tblQualified set OperatorTraining ='").Append(cbbOperatorTraining.Text).Append("', FullScore='").Append(txtFullScoreUpdate.Text).Append("', ActualScore='").Append(txtActualScoreUpdate.Text).Append("', TestResult='").Append(lblTestResultUpdate.Text).Append("', JudgmentTheory='").Append(lblJudgmentTheory.Text).Append("', KnowledgeScore='").Append(txtKnowledgeScoreUpdate.Text).Append("', KnowledgeLevel='").Append(lblKnowleageLevelUpdate.Text).Append("', SkillScore='").Append(txtSkillScoreUpdate.Text).Append("', SkillLevel='").Append(lblSkillLevelUpdate.Text).Append("', JudgmentPractice='").Append(lblJudgmentPractice.Text).Append("', CertifiedDate='").Append(dTimeCertifiedUpdate.Text).Append("', ExpiryDate='").Append(dTimeExpiryUpdate.Text).Append("', Verifier='").Append(Login.UserName).Append("', VerifierDate= GETDATE(), Remark='").Append(txtRemarkUpdate.Text).Append("', Download='").Append(strFolderPath + fileName).Append("'");

                    //sb.Append("WHERE ProcessName= '").Append(ProcessSelect).Append("'and EmpCode='").Append(EmpCodeSelect).Append("'and (DisQualifiedBy= ''or DisQualifiedBy is Null)").Append("");
                    //string sqlSave = sb.ToString();
                    //Cmd.CommandType = CommandType.Text;
                    //Cmd.CommandText = sqlSave;
                    //Cmd.Connection = Conn;

                    //Cmd.Parameters.Clear();

                    //Cmd.ExecuteNonQuery();


  

                    MessageBox.Show("SAVE Complete", "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("Please select upload PDFFile", "Report", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            //else
            //{
            //    MessageBox.Show("Already Skill");
            //    Conn.Close();
            //}
            this.Hide();
            Conn.Close();
        }

        private void dTimeCertifiedUpdate_ValueChanged(object sender, EventArgs e)
        {
            // บวกค่าไปอีก 365-1 วัน จากวัน Certified Date //
            dTimeExpiryUpdate.Value = dTimeCertifiedUpdate.Value.AddDays(364);
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit Update ?", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                this.Hide();
            }
        }

        private void frmUpdateSkill_FormClosing(object sender, FormClosingEventArgs e)
        {
            Application.Exit();
        }


    }
}
