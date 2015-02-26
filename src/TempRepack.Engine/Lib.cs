using Microsoft.Framework.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TempRepack.Engine
{
    public class Lib
    {
        public string FilePath { get { return Path.Combine(LibraryInformation.Path, "lib", "contract", Name + ".dll"); } }
        public string Name => LibraryInformation.Name;
        public string Version => LibraryInformation.Version;
        public ILibraryInformation LibraryInformation { get; set; }
        public List<Lib> Dependencies { get; set; }

        public bool IsReferenceAssembly
        {
            get
            {
                var contractPath = Path.Combine(LibraryInformation.Path, "lib", "contract");
                return Directory.Exists(contractPath);
            }
        }
        public bool IsNet45Stubbed
        {
            get
            {
                var stubPath = Path.Combine(LibraryInformation.Path, "lib", "net45", "_._");
                return File.Exists(stubPath);
            }
        }

        public void RepackReferencePackage(string basePath)
        {
            var categoryName = IsNet45Stubbed ? "cat2" : "cat1";
            var packPath = Path.Combine(basePath, categoryName, Name, Version);
            var refPath = Path.Combine(packPath, "ref");
            var libPath = Path.Combine(packPath, "lib");
            var dllName = Name + ".dll";

            var frameworkAssemblies = new List<string>();
            if (IsNet45Stubbed)
            {
                SafeCopy(
                    Path.Combine(LibraryInformation.Path, "lib", "contract", dllName),
                    Path.Combine(packPath, "ref", "core10", dllName));

                SafeCopy(
                    Path.Combine(LibraryInformation.Path, "lib", "portable-wpa81+wp80+win80+net45+aspnetcore50", dllName),
                    Path.Combine(packPath, "ref", "portable-wpa81+wp80+win80+net45", dllName));

                SafeCopy(
                    Path.Combine(LibraryInformation.Path, "lib", "net45", "_._"),
                    Path.Combine(packPath, "ref", "net45", "_._"));

                // keeping this implementation in the package?
                // PROBLEM: this implementation package has many more dependencies than are stated. 
                // it will need to have the dependencies added to the nupkg for core10 tfm
                // or it will need to become an implementation package
                SafeCopy(
                    Path.Combine(LibraryInformation.Path, "lib", "aspnetcore50", dllName),
                    Path.Combine(packPath, "lib", "core10", dllName));

                SafeCopy(
                    Path.Combine(LibraryInformation.Path, "lib", "net45", "_._"),
                    Path.Combine(packPath, "lib", "net45", "_._"));

                frameworkAssemblies.Add(Name);
            }
            else
            {
                SafeCopy(
                    Path.Combine(LibraryInformation.Path, "lib", "contract", dllName),
                    Path.Combine(packPath, "ref", "core10", dllName));

                SafeCopy(
                    Path.Combine(LibraryInformation.Path, "lib", "portable-wpa81+wp80+win80+net45+aspnetcore50", dllName),
                    Path.Combine(packPath, "ref", "portable-wpa81+wp80+win80+net45", dllName));

                SafeCopy(
                    Path.Combine(LibraryInformation.Path, "lib", "net45", dllName),
                    Path.Combine(packPath, "ref", "net45", dllName));

                SafeCopy(
                    Path.Combine(LibraryInformation.Path, "lib", "net45", dllName),
                    Path.Combine(packPath, "lib", "net45", dllName));

                var comp = new Comp(Path.Combine(packPath, "lib", "net45", dllName), Version);
                comp.Parse();
                foreach (var assemblyReference in comp.AssemblyReferences)
                {
                    if (!Dependencies.Any(d => d.Name == assemblyReference) && assemblyReference != "mscorlib")
                    {
                        frameworkAssemblies.Add(assemblyReference);
                    }
                }
            }


            var nuspecTemplate = @"<?xml version=""1.0""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd"">
  <metadata>
    <id>{0}</id>
    <version>{1}</version>
    <title>{0}</title>
    <authors>Microsoft</authors>
    <owners>Microsoft</owners>
    <licenseUrl>http://go.microsoft.com/fwlink/?LinkId=329770</licenseUrl>
    <iconUrl>http://go.microsoft.com/fwlink/?LinkID=288859</iconUrl>
    <requireLicenseAcceptance>true</requireLicenseAcceptance>
    <description>{0}</description>
    <copyright>Copyright © Microsoft Corporation</copyright>
    <dependencies>{2}
    </dependencies>{3}
  </metadata>
</package>
";
            var dependencyTemplate = @"
      <dependency id=""{0}"" version=""{1}"" />";

            var frameworkAssembliesTemplate = @"
    <frameworkAssemblies>{0}
    </frameworkAssemblies>";

            var frameworkAssemblyTemplate = @"
      <frameworkAssembly assemblyName=""{0}"" targetFramework=""net45"" />";

            var frameworkAssembliesText = "";
            if (frameworkAssemblies.Any())
            {
                frameworkAssembliesText = frameworkAssemblies.Select(x => string.Format(frameworkAssemblyTemplate, x)).Aggregate("", (a, b) => a + b);

                frameworkAssembliesText = string.Format(frameworkAssembliesTemplate, frameworkAssembliesText);
            }

            var nuspecText = string.Format(
                nuspecTemplate,
                Name,
                Version,
                Dependencies
                    .OrderBy(x => x.Name)
                    .Select(x => string.Format(dependencyTemplate, x.Name, x.LibraryInformation.Version))
                    .Aggregate("", (a, b) => a + b),
                frameworkAssembliesText);

            File.WriteAllText(Path.Combine(packPath, Name + ".nuspec"), nuspecText);
        }

        public void SafeCopy(string sourcePath, string targetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }
}
