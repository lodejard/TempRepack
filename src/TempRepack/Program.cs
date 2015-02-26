using Microsoft.Framework.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using TempRepack.Engine;
using TempRepack.Engine.Model;

namespace TempRepack
{
    public class Program
    {
        ILibraryManager _manager;
        public Program(ILibraryManager manager)
        {
            _manager = manager;
        }

        public void Main(string[] args)
        {
            var runtimeFileFormatter = new RuntimeFileFormatter();
            var runtimeFile = runtimeFileFormatter.ReadRuntimeFile("runtime.json");
            foreach (var runtime in runtimeFile.Runtimes.Values)
            {
                Console.WriteLine("{0} Imports: {1}", runtime.Name, string.Join(", ", runtime.Import));
            }
            foreach (var runtime in runtimeFile.Runtimes.Values)
            {
                var sources = new List<string>();
                AddSources(runtimeFile, runtime, sources);

                Console.WriteLine("{0} Effective: {1}", runtime.Name, string.Join(", ", sources));
            }

            var libs = _manager.GetLibraries().ToDictionary(
                x => x.Name,
                x => new Lib { LibraryInformation = x });

            var comps86 = new Dictionary<string, Comp>();
            var comps64 = new Dictionary<string, Comp>();

            foreach (var lib in libs.Values)
            {
                lib.Dependencies = lib.LibraryInformation.Dependencies
                    .Select(x => libs[x])
                    .ToList();
            }

            //if (Directory.Exists("Data\\Build"))
            //{
            //    Directory.Delete("Data\\Build", true);
            //}

            var coreClr = libs["CoreCLR"];

            var clr86Path = Path.Combine(coreClr.LibraryInformation.Path, "Runtime", "x86");
            var clr64Path = Path.Combine(coreClr.LibraryInformation.Path, "Runtime", "amd64");

            var runtimes = new RuntimeCollection();

            foreach (var fn in Directory.GetFiles(clr86Path, "*.dll"))
            {
                var comp = new Comp(fn, coreClr.Version);
                comp.RuntimeName = "win7-x86";
                comp.Parse();
                runtimes.Add(comp);
            }

            foreach (var fn in Directory.GetFiles(clr64Path, "*.dll"))
            {
                var comp = new Comp(fn, coreClr.Version);
                comp.RuntimeName = "win7-amd64";
                comp.Parse();
                runtimes.Add(comp);
            }

            foreach (var entry in runtimes.Entries.Values)
            {
                foreach (var comp in entry.Comps.Values)
                {
                    comp.Lib = libs.Get(comp.Name);
                    comp.Dependencies = comp.AssemblyReferences
                        .Select(x => entry.Comps[x])
                        .Concat(comp.DllImports
                            .Where(x => entry.Comps.ContainsKey(x))
                            .Select(x => entry.Comps[x]))
                        .ToList();
                }
            }

            Directory.CreateDirectory(Path.Combine("Data", "Lib"));
            foreach (var lib in libs.Values.Where(x => x.IsReferenceAssembly))
            {
                File.Copy(
                    lib.FilePath,
                    Path.Combine("Data", "Lib", Path.GetFileName(lib.FilePath)),
                    true);
            }
            foreach (var entry in runtimes.Entries.Values)
            {
                Directory.CreateDirectory(Path.Combine("Data", "Comp", entry.Runtime.Name));
                foreach (var comp in entry.Comps.Values)
                {
                    File.Copy(
                        comp.FilePath,
                        Path.Combine("Data", "Comp", entry.Runtime.Name, Path.GetFileName(comp.FilePath)),
                        true);
                }
            }

            foreach (var lib in libs.Values
                .Where(x => x.IsReferenceAssembly)
                .OrderBy(x => x.Name))
            {
                lib.RepackReferencePackage(Path.Combine("Data", "Build"));
            }
            foreach (var entry in runtimes.Entries.Values)
            {
                foreach (var comp in entry.Comps.Values)
                {
                    comp.RepackImplementationPackage(Path.Combine("Data", "Build", entry.Runtime.Name));

                    var runtimeSpec = runtimeFile.Runtimes.Get(entry.Runtime.Name);
                    if (comp.Lib != null)
                    {
                        var dep = runtimeSpec.Dependencies.GetOrAdd(
                            comp.Lib.Name,
                            () => new DependencySpec { Name = comp.Lib.Name });
                        dep.Implementations.GetOrAdd(
                            comp.PackageId,
                            () => new ImplementationSpec { Name = comp.PackageId, Version = comp.Version });
                    }
                    else
                    {
                        var dep = runtimeSpec.Dependencies.GetOrAdd(
                            comp.DependencyId,
                            () => new DependencySpec { Name = comp.DependencyId });
                        dep.Implementations.GetOrAdd(
                            comp.PackageId,
                            () => new ImplementationSpec { Name = comp.PackageId, Version = comp.Version });
                        if (comp.DependencyId.StartsWith("runtime.api.ms.win"))
                        {
                            runtimeFile
                                .Runtimes.Get("win8")
                                .Dependencies.GetOrAdd(
                                    comp.DependencyId, 
                                    () => new DependencySpec { Name = comp.DependencyId });
                        }
                    }
                }
            }

            runtimeFileFormatter.WriteRuntimeFile(
                Path.Combine("Data", "runtime.json"),
                runtimeFile);


            return;
            foreach (var lib in libs.Values
                .Where(x => x.IsReferenceAssembly)
                .OrderBy(x => x.Name))
            {
                var listed = new List<Lib>();
                Console.WriteLine("${0}", lib.Name);
                Console.WriteLine("  (contracts)");
                Dump(2, lib, libs, listed, _ => false);

                Comp comp = null;
                if (!comps86.TryGetValue(lib.Name, out comp))
                {
                    var path = Path.Combine(lib.LibraryInformation.Path, "lib", "aspnetcore50", lib.Name + ".dll");
                    if (File.Exists(path))
                    {
                        comp = new Comp(path, coreClr.Version);
                        comp.Parse();
                        comps86[comp.Name] = comp;
                        comp.Dependencies = comp.AssemblyReferences
                            .Select(x => comps86[x])
                            .ToList();
                    }
                }
                var listed2 = new List<Comp>();
                Console.WriteLine("  (implementations)");
                if (comp == null)
                {
                    Console.WriteLine("    *** MISSING ***");
                }
                else
                {
                    Dump(2, comp, libs, listed, listed2);
                }
            }

            foreach (var comp in comps86.Values)
            {
                if (libs.ContainsKey(comp.Name))
                {
                    Console.WriteLine("^{0}", comp.Name);
                }
                else
                {
                    Console.WriteLine("^~{0}", comp.Name);
                }

                foreach (var x in comp.TypeForwarding)
                {
                    Console.WriteLine("    {0} <= {1}", x.Key, string.Join(" ", x.Value));
                }
                Console.WriteLine("    ++ {0}", string.Join(" ", comp.TypeDefinitions));
                foreach (var x in comp.TypeReferencing)
                {
                    Console.WriteLine("    {0} => {1}", x.Key, string.Join(" ", x.Value));
                }
            }
        }

