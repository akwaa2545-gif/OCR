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
    public partial class frmUpdateUser : Form
    {
        OpenFileDialog Phodlg = new OpenFileDialog();
       //private Image EmpPhoto;
        private byte[] CurrImg;
        private string ImgFile;
       // private FileStream fs;
       // private MemoryStream ms;


        StringBuilder sb = new StringBuilder();
        SqlConnection Conn = new SqlConnection();
        //string strConnString = "Server=localhost;UID=sa;PASSWORD=password;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
        string strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";

        SqlCommand Cmd = new SqlCommand();


       // string strFolderPath = "\\\\svr120a\\PhotoEmp$\\";
        private void ConvertImg(Image Img)
        {
            MemoryStream ms = new MemoryStream();
            //picPhotoEmp.Image.Save(ms, ImageFormat.Jpeg);
            Img.Save(ms, ImageFormat.Jpeg);

            //Read from MemoryStream into Byte array.
            CurrImg = new Byte[ms.Length];
            ms.Position = 0;
            ms.Read(CurrImg, 0, Convert.ToInt32(ms.Length));
        }



        public frmUpdateUser()
        {
            InitializeComponent();
        }

        private void frmUpdateUser_Load(object sender, EventArgs e)
        {
            btnAddPhoto.Hide();

            cbbJobGrade.Items.Add("O1");
            cbbJobGrade.Items.Add("O2");
            cbbJobGrade.Items.Add("O3");
            cbbJobGrade.Items.Add("OL1");

            cbbPrefixEng.Items.Add("Ms.");
            cbbPrefixEng.Items.Add("Mrs.");
            cbbPrefixEng.Items.Add("Mr.");

            cbbPrefixThai.Items.Add("นางสาว");
            cbbPrefixThai.Items.Add("นาง");
            cbbPrefixThai.Items.Add("นาย");

            cbbShift.Items.Add("A");
            cbbShift.Items.Add("B");
            cbbShift.Items.Add("C");
            cbbShift.Items.Add("DAY");

            cbbNoSkill.Items.Add("New comer");
            cbbNoSkill.Items.Add("Pregnant");
            cbbNoSkill.Items.Add("TA-Wire");
            cbbNoSkill.Items.Add("Handicapped");
            cbbNoSkill.Items.Add("Chronic");
         //   cbbNoSkill.Items.Add("Transfer");

            //************ ดึงข้อมูล Department ******
            DataSet ds = new DataSet();
            string sql = "SELECT DeptName,DeptID FROM tblDepartment";
            SqlDataAdapter da = new SqlDataAdapter(sql, strConnString);
            da.Fill(ds, "Dept");
            cbbDeptName.DisplayMember = "DeptName";
            cbbDeptName.ValueMember = "DeptID";
            cbbDeptName.DataSource = ds.Tables["Dept"];

            cbbDeptName.SelectedIndex = -1;


            //************ ดึงข้อมูล Section ******
            DataSet dsSect = new DataSet();
            sql = "SELECT SectName,SectID FROM tblSection";
            SqlDataAdapter daSect = new SqlDataAdapter(sql, strConnString);
            daSect.Fill(dsSect, "Section");

            cbbSectionName.DisplayMember = "SectName";
            cbbSectionName.ValueMember = "SectID";
            cbbSectionName.DataSource = dsSect.Tables["Section"];
            cbbSectionName.SelectedIndex = -1;


            //************ ดึงข้อมูล Workshop ******
            DataSet dsWorkshop = new DataSet();
            sql = "SELECT WorkshopName,WorkshopID FROM tblWorkshop";
            SqlDataAdapter daWorkshop = new SqlDataAdapter(sql, strConnString);
            daWorkshop.Fill(dsWorkshop, "Workshop");

            cbbWorkshop.DisplayMember = "WorkshopName";
            cbbWorkshop.ValueMember = "WorkshopID";
            cbbWorkshop.DataSource = dsWorkshop.Tables["Workshop"];
            cbbWorkshop.SelectedIndex = -1;

        }



        private void btnAddPhoto_Click(object sender, EventArgs e)
        {



            if (picPhotoEmp.Image == null)
            {
                //  Phodlg.Title = "Please select image file";
                Phodlg.Filter = "JPEG(*.jpg)|*.jpg|GIF(*.gif)|*.gif|Bitmap (*.bmp)|*.bmp";
                Phodlg.FileName = "";
                Phodlg.Multiselect = false;
                Phodlg.FilterIndex = 0;
                Phodlg.ShowDialog();
                ImgFile = Phodlg.FileName;
                if (Phodlg.FileName != "")
                {
                    //picPhotoEmp.SizeMode = PictureBoxSizeMode.StretchImage;

                    picPhotoEmp.Image = Image.FromFile(ImgFile);
                }
                //   Phodlg.FileName = null;

                btnAddPhoto.Text = "Remove Photo";
            }
            else
            {
                btnAddPhoto.Text = "Add Photo";
                picPhotoEmp.Image = null;
            }


        }

        private void dTimeTheory_ValueChanged(object sender, EventArgs e)
        {

        }

        private void txtEmpNo_KeyPress(object sender, KeyPressEventArgs e)
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

        private void cbbPrefixEng_TextChanged(object sender, EventArgs e)
        {

            if (cbbPrefixEng.Text == "Mr.")
            {
                cbbPrefixThai.Text = "นาย";
            }
            else if (cbbPrefixEng.Text == "Ms.")
            {
                cbbPrefixThai.Text = "นางสาว";
            }
            else if (cbbPrefixEng.Text == "Mrs.")
            {
                cbbPrefixThai.Text = "นาง";
            }
        }

        private void btnConfirmAddUser_Click(object sender, EventArgs e)
        {
            string filePath = Phodlg.FileName;
            string fileName = System.IO.Path.GetFileName(filePath);


            if (txtEmpNo.Text.Trim() == "")
            {
                MessageBox.Show("Please input the Emp.No. !!!", "Emp None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            string strSql = "Select * From tblEmployee where Empcode='" + txtEmpNo.Text.Trim() + "'";
           // string strSql = "Select * From View_EmpRemark where Empcode='" + txtEmpNo.Text.Trim() + "'";
            DataTable dt = new DataTable();
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(dt);

            if (dt.Rows.Count > 0)
            {
                if (MessageBox.Show("Update Employee ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {


                    Image UserPhoto = picPhotoEmp.Image;

                    ConvertImg(UserPhoto);


                    Conn.ConnectionString = strConnString;
                    Conn.Open();
                    sb = new StringBuilder();
                    sb.Remove(0, sb.Length);

                    sb.Append("UPDATE tblEmployee set JoinDate ='").Append(dTimeEmpDate.Text).Append("', JobGrade='").Append(cbbJobGrade.Text).Append("', HEng='").Append(cbbPrefixEng.Text).Append("', PersonFnameEng='").Append(txtFNameEng.Text).Append("', PersonLnameEng='").Append(txtLNameEng.Text).Append("', HThai='").Append(cbbPrefixThai.Text.Trim()).Append("', PersonFnameThai='").Append(txtFNameThai.Text.Trim()).Append("', PersonLnameThai='").Append(txtLNameThai.Text.Trim()).Append("', DeptID='").Append(cbbDeptName.SelectedValue.ToString()).Append("', SectID='").Append(cbbSectionName.SelectedValue.ToString()).Append("', WorkshopID='").Append(cbbWorkshop.SelectedValue.ToString()).Append("', Shift='").Append(cbbShift.Text).Append("', Notice='").Append(cbbNoSkill.Text).Append("'");
                    
                    sb.Append("WHERE Empcode= '").Append(txtEmpNo.Text).Append("'");

                   // sb.Append("UPDATE tblQualified set Remark ='").Append(cbbNoSkill.Text).Append("'");

                 //   sb.Append("WHERE Empcode= '").Append(txtEmpNo.Text).Append("'");

                    string sqlSave = sb.ToString();

                    Cmd.CommandType = CommandType.Text;
                    Cmd.CommandText = sqlSave;
                    Cmd.Connection = Conn;
        //            Cmd.Parameters.Clear();


                    MessageBox.Show("SAVE Complete", "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    Cmd.ExecuteNonQuery();
                    Conn.Close();
                }

            }

            else
            {

                MessageBox.Show("Can not Update User");
            }
            //frmWelcome frm = new frmWelcome();
            //frm._strUser = lblUser.Text;
            //frm.sectid = this.sectid;
            //frm.Show();
            this.Hide();
        }

        private void txtEmpNo_KeyUp(object sender, KeyEventArgs e)
        {

        }

        private void btnAddPic_Click(object sender, EventArgs e)
        {
            OpenFileDialog opfilePic = new OpenFileDialog();
            opfilePic.Multiselect = true;
            opfilePic.ShowDialog();

            // string[] uAry = opfilePic.OpenFile();
            //  string str1 = uAry[4].ToString();


        }

        private void btnUpdatePhoto_Click(object sender, EventArgs e)
        {
            if (txtEmpNo.Text.Trim() == "")
            {
                MessageBox.Show("Please input the Emp.No. !!!", "Emp None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            string strSql = "Select * From tblEmployee where Empcode='" + txtEmpNo.Text.Trim() + "'";
            DataTable dt = new DataTable();
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.Fill(dt);

            if (dt.Rows.Count > 0)
            {
                Image UserPhoto = picPhotoEmp.Image;

                ConvertImg(UserPhoto);


                Conn.ConnectionString = strConnString;
                Conn.Open();
                sb = new StringBuilder();
                sb.Remove(0, sb.Length);
                //  cmd.Parameters.Add("UserPhoto", SqlDbType.Image).Value = CurrImg;
                //sb.Append("INSERT INTO tblEmployee(EmpCode,EmpPassword,JoinDate,JobGrade,HEng,PersonFnameEng,PersonLnameEng,HThai,PersonFnameThai,PersonLnameThai) VALUES (@sEmpCode,@sEmpPassword,@sJoinDate,@sJobGrade,@sHEng,@sPersonFnameEng,@sPersonLnameEng,@sHThai,@sPersonFnameThai,@sPersonLnameThai)");

                sb.Append("UPDATE tblEmployee set Photo ='").Append(UserPhoto).Append("'");

                sb.Append("WHERE EmpCode='").Append(txtEmpNo.Text.Trim()).Append("'");


                string sqlSave = sb.ToString();

                Cmd.CommandType = CommandType.Text;
                Cmd.CommandText = sqlSave;
                Cmd.Connection = Conn;
                Cmd.Parameters.Clear();

                // Cmd.Parameters.Add("@sEmpCode", SqlDbType.Int).Value = txtEmpNo.Text.Trim();
                if (CurrImg != null)
                {
                    //      Cmd.Parameters.Add("@sPhoto", SqlDbType.Image).Value = CurrImg;
                }
                else
                {
                    //   Cmd.Parameters.Add("@sPhoto", SqlDbType.Image).Value = DBNull.Value;

                }

                Cmd.ExecuteNonQuery();
                Conn.Close();


            }
        }

        private void cbbDeptName_SelectedIndexChanged(object sender, EventArgs e)
        {
            cbbSectionName.SelectedIndex = -1;
            DataSet ds2 = new DataSet();
            string sql2 = "";

            sql2 = "SELECT SectID,SectNAME FROM tblSection ";
            sql2 += "where DeptID = @DeptID ";

            string DeptID = "";
            if (cbbDeptName.SelectedValue == null)
            {
                DeptID = "";
            }
            else
            {
                DeptID = cbbDeptName.SelectedValue.ToString();
            }

            SqlDataAdapter da2 = new SqlDataAdapter(sql2, strConnString);
            da2.SelectCommand.Parameters.Add("@DeptID", SqlDbType.NVarChar).Value = DeptID;

            da2.Fill(ds2, "Section");
            cbbSectionName.DisplayMember = "SectName";
            cbbSectionName.ValueMember = "SectID";
            cbbSectionName.DataSource = ds2.Tables["Section"];
        }
        private void btnCancel_Click(object sender, EventArgs e)
        {

        }
        public void LoadDetail()
        {
            DataTable dt = receptDataDetail();
            if (dt.Rows.Count > 0)
            {

                this.txtEmpNo.Text = dt.Rows[0]["EmpCode"].ToString();
               // this.txtEmpDate.Text = String.Format("{0:dd MMMM yyyy}", dt.Rows[0]["JoinDate"]);//dt.Rows[0]["JoinDate"].ToString();

                dTimeEmpDate.Text = dt.Rows[0]["JoinDate"].ToString();

                this.cbbJobGrade.Text = dt.Rows[0]["JobGrade"].ToString();
                this.cbbPrefixEng.Text = dt.Rows[0]["HEng"].ToString();
                this.cbbPrefixThai.Text = dt.Rows[0]["HThai"].ToString();
                this.txtFNameEng.Text = dt.Rows[0]["PersonFNameEng"].ToString();
                this.txtFNameThai.Text = dt.Rows[0]["PersonFNameThai"].ToString();
                this.txtLNameEng.Text = dt.Rows[0]["PersonLNameEng"].ToString();
                this.txtLNameThai.Text = dt.Rows[0]["PersonLNameThai"].ToString();
                this.cbbDeptName.Text = dt.Rows[0]["DeptName"].ToString();
                this.cbbSectionName.Text = dt.Rows[0]["SectName"].ToString();
                this.cbbWorkshop.Text = dt.Rows[0]["WorkshopName"].ToString();
                this.cbbShift.Text = dt.Rows[0]["Shift"].ToString();
                picPhotoEmp.ImageLocation = dt.Rows[0]["Photo"].ToString();

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
            //string strConnString = null;
            string strSQL = null;
            ////   string strSQL2 = null;

            //strConnString = "Server=localhost;UID=sa;PASSWORD=password;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
            ////strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=RecordTroubleReportDB;Max Pool Size=400;Connect Timeout=600;";
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

        private void btnExit_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit Update User ?", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                this.Hide();
            }
        }

        private void frmUpdateUser_FormClosing(object sender, FormClosingEventArgs e)
        {
            Application.Exit();
        }

        private void btnPromoted_Click(object sender, EventArgs e)
        {

        }
    }
}
