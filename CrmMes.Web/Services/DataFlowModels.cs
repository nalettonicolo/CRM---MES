namespace CrmMes.Web.Services;

// Strumento per i flussi di dati dal pannello Admin (mirror dei record in CrmMes.Api.Controllers.DataFlowsController).

/// <summary>Tipi di passo conosciuti dal client — stessi valori di CrmMes.Core.Flows.DataFlowStepType.</summary>
public static class DataFlowStepTypes
{
    public const string NotifyRole = "NotifyRole";
    public const string RequireApproval = "RequireApproval";
}

public sealed record DataFlowEventField(string Key, string Label);

public sealed record DataFlowEvent(string Key, string Name, List<DataFlowEventField> Fields);

public sealed record DataFlowStep(Guid Id, int Order, string Type, string TargetRole, string? MessageTemplate);

public sealed record DataFlow(Guid Id, string Name, string TriggerEventKey, bool Enabled, List<DataFlowStep> Steps);

public sealed record DataFlowRun(
    Guid Id, string FlowName, string EventKey, string Status, int CurrentStepOrder,
    string? WaitingOnRole, string? ResolvedBy, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SaveDataFlowStep(string Type, string TargetRole, string? MessageTemplate);

public sealed record SaveDataFlow(string? Name, string? TriggerEventKey, bool Enabled, List<SaveDataFlowStep>? Steps);

public sealed record Notification(Guid Id, string Message, Guid? DataFlowRunId, DateTime CreatedAt, DateTime? ReadAt);
