using System.Collections.ObjectModel;

namespace CmsSync.Domain.Processing;

public sealed class ProcessingDecision
{
    private readonly ReadOnlyCollection<CmsEntityStateOperation> _operations;

    private ProcessingDecision(
        ProcessingOutcome outcome,
        ProcessingCode code,
        CmsEntityStateOperation[] operations)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(operations);

        if (outcome == ProcessingOutcome.Applied && operations.Length == 0)
        {
            throw new ArgumentException("An applied decision must describe at least one state operation.", nameof(operations));
        }

        if (outcome != ProcessingOutcome.Applied && operations.Length != 0)
        {
            throw new ArgumentException("Only an applied decision may describe state operations.", nameof(operations));
        }

        if (Array.Exists(operations, static operation => operation is null))
        {
            throw new ArgumentException("State operations cannot contain null entries.", nameof(operations));
        }

        Outcome = outcome;
        Code = code;
        _operations = Array.AsReadOnly((CmsEntityStateOperation[])operations.Clone());
    }

    public ProcessingOutcome Outcome { get; }

    public ProcessingCode Code { get; }

    public IReadOnlyList<CmsEntityStateOperation> Operations => _operations;

    public static ProcessingDecision Applied(
        ProcessingCode code,
        params CmsEntityStateOperation[] operations) =>
        new(ProcessingOutcome.Applied, code, operations);

    public static ProcessingDecision WithoutStateChange(ProcessingOutcome outcome, ProcessingCode code)
    {
        if (outcome == ProcessingOutcome.Applied)
        {
            throw new ArgumentException("Use Applied to create a decision with state operations.", nameof(outcome));
        }

        return new ProcessingDecision(outcome, code, []);
    }
}
