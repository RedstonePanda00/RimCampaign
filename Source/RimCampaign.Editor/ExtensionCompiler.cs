using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RimCampaign.Core;
using Verse;

namespace RimCampaign.Editor
{
    public static class ExtensionCompiler
    {
        public static string Compile(Project project, string source)
        {
            if (!File.Exists(source) || !string.Equals(Path.GetExtension(source), ".cs", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a C# source file.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(project.PackageId, @"^[a-zA-Z0-9_-]+(\.[a-zA-Z0-9_-]+)+$"))
                throw new InvalidOperationException("Invalid package ID.");
            var directory = Path.Combine(Workspace.WorkRoot, "Compiled", project.Id);
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, project.PackageId + ".Custom.dll");
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".dll";
            try
            {
                var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location);
                var paths = Directory.GetFiles(runtime, "System*.dll")
                    .Where(p => !p.EndsWith(".Native.dll", StringComparison.OrdinalIgnoreCase))
                    .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).Select(a => a.Location))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                var references = paths.Select(p => MetadataReference.CreateFromFile(p));
                var compilation = CSharpCompilation.Create(project.PackageId + ".Custom",
                    new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(source), path: source) }, references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
                using (var output = File.Create(temporary))
                {
                    var result = compilation.Emit(output);
                    if (!result.Success)
                        throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
                }
                // Read PE metadata without executing extension code.
                using (var stream = File.OpenRead(temporary))
                using (var pe = new PEReader(stream))
                {
                    var metadata = pe.GetMetadataReader();
                    foreach (var handle in metadata.AssemblyReferences)
                    {
                        string name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
                        var owner = LoadedModManager.RunningMods.FirstOrDefault(m => m.assemblies.loadedAssemblies.Any(a => a.GetName().Name == name));
                        if (owner != null && !project.ExtraReferences.Any(r => r.Package == owner.PackageIdPlayerFacing && r.Name == name))
                            project.ExtraReferences.Add(new DefRef { Kind = "Assembly", Name = name, Package = owner.PackageIdPlayerFacing });
                    }
                }
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
                return target;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
