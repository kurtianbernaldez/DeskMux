using System.Reflection;

namespace DeskMux.App;

internal static class AppInfo
{
    internal const string WebsiteUrl = "https://deskmux.kurtian.dev";
    internal const string RepositoryUrl = "https://github.com/kurtianbernaldez/DeskMux";
    internal const string ReleasesUrl = RepositoryUrl + "/releases";
    internal const string ReleasesApiUrl = "https://api.github.com/repos/kurtianbernaldez/DeskMux/releases?per_page=20";

    internal static string Version =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.1.0-alpha.1";
}
