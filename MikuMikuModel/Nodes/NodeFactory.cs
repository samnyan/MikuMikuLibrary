using System.Reflection;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.MasterTables;
using MikuMikuModel.Configurations;
using MikuMikuModel.Modules;
using MikuMikuModel.Nodes.IO;

namespace MikuMikuModel.Nodes;

public static class NodeFactory
{
    private static readonly Dictionary<Type, Type> sNodeTypes;

    public static IReadOnlyDictionary<Type, Type> NodeTypes => sNodeTypes;

    public static INode Create(Type type, string name, object data)
    {
        if (!NodeTypes.TryGetValue(type, out var nodeType))
            return null;

        object[] args = { name, data };
        return Activator.CreateInstance(nodeType,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null) as INode;
    }

    public static INode Create<T>(string name, T data)
    {
        if (!NodeTypes.TryGetValue(typeof(T), out var nodeType))
            return null;

        object[] args = { name, data };
        return Activator.CreateInstance(nodeType,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null) as INode;
    }

    public static INode Create(string filePath, IEnumerable<Type> typesToMatch)
    {
        var module = ModuleImportUtilities.GetModule(filePath);
        if (module == null || !NodeTypes.ContainsKey(module.ModelType))
        {
            using var stream = File.OpenRead(filePath);
            if (!FgoMasterTable.IsTextTable(stream))
                throw new InvalidDataException("File type could not be determined.");

            ConfigurationList.Instance.DetermineCurrentConfiguration(filePath);
            return Create(typeof(FgoMasterTable), Path.GetFileName(filePath),
                BinaryFile.Load<FgoMasterTable>(filePath));
        }

        ConfigurationList.Instance.DetermineCurrentConfiguration(filePath);
        return Create(module.ModelType, Path.GetFileName(filePath), module.Import(filePath));
    }

    public static INode Create(string filePath)
    {
        return Create(filePath, FormatModuleRegistry.ModelTypes);
    }

    /// <summary>
    /// Creates a node for a physical file without opening its contents.
    /// Binary nodes are loaded through their existing stream getter seam;
    /// formats without a lazy constructor are represented as a lazy stream.
    /// </summary>
    public static INode CreateFileNode(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        string name = Path.GetFileName(fullPath);
        var module = ModuleImportUtilities.GetModule(fullPath);

        if (module == null && IsFgoMasterTable(fullPath))
            module = FormatModuleRegistry.ModulesByType[typeof(FgoMasterTable)];

        if (module != null && typeof(IBinaryFile).IsAssignableFrom(module.ModelType) &&
            NodeTypes.TryGetValue(module.ModelType, out var nodeType) && HasLazyConstructor(nodeType))
        {
            ConfigurationList.Instance.DetermineCurrentConfiguration(fullPath);
            var node = Create(module.ModelType, name,
                new Func<Stream>(() => File.OpenRead(fullPath)));

            if (node != null)
            {
                node.Tag = fullPath;
                return node;
            }
        }

        var streamNode = new StreamNode(name, () => File.OpenRead(fullPath), fullPath)
        {
            Tag = fullPath
        };
        return streamNode;
    }

    private static bool HasLazyConstructor(Type nodeType) =>
        nodeType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(Func<Stream>) },
            modifiers: null) != null;

    private static bool IsFgoMasterTable(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return FgoMasterTable.IsTextTable(stream);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    static NodeFactory()
    {
        sNodeTypes = new Dictionary<Type, Type>();

        var assembly = Assembly.GetExecutingAssembly();

        var types = assembly.GetTypes().Where(
            x => typeof(INode).IsAssignableFrom(x) && x.IsClass && !x.IsAbstract);

        foreach (var type in types)
            for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
                if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(Node<>))
                {
                    sNodeTypes[baseType.GetGenericArguments()[0]] = type;
                    break;
                }
    }
}
