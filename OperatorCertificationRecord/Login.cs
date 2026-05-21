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

namespace OperatorTrainingRecord
{
    public partial class Login : Form
    {
       // public static string UserNumber;
        public static string UserName;
        public static string DeptName;
        public static string Date;
        public static string Time;

        public Login()
        {

            InitializeComponent();

        }
        public void User()
        {
            //UserNumber = null;
            
            
        }

        private System.Drawing.Point frm_pos; //ประกาศตัวแปรเพื่อใช้เก็บค่าตำแหน่งของ form
        private void Login_Load(object sender, EventArgs e)
        {


            //lblDate.Text = DateTime.Now.ToLongDateString();
            Date = lblDate.Text = DateTime.Now.ToString("dd MMMM yyyy");
            lblDate.Text = DateTime.Now.ToLongDateString();
            Time = DateTime.Now.ToLongTimeString();
            lblTime.Text = Time;
            frm_pos.X = this.Location.X;
            frm_pos.Y = this.Location.Y;
            this.ActiveControl = txtUser;
            
            // Apply modern styling
            ApplyModernStyling();
        }

        private void ApplyModernStyling()
        {
            // Modern website-style color palette
            Color primaryBlue = Color.FromArgb(52, 152, 219);      // #3498db - Vibrant blue
            Color darkBg = Color.FromArgb(44, 62, 80);             // #2c3e50 - Dark background
            Color lightBg = Color.FromArgb(236, 240, 241);         // #ecf0f1 - Light background
            Color accentRed = Color.FromArgb(235, 87, 87);         // #eb5757 - Red accent
            Color textPrimary = Color.FromArgb(52, 73, 94);        // #344a5e - Dark text
            Color textSecondary = Color.FromArgb(127, 140, 141);   // #7f8c8d - Gray text
            Color borderColor = Color.FromArgb(189, 195, 199);     // #bdc3c7 - Light border
            Color white = Color.White;

            // Form styling
            this.BackColor = white;
            this.Font = new Font("Segoe UI", 9F);

            // Sidebar styling with darker blue
            if (pnlSidebar != null)
            {
                pnlSidebar.BackColor = Color.FromArgb(41, 128, 185);
            }

            // Content panel styling
            if (pnlContent != null)
            {
                pnlContent.BackColor = white;
            }

            // Enhanced input field styling with focus effects
            ApplyWebInputStyling(txtUser, lightBg, textPrimary, borderColor);
            ApplyWebInputStyling(txtPassword, lightBg, textPrimary, borderColor);

            // Enhanced label styling
            ApplyWebLabelStyling(label2, textPrimary, 11F, false);
            ApplyWebLabelStyling(label3, textPrimary, 11F, false);
            
            if (lblTitle != null)
            {
                lblTitle.ForeColor = Color.FromArgb(41, 128, 185);
                lblTitle.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
                lblTitle.BackColor = white;
                lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            }

            ApplyWebLabelStyling(lblDate, textSecondary, 9F, false);
            ApplyWebLabelStyling(lblTime, textSecondary, 9F, false);

            // Enhanced button styling with modern look
            ApplyWebButtonStyling(btnLogin, Color.FromArgb(41, 128, 185), white);
            ApplyWebButtonStyling(btnCancel, Color.FromArgb(231, 76, 60), white);

            // GroupBox styling
            groupBox1.BackColor = white;
            groupBox1.ForeColor = textPrimary;
            groupBox1.Font = new Font("Segoe UI", 9F);
        }

        private void ApplyWebInputStyling(TextBox textBox, Color backColor, Color foreColor, Color borderColor)
        {
            textBox.BackColor = backColor;
            textBox.ForeColor = foreColor;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.Font = new Font("Segoe UI", 11F);
            textBox.Padding = new Padding(12, 10, 12, 10);
            textBox.Height = 40;
            textBox.Tag = foreColor; // Store original color for focus effects

            // Add focus event handlers for modern interactivity
            textBox.GotFocus += (s, e) => TextBox_GotFocus(s as TextBox, borderColor);
            textBox.LostFocus += (s, e) => TextBox_LostFocus(s as TextBox);
        }

        private void ApplyWebLabelStyling(Label label, Color foreColor, float fontSize, bool isBold)
        {
            label.ForeColor = foreColor;
            label.Font = isBold ? new Font("Segoe UI", fontSize, FontStyle.Bold) : new Font("Segoe UI", fontSize);
            label.BackColor = Color.White;
        }

