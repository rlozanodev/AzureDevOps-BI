namespace AzureDevOps.Core.Configuration;

public class TransformationOptions
{
    public const string SectionName = "Transformation";

    public bool Enabled { get; set; } = true;
    public string PythonExecutable { get; set; } = "uv";
    public string ScriptPath { get; set; } = "./analytics_engine/transform_analytics.py";
    public int TimeoutSeconds { get; set; } = 300;
}
