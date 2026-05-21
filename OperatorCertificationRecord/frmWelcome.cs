using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.IO;
using System.Timers;
using Excel = Microsoft.Office.Interop.Excel;

namespace OperatorTrainingRecord
{
    public partial class frmWelcome : Form
    {
        public string sectid { get; set; }
        public frmWelcome()
        {
            InitializeComponent();
        }
        public static string UserID;
        private void frmWelcome_Load(object sender, EventArgs e)
        {
            this.ActiveControl = txtSearchUser;
            lblDate.Text = DateTime.Now.ToString("dd MMMM yyyy");
            lblTime.Text = DateTime.Now.ToLongTimeString();

            this.lblUser.Text = strUser;
            lblDept.Text = Login.DeptName;

            if (this.sectid != "S06")
            {
                picAddEmp.Hide();
                //grbEmployee.Hide();                
            }
            
        }

        StringBuilder sb = new StringBuilder();
        SqlConnection Conn = new SqlConnection();
        //string strConnString = "Server=localhost;UID=sa;PASSWORD=password;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
        string strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";

        SqlCommand Cmd = new SqlCommand();
      
        string strUser;
        public string _strUser
        {
            get { return strUser; }
            set { strUser = value; }
        }

        public void LoadWelcome()
        {
            DataTable dt = receptDataWelcome();


            if (dt.Rows.Count > 0)
            {

                lblUser.Text = dt.Rows[0]["PersonFNameEng"].ToString() +" "+ dt.Rows[0]["PersonLNameEng"].ToString();
                lblDept.Text = dt.Rows[0]["DeptName"].ToString();

            }
        }
        private DataTable receptDataWelcome()
        {
            //************* Connext Database Show User login**********

            StringBuilder sb = new StringBuilder();

            SqlConnection Conn = new SqlConnection();
            SqlCommand Cmd = new SqlCommand();

            SqlDataAdapter dtAdapter = default(SqlDataAdapter);
            DataTable dt = new DataTable();
            string strConnString = null;
            string strSQL = null;
         //   string strSQL2 = null;

            //strConnString = "Server=localhost;UID=sa;PASSWORD=password;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
            strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
            Conn.ConnectionString = strConnString;
            Conn.Open();

            strSQL = "SELECT * FROM tblEmployee em inner join tblDepartment dm on em.DeptID = dm.DeptID WHERE EmpCode = '" + strUser + "' ";
         //   strSQL2 = "SELECT * FROM Department";

            Cmd = new SqlCommand(strSQL, Conn);

          //  dtAdapter = new SqlDataAdapter(strSQL2, Conn);
            dtAdapter = new SqlDataAdapter(strSQL, Conn);
            dtAdapter.Fill(dt);

            Conn.Close();
            Conn = null;
            return dt;

        }
        private DataTable receptDataSearchUser()
        {
            //************* Connext Database Show User login**********

            StringBuilder sb = new StringBuilder();

            SqlConnection Conn = new SqlConnection();
            SqlCommand Cmd = new SqlCommand();

            SqlDataAdapter dtAdapter = default(SqlDataAdapter);
            DataTable dt = new DataTable();
            string strConnString = null;
            string strSQL = null;
            //   string strSQL2 = null;

            //strConnString = "Server=localhost;UID=sa;PASSWORD=password;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
            strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
            Conn.ConnectionString = strConnString;
            Conn.Open();

            //strSQL = "SELECT * FROM tblEmployee  WHERE EmpCode = '" + strUser + "' ";
            strSQL = "SELECT * FROM tblEmployee em inner join tblDepartment dm on em.DeptID = dm.DeptID WHERE EmpCode = '" + this.txtSearchUser.Text + "' and (StatusWork != '0' or StatusWork is null)";
            //   strSQL2 = "SELECT * FROM Department";
            
            Cmd = new SqlCommand(strSQL, Conn);

            dtAdapter = new SqlDataAdapter(strSQL, Conn);
            dtAdapter.Fill(dt);

            Conn.Close();
            Conn = null;
            return dt;

        }

        private void txtSearchUser_KeyPress(object sender, KeyPressEventArgs e)
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

        private void txtSearchUser_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)

