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
    public partial class frmDownloadExpiryDate : Form
    {
        public frmDownloadExpiryDate()
        {
            InitializeComponent();
            DesktopReportUi.SetThisYear(dateStartDate, dateEndDate);
        }
        
        StringBuilder sb = new StringBuilder();
        SqlConnection Conn = new SqlConnection();
        string strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";

        SqlCommand Cmd = new SqlCommand();


        private void btnDownloadExpiryDate_Click(object sender, EventArgs e)
        {
            DataSet ds;
            if (!DesktopReportUi.TryLoad(this, DesktopReportKind.Expiry, strConnString,
                dateStartDate.Value.Date, dateEndDate.Value.Date, out ds))
            {
                return;
            }
            if (MessageBox.Show("Download Data By Expiry Date ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {

                MessageBox.Show("Total = " + ds.Tables["Qualified"].Rows.Count + " Rows");

                Excel.Application xlApp;
                Excel.Workbook xlWorkBook;
                Excel.Worksheet xlWorkSheet;
                object misValue = System.Reflection.Missing.Value;

                int i, j;

                xlApp = new Excel.Application();
                xlWorkBook = xlApp.Workbooks.Add(misValue);

                xlWorkSheet = (Excel.Worksheet)xlWorkBook.Worksheets.get_Item(1);
                xlWorkSheet.Name = "My Sheet";
                xlWorkSheet.Cells.NumberFormat = "@";

                for (i = 1; i <= ds.Tables["Qualified"].Columns.Count; i++)
                {
                    xlWorkSheet.Cells[1, i] = ds.Tables["Qualified"].Columns[i - 1].ToString();
                }

                for (i = 1; i <= ds.Tables["Qualified"].Rows.Count; i++)
                {
                    for (j = 1; j <= ds.Tables["Qualified"].Columns.Count; j++)
                    {
                        xlWorkSheet.Cells[i + 1, j] = DesktopReportService.CellText(ds.Tables["Qualified"].Rows[i - 1][j - 1]);
                    }
                    InfoLabel.Text = "Rows " + i;
                }

                try
                {

                    xlWorkBook.SaveAs(@"c:\Temp\Operator training Expiry " + dateStartDate.Text + " - " + dateEndDate.Text + ".xls", Excel.XlFileFormat.xlWorkbookNormal, misValue, misValue, misValue, misValue, Excel.XlSaveAsAccessMode.xlExclusive, misValue, misValue, misValue, misValue, misValue);
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
                    MessageBox.Show(this, "The report could not be saved. Close the existing Excel file and check access to C:\\Temp, then try again.",
                        "Download report", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        //private void frmDownloadExpiryDate_FormClosing(object sender, FormClosingEventArgs e)
        //{
        //    Application.Exit();
        //}


    }
}
