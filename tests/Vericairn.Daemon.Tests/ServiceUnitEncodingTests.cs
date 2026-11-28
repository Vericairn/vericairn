// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Text;
using System.Xml.Linq;
using Vericairn.Daemon;

namespace Vericairn.Daemon.Tests;

public sealed class ServiceUnitEncodingTests
{
    [Fact]
    public void WindowsTaskActionsOmitContextWhenThereIsNoPrincipal()
    {
        XElement actions = WindowsActions(@"C:\Estates\example", @"C:\Vericairn home");

        Assert.Null(actions.Parent!.Element(actions.Name.Namespace + "Principals"));
        Assert.Null(actions.Attribute("Context"));
    }

    [Fact]
    public void WindowsTaskKeepsTheServiceHomeOptionAfterAnEstatePathWithATrailingBackslash()
    {
        const string root = @"C:\Estates\example estate\";
        const string home = @"C:\Vericairn homes\%USERNAME%";
        XElement actions = WindowsActions(root, home);
        XElement arguments = actions.Descendants(actions.Name.Namespace + "Arguments").Single();
        string encoded = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(home));

        Assert.Equal("--estate \"" + root + "\\\" runtime serve --service-home " + encoded, arguments.Value);
    }

    [Fact]
    public void SystemdEscapesSpecifiersAndKeepsLiteralDollarSigns()
    {
        const string root = "/estates/one %n ${USER}";
        const string home = "/homes/one %h ${USER}";
        ServicePlan plan = ServiceInstaller.Plan("linux", root, "01ESTATE", ["/tools/%n/vericairn"], "/user", home, 1000);

        Assert.Contains("ExecStart=:\"/tools/%%n/vericairn\" \"--estate\" \"/estates/one %%n ${USER}\" \"runtime\" \"serve\"\n", plan.UnitContent, StringComparison.Ordinal);
        Assert.Contains("Environment=\"VERICAIRN_HOME=/homes/one %%h ${USER}\"\n", plan.UnitContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlCharacterInThePlanReportsTheHomeWithoutAParameterName()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => ServiceInstaller.Plan(
            "macos", "/estate", "01ESTATE", ["vericairn"], "/user", "/home/invalid\npath", 1000));

        Assert.Equal("The Vericairn home contains a control character the service unit cannot carry.", error.Message);
    }

    private static XElement WindowsActions(string root, string home)
    {
        ServicePlan plan = ServiceInstaller.Plan("windows", root, "01ESTATE", [@"C:\Tools\vericairn.exe"], @"C:\User", home, null);
        XElement task = XDocument.Parse(plan.UnitContent).Root!;
        return task.Element(task.Name.Namespace + "Actions")!;
    }
}
