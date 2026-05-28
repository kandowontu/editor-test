using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Metadata;

string dllPath = args.Length > 0 ? args[0] : @"C:\Editor Test\native-windows\bin\Release\net8.0-windows\win-x64\FamidashEditor.dll";
string outDir  = args.Length > 1 ? args[1] : @"C:\Editor Test\._decomp\release";

Directory.CreateDirectory(outDir);

var settings = new DecompilerSettings(LanguageVersion.CSharp10_0)
{
    ThrowOnAssemblyResolveErrors = false,
};

var decompiler = new CSharpDecompiler(dllPath, settings);
var typeSystem = decompiler.TypeSystem;

foreach (var type in typeSystem.MainModule.TopLevelTypeDefinitions)
{
    string typeName = type.FullTypeName.TopLevelTypeName.Name;
    string ns = type.Namespace;
    if (ns.StartsWith("System") || ns.StartsWith("Microsoft") || ns.StartsWith("ICSharp"))
        continue;

    try
    {
        string code = decompiler.DecompileTypeAsString(type.FullTypeName);
        string fileName = Path.Combine(outDir, typeName + ".cs");
        File.WriteAllText(fileName, code);
        Console.WriteLine($"OK: {typeName}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"SKIP {typeName}: {ex.Message}");
    }
}

Console.WriteLine("Done.");
