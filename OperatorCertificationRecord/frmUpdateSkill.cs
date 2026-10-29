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
        private bool bindingProcesses;
        private bool useStableSkills;
        private Guid? skillRowId;
        private byte[] skillVersion;
     //   string strFolderPath = "\\\\thb242it\\DATA\\";
        string strFolderPath = "\\\\svr120a\\Cert$\\" + frmWelcome.UserID + "" + "\\" + "";

        private void frmUpdateSkill_Load(object sender, EventArgs e)
        {
            ProcessSelect = frmOperatorTraining.SelectqualificationID;
            EmpCodeSelect = frmOperatorTraining.EmpCodeID;
            useStableSkills = new DesktopFileService(strConnString).SchemaAvailable;
            skillRowId = frmOperatorTraining.SelectedSkillRowId;
            DataSet dsOpT = new DataSet();
            DataSet dsProcess = new DataSet();
            string strSqlProcess = "Select distinct ProcessName from tblQualified where EmpCode = '" + EmpCodeSelect + "' and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
            SqlDataAdapter daProcess = new SqlDataAdapter(strSqlProcess, strConnString);
            if (useStableSkills)
            {
                daProcess.SelectCommand.CommandText = "SELECT ProcessName,OcrSkillRowId FROM dbo.tblQualified WHERE EmpCode=@employee AND (DisQualifiedBy='' OR DisQualifiedBy IS NULL)";
                daProcess.SelectCommand.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = EmpCodeSelect;
            }
            daProcess.Fill(dsProcess, "Process");
            lblProcess.DisplayMember = "ProcessName";
            lblProcess.ValueMember = useStableSkills ? "OcrSkillRowId" : "ProcessName";
            bindingProcesses = true;
            try
            {
                lblProcess.DataSource = dsProcess.Tables["Process"];
                lblProcess.SelectedValue = useStableSkills ? (object)skillRowId : ProcessSelect;
            }
            finally
            {
                bindingProcesses = false;
            }

            if (!String.Equals(lblProcess.Text, ProcessSelect, StringComparison.Ordinal))
            {
                btnConfirmUpdateSkill.Enabled = false;
                MessageBox.Show("The selected Process is no longer available. Please select another Process.", "Process Selection", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            string strSql = "Select EmpCode,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as Theory,convert(varchar,OJTTraining,106) as OJT,CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark,Download from tblQualified where EmpCode = '" + EmpCodeSelect + "'and ProcessName = '" + ProcessSelect + "' and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
            DataTable dt = new DataTable();
            SqlDataAdapter da = CreateSkillAdapter(strSql);
           // Cmd = new SqlCommand(strSql, Conn);
 //           Conn.Open();
            da.Fill(dt);
            if (skillVersion == null && dt.Rows.Count > 0 && dt.Columns.Contains("FileStorageVersion")) skillVersion = (byte[])dt.Rows[0]["FileStorageVersion"];
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
            if (!bindingProcesses && (lblProcess.SelectedValue is string || lblProcess.SelectedValue is Guid))
            {
                btnConfirmUpdateSkill.Enabled = true;
                ProcessSelect = lblProcess.Text;
                skillRowId = lblProcess.SelectedValue is Guid ? (Guid?)lblProcess.SelectedValue : null;
                skillVersion = null;
                // Reload the data for the new process
                DataSet dsOpT = new DataSet();
                string strSql = "Select EmpCode,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as Theory,convert(varchar,OJTTraining,106) as OJT,CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark,Download from tblQualified where EmpCode = '" + EmpCodeSelect + "'and ProcessName = '" + ProcessSelect + "' and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
                DataTable dt = new DataTable();
                SqlDataAdapter da = CreateSkillAdapter(strSql);
                da.Fill(dt);
            if (skillVersion == null && dt.Rows.Count > 0 && dt.Columns.Contains("FileStorageVersion")) skillVersion = (byte[])dt.Rows[0]["FileStorageVersion"];
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
            SqlDataAdapter da = CreateSkillAdapter(strSql);
            da.Fill(dt);
            if (skillVersion == null && dt.Rows.Count > 0 && dt.Columns.Contains("FileStorageVersion")) skillVersion = (byte[])dt.Rows[0]["FileStorageVersion"];


            if (dt.Rows.Count > 0 && MessageBox.Show("Update Skill ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                try
                {
                    if (!skillRowId.HasValue)
                        new DesktopFileService(strConnString).AssertLegacyCertificateFallbackAllowed(EmpCodeSelect);
                    if (new DesktopFileService(strConnString).TrySaveCertificate(EmpCodeSelect, skillRowId, filePath,
                        (connection, transaction, replacementId) =>
                        {
                            if (!skillRowId.HasValue || skillVersion == null)
                                throw new InvalidOperationException("Reload the selected skill before saving.");
                            DesktopSkillMutation.Replace(connection, transaction, EmpCodeSelect, skillRowId.Value, replacementId, skillVersion, SqlSkillValues());
                        }))
                    {
                        Conn.Close();
                        MessageBox.Show("SAVE Complete", "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Hide();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Conn.Close();
                    System.Diagnostics.Trace.TraceWarning("SQL skill update failed: " + ex.GetType().Name);
                    MessageBox.Show("The skill and certificate could not be saved. " + ex.Message, "Certificate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                //*** Save File \\svr120a\Cert$

                if (fileName != "")
                {
                    Conn.Close();
                    try
                    {
                        new DesktopFileService(strConnString).ExecuteLegacyMutation(EmpCodeSelect, OperatorCertificationRecord.FileStorage.FileKind.Certificate, skillRowId, (legacyConnection, legacyTransaction) =>
                        {
                            //*** create folder \\svr120a\Cert$
                            System.IO.Directory.CreateDirectory(@"\\svr120a\Cert$\" + EmpCodeSelect + "");

                            File.Copy(filePath, strFolderPath + fileName, true);

                            //   Cmd.Parameters.Add("@sDownload", SqlDbType.NVarChar).Value = strFolderPath + fileName;

                            //                    Conn.Open();
                            const string archiveColumns = "EmpCode, ProcessName, OperatorTraining, TheoryTraining, OJTTraining, CertifiedDate, FullScore, ActualScore, TestResult, JudgmentTheory, KnowledgeScore, KnowledgeLevel, SkillScore, SkillLevel, JudgmentPractice, ExpiryDate, Verifier, VerifierDate, DisQualifiedDate, DisQualifiedBy, TheReason, Remark, Download";
                            string optionalArchiveColumns = "";
                            bool hasSkillRowId = false;
                            using (var schema = new SqlCommand(@"SELECT
                                COL_LENGTH('dbo.tblQualified','PromotionId'), COL_LENGTH('dbo.tblQualified_Obsoleted','PromotionId'),
                                COL_LENGTH('dbo.tblQualified','PromotedAt'), COL_LENGTH('dbo.tblQualified_Obsoleted','PromotedAt'),
                                COL_LENGTH('dbo.tblQualified','PromotedBy'), COL_LENGTH('dbo.tblQualified_Obsoleted','PromotedBy'),
                                COL_LENGTH('dbo.tblQualified','OcrSkillRowId'), COL_LENGTH('dbo.tblQualified_Obsoleted','OcrSkillRowId'),
                                COL_LENGTH('dbo.tblQualified','CertificateFileId'), COL_LENGTH('dbo.tblQualified_Obsoleted','CertificateFileId')", legacyConnection, legacyTransaction))
                            using (var reader = schema.ExecuteReader())
                            {
                                if (!reader.Read()) throw new InvalidOperationException("Unable to inspect qualification archive schema.");
                                var present = Enumerable.Range(0, 10).Select(i => !reader.IsDBNull(i)).ToArray();
                                for (int i = 0; i < present.Length; i += 2)
                                    if (present[i] != present[i + 1]) throw new InvalidOperationException("Qualification archive schema is incomplete; archive writes are blocked.");
                                if ((present[0] || present[2] || present[4]) && !(present[0] && present[2] && present[4]))
                                    throw new InvalidOperationException("Qualification promotion schema is incomplete; archive writes are blocked.");
                                if (present[0]) optionalArchiveColumns += ", PromotionId, PromotedAt, PromotedBy";
                                hasSkillRowId = present[6];
                                if (hasSkillRowId) optionalArchiveColumns += ", OcrSkillRowId";
                                if (present[8]) optionalArchiveColumns += ", CertificateFileId";
                            }
                            string alignedColumns = archiveColumns + optionalArchiveColumns;
                            string sqlCopy = "INSERT INTO dbo.tblQualified_Obsoleted (" + alignedColumns + ") SELECT " + alignedColumns +
                                " FROM dbo.tblQualified WHERE EmpCode = @ArchiveEmpCode AND ProcessName = @ArchiveProcessName AND (DisQualifiedBy = '' OR DisQualifiedBy IS NULL)";
                            Cmd.CommandType = CommandType.Text;
                            Cmd.CommandText = sqlCopy;
                            Cmd.Connection = legacyConnection;
                            Cmd.Transaction = legacyTransaction;
                            Cmd.Parameters.Clear();
                            Cmd.Parameters.Add("@ArchiveEmpCode", SqlDbType.NVarChar, 10).Value = EmpCodeSelect;
                            Cmd.Parameters.Add("@ArchiveProcessName", SqlDbType.NVarChar, 100).Value = ProcessSelect;
                            if (hasSkillRowId && skillRowId.HasValue)
                            {
                                Cmd.CommandText += " AND OcrSkillRowId=@LegacySkillRowId";
                                Cmd.Parameters.Add("@LegacySkillRowId", SqlDbType.UniqueIdentifier).Value = skillRowId.Value;
                                if (skillVersion != null)
                                {
                                    Cmd.CommandText += " AND FileStorageVersion=@LegacyVersion";
                                    Cmd.Parameters.Add("@LegacyVersion", SqlDbType.Binary, 8).Value = skillVersion;
                                }
                            }
                            Cmd.ExecuteNonQuery();
                            DesktopSkillMutation.UpdateLegacy(legacyConnection, legacyTransaction, EmpCodeSelect, ProcessSelect, skillRowId, skillVersion, hasSkillRowId, SqlSkillValues(), strFolderPath + fileName);
                        });
                        Cmd.Transaction = null;
                    }
                    catch (Exception ex)
                    {
                        Cmd.Transaction = null;
                        System.Diagnostics.Trace.TraceWarning("Legacy skill save failed: " + ex.GetType().Name);
                        MessageBox.Show("The skill and certificate could not be saved. Reload the record and try again.", "Certificate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
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

        private SqlDataAdapter CreateSkillAdapter(string sql)
        {
            var index = sql.IndexOf(" where EmpCode", StringComparison.OrdinalIgnoreCase);
            var select = index >= 0 ? sql.Substring(0, index) : sql;
            if (useStableSkills) select = new DesktopFileService(strConnString).SkillProjection(select);
            var adapter = new SqlDataAdapter(select + " WHERE EmpCode=@employee AND " +
                (useStableSkills ? "OcrSkillRowId=@rowId" : "ProcessName=@process") +
                " AND (DisQualifiedBy='' OR DisQualifiedBy IS NULL)", strConnString);
            adapter.SelectCommand.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = EmpCodeSelect;
            if (useStableSkills)
            {
                if (!skillRowId.HasValue) throw new InvalidOperationException("Select a stable skill row before editing.");
                adapter.SelectCommand.Parameters.Add("@rowId", SqlDbType.UniqueIdentifier).Value = skillRowId.Value;
            }
            else adapter.SelectCommand.Parameters.Add("@process", SqlDbType.NVarChar, 100).Value = ProcessSelect;
            return adapter;
        }

        private IDictionary<string, object> SqlSkillValues()
        {
            return new Dictionary<string, object>
            {
                ["OperatorTraining"] = cbbOperatorTraining.Text,
                ["TheoryTraining"] = dTimeTheoryUpdate.Value.Date,
                ["OJTTraining"] = dTimeOJTUpdate.Value.Date,
                ["CertifiedDate"] = dTimeCertifiedUpdate.Value.Date,
                ["FullScore"] = txtFullScoreUpdate.Text,
                ["ActualScore"] = txtActualScoreUpdate.Text,
                ["TestResult"] = lblTestResultUpdate.Text,
                ["JudgmentTheory"] = lblJudgmentTheory.Text,
                ["KnowledgeScore"] = txtKnowledgeScoreUpdate.Text,
                ["KnowledgeLevel"] = lblKnowleageLevelUpdate.Text,
                ["SkillScore"] = txtSkillScoreUpdate.Text,
                ["SkillLevel"] = lblSkillLevelUpdate.Text,
                ["JudgmentPractice"] = lblJudgmentPractice.Text,
                ["ExpiryDate"] = dTimeExpiryUpdate.Value.Date,
                ["Verifier"] = Login.UserName,
                ["Remark"] = txtRemarkUpdate.Text
            };
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
