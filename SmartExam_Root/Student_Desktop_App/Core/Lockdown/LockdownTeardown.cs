namespace Student_Desktop_App.Core.Lockdown;

/// <summary>
/// Runs lockdown teardown steps in order, isolating each one: a step that throws never prevents the
/// later steps from running. Teardown also runs from crash and process-exit handlers on arbitrary
/// threads, where a UI-bound step (the clipboard listener's HwndSource) can throw — so the step that
/// matters most outside the exam (restoring the Task Manager policy) must come first and must not depend
/// on the others succeeding. Pure, so the ordering/isolation contract is unit-tested.
/// </summary>
public static class LockdownTeardown
{
    /// <summary>Runs every step; returns the names of the steps that threw (empty when all succeeded).</summary>
    public static IReadOnlyList<string> RunAll(params (string Name, Action Step)[] steps)
    {
        var failed = new List<string>();
        foreach (var (name, step) in steps)
        {
            try
            {
                step();
            }
            catch
            {
                failed.Add(name);
            }
        }
        return failed;
    }
}
