namespace Notey.PipelineSteps;

public static class PipelineStepContextKeys
{
    public const string OcrLanguage = "ocr.language";
    public const string OcrConfidence = "ocr.confidence";
    public const string OcrSourceImagePath = "ocr.source_image_path";
    public const string OcrWordCount = "ocr.word_count";
    public const string AiProviderId = "ai.provider_id";
    public const string AiModelName = "ai.model_name";
    public const string AiRawOutput = "ai.raw_output";
    public const string TeamsParticipantConfidenceThreshold = "teams.participant_confidence_threshold";
    public const string TeamsSuggestedParticipants = "teams.suggested_participants";
}
