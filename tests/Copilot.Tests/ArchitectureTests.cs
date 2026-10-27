using Xunit;
namespace Copilot.Tests;
public class ArchitectureTests
{
    [Fact] public void CoreTargetsNet8() => Assert.Contains(".NETCoreApp,Version=v8.0", typeof(Copilot.Core.Marker).Assembly.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false).Cast<System.Runtime.Versioning.TargetFrameworkAttribute>().Single().FrameworkName);
}
