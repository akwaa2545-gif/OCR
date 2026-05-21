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
    public partial class frmDownloadResignDate : Form
    {
        public frmDownloadResignDate()
        {
            InitializeComponent();
        }

        StringBuilder sb = new StringBuilder();
        SqlConnection Conn = new SqlConnection();
        string strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";

        SqlCommand Cmd = new SqlCommand();

        private void btnDownloadResign_Click(object sender, EventArgs e)
        {
           
            DataSet ds = new DataSet(); // สร้าง ds

            string strSql;

            //strSql = "SELECT EmpCode,convert(varchar,JoinDate,106) as JoinDate,JobGrade,HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,WorkshopName,Shift,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,convert(varchar,CertifiedDate,106) as CertifiedDate,FullScore,ActualScore,TestResult,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,Judgment,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,convert(varchar,DisQualifiedDate,106) as DisQualifiedDate,DisQualifiedBy,TheReason,Remark FROM ViewEmpQualified_All where ExpiryDate<= '" + dateExpiryDate.Text + "'and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
            //strSql = "SELECT EmpCode,convert(varchar,JoinDate,106) as JoinDate,JobGrade,HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,WorkshopName,Shift,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,convert(varchar,CertifiedDate,106) as CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark,ResignBy,convert(varchar,ResignDate,106) as ResignDate FROM ViewEmpQualified_All where (ResignDate between '" + dateStartDate.Text + "'and'" + dateEndDate.Text + "') and (DisQualifiedBy= ''or DisQualifiedBy is Null) and ResignBy is not Null and ProcessName is not null";
            //strSql = "SELECT EmpCode,convert(varchar,JoinDate,106) as JoinDate,JobGrade,HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,WorkshopName,Shift,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,convert(varchar,CertifiedDate,106) as CertifiedDate,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark,ResignBy,convert(varchar,ResignDate,106) as ResignDate FROM ViewEmpQualified_All where (ResignDate between '" + dateStartDate.Text + "'and'" + dateEndDate.Text + "') and (DisQualifiedBy= ''or DisQualifiedBy is Null) and ResignBy is not Null";
            strSql = "SELECT q.EmpCode,convert(varchar,q.JoinDate,106) as JoinDate,q.JobGrade,q.HEng,q.PersonFNameEng,q.PersonLNameEng,q.HThai,q.PersonFNameThai,q.PersonLNameThai,q.DeptName,q.SectName,q.WorkshopName,q.Shift,q.ProcessName,q.OperatorTraining,convert(varchar,q.TheoryTraining,106) as TheoryTraining,convert(varchar,q.OJTTraining,106) as OJTTraining,q.FullScore,q.ActualScore,q.TestResult,q.JudgmentTheory,q.KnowledgeScore,q.KnowledgeLevel,q.SkillScore,q.SkillLevel,q.JudgmentPractice,convert(varchar,q.CertifiedDate,106) as CertifiedDate,convert(varchar,q.ExpiryDate,106) as ExpiryDate,CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN (v.PersonFnameEng + ' ' + v.PersonLnameEng) ELSE q.Verifier END AS Verifier,convert(varchar,q.VerifierDate,106) as VerifierDate,q.Remark,q.ResignBy,convert(varchar,q.ResignDate,106) as ResignDate FROM ViewEmpQualified_All q LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier where (q.ResignDate between '" + dateStartDate.Text + "'and'" + dateEndDate.Text + "') and q.ResignBy is not Null";
           
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(ds, "Qualified");
            if (MessageBox.Show("Download Data By Resign Date ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
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
                    InfoLabel.Text = "Rows " + i;
                }

                try
                {

                    xlWorkBook.SaveAs(@"c:\Temp\Operator training Resign " + dateStartDate.Text + " - " + dateEndDate.Text + ".xls", Excel.XlFileFormat.xlWorkbookNormal, misValue, misValue, misValue, misValue, Excel.XlSaveAsAccessMode.xlExclusive, misValue, misValue, misValue, misValue, misValue);
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
            this.Hide();
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

    }
}
