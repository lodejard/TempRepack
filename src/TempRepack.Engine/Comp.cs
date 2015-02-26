using Microsoft.Framework.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace TempRepack.Engine
{
    public class Comp
    {
        public Comp(string path, string version)
        {
            FilePath = path;
            Name = Path.GetFileNameWithoutExtension(path);
            Version = version;
        }

        public string FilePath { get; set; }

        public string Name { get; set; }

        public string Version { get; set; }

        public string RuntimeName { get; set; }

        public string DependencyId => ("runtime." + Name).Replace("-", ".");

        public string PackageId => ("runtime." + RuntimeName + "." + Name).Replace("-", ".");

        public List<string> AssemblyReferences { get; set; } = new List<string>();
        public List<string> DllImports { get; set; } = new List<string>();

        public List<Comp> Dependencies { get; set; }

        public Lib Lib { get; set; }
        public ILibraryInformation LibraryInformation => Lib?.LibraryInformation;

        public List<string> TypeDefinitions { get; set; } = new List<string>();

        public Dictionary<string, List<string>> TypeForwarding = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> TypeReferencing = new Dictionary<string, List<string>>();

        public void Parse()
        {
            using (var stream = new FileStream(FilePath, FileMode.Open))
            {
                var per = new PEReader(stream);
                if (!per.HasMetadata)
                {
                    Name = Name.ToLowerInvariant() + ".dll";
                    return;
                    //per.PEHeaders.PEHeader.ImportTableDirectory.
                }
                var mdr = per.GetMetadataReader();

                //mdr.GetModuleReference
                foreach (var af in mdr.AssemblyFiles.Select(mdr.GetAssemblyFile))
                {
                }
                foreach (var ar in mdr.AssemblyReferences.Select(mdr.GetAssemblyReference))
                {
                    AssemblyReferences.Add(mdr.GetString(ar.Name));
                }
                foreach (var xt in mdr.ExportedTypes.Select(mdr.GetExportedType))
                {
                    if (xt.Implementation.Kind == HandleKind.AssemblyReference)
                    {
                        var ar = mdr.GetAssemblyReference((AssemblyReferenceHandle)xt.Implementation);
                        var arn = mdr.GetString(ar.Name);
                        List<string> types;
                        if (!TypeForwarding.TryGetValue(arn, out types))
                        {
                            TypeForwarding[arn] = types = new List<string>();
                        }
                        types.Add(mdr.GetString(xt.Name));
                    }
                }
                foreach (var tr in mdr.TypeReferences.Select(mdr.GetTypeReference))
                {
                    if (tr.ResolutionScope.Kind == HandleKind.AssemblyReference)
                    {
                        var ar = mdr.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope);
                        var arn = mdr.GetString(ar.Name);
                        var trn = mdr.GetString(tr.Name);
                        if (trn.StartsWith("Assembly") && trn.EndsWith("Attribute"))
                        {
                            continue;
                        }
                        List<string> types;
                        if (!TypeReferencing.TryGetValue(arn, out types))
                        {
                            TypeReferencing[arn] = types = new List<string>();
                        }
                        types.Add(trn);
                    }
                    else
                    {
                        //Console.WriteLine("{0}", tr.ResolutionScope.Kind);
                    }
                }
                foreach (var td in mdr.TypeDefinitions.Select(mdr.GetTypeDefinition))
                {
                    var tdn = mdr.GetString(td.Name);
                    if ((td.Attributes & TypeAttributes.Public) == TypeAttributes.Public &&
                        !tdn.StartsWith("<"))
                    {
                        TypeDefinitions.Add(tdn);
                    }
                    foreach (var md in td.GetMethods().Select(mdr.GetMethodDefinition))
                    {
                        var imp = md.GetImport();
                        if (!imp.Module.IsNil)
                        {
                            var mr = mdr.GetModuleReference(imp.Module);
                            var mrn = mdr.GetString(mr.Name);
                            if (!mrn.EndsWith(".dll"))
                            {
                                mrn = mrn + ".dll";
                            }
                            if (!DllImports.Contains(mrn))
                            {
                                DllImports.Add(mrn);
                            }
                        }
                    }
                }
            }
        }

        public void RepackImplementationPackage(string basePath)
        {
            var runtimeName = RuntimeName;
            var packPath = Path.Combine(basePath, PackageId, Version);
            var dllName = Name + ".dll";

            var frameworkAssemblies = new List<string>();
            if (Lib != null)
            {
                SafeCopy(
                    Path.Combine(LibraryInformation.Path, "lib", "contract", Lib.Name + ".dll"),
                    Path.Combine(packPath, "ref", "core10", Lib.Name + ".dll"));
            }

            SafeCopy(
                FilePath,
                Path.Combine(packPath, "runtime", runtimeName, Path.GetFileName(FilePath)));

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

            Func<Comp, string> formatDependency = comp =>
            {
                if (comp.LibraryInformation != null)
                {
                    return string.Format(dependencyTemplate, comp.LibraryInformation.Name, comp.LibraryInformation.Version);
                }
                return string.Format(dependencyTemplate, comp.DependencyId, "");
            };

            var nuspecText = string.Format(
                nuspecTemplate,
                PackageId,
                Version,
                (Dependencies.OrderBy(x => x.Name) ?? Enumerable.Empty<Comp>())
                    .Select(formatDependency)
                    .Aggregate("", (a, b) => a + b),
                frameworkAssembliesText);

            File.WriteAllText(Path.Combine(packPath, PackageId + ".nuspec"), nuspecText);
        }

        public void SafeCopy(string sourcePath, string targetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }
}