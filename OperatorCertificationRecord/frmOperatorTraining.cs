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
using Excel = Microsoft.Office.Interop.Excel;
using System.Text.RegularExpressions;

namespace OperatorTrainingRecord
{
    public partial class frmOperatorTraining : Form
    {
        public string sectid { get; set; }
        public object ddMMMMyyy { get; set; }
        //private byte[] CurrImg;
        //private byte[] UploadFile;
        //private MemoryStream ms;
        
        OpenFileDialog Filedlg = new OpenFileDialog();

        private string PdfFile;

        public static string SelectqualificationID;
        public static string EmpCodeID;
        public DataGridViewCellStyle btn { get; set; }
        
        string strFolderPath = "\\\\svr120a\\Cert$\\" + frmWelcome.UserID + ""+"\\"+"";
        public frmOperatorTraining()
        {
            InitializeComponent();

        }
        public class Judgment
        {
            public string Potential_level { get; set; }
            public string Potential_Name { get; set; }
            public string K { get; set; }
            public string S { get; set; }
            public string Note { get; set; }
        }

        StringBuilder sb = new StringBuilder();
        SqlConnection Conn = new SqlConnection();
        //string strConnString = "Server=localhost;UID=sa;PASSWORD=password;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
        string strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";

        SqlCommand Cmd = new SqlCommand();

        private void GetData1()
        {
             //*********** Show Current Skill ******

            string strSql2 = "Select q.ProcessName as Process,q.OperatorTraining as CertifyClassification,convert(varchar,q.TheoryTraining,106) as Theory,convert(varchar,q.OJTTraining,106) as OJT,q.KnowledgeLevel as K,q.SkillLevel as S,q.JudgmentPractice as Judgment,convert(varchar,q.CertifiedDate,106) as Certified_Date,convert(varchar,q.ExpiryDate,106) as ExpiryDate,CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN (v.PersonFnameEng + ' ' + v.PersonLnameEng) ELSE q.Verifier END AS Verifier,q.Remark,q.Download,q.CertifiedDate From tblQualified q LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier where q.EmpCode = '" + frmWelcome.UserID + "'and (q.DisQualifiedBy= ''or q.DisQualifiedBy is Null) order by q.CertifiedDate DESC ";
         //   strSql2 = "SELECT MAX(VerifierDate) FROM tblQualified";
            DataTable dt2 = new DataTable();
            DataSet ds = new DataSet();
            SqlDataAdapter da2 = new SqlDataAdapter(strSql2, strConnString);
            da2.Fill(dt2);
            da2.Fill(ds, "Qualified");
            if (dt2.Rows.Count > 0)
            {
                
                gridCurrentSkill.DataSource = ds.Tables[0];
                //txtQualified.Text = dt2.Rows.Count.ToString();
                // gridCurrentSkill.Columns[0].HeaderText = "Disqualified";
                //gridCurrentSkill.Columns[1].HeaderText = "Process";
                gridCurrentSkill.ForeColor = Color.Black;
                gridCurrentSkill.ReadOnly = false;
                gridCurrentSkill.Columns[13].Visible = false;
                gridCurrentSkill.Columns[14].Visible = false;
                gridCurrentSkill.Show();
                gridCurrentSkill.Columns["btn_Download"].DisplayIndex = gridCurrentSkill.Columns.Count-1;
             //   gridCurrentSkill.Columns["btn_Download"].DisplayIndex = 1;

                //************** เปลี่ยนสีที่ Judment ถ้า fail ให้เป็นสีแดงทั้งแถว  ***************

                //foreach (DataGridViewRow row in gridCurrentSkill.Rows)
                //{

                //    //if (row.Cells[1].Value.ToString().Equals(searchValue))
                //   // if (row.Cells[10].Value.ToString().StartsWith(txtLotNo.Text))
                //    //if (row.Cells[10].Value.ToString() == "Fail")
                //    if (row.Cells[10].Value.ToString() == "Fail")
                //    {
                //        int rowIndex = row.Index;
                //       // row.DefaultCellStyle.BackColor = Color.Red;
                //        gridCurrentSkill.Rows[rowIndex].DefaultCellStyle.BackColor = Color.Red;
                //        //gridCurrentSkill.row.Cells[10].Value.ToString() = Color.Red;
                //        //gridCurrentSkill.Rows[rowIndex].Selected = true;


                //        //int rowIndex = row.Index;
                //        //gridCurrentSkill.Rows[rowIndex].Selected = true;
                //        //gridTRData.FirstDisplayedScrollingRowIndex = gridTRData.SelectedRows[0].Index;

                //    }

                //}

                //************** เปลี่ยนสีที่ Judment ถ้า fail ให้เป็นสีแดง คอลัมน์เดียว ***************
                    int i;

                    for (i = 0; i < ds.Tables[0].Rows.Count; i++)
                    {


                        if (gridCurrentSkill.Rows[i].Cells["Judgment"].Value.ToString() == "Fail")
                        {
                            gridCurrentSkill.Rows[i].Cells["Judgment"].Style.BackColor = Color.Red;
                        }
                        else if (gridCurrentSkill.Rows[i].Cells["Judgment"].Value.ToString() == "Pass")
                        {
                            gridCurrentSkill.Rows[i].Cells["Judgment"].Style.BackColor = Color.Green;
                        }
                        //if (gridCurrentSkill.Rows[i].Cells["K"].Value.ToString() == "X")
                        //{
                        //    gridCurrentSkill.Rows[i].Cells["K"].Style.BackColor = Color.Red;
                        //}
                        //if (gridCurrentSkill.Rows[i].Cells["S"].Value.ToString() == "X")
                        //{
                        //    gridCurrentSkill.Rows[i].Cells["S"].Style.BackColor = Color.Red;
                        //}
                    }


                //********** นับจำนวนของ Qualified **********
                strSql2 = "Select * From tblQualified where Empcode='" + txtEmpNo.Text.Trim() + "'and (DisQualifiedBy= ''or DisQualifiedBy is Null) and KnowledgeLevel != 'X' and SkillLevel != 'X'";
                DataTable dt3 = new DataTable();
                SqlDataAdapter da3 = new SqlDataAdapter(strSql2, strConnString);
                da3.Fill(dt3);
                txtQualified.Text = dt3.Rows.Count.ToString();
            }
        }

        
        private void frmOperatorTraining_Load(object sender, EventArgs e)

