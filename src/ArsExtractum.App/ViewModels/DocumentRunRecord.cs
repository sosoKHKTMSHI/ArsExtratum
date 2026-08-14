using ArsExtractum.Core.Pipeline;

namespace ArsExtractum.App.ViewModels;

public sealed record DocumentRunRecord(
    string FileName,
    DocumentExecution? Execution,
    string? ErrorMessage);
