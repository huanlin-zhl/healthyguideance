using System.Text;

namespace HealthyGuidance.Core.Prompts;

public static class PromptLoader
{
    public static string Load(string sharedRoot, string promptFileName)
    {
        var path = Path.Combine(sharedRoot, "prompts", promptFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Prompt file not found: {path}");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    public static string Render(string template, IReadOnlyDictionary<string, string> variables)
    {
        var result = template;
        foreach (var (key, value) in variables)
        {
            var placeholder = "{" + key + "}";
            if (!result.Contains(placeholder))
                throw new InvalidOperationException(
                    $"Variable '{key}' not found in template. Check the placeholder name.");
            result = result.Replace(placeholder, value);
        }

        var unreplacedStart = result.IndexOf('{');
        if (unreplacedStart >= 0)
        {
            var unreplacedEnd = result.IndexOf('}', unreplacedStart);
            if (unreplacedEnd > unreplacedStart)
            {
                var unreplaced = result.Substring(unreplacedStart, unreplacedEnd - unreplacedStart + 1);
                throw new InvalidOperationException(
                    $"Template contains unreplaced placeholder: {unreplaced}");
            }
        }

        return result;
    }
}
