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
    public partial class frmAddUser : Form
    {
        OpenFileDialog Phodlg = new OpenFileDialog();
       // private Image EmpPhoto;
        private byte[] CurrImg;
        private string ImgFile;
       // private FileStream fs;
       // private MemoryStream ms;

        string strFolderPath = "\\\\svr120a\\PhotoEmp$\\";
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



        public frmAddUser()
        {
            InitializeComponent();
        }

        StringBuilder sb = new StringBuilder();
        SqlConnection Conn = new SqlConnection();
        //string strConnString = "Server=localhost;UID=sa;PASSWORD=password;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
        string strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";

        SqlCommand Cmd = new SqlCommand();

        private void frmAddUser_Load(object sender, EventArgs e)
        {
            // ตัวเลือกระดับงานใหม่สำหรับพนักงานที่เพิ่มเข้าระบบ
            cbbJobGrade.Items.Add("51T");
            cbbJobGrade.Items.Add("52T");
            cbbJobGrade.Items.Add("53T");
            cbbJobGrade.Items.Add("54T");

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
            else if (cbbDeptName.Text.Trim() == "")
            {
                MessageBox.Show("Please input the Dept !!!", "Dept None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (cbbSectionName.Text.Trim() == "")
            {
                MessageBox.Show("Please input the Section !!!", "Section None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (cbbWorkshop.Text.Trim() == "")
            {
                MessageBox.Show("Please input the Work shop !!!", "Work shop None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else if (cbbShift.Text.Trim() == "")
            {
                MessageBox.Show("Please input the Shift !!!", "Shift None", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            string strSql = "SELECT * FROM dbo.tblEmployee WHERE EmpCode=@employeeCode";
            DataTable dt = new DataTable();
            SqlDataAdapter da = new SqlDataAdapter(strSql, strConnString);
            da.SelectCommand.Parameters.Add("@employeeCode", SqlDbType.NVarChar, 10).Value = txtEmpNo.Text.Trim();
            da.Fill(dt);

            if (dt.Rows.Count == 0)
            {
                if (MessageBox.Show("Add Employee ? ", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {


                    Image UserPhoto = picPhotoEmp.Image;

                    if (UserPhoto != null) ConvertImg(UserPhoto);


                    Conn.ConnectionString = strConnString;
                    sb = new StringBuilder();
                    sb.Remove(0, sb.Length);
                    sb.Append("INSERT INTO tblEmployee(EmpCode,EmpPassword,JoinDate,JobGrade,HEng,PersonFnameEng,PersonLnameEng,HThai,PersonFnameThai,PersonLnameThai,DeptID,SectID,WorkshopID,Shift,Photo) VALUES (@sEmpCode,@sEmpPassword,@sJoinDate,@sJobGrade,@sHEng,@sPersonFnameEng,@sPersonLnameEng,@sHThai,@sPersonFnameThai,@sPersonLnameThai,@sDeptID,@sSectID,@sWorkshopID,@sShift,@sPhoto)");
                    //sb.Append("INSERT INTO tblEmployee(EmpCode,EmpPassword,JoinDate,JobGrade,HEng,PersonFnameEng,PersonLnameEng,HThai,PersonFnameThai,PersonLnameThai,Remark) VALUES (@sEmpCode,@sEmpPassword,@sJoinDate,@sJobGrade,@sHEng,@sPersonFnameEng,@sPersonLnameEng,@sHThai,@sPersonFnameThai,@sPersonLnameThai,@sRemark)");

                    string sqlSave = sb.ToString();

                    Cmd.CommandType = CommandType.Text;
                    Cmd.CommandText = sqlSave;
                    Cmd.Connection = Conn;
                    Cmd.Parameters.Clear();

                    Cmd.Parameters.Add("@sEmpCode", SqlDbType.NVarChar, 10).Value = txtEmpNo.Text.Trim();
                    Cmd.Parameters.Add("@sEmpPassword", SqlDbType.NVarChar).Value = txtEmpNo.Text.Trim();
                    Cmd.Parameters.Add("@sJoinDate", SqlDbType.Date).Value = dTimeEmpDate.Text.Trim();
                    Cmd.Parameters.Add("@sJobGrade", SqlDbType.NVarChar).Value = cbbJobGrade.Text.Trim();
                    Cmd.Parameters.Add("@sHEng", SqlDbType.NVarChar).Value = cbbPrefixEng.Text.Trim();
                    Cmd.Parameters.Add("@sPersonFnameEng", SqlDbType.NVarChar).Value = txtFNameEng.Text.Trim();
                    Cmd.Parameters.Add("@sPersonLnameEng", SqlDbType.NVarChar).Value = txtLNameEng.Text.Trim();
                    Cmd.Parameters.Add("@sHThai", SqlDbType.NVarChar).Value = cbbPrefixThai.Text.Trim();
                    Cmd.Parameters.Add("@sPersonFnameThai", SqlDbType.NVarChar).Value = txtFNameThai.Text.Trim();
                    Cmd.Parameters.Add("@sPersonLnameThai", SqlDbType.NVarChar).Value = txtLNameThai.Text.Trim();
                    Cmd.Parameters.Add("@sDeptID", SqlDbType.NVarChar).Value = cbbDeptName.SelectedValue.ToString();
                    Cmd.Parameters.Add("@sSectID", SqlDbType.NVarChar).Value = cbbSectionName.SelectedValue.ToString();
                    Cmd.Parameters.Add("@sWorkshopID", SqlDbType.NVarChar).Value = cbbWorkshop.SelectedValue.ToString();
                    Cmd.Parameters.Add("@sShift", SqlDbType.NVarChar).Value = cbbShift.Text.Trim();
                  
                    
                    //*** Save Photo \\svr120a\PhotoEmp

                    Cmd.Parameters.Add("@sPhoto", SqlDbType.NVarChar).Value = strFolderPath + fileName;
                    try
                    {
                        if (!string.IsNullOrEmpty(filePath) && new DesktopFileService(strConnString).TrySavePhoto(
                            txtEmpNo.Text.Trim(), filePath, (connection, transaction) =>
                            {
                                using (var mutation = DesktopPhotoCommand.ForTransaction(Cmd, connection, transaction, true))
                                    mutation.ExecuteNonQuery();
                            }, createEmployee: true))
                        {
                            MessageBox.Show("SAVE Complete", "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            this.Hide();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceError("SQL photo creation failed ({0}).", ex.GetType().Name);
                        MessageBox.Show("The employee and photo could not be saved. Check SQL file storage settings and try again.", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    try
                    {
                    new DesktopFileService(strConnString).ExecuteLegacyMutation(
                        txtEmpNo.Text.Trim(), OperatorCertificationRecord.FileStorage.FileKind.Photo, null,
                        (connection, transaction) =>
                        {
                            if (!string.IsNullOrEmpty(filePath))
                            {
                                var destination = DesktopPhotoCommand.CreateLegacyPhotoPath(strFolderPath, txtEmpNo.Text.Trim(), Path.GetExtension(filePath));
                                DesktopPhotoCommand.CopyNewLegacyPhoto(filePath, destination);
                                Cmd.Parameters["@sPhoto"].Value = destination;
                            }
                            else Cmd.Parameters["@sPhoto"].Value = DBNull.Value;
                            using (var mutation = DesktopPhotoCommand.ForTransaction(Cmd, connection, transaction, false))
                                mutation.ExecuteNonQuery();
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceError("Employee creation failed ({0}).", ex.GetType().Name);
                        MessageBox.Show("The employee and photo could not be saved. Check storage settings and try again.", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    MessageBox.Show("SAVE Complete", "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

            }

            else
            {

                MessageBox.Show("Already User");
            }
            this.Hide();
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
            try
            {
                var employeeCode = txtEmpNo.Text.Trim();
                var sourcePath = Phodlg.FileName;
                var storage = new DesktopFileService(strConnString);
                if (!storage.TrySavePhoto(employeeCode, sourcePath))
                {
                    if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                        throw new IOException("Select a photo before saving.");
                    storage.ExecuteLegacyMutation(employeeCode,
                        OperatorCertificationRecord.FileStorage.FileKind.Photo, null,
                        (connection, transaction) =>
                        {
                            var legacyPath = DesktopPhotoCommand.CreateLegacyPhotoPath(strFolderPath, employeeCode, Path.GetExtension(sourcePath));
                            DesktopPhotoCommand.CopyNewLegacyPhoto(sourcePath, legacyPath);
                            using (var mutation = DesktopPhotoCommand.ForLegacyPhotoUpdate(
                                employeeCode, legacyPath, connection, transaction))
                            {
                                if (mutation.ExecuteNonQuery() != 1)
                                    throw new InvalidOperationException("The employee could not be updated.");
                            }
                        });
                }
                MessageBox.Show("Photo saved.", "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Photo update failed ({0}).", ex.GetType().Name);
                MessageBox.Show("The photo could not be saved. Select a photo and check storage settings.", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void btnExit_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit Add User ?", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                this.Hide();
            }
        }

        //private void frmAddUser_FormClosing(object sender, FormClosingEventArgs e)
        //{
        //    Application.Exit();
        //}
    }
}
