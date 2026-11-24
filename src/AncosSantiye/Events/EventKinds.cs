namespace AncosSantiye.Events;

/// <summary>Every event kind the core understands. Unknown kinds are kept in the log and ignored by the scene.</summary>
public static class EventKinds
{
    public const string SessionStart = "session.start";
    public const string SessionEnd = "session.end";
    public const string ToolStart = "tool.start";
    public const string ToolEnd = "tool.end";
    public const string ToolFail = "tool.fail";
    public const string AgentStart = "agent.start";
    public const string AgentStop = "agent.stop";
    public const string TurnStop = "turn.stop";
    public const string TaskCreated = "task.created";
    public const string TaskCompleted = "task.completed";
    public const string Usage = "usage";
    public const string StageChanged = "stage.changed";
    public const string PieceStatus = "piece.status";
    public const string DeliveryArrived = "delivery.arrived";
    public const string ReviewVerdict = "review.verdict";
    public const string GatePending = "gate.pending";
    public const string GateCleared = "gate.cleared";
}

/// <summary>Who an actor is on the site.</summary>
public static class Roles
{
    public const string Chief = "chief";
    public const string Worker = "worker";
    public const string Subcontractor = "subcontractor";
    public const string Inspector = "inspector";
    public const string Architect = "architect";
    public const string Supplier = "supplier";
}

/// <summary>Where an event came from.</summary>
public static class Sources
{
    public const string ClaudeCode = "claude-code";
    public const string OpenCode = "opencode";
    public const string Folder = "folder";
}
