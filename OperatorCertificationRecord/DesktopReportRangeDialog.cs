using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security;
using System.Windows.Forms;

namespace OperatorTrainingRecord
{
    internal sealed class DesktopReportRangeDialog : Form
    {
        private readonly DesktopReportKind report;
        private readonly ComboBox period = new ComboBox();
        private readonly DateTimePicker from = new DateTimePicker();
        private readonly DateTimePicker to = new DateTimePicker();
        private bool changingPeriod;

        internal DesktopReportRangeDialog(string title, DesktopReportKind report)
        {
            if (report != DesktopReportKind.NoSkill && report != DesktopReportKind.Obsoleted)
                throw new ArgumentException("This report already has its own date selection.", "report");

            this.report = report;
            Text = title;
            ClientSize = new Size(440, 270);
            Font = new Font("Segoe UI", 10F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 2,
                RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var row = 0; row < 4; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, row == 3 ? 40 : 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            period.DropDownStyle = ComboBoxStyle.DropDownList;
            period.Items.AddRange(new object[] { "This year", "All time", "Custom" });
            period.AccessibleName = "Report period";
            period.Dock = DockStyle.Fill;
            period.SelectedIndexChanged += ChangePeriod;
            ConfigureDate(from, "Start date");
            ConfigureDate(to, "End date");
            from.ValueChanged += DateChanged;
            to.ValueChanged += DateChanged;

            AddField(layout, "&Period", period, 0);
            AddField(layout, "&From", from, 1);
            AddField(layout, "&To", to, 2);
            var hint = new Label
            {
                Text = report == DesktopReportKind.NoSkill
                    ? "Dates filter the employee's joining date."
                    : "Dates filter the skill's certified date.",
                AutoSize = true,
                Dock = DockStyle.Fill,
                ForeColor = SystemColors.GrayText
            };
            layout.Controls.Add(hint, 0, 3);
            layout.SetColumnSpan(hint, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            var download = new Button { Text = "&Download", AutoSize = true, Height = 34 };
            download.Click += Confirm;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Height = 34 };
            buttons.Controls.Add(download);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout);
            AcceptButton = download;
            CancelButton = cancel;
            period.SelectedIndex = 0;
        }

        internal DateTime? StartDate
        {
            get { return IsUnbounded ? (DateTime?)null : from.Value.Date; }
        }

        internal DateTime? EndDate
        {
            get { return IsUnbounded ? (DateTime?)null : to.Value.Date; }
        }

        private bool IsUnbounded
        {
            get { return report == DesktopReportKind.NoSkill && period.SelectedIndex == 1; }
        }

        private static void ConfigureDate(DateTimePicker picker, string name)
        {
            picker.Format = DateTimePickerFormat.Custom;
            picker.CustomFormat = "dd MMM yyyy";
            picker.Dock = DockStyle.Fill;
            picker.AccessibleName = name;
        }

        private static void AddField(TableLayoutPanel layout, string text, Control control, int row)
        {
            layout.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private void ChangePeriod(object sender, EventArgs e)
        {
            changingPeriod = true;
            try
            {
                from.Enabled = to.Enabled = !IsUnbounded;
                if (period.SelectedIndex == 0)
                    DesktopReportUi.SetThisYear(from, to);
                else if (period.SelectedIndex == 1 && !IsUnbounded)
                {
                    from.Value = new DateTime(2000, 1, 1);
                    to.Value = new DateTime(2099, 12, 31);
                }
            }
            finally { changingPeriod = false; }
        }

        private void DateChanged(object sender, EventArgs e)
        {
            if (!changingPeriod)
                period.SelectedIndex = 2;
        }

        private void Confirm(object sender, EventArgs e)
        {
            if (!IsUnbounded && from.Value.Date > to.Value.Date)
            {
                MessageBox.Show(this, "The end date must be on or after the start date.",
                    "Report period", MessageBoxButtons.OK, MessageBoxIcon.Information);
                to.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal static class DesktopReportUi
    {
        internal static void SetThisYear(DateTimePicker start, DateTimePicker end)
        {
            var today = DateTime.Today;
            start.Value = new DateTime(today.Year, 1, 1);
            end.Value = today;
        }

        internal static bool TryLoad(IWin32Window owner, DesktopReportKind report, string connectionString,
            DateTime? start, DateTime? end, out DataSet data, string section = null)
        {
            data = null;
            if (start.HasValue && end.HasValue && start.Value.Date > end.Value.Date)
            {
                MessageBox.Show(owner, "The end date must be on or after the start date.",
                    "Report period", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            try
            {
                var table = DesktopReportService.Load(report, connectionString, start, end, section);
                table.TableName = "Qualified";
                data = new DataSet();
                data.Tables.Add(table);
                return true;
            }
            catch (Exception error) when (error is SqlException || error is IOException ||
                error is UnauthorizedAccessException || error is ArgumentException ||
                error is InvalidOperationException || error is SecurityException ||
                error is ConfigurationErrorsException)
            {
                // Never display database credentials, SQL text, or private storage paths.
                Trace.TraceError("Desktop report {0} could not load ({1}).", report, error.GetType().Name);
                var message = error is IOException || error is UnauthorizedAccessException ||
                    error is SecurityException || error is ConfigurationErrorsException
                    ? "Developer report exclusions are unavailable. Check DeveloperRoles:StorePath and access to the shared Developer role store, then try again."
                    : "The report could not be loaded. Check the selected dates and database access, then try again.";
                MessageBox.Show(owner, message, "Download report", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}
