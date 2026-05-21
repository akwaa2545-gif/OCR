using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.IO;
using System.Timers;

namespace OperatorTrainingRecord
{
    public partial class frmDisqualification : Form
    {
        private string ProcessSelect;
        private string EmpCodeSelect;
        public frmDisqualification()
        {
            InitializeComponent();
        }

        StringBuilder sb = new StringBuilder();
        SqlConnection Conn = new SqlConnection();
        string strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
        
        SqlCommand Cmd = new SqlCommand();

        private void frmDisqualification_Load(object sender, EventArgs e)
        {
            ProcessSelect = frmOperatorTraining.SelectqualificationID;
            EmpCodeSelect = frmOperatorTraining.EmpCodeID;
        }

        private void btnConfirmDis_Click(object sender, EventArgs e)
        {
            // *****DisQualified*****
            string strDis = string.Empty;

            string strSql = "Select ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,TestResult,KnowledgeLevel,SkillLevel,JudgmentPractice as Judgment,convert(varchar,CertifiedDate,106) as CertifiedDate,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark,Download From tblQualified where EmpCode = '" + frmWelcome.UserID + "'and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
            DataTable dt = new DataTable();
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(dt);
            Conn.ConnectionString = strConnString;
            Conn.Open();
            Cmd = new SqlCommand(strSql, Conn);
            if (rdDis1.Checked == true)
            {
                sb.Append("UPDATE tblQualified set DisQualifiedBy ='").Append(Login.UserName).Append("', TheReason='").Append(rdDis1.Text).Append("', DisQualifiedDate = GETDATE()");

                sb.Append("WHERE ProcessName= '").Append(ProcessSelect).Append("'and EmpCode='").Append(EmpCodeSelect).Append("'and (DisQualifiedBy= ''or DisQualifiedBy is Null)").Append("");
                string sqlSave = sb.ToString();
                Cmd.CommandType = CommandType.Text;
                Cmd.CommandText = sqlSave;
                Cmd.Connection = Conn;
                Cmd.ExecuteNonQuery();
                Conn.Close();
                MessageBox.Show("DisQualified is "+ ProcessSelect + " ", "DisQualified None", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Hide();

            }
            else if (rdDis2.Checked == true)
            {
                sb.Append("UPDATE tblQualified set DisQualifiedBy ='").Append(Login.UserName).Append("', TheReason='").Append(rdDis2.Text).Append("', DisQualifiedDate = GETDATE()");

                sb.Append("WHERE ProcessName= '").Append(ProcessSelect).Append("'and EmpCode='").Append(EmpCodeSelect).Append("'and (DisQualifiedBy= ''or DisQualifiedBy is Null)").Append("");
                string sqlSave = sb.ToString();
                Cmd.CommandType = CommandType.Text;
                Cmd.CommandText = sqlSave;
                Cmd.Connection = Conn;
                Cmd.ExecuteNonQuery();
                Conn.Close();
                MessageBox.Show("DisQualified is " + ProcessSelect + " ", "DisQualified None", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Hide();
            }
            else if (rdDis3.Checked == true)
            {
                sb.Append("UPDATE tblQualified set DisQualifiedBy ='").Append(Login.UserName).Append("', TheReason='").Append(rdDis3.Text).Append("', DisQualifiedDate = GETDATE()");

                sb.Append("WHERE ProcessName= '").Append(ProcessSelect).Append("'and EmpCode='").Append(EmpCodeSelect).Append("'and (DisQualifiedBy= ''or DisQualifiedBy is Null)").Append("");
                string sqlSave = sb.ToString();
                Cmd.CommandType = CommandType.Text;
                Cmd.CommandText = sqlSave;
                Cmd.Connection = Conn;
                Cmd.ExecuteNonQuery();
                Conn.Close();
                MessageBox.Show("DisQualified is " + ProcessSelect + " ", "DisQualified None", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Hide();
            }
            else if (rdDis4.Checked == true)
            {
                sb.Append("UPDATE tblQualified set DisQualifiedBy ='").Append(Login.UserName).Append("', TheReason='").Append(rdDis4.Text).Append("', DisQualifiedDate = GETDATE()");

                sb.Append("WHERE ProcessName= '").Append(ProcessSelect).Append("'and EmpCode='").Append(EmpCodeSelect).Append("'and (DisQualifiedBy= ''or DisQualifiedBy is Null)").Append("");
                string sqlSave = sb.ToString();
                Cmd.CommandType = CommandType.Text;
                Cmd.CommandText = sqlSave;
                Cmd.Connection = Conn;
                Cmd.ExecuteNonQuery();
                Conn.Close();
                MessageBox.Show("DisQualified is " + ProcessSelect + " ", "DisQualified None", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Hide();
            }
            else if (rdDis5.Checked == true)
            {
                sb.Append("UPDATE tblQualified set DisQualifiedBy ='").Append(Login.UserName).Append("', TheReason='").Append(rdDis5.Text).Append("', DisQualifiedDate = GETDATE()");

                sb.Append("WHERE ProcessName= '").Append(ProcessSelect).Append("'and EmpCode='").Append(EmpCodeSelect).Append("'and (DisQualifiedBy= ''or DisQualifiedBy is Null)").Append("");
                string sqlSave = sb.ToString();
                Cmd.CommandType = CommandType.Text;
                Cmd.CommandText = sqlSave;
                Cmd.Connection = Conn;
                Cmd.ExecuteNonQuery();
                Conn.Close();
                MessageBox.Show("DisQualified is " + ProcessSelect + " ", "DisQualified None", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Hide();
            }
            else 
            {
                
                MessageBox.Show("Please select DisQualified !!!", "DisQualified None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }

        }

        private void btnCancelDis_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Cancel Disqualified ?", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                this.Hide();
            }
        }

        //private void frmDisqualification_FormClosing(object sender, FormClosingEventArgs e)
        //{
        //    Application.Exit();
        //}


    }
}
