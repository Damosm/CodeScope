using System.Xml.Linq;
using CodeScope.Domain;

namespace CodeScope.Infrastructure;

internal static class ProjectFileMetadata
{
    public static void AddPackages(ProjectInfo project, XDocument document)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in document.Descendants().Where(element => element.Name.LocalName == "PackageReference"))
        {
            var name = package.Attribute("Include")?.Value ?? package.Attribute("Update")?.Value;
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name.Trim())) continue;
            var version = package.Attribute("Version")?.Value ??
                package.Elements().FirstOrDefault(element => element.Name.LocalName is "Version" or "VersionOverride")?.Value;
            project.Packages.Add(new PackageReferenceInfo
            {
                ProjectInfoId = project.Id,
                Name = name.Trim(),
                Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim()
            });
        }
    }
}
