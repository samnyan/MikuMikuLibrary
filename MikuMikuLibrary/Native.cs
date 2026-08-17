using System.Reflection;
using MikuMikuLibrary.IBLs.Processing.Interfaces;
using MikuMikuLibrary.Objects.Processing.Fbx.Interfaces;
using MikuMikuLibrary.Objects.Processing.Interfaces;
using MikuMikuLibrary.Textures.Processing.Interfaces;

namespace MikuMikuLibrary;

public static class Native
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFile(string filePath);

    private const string DLL_FILE_NAME = "MikuMikuLibrary.Native.dll";

    public static IFbxExporter FbxExporter { get; set; }
    public static ITextureDecoder TextureDecoder { get; }
    public static ITextureEncoder TextureEncoder { get; }
    public static ILightMapImporter LightMapImporter { get; }
    public static IStripifier Stripifier { get; }
    public static IUnifier Unifier { get; }
    public static IOptimizer Optimizer { get; }
    public static ITangentGenerator TangentGenerator { get; }

    static Native()
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string architecture = IntPtr.Size == 8 ? "win-x64" : "win-x86";
        string configuredPath = Environment.GetEnvironmentVariable("MIKUMIKULIBRARY_NATIVE_PATH");

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add(File.Exists(configuredPath)
                ? configuredPath
                : Path.Combine(configuredPath, DLL_FILE_NAME));
        }

        candidates.Add(Path.Combine(baseDirectory, "runtimes", architecture, "native", DLL_FILE_NAME));
        candidates.Add(Path.Combine(baseDirectory, DLL_FILE_NAME));

        string dllFilePath = candidates.FirstOrDefault(File.Exists);
        if (dllFilePath == null)
        {
            string checkedPaths = string.Join(Environment.NewLine, candidates.Select(path => $"  - {path}"));
            throw new FileNotFoundException(
                $"Native MML library could not be found. Build MikuMikuLibrary.Native first, " +
                $"or set MIKUMIKULIBRARY_NATIVE_PATH to its DLL or containing directory. " +
                $"Checked paths:{Environment.NewLine}{checkedPaths}",
                candidates[0]);
        }

        // Unblock DLL when extracted through Windows (thanks Sewer).
        DeleteFile(dllFilePath + ":Zone.Identifier");

        var assembly = Assembly.LoadFile(dllFilePath);

        assembly.GetType("MikuMikuLibrary.NativeContext")
            .GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);

        FbxExporter = (IFbxExporter)Activator.CreateInstance(
            assembly.GetType("MikuMikuLibrary.Objects.Processing.Fbx.FbxExporterCore"));

        TextureDecoder = (ITextureDecoder)Activator.CreateInstance(
            assembly.GetType("MikuMikuLibrary.Textures.Processing.TextureDecoderCore"));

        TextureEncoder = (ITextureEncoder)Activator.CreateInstance(
            assembly.GetType("MikuMikuLibrary.Textures.Processing.TextureEncoderCore"));

        LightMapImporter = (ILightMapImporter)Activator.CreateInstance(
            assembly.GetType("MikuMikuLibrary.IBLs.Processing.LightMapImporterCore"));

        Stripifier = (IStripifier)Activator.CreateInstance(
            assembly.GetType("MikuMikuLibrary.Objects.Processing.StripifierCore"));

        Unifier = (IUnifier)Activator.CreateInstance(
            assembly.GetType("MikuMikuLibrary.Objects.Processing.UnifierCore"));    
        
        Optimizer = (IOptimizer)Activator.CreateInstance(
            assembly.GetType("MikuMikuLibrary.Objects.Processing.OptimizerCore"));  
        
        TangentGenerator = (ITangentGenerator)Activator.CreateInstance(
            assembly.GetType("MikuMikuLibrary.Objects.Processing.TangentGeneratorCore"));
    }
}