        {
                        
            gridDisqualified.DataSource = null;
            gridCurrentSkill.DataSource = null;
            // lblUser.Text = OperatorCertificationRecord.Login.UserNumber.text;
            lblUser.Text = Login.UserName;
            lblDept.Text = Login.DeptName;
            lblDate.Text = Login.Date;
            lblTime.Text = Login.Time;
            
            // ****** ซ่อนปุ่มเก่า
            btnCurrentSkill.Hide();
            btnDisqualified.Hide();
            btnAddSkill.Hide();
            btnAddEmp.Hide();
            //********
            //    tabAddSkill.Hide();

            // gridCurrentSkill.Hide();
            if (this.sectid != "S06")
            {
                btnAddEmp.Hide();
                btnResign.Hide();
                btnAddSkill.Hide();
                btnUpdateEmp.Hide();

                TabQualified.TabPages.Remove(tabAddSkill);
                TabQualified.TabPages.Remove(tabUpdateSkill);
                btnUpdateSkill.Hide();
                btnDisOK.Hide();

                gridCurrentSkill.Columns["btn_Download"].Visible = false;
                gridDisqualified.Columns["btn_DownloadDis"].Visible = false;
                gridObsoleted.Columns["btn_DownloadObsoleted"].Visible = false;
            }
            TabQualified.TabPages.Remove(tabUpdateSkill);
           
            grbAddSkill.Hide();

            if (TabQualified.SelectedIndex == 0)
            {
                GetData1();
            }
           

            ////*********** Show Current Skill ******
            //string strSql2 = "Select ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,TestResult,convert(varchar,CertifiedDate,106) as CertifiedDate,KnowledgeLevel,SkillLevel,Judgment,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,Remark,Download From tblQualified where EmpCode = '" + frmWelcome.UserID + "'and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
            //DataTable dt2 = new DataTable();
            //DataSet ds = new DataSet();
            //SqlDataAdapter da2 = new SqlDataAdapter(strSql2, strConnString);
            //da2.Fill(dt2);
            //da2.Fill(ds, "Qualified");
            //if (dt2.Rows.Count > 0)
            //{
                
            //    gridCurrentSkill.DataSource = ds.Tables[0];
            //    txtQualified.Text = dt2.Rows.Count.ToString();
            //    // gridCurrentSkill.Columns[0].HeaderText = "Disqualified";
            //    gridCurrentSkill.Columns[1].HeaderText = "Process";
            //    gridCurrentSkill.ForeColor = Color.Black;
            //    gridCurrentSkill.ReadOnly = false;
            //    gridCurrentSkill.Columns[13].Visible = false;
            //    gridCurrentSkill.Show();

            //    // **************** Add button download ************
            //    DataGridViewButtonColumn btn = new DataGridViewButtonColumn();
            //    gridCurrentSkill.Columns.Add(btn);
            //    btn.HeaderText = "Download";                
            //    btn.Text = "Click";
            //    btn.Name = "btn";
              //  btn.UseColumnTextForButtonValue = true;
           //     this.gridCurrentSkill.Columns["btn"].DisplayIndex = 1;
            //}



         //   gridCurrentSkill.Show();
            grbAddSkill.Hide();




            //*** List<T>
            var ls = new List<Judgment>();
            ls.Add(new Judgment { Potential_level = "X", Potential_Name = "ไม่อนุญาติให้ทำงานโดยลำพัง ต้องมีผู้ดูแล สอนงานเพิ่มและต้องมีการตรวจสอบซ้ำ (Not allow operation without any taking care by mentor person and confirm product result.)", K = "1 - 25", S = "1 - 25", Note = "ยังไม่ผ่านการ qualify" });
            ls.Add(new Judgment { Potential_level = "I", Potential_Name = "สามารถทำงานได้ด้วยตัวเองแต่ยังไม่ถึงเกณฑ์มาตรฐาน (Can operate by them self but still not keep target.)", K = "26 - 50", S = "26 - 50", Note = "" });
            ls.Add(new Judgment { Potential_level = "L", Potential_Name = "สามารถทำงานได้ด้วยตัวเอง อยู่ในเกณฑ์มาตรฐาน เข้าใจผลกระทบ (Can operate by them self ,keep target and understand any impact.)", K = "51 - 75", S = "51 - 75", Note = "" });
            ls.Add(new Judgment { Potential_level = "U", Potential_Name = "มีความเชี่ยวชาญในงานสูง แก้ไขปัญหาได้ พยายามทำให้ดีกว่าเป้าหมายเสมอ (High skill operation can proving the problem and attempt to do more than target.)", K = "76 - 100", S = "76 - 100", Note = "" });
            ls.Add(new Judgment { Potential_level = "O", Potential_Name = "สามารถถ่ายทอดหรือสอนงานผู้อื่นได้ ตัดสินใจผลกระทบต่างๆได้ดี กระตุ้นหรือจูงใจผู้อื่นอยู่เสมอ (Can train other person, good tread the problem and always activate other person.)", K = "> 100", S = "> 100", Note = "" });



            //*** ListView Header
            this.myListView.Clear();
            // myListView.ForeColor = Color.Red;
            myListView.Columns.Clear();
            myListView.HideSelection = false;
            //myListView.Columns.Add("Potential level", 150, HorizontalAlignment.Center);
            myListView.Columns.Add("level", 50, HorizontalAlignment.Center);
            myListView.Columns.Add("Potential Name", 930, HorizontalAlignment.Left);
            myListView.Columns.Add("K", 60, HorizontalAlignment.Center);
            myListView.Columns.Add("S", 60, HorizontalAlignment.Center);
            myListView.Columns.Add("Note", 115, HorizontalAlignment.Center);
            myListView.FullRowSelect = true;
            myListView.View = View.Details;

            //*** ListView Row
            foreach (var item in ls)
            {
                var lvi = new ListViewItem(item.Potential_level);
                lvi.SubItems.Add(item.Potential_Name);
                lvi.SubItems.Add(item.K.ToString());
                lvi.SubItems.Add(item.S);
                lvi.SubItems.Add(item.Note);
                this.myListView.Items.Add(lvi);
            }


        }
 


        string strUser;
        public string _strUser
        {
            get { return strUser; }
            set { strUser = value; }

        }

       
        private void gridCurrentSkill_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            //if (e.ColumnIndex == gridCurrentSkill.Columns["Test_column"].Index)
            //{
            //    //Do something with your button.
            //    //*** download File \\svr120a\Cert$


            //    MessageBox.Show((e.RowIndex + 1) + "  Row  " + (e.ColumnIndex + 1) + "  Column button clicked ");
            //  //  File.Open = gridCurrentSkill.Columns["uninstall_column"];


            //   // File.Copy(filePath, strFolderPath + fileName, true);

            //}
            //if (e.ColumnIndex == gridCurrentSkill.Columns["btn"].Index)
            //{
            //    //Do something with your button.

            //    //*** download File \\svr120a\Cert$
            //    string downloadFile = gridCurrentSkill[13, e.RowIndex].Value.ToString();
                
            //    string filePath = @"C:\Temp\";


            //    if (downloadFile != null && downloadFile != "")               
            //    {
            //        string[] sAry = downloadFile.Split('\\');
            //        string str1 = sAry[4].ToString();

            //        string fileName = filePath + downloadFile.Replace(strFolderPath, "");

            //        File.Copy(downloadFile, fileName, true);

            //        MessageBox.Show("Download file " + str1 + " ", "None", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                    
            //    }
            //    else 
            //    {

            //        MessageBox.Show("File None", "None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                   
            //    }

            //}

        }
    

        public void LoadDetail()
        {
            DataTable dt = receptDataDetail();
            if (dt.Rows.Count > 0)
            {

                this.txtEmpNo.Text = dt.Rows[0]["EmpCode"].ToString();
                this.txtEmpDate.Text = String.Format("{0:dd MMMM yyyy}", dt.Rows[0]["JoinDate"]);//dt.Rows[0]["JoinDate"].ToString();

                //dTimeEmpDate.Text = dt.Rows[0]["JoinDate"].ToString();

                this.txtJobGrade.Text = dt.Rows[0]["JobGrade"].ToString();
                this.txtPrefixEng.Text = dt.Rows[0]["HEng"].ToString();
                this.txtPrefixThai.Text = dt.Rows[0]["HThai"].ToString();
                this.txtFNameEng.Text = dt.Rows[0]["PersonFNameEng"].ToString();
                this.txtFNameThai.Text = dt.Rows[0]["PersonFNameThai"].ToString();
                this.txtLNameEng.Text = dt.Rows[0]["PersonLNameEng"].ToString();
                this.txtLNameThai.Text = dt.Rows[0]["PersonLNameThai"].ToString();
                this.txtDeptName.Text = dt.Rows[0]["DeptName"].ToString();
                this.txtSectionName.Text = dt.Rows[0]["SectName"].ToString();
                this.txtWorkshop.Text = dt.Rows[0]["WorkshopName"].ToString();
                this.txtShift.Text = dt.Rows[0]["Shift"].ToString();
                picPhoto.ImageLocation = dt.Rows[0]["Photo"].ToString();

                //********การดึงรูป ที่saveรูปลง sql **********
                //if (dt.Rows[0]["Photo"].ToString() != "")
                //{
                //    CurrImg = (byte[])dt.Rows[0]["Photo"];
                //    ms = new MemoryStream(CurrImg, true);
                //    picPhoto.SizeMode = PictureBoxSizeMode.StretchImage;
                //    picPhoto.Image = Image.FromStream(ms);

                //}


                //else
                //{
                //    //picPhoto.Image = null;
                //}

                //********** นับจำนวนของ Qualified **********
                string strSql2 = "Select * From tblQualified where Empcode='" + txtEmpNo.Text.Trim() + "'and (DisQualifiedBy= ''or DisQualifiedBy is Null) and KnowledgeLevel != 'X' and SkillLevel != 'X'";
                DataTable dt2 = new DataTable();
                SqlDataAdapter da2 = new SqlDataAdapter(strSql2, strConnString);
                da2.Fill(dt2);
                txtQualified.Text = dt2.Rows.Count.ToString();

            }
        }

