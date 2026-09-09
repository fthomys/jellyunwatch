using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyUnwatch.Web;

public static class FileTransformationRegistrar
{
    public static readonly Guid TransformationId = Guid.Parse("e010f1ec-7cf9-44d6-a239-8d9c916dd1bb");

    private const string PluginInterfaceType = "Jellyfin.Plugin.FileTransformation.PluginInterface";

    public static bool Register(ILogger logger)
    {
        var pluginInterface = GetPluginInterface();
        if (pluginInterface is null)
        {
            return false;
        }

        var method = pluginInterface.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static);
        var parameterType = method?.GetParameters().FirstOrDefault()?.ParameterType;
        if (method is null || parameterType is null)
        {
            logger.LogWarning("File Transformation is installed but RegisterTransformation was not found");
            return false;
        }

        var payload = CreatePayload(parameterType);
        if (payload is null)
        {
            logger.LogWarning("The transformation payload could not be created for {Type}", parameterType.FullName);
            return false;
        }

        method.Invoke(null, new[] { payload });
        logger.LogInformation("Registered the index.html transformation with File Transformation");
        return true;
    }

    public static void Unregister()
    {
        try
        {
            var pluginInterface = GetPluginInterface();
            var method = pluginInterface?.GetMethod("RemoveTransformation", BindingFlags.Public | BindingFlags.Static);
            method?.Invoke(null, new object[] { TransformationId });
        }
        catch (Exception)
        {
            return;
        }
    }

    private static object? CreatePayload(Type parameterType)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["id"] = TransformationId.ToString(),
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = typeof(IndexHtmlTransformation).Assembly.FullName,
            ["callbackClass"] = typeof(IndexHtmlTransformation).FullName,
            ["callbackMethod"] = nameof(IndexHtmlTransformation.Transform)
        });

        var parse = parameterType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) });
        return parse?.Invoke(null, new object[] { json });
    }

    private static Type? GetPluginInterface()
    {
        var assembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(candidate => candidate.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);

        return assembly?.GetType(PluginInterfaceType);
    }
}
