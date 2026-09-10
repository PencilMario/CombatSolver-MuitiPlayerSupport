using CombatSolver;

if (args is ["memory"])
    PolicyCheck.Run("player trace memory accounting", GcMemoryBudgetChecks.Run);
else if (args is ["parallelism"])
    SearchParallelismControllerChecks.Run();
else if (args is ["scopes"])
    GcScopeLifecycleChecks.Run();
else if (args.Length == 0)
    GcPolicyChecks.Run();
else
    throw new ArgumentException("Expected no arguments, 'parallelism', 'scopes' or 'memory'.");
Console.WriteLine($"GC policy checks passed: {PolicyCheck.Completed} scenarios.");