        private void ApplyWebButtonStyling(Button button, Color backColor, Color foreColor)
        {
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.BorderColor = Color.Transparent;
            button.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            button.Padding = new Padding(10);

            // Add hover effects
            button.MouseEnter += (s, e) => Button_MouseEnter(s as Button, backColor);
            button.MouseLeave += (s, e) => Button_MouseLeave(s as Button, backColor);
        }

        private void TextBox_GotFocus(TextBox textBox, Color accentColor)
        {
            if (textBox == null) return;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            // Visual feedback for focus - change to white background and add blue border
            textBox.BackColor = Color.White;
        }

        private void TextBox_LostFocus(TextBox textBox)
        {
            if (textBox == null) return;
            textBox.BackColor = Color.FromArgb(236, 240, 241);
        }

        private void Button_MouseEnter(Button button, Color originalColor)
        {
            if (button == null) return;
            // Darken button on hover for web-like effect
            int r = Math.Max(0, originalColor.R - 20);
            int g = Math.Max(0, originalColor.G - 20);
            int b = Math.Max(0, originalColor.B - 20);
            button.BackColor = Color.FromArgb(r, g, b);
        }

        private void Button_MouseLeave(Button button, Color originalColor)
        {
            if (button == null) return;
            button.BackColor = originalColor;
        }

        private void Login_Move(object sender, EventArgs e)
        {
            if ((frm_pos.X != 0) && (frm_pos.Y != 0))
            {
                this.Location = frm_pos;
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {           
            lblTime.Text = DateTime.Now.ToLongTimeString();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit application?", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                Application.Exit();
            }

        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
           
            txtUser.Focus();
            DataTable dt = receptData();
         //   string Dept_name = null ;            
            
            if (dt.Rows.Count > 0)
            {
                                
                UserName = dt.Rows[0]["PersonFNameEng"].ToString() + " " + dt.Rows[0]["PersonLNameEng"].ToString();
                DeptName = dt.Rows[0]["DeptName"].ToString();
                //Time = DateTime.Now.ToLongDateString();
                frmWelcome frm = new frmWelcome();
                
                frm.sectid = dt.Rows[0]["SectID"].ToString();
                frm.Show();
                frm._strUser = this.txtUser.Text;
                frm.LoadWelcome();
               // Dept_name = dt.Rows[0]["Dept"].ToString();
               
              //  frm.WindowState = FormWindowState.Maximized;
               // frm.WindowState = FormWindowState.Minimized;
                this.Hide();
            }
            else if (dt.Rows.Count == 0)
            {
                MessageBox.Show("UserID or Password Incorrect");

            }
     
        }
        private DataTable receptData()
        {
            // *********** Connext Database ********
            SqlConnection Conn = new SqlConnection();
            SqlCommand Cmd = new SqlCommand();

            SqlDataAdapter dtAdapter = default(SqlDataAdapter);
            DataTable dt = new DataTable();
            string strConnString = null;
            string strSQL = null;


            //strConnString = "Server=Localhost;UID=sa;PASSWORD=password;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";
            strConnString = "Server=svr120a;UID=TETUSR;PASSWORD=TETPWD;Database=OperatorCertificationRecordDB;Max Pool Size=400;Connect Timeout=600;";

            Conn.ConnectionString = strConnString;
            Conn.Open();

            strSQL = "SELECT * FROM ViewEmpDept WHERE EmpCode = '" + this.txtUser.Text + "' and EmpPassword = '" + this.txtPassword.Text + "'";
           // strSQL = "SELECT * FROM tblEmployee em inner join tblDepartment dm on em.DeptID = dm.DeptID WHERE EmpCode = '" + this.txtUser.Text + "' and EmpPassword = '" + this.txtPassword.Text + "'";
            
            Cmd = new SqlCommand(strSQL, Conn);

            dtAdapter = new SqlDataAdapter(strSQL, Conn);
            dtAdapter.Fill(dt);
            Conn.Close();
            Conn = null;
            return dt;
        }

        private void txtUser_KeyPress(object sender, KeyPressEventArgs e)
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

        private void txtUser_KeyUp(object sender, KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Enter) || (e.KeyCode == Keys.Tab))
            {
                txtPassword.Focus();
            }

        }

        private void txtPassword_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                btnLogin.Focus();
            }
        }
    }
}
