using Microsoft.Extensions.Logging;
using Notey.Pipelines.Context;
using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Progress;
using Notey.Pipelines.Registry;
using Notey.Pipelines.Steps;
using Notey.Pipelines.Validation;

namespace Notey.Pipelines.Execution;

public sealed class PipelineExecutor(
    IPipelineStepRegistry registry,
    PipelineValidator validator,
    TimeProvider timeProvider,
    ILogger<PipelineExecutor> logger)
{
    public async ValueTask<PipelineExecutionResult> ExecuteAsync(
        PipelineDefinition pipeline,
        PipelineData input,
        PipelineContext? context = null,
        IProgress<PipelineProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(input);

        var validation = validator.Validate(pipeline);
        if (!validation.IsValid)
        {
            throw new PipelineValidationException(
                $"Pipeline '{pipeline.Id}' is invalid: {string.Join("; ", validation.Errors)}",
                validation.Errors);
        }

        context ??= new PipelineContext(pipeline.Id, timeProvider.GetUtcNow());

        if (!pipeline.Enabled)
        {
            logger.LogWarning("Pipeline '{PipelineId}' is disabled and cannot be executed.", pipeline.Id);
            throw new PipelineExecutionException($"Pipeline '{pipeline.Id}' is disabled.", context);
        }

        if (!pipeline.AcceptedInputTypes.Contains(input.Type))
        {
            throw new PipelineExecutionException(
                $"Pipeline '{pipeline.Id}' does not accept input type {input.Type}.",
                context);
        }

        progress?.Report(new PipelineProgressUpdate(
            pipeline.Id,
            PipelineProgressStatus.Started,
            0,
            pipeline.Steps.Count));

        logger.LogInformation(
            "Pipeline '{PipelineId}' started with {StepCount} step(s), input type {InputType}.",
            pipeline.Id, pipeline.Steps.Count, input.Type);

        PipelineData current = input;

        for (var index = 0; index < pipeline.Steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stepDefinition = pipeline.Steps[index];
            var step = GetStep(pipeline, stepDefinition, context);

            if (!step.AcceptedInputTypes.Contains(current.Type))
            {
                throw new PipelineExecutionException(
                    $"Pipeline '{pipeline.Id}' step '{stepDefinition.Id}' cannot consume runtime input type {current.Type}.",
                    context);
            }

            progress?.Report(new PipelineProgressUpdate(
                pipeline.Id,
                PipelineProgressStatus.StepStarted,
                index,
                pipeline.Steps.Count,
                stepDefinition.Id));

            try
            {
                logger.LogDebug("Pipeline '{PipelineId}' executing step '{StepId}' ({StepIndex}/{StepCount}).", pipeline.Id, stepDefinition.Id, index + 1, pipeline.Steps.Count);
                var stepContext = new PipelineStepExecutionContext(pipeline, stepDefinition, context, progress);
                var result = await step.ExecuteAsync(current, stepContext, cancellationToken);

                if (result.Output.Type != step.OutputType)
                {
                    throw new PipelineExecutionException(
                        $"Pipeline '{pipeline.Id}' step '{stepDefinition.Id}' declared {step.OutputType} but returned {result.Output.Type}.",
                        context);
                }

                current = result.Output;
                logger.LogDebug("Pipeline '{PipelineId}' step '{StepId}' completed.", pipeline.Id, stepDefinition.Id);

                progress?.Report(new PipelineProgressUpdate(
                    pipeline.Id,
                    PipelineProgressStatus.StepCompleted,
                    index + 1,
                    pipeline.Steps.Count,
                    stepDefinition.Id,
                    result.Message));
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Pipeline '{PipelineId}' step '{StepId}' was cancelled.", pipeline.Id, stepDefinition.Id);
                progress?.Report(new PipelineProgressUpdate(
                    pipeline.Id,
                    PipelineProgressStatus.Cancelled,
                    index,
                    pipeline.Steps.Count,
                    stepDefinition.Id));
                throw;
            }
            catch (PipelineExecutionException exception)
            {
                logger.LogError(exception, "Pipeline '{PipelineId}' step '{StepId}' failed with a pipeline error.", pipeline.Id, stepDefinition.Id);
                progress?.Report(new PipelineProgressUpdate(
                    pipeline.Id,
                    PipelineProgressStatus.Failed,
                    index,
                    pipeline.Steps.Count,
                    stepDefinition.Id,
                    exception.Message));
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Pipeline '{PipelineId}' step '{StepId}' threw an unexpected exception.", pipeline.Id, stepDefinition.Id);
                progress?.Report(new PipelineProgressUpdate(
                    pipeline.Id,
                    PipelineProgressStatus.Failed,
                    index,
                    pipeline.Steps.Count,
                    stepDefinition.Id,
                    exception.Message));
                throw new PipelineExecutionException(
                    $"Pipeline '{pipeline.Id}' step '{stepDefinition.Id}' failed.",
                    context,
                    exception);
            }
        }

        if (current.Type != pipeline.FinalOutputType)
        {
            throw new PipelineExecutionException(
                $"Pipeline '{pipeline.Id}' produced {current.Type}, but expected {pipeline.FinalOutputType}.",
                context);
        }

        progress?.Report(new PipelineProgressUpdate(
            pipeline.Id,
            PipelineProgressStatus.Completed,
            pipeline.Steps.Count,
            pipeline.Steps.Count));

        logger.LogInformation("Pipeline '{PipelineId}' completed successfully.", pipeline.Id);
        return new PipelineExecutionResult(pipeline, current, context);
    }

    private IPipelineStep GetStep(
        PipelineDefinition pipeline,
        PipelineStepDefinition stepDefinition,
        PipelineContext context)
    {
        if (!registry.TryGet(stepDefinition.StepId, out var step))
        {
            throw new PipelineExecutionException(
                $"Pipeline '{pipeline.Id}' step '{stepDefinition.Id}' references unknown step id '{stepDefinition.StepId}'.",
                context);
        }

        return step;
    }
}
