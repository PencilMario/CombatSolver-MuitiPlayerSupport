namespace CombatSolver;

/// <summary>CombatSolver's independent, asynchronously written diagnostic log.</summary>
public sealed class CombatSolverLog
{
    internal CombatDiagnosticJournal Journal { get; }
    internal CombatSolverLog(string directory) => Journal = new(directory);
    public void Info(string message) => Journal.Write("info", message);
    public void Debug(string message) => Journal.Write("debug", message);
    public void Warn(string message) => Journal.Write("warning", message);
    public void Error(string message) => Journal.Write("error", message);
}
