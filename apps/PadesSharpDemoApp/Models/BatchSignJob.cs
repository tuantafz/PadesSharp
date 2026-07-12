namespace PadesSharpDemoApp.Models;

public enum JobStatus { Pending, Running, Success, Failed }

public sealed class BatchSignJob
{
    public string     InputPath    { get; set; } = "";
    public string     OutputPath   { get; set; } = "";
    public JobStatus  Status       { get; set; } = JobStatus.Pending;
    public string?    ErrorMessage { get; set; }
}