        private void AddSources(RuntimeFile runtimeFile, RuntimeSpec runtime, List<string> sources)
        {
            sources.RemoveAll(name => name == runtime.Name);
            sources.Add(runtime.Name);
            foreach (var import in runtime.Import)
            {
                AddSources(runtimeFile, runtimeFile.Runtimes[import], sources);
            }
        }

        private void Dump(
            int depth,
            Lib lib,
            IDictionary<string, Lib> libs,
            IList<Lib> listed,
            Func<string, bool> eclipsed)
        {
            if (!listed.Contains(lib))
            {
                listed.Add(lib);
            }
            else
            {
                return;
            }

            Console.WriteLine("{0}{1}",
                new String(' ', depth * 2),
                lib.Name);

            foreach (var nested in lib.Dependencies)
            {
                if (eclipsed(nested.Name))
                {
                    continue;
                }
                Dump(
                    depth + 1,
                    nested,
                    libs,
                    listed,
                    name => lib.Dependencies.Any(x => x.Name == name) || eclipsed(name));
            }
        }

        private void Dump(int depth, Comp comp, IDictionary<string, Lib> libs, List<Lib> listed, List<Comp> listed2)
        {
            if (!listed2.Contains(comp))
            {
                listed2.Add(comp);
            }
            else
            {
                return;
            }

            var extra = !listed.Any(x => x.Name == comp.Name);
            var contract = libs.Any(x => x.Key == comp.Name && x.Value.IsReferenceAssembly);

            Console.WriteLine("{0}{2}{1}{3}",
                new String(' ', depth * 2),
                comp.Name,
                extra ? (contract ? "! " : "!~") : "  ",
                comp.DllImports.Aggregate("", (a, b) => a + " " + b));

            foreach (var nested in comp.Dependencies)
            {
                Dump(depth + 1, nested, libs, listed, listed2);
            }
        }
    }
}
