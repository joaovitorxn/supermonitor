using System.Xml.Linq;
using Supermonitor.Core.Shell;

namespace Supermonitor.Tests.Shell;

public class AutostartManagerTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void Task_xml_runs_elevated_at_logon_without_time_limit()
    {
        var xml = XDocument.Parse(AutostartManager.BuildTaskXml(@"C:\Apps\Supermonitor.exe", @"PC\user"));

        Assert.Equal("HighestAvailable", xml.Descendants(Ns + "RunLevel").Single().Value);
        Assert.Equal("PT0S", xml.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("false", xml.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal(@"PC\user", xml.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")!.Value);
        Assert.Equal(@"C:\Apps\Supermonitor.exe", xml.Descendants(Ns + "Command").Single().Value);
        Assert.Equal("--minimized", xml.Descendants(Ns + "Arguments").Single().Value);
    }

    [Fact]
    public void Task_xml_escapes_special_characters_in_path()
    {
        var xml = XDocument.Parse(AutostartManager.BuildTaskXml(@"C:\A&B <x>\Supermonitor.exe", @"PC\user"));

        Assert.Equal(@"C:\A&B <x>\Supermonitor.exe", xml.Descendants(Ns + "Command").Single().Value);
    }
}
