using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using OperatorTrainingRecord;
using Xunit;

public sealed class DesktopReportRangeDialogTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void NewDialogDefaultsToThisYearThroughToday(int kind)
    {
        RunSta(() =>
        {
            using (var dialog = new DesktopReportRangeDialog("Report", (DesktopReportKind)kind))
            {
                Assert.Equal(new DateTime(DateTime.Today.Year, 1, 1), dialog.StartDate);
                Assert.Equal(DateTime.Today, dialog.EndDate);
            }
        });
    }

    [Fact]
    public void NoSkillAllTimeIsUnboundedAndThisYearRestoresDates()
    {
        RunSta(() =>
        {
            using (var dialog = new DesktopReportRangeDialog("Report", DesktopReportKind.NoSkill))
            {
                var period = Find<ComboBox>(dialog).Single();
                period.SelectedIndex = 1;
                Assert.Null(dialog.StartDate);
                Assert.Null(dialog.EndDate);
                Assert.All(Find<DateTimePicker>(dialog), picker => Assert.False(picker.Enabled));
                period.SelectedIndex = 0;
                Assert.Equal(new DateTime(DateTime.Today.Year, 1, 1), dialog.StartDate);
                Assert.Equal(DateTime.Today, dialog.EndDate);
                Assert.All(Find<DateTimePicker>(dialog), picker => Assert.True(picker.Enabled));
            }
        });
    }

    [Fact]
    public void ObsoleteAllTimeUsesSameBoundedIntervalAsWeb()
    {
        RunSta(() =>
        {
            using (var dialog = new DesktopReportRangeDialog("Report", DesktopReportKind.Obsoleted))
            {
                Find<ComboBox>(dialog).Single().SelectedIndex = 1;
                Assert.Equal(new DateTime(2000, 1, 1), dialog.StartDate);
                Assert.Equal(new DateTime(2099, 12, 31), dialog.EndDate);
                Assert.All(Find<DateTimePicker>(dialog), picker => Assert.True(picker.Enabled));
            }
        });
    }

    [Fact]
    public void EditingASelectedRangeMakesItCustomWithoutResettingIt()
    {
        RunSta(() =>
        {
            using (var dialog = new DesktopReportRangeDialog("Report", DesktopReportKind.Obsoleted))
            {
                var period = Find<ComboBox>(dialog).Single();
                period.SelectedIndex = 1;
                var start = Find<DateTimePicker>(dialog).Single(picker => picker.AccessibleName == "Start date");
                start.Value = new DateTime(2025, 4, 17);
                Assert.Equal(2, period.SelectedIndex);
                Assert.Equal(new DateTime(2025, 4, 17), dialog.StartDate);
                Assert.Equal(new DateTime(2099, 12, 31), dialog.EndDate);
            }
        });
    }

    private static System.Collections.Generic.IEnumerable<T> Find<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Find<T>(child))
                yield return descendant;
        }
    }

    private static void RunSta(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The date selector did not finish responding.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