            {
                if(txtSearchUser.Text == "Enter the Employee Number")
                {
                    MessageBox.Show("Please input employee number");
                    return;
                }
                DataTable dt = receptDataSearchUser();                
               
                if (dt.Rows.Count > 0)
                {
                    UserID = dt.Rows[0]["EmpCode"].ToString();
 
                    frmOperatorTraining frm = new frmOperatorTraining();
                    frm.sectid = this.sectid;
                    
                    
                    frm.Show();
                    frm.LoadDetail();
                    frm.WindowState = FormWindowState.Maximized;
    
                    this.Hide();
                }
                else
                {
                    MessageBox.Show("Employee Number is incorrect", "Report", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
        }
        private void timer1_Tick(object sender, EventArgs e)
        {
            lblTime.Text = DateTime.Now.ToLongTimeString();
        }


        private void btnAllDepartment_Click(object sender, EventArgs e)
        {
            DataTable dt = receptDataWelcome();
            DataSet ds = new DataSet(); // สร้าง ds
    
            string strSql;

            strSql = "SELECT EmpCode,convert(varchar,JoinDate,106) as JoinDate,JobGrade,HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,WorkshopName,Shift,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,convert(varchar,CertifiedDate,106) as CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,convert(varchar,DisQualifiedDate,106) as DisQualifiedDate,DisQualifiedBy,TheReason,Remark FROM ViewEmpQualified_All where (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(ds, "Qualified");
            if (MessageBox.Show("Download Data All Dept ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {

                MessageBox.Show("Total = " + ds.Tables["Qualified"].Rows.Count + " Rows");

                Excel.Application xlApp;
                Excel.Workbook xlWorkBook;
                Excel.Worksheet xlWorkSheet;
                object misValue = System.Reflection.Missing.Value;

                Int16 i, j;

                xlApp = new Excel.Application();
                xlWorkBook = xlApp.Workbooks.Add(misValue);

                xlWorkSheet = (Excel.Worksheet)xlWorkBook.Worksheets.get_Item(1);
                xlWorkSheet.Name = "My Sheet";

                /* ******* Download file Colums&Rows form DataSet by P'Sungsan *****
            
                  i = 0;

                  foreach (DataColumn col in ds.Tables["Qualified"].Columns)
                  {
                      i++;

                      xlWorkSheet.Cells[1, i] = col.ColumnName;

                  }

                  j = 0;

                  foreach (DataRow row in ds.Tables["Qualified"].Rows)
                  {
                      j++;
                      Application.DoEvents();
                      i = 0;
                      foreach (DataColumn col in ds.Tables["Qualified"].Columns)
                      {
                          i++;
                          xlWorkSheet.Cells[j + 1, i] = row[col.ColumnName].ToString();
                      }

                      InfoLabel.Text = "Row " + j;

                  }
                */


                for (i = 1; i <= ds.Tables["Qualified"].Columns.Count; i++)
                {
                    xlWorkSheet.Cells[1, i] = ds.Tables["Qualified"].Columns[i - 1].ToString();
                }

                for (i = 1; i <= ds.Tables["Qualified"].Rows.Count; i++)
                {
                    for (j = 1; j <= ds.Tables["Qualified"].Columns.Count; j++)
                    {
                        xlWorkSheet.Cells[i + 1, j] = ds.Tables["Qualified"].Rows[i - 1][j - 1].ToString();
                    }
                    InfoLabel.Text = "Rows download all =" + i;
                }

                try
                {

                    xlWorkBook.SaveAs(@"c:\Temp\Operator training All Dept" + DateTime.Now.ToString("yyyMMdd") + ".xls", Excel.XlFileFormat.xlWorkbookNormal, misValue, misValue, misValue, misValue, Excel.XlSaveAsAccessMode.xlExclusive, misValue, misValue, misValue, misValue, misValue);
                   // xlApp.Workbooks.Close();

                    xlWorkBook.Close(true, misValue, misValue);
                    xlApp.Quit();

                    releaseObject(xlWorkSheet);
                    releaseObject(xlWorkBook);
                    releaseObject(xlApp);

                    
                    MessageBox.Show("Download Data Successfully");
                    InfoLabel.Text = "";
                }
                catch
                {
                    return;
                }
            }
        }

        private void releaseObject(object obj)
        {
            try
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(obj);
                obj = null;
            }
            catch (Exception ex)
            {
                obj = null;
                MessageBox.Show("Exception Occured while releasing object " + ex.ToString());
            }
            finally
            {
                GC.Collect();
            }
        }      

        private void picDownLoadAllDept_Click(object sender, EventArgs e)
        {
            DataTable dt = receptDataWelcome();
            DataSet ds = new DataSet(); // สร้าง ds

            string strSql;

            strSql = "SELECT EmpCode,convert(varchar,JoinDate,106) as JoinDate,HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,WorkshopName,JobGrade,Shift,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,convert(varchar,CertifiedDate,106) as CertifiedDate,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark FROM ViewEmpQualified_ALL where (DisQualifiedBy= ''or DisQualifiedBy is Null) and ResignDate is null and ProcessName is not null";
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(ds, "Qualified");
            if (MessageBox.Show("Download Data All Dept ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {

                MessageBox.Show("Total = " + ds.Tables["Qualified"].Rows.Count + " Rows");

                Excel.Application xlApp;
                Excel.Workbook xlWorkBook;
                Excel.Worksheet xlWorkSheet;
                object misValue = System.Reflection.Missing.Value;

                Int16 i, j;

                xlApp = new Excel.Application();
                xlWorkBook = xlApp.Workbooks.Add(misValue);

                xlWorkSheet = (Excel.Worksheet)xlWorkBook.Worksheets.get_Item(1);
                xlWorkSheet.Name = "My Sheet";

                /* ******* Download file Colums&Rows form DataSet by P'Sungsan *****
            
                  i = 0;

                  foreach (DataColumn col in ds.Tables["Qualified"].Columns)
                  {
                      i++;

                      xlWorkSheet.Cells[1, i] = col.ColumnName;

                  }

                  j = 0;

                  foreach (DataRow row in ds.Tables["Qualified"].Rows)
                  {
                      j++;
                      Application.DoEvents();
                      i = 0;
                      foreach (DataColumn col in ds.Tables["Qualified"].Columns)
                      {
                          i++;
                          xlWorkSheet.Cells[j + 1, i] = row[col.ColumnName].ToString();
                      }

                      InfoLabel.Text = "Row " + j;

                  }
                */


                for (i = 1; i <= ds.Tables["Qualified"].Columns.Count; i++)
                {
                    xlWorkSheet.Cells[1, i] = ds.Tables["Qualified"].Columns[i - 1].ToString();
                }

                for (i = 1; i <= ds.Tables["Qualified"].Rows.Count; i++)
                {
                    for (j = 1; j <= ds.Tables["Qualified"].Columns.Count; j++)
                    {
                        xlWorkSheet.Cells[i + 1, j] = ds.Tables["Qualified"].Rows[i - 1][j - 1].ToString();
                    }
                    InfoLabel.Text = "Rows " + i;
                }

                try
                {

                    xlWorkBook.SaveAs(@"c:\Temp\Operator training All Dept" + DateTime.Now.ToString("yyyMMdd") + ".xls", Excel.XlFileFormat.xlWorkbookNormal, misValue, misValue, misValue, misValue, Excel.XlSaveAsAccessMode.xlExclusive, misValue, misValue, misValue, misValue, misValue);
                    // xlApp.Workbooks.Close();

                    xlWorkBook.Close(true, misValue, misValue);
                    xlApp.Quit();

                    releaseObject(xlWorkSheet);
                    releaseObject(xlWorkBook);
                    releaseObject(xlApp);


                    MessageBox.Show("Download Data Successfully");
                }
                catch
                {
                    return;
                }
            }
        }

        private void picDownLoadDisqualification_Click(object sender, EventArgs e)
        {
            frmDownLoadDisqualification frm = new frmDownLoadDisqualification();
            
            frm.Show(); 

        }

        private void picDownloadSection_Click(object sender, EventArgs e)
        {
            frmDownloadSection frm = new frmDownloadSection();
            
            frm.Show();
        }

        private void picDownloadExpiryDate_Click(object sender, EventArgs e)
        {
            frmDownloadExpiryDate frm = new frmDownloadExpiryDate();

            frm.Show();
        }

        private void picDownloadResignDate_Click(object sender, EventArgs e)
        {
            frmDownloadResignDate frm = new frmDownloadResignDate();
            
            frm.Show();
        }

        private void picObsoleted_Click(object sender, EventArgs e)
        {
            DataTable dt = receptDataWelcome();
            DataSet ds = new DataSet(); // สร้าง ds

            string strSql;

            strSql = "SELECT EmpCode,convert(varchar,JoinDate,106) as JoinDate,JobGrade,HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,WorkshopName,Shift,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,convert(varchar,CertifiedDate,106) as CertifiedDate,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,convert(varchar,DisQualifiedDate,106) as DisQualifiedDate,DisQualifiedBy,TheReason,Remark FROM ViewEmpQualified_All_Obsoleted where (DisQualifiedBy= ''or DisQualifiedBy is Null) and ResignDate is null ";
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(ds, "Qualified");
            if (MessageBox.Show("Download Obsoleted ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {

                MessageBox.Show("Total = " + ds.Tables["Qualified"].Rows.Count + " Rows");

                Excel.Application xlApp;
                Excel.Workbook xlWorkBook;
                Excel.Worksheet xlWorkSheet;
                object misValue = System.Reflection.Missing.Value;

                Int16 i, j;

                xlApp = new Excel.Application();
                xlWorkBook = xlApp.Workbooks.Add(misValue);

                xlWorkSheet = (Excel.Worksheet)xlWorkBook.Worksheets.get_Item(1);
                xlWorkSheet.Name = "My Sheet";

                
                for (i = 1; i <= ds.Tables["Qualified"].Columns.Count; i++)
                {
                    xlWorkSheet.Cells[1, i] = ds.Tables["Qualified"].Columns[i - 1].ToString();
                }

                for (i = 1; i <= ds.Tables["Qualified"].Rows.Count; i++)
                {
                    for (j = 1; j <= ds.Tables["Qualified"].Columns.Count; j++)
                    {
                        xlWorkSheet.Cells[i + 1, j] = ds.Tables["Qualified"].Rows[i - 1][j - 1].ToString();
                    }
                    InfoLabel.Text = "Rows Obsoleted =" + i;
                }

                try
                {

                    xlWorkBook.SaveAs(@"c:\Temp\Operator training Obsoleted " + DateTime.Now.ToString("yyyMMdd") + ".xls", Excel.XlFileFormat.xlWorkbookNormal, misValue, misValue, misValue, misValue, Excel.XlSaveAsAccessMode.xlExclusive, misValue, misValue, misValue, misValue, misValue);
                    // xlApp.Workbooks.Close();

                    xlWorkBook.Close(true, misValue, misValue);
                    xlApp.Quit();

                    releaseObject(xlWorkSheet);
                    releaseObject(xlWorkBook);
                    releaseObject(xlApp);


                    MessageBox.Show("Download Data Successfully");
                    InfoLabel.Text = "";
                }
                catch
                {
                    return;
                }
            }

        }
        private void PicNoSkill_Click(object sender, EventArgs e)
        {
            DataTable dt = receptDataWelcome();
            DataSet ds = new DataSet(); // สร้าง ds

            string strSql;

            //strSql = "SELECT EmpCode,convert(varchar,JoinDate,106) as JoinDate,JobGrade,HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,WorkshopName,Shift,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,convert(varchar,CertifiedDate,106) as CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,convert(varchar,DisQualifiedDate,106) as DisQualifiedDate,DisQualifiedBy,TheReason,Remark FROM ViewEmpQualified_All where (ProcessName is null or DisQF = 0) and ResignBy is Null and JobGrade in ('O1','O2','O3')";
            strSql = "SELECT  DISTINCT EmpCode,convert(varchar,JoinDate,106) as JoinDate,HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,WorkshopName,JobGrade,Shift,Notice FROM ViewEmpQualified_All where (ProcessName is null or DisQF = 0) and ResignBy is Null and Shift != 'DAY' and JobGrade in ('O1','O2','O3')" ;
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(ds, "Qualified");
            if (MessageBox.Show("Download of Employee No skill ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {

                MessageBox.Show("Total = " + ds.Tables["Qualified"].Rows.Count + " Rows");

                Excel.Application xlApp;
                Excel.Workbook xlWorkBook;
                Excel.Worksheet xlWorkSheet;
                object misValue = System.Reflection.Missing.Value;

                Int16 i, j;

                xlApp = new Excel.Application();
                xlWorkBook = xlApp.Workbooks.Add(misValue);

                xlWorkSheet = (Excel.Worksheet)xlWorkBook.Worksheets.get_Item(1);
                xlWorkSheet.Name = "My Sheet";


                for (i = 1; i <= ds.Tables["Qualified"].Columns.Count; i++)
                {
                    xlWorkSheet.Cells[1, i] = ds.Tables["Qualified"].Columns[i - 1].ToString();
                }

                for (i = 1; i <= ds.Tables["Qualified"].Rows.Count; i++)
                {
                    for (j = 1; j <= ds.Tables["Qualified"].Columns.Count; j++)
                    {
                        xlWorkSheet.Cells[i + 1, j] = ds.Tables["Qualified"].Rows[i - 1][j - 1].ToString();
                    }
                    InfoLabel.Text = "Employee no skill = " + i;
                }

                try
                {

                    xlWorkBook.SaveAs(@"c:\Temp\Operator training Employee no skill " + DateTime.Now.ToString("yyyMMdd") + ".xls", Excel.XlFileFormat.xlWorkbookNormal, misValue, misValue, misValue, misValue, Excel.XlSaveAsAccessMode.xlExclusive, misValue, misValue, misValue, misValue, misValue);
                    // xlApp.Workbooks.Close();

                    xlWorkBook.Close(true, misValue, misValue);
                    xlApp.Quit();

                    releaseObject(xlWorkSheet);
                    releaseObject(xlWorkBook);
                    releaseObject(xlApp);


                    MessageBox.Show("Download Data Successfully");
                    InfoLabel.Text = "";
                }
                catch
                {
                    return;
                }
            }

        }

        private void btnAddEmp_Click(object sender, EventArgs e)
        {
            frmAddUser frm = new frmAddUser();
            frm.Show();
        }

        private void frmWelcome_FormClosing(object sender, FormClosingEventArgs e)
        {
            Application.Exit();
        }

        private void picAddEmp_Click(object sender, EventArgs e)

        {
            frmAddUser frm = new frmAddUser();
            frm.Show();
        }
         

    }
}
