using System.Text.Json;
using System.Text.Json.Nodes;

namespace HealthyGuidance.Core.Schemas;

public static class SchemaLoader
{
    public static string LoadInlined(string sharedRoot, string schemaFileName)
    {
        var schemasDir = Path.Combine(sharedRoot, "schemas");
        var rootPath = Path.Combine(schemasDir, schemaFileName);
        if (!File.Exists(rootPath))
            throw new FileNotFoundException($"Schema file not found: {rootPath}");

        var rootNode = JsonNode.Parse(File.ReadAllText(rootPath))
            ?? throw new InvalidOperationException($"Failed to parse schema: {rootPath}");

        InlineRefs(rootNode, schemasDir);
        RemoveTopLevelMetadata(rootNode);

        return rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static void InlineRefs(JsonNode? node, string schemasDir)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out var refNode) && refNode is JsonValue refValue)
            {
                var refPath = refValue.GetValue<string>();
                if (!refPath.StartsWith("#") && !refPath.Contains("://"))
                {
                    var targetPath = Path.Combine(schemasDir, refPath);
                    if (!File.Exists(targetPath))
                        throw new FileNotFoundException($"Referenced schema not found: {targetPath}");

                    var referenced = JsonNode.Parse(File.ReadAllText(targetPath))
                        ?? throw new InvalidOperationException($"Failed to parse: {targetPath}");

                    InlineRefs(referenced, schemasDir);

                    if (referenced is JsonObject referencedObj)
                    {
                        obj.Remove("$ref");
                        referencedObj.Remove("$schema");
                        referencedObj.Remove("$id");
                        foreach (var kv in referencedObj.ToList())
                        {
                            referencedObj.Remove(kv.Key);
                            if (!obj.ContainsKey(kv.Key))
                                obj[kv.Key] = kv.Value;
                        }
                    }
                    return;
                }
            }

            foreach (var kv in obj.ToList())
                InlineRefs(kv.Value, schemasDir);
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
                InlineRefs(item, schemasDir);
        }
    }

    private static void RemoveTopLevelMetadata(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("$schema");
            obj.Remove("$id");
        }
    }
}