        private DataTable receptDataDetail()
        {
            //************* Connext Database Show User login**********

            StringBuilder sb = new StringBuilder();

            SqlConnection Conn = new SqlConnection();
            SqlCommand Cmd = new SqlCommand();

            SqlDataAdapter dtAdapter = default(SqlDataAdapter);
            DataTable dt = new DataTable();
            
            string strSQL = null;
            
            Conn.ConnectionString = strConnString;
            Conn.Open();

            strSQL = "SELECT * FROM tblEmployee em inner join tblDepartment dm on em.DeptID = dm.DeptID inner join tblSection st on em.SectID = st.SectID Where EmpCode = '" + frmWelcome.UserID + "' ";
            strSQL = "SELECT * FROM ViewEmpDeptSect Where EmpCode = '" + frmWelcome.UserID + "' ";
            //   strSQL2 = "SELECT * FROM Department";

            Cmd = new SqlCommand(strSQL, Conn);

            //  dtAdapter = new SqlDataAdapter(strSQL2, Conn);
            dtAdapter = new SqlDataAdapter(strSQL, Conn);
            dtAdapter.Fill(dt);

            Conn.Close();
            Conn = null;
            return dt;

        }
        private void timer1_Tick(object sender, EventArgs e)
        {
            lblTime.Text = DateTime.Now.ToLongTimeString();
        }

        private void btnAddEmp_Click(object sender, EventArgs e)
        {
            frmAddUser frm = new frmAddUser();
            frm.Show();
            //       this.Hide();

            //if (txtEmpNo.Text.Trim() == "")
            //{
            //    MessageBox.Show("Please input the Emp.No. !!!", "Emp None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            //    return;
            //}

            //string strSql = "Select * From tblEmployee where Empcode='" + txtEmpNo.Text.Trim() + "'";
            //DataTable dt = new DataTable();
            //SqlDataAdapter da = new SqlDataAdapter(strSql,Conn);
            //da.Fill(dt);

            //if (dt.Rows.Count == 0)
            //{
            //    if (MessageBox.Show("Add Employee ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            //    {
            //    //MessageBox.Show("SAVE Complete", "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);

            //        //**** Insert to Database ****
            //        //         DataTable dt = receptDataGroup1();
            //        // string strConn;
            //        //strConn = "Server=localhost;UID=sa;PASSWORD=password;Database=RecordTroubleReportDB;Max Pool Size=400;Connect Timeout=600;";
            //        //strConnString = "Server=localhost;UID=sa;PASSWORD=password;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
            //        //Conn = new SqlConnection();
            //        //Conn.ConnectionString = strConnString;
            //        //Conn.Open();

            //        string strSql2 = "Select * From tblEmployee where Empcode='" + txtEmpNo.Text.Trim() + "'";
            //        DataTable dt2 = new DataTable();
            //        SqlDataAdapter da2 = new SqlDataAdapter(strSql2, strConnString);
            //        da2.Fill(dt2);

            //        if (dt2.Rows.Count == 0)
            //        {
            //            sb = new StringBuilder(); 
            //            sb.Remove(0, sb.Length);
            //            //sb.Append("INSERT INTO tblEmployee(EmpCode,EmpPassword,JoinDate,JobGrade,HEng,PersonFnameEng,PersonLnameEng,HThai,PersonFnameThai,PersonLnameThai,DeptID,SectID,WorkshopID,Shift,Photo) VALUES (@sEmpCode,@sEmpPassword,@sJoinDate,@sJobGrade,@sHEng,@sPersonFnameEng,@sPersonLnameEng,@sHThai,@sPersonFnameThai,@sPersonLnameThai,@sDeptID,@sSectID,@sWorkshopID,@sShift,@sPhoto)");
            //            sb.Append("INSERT INTO tblEmployee(EmpCode,EmpPassword,JoinDate,JobGrade,HEng,PersonFnameEng,PersonLnameEng,HThai,PersonFnameThai,PersonLnameThai) VALUES (@sEmpCode,@sEmpPassword,@sJoinDate,@sJobGrade,@sHEng,@sPersonFnameEng,@sPersonLnameEng,@sHThai,@sPersonFnameThai,@sPersonLnameThai)");
            //            string sqlSave = sb.ToString();


            //            Cmd.CommandType = CommandType.Text;
            //            Cmd.CommandText = sqlSave;
            //            Cmd.Connection = Conn;


            //            Cmd.Parameters.Clear();

            //            Cmd.Parameters.Add("@sEmpCode", SqlDbType.Int).Value = txtEmpNo.Text.Trim();
            //            Cmd.Parameters.Add("@sEmpPassword", SqlDbType.NVarChar).Value = txtEmpNo.Text.Trim();
            //            Cmd.Parameters.Add("@sJoinDate", SqlDbType.Date).Value = txtEmpDate.Text.Trim();
            //            //Cmd.Parameters.Add("@sJoinDate", SqlDbType.Date).Value = dTimeEmpDate.Text.Trim();
            //            Cmd.Parameters.Add("@sJobGrade", SqlDbType.NVarChar).Value = txtJobGrade.Text.Trim();
            //            Cmd.Parameters.Add("@sHEng", SqlDbType.NVarChar).Value = txtPrefixEng.Text.Trim();
            //            Cmd.Parameters.Add("@sPersonFnameEng", SqlDbType.NVarChar).Value = txtFNameEng.Text.Trim();
            //            Cmd.Parameters.Add("@sPersonLnameEng", SqlDbType.NVarChar).Value = txtLNameEng.Text.Trim();
            //            Cmd.Parameters.Add("@sHThai", SqlDbType.NVarChar).Value = txtPrefixThai.Text.Trim();
            //            Cmd.Parameters.Add("@sPersonFnameThai", SqlDbType.NVarChar).Value = txtFNameThai.Text.Trim();
            //            Cmd.Parameters.Add("@sPersonLnameThai", SqlDbType.NVarChar).Value = txtLNameThai.Text.Trim();
            //            //Cmd.Parameters.Add("@sDeptID", SqlDbType.NVarChar).Value = txtDeptName.Text.Trim();
            //            //Cmd.Parameters.Add("@sSectID", SqlDbType.NVarChar).Value = txtSectionName.Text.Trim();
            //            //Cmd.Parameters.Add("@sWorkshopID", SqlDbType.NVarChar).Value = txtWorkshop.Text.Trim();
            //            //Cmd.Parameters.Add("@sShift", SqlDbType.NVarChar).Value = txtShift.Text.Trim();
            //            //Cmd.Parameters.Add("@sPhoto", SqlDbType.NVarChar).Value = picPhoto.ToString();

            //            //Cmd.ExecuteNonQuery();
            //            //Conn.Close();
            //            //  MessageBox.Show("SAVE Complete", "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
            //            //if (txtTRItem.Text.ToString() == "DRY BAKING" || txtTRItem.Text.ToString() == "WAIT FOR EVALUATION TEST (BEFORE F2)" || txtTRItem.Text.ToString() == "MINOR DEFECT (RESCREEN)")
            //            if(txtEmpNo == null)
            //            {
            //                //sb = new StringBuilder(); 
            //                //sb.Remove(0, sb.Length);
            //                //sb.Append("INSERT INTO tblTroubleReport(Approve1Name) VALUES (@sApprove1Name) where LotNo='" + txtLotNo.Text.Trim() + "' and PartNo='" + txtPartNo.Text.Trim() + "'");

            //                //string sqlSaveApprove1 = sb.ToString();


            //                //Cmd.CommandType = CommandType.Text;
            //                //Cmd.CommandText = sqlSaveApprove1;
            //                //Cmd.Connection = Conn;
            //                Cmd.Parameters.Add("@sApprove1Name", SqlDbType.NVarChar).Value = "None";

            //            }
            //            else
            //            {
            //                Cmd.Parameters.Add("@sApprove1Name", SqlDbType.NVarChar).Value = DBNull.Value;
            //            }
            //            Cmd.ExecuteNonQuery();
            //            Conn.Close();
            //            DataSet ds = new DataSet(); // สร้าง ds
            //            strSql2 = "Select * From tblEmployee where Empcode='" + txtEmpNo.Text.Trim() + "'";
            //            SqlDataAdapter da3 = new SqlDataAdapter(strSql, Conn);
            //            da3.Fill(ds, "TroubleReport");

            //            //gridTRDATAReander(ds.Tables[0]);
            //            //gridTRData.DataSource = ds.Tables[0];
            //            //txtBarCode.Focus();
            //            //txtBarCode.Clear();
            //            Conn.Close();
            //        }

            //        else
            //        {
            //            //     MessageBox.Show("Lot & Part Number Incorrect");
            //            MessageBox.Show("Already issued");
            //        }
            //    } 
            //}
        }

