using System;
using System.Collections.Generic;
using System.Data;
using OperatorTrainingRecord;
using Xunit;

public sealed class DesktopEmployeeCommandTests
{
    [Fact] public void Employee_profile_text_is_data_and_cannot_modify_file_authority()
    {
        const string injected = "';UPDATE dbo.tblEmployee SET PhotoFileId=NULL;--";
        using (var command = DesktopEmployeeCommand.ForProfileUpdate("001234", new DateTime(2026, 10, 27),
            new Dictionary<string, string> { ["Notice"] = injected, ["PersonFnameEng"] = "O'Brien" }))
        {
            Assert.DoesNotContain(injected, command.CommandText);
            Assert.DoesNotContain("Photo", command.CommandText);
            Assert.Equal("001234", command.Parameters["@employee"].Value);
            Assert.Equal(SqlDbType.NVarChar, command.Parameters["@employee"].SqlDbType);
            Assert.Equal(10, command.Parameters["@employee"].Size);
            Assert.Contains("WHERE EmpCode=@employee", command.CommandText);
            Assert.Equal(injected, command.Parameters["@value0"].Value);
        }
    }

    [Fact] public void Profile_command_rejects_file_metadata_columns()
    {
        Assert.Throws<ArgumentException>(() => DesktopEmployeeCommand.ForProfileUpdate("001234", DateTime.Today,
            new Dictionary<string, string> { ["PhotoFileId"] = "must not be accepted" }));
    }
}
