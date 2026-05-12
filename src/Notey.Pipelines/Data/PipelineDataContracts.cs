namespace Notey.Pipelines.Data;

public static class PipelineDataContracts
{
    private static readonly IReadOnlyDictionary<PipelineDataType, Type> TypeMap =
        new Dictionary<PipelineDataType, Type>
        {
            [PipelineDataType.ImageData] = typeof(ImageData),
            [PipelineDataType.TextData] = typeof(TextData),
            [PipelineDataType.OcrTextData] = typeof(OcrTextData),
            [PipelineDataType.StructuredNoteData] = typeof(StructuredNoteData),
            [PipelineDataType.MarkdownContent] = typeof(MarkdownContent),
        };

    public static bool TryGetClrType(PipelineDataType dataType, out Type clrType)
    {
        return TypeMap.TryGetValue(dataType, out clrType!);
    }

    public static bool TryGetDataType(Type clrType, out PipelineDataType dataType)
    {
        ArgumentNullException.ThrowIfNull(clrType);

        foreach (var pair in TypeMap)
        {
            if (pair.Value == clrType)
            {
                dataType = pair.Key;
                return true;
            }
        }

        dataType = PipelineDataType.Unknown;
        return false;
    }
}