        private void btnUpdateEmp_Click(object sender, EventArgs e)
        {

            frmUpdateUser frm = new frmUpdateUser();
            frm.Show();
            frm.LoadDetail();
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit Application?", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                Application.Exit();
            }
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            frmWelcome frm = new frmWelcome();
            frm._strUser = lblUser.Text;
            frm.sectid = this.sectid;
            frm.Show();
            this.Hide();
        }


        //private void btnCurrentSkill_Click(object sender, EventArgs e)
        //{
        //    gridDisqualified.DataSource = null;
        //    gridCurrentSkill.DataSource = null;
        //    string strSql = "Select ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,TestResult,convert(varchar,CertifiedDate,106) as CertifiedDate,KnowledgeLevel,SkillLevel,Judgment,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,Remark,Download From tblQualified where Empcode='" + txtEmpNo.Text.Trim() + "'and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
        //    DataTable dt = new DataTable();
        //    DataSet ds = new DataSet();
        //    SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
        //    da.Fill(dt);
        //    da.Fill(ds, "Qualified");
        //    if (dt.Rows.Count > 0)
        //    {

        //        gridCurrentSkill.DataSource = ds.Tables[0];
        //        txtQualified.Text = dt.Rows.Count.ToString();
        //        // gridCurrentSkill.Columns[0].HeaderText = "Disqualified";
        //        gridCurrentSkill.Columns[1].HeaderText = "Process";
        //        gridCurrentSkill.ForeColor = Color.Black;
        //        gridCurrentSkill.ReadOnly = false;
        //        gridCurrentSkill.Show();

        //    }
        //    grbAddSkill.Hide();
        //}

        //private void btnDisqualified_Click(object sender, EventArgs e)
        //{            
        //    gridCurrentSkill.Hide();

        //    string strSql2 = "Select ProcessName,convert(varchar,CertifiedDate,106) as CertifiedDate,KnowledgeLevel,SkillLevel,Verifier,convert(varchar,DisQualifiedDate,106) as DisQualifiedDate,DisQualifiedBy,TheReason,Remark From tblQualified where Empcode='" + txtEmpNo.Text.Trim() + "'and DisQualifiedBy!=''";
        //    DataTable dt2 = new DataTable();
        //    DataSet ds = new DataSet();
        //    SqlDataAdapter da2 = new SqlDataAdapter(strSql2, strConnString);
        //    da2.Fill(dt2);
        //    da2.Fill(ds, "Qualified");
        //    if (dt2.Rows.Count > 0)
        //    {
        //        gridCurrentSkill.DataSource = ds.Tables[0];
        //        gridCurrentSkill.ForeColor = Color.Red;
        //        gridCurrentSkill.ReadOnly = true;
        //        gridCurrentSkill.Show();
        //    }

        //    //   gridCurrentSkill.Show();
        //    grbAddSkill.Hide();
        //}
        //private void btnAddSkill_Click(object sender, EventArgs e)
        //{

        //    grbAddSkill.Size = new System.Drawing.Size(916, 317);
        //    grbAddSkill.Show();
        //    gridCurrentSkill.Hide();
        //    txtVerifier.Text = Login.UserName;


        //    DataSet ds = new DataSet();
        //    DataSet dsOpT = new DataSet();

        //    //************ ดึงข้อมูล Process ******

        //    string sql = "SELECT WorkshopName,WorkshopID FROM tblWorkshop";
        //    SqlDataAdapter da = new SqlDataAdapter(sql, strConnString);
        //    da.Fill(ds, "Workshop");
        //    cbbWorkShop.DisplayMember = "WorkshopName";
        //    cbbWorkShop.ValueMember = "WorkshopID";
        //    cbbWorkShop.DataSource = ds.Tables["Workshop"];

        //    cbbWorkShop.SelectedIndex = -1;
        //    cbbProcess.Text = "";

        //    //************ ดึงข้อมูล OperatorTraining ******

        //    sql = "SELECT OperatorTrainingName,OperatorTrainingID FROM tblOperatorTraining";
        //    SqlDataAdapter daOpT = new SqlDataAdapter(sql, strConnString);
        //    daOpT.Fill(dsOpT, "OperatorTraining");

        //    cbbOperatorTraining.DisplayMember = "OperatorTrainingName";
        //    cbbOperatorTraining.ValueMember = "OperatorTrainingID";
        //    cbbOperatorTraining.DataSource = dsOpT.Tables["OperatorTraining"];
        //    cbbOperatorTraining.SelectedIndex = -1;

        //    dTimeExpiry.ResetText();

        //}

        private void cbbWorkShop_SelectedIndexChanged(object sender, EventArgs e)
        {
            cbbProcess.SelectedIndex = -1;
            DataSet ds2 = new DataSet();
            string sql2 = "";

            sql2 = "SELECT ProcessID,ProcessNAME FROM tblProcess ";
            sql2 += "where WorkshopID = @WorkshopID ";

            string WorksID = "";
            if (cbbWorkShop.SelectedValue == null)
            {
                WorksID = "";
            }
            else
            {
                WorksID = cbbWorkShop.SelectedValue.ToString();
            }

            SqlDataAdapter da2 = new SqlDataAdapter(sql2, strConnString);
            da2.SelectCommand.Parameters.Add("@WorkshopID", SqlDbType.NVarChar).Value = WorksID;

            da2.Fill(ds2, "Process");
            cbbProcess.DisplayMember = "ProcessName";
            cbbProcess.ValueMember = "ProcessID";
            cbbProcess.DataSource = ds2.Tables["Process"];


        }
        private void btnClear_Click(object sender, EventArgs e)
        {
            cbbWorkShop.Text = "";
            cbbProcess.Text = "";
            cbbOperatorTraining.Text = "";
            txtFullScore.Text = "";            
            txtActualScore.Text = "";
            lblTestResult.Text = "";
            lblJudgmentTheory.Text = "";
            txtKnowledgeScore.Text = "";
            lblKnowleageLevel.Text = "";
            txtSkillScore.Text = "";
            lblSkillLevel.Text = "";
            lblJudgmentPractice.Text = "";
            txtRemark.Text = "";


        }

        private void btnConfirmAddSkill_Click(object sender, EventArgs e)
        {

            string filePath = Filedlg.FileName;
            string fileName = System.IO.Path.GetFileName(filePath);

            if (cbbProcess.Text.Trim() == "")
            {
                MessageBox.Show("Please input the Process !!!", "PartNumber None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (cbbOperatorTraining.Text.Trim() == "")
            {
                MessageBox.Show("Please input the OperatorTraining !!!", "OperatorTraining None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (lblTestResult.Text.Trim() == "")
            {
                MessageBox.Show("Please input the TestResult !!!", "TestResult None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (lblKnowleageLevel.Text.Trim() == "")
            {
                MessageBox.Show("Please input the KnowleageLevel !!!", "KnowleageLevel None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (lblSkillLevel.Text.Trim() == "")
            {
                MessageBox.Show("Please input the SkillLevel !!!", "SkillLevel None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (fileName == "")
            {
                MessageBox.Show("Please select upload PDFFile !!!", "File None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (Regex.Match(btnUpload.Text.ToString(), "'").Success)
            {
                MessageBox.Show("Please Input the PDFFile !!!", "File incorrect", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            Conn.ConnectionString = strConnString;
            Conn.Open();
            string strSql = "Select * From tblQualified where EmpCode='" + txtEmpNo.Text.Trim() + "' and ProcessName='" + cbbProcess.Text.Trim() + "' and (DisQualifiedDate ='' or DisQualifiedDate is null)";
            //string strSql = "Select * From View_QualifiedProcess where EmpCode='" + txtEmpNo.Text.Trim() + "' and ProcessID='" + cbbProcess.SelectedValue.ToString() + "' and (DisQualifiedDate ='' or DisQualifiedDate is null)";
            DataTable dt = new DataTable();
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(dt);

            // string strFolderPath = Application.StartupPath + "/myFile/";
           /// string strFolderPath = "\\\\svr120a\\Cert$\\";
          //  OpenFileDialog Filedlg = new OpenFileDialog();
            //Filedlg.Filter = "Image,Pdf Only|*.pdf;*.jpg;*.jpeg";
            //Filedlg.Multiselect = false;
            //PdfFile = Filedlg.FileName;


            if (dt.Rows.Count == 0)
            {
                if (MessageBox.Show("Add Skill ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    sb = new StringBuilder();
                    sb.Remove(0, sb.Length);
                    //sb.Append("INSERT INTO tblQualified(EmpCode,ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark,Download) VALUES (@sEmpCode,@sProcessName,@sOperatorTraining,@sTheoryTraining,@sOJTTraining,@sCertifiedDate,@sFullScore,@sActualScore,@sTestResult,@sJudgmentTheory,@sKnowledgeScore,@sKnowledgeLevel,@sSkillScore,@sSkillLevel,@sJudgmentPractice,@sExpiryDate,@sVerifier,@sVerifierDate,@sRemark,@sDownload)");
                    sb.Append("INSERT INTO tblQualified(EmpCode,ProcessName,OperatorTraining,TheoryTraining,OJTTraining,CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,VerifierDate,Remark,Download) VALUES (@sEmpCode,@sProcessName,@sOperatorTraining,@sTheoryTraining,@sOJTTraining,@sCertifiedDate,@sFullScore,@sActualScore,@sTestResult,@sJudgmentTheory,@sKnowledgeScore,@sKnowledgeLevel,@sSkillScore,@sSkillLevel,@sJudgmentPractice,@sExpiryDate,@sVerifier,@sVerifierDate,@sRemark,@sDownload)");

                    string sqlSave = sb.ToString();

                    Cmd.CommandType = CommandType.Text;
                    Cmd.CommandText = sqlSave;
                    Cmd.Connection = Conn;

                    Cmd.Parameters.Clear();

                    Cmd.Parameters.Add("@sEmpCode", SqlDbType.NVarChar).Value = txtEmpNo.Text.Trim();
                    Cmd.Parameters.Add("@sProcessName", SqlDbType.NVarChar).Value = cbbProcess.Text.Trim();
                    Cmd.Parameters.Add("@sOperatorTraining", SqlDbType.NVarChar).Value = cbbOperatorTraining.Text.Trim();
                    Cmd.Parameters.Add("@sTheoryTraining", SqlDbType.Date).Value = dTimeTheory.Text.Trim();
                    Cmd.Parameters.Add("@sOJTTraining", SqlDbType.Date).Value = dTimeOJT.Text.Trim();
                    Cmd.Parameters.Add("@sCertifiedDate", SqlDbType.Date).Value = dTimeCertified.Text.Trim();
                    Cmd.Parameters.Add("@sFullScore", SqlDbType.NVarChar).Value = txtFullScore.Text.Trim();
                    Cmd.Parameters.Add("@sActualScore", SqlDbType.NVarChar).Value = txtActualScore.Text.Trim();
                    Cmd.Parameters.Add("@sTestResult", SqlDbType.NVarChar).Value = lblTestResult.Text.Trim();
                    Cmd.Parameters.Add("@sJudgmentTheory", SqlDbType.NVarChar).Value = lblJudgmentTheory.Text.Trim();
                    Cmd.Parameters.Add("@sKnowledgeScore", SqlDbType.NVarChar).Value = txtKnowledgeScore.Text.Trim();
                    Cmd.Parameters.Add("@sKnowledgeLevel", SqlDbType.NVarChar).Value = lblKnowleageLevel.Text.Trim();
                    Cmd.Parameters.Add("@sSkillScore", SqlDbType.NVarChar).Value = txtSkillScore.Text.Trim();
                    Cmd.Parameters.Add("@sSkillLevel", SqlDbType.NVarChar).Value = lblSkillLevel.Text.Trim();
                    Cmd.Parameters.Add("@sJudgmentPractice", SqlDbType.NVarChar).Value = lblJudgmentPractice.Text.Trim();
                    Cmd.Parameters.Add("@sExpiryDate", SqlDbType.Date).Value = dTimeExpiry.Text.Trim();
                    Cmd.Parameters.Add("@sVerifier", SqlDbType.NVarChar).Value = txtVerifier.Text.Trim();
                    Cmd.Parameters.Add("@sVerifierDate", SqlDbType.NVarChar).Value = DateTime.Now;
                    Cmd.Parameters.Add("@sRemark", SqlDbType.NVarChar).Value = txtRemark.Text.Trim();


                    //*** Save File \\svr120a\Cert$
                    System.IO.Directory.CreateDirectory(@"\\svr120a\Cert$\" + frmWelcome.UserID + "");

                    File.Copy(filePath, strFolderPath + fileName, true);

                    Cmd.Parameters.Add("@sDownload", SqlDbType.NVarChar).Value = strFolderPath + fileName;


                    Cmd.ExecuteNonQuery();
                    //Conn.Close();


                    MessageBox.Show("SAVE Complete", "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            else
            {
                MessageBox.Show("Already Skill");
               
            }
            Conn.Close();
        }
        private void btnUpdateSkill_Click(object sender, EventArgs e)
        {

            // *****Update Skill*****
            string strUpdateSkill = string.Empty;
            bool chkUpdateSkill = false;

            foreach (DataGridViewRow row in this.gridCurrentSkill.Rows)
            {

                if (Convert.ToBoolean(row.Cells["cbcDisqualified"].Value) == true)
                {


                    chkUpdateSkill = true;
                    //   strDisQualified = strDisQualified + row.Cells["ProcessName"].Value + ", ";
                    strUpdateSkill = strUpdateSkill + row.Cells["Process"].Value;

                    // DisqualificationID = row.Cells["ProcessName"].Value.ToString();
                    SelectqualificationID = strUpdateSkill;
                    EmpCodeID = txtEmpNo.Text;
                    frmUpdateSkill frm = new frmUpdateSkill();
                //    frmDisqualification frm = new frmDisqualification();
                    frm.ShowDialog();
                }

            }
            if (chkUpdateSkill == false)
            {
                MessageBox.Show("Please select Process !!!", "Process None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            
            
        }

        private void txtFullScore_KeyPress(object sender, KeyPressEventArgs e)
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


        private void txtActualScore_KeyPress(object sender, KeyPressEventArgs e)
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
        private void txtSkillScore_KeyPress(object sender, KeyPressEventArgs e)
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

        private void txtKnowledgeScore_KeyPress(object sender, KeyPressEventArgs e)
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

        private void txtActualScore_TextChanged(object sender, EventArgs e)
        {
            int FullScore = 0;
            int ActualScore = 0;

            if (txtFullScore.Text == "")
            {
                txtFullScore.Text = "0";
            }
            if (txtActualScore.Text == "")
            {
                txtActualScore.Text = "0";
            }
            //if ((txtActualScore.Text != "") && ((Convert.ToInt16(txtFullScore.Text) > 0)))
            if (Convert.ToInt16(txtFullScore.Text) > 0)
            {
                FullScore = int.Parse(txtFullScore.Text);
                ActualScore = int.Parse(txtActualScore.Text);
                lblTestResult.Text = ((ActualScore * 100) / FullScore).ToString();

                if (int.Parse(lblTestResult.Text) >= 80)
                {
                    lblJudgmentTheory.Text = "Pass";
                    lblTestResult.ForeColor = Color.Black;
                    lblJudgmentTheory.ForeColor = Color.Black;
                }
                else
                {

                    lblJudgmentTheory.Text = "Fail";
                    lblTestResult.ForeColor = Color.Red;
                    lblJudgmentTheory.ForeColor = Color.Red;

                }
            }
            else
            {
                return;
            }
        }

        private void txtFullScore_TextChanged(object sender, EventArgs e)
        {
            int FullScore = 0;
            int ActualScore = 0;

            // if ((txtActualScore.Text != "") && (txtFullScore.Text != ""))
            if (txtFullScore.Text == "")
            {
                txtFullScore.Text = "0";
            }
            if (txtActualScore.Text == "")
            {
                txtActualScore.Text = "0";
            }
            //if ((txtActualScore.Text != "") && ((Convert.ToInt16(txtFullScore.Text) > 0)))
            if (Convert.ToInt16(txtFullScore.Text) > 0)
            {
                FullScore = int.Parse(txtFullScore.Text);
                ActualScore = int.Parse(txtActualScore.Text);
                lblTestResult.Text = ((ActualScore * 100) / FullScore).ToString();

                if (int.Parse(lblTestResult.Text) >= 80)
                {
                    lblJudgmentTheory.Text = "Pass";
                    lblTestResult.ForeColor = Color.Black;
                    lblJudgmentTheory.ForeColor = Color.Black;
                }
                else
                {

                    lblJudgmentTheory.Text = "Fail";
                    lblTestResult.ForeColor = Color.Red;
                    lblJudgmentTheory.ForeColor = Color.Red;
                }

            }
            else
            {
                return;
            }
        }

        private void txtKnowledgeScore_TextChanged(object sender, EventArgs e)
        {
            int KnowledgeScore;
            int SkillScore;

            if (txtKnowledgeScore.Text == "")
            {
                txtKnowledgeScore.Text = "0";
            }
            KnowledgeScore = int.Parse(txtKnowledgeScore.Text);


            if (txtSkillScore.Text == "")
            {
                txtSkillScore.Text = "0";
            }
            SkillScore = int.Parse(txtSkillScore.Text);


            //if (KnowledgeScore >= 26)
            //{
            //    lblJudgment.Text = "Pass";
            //    lblJudgment.ForeColor = Color.Black;
            //}
            if (KnowledgeScore > 100)
            {
                lblKnowleageLevel.Text = "O";
                lblKnowleageLevel.ForeColor = Color.Black;
            }
            else if (KnowledgeScore >= 76)
            {
                lblKnowleageLevel.Text = "U";
                lblKnowleageLevel.ForeColor = Color.Black;
            }
            else if (KnowledgeScore >= 51)
            {
                lblKnowleageLevel.Text = "L";
                lblKnowleageLevel.ForeColor = Color.Black;
            }
            else if (KnowledgeScore >= 26)
            {
                lblKnowleageLevel.Text = "I";
                lblKnowleageLevel.ForeColor = Color.Black;
            }
            else if (KnowledgeScore <= 25)
            {
                lblKnowleageLevel.Text = "X";
                lblJudgmentPractice.Text = "Fail";
                lblKnowleageLevel.ForeColor = Color.Red;
                lblJudgmentPractice.ForeColor = Color.Red;
            }

            if (lblKnowleageLevel.Text != "X" && lblSkillLevel.Text != "X")
            {
                lblJudgmentPractice.Text = "Pass";
                lblJudgmentPractice.ForeColor = Color.Black;
            }
            else if (lblKnowleageLevel.Text == "X" || lblSkillLevel.Text != "X")
            {
                lblJudgmentPractice.Text = "Fail";
                lblJudgmentPractice.ForeColor = Color.Red;
            }
        }

        private void txtSkillScore_TextChanged(object sender, EventArgs e)
        {

            int KnowledgeScore;
            int SkillScore;

            if (txtKnowledgeScore.Text == "")
            {
                txtKnowledgeScore.Text = "0";
            }
            KnowledgeScore = int.Parse(txtKnowledgeScore.Text);


            if (txtSkillScore.Text == "")
            {
                txtSkillScore.Text = "0";
            }
            SkillScore = int.Parse(txtSkillScore.Text);

            if (KnowledgeScore >= 26 && SkillScore >= 26)
            {
                lblJudgmentPractice.Text = "Pass";
                lblJudgmentPractice.ForeColor = Color.Black;

            }

            if (SkillScore > 100)
            {
                lblSkillLevel.Text = "O";
                lblSkillLevel.ForeColor = Color.Black;
            }
            else if (SkillScore >= 76)
            {
                lblSkillLevel.Text = "U";
                lblSkillLevel.ForeColor = Color.Black;
            }
            else if (SkillScore >= 51)
            {
                lblSkillLevel.Text = "L";
                lblSkillLevel.ForeColor = Color.Black;
            }
            else if (SkillScore >= 26)
            {
                lblSkillLevel.Text = "I";
                lblSkillLevel.ForeColor = Color.Black;
            }
            else
            {
                lblSkillLevel.Text = "X";
                lblJudgmentPractice.Text = "Fail";
                lblSkillLevel.ForeColor = Color.Red;
                lblJudgmentPractice.ForeColor = Color.Red;
            }
        }

        private void dTimeCertified_ValueChanged(object sender, EventArgs e)
        {
            // บวกค่าไปอีก 365-1 วัน จากวัน Certified Date //
            dTimeExpiry.Value = dTimeCertified.Value.AddDays(364);

        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {


        }

        private void btnUpload_Click(object sender, EventArgs e)
        {
           // string strFolderPath = Application.StartupPath + "/myFile/";
            //string strFolderPath = "\\\\svr120a\\Cert$\\";
            //OpenFileDialog Filedlg = new OpenFileDialog();
            Filedlg.Filter = "Pdf Only|*.pdf";
            Filedlg.Multiselect = false;
            PdfFile = Filedlg.FileName;



            if (Filedlg.ShowDialog() == DialogResult.OK)
            {
                //*** Create Folder
                //if (!Directory.Exists(strFolderPath))
                //{
                //    Directory.CreateDirectory(strFolderPath);
                //}
                
               // if (!Regex.Match( txtLotNo.Text.Trim().ToUpper(), "^[0-9A-Z]*$").Success || !Regex.Match(txtPartNo.Text.Trim().ToUpper(), "^[A-Z0-9( )-]*$").Success)
                this.btnUpload.Text = Filedlg.FileName;
                if (Regex.Match(btnUpload.Text.ToString(), "'").Success)
                {
                    MessageBox.Show("File invalid Text");

                }

                //*** Save File \\svr120a\Cert$
                //string filePath = Filedlg.FileName;
                //string fileName = System.IO.Path.GetFileName(filePath);
                ////File.Copy(filePath, strFolderPath + fileName, true);

                //MessageBox.Show("อัพโหลดไฟล์เรียบร้อยแล้ว");
            }

        }

        private void btnDisOK_Click(object sender, EventArgs e)
        {


            // *****DisQualified*****
            string strDisQualified = string.Empty;
            bool chkDisQualified = false;

            foreach (DataGridViewRow row in this.gridCurrentSkill.Rows)
            {

                if (Convert.ToBoolean(row.Cells["cbcDisqualified"].Value) == true)
                {


                    chkDisQualified = true;
                 //   strDisQualified = strDisQualified + row.Cells["ProcessName"].Value + ", ";
                    strDisQualified = strDisQualified + row.Cells["Process"].Value;

                   // DisqualificationID = row.Cells["ProcessName"].Value.ToString();
                    SelectqualificationID = strDisQualified;
                    EmpCodeID = txtEmpNo.Text;
                    frmDisqualification frm = new frmDisqualification();
                    frm.ShowDialog();
                }

            }
            if (chkDisQualified == false)
            {
                MessageBox.Show("Please select Process !!!", "DisQualified None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }



        }

        private void tabContro1_SelectedIndexChanged(object sender, EventArgs e)
        {
           
            //dTimeExpiry.Value = DateTime.Now.AddDays(364);
            if (TabQualified.SelectedIndex == 0)
            {
                //gridCurrentSkill.DataSource = null;                
               // gridCurrentSkill.Show();
                //string strSql = "Select ProcessName,OperatorTraining,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,TestResult,convert(varchar,CertifiedDate,106) as CertifiedDate,KnowledgeLevel,SkillLevel,Judgment,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,Remark,Download From tblQualified where Empcode='" + txtEmpNo.Text.Trim() + "'and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
                //DataTable dt = new DataTable();
                //DataSet ds = new DataSet();
                //SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
                //da.Fill(dt);
                //da.Fill(ds, "Qualified");
                //if (dt.Rows.Count > 0)
                //{

                //    gridCurrentSkill.DataSource = ds.Tables[0];

                //    txtQualified.Text = dt.Rows.Count.ToString();

                //    gridCurrentSkill.ForeColor = Color.Black;
                //    gridCurrentSkill.ReadOnly = false;
                //    gridCurrentSkill.Show();

                //    // **************** Add button2 download ************
                //DataGridViewButtonColumn btn2 = new DataGridViewButtonColumn();
                //gridCurrentSkill.Columns.Add(btn2);
                //btn2.HeaderText = "Download2";
                //btn2.Text = "Click2";
                //btn2.Name = "btn2";
                //gridCurrentSkill.Columns["btn2"].DisplayIndex = 15;
                //}
                //grbAddSkill.Hide();

                // **************** Add button download ************

               // btn2.UseColumnTextForButtonValue = true;
                //     this.gridCurrentSkill.Columns["btn"].DisplayIndex = 1;

                GetData1();

            }


            if (TabQualified.SelectedIndex == 1)
            {
               // gridCurrentSkill.DataSource = null;
                // gridCurrentSkill.Hide();

                string strSql2 = "Select ProcessName as Process,convert(varchar,CertifiedDate,106) as CertifiedDate,KnowledgeLevel as K,SkillLevel as S,Verifier,convert(varchar,DisQualifiedDate,106) as DisQualifiedDate,DisQualifiedBy,TheReason,Remark,Download From tblQualified where Empcode='" + txtEmpNo.Text.Trim() + "'and DisQualifiedBy!='' order by CertifiedDate DESC";
                DataTable dt2 = new DataTable();
                DataSet ds = new DataSet();
                SqlDataAdapter da2 = new SqlDataAdapter(strSql2, strConnString);
                da2.Fill(dt2);
                da2.Fill(ds, "Qualified");
                if (dt2.Rows.Count > 0)
                {

                    gridDisqualified.DataSource = ds.Tables[0];
                  //  gridDisqualified.Columns[0].HeaderText = "Process";
                    gridDisqualified.ForeColor = Color.Red;
                    gridDisqualified.ReadOnly = true;                  

                    gridDisqualified.Columns[10].Visible = false;
                    gridDisqualified.Show();
                    gridDisqualified.Columns["btn_DownloadDis"].DisplayIndex = gridDisqualified.Columns.Count - 1;
                    
                }

                //   gridCurrentSkill.Show();
                //  grbAddSkill.Hide();
            }
            if (TabQualified.SelectedIndex == 2)
            {
                // gridCurrentSkill.DataSource = null;
                // gridCurrentSkill.Hide();

                string strSql3 = "Select ProcessName as Process,OperatorTraining as CertifyClassification,convert(varchar,TheoryTraining,106) as Theory,convert(varchar,OJTTraining,106) as OJT,TestResult as Result,convert(varchar,CertifiedDate,106) as CertifiedDate,KnowledgeLevel as K,SkillLevel as S,JudgmentPractice as Judgment,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,Remark,Download From tblQualified_Obsoleted where Empcode='" + txtEmpNo.Text.Trim() + "' order by CertifiedDate DESC";
                DataTable dt3 = new DataTable();
                DataSet ds3 = new DataSet();
                SqlDataAdapter da3 = new SqlDataAdapter(strSql3, strConnString);
                da3.Fill(dt3);
                da3.Fill(ds3, "Obsoleted");
                if (dt3.Rows.Count > 0)
                {

                    gridObsoleted.DataSource = ds3.Tables[0];
                    gridObsoleted.ReadOnly = true;
                    gridObsoleted.ForeColor = Color.LightSlateGray;
                    gridObsoleted.Columns[13].Visible = false;
                    gridObsoleted.Show();
                    gridObsoleted.Columns["btn_DownloadObsoleted"].DisplayIndex = gridObsoleted.Columns.Count - 1;

                }

                //   gridCurrentSkill.Show();
                //  grbAddSkill.Hide();
            }

            if (TabQualified.SelectedIndex == 3)
            {
                gridCurrentSkill.DataSource = null;
                grbAddSkill.Size = new System.Drawing.Size(916, 317);
                grbAddSkill.Show();
                //  gridCurrentSkill.Hide();
                txtVerifier.Text = Login.UserName;


                DataSet ds = new DataSet();
                DataSet dsOpT = new DataSet();

                //************ ดึงข้อมูล Process ******

                string sql = "SELECT WorkshopName,WorkshopID FROM tblWorkshop";
                SqlDataAdapter da = new SqlDataAdapter(sql, strConnString);
                da.Fill(ds, "Workshop");
                cbbWorkShop.DisplayMember = "WorkshopName";
                cbbWorkShop.ValueMember = "WorkshopID";
                cbbWorkShop.DataSource = ds.Tables["Workshop"];

                cbbWorkShop.SelectedIndex = -1;
                cbbProcess.Text = "";

                //************ ดึงข้อมูล OperatorTraining ******

                sql = "SELECT OperatorTrainingName,OperatorTrainingID FROM tblOperatorTraining where OperatorTrainingID !='OT3'";
                SqlDataAdapter daOpT = new SqlDataAdapter(sql, strConnString);
                daOpT.Fill(dsOpT, "OperatorTraining");

                cbbOperatorTraining.DisplayMember = "OperatorTrainingName";
                cbbOperatorTraining.ValueMember = "OperatorTrainingID";
                cbbOperatorTraining.DataSource = dsOpT.Tables["OperatorTraining"];
                cbbOperatorTraining.SelectedIndex = -1;                
                dTimeExpiry.ResetText();

            }
            //if (TabQualified.TabPages.ToString() == "tabObsoleted")
            
        }

        private void btnResign_Click(object sender, EventArgs e)
        {
            string strSql2 = "Select * From tblEmployee where Empcode='" + txtEmpNo.Text.Trim() + "'";
            DataTable dt2 = new DataTable();
            SqlDataAdapter da2 = new SqlDataAdapter(strSql2, strConnString);
            da2.Fill(dt2);
         //   txtQualified.Text = dt2.Rows.Count.ToString();
            sb = new StringBuilder();      // reset ค่า sb

            Conn = new SqlConnection();
            Conn.ConnectionString = strConnString;
            Conn.Open();
            if (dt2.Rows.Count > 0)
            {
                if (MessageBox.Show("Resign Employee No :" + txtEmpNo.Text.ToString() + "", "Resign !", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    sb.Append("UPDATE tblEmployee set StatusWork='").Append("0").Append("', ResignBy='").Append(Login.UserName).Append("', ResignDate = GETDATE()");

                    sb.Append("WHERE EmpCode='").Append(txtEmpNo.Text).Append("'and (ResignBy= ''or ResignBy is Null)").Append("");


                    string sqlSave = sb.ToString();


                    Cmd.CommandType = CommandType.Text;
                    Cmd.CommandText = sqlSave;
                    Cmd.Connection = Conn;
                    Cmd.ExecuteNonQuery();
                    Conn.Close();
                }
            }
            frmWelcome frm = new frmWelcome();
            frm._strUser = lblUser.Text;
            frm.sectid = this.sectid;
            frm.Show();
            this.Hide();
 
        }



        private void gridCurrentSkill_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            //if (e.ColumnIndex == gridCurrentSkill.Columns["btn"].Index)

            if (e.ColumnIndex == 1)
            {
                //Do something with your button.

                //*** download File \\svr120a\Cert$
                string downloadFile = gridCurrentSkill[13, e.RowIndex].Value.ToString();

                string filePath = @"C:\Temp\";


                if (downloadFile != null && downloadFile != "")
                {
                    string[] sAry = downloadFile.Split('\\');
                    string str1 = sAry[5].ToString();

                    string fileName = filePath + downloadFile.Replace(strFolderPath, "");

                    File.Copy(downloadFile, fileName, true);

                    MessageBox.Show("Download file " + str1 + " ", "None", MessageBoxButtons.OK, MessageBoxIcon.Information);

                }
                else
                {

                    MessageBox.Show("File None", "None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

                }

            }
        }

        private void gridDisqualified_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            //if (e.ColumnIndex == gridCurrentSkill.Columns["btn"].Index)

            if (e.ColumnIndex == 0)
            {
                //Do something with your button.

                //*** download File \\svr120a\Cert$
                string downloadFile = gridDisqualified[10, e.RowIndex].Value.ToString();

                string filePath = @"C:\Temp\";


                if (downloadFile != null && downloadFile != "")
                {
                    string[] sAry = downloadFile.Split('\\');
                    string str1 = sAry[5].ToString();

                    string fileName = filePath + downloadFile.Replace(strFolderPath, "");

                    File.Copy(downloadFile, fileName, true);

                    MessageBox.Show("Download file " + str1 + " ", "None", MessageBoxButtons.OK, MessageBoxIcon.Information);

                }
                else
                {

                    MessageBox.Show("File None", "None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

                }

            }
        }
        private void gridObsoleted_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            //if (e.ColumnIndex == gridCurrentSkill.Columns["btn"].Index)

            if (e.ColumnIndex == 0)
            {
                //Do something with your button.

                //*** download File \\svr120a\Cert$
                string downloadFile = gridObsoleted[13, e.RowIndex].Value.ToString();

                string filePath = @"C:\Temp\";


                if (downloadFile != null && downloadFile != "")
                {
                    string[] sAry = downloadFile.Split('\\');
                    string str1 = sAry[5].ToString();

                    string fileName = filePath + downloadFile.Replace(strFolderPath, "");

                    File.Copy(downloadFile, fileName, true);

                    MessageBox.Show("Download file " + str1 + " ", "None", MessageBoxButtons.OK, MessageBoxIcon.Information);

                }
                else
                {

                    MessageBox.Show("File None", "None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

                }

            }
        }

        private void btnDownloadCurrent_Click(object sender, EventArgs e)
        {
            DataTable dt = receptDataDetail();
            DataSet ds = new DataSet(); // สร้าง ds

            string strSql;

            strSql = "SELECT EmpCode,convert(varchar,JoinDate,106) as JoinDate,HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,WorkshopName,JobGrade,Shift,ProcessName,OperatorTraining as CertifyClassification,convert(varchar,TheoryTraining,106) as TheoryTraining,convert(varchar,OJTTraining,106) as OJTTraining,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,convert(varchar,CertifiedDate,106) as CertifiedDate,convert(varchar,ExpiryDate,106) as ExpiryDate,Verifier,convert(varchar,VerifierDate,106) as VerifierDate,Remark FROM ViewEmpQualified_ALL where EmpCode = '" + frmWelcome.UserID + "' and (DisQualifiedBy= ''or DisQualifiedBy is Null) ";
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(ds, "Qualified");
            if (MessageBox.Show("Download Current ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
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
                   
                }

                try
                {

                    xlWorkBook.SaveAs(@"c:\Temp\Current Skill " + frmWelcome.UserID +  " " + DateTime.Now.ToString("yyyMMdd") + ".xls", Excel.XlFileFormat.xlWorkbookNormal, misValue, misValue, misValue, misValue, Excel.XlSaveAsAccessMode.xlExclusive, misValue, misValue, misValue, misValue, misValue);
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

        private void button31_Click(object sender, EventArgs e)
        {
            //System.IO.Directory.CreateDirectory(@"C:\project.v1\" + DateTime.Now.ToShortDateString() + "\\การเงิน");
            //System.IO.Directory.CreateDirectory(@"C:\project.v1\" + DateTime.Now.ToShortDateString() + "\\วิชาการ");
            System.IO.Directory.CreateDirectory(@"\\svr120a\Cert$\" + frmWelcome.UserID + "");
           

        }

        private void frmOperatorTraining_FormClosing(object sender, FormClosingEventArgs e)
        {
            Application.Exit();
        }

        
    }
}
