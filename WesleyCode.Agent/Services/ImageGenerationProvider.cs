using System.ClientModel;
using System.ClientModel.Primitives;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Services;

internal sealed class ImageGenerationProvider : AIContextProvider
{
    private readonly IOptions<WorkingOptions> _workingOptions;
    private readonly IOptions<ImageClientOptions> _imageOptions;

    public ImageGenerationProvider(IOptions<WorkingOptions> workingOptions, IOptions<ImageClientOptions> imageOptions)
    {
        this._workingOptions = workingOptions;
        this._imageOptions = imageOptions;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        if (
            string.IsNullOrWhiteSpace(_imageOptions.Value.ApiKey)
            || string.IsNullOrWhiteSpace(_imageOptions.Value.BaseUrl)
            || string.IsNullOrWhiteSpace(_imageOptions.Value.ModelId)
        )
        {
            return base.ProvideAIContextAsync(context, cancellationToken);
        }

        return ValueTask.FromResult(
            new AIContext
            {
                Instructions = """
                ## Image Generation
                当用户要求创建图片时，使用 `image_generate` 生成 PNG 图片并保存到当前工作区。
                文件路径必须是相对于工作区根目录的路径；除非用户明确要求，否则不要覆盖已有文件。
                """,
                Tools =
                [
                    AIFunctionFactory.Create(
                        GenerateImageAsync,
                        new AIFunctionFactoryOptions { Name = "image_generate", Description = "根据文本提示生成 PNG 图片并保存到工作区。" }
                    ),
                ],
            }
        );
        async IAsyncEnumerable<string> GenerateImageAsync(
            [Description("图片尺寸-宽")] int width,
            [Description("图片尺寸-高")] int height,
            [Description("图片生成的数量")] int count,
            [Description("图片保存位置的相对路径")] string path,
            [Description("图片内容的详细文字描述")] string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            var imageGenerator = new OpenAIClient(
                new ApiKeyCredential(_imageOptions.Value.ApiKey),
                new OpenAIClientOptions() { Endpoint = new Uri(_imageOptions.Value.BaseUrl) }
            )
                .GetImageClient(_imageOptions.Value.ModelId)
                .AsIImageGenerator();
            var response = await imageGenerator.GenerateAsync(
                new ImageGenerationRequest(prompt),
                new AdditionalImageGenerationOptions
                {
                    Count = count,
                    ImageSize = new System.Drawing.Size(width, height),
                    ResponseFormat = ImageGenerationResponseFormat.Data,
                    AdditionalProperties = new AdditionalPropertiesDictionary { ["watermark"] = false },
                }
            );
            foreach (var content in response.Contents)
            {
                if (content is DataContent dataContent)
                {
                    var targetImage = Path.Combine(_workingOptions.Value.BasePath, path);
                    await using var file = new FileStream(targetImage, FileMode.Create, FileAccess.Write, FileShare.None);
                    await file.WriteAsync(dataContent.Data);
                    yield return $"图片生成成功,图片地址: {targetImage}";
                }
            }
        }
    }

    private class AdditionalImageGenerationOptions : ImageGenerationOptions
    {
        public AdditionalImageGenerationOptions()
        {
            RawRepresentationFactory = g => new CustomImageGenerationOptions() { AdditionalProperties = AdditionalProperties };
        }
    }

    private class CustomImageGenerationOptions : OpenAI.Images.ImageGenerationOptions
    {
        public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

        protected override void JsonModelWriteCore(Utf8JsonWriter writer, ModelReaderWriterOptions options)
        {
            base.JsonModelWriteCore(writer, options);

            if (AdditionalProperties is not null)
            {
                foreach (var (key, value) in AdditionalProperties)
                {
                    writer.WritePropertyName(key);
                    if (value is null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        JsonSerializer.Serialize(writer, value, value.GetType());
                    }
                }
            }
        }
    }
}
